// @vitest-environment jsdom
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import type { Root } from 'react-dom/client';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type { MatchSnapshot, Session } from './types.ts';

const get = vi.fn<(path: string, token?: string) => Promise<unknown>>();
const post = vi.fn<(path: string, body?: unknown, token?: string) => Promise<unknown>>();

// Only the two functions that talk to the server are replaced. Everything else in the module - the
// storage helpers, the error shape the screen catches on - is the real thing, because the screen is
// what is under test.
vi.mock('./lib/api.ts', async (importActual) => ({
  ...await importActual<typeof import('./lib/api.ts')>(),
  get: (path: string, token?: string) => get(path, token),
  post: (path: string, body?: unknown, token?: string) => post(path, body, token),
}));

const engines = { starGrunt: false, dirtside: false };
const readFeatures = vi.fn<() => Promise<typeof engines>>(() => Promise.resolve(engines));

vi.mock('./lib/starGruntApi.ts', () => ({
  readFeatures: () => readFeatures(),
}));

// The hub is a live connection to a server that is not there. The screen only needs it to build,
// start and accept a subscription. The `MatchSnapshotChanged` handler is kept so a test can deliver
// a push the way the server would; the rest is inert.
const hub: {
  notify?: (matchId: string, version: number, reason: string) => void;
  reconnected?: (connectionId?: string) => void;
  invoke?: (method: string, ...args: unknown[]) => Promise<unknown>;
} = {};

vi.mock('@microsoft/signalr', () => {
  class HubConnectionBuilder {
    withUrl() { return this; }
    withAutomaticReconnect() { return this; }
    build() {
      return {
        on: (event: string, handler: (matchId: string, version: number, reason: string) => void) => {
          if (event === 'MatchSnapshotChanged') {
            hub.notify = handler;
          }
        },
        onreconnecting: () => undefined,
        onclose: () => undefined,
        onreconnected: (handler: (connectionId?: string) => void) => {
          hub.reconnected = handler;
        },
        start: () => Promise.resolve(),
        stop: () => Promise.resolve(),
        invoke: (method: string, ...args: unknown[]) => (hub.invoke ? hub.invoke(method, ...args) : Promise.resolve()),
      };
    }
  }

  return { HubConnectionBuilder };
});

// Every test imports `main.tsx` afresh, and the import is what mounts the app. Nothing used to take
// that app down again: clearing the body detached its DOM but left the React root alive, still
// holding its hub connection and still answering every promise the test had started. Each test
// added another, and a test that timed out left one that went on writing its session to storage
// after the next test's `localStorage.clear()` - so the next screen restored a match instead of
// offering Create Match, and one slow test failed every test after it. The roots are caught as they
// are made, so each test can unmount its own.
const mountedRoots: Root[] = [];
vi.mock('react-dom/client', async (importActual) => {
  const actual = await importActual<typeof import('react-dom/client')>();
  return {
    ...actual,
    createRoot: (...args: Parameters<typeof actual.createRoot>) => {
      const created = actual.createRoot(...args);
      mountedRoots.push(created);
      return created;
    },
  };
});

// The buttons these tests press are found by their text, not by role. A role query computes the
// accessible name of every element on the page, and on the match board that is thirty times the
// cost of a text query (about 35 ms against 1 ms here) - and a `find` reruns it on every DOM change
// until it matches. Under load that alone ran the first two tests past their five seconds. What a
// button is called is still what is checked, and the one role that is the point of a test - the
// alert beside the room-code field - is still asked for by role. A role query that finds nothing is
// the worst case, since it names every element before giving up: about 300 ms each on this board.
function getButton(name: string) {
  return screen.getByText(name, { selector: 'button' });
}

function findButton(name: string) {
  return screen.findByText(name, { selector: 'button' });
}

function unmountApps() {
  act(() => {
    for (const root of mountedRoots.splice(0)) {
      root.unmount();
    }
  });
}

// The first import of `main.tsx` in a worker transforms its whole module graph, and the first
// render and first role query warm up React and the accessibility tree code. On a busy machine that
// was most of the first two tests' five seconds each - a cost of the file, not of anything they
// check - so they timed out while the tests after them, which found everything warm, passed. It is
// paid once here, under a budget of its own, by mounting the start screen and taking it down again.
beforeAll(async () => {
  localStorage.clear();
  document.body.innerHTML = '<div id="app"></div>';
  await import('./main.tsx');
  await screen.findByText('Create Match', { selector: 'button' }, { timeout: 60_000 });
  unmountApps();
  vi.resetModules();
  document.body.innerHTML = '';
}, 120_000);

// What every test starts from, whatever the test before it did: engines readable, and a hub with
// nothing left over - a rejoin the last test held open would otherwise hold this test's link too.
beforeEach(() => {
  readFeatures.mockReset();
  readFeatures.mockImplementation(() => Promise.resolve(engines));
  hub.notify = undefined;
  hub.reconnected = undefined;
  hub.invoke = undefined;
});

afterEach(unmountApps);

function session(room: string): Session & { matchId: string; joinCode: string } {
  return {
    matchId: `match-${room}`,
    participantId: `participant-${room}`,
    participantToken: `token-${room}`,
    joinCode: room,
  };
}

function snapshot(room: string, version: number): MatchSnapshot {
  return {
    matchId: `match-${room}`,
    joinCode: room,
    name: `${room} Match`,
    phase: 'FleetSetup',
    turnNumber: 1,
    rulesProfileKey: 'custom',
    tableWidth: 72,
    tableDepth: 48,
    participants: [{
      id: `participant-${room}`,
      displayName: 'Admiral',
      role: 'Owner',
      isReady: false,
      isConnected: true,
    }] as MatchSnapshot['participants'],
    fleets: [],
    ships: [],
    orderStatuses: [],
    revealedOrders: [],
    movementResults: [],
    firingResults: [],
    ordnanceMarkers: [],
    matchLog: [],
    version,
    pointsLimit: 0,
  };
}

describe('leaving one room and joining the next', () => {
  beforeEach(() => {
    localStorage.clear();
    get.mockReset();
    post.mockReset();
    vi.resetModules();
    document.body.innerHTML = '<div id="app"></div>';
  });

  afterEach(() => {
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
  });

  it('shows the room this device just joined, not the one it left', async () => {
    // The first room's snapshot is held open, so the test decides when it lands.
    let deliverFirstSnapshot: (value: MatchSnapshot) => void = () => undefined;
    const firstSnapshot = new Promise<MatchSnapshot>((resolve) => {
      deliverFirstSnapshot = resolve;
    });

    post.mockImplementation((path) => Promise.resolve(
      path === '/api/matches' ? session('ALPHA') : session('BRAVO')));
    get.mockImplementation((path) => path.startsWith('/api/matches/match-ALPHA')
      ? firstSnapshot
      : Promise.resolve(snapshot('BRAVO', 1)));

    await import('./main.tsx');

    fireEvent.click(await findButton('Create Match'));
    await screen.findByText('Created room ALPHA.');

    // Leave before that first snapshot has arrived. This is the ordinary case, not a contrived one:
    // the screen refetches the whole snapshot on every hub notification and never cancels the
    // request, so any of them can still be in the air when the player taps Leave.
    vi.stubGlobal('confirm', () => true);
    fireEvent.click(getButton('Leave Device Session'));
    await findButton('Join Match');

    // And now it lands, on a screen that has already let go of that match. Version 9 is not a large
    // number - it is four or five actions into a game - but it is larger than the version any fresh
    // room starts at, which is all it takes.
    deliverFirstSnapshot(snapshot('ALPHA', 9));

    fireEvent.change(screen.getByLabelText('Room code'), { target: { value: 'BRAVO' } });
    fireEvent.click(getButton('Join Match'));
    await screen.findByText('Joined room BRAVO.');

    // Snapshot versions are per match and every room starts again at one, so a high water mark left
    // behind by the last match makes the new room's first snapshot look stale and it is dropped on
    // the floor. The join succeeds, the server returns the right board, and the screen goes on
    // showing the previous match's room code - until some later room happens to out-number it.
    await waitFor(() => {
      expect(screen.getByText('BRAVO')).toBeTruthy();
    });
    expect(screen.queryByText('ALPHA')).toBeNull();
  });
});

describe('tapping a setup control twice', () => {
  beforeEach(() => {
    localStorage.clear();
    get.mockReset();
    post.mockReset();
    vi.resetModules();
    document.body.innerHTML = '<div id="app"></div>';
  });

  afterEach(() => {
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
  });

  it('creates one fleet, not two', async () => {
    // The create is held open, which is what a slow link looks like from the table: the player taps,
    // nothing on screen changes, and they tap again.
    let finishCreate: (value: MatchSnapshot) => void = () => undefined;
    const pendingCreate = new Promise<MatchSnapshot>((resolve) => {
      finishCreate = resolve;
    });

    // A match that already has one fleet, which is what puts New Fleet on screen.
    const withAFleet = (version: number): MatchSnapshot => ({
      ...snapshot('ALPHA', version),
      fleets: [{
        id: 'fleet-1',
        ownerParticipantId: 'participant-ALPHA',
        name: 'Blue Watch',
        fleetColor: '#f5c766',
      }],
    });

    post.mockImplementation((path) => path === '/api/matches'
      ? Promise.resolve(session('ALPHA'))
      : pendingCreate);
    get.mockImplementation(() => Promise.resolve(withAFleet(1)));

    await import('./main.tsx');

    fireEvent.click(await findButton('Create Match'));
    await screen.findByText('Created room ALPHA.');

    fireEvent.click(await findButton('New Fleet'));
    const createFleet = await findButton('Create Fleet');
    fireEvent.click(createFleet);
    fireEvent.click(createFleet);

    // Two taps used to be two fleets, and the second one is not obviously a mistake afterwards -
    // it is an empty fleet with the same name that somebody has to notice and delete.
    const fleetPosts = post.mock.calls.filter(([path]) => path.endsWith('/fleets'));
    expect(fleetPosts).toHaveLength(1);

    finishCreate(snapshot('ALPHA', 2));
    await waitFor(() => {
      expect((getButton('Create Fleet') as HTMLButtonElement).disabled).toBe(false);
    });
  });
});

/**
 * The `MatchSnapshotChanged` handler the screen registered, once it has.
 *
 * Waiting for it is the reached-the-subject check: a test that pushed into `hub.notify` before the
 * screen had subscribed would be calling nothing at all and would then assert against a screen that
 * had never been told anything.
 */
async function subscribedHandler() {
  await waitFor(() => expect(hub.notify).toBeTypeOf('function'));
  const notify = hub.notify;
  if (!notify) {
    throw new Error('the screen never subscribed to MatchSnapshotChanged');
  }

  return notify;
}

describe('a background refresh that fails', () => {
  beforeEach(() => {
    localStorage.clear();
    get.mockReset();
    post.mockReset();
    hub.notify = undefined;
    vi.resetModules();
    document.body.innerHTML = '<div id="app"></div>';
  });

  afterEach(() => {
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
  });

  // The success path of background traffic was routed to the activity line for exactly this reason,
  // with the reason written beside it - and the failure path still went to the message line. So at a
  // table: your shot is refused with a specific reason, the opponent moves a ship half a second
  // later, the push-triggered GET times out on venue wifi, and the reason your shot failed is
  // replaced by "The ForceSignal server did not answer". The refusal was the only copy.
  it('does not overwrite the message the player is reading', async () => {
    post.mockResolvedValue(session('ALPHA'));
    get.mockResolvedValue(snapshot('ALPHA', 1));

    await import('./main.tsx');

    fireEvent.click(await findButton('Create Match'));
    const standingMessage = await screen.findByText('Created room ALPHA.');

    // Reached-the-subject: the screen really subscribed, so the push below is the real handler
    // rather than a test talking to itself.
    const notify = await subscribedHandler();

    get.mockRejectedValue(new Error('The ForceSignal server did not answer within 15 seconds.'));
    notify('match-ALPHA', 2, 'ShipMoved');

    // The failure is reported - beside the connection state, where background traffic already goes.
    await screen.findByText(/Could not refresh:.*did not answer/);

    // And the message the player was reading is still on screen.
    expect(standingMessage.textContent).toBe('Created room ALPHA.');
  });

  // The control that must still happen. An expired session is not a report about traffic, it is
  // every control on the screen having just stopped working, so it takes the message line whoever
  // asked for the request that found out.
  it('still takes the message line when the session has expired', async () => {
    post.mockResolvedValue(session('ALPHA'));
    get.mockResolvedValue(snapshot('ALPHA', 1));

    const { ApiRequestError } = await import('./lib/api.ts');
    await import('./main.tsx');

    fireEvent.click(await findButton('Create Match'));
    await screen.findByText('Created room ALPHA.');
    const notify = await subscribedHandler();

    get.mockRejectedValue(new ApiRequestError('Match not found.', 404));
    notify('match-ALPHA', 2, 'ShipMoved');

    await screen.findByText('Match session expired. Create or join a room again.');
  });
});

describe('the realtime link chip', () => {
  beforeEach(() => {
    localStorage.clear();
    get.mockReset();
    post.mockReset();
    hub.notify = undefined;
    hub.reconnected = undefined;
    hub.invoke = undefined;
    vi.resetModules();
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    vi.spyOn(console, 'info').mockImplementation(() => undefined);
    document.body.innerHTML = '<div id="app"></div>';
  });

  afterEach(() => {
    document.body.innerHTML = '';
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  // The chip said "Link live" the moment the transport came back, before the server had put the
  // new connection into the match's group. When that rejoin was refused the chip went on saying
  // live for a link that would never deliver another push - the board quietly stopped following
  // the opponent while telling the player it was.
  it('does not say live after a reconnect whose rejoin was refused', async () => {
    post.mockResolvedValue(session('ALPHA'));
    get.mockResolvedValue(snapshot('ALPHA', 1));

    await import('./main.tsx');

    fireEvent.click(await findButton('Create Match'));
    await screen.findByText('Link live');
    await waitFor(() => expect(hub.reconnected).toBeDefined());

    hub.invoke = () => Promise.reject(new Error('A valid participant token is required to follow this match.'));
    hub.reconnected?.('connection-2');

    await screen.findByText(/Could not rejoin the live feed/);
    expect(screen.queryByText('Link live')).toBeNull();
    expect(screen.getByText('Link lost')).toBeTruthy();
  });

  it('says live once the rejoin after a reconnect succeeds', async () => {
    post.mockResolvedValue(session('ALPHA'));
    get.mockResolvedValue(snapshot('ALPHA', 1));

    await import('./main.tsx');

    fireEvent.click(await findButton('Create Match'));
    await screen.findByText('Link live');
    await waitFor(() => expect(hub.reconnected).toBeDefined());

    let finishRejoin: () => void = () => undefined;
    hub.invoke = () => new Promise((resolve) => {
      finishRejoin = () => resolve(undefined);
    });
    hub.reconnected?.('connection-2');

    // Until the server answers the rejoin, the link is not live yet.
    await screen.findByText('Reconnecting');
    finishRejoin();
    await screen.findByText('Link live');
  });
});

describe('the next-step banner', () => {
  beforeEach(() => {
    localStorage.clear();
    get.mockReset();
    post.mockReset();
    hub.notify = undefined;
    vi.resetModules();
    document.body.innerHTML = '<div id="app"></div>';
  });

  afterEach(() => {
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  // The refusal used to land in a muted line at the top of the board while the commands sit at the
  // bottom of the sidebar, styled exactly like a success - so a refusal looked like nothing at all.
  it('shows a refused command as an alert in the banner, not as a quiet success', async () => {
    post.mockImplementation((path) => (path.endsWith('/table')
      ? Promise.reject(new Error('Only the owner can change the table.'))
      : Promise.resolve(session('ALPHA'))));
    get.mockResolvedValue(snapshot('ALPHA', 1));

    await import('./main.tsx');
    fireEvent.click(await findButton('Create Match'));
    await screen.findByText('Created room ALPHA.');

    // Table settings live in the setup section, which opens once the match is known to be in setup.
    await waitFor(() => expect(screen.getByTestId('next-step').textContent).toContain('Fleet setup'));
    fireEvent.click(await findButton('Set Table'));
    const banner = await screen.findByTestId('next-step');
    const alert = await waitFor(() => {
      const found = banner.querySelector('[role="alert"]');
      expect(found?.textContent).toContain('Only the owner can change the table.');
      return found;
    });
    expect(alert?.className).toContain('error');
  });

  it('says what this player should do next and who the table is waiting on', async () => {
    post.mockResolvedValue(session('ALPHA'));
    const ordering: MatchSnapshot = {
      ...snapshot('ALPHA', 1),
      phase: 'OrderEntry',
      participants: [
        { id: 'participant-ALPHA', displayName: 'Blue', role: 'Owner', isReady: true, isConnected: true, ordersComplete: true },
        { id: 'participant-RED', displayName: 'Red', role: 'Player', isReady: true, isConnected: true, ordersComplete: false },
      ],
    };
    get.mockResolvedValue(ordering);

    await import('./main.tsx');
    fireEvent.click(await findButton('Create Match'));

    const banner = await screen.findByTestId('next-step');
    await waitFor(() => {
      expect(banner.textContent).toContain('Order entry · Turn 1');
      expect(banner.textContent).toContain('Your orders are in.');
      expect(banner.textContent).toContain('Waiting for Red to finish plotting.');
    });
  });

  it('clears a message from an earlier phase once the match moves on', async () => {
    post.mockResolvedValue(session('ALPHA'));
    get.mockResolvedValue(snapshot('ALPHA', 1));

    await import('./main.tsx');
    fireEvent.click(await findButton('Create Match'));
    await screen.findByText('Created room ALPHA.');
    const notify = await subscribedHandler();
    // The setup snapshot has to have landed first, or the screen never sees a phase change at all -
    // only a first snapshot that is already in order entry.
    await waitFor(() => expect(screen.getByTestId('next-step').textContent).toContain('Fleet setup'));

    // Time passes at the table before the phase turns over.
    const later = Date.now() + 60_000;
    vi.spyOn(Date, 'now').mockReturnValue(later);
    get.mockResolvedValue({ ...snapshot('ALPHA', 2), phase: 'OrderEntry' });
    notify('match-ALPHA', 2, 'PhaseChanged');

    await waitFor(() => expect(screen.getByTestId('next-step').textContent).toContain('Order entry'));
    // The clear is a state update from an effect, so it lands a render after the phase does.
    await waitFor(() => expect(screen.queryByText('Created room ALPHA.')).toBeNull());
  });

  // A fresh match used to offer Ready, the checklist said "Clear to proceed", and the server then
  // refused it off-screen. Now the button says why it cannot be pressed.
  it('does not offer Ready on a fresh match, and says why', async () => {
    post.mockResolvedValue(session('ALPHA'));
    get.mockResolvedValue(snapshot('ALPHA', 1));

    await import('./main.tsx');
    fireEvent.click(await findButton('Create Match'));

    const ready = await findButton('Ready');
    expect((ready as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByText('Needs a rules profile first.')).toBeTruthy();
    // And the commands that mean nothing yet are not on offer at all.
    expect(screen.queryByText('Lock Fleet Orders', { selector: 'button' })).toBeNull();
    expect(screen.queryByText('Reveal Fleet Orders', { selector: 'button' })).toBeNull();
    expect(screen.queryByText('Advance Turn', { selector: 'button' })).toBeNull();
  });
});

describe('joining by invite link', () => {
  beforeEach(() => {
    localStorage.clear();
    get.mockReset();
    post.mockReset();
    vi.resetModules();
    document.body.innerHTML = '<div id="app"></div>';
  });

  afterEach(() => {
    document.body.innerHTML = '';
    window.history.replaceState(null, '', '/');
    vi.unstubAllGlobals();
  });

  it('arrives with the room code already filled in', async () => {
    window.history.replaceState(null, '', '/?join=pivot-violet-seven-indigo');
    await import('./main.tsx');

    const code = await screen.findByLabelText('Room code');
    expect((code as HTMLInputElement).value).toBe('PIVOT-VIOLET-SEVEN-INDIGO');
  });

  it('says what to check when the code is wrong, as an alert beside the field', async () => {
    const { ApiRequestError } = await import('./lib/api.ts');
    post.mockRejectedValue(new ApiRequestError('Match was not found.', 404));
    await import('./main.tsx');

    fireEvent.change(await screen.findByLabelText('Room code'), { target: { value: 'PIVOT-VIOLET' } });
    fireEvent.click(getButton('Join Match'));

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('There is no room PIVOT-VIOLET');
    expect(alert.textContent).toContain('four words');
  });
});

describe('a server that will not say which engines it has', () => {
  beforeEach(() => {
    localStorage.clear();
    get.mockReset();
    post.mockReset();
    vi.resetModules();
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    vi.spyOn(console, 'info').mockImplementation(() => undefined);
    document.body.innerHTML = '<div id="app"></div>';
  });

  afterEach(() => {
    document.body.innerHTML = '';
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  // A server that was down at page load used to read as one with StarGrunt and Dirtside switched
  // off, silently and for the life of the tab - a configuration the server does not have.
  it('says the engines are hidden because it could not ask, and can ask again', async () => {
    readFeatures.mockRejectedValueOnce(new Error('The ForceSignal server did not answer within 15 seconds.'));
    readFeatures.mockResolvedValue({ starGrunt: false, dirtside: true });

    await import('./main.tsx');

    const note = await screen.findByText(/did not say which other games it offers/);
    expect(note.textContent).toContain('StarGrunt and Dirtside are hidden');
    expect(screen.queryByText('Dirtside II', { selector: 'button' })).toBeNull();

    fireEvent.click(getButton('Check Again'));

    await findButton('Dirtside II');
    expect(screen.queryByText(/did not say which other games it offers/)).toBeNull();
  });

  it('asks again by itself once the realtime link is up', async () => {
    readFeatures.mockRejectedValueOnce(new Error('Failed to fetch'));
    readFeatures.mockResolvedValue({ starGrunt: true, dirtside: false });
    post.mockResolvedValue(session('ALPHA'));
    get.mockResolvedValue(snapshot('ALPHA', 1));

    await import('./main.tsx');
    await screen.findByText(/did not say which other games it offers/);

    // The link coming up is the first sign the server is back, and nobody has pressed anything.
    fireEvent.click(await findButton('Create Match'));
    await screen.findByText('Link live');

    await findButton('StarGrunt II');
    expect(readFeatures).toHaveBeenCalledTimes(2);
    expect(screen.queryByText(/did not say which other games it offers/)).toBeNull();
  });

  // The control: a read that worked is not repeated on every reconnect.
  it('does not ask again once it has an answer', async () => {
    post.mockResolvedValue(session('ALPHA'));
    get.mockResolvedValue(snapshot('ALPHA', 1));

    await import('./main.tsx');
    fireEvent.click(await findButton('Create Match'));
    await screen.findByText('Link live');

    expect(readFeatures).toHaveBeenCalledTimes(1);
    expect(screen.queryByText(/did not say which other games it offers/)).toBeNull();
  });
});

describe('the end of a battle (playtest M1)', () => {
  beforeEach(() => {
    localStorage.clear();
    get.mockReset();
    post.mockReset();
    vi.resetModules();
    document.body.innerHTML = '<div id="app"></div>';
  });

  afterEach(() => {
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
  });

  function inPlay(version: number, patch: Partial<MatchSnapshot> = {}): MatchSnapshot {
    const base = snapshot('ALPHA', version);
    return {
      ...base,
      phase: 'OrderEntry',
      participants: [
        ...base.participants,
        { id: 'participant-red', displayName: 'Red', role: 'Player', isReady: true, isConnected: true } as MatchSnapshot['participants'][number],
      ],
      fleets: [
        { id: 'fleet-blue', ownerParticipantId: 'participant-ALPHA', name: 'Blue', fleetColor: '#47f1ff' },
        { id: 'fleet-red', ownerParticipantId: 'participant-red', name: 'Red', fleetColor: '#ff4d6d' },
      ] as MatchSnapshot['fleets'],
      ships: [
        { ...snapshot('x', 1).ships[0], id: 'ship-blue', fleetId: 'fleet-blue', name: 'Valiant', isDestroyed: false },
        { id: 'ship-red', fleetId: 'fleet-red', name: 'Mark', isDestroyed: true },
      ].map((ship) => ({
        className: 'Cruiser', thrustRating: 4, currentVelocity: 0, currentCourse: 12, positionX: 20, positionY: 20,
        hullMax: 10, hullDamage: ship.isDestroyed ? 10 : 0, armorMax: 0, armorDamage: 0, fireControlMax: 1, fireControlDamage: 0,
        pointDefenseSystems: 0, fighterBays: 0, fighterBayDamage: 0, damageControlParties: 0, driveDamage: 0, weaponDamage: 0,
        screenRating: 0, screenDamage: 0, weapons: [], hullRows: [], hullRowsCompleted: 0, iconKey: 'cruiser',
        fighterEnduranceMax: 0, fighterEnduranceUsed: 0, fighterMaxRange: 0, pointsValue: 0, effectiveScreens: 0,
        workingFireControl: 1, ...ship,
      })) as MatchSnapshot['ships'],
      ...patch,
    };
  }

  it('offers the host End Battle, and shows both screens the result with the record to keep', async () => {
    const over = inPlay(3, { battleOver: { reason: 'EndedByHost', message: 'Admiral ended the battle.' } });
    post.mockImplementation((path) => Promise.resolve(path === '/api/matches' ? session('ALPHA') : over));
    get.mockResolvedValue(inPlay(2));

    await import('./main.tsx');
    fireEvent.click(await findButton('Create Match'));
    await screen.findByText('Created room ALPHA.');

    vi.stubGlobal('confirm', () => true);
    fireEvent.click(await findButton('End Battle'));

    await waitFor(() => expect(post).toHaveBeenCalledWith('/api/matches/match-ALPHA/end', { participantToken: 'token-ALPHA' }, 'token-ALPHA'));
    const panel = await screen.findByLabelText('Battle over');
    expect(panel.textContent).toContain('Admiral ended the battle.');
    expect(panel.textContent).toContain('Admiral: 1 of 1 ship still afloat');
    expect(panel.textContent).toContain('Red: 0 of 1 ship still afloat');
    expect(screen.getByText('Export Battle Log', { selector: 'button' })).toBeTruthy();
    expect(screen.queryByText('Lock Fleet Orders', { selector: 'button' })).toBeNull();
    expect(screen.queryByText('End Battle', { selector: 'button' })).toBeNull();
    expect(screen.getAllByText('Battle over').length).toBeGreaterThan(0);
  });

  it('reports an opponent ship out of its own numbers, and drops order chatter once orders are done (playtest m2, m3)', async () => {
    post.mockResolvedValue(session('ALPHA'));
    get.mockResolvedValue(inPlay(2, { phase: 'Firing' }));

    await import('./main.tsx');
    fireEvent.click(await findButton('Create Match'));
    await screen.findByText('Created room ALPHA.');

    const report = (await screen.findByText('Damage report')).parentElement as HTMLElement;
    expect(report.textContent).toContain('Firecon0/1');
    expect(report.textContent).toContain('Weapons0/0');
    expect(report.textContent).not.toContain('/6');
    expect(report.textContent).not.toContain('/12');
    expect(report.textContent).not.toContain('remains hidden until reveal');
    expect(screen.queryByText('Awaiting orders')).toBeNull();
  });

  it('marks a wreck out of action rather than awaiting orders', async () => {
    post.mockResolvedValue(session('ALPHA'));
    get.mockResolvedValue(inPlay(2));

    await import('./main.tsx');
    fireEvent.click(await findButton('Create Match'));
    await screen.findByText('Created room ALPHA.');

    expect(await screen.findByText('Out of action')).toBeTruthy();
  });
});

describe('the start screen and the keyboard (playtest m9)', () => {
  beforeEach(() => {
    localStorage.clear();
    get.mockReset();
    post.mockReset();
    vi.resetModules();
    document.body.innerHTML = '<div id="app"></div>';
  });

  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('joins on Enter in the room code field', async () => {
    post.mockImplementation((path) => Promise.resolve(path === '/api/matches' ? session('ALPHA') : session('BRAVO')));
    get.mockImplementation((path) => Promise.resolve(snapshot(path.includes('ALPHA') ? 'ALPHA' : 'BRAVO', 1)));
    await import('./main.tsx');

    const code = await screen.findByLabelText('Room code');
    fireEvent.change(code, { target: { value: 'BRAVO' } });
    fireEvent.submit(code.closest('form') as HTMLFormElement);
    await waitFor(() => expect(post).toHaveBeenCalledWith('/api/matches/join', expect.objectContaining({ joinCode: 'BRAVO' }), undefined));
  });
});

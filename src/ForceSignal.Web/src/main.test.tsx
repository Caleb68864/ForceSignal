// @vitest-environment jsdom
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
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

vi.mock('./lib/starGruntApi.ts', () => ({
  readFeatures: () => Promise.resolve({ starGrunt: false, dirtside: false }),
}));

// The hub is a live connection to a server that is not there. The screen only needs it to build,
// start and accept a subscription. The `MatchSnapshotChanged` handler is kept so a test can deliver
// a push the way the server would; the rest is inert.
const hub: { notify?: (matchId: string, version: number, reason: string) => void } = {};

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
        onreconnected: () => undefined,
        start: () => Promise.resolve(),
        stop: () => Promise.resolve(),
        invoke: () => Promise.resolve(),
      };
    }
  }

  return { HubConnectionBuilder };
});

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

    fireEvent.click(await screen.findByRole('button', { name: 'Create Match' }));
    await screen.findByText('Created room ALPHA.');

    // Leave before that first snapshot has arrived. This is the ordinary case, not a contrived one:
    // the screen refetches the whole snapshot on every hub notification and never cancels the
    // request, so any of them can still be in the air when the player taps Leave.
    vi.stubGlobal('confirm', () => true);
    fireEvent.click(screen.getByRole('button', { name: 'Leave Device Session' }));
    await screen.findByRole('button', { name: 'Join Match' });

    // And now it lands, on a screen that has already let go of that match. Version 9 is not a large
    // number - it is four or five actions into a game - but it is larger than the version any fresh
    // room starts at, which is all it takes.
    deliverFirstSnapshot(snapshot('ALPHA', 9));

    fireEvent.change(screen.getByLabelText('Room code'), { target: { value: 'BRAVO' } });
    fireEvent.click(screen.getByRole('button', { name: 'Join Match' }));
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

    fireEvent.click(await screen.findByRole('button', { name: 'Create Match' }));
    await screen.findByText('Created room ALPHA.');

    fireEvent.click(await screen.findByRole('button', { name: 'New Fleet' }));
    const createFleet = await screen.findByRole('button', { name: 'Create Fleet' });
    fireEvent.click(createFleet);
    fireEvent.click(createFleet);

    // Two taps used to be two fleets, and the second one is not obviously a mistake afterwards -
    // it is an empty fleet with the same name that somebody has to notice and delete.
    const fleetPosts = post.mock.calls.filter(([path]) => path.endsWith('/fleets'));
    expect(fleetPosts).toHaveLength(1);

    finishCreate(snapshot('ALPHA', 2));
    await waitFor(() => {
      expect((screen.getByRole('button', { name: 'Create Fleet' }) as HTMLButtonElement).disabled).toBe(false);
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

    fireEvent.click(await screen.findByRole('button', { name: 'Create Match' }));
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

    fireEvent.click(await screen.findByRole('button', { name: 'Create Match' }));
    await screen.findByText('Created room ALPHA.');
    const notify = await subscribedHandler();

    get.mockRejectedValue(new ApiRequestError('Match not found.', 404));
    notify('match-ALPHA', 2, 'ShipMoved');

    await screen.findByText('Match session expired. Create or join a room again.');
  });
});

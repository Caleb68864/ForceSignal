// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { dirtsideGameKey } from '../../constants.ts';
import { ApiRequestError } from '../../lib/api.ts';
import type { DirtsideElementState, DirtsideGameCreated, DirtsidePlatoonState, DirtsideSnapshot, GameHandle } from '../../types.ts';
import { DirtsideView } from './DirtsideView.tsx';

const readGame = vi.fn<(game: GameHandle) => Promise<DirtsideSnapshot>>();
const beginTurn = vi.fn<(game: GameHandle) => Promise<DirtsideSnapshot>>();
const endTurn = vi.fn<(game: GameHandle) => Promise<DirtsideSnapshot>>();
const createGame = vi.fn<(name: string, chitPot?: unknown, profile?: unknown) => Promise<DirtsideGameCreated>>();
// The live link to other devices is covered in groundLive.test.ts. Here it must not reach for a
// server: in jsdom a real SignalR start fails and logs after the test has ended.
vi.mock('../../lib/groundLive.ts', () => ({ useGroundLive: () => 'offline' }));
vi.mock('../../lib/dirtsideApi.ts', () => ({
  createGame: (name: string, chitPot?: unknown, profile?: unknown) => createGame(name, chitPot, profile),
  readGame: (game: GameHandle) => readGame(game),
  beginTurn: (game: GameHandle) => beginTurn(game),
  endTurn: (game: GameHandle) => endTurn(game),
}));

const handle: GameHandle = { gameId: 'game-1', token: 'token-1' };
const snapshot: DirtsideSnapshot = {
  gameId: 'game-1',
  name: 'Ridge Line',
  turnNumber: 1,
  phase: 'NotStarted',
  sides: ['blue', 'red'],
  elementsStillToChoose: [],
  canEndActivation: false,
  units: [],
  log: [],
  version: 1,
};

describe('DirtsideView reopening', () => {
  beforeEach(() => {
    localStorage.clear();
    readGame.mockReset();
  });

  afterEach(cleanup);

  it('reopens the game this device stored', async () => {
    localStorage.setItem(dirtsideGameKey, JSON.stringify(handle));
    readGame.mockResolvedValue(snapshot);

    render(<DirtsideView />);

    expect(await screen.findByText('Reopened Ridge Line.')).toBeTruthy();
    expect(readGame).toHaveBeenCalledWith(handle);
    expect(screen.getByRole('heading', { name: 'Ridge Line' })).toBeTruthy();
  });

  // A game the server no longer has would otherwise fail every action; forgetting it is the only
  // useful answer.
  it('forgets a stored game the server no longer has', async () => {
    localStorage.setItem(dirtsideGameKey, JSON.stringify(handle));
    readGame.mockRejectedValue(new ApiRequestError('Game not found.', 404));

    render(<DirtsideView />);

    expect((await screen.findByRole('alert')).textContent).toContain('no longer available');
    await waitFor(() => expect(localStorage.getItem(dirtsideGameKey)).toBeNull());
    expect((screen.getByRole('button', { name: 'Start Game' }) as HTMLButtonElement).disabled).toBe(false);
  });

  it('offers a fresh start when nothing is stored', () => {
    render(<DirtsideView />);

    expect(readGame).not.toHaveBeenCalled();
    expect((screen.getByRole('button', { name: 'Start Game' }) as HTMLButtonElement).disabled).toBe(false);
  });

  // A transient failure used to leave exactly one live control on this screen, and it was the one
  // that throws the game away: the effect is keyed on `[game, snapshot]`, neither of which a 500
  // changes, so it ran once and never again while the Start button sat disabled reading
  // "Reopening last game...". The game is kept - it is almost certainly still on the server - and
  // a Try Again runs the reopen again.
  it('keeps a stored game after a transient failure and offers a retry that works', async () => {
    localStorage.setItem(dirtsideGameKey, JSON.stringify(handle));
    readGame.mockRejectedValueOnce(new ApiRequestError('The server did not answer.', 500));

    render(<DirtsideView />);

    expect((await screen.findByRole('alert')).textContent).toContain('has not been forgotten');

    // Not forgotten, and not pretending to still be loading.
    expect(localStorage.getItem(dirtsideGameKey)).not.toBeNull();
    expect(screen.getByRole('button', { name: 'Could not reopen last game' })).toBeTruthy();

    // The retry really runs the reopen again, and the screen arrives.
    readGame.mockResolvedValue(snapshot);
    screen.getByRole('button', { name: 'Try Again' }).click();

    expect(await screen.findByText('Reopened Ridge Line.')).toBeTruthy();
    expect(readGame).toHaveBeenCalledTimes(2);
    expect(screen.getByRole('heading', { name: 'Ridge Line' })).toBeTruthy();
  });

  // The control on the retry: a 404 is not transient, so it must still be forgotten rather than
  // offered a Try Again that can only fail. An over-broad fix here would keep a dead game forever.
  it('offers no retry for a game the server no longer has', async () => {
    localStorage.setItem(dirtsideGameKey, JSON.stringify(handle));
    readGame.mockRejectedValue(new ApiRequestError('Game not found.', 404));

    render(<DirtsideView />);

    expect((await screen.findByRole('alert')).textContent).toContain('no longer available');
    expect(screen.queryByRole('button', { name: 'Try Again' })).toBeNull();
  });
});

function element(extra: Partial<DirtsideElementState> = {}): DirtsideElementState {
  return {
    id: 'alpha-1',
    name: 'Alpha One',
    isDestroyed: false,
    isDamaged: false,
    isSystemsDown: false,
    movedOverHalf: false,
    areaDefenceSensorsLive: false,
    movement: 12,
    hasChosen: false,
    hasMoved: false,
    hasTakenCombatAction: false,
    hasStoodDown: false,
    weapons: ['Main Gun'],
    ...extra,
  };
}

function activating(elements: DirtsideElementState[]): DirtsideSnapshot {
  const alpha: DirtsidePlatoonState = {
    id: 'alpha',
    name: 'Alpha Troop',
    side: 'blue',
    kind: 'Armour',
    isCybertank: false,
    confidence: 'Confident',
    isUnderFire: false,
    isDisorganised: false,
    hasActivated: true,
    canActivate: false,
    elements,
  };

  return { ...snapshot, phase: 'Activating', activeSide: 'blue', activatingUnitId: 'alpha', units: [alpha] };
}

// The server halves a damaged element's movement, and this is the half a player reads. Before it was
// rendered, the only movement anywhere on the screen was the one off the record card, so a damaged
// vehicle silently kept advertising the distance it could cover when it was whole.
describe('DirtsideView element movement', () => {
  beforeEach(() => {
    localStorage.clear();
    readGame.mockReset();
    localStorage.setItem(dirtsideGameKey, JSON.stringify(handle));
  });

  afterEach(cleanup);

  // The name appears in the roster as well as in the open activation, and only the activation row
  // carries the markers, so the row is found by the marker rather than by the name alone.
  const activationRow = async () =>
    (await screen.findAllByText(/Alpha One/)).map((node) => node.textContent ?? '').find((text) => text.includes('move'));

  it('shows the movement the server says the element has now', async () => {
    readGame.mockResolvedValue(activating([element()]));

    render(<DirtsideView />);

    expect(await activationRow()).toContain('move 12');
  });

  it('shows the halved movement beside a damaged element', async () => {
    readGame.mockResolvedValue(activating([element({ isDamaged: true, movement: 6 })]));

    render(<DirtsideView />);

    const row = await activationRow();
    expect(row).toContain('damaged');
    expect(row).toContain('move 6');
    expect(row).not.toContain('move 12');
  });
});

// Every per-element button used to read the same to a screen reader - "Move", "Move", "Move" - with
// the element's name in a sibling span nothing attached to them. The name travels in the accessible
// name now, and a refusal that lived only in a `title` on a disabled button is rendered as text.
describe('DirtsideView element controls are told apart', () => {
  beforeEach(() => {
    localStorage.clear();
    readGame.mockReset();
    localStorage.setItem(dirtsideGameKey, JSON.stringify(handle));
  });

  afterEach(cleanup);

  it('names the element in every per-element button', async () => {
    readGame.mockResolvedValue(activating([
      element(),
      element({ id: 'alpha-2', name: 'Alpha Two' }),
    ]));

    render(<DirtsideView />);

    // The control that must be accepted: the verb a sighted player reads is unchanged.
    expect((await screen.findAllByText('Move')).length).toBe(2);

    // And the two are distinguishable by accessible name, which is what a reader hears.
    expect(screen.getByRole('button', { name: 'Move Alpha One' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Move Alpha Two' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Stand down Alpha Two' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Aim Alpha One' })).toBeTruthy();

    // The dullest violation: a bare "Move" with no name must not survive anywhere.
    expect(screen.queryByRole('button', { name: 'Move' })).toBeNull();
  });

  it('renders the recovery refusal as text rather than only as a tooltip', async () => {
    readGame.mockResolvedValue(activating([element({
      isSystemsDown: true,
      canRecoverSystems: false,
      whyItCannotRecoverSystems: 'The marker went on this activation.',
    })]));

    render(<DirtsideView />);

    expect(await screen.findByText('Alpha One: The marker went on this activation.')).toBeTruthy();
  });
});

/**
 * Who the open activation is waiting on.
 *
 * `elementsStillToChoose` has been computed on every snapshot since Dirtside had a screen and was
 * read by no production code in either language, so a table learned who was holding the activation
 * up by asking each other or by hovering a disabled button. The ids the wire carries are turned into
 * names, because nobody at a table calls a vehicle `alpha-2`.
 */
describe('DirtsideView waiting list', () => {
  beforeEach(() => {
    localStorage.clear();
    readGame.mockReset();
    localStorage.setItem(dirtsideGameKey, JSON.stringify(handle));
  });

  afterEach(cleanup);

  it('names who the activation is waiting on rather than listing ids', async () => {
    const open = activating([
      element({ hasChosen: true }),
      element({ id: 'alpha-2', name: 'Alpha Two' }),
    ]);
    readGame.mockResolvedValue({ ...open, elementsStillToChoose: ['alpha-2'] });

    render(<DirtsideView />);

    const said = (await screen.findByText(/Waiting on/)).textContent ?? '';
    expect(said).toContain('Alpha Two');
    expect(said).not.toContain('alpha-2');
  });

  it('says so when the activation could close', async () => {
    readGame.mockResolvedValue(activating([element({ hasChosen: true })]));

    render(<DirtsideView />);

    expect(await screen.findByText(/Every element has said what it is doing/)).toBeTruthy();
  });

  it('marks the elements that have not chosen, beside the two flags that is built from', async () => {
    // `hasChosen` is the one flag on this DTO that had no renderer. It is not the same as having
    // done both things - an element that has moved and not acted has chosen - so it is rendered
    // next to `moved` and `acted` rather than instead of them.
    readGame.mockResolvedValue(activating([element({ hasMoved: true, hasChosen: true })]));

    render(<DirtsideView />);

    const row = await screen.findByText(/Alpha One · moved/);
    expect(row.textContent).not.toContain('still to choose');
  });

  it('falls back to the id when the roster does not know the element', async () => {
    // Version skew rather than a normal state, but printing nothing would be worse than printing
    // something unfamiliar - the activation would look closeable and refuse.
    const open = activating([element({ hasChosen: true })]);
    readGame.mockResolvedValue({ ...open, elementsStillToChoose: ['ghost-9'] });

    render(<DirtsideView />);

    expect((await screen.findByText(/Waiting on/)).textContent).toContain('ghost-9');
  });
});

describe('DirtsideView status line', () => {
  // Found playing a game in the browser: the line shows the newest log entry after each command,
  // and a command that logs nothing left the previous one standing - after red's platoon activated
  // it still said blue's element had stood down.
  const stale = "Hammer's Vehicle 2 stood down for the turn.";

  beforeEach(() => {
    localStorage.clear();
    readGame.mockReset();
    beginTurn.mockReset();
    endTurn.mockReset();
    localStorage.setItem(dirtsideGameKey, JSON.stringify(handle));
  });

  afterEach(cleanup);

  function status() {
    return screen.getAllByText((_, node) => node?.getAttribute('aria-live') === 'polite' && node.tagName === 'P')[0].textContent;
  }

  it('says the turn ended rather than repeating an older entry when ending it logs nothing', async () => {
    const before = { ...snapshot, phase: 'Activating', log: [stale] };
    readGame.mockResolvedValue(before);
    endTurn.mockResolvedValue({ ...before, phase: 'TurnEnded', version: 2 });

    render(<DirtsideView />);
    fireEvent.click(await screen.findByRole('button', { name: 'End Turn' }));

    await waitFor(() => expect(endTurn).toHaveBeenCalled());
    await waitFor(() => expect(status()).toBe('Turn ended.'));
  });

  it('still shows what a command logged when it did log something', async () => {
    const before = { ...snapshot, log: [stale] };
    readGame.mockResolvedValue(before);
    beginTurn.mockResolvedValue({ ...before, turnNumber: 2, log: [stale, 'Turn 2 began; red keeps the initiative.'], version: 2 });

    render(<DirtsideView />);
    fireEvent.click(await screen.findByRole('button', { name: 'Begin Turn' }));

    await waitFor(() => expect(status()).toBe('Turn 2 began; red keeps the initiative.'));
  });

  it('still shows what a command logged once the log is at its cap', async () => {
    // The server keeps the newest 2000 entries; past that a logged line leaves the length alone,
    // and "did it grow" said nothing had been logged.
    // Five entries stand in for the server's 2000: what matters is a log that stays the same length
    // because the oldest entry was dropped for the new one, and a short one keeps the render cheap.
    const full = Array.from({ length: 5 }, (_, index) => `entry ${index}`);
    const before = { ...snapshot, log: full };
    readGame.mockResolvedValue(before);
    beginTurn.mockResolvedValue({ ...before, turnNumber: 2, log: [...full.slice(1), 'Turn 2 began; red keeps the initiative.'], version: 2 });

    render(<DirtsideView />);
    fireEvent.click(await screen.findByRole('button', { name: 'Begin Turn' }));

    await waitFor(() => expect(status()).toBe('Turn 2 began; red keeps the initiative.'));
  });
});

describe('DirtsideView firer walk', () => {
  // The walk reached the engine and the wire on 2026-11-09 and not the screen: only an API client
  // could enter one, and a game that fell back to the built-in walk said so only by a null field.
  const profile: DirtsideSnapshot['profile'] = {
    fireControl: [{ key: 'Basic', die: 'D6' }],
    posture: [],
    signature: [],
    systemsDownRecoveryDie: null,
    systemsDownRecoveryRoll: 0,
    systemsDownRecoveryRollWithBackup: 0,
    areaDefenceReach: 0,
  };

  beforeEach(() => {
    localStorage.clear();
    readGame.mockReset();
    createGame.mockReset();
  });

  afterEach(cleanup);

  it('sends the walk typed on the create screen', async () => {
    createGame.mockResolvedValue({ ...handle, snapshot: { ...snapshot, profile } });

    render(<DirtsideView />);
    fireEvent.change(screen.getByLabelText('Firer walk: rungs up at close'), { target: { value: '2' } });
    fireEvent.change(screen.getByLabelText('Rungs down at long'), { target: { value: '0' } });
    fireEvent.change(screen.getByLabelText('Rungs down moving over half'), { target: { value: '3' } });
    fireEvent.click(screen.getByRole('button', { name: 'Start Game' }));

    await waitFor(() => expect(createGame).toHaveBeenCalled());
    expect(createGame.mock.calls[0][2]).toMatchObject({
      firerWalk: { rungsUpAtClose: 2, rungsDownAtLong: 0, rungsDownMovingOverHalf: 3 },
    });
  });

  it('asks before starting a game that will play the built-in walk, and starts nothing on a no', async () => {
    // The walk is the one entry the server fills in itself; a table that never saw the boxes would
    // play the app's rungs without knowing. Asked rather than refused, so no flow is broken.
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
    createGame.mockResolvedValue({ ...handle, snapshot: { ...snapshot, profile } });

    render(<DirtsideView />);
    fireEvent.click(screen.getByRole('button', { name: 'Start Game' }));

    expect(confirm).toHaveBeenCalledTimes(1);
    expect(confirm.mock.calls[0][0]).toContain('built-in firer walk');
    expect(createGame).not.toHaveBeenCalled();
    expect(screen.getByText(/Enter your firer walk, or start again/)).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'Start Game' }));
    await waitFor(() => expect(createGame).toHaveBeenCalledTimes(1));
    confirm.mockRestore();
  });

  it('does not ask when the walk was entered', async () => {
    const confirm = vi.spyOn(window, 'confirm');
    createGame.mockResolvedValue({ ...handle, snapshot: { ...snapshot, profile } });

    render(<DirtsideView />);
    fireEvent.change(screen.getByLabelText('Firer walk: rungs up at close'), { target: { value: '1' } });
    fireEvent.change(screen.getByLabelText('Rungs down at long'), { target: { value: '1' } });
    fireEvent.change(screen.getByLabelText('Rungs down moving over half'), { target: { value: '1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Start Game' }));

    await waitFor(() => expect(createGame).toHaveBeenCalledTimes(1));
    expect(confirm).not.toHaveBeenCalled();
    confirm.mockRestore();
  });

  it('says on the game screen when the built-in walk is being played', async () => {
    localStorage.setItem(dirtsideGameKey, JSON.stringify(handle));
    readGame.mockResolvedValue({ ...snapshot, profile: { ...profile, firerWalk: null } });

    render(<DirtsideView />);

    const line = await screen.findByText(/playing the built-in firer walk/);
    expect(line.className).toContain('warning');
  });
});

describe('DirtsideView battle log', () => {
  // The server keeps up to 2000 lines and every one went into the DOM on every render. The newest
  // 300 are drawn, as the Full Thrust log does; the rest stay in the snapshot.
  beforeEach(() => {
    localStorage.clear();
    readGame.mockReset();
    localStorage.setItem(dirtsideGameKey, JSON.stringify(handle));
  });

  afterEach(cleanup);

  it('draws only the newest 300 lines of a long log, and says so', async () => {
    const log = Array.from({ length: 350 }, (_, index) => `entry ${index}`);
    readGame.mockResolvedValue({ ...snapshot, log, logStart: 0 });

    const { container } = render(<DirtsideView />);
    await screen.findByText('Reopened Ridge Line.');

    const lines = container.querySelectorAll('[aria-label="Battle log"] li');
    expect(lines.length).toBe(300);
    expect(lines[0].textContent).toBe('entry 50');
    expect(lines[299].textContent).toBe('entry 349');
    expect(screen.getByText('Showing the most recent 300 of 350 lines.')).toBeTruthy();
  });
});

describe('DirtsideView says what to do next', () => {
  // The header printed the engine's phase name - "Turn 1 · ChoosingFirstActivator" - and left the
  // table to find the button the game was waiting for.
  beforeEach(() => {
    localStorage.clear();
    readGame.mockReset();
    localStorage.setItem(dirtsideGameKey, JSON.stringify(handle));
  });

  afterEach(cleanup);

  it('names the phase in words and the step the game is waiting for', async () => {
    readGame.mockResolvedValue({ ...snapshot, phase: 'ChoosingFirstActivator' });

    render(<DirtsideView />);

    const next = await screen.findByLabelText('What to do next');
    expect(next.textContent).toContain('Turn 1 · Choosing who goes first');
    expect(next.textContent).toContain('Choose which side goes first.');
    expect(screen.queryByText(/ChoosingFirstActivator/)).toBeNull();
  });
});

describe('DirtsideView in words (playtest m6)', () => {
  afterEach(cleanup);

  it('labels the posture and chit rows in words, not code names', () => {
    localStorage.clear();
    render(<DirtsideView />);

    expect(screen.getByLabelText('Hull down')).toBeTruthy();
    expect(screen.getByLabelText('Systems down (target)')).toBeTruthy();
    expect(screen.queryByText('HullDown')).toBeNull();
    expect(screen.queryByText('SystemsDownTarget')).toBeNull();
  });
});

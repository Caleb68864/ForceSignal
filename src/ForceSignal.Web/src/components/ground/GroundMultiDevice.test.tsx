// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { dirtsideGameKey, starGruntGameKey } from '../../constants.ts';
import { ApiRequestError } from '../../lib/api.ts';
import { toGameCode } from '../../lib/groundShare.ts';
import type { DirtsideSnapshot, GameHandle, StarGruntSnapshot } from '../../types.ts';
import { DirtsideView } from './DirtsideView.tsx';
import { StarGruntView } from './StarGruntView.tsx';

const dirtside = vi.hoisted(() => ({ readGame: vi.fn() }));
const starGrunt = vi.hoisted(() => ({ readGame: vi.fn() }));
const live = vi.hoisted(() => ({ follow: vi.fn() }));
vi.mock('../../lib/dirtsideApi.ts', () => dirtside);
vi.mock('../../lib/starGruntApi.ts', () => starGrunt);
// The link itself is covered in groundLive.test.ts; here it is the screen's answer to a push.
vi.mock('../../lib/groundLive.ts', () => ({
  useGroundLive: (engine: string, game: GameHandle | null, onChanged: (version: number | null) => void) => {
    live.follow(engine, game, onChanged);
    return game ? 'live' : 'offline';
  },
}));

const handle: GameHandle = { gameId: '0f8fad5b-d9cb-469f-a165-70867728950e', token: 'b'.repeat(64) };

const ridge: DirtsideSnapshot = {
  gameId: handle.gameId,
  name: 'Ridge Line',
  turnNumber: 1,
  phase: 'Setup',
  sides: ['blue', 'red'],
  elementsStillToChoose: [],
  canEndActivation: false,
  units: [],
  log: [],
  version: 3,
};

const hill = JSON.parse(`{
  "gameId": "${handle.gameId}", "name": "Hill 43", "turnNumber": 0, "phase": "Setup",
  "sides": ["blue", "red"], "units": [], "log": [], "version": 3,
  "profile": { "bandWidths": [], "rangeDice": [] }
}`) as StarGruntSnapshot;

function lastFollow() {
  return live.follow.mock.calls[live.follow.mock.calls.length - 1] as [string, GameHandle | null, (version: number | null) => void];
}

describe('a ground game joined from another device', () => {
  beforeEach(() => {
    localStorage.clear();
    dirtside.readGame.mockReset();
    starGrunt.readGame.mockReset();
    live.follow.mockReset();
  });

  afterEach(cleanup);

  it('opens a Dirtside game from its code and keeps it on this device', async () => {
    dirtside.readGame.mockResolvedValue(ridge);
    render(<DirtsideView />);

    fireEvent.change(screen.getByLabelText('Game code'), { target: { value: `  ${toGameCode(handle)}\n` } });
    fireEvent.click(screen.getByText('Join Game', { selector: 'button' }));

    expect(await screen.findByText('Joined Ridge Line.')).toBeTruthy();
    expect(dirtside.readGame).toHaveBeenCalledWith(handle);
    expect(JSON.parse(localStorage.getItem(dirtsideGameKey) ?? 'null')).toEqual(handle);
  });

  it('opens a StarGrunt game from its code', async () => {
    starGrunt.readGame.mockResolvedValue(hill);
    render(<StarGruntView />);

    fireEvent.change(screen.getByLabelText('Game code'), { target: { value: toGameCode(handle) } });
    fireEvent.click(screen.getByText('Join Game', { selector: 'button' }));

    expect(await screen.findByText('Joined Hill 43.')).toBeTruthy();
    expect(JSON.parse(localStorage.getItem(starGruntGameKey) ?? 'null')).toEqual(handle);
  });

  it('says so when the text is not a game code, and asks the server nothing', () => {
    render(<DirtsideView />);

    fireEvent.change(screen.getByLabelText('Game code'), { target: { value: 'HAWK-FALCON' } });
    fireEvent.click(screen.getByText('Join Game', { selector: 'button' }));

    expect(screen.getByRole('alert').textContent).toContain('not a game code');
    expect(dirtside.readGame).not.toHaveBeenCalled();
  });

  it('says so when the server will not open the game for that code', async () => {
    dirtside.readGame.mockRejectedValue(new ApiRequestError('Game token is invalid.', 403));
    render(<DirtsideView />);

    fireEvent.change(screen.getByLabelText('Game code'), { target: { value: toGameCode(handle) } });
    fireEvent.click(screen.getByText('Join Game', { selector: 'button' }));

    expect((await screen.findByText(/does not open a game on this server/)).textContent).toBeTruthy();
    expect(localStorage.getItem(dirtsideGameKey)).toBeNull();
  });

  it('shows the game code on the game screen only when asked', async () => {
    localStorage.setItem(dirtsideGameKey, JSON.stringify(handle));
    dirtside.readGame.mockResolvedValue(ridge);
    render(<DirtsideView />);
    await screen.findByText('Reopened Ridge Line.');

    expect(screen.queryByText(toGameCode(handle))).toBeNull();
    fireEvent.click(screen.getByText('Show Game Code', { selector: 'button' }));
    expect(screen.getByText(toGameCode(handle))).toBeTruthy();
    expect(screen.getByText('Link live')).toBeTruthy();
  });
});

describe('a change made on the other device', () => {
  beforeEach(() => {
    localStorage.clear();
    dirtside.readGame.mockReset();
    live.follow.mockReset();
    localStorage.setItem(dirtsideGameKey, JSON.stringify(handle));
  });

  afterEach(cleanup);

  it('is read and shown when the push names a newer version', async () => {
    dirtside.readGame.mockResolvedValueOnce(ridge);
    render(<DirtsideView />);
    await screen.findByText('Reopened Ridge Line.');
    expect(lastFollow()[0]).toBe('dirtside');
    expect(lastFollow()[1]).toEqual(handle);

    dirtside.readGame.mockResolvedValueOnce({ ...ridge, turnNumber: 2, version: 4 });
    lastFollow()[2](4);

    expect(await screen.findByText(/Turn 2/)).toBeTruthy();
  });

  it('reads nothing for a version this screen already shows', async () => {
    dirtside.readGame.mockResolvedValueOnce(ridge);
    render(<DirtsideView />);
    await screen.findByText('Reopened Ridge Line.');

    lastFollow()[2](3);
    lastFollow()[2](2);

    await Promise.resolve();
    expect(dirtside.readGame).toHaveBeenCalledTimes(1);
  });

  it('reads the game again after the link comes back, whatever was missed', async () => {
    dirtside.readGame.mockResolvedValueOnce(ridge);
    render(<DirtsideView />);
    await screen.findByText('Reopened Ridge Line.');

    dirtside.readGame.mockResolvedValueOnce({ ...ridge, turnNumber: 5, version: 9 });
    lastFollow()[2](null);

    await waitFor(() => expect(dirtside.readGame).toHaveBeenCalledTimes(2));
    expect(await screen.findByText(/Turn 5/)).toBeTruthy();
  });
});

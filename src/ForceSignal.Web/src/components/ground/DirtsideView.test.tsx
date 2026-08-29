// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { dirtsideGameKey } from '../../constants.ts';
import { ApiRequestError } from '../../lib/api.ts';
import type { DirtsideSnapshot, GameHandle } from '../../types.ts';
import { DirtsideView } from './DirtsideView.tsx';

const readGame = vi.fn<(game: GameHandle) => Promise<DirtsideSnapshot>>();
vi.mock('../../lib/dirtsideApi.ts', () => ({
  readGame: (game: GameHandle) => readGame(game),
}));

const handle: GameHandle = { gameId: 'game-1', token: 'token-1' };
const snapshot: DirtsideSnapshot = {
  gameId: 'game-1',
  name: 'Ridge Line',
  turnNumber: 1,
  phase: 'Setup',
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
});

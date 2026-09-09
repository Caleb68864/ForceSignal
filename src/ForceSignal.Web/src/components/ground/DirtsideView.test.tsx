// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { dirtsideGameKey } from '../../constants.ts';
import { ApiRequestError } from '../../lib/api.ts';
import type { DirtsideElementState, DirtsidePlatoonState, DirtsideSnapshot, GameHandle } from '../../types.ts';
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

// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { starGruntGameKey } from '../../constants.ts';
import type { GameHandle, StarGruntSnapshot } from '../../types.ts';
import { StarGruntView } from './StarGruntView.tsx';

const readGame = vi.fn<(game: GameHandle) => Promise<StarGruntSnapshot>>();
vi.mock('../../lib/starGruntApi.ts', () => ({
  readGame: (game: GameHandle) => readGame(game),
}));

const handle: GameHandle = { gameId: 'game-1', token: 'token-1' };

/**
 * A snapshot as it arrives: parsed from JSON, because that is the only way one is ever made.
 * `get<StarGruntSnapshot>` asserts the shape rather than checking it, so what the client holds is
 * whatever the server sent - including a unit written by a version that did not put figures on the
 * wire.
 */
function snapshotFrom(unitJson: string): StarGruntSnapshot {
  return JSON.parse(`{
    "gameId": "game-1",
    "name": "Hill 43",
    "turnNumber": 1,
    "phase": "Setup",
    "sides": ["blue", "red"],
    "units": [${unitJson}],
    "log": []
  }`) as StarGruntSnapshot;
}

const wholeUnit = `{
  "id": "alpha", "name": "Alpha Squad", "side": "blue", "level": "Squad",
  "qualityDie": 8, "leadershipValue": 2, "fatigue": "Fresh",
  "figures": [{ "armourDie": 12 }, { "armourDie": 12 }],
  "figuresAlive": 2, "fullStrength": 2, "figuresWounded": 0,
  "isLeaderDown": false, "suppressionMarkers": 0, "confidence": "Confident",
  "isDisorganised": false, "isInCover": false, "nextMoveLeavesCover": false,
  "reactionTestCleared": false, "hasActivated": false,
  "weapons": [{ "name": "Rifles", "impactDie": 10, "isSupport": false, "isCloseRange": false, "supportFirepowerDie": 6, "neverJoinsSquadFire": false }],
  "canActivate": true, "activationBlocker": null, "weaponLegality": []
}`;

/** The same unit from a server that did not send rosters. Only `figures` differs. */
const unitWithNoRoster = wholeUnit.replace('"figures": [{ "armourDie": 12 }, { "armourDie": 12 }],', '');

async function open(unitJson: string) {
  localStorage.setItem(starGruntGameKey, JSON.stringify(handle));
  readGame.mockResolvedValue(snapshotFrom(unitJson));
  render(<StarGruntView />);
  // Reached-the-subject: the game really opened, so the Export button below is on a live game.
  expect(await screen.findByText('Reopened Hill 43.')).toBeTruthy();
}

/**
 * Export was the only control on this screen with no error handling of any kind: a bare `onClick`,
 * outside the `run()` wrapper everything else uses. `toForceFile` throwing landed as an unhandled
 * React event-handler error, so the button downloaded nothing, said nothing, and looked exactly
 * like a button that had worked. A save control that silently does not save is worse than one that
 * refuses.
 */
describe('StarGruntView force export', () => {
  beforeEach(() => {
    localStorage.clear();
    readGame.mockReset();
    // jsdom implements no object URLs. Recorded rather than performed, so the success case below
    // proves a file was actually written rather than that nothing threw.
    vi.stubGlobal('URL', Object.assign(Object.create(URL), {
      createObjectURL: vi.fn(() => 'blob:written'),
      revokeObjectURL: vi.fn(),
    }));
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it('says so when the snapshot carries no roster to write', async () => {
    await open(unitWithNoRoster);

    fireEvent.click(screen.getByRole('button', { name: 'Export blue' }));

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toMatch(/Alpha Squad/);
    expect(alert.textContent).not.toMatch(/Exported/);
    expect(URL.createObjectURL).not.toHaveBeenCalled();
  });

  it('still writes a file for a snapshot that carries one', async () => {
    // The control. Without it a refusal that refuses everything would read as a pass.
    await open(wholeUnit);

    fireEvent.click(screen.getByRole('button', { name: 'Export blue' }));

    expect(await screen.findByText('Exported blue.')).toBeTruthy();
    expect(URL.createObjectURL).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('alert')).toBeNull();
  });
});

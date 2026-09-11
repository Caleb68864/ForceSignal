// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { starGruntGameKey } from '../../constants.ts';
import type { GameHandle, StarGruntSnapshot } from '../../types.ts';
import { StarGruntView } from './StarGruntView.tsx';

const readGame = vi.fn<(game: GameHandle) => Promise<StarGruntSnapshot>>();
const addUnit = vi.fn<(game: GameHandle, unit: Record<string, unknown>) => Promise<StarGruntSnapshot>>();
vi.mock('../../lib/starGruntApi.ts', () => ({
  readGame: (game: GameHandle) => readGame(game),
  addUnit: (game: GameHandle, unit: Record<string, unknown>) => addUnit(game, unit),
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

/**
 * Blanking a form is only half the job, and the other half is what a browser does with the blank.
 *
 * A `<select>` whose value matches none of its options renders the first one, so seeding the quality
 * die at zero over a ladder of 4 to 12 would have shown "D4" while holding nothing - a form that
 * looks filled in and is empty, which is worse than the 8 it replaced. This is the half the unit
 * walk in `lib/contentPolicy.test.ts` cannot see, because it reads the object rather than the
 * screen.
 */
describe('StarGrunt add-a-squad panel', () => {
  beforeEach(() => {
    localStorage.clear();
    readGame.mockReset();
    addUnit.mockReset();
  });

  afterEach(cleanup);

  /** The selects on the add-a-squad panel, by the label each sits under. */
  function die(label: string): HTMLSelectElement {
    return screen.getByLabelText(label) as HTMLSelectElement;
  }

  it('shows every die as unentered rather than as the first rung of the ladder', async () => {
    await open(wholeUnit);

    // Reached-the-subject: the panel is on screen with its selects on it.
    expect(screen.getByRole('button', { name: 'Add Squad' })).toBeTruthy();

    for (const label of ['Quality', 'Armour', 'Impact', 'Leadership']) {
      const select = die(label);
      expect(select.value).toBe('0');
      expect(select.selectedOptions[0]?.textContent).toBe('Not entered');
    }

    expect((screen.getByLabelText('Figures') as HTMLInputElement).value).toBe('0');
  });

  it('posts exactly the record card that was typed in, and nothing beside it', async () => {
    // The control that must be accepted. A blanked form whose dice could no longer be entered at
    // all would pass every check above and be useless.
    await open(wholeUnit);
    readGame.mockResolvedValue(snapshotFrom(wholeUnit));
    addUnit.mockResolvedValue(snapshotFrom(wholeUnit));

    fireEvent.change(die('Quality'), { target: { value: '12' } });
    fireEvent.change(die('Armour'), { target: { value: '4' } });
    fireEvent.change(die('Impact'), { target: { value: '10' } });
    fireEvent.change(die('Leadership'), { target: { value: '1' } });
    fireEvent.change(screen.getByLabelText('Figures'), { target: { value: '5' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add Squad' }));

    expect(await screen.findByText(/joined blue\./)).toBeTruthy();
    const posted = addUnit.mock.calls[0][1];
    expect(posted.qualityDie).toBe(12);
    expect(posted.leadershipValue).toBe(1);
    expect(posted.figures).toEqual(Array.from({ length: 5 }, () => ({ armourDie: 4 })));
    expect((posted.weapons as { impactDie: number }[])[0].impactDie).toBe(10);
  });
});

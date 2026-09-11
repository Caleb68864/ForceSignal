// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { starGruntGameKey } from '../../constants.ts';
import type { GameHandle, StarGruntSnapshot } from '../../types.ts';
import { StarGruntView } from './StarGruntView.tsx';

const readGame = vi.fn<(game: GameHandle) => Promise<StarGruntSnapshot>>();
const addUnit = vi.fn<(game: GameHandle, unit: Record<string, unknown>) => Promise<StarGruntSnapshot>>();
const fightMelee = vi.fn<(game: GameHandle, request: Record<string, unknown>) => Promise<StarGruntSnapshot>>();
const createGame = vi.fn<(name: string, profile?: Record<string, unknown>) => Promise<GameHandle & { snapshot: StarGruntSnapshot }>>();
vi.mock('../../lib/starGruntApi.ts', () => ({
  readGame: (game: GameHandle) => readGame(game),
  addUnit: (game: GameHandle, unit: Record<string, unknown>) => addUnit(game, unit),
  fightMelee: (game: GameHandle, request: Record<string, unknown>) => fightMelee(game, request),
  createGame: (name: string, profile?: Record<string, unknown>) => createGame(name, profile),
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

/**
 * The melee the engine can fight, as this screen is able to ask for it.
 *
 * `CloseAssault.Fight` doubles a figure's score for power armour, `Combatant` carries the flag,
 * `StarGruntGameService` passes it and `MeleePairingDto` puts it on the wire - and this screen, the
 * only caller of `fightMelee` in the app, sent a literal `false` for both sides with no control
 * anywhere to change it. A table fielding power-armoured troopers fought every melee at half
 * strength and nothing said the option existed.
 *
 * There was no workaround either, unlike the support-weapon flags beside it: the pairings are built
 * in the component at click time from form state that had no armour field, so no imported force
 * file could reach them.
 */
describe('StarGrunt close assault', () => {
  beforeEach(() => {
    localStorage.clear();
    readGame.mockReset();
    fightMelee.mockReset();
  });

  afterEach(cleanup);

  /** Two units with one of them activated, which is what puts the assault panel on screen. */
  function assaultSnapshot(): StarGruntSnapshot {
    const snapshot = snapshotFrom(`${wholeUnit},${wholeUnit.replace('"id": "alpha", "name": "Alpha Squad", "side": "blue"', '"id": "bravo", "name": "Bravo Squad", "side": "red"')}`);
    return { ...snapshot, activatingUnitId: 'alpha' };
  }

  async function openAssault() {
    localStorage.setItem(starGruntGameKey, JSON.stringify(handle));
    readGame.mockResolvedValue(assaultSnapshot());
    render(<StarGruntView />);
    expect(await screen.findByText('Reopened Hill 43.')).toBeTruthy();
  }

  it('sends the power armour the table said its men are wearing', async () => {
    await openAssault();
    fightMelee.mockResolvedValue(assaultSnapshot());

    // Reached-the-subject: the assault panel really rendered, so the checkboxes below are the
    // real ones and Fight Round is on a live activation rather than absent.
    const fightRound = screen.getByRole('button', { name: 'Fight Round' });
    expect(fightRound).toBeTruthy();

    fireEvent.click(screen.getByLabelText('Attackers in power armour'));
    fireEvent.click(screen.getByLabelText('Defenders in power armour'));
    fireEvent.click(fightRound);

    expect(await screen.findByText('A round of melee was fought.')).toBeTruthy();
    const sent = fightMelee.mock.calls[0][1] as { pairings: { attackerPowerArmour: boolean; defenderPowerArmour: boolean }[] };
    expect(sent.pairings[0].attackerPowerArmour).toBe(true);
    expect(sent.pairings[0].defenderPowerArmour).toBe(true);
  });

  it('sends false when the table did not say so, which is the ordinary case', async () => {
    // The control that must be accepted. A screen that sent `true` unconditionally would pass the
    // test above and be exactly as wrong, in the other direction - and this flag is a fact about
    // the figures on the table, so inventing either answer is the defect.
    await openAssault();
    fightMelee.mockResolvedValue(assaultSnapshot());

    fireEvent.click(screen.getByRole('button', { name: 'Fight Round' }));

    expect(await screen.findByText('A round of melee was fought.')).toBeTruthy();
    const sent = fightMelee.mock.calls[0][1] as { pairings: { attackerPowerArmour: boolean; defenderPowerArmour: boolean }[] };
    expect(sent.pairings[0].attackerPowerArmour).toBe(false);
    expect(sent.pairings[0].defenderPowerArmour).toBe(false);
  });
});

/**
 * The range table on the create screen.
 *
 * The engine used to carry StarGrunt's range page as arithmetic - a band the size of the firer's die,
 * a walk up the ladder a rung a band, cover worth one or two rungs - and now refuses a shot whose
 * entry nobody made. So the form that feeds it has to open on nothing, look like nothing, and send
 * exactly what was typed: a select showing a die over an empty value is a form that looks filled in
 * and is not, which is a bug this screen has already had once.
 */
describe('StarGrunt range table', () => {
  beforeEach(() => {
    localStorage.clear();
    readGame.mockReset();
    createGame.mockReset();
  });

  afterEach(cleanup);

  function created(profile: StarGruntSnapshot['profile']) {
    return { ...handle, snapshot: { ...snapshotFrom(wholeUnit), profile } };
  }

  it('opens with every entry showing as not entered', () => {
    render(<StarGruntView />);

    const range = screen.getByLabelText('Range die, 1 band out') as HTMLSelectElement;
    expect(range.value).toBe('');
    expect(range.selectedOptions[0]?.textContent).toBe('Not entered');

    for (const label of ['Band for D8 troops, inches', 'Bands of effective range', 'Soft cover, rungs', 'Hard cover, rungs', 'Dug in, rungs', 'Melee cover, rungs']) {
      const input = screen.getByLabelText(label) as HTMLInputElement;
      expect(input.value).toBe('');
      expect(input.placeholder).toBe('Not entered');
    }
  });

  it('starts a game with no table at all when nothing was entered', async () => {
    createGame.mockResolvedValue(created({ bandWidths: [], rangeDice: [] }));
    render(<StarGruntView />);

    fireEvent.click(screen.getByRole('button', { name: 'Start Game' }));

    expect(await screen.findByText(/Started Hill 43/)).toBeTruthy();
    expect(createGame.mock.calls[0][1]).toBeUndefined();
    // And the game screen says so, warning-styled, before anybody fires.
    const summary = screen.getByText(/^Range table:/);
    expect(summary.textContent).toMatch(/first shot will be refused/);
    expect(summary.className).toMatch(/warning/);
  });

  it('sends exactly the entries that were typed in', async () => {
    // The control that must be accepted: a form that could not send a table would pass both tests
    // above and leave every game refusing its first shot. Invented numbers.
    createGame.mockResolvedValue(created({ bandWidths: [{ qualityDie: 8, inches: 7 }], rangeDice: [{ bandsOut: 2, die: 4 }] }));
    render(<StarGruntView />);

    fireEvent.change(screen.getByLabelText('Band for D8 troops, inches'), { target: { value: '7' } });
    fireEvent.change(screen.getByLabelText('Range die, 1 band out'), { target: { value: '4' } });
    // Filling the first row puts the second on screen.
    fireEvent.change(screen.getByLabelText('Range die, 2 bands out'), { target: { value: '6' } });
    fireEvent.change(screen.getByLabelText('Hard cover, rungs'), { target: { value: '0' } });
    fireEvent.click(screen.getByRole('button', { name: 'Start Game' }));

    expect(await screen.findByText(/Started Hill 43/)).toBeTruthy();
    expect(createGame.mock.calls[0][1]).toEqual({
      bandWidths: [{ qualityDie: 8, inches: 7 }],
      rangeDice: [{ bandsOut: 1, die: 4 }, { bandsOut: 2, die: 6 }],
      hardCoverShift: 0,
    });
    const summary = screen.getByText(/^Range table:/);
    expect(summary.className).not.toMatch(/warning/);
  });
});

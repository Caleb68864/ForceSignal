// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { starGruntGameKey } from '../../constants.ts';
import { ApiRequestError } from '../../lib/api.ts';
import type { GameHandle, StarGruntSnapshot } from '../../types.ts';
import { StarGruntView } from './StarGruntView.tsx';

const readGame = vi.fn<(game: GameHandle) => Promise<StarGruntSnapshot>>();
const addUnit = vi.fn<(game: GameHandle, unit: Record<string, unknown>) => Promise<StarGruntSnapshot>>();
const fightMelee = vi.fn<(game: GameHandle, request: Record<string, unknown>) => Promise<StarGruntSnapshot>>();
const createGame = vi.fn<(name: string, profile?: Record<string, unknown>) => Promise<GameHandle & { snapshot: StarGruntSnapshot }>>();
const fire = vi.fn<(game: GameHandle, request: Record<string, unknown>) => Promise<StarGruntSnapshot>>();
const confidenceTest = vi.fn<(game: GameHandle, unitId: string, threat: number) => Promise<StarGruntSnapshot>>();
// The live link to other devices is covered in groundLive.test.ts. Here it must not reach for a
// server: in jsdom a real SignalR start fails and logs after the test has ended.
vi.mock('../../lib/groundLive.ts', () => ({ useGroundLive: () => 'offline' }));
vi.mock('../../lib/starGruntApi.ts', () => ({
  confidenceTest: (game: GameHandle, unitId: string, threat: number) => confidenceTest(game, unitId, threat),
  fire: (game: GameHandle, request: Record<string, unknown>) => fire(game, request),
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
/**
 * A snapshot, on a profile that says which numbers are Leadership Values.
 *
 * 2 to 5, invented, and deliberately backwards from the bound that used to be written into the
 * add-a-squad panel: it offered 1, 2 and 3. A panel that still knew those would offer a 1 this
 * table has not got and would not offer the 4 it has.
 */
function snapshotFrom(unitJson: string, profileJson = '"profile": { "bandWidths": [], "rangeDice": [], "lowestLeadershipValue": 2, "highestLeadershipValue": 5 },'): StarGruntSnapshot {
  return JSON.parse(`{
    "gameId": "game-1",
    "name": "Hill 43",
    "turnNumber": 0,
    "phase": "Setup",
    "sides": ["blue", "red"],
    ${profileJson}
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

async function open(unitJson: string, profileJson?: string) {
  localStorage.setItem(starGruntGameKey, JSON.stringify(handle));
  readGame.mockResolvedValue(
    profileJson === undefined ? snapshotFrom(unitJson) : snapshotFrom(unitJson, profileJson));
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

  // The server refuses a unit once the first turn has begun (it would restart the game). Adding a
  // squad and importing a force both add units, so both go; exporting adds nothing and stays.
  it('closes adding and importing at the first turn, and keeps export', async () => {
    await open(wholeUnit);
    expect(screen.getByRole('button', { name: 'Add Squad' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Import Force' })).toBeTruthy();
    cleanup();

    localStorage.setItem(starGruntGameKey, JSON.stringify(handle));
    readGame.mockResolvedValue({ ...snapshotFrom(wholeUnit), turnNumber: 1, phase: 'Activations' } as StarGruntSnapshot);
    render(<StarGruntView />);
    expect(await screen.findByText('Reopened Hill 43.')).toBeTruthy();

    expect(screen.queryByRole('button', { name: 'Add Squad' })).toBeNull();
    expect(screen.queryByLabelText('Add a unit')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Import Force' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Export blue' })).toBeTruthy();
  });

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
    // 4 is a Leadership Value at this table and was not one the panel used to offer at all.
    fireEvent.change(die('Leadership'), { target: { value: '4' } });
    fireEvent.change(screen.getByLabelText('Figures'), { target: { value: '5' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add Squad' }));

    expect(await screen.findByText(/joined blue\./)).toBeTruthy();
    const posted = addUnit.mock.calls[0][1];
    expect(posted.qualityDie).toBe(12);
    expect(posted.leadershipValue).toBe(4);
    expect(posted.figures).toEqual(Array.from({ length: 5 }, () => ({ armourDie: 4 })));
    expect((posted.weapons as { impactDie: number }[])[0].impactDie).toBe(10);
  });

  it('offers the Leadership Values this game entered, and no others', async () => {
    await open(wholeUnit);

    const offered = [...die('Leadership').options].map((option) => option.value);

    // The entered set, and the unentered option every control on this panel carries.
    expect(offered).toEqual(['0', '2', '3', '4', '5']);
    // The three the panel used to ship, off a page this app does not have. 2 and 3 are here because
    // this table entered them, and 1 is the one that proves the difference.
    expect(offered).not.toContain('1');
  });

  it('offers no Leadership Value at all when the game was never told what they are', async () => {
    // A game whose profile says nothing about them - which every game stored before the entry
    // existed reads back as. The panel offers none rather than falling back on three of its own,
    // and says why, which is the refusal the server would give arriving before the player types.
    await open(wholeUnit, '"profile": { "bandWidths": [], "rangeDice": [] },');

    const leadership = die('Leadership');
    expect([...leadership.options].map((option) => option.value)).toEqual(['0']);
    expect(leadership.disabled).toBe(true);
    expect(screen.getByText(/Nobody has entered which Leadership Values/)).toBeTruthy();
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

describe('StarGrunt status line after a shot', () => {
  // Fire was the one command with no sentence of its own, and run() only wrote the line when it was
  // given one - so a refused shot's alert stayed up, still marked as an alert, over the shot that
  // then went through. Dirtside had the same defect and was fixed on 2026-11-16.
  beforeEach(() => {
    localStorage.clear();
    readGame.mockReset();
    fire.mockReset();
  });

  afterEach(cleanup);

  function firingSnapshot(log: string[] = []): StarGruntSnapshot {
    const armed = wholeUnit.replace('"weaponLegality": []', '"weaponLegality": [{ "name": "Rifles", "canFire": true, "blocker": null }]');
    const snapshot = snapshotFrom(`${armed},${wholeUnit.replace('"id": "alpha", "name": "Alpha Squad", "side": "blue"', '"id": "bravo", "name": "Bravo Squad", "side": "red"')}`);
    return { ...snapshot, activatingUnitId: 'alpha', log };
  }

  function status() {
    return screen.getAllByText((_, node) => node?.getAttribute('aria-live') === 'polite' && node.tagName === 'P')[0];
  }

  it('shows what the shot logged, not the refusal before it', async () => {
    localStorage.setItem(starGruntGameKey, JSON.stringify(handle));
    readGame.mockResolvedValue(firingSnapshot());
    fire.mockRejectedValueOnce(new ApiRequestError('No range die was entered for that band.', 400));
    fire.mockResolvedValueOnce(firingSnapshot(['Alpha Squad fired on Bravo Squad: one down.']));

    render(<StarGruntView />);
    expect(await screen.findByText('Reopened Hill 43.')).toBeTruthy();

    fireEvent.click(screen.getByText('Fire', { selector: 'button' }));
    expect(await screen.findByText('No range die was entered for that band.')).toBeTruthy();

    fireEvent.click(screen.getByText('Fire', { selector: 'button' }));
    expect(await screen.findByText('Alpha Squad fired on Bravo Squad: one down.', { selector: 'p' })).toBeTruthy();
    expect(status().getAttribute('role')).toBeNull();
  });

  it('shows what the shot logged once the log is at its cap', async () => {
    // Five entries stand in for the server's 2000: what matters is a log that stays the same length
    // because the oldest entry was dropped for the new one, and a short one keeps the render cheap.
    const full = Array.from({ length: 5 }, (_, index) => `entry ${index}`);
    localStorage.setItem(starGruntGameKey, JSON.stringify(handle));
    readGame.mockResolvedValue(firingSnapshot(full));
    fire.mockResolvedValueOnce(firingSnapshot([...full.slice(1), 'Alpha Squad fired on Bravo Squad: one down.']));

    render(<StarGruntView />);
    expect(await screen.findByText('Reopened Hill 43.')).toBeTruthy();

    fireEvent.click(screen.getByText('Fire', { selector: 'button' }));
    expect(await screen.findByText('Alpha Squad fired on Bravo Squad: one down.', { selector: 'p' })).toBeTruthy();
  });

  it('shows the same sentence again when the shot logs it at the cap', async () => {
    // The newest line is the same before and after, so only where the log starts can tell.
    const full = [...Array.from({ length: 4 }, (_, index) => `entry ${index}`), 'Alpha Squad fired on Bravo Squad: one down.'];
    localStorage.setItem(starGruntGameKey, JSON.stringify(handle));
    readGame.mockResolvedValue({ ...firingSnapshot(full), logStart: 10 });
    fire.mockResolvedValueOnce({ ...firingSnapshot([...full.slice(1), 'Alpha Squad fired on Bravo Squad: one down.']), logStart: 11 });

    render(<StarGruntView />);
    expect(await screen.findByText('Reopened Hill 43.')).toBeTruthy();

    fireEvent.click(screen.getByText('Fire', { selector: 'button' }));
    expect(await screen.findByText('Alpha Squad fired on Bravo Squad: one down.', { selector: 'p' })).toBeTruthy();
  });
});

describe('StarGrunt log', () => {
  beforeEach(() => {
    localStorage.clear();
    readGame.mockReset();
  });

  afterEach(cleanup);

  it('draws only the newest 300 lines of a long log, and says so', async () => {
    const log = Array.from({ length: 350 }, (_, index) => `entry ${index}`);
    localStorage.setItem(starGruntGameKey, JSON.stringify(handle));
    readGame.mockResolvedValue({ ...snapshotFrom(wholeUnit), log });

    const { container } = render(<StarGruntView />);
    expect(await screen.findByText('Reopened Hill 43.')).toBeTruthy();

    const lines = container.querySelectorAll('[aria-label="StarGrunt log"] li');
    expect(lines.length).toBe(300);
    expect(lines[0].textContent).toBe('entry 50');
    expect(screen.getByText('Showing the most recent 300 of 350 lines.')).toBeTruthy();
  });
});

describe('StarGrunt unit cards are told apart', () => {
  // Every card carried buttons named only "Activate", "Confidence Test" and "Scattered", so a screen
  // reader - or a test, or a voice command - heard the same three names once per unit. Dirtside's
  // per-element buttons were fixed for the same thing.
  beforeEach(() => {
    localStorage.clear();
    readGame.mockReset();
  });

  afterEach(cleanup);

  it('names the unit in every button on its card', async () => {
    await open(`${wholeUnit},${wholeUnit.replace('"id": "alpha", "name": "Alpha Squad", "side": "blue"', '"id": "bravo", "name": "Bravo Squad", "side": "red"')}`);

    for (const name of ['Alpha Squad', 'Bravo Squad']) {
      expect(screen.getByRole('button', { name: `Activate ${name}` })).toBeTruthy();
      expect(screen.getByRole('button', { name: `Confidence test for ${name}` })).toBeTruthy();
      expect(screen.getByRole('button', { name: `Mark ${name} scattered` })).toBeTruthy();
    }
  });
});

describe('StarGrunt targets (playtest M2)', () => {
  // Both red squads shot their own side's unit: it came first in the roster, so it was the default,
  // and the options did not say whose anything was.
  beforeEach(() => {
    localStorage.clear();
    readGame.mockReset();
    fire.mockReset();
  });

  afterEach(cleanup);

  function redActivating(): StarGruntSnapshot {
    const armed = wholeUnit
      .replace('"id": "alpha", "name": "Alpha Squad", "side": "blue"', '"id": "red-a", "name": "Red Alpha", "side": "red"')
      .replace('"weaponLegality": []', '"weaponLegality": [{ "name": "Rifles", "canFire": true, "blocker": null }]');
    const friend = wholeUnit.replace('"id": "alpha", "name": "Alpha Squad", "side": "blue"', '"id": "red-b", "name": "Red Bravo", "side": "red"');
    const enemy = wholeUnit.replace('"id": "alpha", "name": "Alpha Squad", "side": "blue"', '"id": "blue-a", "name": "Blue Alpha", "side": "blue"');
    return { ...snapshotFrom(`${armed},${friend},${enemy}`), activatingUnitId: 'red-a' };
  }

  async function openRed() {
    localStorage.setItem(starGruntGameKey, JSON.stringify(handle));
    readGame.mockResolvedValue(redActivating());
    render(<StarGruntView />);
    expect(await screen.findByText('Reopened Hill 43.')).toBeTruthy();
  }

  it('offers the enemy first, says whose each unit is, and fires on the enemy by default', async () => {
    await openRed();
    fire.mockResolvedValue(redActivating());

    const options = [...screen.getByLabelText('Red Alpha fire').querySelectorAll('select')[0].querySelectorAll('option')].map((option) => option.textContent);
    expect(options[0]).toContain('Blue Alpha (blue)');
    expect(options[1]).toContain('Red Bravo (red) · own side');

    fireEvent.click(screen.getByText('Fire', { selector: 'button' }));
    await waitFor(() => expect(fire).toHaveBeenCalled());
    expect(fire.mock.calls[0][1]).toMatchObject({ targetId: 'blue-a' });
  });

  it('asks before firing on its own side, and fires only on a yes', async () => {
    await openRed();
    fire.mockResolvedValue(redActivating());
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
    const targetSelect = screen.getByLabelText('Red Alpha fire').querySelectorAll('select')[0];
    fireEvent.change(targetSelect, { target: { value: 'red-b' } });

    fireEvent.click(screen.getByText('Fire', { selector: 'button' }));
    expect(confirm.mock.calls[0][0]).toContain("Red Bravo is on red, Red Alpha's own side");
    expect(fire).not.toHaveBeenCalled();

    fireEvent.click(screen.getByText('Fire', { selector: 'button' }));
    await waitFor(() => expect(fire).toHaveBeenCalledTimes(1));
    expect(fire.mock.calls[0][1]).toMatchObject({ targetId: 'red-b' });
    confirm.mockRestore();
  });
});

describe('StarGrunt confidence test (playtest M8)', () => {
  // "Not entered" rolled as threat 0 and dropped a unit two rungs to Shaken.
  beforeEach(() => {
    localStorage.clear();
    readGame.mockReset();
    confidenceTest.mockReset();
  });

  afterEach(cleanup);

  it('refuses to test with no threat level picked, and tests at the one picked', async () => {
    await open(wholeUnit);
    confidenceTest.mockResolvedValue(snapshotFrom(wholeUnit));

    fireEvent.click(screen.getByRole('button', { name: 'Confidence test for Alpha Squad' }));
    expect(await screen.findByText('Pick a threat level first: the one your own table gives the event.')).toBeTruthy();
    expect(confidenceTest).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText('Threat level'), { target: { value: '2' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confidence test for Alpha Squad' }));
    await waitFor(() => expect(confidenceTest).toHaveBeenCalledWith(handle, 'alpha', 2));
  });
});

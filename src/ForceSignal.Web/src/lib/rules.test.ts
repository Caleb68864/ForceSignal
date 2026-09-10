import { describe, expect, it } from 'vitest';
import { buildPreTurnChecklist, captureDamageState, describeArcs, fighterFlightRefusal, firingDraftFor, firingTargetOptions, focusedFirstShips, isFighterGroup, partiesPerJobCap } from './rules.ts';
import { blankRulesProfile, type MatchSnapshot, type RulesProfile, type Ship, type WeaponMount } from '../types.ts';

/**
 * The server re-checks all of this. What these guard is the screen's own reading of the board:
 * which target a console defaults to, what the checklist says before the button is pressed, and
 * what the undo slot captures - so a wrong answer here is a wrong reason shown to a player.
 */
function weapon(overrides: Partial<WeaponMount> = {}): WeaponMount {
  return { id: 'w1', name: 'Beam', attackDice: 2, maxRange: 24, arcs: ['Fore'], kind: 'Beam', ammoMax: 0, ammoUsed: 0, reloadTurns: 0, ...overrides };
}

function ship(overrides: Partial<Ship> = {}): Ship {
  return {
    id: 'a',
    fleetId: 'blue',
    name: 'Alpha',
    className: 'Cruiser',
    thrustRating: 4,
    currentVelocity: 8,
    currentCourse: 1,
    positionX: 10,
    positionY: 10,
    hullMax: 12,
    hullDamage: 0,
    armorMax: 4,
    armorDamage: 0,
    fireControlMax: 2,
    fireControlDamage: 0,
    driveDamage: 0,
    weaponDamage: 0,
    screenRating: 1,
    weapons: [weapon()],
    isDestroyed: false,
    iconKey: 'cruiser',
    fighterEnduranceMax: 0,
    fighterEnduranceUsed: 0,
    fighterMaxRange: 0,
    fighterStatus: 'Docked',
    pointsValue: 100,
    effectiveScreens: 1,
    workingFireControl: 2,
    fighterReach: 0,
    repairableSystems: [],
    ...overrides,
  };
}

function snapshot(overrides: Partial<MatchSnapshot> = {}): MatchSnapshot {
  return {
    matchId: 'm',
    joinCode: 'ABC',
    name: 'Test',
    phase: 'OrderEntry',
    turnNumber: 1,
    rulesProfileKey: 'x',
    tableWidth: 72,
    tableDepth: 48,
    participants: [{ id: 'p1', displayName: 'Blue', role: 'Owner', isReady: true, isConnected: true }],
    fleets: [{ id: 'blue', ownerParticipantId: 'p1', name: 'Blue', fleetColor: '#47f1ff' }],
    ships: [],
    orderStatuses: [],
    revealedOrders: [],
    movementResults: [],
    firingResults: [],
    ordnanceMarkers: [],
    matchLog: [],
    version: 1,
    pointsLimit: 0,
    ...overrides,
  };
}

describe('describeArcs', () => {
  it('collapses the five firable arcs into all round', () => {
    expect(describeArcs(['Fore', 'ForeStarboard', 'AftStarboard', 'AftPort', 'ForePort'])).toBe('all round');
  });

  it('abbreviates anything narrower', () => {
    expect(describeArcs(['Fore', 'ForePort'])).toBe('F/FP');
    expect(describeArcs([])).toBe('no arc');
  });
});

describe('isFighterGroup', () => {
  it('reads the icon first and the class name second', () => {
    expect(isFighterGroup(ship({ iconKey: 'fighter-group', className: 'Cruiser' }))).toBe(true);
    expect(isFighterGroup(ship({ iconKey: 'cruiser', className: 'Fighter Wing' }))).toBe(true);
    expect(isFighterGroup(ship())).toBe(false);
  });
});

describe('firingTargetOptions', () => {
  const me = ship({ id: 'me', positionX: 0, positionY: 0 });
  const near = ship({ id: 'near', fleetId: 'red', positionX: 6, positionY: 0 });
  const far = ship({ id: 'far', fleetId: 'red', positionX: 30, positionY: 0 });
  const friend = ship({ id: 'friend', positionX: 1, positionY: 0 });
  const dead = ship({ id: 'dead', fleetId: 'red', positionX: 2, positionY: 0, isDestroyed: true });
  const fighters = ship({ id: 'fighters', fleetId: 'red', iconKey: 'fighter-group', positionX: 3, positionY: 0 });

  it('offers hostiles nearest first, friends last, and never the dead', () => {
    const options = firingTargetOptions(me, [me, far, friend, near, dead], new Set(['me', 'friend']));

    expect(options.map((candidate) => candidate.id)).toEqual(['near', 'far', 'friend']);
  });

  // Main batteries cannot engage fighters; point defence answers a strike when it comes in.
  it('hides fighter groups from a warship but not from other fighters', () => {
    expect(firingTargetOptions(me, [me, fighters]).map((candidate) => candidate.id)).toEqual([]);
    const wing = ship({ id: 'wing', iconKey: 'fighter-group', positionX: 0, positionY: 0 });
    expect(firingTargetOptions(wing, [wing, fighters]).map((candidate) => candidate.id)).toEqual(['fighters']);
  });
});

describe('firingDraftFor', () => {
  const me = ship({ id: 'me', positionX: 0, positionY: 0, weapons: [weapon({ id: 'beam' }), weapon({ id: 'needle', kind: 'NeedleBeam' })] });
  const target = ship({ id: 'target', fleetId: 'red', positionX: 9, positionY: 12 });

  it('defaults to the nearest hostile at the measured range, with the first mount', () => {
    const draft = firingDraftFor(me, [me, target], {});

    expect(draft).toEqual({ targetShipId: 'target', weaponId: 'beam', range: 15, targetSystem: undefined, targetSystemWeaponId: undefined });
  });

  it('keeps a chosen target, weapon and range', () => {
    const draft = firingDraftFor(me, [me, target], { me: { targetShipId: 'target', weaponId: 'needle', range: 7 } });

    expect(draft.weaponId).toBe('needle');
    expect(draft.range).toBe(7);
  });

  it('drops a target that has been destroyed since it was chosen', () => {
    const gone = ship({ ...target, isDestroyed: true });
    const other = ship({ id: 'other', fleetId: 'red', positionX: 20, positionY: 0 });

    expect(firingDraftFor(me, [me, gone, other], { me: { targetShipId: 'target', weaponId: 'beam', range: 15 } }).targetShipId).toBe('other');
  });

  it('never offers a range below one', () => {
    expect(firingDraftFor(me, [me, target], { me: { targetShipId: 'target', weaponId: 'beam', range: 0 } }).range).toBe(1);
  });

  // A nomination means something only for a needle aimed at the ship it was named against.
  it('keeps a system nomination only while the needle and its target stand', () => {
    const named = { targetShipId: 'target', weaponId: 'needle', range: 7, targetSystem: 'Weapon', targetSystemWeaponId: 'w1' };

    expect(firingDraftFor(me, [me, target], { me: named }).targetSystem).toBe('Weapon');
    expect(firingDraftFor(me, [me, target], { me: { ...named, weaponId: 'beam' } }).targetSystem).toBeUndefined();
  });
});

describe('focusedFirstShips', () => {
  const ships = [ship({ id: 'a' }), ship({ id: 'b' }), ship({ id: 'c' })];

  it('moves the focused ship to the front and leaves the rest in order', () => {
    expect(focusedFirstShips(ships, 'c').map((item) => item.id)).toEqual(['c', 'a', 'b']);
  });

  it('falls back to the given ship, and otherwise changes nothing', () => {
    expect(focusedFirstShips(ships, null, 'b').map((item) => item.id)).toEqual(['b', 'a', 'c']);
    expect(focusedFirstShips(ships, null)).toBe(ships);
  });
});

describe('captureDamageState', () => {
  it('takes exactly the five tracks the undo slot restores', () => {
    const captured = captureDamageState(ship({ hullDamage: 3, armorDamage: 1, fireControlDamage: 1, driveDamage: 2, weaponDamage: 1 }));

    expect(captured).toEqual({ hullDamage: 3, armorDamage: 1, fireControlDamage: 1, driveDamage: 2, weaponDamage: 1 });
  });
});

describe('buildPreTurnChecklist', () => {
  const find = (items: ReturnType<typeof buildPreTurnChecklist>, id: string) => items.find((item) => item.id === id);

  it('blocks on unlocked friendly orders and only warns on the opponent', () => {
    const board = snapshot({ ships: [ship({ id: 'mine' }), ship({ id: 'theirs', fleetId: 'red' })] });

    expect(find(buildPreTurnChecklist(board, new Set(['mine'])), 'orders')?.severity).toBe('blocker');
    expect(find(buildPreTurnChecklist(board, new Set()), 'orders')?.severity).toBe('warning');
  });

  it('is clear once every live ship has locked', () => {
    const board = snapshot({
      ships: [ship({ id: 'mine' }), ship({ id: 'wreck', isDestroyed: true })],
      orderStatuses: [{ shipId: 'mine', ownerParticipantId: 'p1', isCommitted: true, isRevealed: false, verificationFailed: false }],
    });

    expect(find(buildPreTurnChecklist(board, new Set(['mine'])), 'orders')?.severity).toBe('ok');
  });

  it('blocks movement on an unrevealed locked order', () => {
    const board = snapshot({
      phase: 'Movement',
      ships: [ship({ id: 'mine' })],
      orderStatuses: [{ shipId: 'mine', ownerParticipantId: 'p1', isCommitted: true, isRevealed: false, verificationFailed: false }],
    });

    expect(find(buildPreTurnChecklist(board, new Set(['mine'])), 'reveals')?.severity).toBe('blocker');
  });

  it('counts friendly weapons that have not fired', () => {
    const board = snapshot({
      phase: 'Firing',
      ships: [ship({ id: 'mine', weapons: [weapon({ id: 'w1' }), weapon({ id: 'w2' })] })],
      firingResults: [{ attackerShipId: 'mine', targetShipId: 'x', weaponId: 'w1', weaponName: 'Beam', turnNumber: 1, range: 10, rangeBand: 'x', arc: 'Fore', rawDice: 2, rangePenalty: 0, screenReduction: 0, systemPenalty: 0, damage: 1, armorDamageApplied: 0, hullDamageApplied: 1, diceRolls: [4, 5] }],
    });

    expect(find(buildPreTurnChecklist(board, new Set(['mine'])), 'fire')?.text).toBe('1 friendly weapon not logged');
  });

  it('names the player who is over the points limit', () => {
    const board = snapshot({ pointsLimit: 150, ships: [ship({ id: 'a', pointsValue: 100 }), ship({ id: 'b', pointsValue: 100 })] });

    const item = find(buildPreTurnChecklist(board, new Set()), 'points');
    expect(item?.severity).toBe('blocker');
    expect(item?.text).toBe('Blue is 50 over the 150 point limit');
  });

  it('blocks on drives that are gone and fighters that are out of endurance', () => {
    const board = snapshot({
      ships: [
        ship({ id: 'dead-in-space', driveDamage: 4 }),
        ship({ id: 'wing', iconKey: 'fighter-group', fighterStatus: 'Airborne', fighterEnduranceMax: 6, fighterEnduranceUsed: 6 }),
      ],
    });

    const items = buildPreTurnChecklist(board, new Set());
    expect(find(items, 'dead-in-space')?.severity).toBe('blocker');
    expect(find(items, 'fighters')?.severity).toBe('blocker');
  });

  it('reports a ship that has drifted off the table', () => {
    const board = snapshot({ ships: [ship({ positionX: 80 })] });

    expect(find(buildPreTurnChecklist(board, new Set()), 'positions')?.text).toBe('1 live ship outside table bounds');
  });
});

describe('how far a fighter group may fly', () => {
  // The screen used to hold 12 as a constant and refuse against it. The distance a group flies is
  // the player's - RulesProfile.FighterMoveAllowance, entered in the profile editor and enforced by
  // the server - so a table playing 18 was refused at 12 by a number from nowhere, and a table
  // playing 6 was let past 6 and refused by the server with a different number.
  it('refuses only past the allowance the table entered', () => {
    expect(fighterFlightRefusal(20, 18)).toBeNull();
    expect(fighterFlightRefusal(20, 18.4)).toBeNull();
    expect(fighterFlightRefusal(6, 6)).toBeNull();
  });

  it('names the number the table entered when it refuses', () => {
    expect(fighterFlightRefusal(6, 11)).toBe('11 away, past the 6 a group can fly in a turn');
    expect(fighterFlightRefusal(18, 20.25)).toBe('20.3 away, past the 18 a group can fly in a turn');
  });

  it('says nothing at all when the table has not entered one', () => {
    // A profile that has not arrived, or a field left blank. Inventing a limit here is the whole
    // defect; the server is the authority and will answer.
    expect(fighterFlightRefusal(0, 500)).toBeNull();
    expect(fighterFlightRefusal(undefined, 500)).toBeNull();
  });
});

describe('how many damage control parties one job may hold', () => {
  // The panel capped this at a hardcoded 3 - `RulesProfile.maxPartiesPerJob`, and the player's - so
  // a table whose profile allows four was stopped at three by a number that appears nowhere in it.
  // The same shape as the fighter allowance above, and settled the same way.
  const rules = (maxPartiesPerJob: number): RulesProfile => ({ ...blankRulesProfile, maxPartiesPerJob });

  it('caps at the number the table entered', () => {
    expect(partiesPerJobCap(rules(4), 8)).toBe(4);
    expect(partiesPerJobCap(rules(2), 8)).toBe(2);
  });

  it('never offers more parties than the ship has aboard', () => {
    expect(partiesPerJobCap(rules(4), 1)).toBe(1);
  });

  it('caps at nothing but the ship itself when the table has not entered one', () => {
    expect(partiesPerJobCap(rules(0), 8)).toBe(8);
    expect(partiesPerJobCap(undefined, 8)).toBe(8);
  });
});

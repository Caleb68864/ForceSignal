/**
 * The rules questions the screen needs answered before it will let a player do something.
 *
 * The server is the authority on all of this - it re-checks everything and refuses what it
 * must. These exist so the reason is on screen before the button is pressed, rather than
 * arriving as an error afterwards, and so a control that cannot legally do anything can say
 * why instead of just failing.
 */

import { arcAbbreviations, arcLabels, firableArcs } from '../constants.ts';
import { distanceBetweenShips } from './geometry.ts';
import { normalizeOrdnanceStatus, normalizeShipIconKey } from './normalize.ts';
import type { DamageState, FiringArc, FiringDraft, FiringSolution, MatchSnapshot, RulesProfile, Ship, ShipForm } from '../types.ts';

export function arcLabel(arc: FiringArc) {
  return arcLabels[arc] ?? arc;
}
export function describeArcs(arcs: FiringArc[]) {
  if (arcs.length === 0) {
    return 'no arc';
  }

  return arcs.length === firableArcs.length
    ? 'all round'
    : arcs.map((arc) => arcAbbreviations[arc]).join('/');
}
// Arc bearing, firing turn order, the range cross-check, a torpedo's to-hit number and a needle's
// list of live systems all used to be worked out here as well as on the server. They are gone: the
// firing solution query answers all of them, from the same checks that decide whether the shot is
// actually allowed. Keeping a copy here meant keeping an incomplete one - it knew nothing about
// ammunition, spent mounts, fighter endurance or the fire control a needle beam claims - so the
// console offered shots the server then refused.
/**
 * Why a fighter group may not fly to a spot, or null when it may.
 *
 * How far a group flies is the player's number - `RulesProfile.fighterMoveAllowance`, typed into the
 * profile editor and enforced by the server. The screen used to hold a constant 12 and refuse
 * against that, so a table playing 18 was refused at 12 by a figure that appears nowhere in their
 * profile, and a table playing 6 was let past 6 here and then refused by the server quoting a
 * different one.
 *
 * An allowance of zero means the table has not entered one - the profile has not landed yet, or the
 * field is blank - and the honest answer to "how far may it fly?" is then nothing at all. Inventing
 * a limit is the defect; the server is the authority and will say.
 */
export function fighterFlightRefusal(allowance: number | undefined, distance: number): string | null {
  if (!allowance || allowance <= 0 || distance <= allowance) {
    return null;
  }

  return `${Number(distance.toFixed(1))} away, past the ${allowance} a group can fly in a turn`;
}

/**
 * The four sentences these screens used to state as fact.
 *
 * *"each rolls a die at fighters and missiles inside 6mu"*, *"one repairs on a 6, and up to three on
 * the same job need only 4 or better"*, *"takes a system on a 6"*, *"strikes the closest enemy
 * within 6"* - readings off somebody's published card, written into a tooltip and a caption where
 * the player cannot overwrite them, and flatly contradicting a profile that says otherwise. They are
 * worse than a default: a default is a number offered, and these were the app telling the table what
 * their rules say.
 *
 * The right pattern was already one line above the wrong one - `needs {toHitNumber}+ to hit` comes
 * off the wire - and `fighterFlightRefusal` above settles what to do when the profile is silent. A
 * number the profile does not carry is one nobody has entered, so each of these says the thing
 * without the number rather than filling one in. None of them decides anything: the server does.
 */
export function pointDefenceNote(rules?: RulesProfile): string {
  const reach = rules?.pointDefenseRange ?? 0;
  return 'Point defence turrets: each rolls a die at fighters and missiles '
    + (reach > 0 ? `inside ${reach}mu` : 'inside the reach your profile gives them')
    + ', and they carry their own fire control.';
}

/** How the odds on a repair job read, or null while the profile does not say. */
export function repairOddsNote(rules?: RulesProfile): string | null {
  const withOneParty = rules?.repairRollWithOneParty ?? 0;
  const perJob = rules?.maxPartiesPerJob ?? 0;
  const best = rules?.repairBestRoll ?? 0;
  if (withOneParty <= 0 || perJob <= 0 || best <= 0) {
    return null;
  }

  return `One party repairs on a ${withOneParty}; ${perJob} on one job need ${best} or better.`;
}

export function damageControlNote(rules?: RulesProfile): string {
  const odds = repairOddsNote(rules);
  const opening = 'Damage control parties. Between turns they roll to bring back systems lost to a threshold check';
  return odds ? `${opening}: ${odds.charAt(0).toLowerCase()}${odds.slice(1)}` : `${opening}, on the numbers in your profile.`;
}

/**
 * The attacker's own numbers, as the bearing readout states them.
 *
 * The firing solution carries three of these and the console showed one. `workingFireControl` was
 * rendered as "2 firecons" and `engagedTargetCount` and `targetScreens` - computed on the server in
 * the same breath, put on the wire, and declared on `FiringSolution` - were read by nothing in
 * either screen.
 *
 * The fire control pair is the one that costs a player something. A ship may direct one target per
 * working fire control system, and `PrepareShot` refuses past it in as many words: *"has 2 working
 * fire control systems and is already engaging X and Y"*. So the console was showing the capacity
 * and hiding the usage, and the only way to discover a firecon was spent was to pick a target and
 * be refused - which is the thing `IsValid`/`Errors` were wired for on the plotting side.
 *
 * Screens are the other half of judging a shot: what the target still generates after damage, which
 * is not the rating it was built with. It is said only once a target is picked, because with no
 * target the server sends zero and "unscreened" would be a statement about nobody.
 *
 * Lives here rather than in either screen because the bearing readout is written out twice, in
 * `ShipCard` and in `PlayMap`, and a third copy of a rules-shaped sentence is how this repository
 * has acquired every one of its drifts.
 */
export function firingReadoutNotes(solution: FiringSolution | null | undefined): string[] {
  if (!solution) {
    return [];
  }

  const firecons = solution.workingFireControl;
  const engaged = solution.engagedTargetCount;
  const notes = [
    `${firecons} firecon${firecons === 1 ? '' : 's'}`
    + (engaged > 0 ? `, ${engaged} engaged` : ''),
  ];

  if (solution.targetShipId) {
    notes.push(solution.targetScreens > 0
      ? `target screens ${solution.targetScreens}`
      : 'target unscreened');
  }

  return notes;
}

/** What a needle beam does to the system it is aimed at, or null while the profile does not say. */
export function needleSystemNote(rules?: RulesProfile): string | null {
  const roll = rules?.needleSystemKillRoll ?? 0;
  return roll > 0 ? `takes a system on a ${roll}` : null;
}

/** How close a salvo has to land, said only when the table has entered it. */
export function salvoStrikeNote(rules?: RulesProfile): string {
  const radius = rules?.salvoAttackRadius ?? 0;
  return radius > 0
    ? `after movement it strikes the closest enemy within ${radius}.`
    : 'after movement it strikes the closest enemy inside the attack radius on your profile.';
}

/**
 * How many parties may pile onto one repair job.
 *
 * Zero means the profile has not said, and the honest cap is then the ship's own party count: the
 * server holds the real limit and refuses past it. A hardcoded 3 was a rule this app does not own,
 * quietly stopping a table whose profile allows four.
 */
export function partiesPerJobCap(rules: RulesProfile | undefined, partiesAboard: number): number {
  const perJob = rules?.maxPartiesPerJob ?? 0;
  return perJob > 0 ? Math.min(perJob, partiesAboard) : partiesAboard;
}

export function isFighterGroup(ship: Pick<Ship, 'iconKey' | 'className'>) {
  return normalizeShipIconKey(ship.iconKey, ship.className) === 'fighter-group'
    || (ship.className ?? '').toLowerCase().includes('fighter');
}
export function isFighterGroupForm(form: Pick<ShipForm, 'iconKey' | 'className'>) {
  return normalizeShipIconKey(form.iconKey, form.className) === 'fighter-group'
    || form.className.toLowerCase().includes('fighter');
}
// The hull row split, screens and fire control still generating, a fighter group's reach, and the
// systems damage control could bring back all used to be worked out here. They are fields on the
// ship in the snapshot now: each is a small subtraction, but a small subtraction copied into the
// client is still a second place for the rule to live, and a screen rating is what a ship was
// built with rather than what it still generates.
export function firingDraftFor(ship: Ship, ships: Ship[], drafts: Record<string, FiringDraft>, ownedShipIds?: Set<string>): FiringDraft {
  const current = drafts[ship.id];
  const isTargetable = (candidate: Ship) => candidate.id !== ship.id && !candidate.isDestroyed;
  const target = ships.find((candidate) => candidate.id === current?.targetShipId && isTargetable(candidate))
    ?? firingTargetOptions(ship, ships, ownedShipIds)[0];
  const weapon = ship.weapons.find((mount) => mount.id === current?.weaponId) ?? ship.weapons[0];
  // A nomination only means anything for a needle aimed at the ship it was named against, so it is
  // dropped when either changes. Whether the target still *has* that system is not asked here: the
  // answer belongs to the server, which offers only live systems in the firing solution and refuses
  // a shot at a system that has gone - before it moves any state, so a stale nomination costs a
  // refusal rather than a wedged turn.
  const keepsNomination = weapon?.kind === 'NeedleBeam'
    && Boolean(current?.targetSystem)
    && current?.targetShipId === target?.id;
  return {
    targetShipId: target?.id ?? '',
    weaponId: weapon?.id ?? '',
    // Default to the measured distance to the resolved target. A fixed default would let one
    // click on Fire resolve an attack at a range the table geometry does not support.
    range: Math.max(1, current?.range ?? (target ? Math.round(distanceBetweenShips(ship, target)) : 12)),
    targetSystem: keepsNomination ? current?.targetSystem : undefined,
    targetSystemWeaponId: keepsNomination ? current?.targetSystemWeaponId : undefined,
  };
}
/// Selectable targets for a firing solution: hostile contacts first, nearest first.
/// Friendly hulls stay selectable for deliberate crossfire but are never the default.
export function firingTargetOptions(ship: Ship, ships: Ship[], ownedShipIds?: Set<string>): Ship[] {
  // Main batteries cannot engage fighters at all - point defence answers a strike when it comes in -
  // so a warship is not offered fighter groups as targets. Fighters may shoot at each other.
  const canEngageFighters = isFighterGroup(ship);
  return ships
    .filter((candidate) => candidate.id !== ship.id && !candidate.isDestroyed
      && (canEngageFighters || !isFighterGroup(candidate)))
    .map((candidate) => ({
      candidate,
      friendly: ownedShipIds?.has(candidate.id) ?? false,
      range: distanceBetweenShips(ship, candidate),
    }))
    .sort((left, right) => Number(left.friendly) - Number(right.friendly) || left.range - right.range)
    .map((entry) => entry.candidate);
}
export function focusedFirstShips(ships: Ship[], focusedShipId: string | null, fallbackShipId?: string) {
  const priorityShipId = focusedShipId ?? fallbackShipId;
  if (!priorityShipId) {
    return ships;
  }

  return [...ships].sort((left, right) => {
    if (left.id === priorityShipId) {
      return -1;
    }

    if (right.id === priorityShipId) {
      return 1;
    }

    return 0;
  });
}
export function captureDamageState(ship: Ship): DamageState {
  return {
    hullDamage: ship.hullDamage,
    armorDamage: ship.armorDamage,
    fireControlDamage: ship.fireControlDamage,
    driveDamage: ship.driveDamage,
    weaponDamage: ship.weaponDamage,
  };
}
export function buildPreTurnChecklist(snapshot: MatchSnapshot, ownedShipIds: Set<string>) {
  const items: { id: string; text: string; severity: 'ok' | 'warning' | 'blocker' }[] = [];
  const liveShips = snapshot.ships.filter((ship) => !ship.isDestroyed);
  const ownedLiveShips = liveShips.filter((ship) => ownedShipIds.has(ship.id));
  const statuses = new Map(snapshot.orderStatuses.map((status) => [status.shipId, status]));
  const missingPositions = liveShips.filter((ship) => ship.positionX < 0 || ship.positionY < 0 || ship.positionX > snapshot.tableWidth || ship.positionY > snapshot.tableDepth);

  if (snapshot.phase === 'OrderEntry') {
    const unlocked = liveShips.filter((ship) => !statuses.get(ship.id)?.isCommitted);
    const friendlyUnlocked = ownedLiveShips.filter((ship) => !statuses.get(ship.id)?.isCommitted);
    items.push({
      id: 'orders',
      text: unlocked.length === 0 ? 'All live ships have locked orders' : `${unlocked.length} live ship${unlocked.length === 1 ? '' : 's'} missing orders`,
      severity: unlocked.length === 0 ? 'ok' : friendlyUnlocked.length > 0 ? 'blocker' : 'warning',
    });
  }

  if (snapshot.phase === 'Movement') {
    const unrevealed = liveShips.filter((ship) => {
      const status = statuses.get(ship.id);
      return status?.isCommitted && !status.isRevealed;
    });
    items.push({
      id: 'reveals',
      text: unrevealed.length === 0 ? 'All locked orders revealed' : `${unrevealed.length} order${unrevealed.length === 1 ? '' : 's'} still unrevealed`,
      severity: unrevealed.length === 0 ? 'ok' : 'blocker',
    });
  }

  if (snapshot.phase === 'Firing') {
    const armed = ownedLiveShips.filter((ship) => ship.weapons.length > 0 && ship.weaponDamage < ship.weapons.length);
    const fired = new Set(snapshot.firingResults.map((result) => `${result.attackerShipId}:${result.weaponId}`));
    const availableShots = armed.flatMap((ship) => ship.weapons.map((weapon) => ({ ship, weapon }))).filter(({ ship, weapon }) => !fired.has(`${ship.id}:${weapon.id}`));
    items.push({
      id: 'fire',
      text: availableShots.length === 0 ? 'No friendly unfired weapons detected' : `${availableShots.length} friendly weapon${availableShots.length === 1 ? '' : 's'} not logged`,
      severity: availableShots.length === 0 ? 'ok' : 'warning',
    });
  }

  const destroyedWithOrders = snapshot.ships.filter((ship) => ship.isDestroyed && statuses.get(ship.id)?.isCommitted);
  if (destroyedWithOrders.length > 0) {
    items.push({ id: 'destroyed-orders', text: `${destroyedWithOrders.length} destroyed ship${destroyedWithOrders.length === 1 ? ' still has' : 's still have'} orders`, severity: 'warning' });
  }

  const destroyedFired = snapshot.firingResults.filter((result) => snapshot.ships.find((ship) => ship.id === result.attackerShipId)?.isDestroyed);
  if (destroyedFired.length > 0) {
    items.push({ id: 'destroyed-fire', text: `${destroyedFired.length} shot${destroyedFired.length === 1 ? '' : 's'} logged from destroyed ships`, severity: 'warning' });
  }

  const activeOrdnance = (snapshot.ordnanceMarkers ?? []).filter((marker) => normalizeOrdnanceStatus(marker.status) === 'Active');
  if (activeOrdnance.length > 0) {
    items.push({
      id: 'ordnance',
      text: `${activeOrdnance.length} active ordnance marker${activeOrdnance.length === 1 ? ' needs' : 's need'} movement/resolution checks`,
      severity: 'warning',
    });
  }

  const fighterTrouble = liveShips.filter((ship) => isFighterGroup(ship) && ship.fighterStatus !== 'Docked' && ship.fighterEnduranceUsed >= ship.fighterEnduranceMax);
  if (fighterTrouble.length > 0) {
    items.push({
      id: 'fighters',
      text: `${fighterTrouble.length} fighter group${fighterTrouble.length === 1 ? '' : 's'} at endurance limit`,
      severity: 'blocker',
    });
  }

  const carrierOps = liveShips.filter((ship) => normalizeShipIconKey(ship.iconKey, ship.className) === 'carrier');
  const airborneFighters = liveShips.filter((ship) => isFighterGroup(ship) && ship.fighterStatus !== 'Docked');
  if (carrierOps.length > 0 && airborneFighters.length > 0) {
    items.push({
      id: 'carrier-ops',
      text: `${airborneFighters.length} airborne/recovering fighter group${airborneFighters.length === 1 ? '' : 's'} to reconcile with carriers`,
      severity: 'warning',
    });
  }

  if ((snapshot.pointsLimit ?? 0) > 0) {
    const overStrength = snapshot.participants
      .map((participant) => {
        const fleetIds = new Set(snapshot.fleets.filter((fleet) => fleet.ownerParticipantId === participant.id).map((fleet) => fleet.id));
        const total = snapshot.ships.filter((ship) => fleetIds.has(ship.fleetId)).reduce((sum, ship) => sum + (ship.pointsValue ?? 0), 0);
        return { name: participant.displayName, over: total - snapshot.pointsLimit };
      })
      .filter((entry) => entry.over > 0);
    items.push({
      id: 'points',
      text: overStrength.length === 0
        ? `All fleets inside the ${snapshot.pointsLimit} point limit`
        : overStrength.map((entry) => `${entry.name} is ${entry.over} over the ${snapshot.pointsLimit} point limit`).join('; '),
      severity: overStrength.length === 0 ? 'ok' : 'blocker',
    });
  }

  // Full Thrust has no crippled state: a ship fights at full effect until a threshold check takes
  // its systems, and dies when the last hull box goes. Half hull is a ForceSignal watch list, so it
  // says so rather than reading like a rule.
  const halfHull = liveShips.filter((ship) => ship.hullDamage >= Math.ceil(ship.hullMax / 2));
  if (halfHull.length > 0) {
    items.push({
      id: 'half-hull',
      text: `${halfHull.length} ship${halfHull.length === 1 ? '' : 's'} at or past half hull (watch list, not a rule)`,
      severity: 'warning',
    });
  }

  const deadInSpace = liveShips.filter((ship) => ship.thrustRating > 0 && ship.driveDamage >= ship.thrustRating);
  if (deadInSpace.length > 0) {
    items.push({
      id: 'dead-in-space',
      text: `${deadInSpace.length} ship${deadInSpace.length === 1 ? '' : 's'} with drives disabled`,
      severity: 'blocker',
    });
  }

  items.push({
    id: 'positions',
    text: missingPositions.length === 0 ? 'All live ships are inside table bounds' : `${missingPositions.length} live ship${missingPositions.length === 1 ? '' : 's'} outside table bounds`,
    severity: missingPositions.length === 0 ? 'ok' : 'warning',
  });

  return items;
}

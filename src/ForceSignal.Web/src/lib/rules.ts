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
import type { DamageState, FiringArc, FiringDraft, MatchSnapshot, Ship, ShipForm } from '../types.ts';

/// The hull damage track as boxes per row: four rows, remainder weighted to the upper rows.
export function hullRowsFor(hullMax: number): number[] {
  if (hullMax <= 0) {
    return [];
  }

  if (hullMax < 4) {
    return Array.from({ length: hullMax }, () => 1);
  }

  const baseSize = Math.floor(hullMax / 4);
  const remainder = hullMax % 4;
  return Array.from({ length: 4 }, (_, index) => baseSize + (index < remainder ? 1 : 0));
}
export function hullRowsOf(ship: Ship): number[] {
  return ship.hullRows && ship.hullRows.length > 0 ? ship.hullRows : hullRowsFor(ship.hullMax);
}
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
/// Screen levels still generating, after whatever has been shot away. The rating a ship carries is
/// what it was built with, so anything shown to the table has to subtract the damage.
export function effectiveScreens(ship: Ship) {
  return Math.max(0, ship.screenRating - (ship.screenDamage ?? 0));
}
/// Fire control systems still working. Each one holds a single target ship for the turn.
export function workingFireControl(ship: Ship) {
  return Math.max(0, (ship.fireControlMax ?? 1) - ship.fireControlDamage);
}
export function isFighterGroup(ship: Pick<Ship, 'iconKey' | 'className'>) {
  return normalizeShipIconKey(ship.iconKey, ship.className) === 'fighter-group'
    || (ship.className ?? '').toLowerCase().includes('fighter');
}
export function isFighterGroupForm(form: Pick<ShipForm, 'iconKey' | 'className'>) {
  return normalizeShipIconKey(form.iconKey, form.className) === 'fighter-group'
    || form.className.toLowerCase().includes('fighter');
}
export function fighterEnduranceRange(ship: Ship) {
  const remaining = Math.max(0, ship.fighterEnduranceMax - ship.fighterEnduranceUsed);
  const velocityReach = Math.max(1, ship.currentVelocity) * Math.max(1, remaining);
  const maxRange = ship.fighterMaxRange || 24;
  return Math.max(1, Math.min(maxRange, velocityReach));
}
/// Systems on a ship that damage control could actually bring back. Hull damage and dead parties are
/// never on the list, and neither is anything a needle beam cut out.
export function repairableSystems(ship: Ship): { key: string; label: string; kind: string; weaponId?: string }[] {
  const jobs: { key: string; label: string; kind: string; weaponId?: string }[] = [];
  if (ship.fireControlDamage > 0) {
    jobs.push({ key: 'firecon', label: 'Fire control', kind: 'FireControl' });
  }

  if (ship.driveDamage > 0) {
    jobs.push({ key: 'drive', label: 'Drives', kind: 'Drive' });
  }

  if ((ship.screenDamage ?? 0) > 0) {
    jobs.push({ key: 'screen', label: 'Screen generator', kind: 'Screen' });
  }

  if ((ship.fighterBayDamage ?? 0) > 0) {
    jobs.push({ key: 'bay', label: 'Fighter bay', kind: 'FighterBay' });
  }

  for (const mount of ship.weapons) {
    if (mount.isDestroyed && !mount.isNeedleKilled) {
      jobs.push({ key: `mount-${mount.id}`, label: mount.name, kind: 'Weapon', weaponId: mount.id });
    }
  }

  return jobs;
}
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

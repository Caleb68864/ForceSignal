/**
 * Every field the server sends has a home in `types.ts`.
 *
 * The client's types are a hand-kept copy of the C# contracts, and a copy falls behind quietly: a
 * field the server added is simply invisible to the type checker, so nothing reads it and nothing
 * says so. The snapshot the application tests captured from the real service is held up against
 * the declared keys below.
 *
 * Each key list is typed `Record<keyof T, true>`, so the compiler refuses a list that leaves a
 * declared field out or names one the type does not have; the test then refuses a wire field that is
 * not on the list. Between them the copy cannot drift in either direction without something failing.
 */

import { describe, expect, it } from 'vitest';
import wire from './playback/fixtures/05-turn1-firing-complete.json' with { type: 'json' };
import type { FiringResult, Fleet, LastTurn, MatchLogEntry, MatchSnapshot, MovementResult, OrderStatus, OrdnanceMarker, Participant, RevealedOrder, Ship, TurnEvent, WeaponMount } from '../types.ts';
import nextTurn from './playback/fixtures/06-turn2-order-entry.json' with { type: 'json' };

type Keys<T> = Record<keyof T, true>;

const snapshotKeys: Keys<MatchSnapshot> = {
  matchId: true, joinCode: true, name: true, phase: true, turnNumber: true, rulesProfileKey: true, tableWidth: true, tableDepth: true,
  participants: true, fleets: true, ships: true, orderStatuses: true, revealedOrders: true, movementResults: true, firingResults: true,
  ordnanceMarkers: true, matchLog: true, version: true, pointsLimit: true, rules: true, firingShipId: true, firingParticipantId: true,
  activatedShipIds: true, turnEvents: true, lastTurn: true,
};

const shipKeys: Keys<Ship> = {
  id: true, fleetId: true, name: true, className: true, thrustRating: true, currentVelocity: true, currentCourse: true,
  positionX: true, positionY: true, hullMax: true, hullDamage: true, armorMax: true, armorDamage: true, fireControlMax: true,
  fireControlDamage: true, pointDefenseSystems: true, fighterBays: true, fighterBayDamage: true, damageControlParties: true,
  driveDamage: true, weaponDamage: true, screenRating: true, screenDamage: true, weapons: true, isDestroyed: true, hullRows: true,
  hullRowsCompleted: true, iconKey: true, fighterEnduranceMax: true, fighterEnduranceUsed: true, fighterMaxRange: true,
  fighterStatus: true, homeCarrierShipId: true, pointsValue: true, effectiveScreens: true, workingFireControl: true,
  fighterReach: true, repairableSystems: true, needledFireControl: true, needledDrives: true, needledScreens: true,
  needledBays: true, fighterRelaunchTurn: true, fighterGroundedForGame: true,
};

const weaponKeys: Keys<WeaponMount> = {
  id: true, name: true, attackDice: true, maxRange: true, arcs: true, ammoMax: true, ammoUsed: true, reloadTurns: true,
  isDestroyed: true, isNeedleKilled: true, kind: true,
};

const firingKeys: Keys<FiringResult> = {
  attackerShipId: true, targetShipId: true, weaponId: true, weaponName: true, turnNumber: true, range: true, rangeBand: true,
  arc: true, rawDice: true, rangePenalty: true, screenReduction: true, systemPenalty: true, damage: true, armorDamageApplied: true,
  hullDamageApplied: true, diceRolls: true, weaponKind: true, toHitNumber: true, isHit: true, mapRange: true, rangeDisagreed: true,
};

const movementKeys: Keys<MovementResult> = {
  shipId: true, startingVelocity: true, startingCourse: true, endingVelocity: true, endingCourse: true, segments: true,
  path: true, clamped: true, drifted: true,
};

const turnEventKeys: Keys<TurnEvent> = {
  sequence: true, turnNumber: true, kind: true, actorShipId: true, targetShipId: true, markerId: true, firingResultIndex: true,
  from: true, to: true, rolls: true, kills: true, launched: true, arriving: true, surviving: true, damageRolls: true, damage: true,
  armorApplied: true, hullApplied: true, row: true, lostSystems: true,
};
const lastTurnKeys: Keys<LastTurn> = { turnNumber: true, movementResults: true, firingResults: true, events: true };

const fleetKeys: Keys<Fleet> = { id: true, ownerParticipantId: true, name: true, faction: true, fleetColor: true };
const participantKeys: Keys<Participant> = { id: true, displayName: true, role: true, isReady: true, isConnected: true, ordersComplete: true };
const orderStatusKeys: Keys<OrderStatus> = { shipId: true, ownerParticipantId: true, isCommitted: true, isRevealed: true, verificationFailed: true };
const revealedKeys: Keys<RevealedOrder> = { shipId: true, velocityDelta: true, turnSteps: true, turnDirection: true, turnManeuvers: true };
const logKeys: Keys<MatchLogEntry> = { sequence: true, timestamp: true, turnNumber: true, phase: true, category: true, message: true };
const markerKeys: Keys<OrdnanceMarker> = {
  id: true, ownerParticipantId: true, name: true, markerType: true, sourceShipId: true, targetShipId: true, positionX: true,
  positionY: true, course: true, speed: true, enduranceRemaining: true, attackDice: true, maxRange: true, status: true,
};

/** Wire keys an object carries that its declared list does not. */
function undeclared(objects: unknown[], declared: Record<string, true>): string[] {
  const missing = new Set<string>();
  for (const object of objects) {
    for (const key of Object.keys(object as object)) {
      if (!(key in declared)) {
        missing.add(key);
      }
    }
  }

  return [...missing].sort();
}

describe('the wire snapshot against types.ts', () => {
  const snapshot = wire as unknown as Record<string, unknown[]>;

  it('declares every field of every object the server sent', () => {
    // Reached-the-subject: the capture really has every kind of object in it.
    for (const list of ['ships', 'fleets', 'participants', 'orderStatuses', 'revealedOrders', 'movementResults', 'firingResults', 'ordnanceMarkers', 'matchLog', 'turnEvents']) {
      expect(snapshot[list]?.length, list).toBeGreaterThan(0);
    }

    expect({
      snapshot: undeclared([wire], snapshotKeys),
      ship: undeclared(snapshot.ships, shipKeys),
      weapon: undeclared(snapshot.ships.flatMap((ship) => (ship as Ship).weapons), weaponKeys),
      firingResult: undeclared(snapshot.firingResults, firingKeys),
      movementResult: undeclared(snapshot.movementResults, movementKeys),
      fleet: undeclared(snapshot.fleets, fleetKeys),
      participant: undeclared(snapshot.participants, participantKeys),
      orderStatus: undeclared(snapshot.orderStatuses, orderStatusKeys),
      revealedOrder: undeclared(snapshot.revealedOrders, revealedKeys),
      ordnanceMarker: undeclared(snapshot.ordnanceMarkers, markerKeys),
      logEntry: undeclared(snapshot.matchLog, logKeys),
      turnEvent: undeclared(snapshot.turnEvents, turnEventKeys),
      lastTurn: undeclared([nextTurn.lastTurn], lastTurnKeys),
      lastTurnEvent: undeclared(nextTurn.lastTurn.events, turnEventKeys),
    }).toEqual({
      snapshot: [], ship: [], weapon: [], firingResult: [], movementResult: [], fleet: [], participant: [],
      orderStatus: [], revealedOrder: [], ordnanceMarker: [], logEntry: [], turnEvent: [], lastTurn: [], lastTurnEvent: [],
    });
  });

  it('allows null where the server sends null', () => {
    // Fleet.faction arrives as null when nobody named one, not as a missing key.
    const fleet: Fleet = { id: 'f', ownerParticipantId: 'p', name: 'Blue', faction: null, fleetColor: '#000000' };
    expect(fleet.faction).toBeNull();
  });
});

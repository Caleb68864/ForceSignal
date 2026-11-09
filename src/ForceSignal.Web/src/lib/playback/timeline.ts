/**
 * One resolved turn, as a list of things to show in the order the server did them.
 *
 * This is the only place playback learns what happened, and it learns it by reading the snapshot -
 * never by working anything out. Every firing event carries the server's own `FiringResult` object
 * rather than a copy of the fields someone thought the effects would need, so an effect can only
 * ever show a number the server sent. Movement is the server's path as flown - drifting ships
 * included, edge-clamped hulls from their real start - through `trailPointsForResult`, the same
 * points the resolved-trail overlay draws.
 *
 * Pure and deterministic: the same snapshot gives the same timeline on every device, because the
 * order is the order of the arrays the server sent and nothing here reads a clock or a die.
 *
 * What it cannot show yet is listed in docs/visuals/findings-2026-11-09.md: salvo strikes, point
 * defence and threshold checks reach the client only as log sentences. The tests pin that as a
 * known failure, so the day the server sends them the tests say so.
 */

import { trailPointsForResult } from '../mapGeometry.ts';
import { normalizeShipIconKey } from '../normalize.ts';
import type { FiringResult, MatchSnapshot, MovementSegment, Ship, TablePoint, WeaponKind } from '../../types.ts';

/** How an attack travels and lands. A fighter strike is reported as a beam, so it is told apart by the attacker. */
export type FxKey = WeaponKind | 'FighterStrike' | 'Unknown';

export type MoveLeg = { from: TablePoint; to: TablePoint; course: number };

export type MoveEvent = {
  kind: 'move';
  /** Every move in a turn is simultaneous, so they all share the first step. */
  step: 0;
  shipId: string;
  startCourse: number;
  endCourse: number;
  legs: MoveLeg[];
};

export type FireEvent = {
  kind: 'fire';
  /** One-based, in the order the server resolved the shots. */
  step: number;
  /** Where the result sits in `snapshot.firingResults`. */
  resultIndex: number;
  /** The server's record, untouched. */
  result: FiringResult;
  fx: FxKey;
  from: TablePoint;
  to: TablePoint;
  /**
   * True on the last shot that put hull damage on a ship the snapshot now calls destroyed.
   *
   * An attribution, not a verdict: `isDestroyed` is the server's, and this only says which of its
   * shots to hang the explosion on. A ship finished by a salvo or a threshold check has no such shot
   * and gets no explosion, which is one of the gaps a server-side event list closes.
   */
  destroysTarget: boolean;
};

export type TimelineEvent = MoveEvent | FireEvent;

export type TimelineStatus =
  /** The turn's moves are flown and its shots so far are listed. */
  | 'resolved'
  /** Orders are revealed but the ships have not moved: the results exist, the positions do not yet match them. */
  | 'awaiting-movement'
  /** Nothing has resolved this turn yet. */
  | 'empty'
  /** Not the turn the snapshot is on. The server clears a turn's results when the next one opens. */
  | 'unavailable';

export type TurnTimeline = {
  turnNumber: number;
  status: TimelineStatus;
  events: TimelineEvent[];
};

export function buildTurnTimeline(snapshot: MatchSnapshot, turnNumber: number): TurnTimeline {
  if (turnNumber !== snapshot.turnNumber) {
    return { turnNumber, status: 'unavailable', events: [] };
  }

  // Before the turn advances out of Movement the results are already on the snapshot but the ships
  // are still where they started, and walking the segments back from there would fly them in from
  // somewhere they never were.
  if (snapshot.phase !== 'Firing') {
    return { turnNumber, status: snapshot.movementResults.length > 0 ? 'awaiting-movement' : 'empty', events: [] };
  }

  const ships = new Map(snapshot.ships.map((ship) => [ship.id, ship]));
  const events: TimelineEvent[] = [];

  for (const result of snapshot.movementResults) {
    const ship = ships.get(result.shipId);
    if (!ship) {
      continue;
    }

    const points = trailPointsForResult(ship, result);
    const segments: MovementSegment[] = result.segments?.length ? result.segments : [{ course: result.endingCourse, distance: result.endingVelocity }];
    events.push({
      kind: 'move',
      step: 0,
      shipId: ship.id,
      startCourse: result.startingCourse,
      endCourse: result.endingCourse,
      legs: segments.map((segment, index) => ({ from: points[index], to: points[index + 1], course: segment.course })),
    });
  }

  const shots = snapshot.firingResults
    .map((result, resultIndex) => ({ result, resultIndex }))
    .filter(({ result }) => result.turnNumber === turnNumber);
  const finalBlow = new Map<string, number>();
  for (const { result, resultIndex } of shots) {
    if (result.hullDamageApplied > 0 && ships.get(result.targetShipId)?.isDestroyed) {
      finalBlow.set(result.targetShipId, resultIndex);
    }
  }

  shots.forEach(({ result, resultIndex }, order) => {
    const attacker = ships.get(result.attackerShipId);
    const target = ships.get(result.targetShipId);
    events.push({
      kind: 'fire',
      step: order + 1,
      resultIndex,
      result,
      fx: fxKeyFor(result, attacker),
      from: positionOf(attacker),
      to: positionOf(target),
      destroysTarget: finalBlow.get(result.targetShipId) === resultIndex,
    });
  });

  return { turnNumber, status: events.length > 0 ? 'resolved' : 'empty', events };
}

function fxKeyFor(result: FiringResult, attacker: Ship | undefined): FxKey {
  if (attacker && normalizeShipIconKey(attacker.iconKey, attacker.className) === 'fighter-group') {
    return 'FighterStrike';
  }

  return result.weaponKind ?? 'Unknown';
}

// A record whose ship has since gone (removed from the table) still gets its event: dropping it
// would be the timeline deciding a shot did not happen. It is drawn from the table's origin.
function positionOf(ship: Ship | undefined): TablePoint {
  return ship ? { x: ship.positionX, y: ship.positionY } : { x: 0, y: 0 };
}

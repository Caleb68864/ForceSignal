/**
 * One resolved turn, as a list of things to show in the order the server did them.
 *
 * This is the only place playback learns what happened, and it learns it by reading the snapshot -
 * never by working anything out. Every event carries the server's own record (the `FiringResult`,
 * the `TurnEvent`) rather than a copy of the fields someone thought the effects would need, so an
 * effect can only ever show a number the server sent. Movement is the server's path as flown -
 * drifting ships included, edge-clamped hulls from their real start - through
 * `trailPointsForResult`, the same points the resolved-trail overlay draws.
 *
 * The order is the server's `turnEvents` list where there is one: fighter flights, then every
 * ship's move at once, then salvos, point defence, shots, threshold checks and kills, each where it
 * was resolved. A snapshot without that list (an older server, a restored backup) falls back to
 * moves then shots, which is all such a snapshot can say.
 *
 * Pure and deterministic: the same snapshot gives the same timeline on every device, because the
 * order is the order of the arrays the server sent and nothing here reads a clock or a die.
 */

import { trailPointsForResult } from '../mapGeometry.ts';
import { normalizeShipIconKey } from '../normalize.ts';
import type { FiringResult, MatchSnapshot, MovementResult, MovementSegment, Ship, TablePoint, TurnEvent, WeaponKind } from '../../types.ts';

/** How an attack travels and lands. A fighter strike is reported as a beam, so it is told apart by the attacker. */
export type FxKey = WeaponKind | 'FighterStrike' | 'Unknown';

export type MoveLeg = { from: TablePoint; to: TablePoint; course: number };

export type MoveEvent = {
  kind: 'move';
  /** Every move in a turn is simultaneous, so they all share one step. */
  step: number;
  shipId: string;
  startCourse: number;
  endCourse: number;
  legs: MoveLeg[];
};

export type FireEvent = {
  kind: 'fire';
  step: number;
  /** Where the result sits in the turn's firing results. */
  resultIndex: number;
  /** The server's record, untouched. */
  result: FiringResult;
  fx: FxKey;
  from: TablePoint;
  to: TablePoint;
  /**
   * True on a shot whose kill is shown as part of the shot. Only without an event list, where it is
   * an attribution: the last shot that put hull damage on a ship the snapshot calls destroyed. With
   * one, the kill is its own `destroyed` event straight after, whatever caused it.
   */
  destroysTarget: boolean;
};

/** Something the server's event list reports beyond moves and shots, carried as it was sent. */
export type ReportedEvent = {
  kind: 'flight' | 'salvo' | 'salvo-wasted' | 'point-defence' | 'strike-stopped' | 'threshold' | 'destroyed';
  step: number;
  /** The server's record, untouched. */
  event: TurnEvent;
  from: TablePoint;
  to: TablePoint;
};

export type TimelineEvent = MoveEvent | FireEvent | ReportedEvent;

export type TimelineStatus =
  /** The turn's moves are flown and its shots so far are listed. */
  | 'resolved'
  /** Orders are revealed but the ships have not moved: the results exist, the positions do not yet match them. */
  | 'awaiting-movement'
  /** Nothing has resolved this turn yet. */
  | 'empty'
  /** Neither the snapshot's turn nor the one kept from before it. */
  | 'unavailable';

export type TurnTimeline = {
  turnNumber: number;
  status: TimelineStatus;
  events: TimelineEvent[];
};

/**
 * How each kind of server event shows on the timeline: a reported event of its own, or (for the
 * two not listed) the moves and the shots, which have records of their own.
 */
export const reportedKinds: Partial<Record<TurnEvent['kind'], ReportedEvent['kind']>> = {
  FighterFlew: 'flight',
  SalvoStruck: 'salvo',
  SalvoWasted: 'salvo-wasted',
  PointDefenseFired: 'point-defence',
  FighterStrikeStopped: 'strike-stopped',
  ThresholdChecked: 'threshold',
  ShipDestroyed: 'destroyed',
};

export function buildTurnTimeline(snapshot: MatchSnapshot, turnNumber: number): TurnTimeline {
  const ships = new Map(snapshot.ships.map((ship) => [ship.id, ship]));

  if (turnNumber === snapshot.turnNumber) {
    // Before the turn advances out of Movement the results are already on the snapshot but the ships
    // are still where they started, and drawing them would fly them in from somewhere they never were.
    if (snapshot.phase !== 'Firing') {
      return { turnNumber, status: snapshot.movementResults.length > 0 ? 'awaiting-movement' : 'empty', events: [] };
    }

    return assemble(turnNumber, ships, snapshot.movementResults, snapshot.firingResults, snapshot.turnEvents ?? null);
  }

  const last = snapshot.lastTurn;
  if (last && last.turnNumber === turnNumber) {
    return assemble(turnNumber, ships, last.movementResults, last.firingResults, last.events);
  }

  return { turnNumber, status: 'unavailable', events: [] };
}

function assemble(
  turnNumber: number,
  ships: Map<string, Ship>,
  movementResults: MovementResult[],
  firingResults: FiringResult[],
  turnEvents: TurnEvent[] | null,
): TurnTimeline {
  const shots = firingResults.map((result, resultIndex) => ({ result, resultIndex })).filter(({ result }) => result.turnNumber === turnNumber);
  const events: TimelineEvent[] = [];
  let step = 0;

  const addMoves = () => {
    for (const result of movementResults) {
      const move = moveFor(result, ships.get(result.shipId), step);
      if (move) {
        events.push(move);
      }
    }

    step += 1;
  };

  if (!turnEvents) {
    addMoves();
    const finalBlow = attributedKills(shots, ships);
    for (const { result, resultIndex } of shots) {
      events.push(fireFor(result, resultIndex, ships, step, finalBlow.get(result.targetShipId) === resultIndex, null));
      step += 1;
    }

    return { turnNumber, status: events.length > 0 ? 'resolved' : 'empty', events };
  }

  let movesPlaced = false;
  for (const turnEvent of turnEvents) {
    if (turnEvent.turnNumber !== turnNumber) {
      continue;
    }

    if (turnEvent.kind === 'MovementResolved') {
      addMoves();
      movesPlaced = true;
      continue;
    }

    if (turnEvent.kind === 'WeaponFired') {
      const shot = shots.find(({ resultIndex }) => resultIndex === turnEvent.firingResultIndex);
      if (shot) {
        events.push(fireFor(shot.result, shot.resultIndex, ships, step, false, turnEvent));
        step += 1;
      }

      continue;
    }

    const kind = reportedKinds[turnEvent.kind];
    if (kind) {
      events.push({
        kind,
        step,
        event: turnEvent,
        from: turnEvent.from ?? positionOf(ships.get(turnEvent.actorShipId ?? '')),
        to: turnEvent.to ?? positionOf(ships.get(turnEvent.targetShipId ?? '')),
      });
      step += 1;
    }
  }

  // A list from a turn whose movement predates the marker still shows its moves, first.
  if (!movesPlaced && movementResults.length > 0) {
    const moves = movementResults.map((result) => moveFor(result, ships.get(result.shipId), 0)).filter((move): move is MoveEvent => move !== null);
    for (const event of events) {
      event.step += 1;
    }

    events.unshift(...moves);
  }

  return { turnNumber, status: events.length > 0 ? 'resolved' : 'empty', events };
}

function moveFor(result: MovementResult, ship: Ship | undefined, step: number): MoveEvent | null {
  // The server's path needs no ship; walking back from the end does.
  if (!ship && !(result.path && result.path.length > 1)) {
    return null;
  }

  const points = trailPointsForResult(ship ?? ({ positionX: 0, positionY: 0 } as Ship), result);
  const segments: MovementSegment[] = result.segments?.length ? result.segments : [{ course: result.endingCourse, distance: result.endingVelocity }];
  return {
    kind: 'move',
    step,
    shipId: result.shipId,
    startCourse: result.startingCourse,
    endCourse: result.endingCourse,
    legs: segments.slice(0, points.length - 1).map((segment, index) => ({ from: points[index], to: points[index + 1], course: segment.course })),
  };
}

function fireFor(result: FiringResult, resultIndex: number, ships: Map<string, Ship>, step: number, destroysTarget: boolean, turnEvent: TurnEvent | null): FireEvent {
  const attacker = ships.get(result.attackerShipId);
  return {
    kind: 'fire',
    step,
    resultIndex,
    result,
    fx: fxKeyFor(result, attacker),
    // Where the ships were as the shot was taken, when the server says; otherwise where they are now.
    from: turnEvent?.from ?? positionOf(attacker),
    to: turnEvent?.to ?? positionOf(ships.get(result.targetShipId)),
    destroysTarget,
  };
}

function attributedKills(shots: { result: FiringResult; resultIndex: number }[], ships: Map<string, Ship>): Map<string, number> {
  const finalBlow = new Map<string, number>();
  for (const { result, resultIndex } of shots) {
    if (result.hullDamageApplied > 0 && ships.get(result.targetShipId)?.isDestroyed) {
      finalBlow.set(result.targetShipId, resultIndex);
    }
  }

  return finalBlow;
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

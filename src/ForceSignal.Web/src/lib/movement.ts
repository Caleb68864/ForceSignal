/**
 * Plotting a move: the turn maneuvers a draft holds, what thrust allows, and where the ship
 * ends up if the plot is followed.
 *
 * A plotted turn is made half at the start of the move and half at the mid-point, which is what
 * puts a ship where the rulebook's worked examples say it ends up. Rounding down is why a
 * single-point turn happens entirely at the mid-point.
 */

import { newId } from './api.ts';
import { wrapCourse } from './geometry.ts';
import type { DraftOrder, MovementSegment, Ship, TurnDirection, TurnManeuver } from '../types.ts';

export function turnManeuversForDraft(draft: DraftOrder): TurnManeuver[] {
  if (Array.isArray(draft.turnManeuvers) && draft.turnManeuvers.length > 0) {
    return draft.turnManeuvers
      .filter((maneuver): maneuver is TurnManeuver => (
        (maneuver.direction === 'Port' || maneuver.direction === 'Starboard')
        && Number.isFinite(maneuver.steps)
        && maneuver.steps > 0
      ))
      .map((maneuver) => ({ direction: maneuver.direction, steps: Math.round(maneuver.steps) }));
  }

  if (draft.turnSteps > 0 && draft.turnDirection !== 'None') {
    return [{ direction: draft.turnDirection, steps: draft.turnSteps }];
  }

  return [];
}
export function clampTurnManeuvers(maneuvers: TurnManeuver[], maxTurn: number) {
  let remaining = Math.max(0, maxTurn);
  const clamped: TurnManeuver[] = [];

  for (const maneuver of maneuvers) {
    if (remaining <= 0) {
      break;
    }

    const steps = Math.min(remaining, Math.max(0, Math.round(maneuver.steps)));
    if (steps > 0) {
      clamped.push({ direction: maneuver.direction, steps });
      remaining -= steps;
    }
  }

  return clamped;
}
export function totalTurnSteps(draft: DraftOrder) {
  return turnManeuversForDraft(draft).reduce((sum, maneuver) => sum + maneuver.steps, 0);
}
export function formatTurnSequence(draft: DraftOrder) {
  const maneuvers = turnManeuversForDraft(draft);
  return maneuvers.length === 0
    ? 'No turn'
    : maneuvers.map((maneuver) => `${maneuver.direction === 'Port' ? 'P' : 'S'}${maneuver.steps}`).join(' ');
}
export function turnPatchFromManeuvers(maneuvers: TurnManeuver[]): Partial<DraftOrder> {
  const clean = maneuvers.filter((maneuver) => maneuver.steps > 0);
  const turnSteps = clean.reduce((sum, maneuver) => sum + maneuver.steps, 0);
  const directions = new Set(clean.map((maneuver) => maneuver.direction));

  return {
    turnSteps,
    turnDirection: turnSteps === 0 ? 'None' : directions.size === 1 ? clean[0].direction : 'None',
    turnManeuvers: clean,
  };
}
export function previewCourse(currentCourse: number, draft: DraftOrder) {
  return turnManeuversForDraft(draft).reduce((course, maneuver) => (
    wrapCourse(course + (maneuver.direction === 'Starboard' ? maneuver.steps : -maneuver.steps))
  ), currentCourse);
}
/// Thrust actually available for plotting once drive damage is recorded.
export function usableThrust(ship: Pick<Ship, 'thrustRating' | 'driveDamage'>) {
  return Math.max(0, ship.thrustRating - ship.driveDamage);
}
export function maxLegalTurn(thrustRating: number, velocityDelta: number) {
  const remainingThrust = Math.max(0, thrustRating - Math.abs(velocityDelta));
  return Math.min(Math.ceil(thrustRating / 2), remainingThrust);
}
export function turnPatchForCourse(currentCourse: number, targetCourse: number, maxTurn: number, preferredDirection: TurnDirection): Partial<DraftOrder> {
  if (maxTurn === 0 || targetCourse === currentCourse) {
    return turnPatchFromManeuvers([]);
  }

  const starboard = (targetCourse - currentCourse + 12) % 12;
  const port = (currentCourse - targetCourse + 12) % 12;
  const useStarboard = starboard < port || (starboard === port && preferredDirection !== 'Port');
  const rawSteps = useStarboard ? starboard : port;
  const turnSteps = Math.min(rawSteps, maxTurn);

  return turnPatchFromManeuvers(turnSteps === 0 ? [] : [{ direction: useStarboard ? 'Starboard' : 'Port', steps: turnSteps }]);
}
export function appendTurnPatchForCourse(draft: DraftOrder, currentCourse: number, targetCourse: number, maxTurn: number): Partial<DraftOrder> {
  const existing = turnManeuversForDraft(draft);
  const remaining = Math.max(0, maxTurn - existing.reduce((sum, maneuver) => sum + maneuver.steps, 0));
  if (remaining <= 0 || targetCourse === currentCourse) {
    return turnPatchFromManeuvers(existing);
  }

  const patch = turnPatchForCourse(currentCourse, targetCourse, remaining, draft.turnDirection);
  return turnPatchFromManeuvers([...existing, ...turnManeuversForDraft({ ...draft, ...patch })]);
}
/// The legs a plotted order will actually be flown as, mirroring the server: each plotted turn
/// takes an equal share of the move and is made half at the start of its leg and half at the
/// mid-point, with half rounded down. A straight line to the ending course would put the preview
/// somewhere the ship never goes.
export function plannedSegments(currentCourse: number, endingVelocity: number, draft: DraftOrder): MovementSegment[] {
  const maneuvers = turnManeuversForDraft(draft);
  if (maneuvers.length === 0) {
    return [{ course: currentCourse, distance: endingVelocity }];
  }

  const halfLeg = endingVelocity / (maneuvers.length * 2);
  const segments: MovementSegment[] = [];
  const addLeg = (course: number, distance: number) => {
    const last = segments[segments.length - 1];
    if (last && last.course === course) {
      last.distance += distance;
      return;
    }

    segments.push({ course, distance });
  };

  let course = currentCourse;
  for (const maneuver of maneuvers) {
    const sign = maneuver.direction === 'Port' ? -1 : 1;
    const openingPivot = Math.floor(maneuver.steps / 2);
    course = wrapCourse(course + sign * openingPivot);
    addLeg(course, halfLeg);
    course = wrapCourse(course + sign * (maneuver.steps - openingPivot));
    addLeg(course, halfLeg);
  }

  return segments;
}
export function estimateDraftEndpoint(ship: Ship, draft: DraftOrder, tableWidth: number, tableDepth: number): { x: number; y: number } {
  const endingVelocity = Math.max(0, ship.currentVelocity + draft.velocityDelta);
  let x = ship.positionX;
  let y = ship.positionY;
  for (const segment of plannedSegments(ship.currentCourse, endingVelocity, draft)) {
    const radians = segment.course * Math.PI / 6;
    x = Math.max(0, Math.min(tableWidth, x + Math.sin(radians) * segment.distance));
    y = Math.max(0, Math.min(tableDepth, y - Math.cos(radians) * segment.distance));
  }

  return { x, y };
}
export function toOrder(draft: DraftOrder) {
  const maneuvers = turnManeuversForDraft(draft);
  const turnSteps = maneuvers.reduce((sum, maneuver) => sum + maneuver.steps, 0);
  const directions = new Set(maneuvers.map((maneuver) => maneuver.direction));
  return {
    velocityDelta: draft.velocityDelta,
    turnSteps,
    turnDirection: turnSteps === 0 ? 'None' : directions.size === 1 ? maneuvers[0].direction : 'None',
    turnManeuvers: maneuvers,
  };
}
export function clampDraftForShip(thrustRating: number, draft: DraftOrder): DraftOrder {
  const velocityDelta = Math.max(-thrustRating, Math.min(thrustRating, draft.velocityDelta));
  const maxTurn = maxLegalTurn(thrustRating, velocityDelta);
  const maneuvers = clampTurnManeuvers(turnManeuversForDraft(draft), maxTurn);
  const turnSteps = maneuvers.reduce((sum, maneuver) => sum + maneuver.steps, 0);
  const directions = new Set(maneuvers.map((maneuver) => maneuver.direction));
  return {
    ...draft,
    velocityDelta,
    turnSteps,
    turnDirection: turnSteps === 0 ? 'None' : directions.size === 1 ? maneuvers[0].direction : draft.turnDirection === 'None' ? maneuvers[0].direction : draft.turnDirection,
    turnManeuvers: maneuvers,
  };
}
export function resetOrderDraft(draft: DraftOrder): DraftOrder {
  return {
    ...draft,
    velocityDelta: 0,
    turnSteps: 0,
    turnDirection: 'None',
    turnManeuvers: [],
  };
}
export function draftFor(shipId: string, drafts: Record<string, DraftOrder>): DraftOrder {
  return drafts[shipId] ?? createDraftOrder();
}
export function createDraftOrder(): DraftOrder {
  return {
    velocityDelta: 0,
    turnSteps: 0,
    turnDirection: 'None',
    turnManeuvers: [],
    salt: newId(),
  };
}


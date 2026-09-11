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
import type { DraftOrder, Ship, TurnDirection, TurnManeuver } from '../types.ts';

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
/**
 * Most turn steps the helm may still plot, given what the ship is doing.
 *
 * `currentVelocity` is not decoration and is not optional. A ship at rest that is not accelerating
 * rotates on the spot to any heading, spending no thrust and ignoring the half-thrust cap - the
 * resolver carves that out by name in `FullThrustLightCinematicRules.MaxTurnSteps` and again in its
 * `Validate`, and this file had no copy of it. So the compass drew PORT/STARBOARD LIMIT at
 * ceil(thrust/2) around a stationary ship, `clampDraftForShip` trimmed the manoeuvre back down, and
 * the map answered "has no turn points left" to a rotation the server would have accepted. With
 * `defaultShipForm.currentVelocity` now opening at zero, that is the state every ship is created in.
 *
 * The velocity is a required parameter rather than one defaulting to something, because a caller
 * that does not know the ship's velocity cannot answer this question and should not be able to
 * compile as though it had. `turnLimitCases.json` holds this function and the resolver to each
 * other across the language boundary.
 */
export function maxLegalTurn(thrustRating: number, velocityDelta: number, currentVelocity: number) {
  if (currentVelocity === 0 && velocityDelta === 0) {
    return 12;
  }

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
// Where a plotted order lands is deliberately not worked out here. The split-turn geometry - each
// turn taking an equal share of the move, made half at the start of its leg and half at the
// mid-point - used to be mirrored in this file, and a mirror is a second implementation that can
// drift from the resolver that actually flies the turn. The server answers instead, through
// `useOrderPreview`, so the preview cannot disagree with the move.
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
export function clampDraftForShip(thrustRating: number, draft: DraftOrder, currentVelocity: number): DraftOrder {
  const velocityDelta = Math.max(-thrustRating, Math.min(thrustRating, draft.velocityDelta));
  const maxTurn = maxLegalTurn(thrustRating, velocityDelta, currentVelocity);
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


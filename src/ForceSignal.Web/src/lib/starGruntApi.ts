/**
 * Talking to the StarGrunt engine.
 *
 * Kept apart from the match API because it is a different game rather than a mode of the same one:
 * no room code, no participant token, one device at the table. The routes exist only when the
 * server has the engine switched on, so `readFeatures` is asked before any of this is offered.
 */

import { get, post } from './api.ts';
import type { FeatureFlags, StarGruntGameCreated, StarGruntSnapshot } from '../types.ts';

/** Which optional engines this server offers. */
export function readFeatures() {
  return get<FeatureFlags>('/api/features');
}

/** Starts a game. */
export function createGame(name: string) {
  return post<StarGruntGameCreated>('/api/stargrunt/games', { name });
}

/** Reads a game back. */
export function readGame(gameId: string) {
  return get<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}`);
}

/** Puts a unit on the table. Every die is a face count off the user's own record card. */
export function addUnit(gameId: string, unit: {
  id: string;
  name: string;
  side: string;
  level: string;
  qualityDie: number;
  leadershipValue: number;
  figures: { armourDie: number }[];
  weapons: { name: string; impactDie: number; isSupport: boolean; isCloseRange: boolean }[];
}) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}/units`, unit);
}

/** Opens the next turn. */
export function beginTurn(gameId: string) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}/turns/begin`, {});
}

/** Settles who takes the first activation this turn. */
export function chooseFirstActivator(gameId: string, side: string, takeIt: boolean) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}/turns/current/first-activator`, { side, takeIt });
}

/** Opens an activation. */
export function beginActivation(gameId: string, side: string, unitId: string) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}/activations`, { side, unitId });
}

/** Spends an action on something other than shooting. */
export function takeStep(gameId: string, action: string) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}/activations/current/steps`, { action });
}

/** Fires one weapon at another unit. Range and cover are the players' call, as at a table. */
export function fire(gameId: string, shot: {
  firerId: string;
  targetId: string;
  weaponName: string;
  firepowerDie: number;
  supportDice: number[];
  distanceInches: number;
  cover: string;
  inPosition: boolean;
}) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}/activations/current/fire`, shot);
}

/** Spends an action trying to shake off one suppression marker. One roll, one marker at best. */
export function removeSuppression(gameId: string, unitId: string) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}/activations/current/remove-suppression`, { unitId });
}

/** Closes the open activation. */
export function endActivation(gameId: string) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}/activations/current/end`, {});
}

/** Declines to activate anything. */
export function pass(gameId: string, side: string) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}/turns/current/pass`, { side });
}

/** Ends the turn. */
export function endTurn(gameId: string) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}/turns/current/end`, {});
}

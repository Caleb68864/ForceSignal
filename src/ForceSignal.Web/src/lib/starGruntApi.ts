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
  fatigue: string;
  figures: { armourDie: number }[];
  weapons: { name: string; impactDie: number; isSupport: boolean; isCloseRange: boolean; supportFirepowerDie: number; neverJoinsSquadFire: boolean }[];
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
  supportWeapons: string[];
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

/**
 * Puts a unit's nerve to the test. Not an action - it happens the moment something bad does, to
 * whichever unit it happened to. The threat level comes off the player's own table.
 */
export function confidenceTest(gameId: string, unitId: string, threatLevel: number) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}/confidence-tests`, { unitId, threatLevel });
}

/** Spends a command element's action steadying a subordinate. */
export function rally(gameId: string, rallyingUnitId: string, ralliedUnitId: string) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}/activations/current/rally`, { rallyingUnitId, ralliedUnitId });
}

/** Spends an action putting a scattered unit back in order. */
export function reorganise(gameId: string, unitId: string) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}/activations/current/reorganise`, { unitId });
}

/** Declares whether a unit has scattered out of integrity. Measured with a ruler, not computed. */
export function setDisorganised(gameId: string, unitId: string, isDisorganised: boolean) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}/units/disorganised`, { unitId, isDisorganised });
}

/**
 * Rolls to see whether troops have the nerve for a risky order. Failing costs the action and never
 * a confidence level; passing spends nothing by itself.
 */
export function reactionTest(gameId: string, unitId: string, threatLevel: number) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}/reaction-tests`, { unitId, threatLevel });
}

/** Declares that a unit's next move would take it out of cover. An eyeball call, not a computed one. */
export function setLeavesCover(gameId: string, unitId: string, leavesCover: boolean) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}/units/leaves-cover`, { unitId, leavesCover });
}

/** Declares a close assault. The threat it asks is the player's, off their own table. */
export function declareCharge(gameId: string, attackerId: string, defenderId: string, threatLevel: number) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}/activations/current/charge`, { attackerId, defenderId, threatLevel });
}

/** Rolls the defender's nerve to stand. The threat comes from the odds, doubled by terror. */
export function defenderStands(gameId: string, attackerId: string, defenderId: string, terror: boolean) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}/assaults/stand`, { attackerId, defenderId, terror });
}

/** Fights one round of melee. Who fights whom is the players' call, not the app's. */
export function fightMelee(gameId: string, melee: {
  attackerId: string;
  defenderId: string;
  pairings: { attackerShift: number; defenderShift: number; attackerPowerArmour: boolean; defenderPowerArmour: boolean }[];
  defendersInCover: boolean;
}) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${gameId}/assaults/melee`, melee);
}

/** Rolls what became of a unit's downed figures, once somebody has won the ground. */
export function settleTheDowned(
  gameId: string,
  unitId: string,
  downed: number,
  wonTheAssault: boolean,
  deadUpTo: number,
  woundedUpTo: number,
) {
  return post<StarGruntSnapshot>(
    `/api/stargrunt/games/${gameId}/assaults/downed`,
    { unitId, downed, wonTheAssault, deadUpTo, woundedUpTo },
  );
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

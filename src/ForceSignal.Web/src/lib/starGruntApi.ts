/**
 * Talking to the StarGrunt engine.
 *
 * Kept apart from the match API because it is a different game rather than a mode of the same one:
 * no room code, no seats, one device at the table. What that device holds instead is the game
 * token minted when the game was created, presented on every route that reads or changes it. The
 * routes exist only when the server has the engine switched on, so `readFeatures` is asked before
 * any of this is offered.
 */

import { get, post } from './api.ts';
import type { FeatureFlags, GameHandle, StarGruntGameCreated, StarGruntSnapshot } from '../types.ts';

function gameAuth(game: GameHandle) {
  return { 'X-Game-Token': game.token };
}

/** Which optional engines this server offers. */
export function readFeatures() {
  return get<FeatureFlags>('/api/features');
}

/**
 * The range table a game is played on, off the user's own rulebook. This app supplies none of it.
 *
 * An entry the user has not made is left out of the body rather than sent as zero, so the server
 * reads it as not entered; a zero the user did type is sent, because a cover shift of nothing is a
 * real answer.
 */
export type StarGruntRulesProfileInput = {
  bandWidths: { qualityDie: number; inches: number }[];
  rangeDice: { bandsOut: number; die: number }[];
  effectiveBands?: number;
  softCoverShift?: number;
  hardCoverShift?: number;
  inPositionShift?: number;
  meleeCoverShift?: number;
  lowestLeadershipValue?: number;
  highestLeadershipValue?: number;
};

/**
 * Starts a game.
 *
 * The range table is sent when the user has read it off their rulebook and left out when they have
 * not, and there is no fallback to leave it out into: a game with no table refuses its first shot
 * and names the entry it wanted. Partial is fine and is sent as given.
 */
export function createGame(name: string, profile?: StarGruntRulesProfileInput) {
  return post<StarGruntGameCreated>('/api/stargrunt/games', profile ? { name, profile } : { name });
}

/** Reads a game back. */
export function readGame(game: GameHandle) {
  return get<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}`, undefined, gameAuth(game));
}

/** Puts a unit on the table. Every die is a face count off the user's own record card. */
export function addUnit(game: GameHandle, unit: {
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
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}/units`, unit, undefined, gameAuth(game));
}

/** Opens the next turn. */
export function beginTurn(game: GameHandle) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}/turns/begin`, {}, undefined, gameAuth(game));
}

/** Settles who takes the first activation this turn. */
export function chooseFirstActivator(game: GameHandle, side: string, takeIt: boolean) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}/turns/current/first-activator`, { side, takeIt }, undefined, gameAuth(game));
}

/** Opens an activation. */
export function beginActivation(game: GameHandle, side: string, unitId: string) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}/activations`, { side, unitId }, undefined, gameAuth(game));
}

/** Spends an action on something other than shooting. */
export function takeStep(game: GameHandle, action: string) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}/activations/current/steps`, { action }, undefined, gameAuth(game));
}

/** Fires one weapon at another unit. Range and cover are the players' call, as at a table. */
export function fire(game: GameHandle, shot: {
  firerId: string;
  targetId: string;
  weaponName: string;
  firepowerDie: number;
  supportWeapons: string[];
  distanceInches: number;
  cover: string;
  inPosition: boolean;
}) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}/activations/current/fire`, shot, undefined, gameAuth(game));
}

/** Spends an action trying to shake off one suppression marker. One roll, one marker at best. */
export function removeSuppression(game: GameHandle, unitId: string) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}/activations/current/remove-suppression`, { unitId }, undefined, gameAuth(game));
}

/**
 * Puts a unit's nerve to the test. Not an action - it happens the moment something bad does, to
 * whichever unit it happened to. The threat level comes off the player's own table.
 */
export function confidenceTest(game: GameHandle, unitId: string, threatLevel: number) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}/confidence-tests`, { unitId, threatLevel }, undefined, gameAuth(game));
}

/** Spends a command element's action steadying a subordinate. */
export function rally(game: GameHandle, rallyingUnitId: string, ralliedUnitId: string) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}/activations/current/rally`, { rallyingUnitId, ralliedUnitId }, undefined, gameAuth(game));
}

/** Spends an action putting a scattered unit back in order. */
export function reorganise(game: GameHandle, unitId: string) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}/activations/current/reorganise`, { unitId }, undefined, gameAuth(game));
}

/** Declares whether a unit has scattered out of integrity. Measured with a ruler, not computed. */
export function setDisorganised(game: GameHandle, unitId: string, isDisorganised: boolean) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}/units/disorganised`, { unitId, isDisorganised }, undefined, gameAuth(game));
}

/**
 * Rolls to see whether troops have the nerve for a risky order. Failing costs the action and never
 * a confidence level; passing spends nothing by itself.
 */
export function reactionTest(game: GameHandle, unitId: string, threatLevel: number) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}/reaction-tests`, { unitId, threatLevel }, undefined, gameAuth(game));
}

/** Declares that a unit's next move would take it out of cover. An eyeball call, not a computed one. */
export function setLeavesCover(game: GameHandle, unitId: string, leavesCover: boolean) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}/units/leaves-cover`, { unitId, leavesCover }, undefined, gameAuth(game));
}

/** Declares a close assault. The threat it asks is the player's, off their own table. */
export function declareCharge(game: GameHandle, attackerId: string, defenderId: string, threatLevel: number) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}/activations/current/charge`, { attackerId, defenderId, threatLevel }, undefined, gameAuth(game));
}

/** Rolls the defender's nerve to stand. The threat comes from the odds, doubled by terror. */
export function defenderStands(game: GameHandle, attackerId: string, defenderId: string, terror: boolean) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}/assaults/stand`, { attackerId, defenderId, terror }, undefined, gameAuth(game));
}

/** Fights one round of melee. Who fights whom is the players' call, not the app's. */
export function fightMelee(game: GameHandle, melee: {
  attackerId: string;
  defenderId: string;
  pairings: { attackerShift: number; defenderShift: number; attackerPowerArmour: boolean; defenderPowerArmour: boolean }[];
  defendersInCover: boolean;
}) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}/assaults/melee`, melee, undefined, gameAuth(game));
}

/** Rolls what became of a unit's downed figures, once somebody has won the ground. */
export function settleTheDowned(
  game: GameHandle,
  unitId: string,
  downed: number,
  wonTheAssault: boolean,
  deadUpTo: number,
  woundedUpTo: number,
  // The die the bands above are read against, off the same table. Zero when the table has not said,
  // which the game refuses by name rather than throwing a die of its own choosing.
  fateDie: number,
) {
  return post<StarGruntSnapshot>(
    `/api/stargrunt/games/${game.gameId}/assaults/downed`,
    { unitId, downed, wonTheAssault, deadUpTo, woundedUpTo, fateDie },
    undefined,
    gameAuth(game),
  );
}

/** Closes the open activation. */
export function endActivation(game: GameHandle) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}/activations/current/end`, {}, undefined, gameAuth(game));
}

/** Declines to activate anything. */
export function pass(game: GameHandle, side: string) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}/turns/current/pass`, { side }, undefined, gameAuth(game));
}

/** Ends the turn. */
export function endTurn(game: GameHandle) {
  return post<StarGruntSnapshot>(`/api/stargrunt/games/${game.gameId}/turns/current/end`, {}, undefined, gameAuth(game));
}

/**
 * Talking to the Dirtside engine.
 *
 * Kept apart from the match API for the same reason the StarGrunt client is: a different game rather
 * than a mode of the same one. No room code, no seats, one device at the table - which holds the
 * game token minted on create and presents it on every later route. The routes exist only when the
 * server has the engine switched on, so `readFeatures` is asked first.
 */

import { get, post } from './api.ts';
import type {
  DirtsideGameCreated,
  DirtsideNumericalChits,
  DirtsideSnapshot,
  DirtsideSpecialChits,
  GameHandle,
} from '../types.ts';

function gameAuth(game: GameHandle) {
  return { 'X-Game-Token': game.token };
}

/**
 * What is in the chit pot, counted off the user's own counter sheet. This app supplies none of it.
 */
export type DirtsideChitPotInput = {
  numericals: DirtsideNumericalChits[];
  specials: DirtsideSpecialChits[];
};

/**
 * One row of a die table: a key off the record card, and the die it rolls.
 */
export type DirtsideDieRowInput = { key: string; die: string };

/**
 * The dice this game is settled with, off the user's own rulebook. This app supplies none of them.
 */
export type DirtsideRulesProfileInput = {
  fireControl: DirtsideDieRowInput[];
  posture: DirtsideDieRowInput[];
  signature: DirtsideDieRowInput[];
  systemsDownRecoveryDie?: string;
  systemsDownRecoveryRoll: number;
  systemsDownRecoveryRollWithBackup: number;
};

/**
 * Starts a game.
 *
 * The chit pot is sent when the user has counted one and left out when they have not. Leaving it out
 * is not free: the server falls back to a built-in composition whose special counts are its own
 * guess, and says so on the snapshot it hands back. The fallback exists for one release so that
 * games started before the pot was asked for still open.
 *
 * The die tables work the other way round. They are sent when the user has read them off their
 * rulebook and left out when they have not, and there is no fallback to leave them out into: a game
 * with no rows refuses its first shot and names the row it wanted. Partial is fine and is sent as
 * given - only the rows a particular shot reads have to be there.
 */
export function createGame(
  name: string,
  chitPot?: DirtsideChitPotInput,
  profile?: DirtsideRulesProfileInput,
) {
  return post<DirtsideGameCreated>('/api/dirtside/games', {
    name,
    ...(chitPot ? { chitPot } : {}),
    ...(profile ? { profile } : {}),
  });
}

/** Reads a game back. */
export function readGame(game: GameHandle) {
  return get<DirtsideSnapshot>(`/api/dirtside/games/${game.gameId}`, undefined, gameAuth(game));
}

/** One weapon system, off the user's own record card. This app supplies no stats. */
export type DirtsideWeaponInput = {
  name: string;
  chitCount: number;
  barrels: number;
  isFixedMount: boolean;
  close: { colours: string };
  medium: { colours: string };
  long: { colours: string };
};

/**
 * Which chits count and how they read, off the user's own card. Colours is any of All, Red, Yellow
 * or Green; value scale is Doubled, FaceValue or Halved.
 */
export type DirtsideValidityInput = {
  colours: string;
  valueScale: string;
  specialsCount: boolean;
  isIneffective: boolean;
};

/**
 * Puts a platoon on the table. The quality die, leadership value and the two assault numbers are
 * left out when the card does not give them - the platoon can still do everything except assault.
 */
export function addPlatoon(game: GameHandle, platoon: {
  id: string;
  name: string;
  side: string;
  kind: string;
  isCybertank: boolean;
  qualityDie?: string;
  leadershipValue?: number;
  elements: {
    id: string;
    name: string;
    fireControl: string;
    signature: number;
    armourValue: number;
    movement: number;
    weapons: DirtsideWeaponInput[];
    hasBackupSystems?: boolean;
    assaultChits?: number;
    killThreshold?: number;
  }[];
}) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${game.gameId}/units`, platoon, undefined, gameAuth(game));
}

/** Opens the next turn. */
export function beginTurn(game: GameHandle) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${game.gameId}/turns`, {}, undefined, gameAuth(game));
}

/** Settles who takes the first activation this turn. */
export function chooseFirstActivator(game: GameHandle, side: string, takeIt: boolean) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${game.gameId}/turns/current/first-activator`, { side, takeIt }, undefined, gameAuth(game));
}

/** Turns a platoon's marker over and starts its activation. */
export function beginActivation(game: GameHandle, side: string, unitId: string) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${game.gameId}/activations`, { side, unitId }, undefined, gameAuth(game));
}

/** Moves one element. How far it went is measured at the table, so it is answered rather than computed. */
export function moveElement(game: GameHandle, elementId: string, overHalfItsMovement: boolean) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${game.gameId}/activations/current/moves`, { elementId, overHalfItsMovement }, undefined, gameAuth(game));
}

/** Declares that an element is sitting this activation out, and therefore the whole turn. */
export function standDown(game: GameHandle, elementId: string) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${game.gameId}/activations/current/stand-down`, { elementId }, undefined, gameAuth(game));
}

/** Switches an element's area-defence sensors on or off, spending its combat action. */
export function setSensors(game: GameHandle, elementId: string, live: boolean) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${game.gameId}/activations/current/sensors`, { elementId, live }, undefined, gameAuth(game));
}

/**
 * Fires one element's weapon at one designated element. The declaration is binding - including
 * `willMoveOverHalf`, which penalises the shot now and is the only way to move over half afterwards.
 */
export function fire(game: GameHandle, shot: {
  elementId: string;
  weapon: string;
  targetUnitId: string;
  targetElementId: string;
  measuredBand: string;
  willMoveOverHalf: boolean;
}) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${game.gameId}/activations/current/fire`, shot, undefined, gameAuth(game));
}

/**
 * Tries to get a Systems Down marker off one element, spending its combat action. Refused unless
 * the marker went on during an earlier activation and the element has not acted yet.
 */
export function recoverSystems(game: GameHandle, elementId: string) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${game.gameId}/activations/current/recover-systems`, { elementId }, undefined, gameAuth(game));
}

/**
 * Sends the activated platoon in against another. Every listed element spends its combat action
 * whether or not the troops go; the threat level is the player's, off their own table.
 */
export function launchAssault(game: GameHandle, assault: {
  targetUnitId: string;
  elementIds: string[];
  threatLevel: number;
  validity: DirtsideValidityInput;
  handToHandValidity: DirtsideValidityInput | null;
}) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${game.gameId}/assaults/launch`, assault, undefined, gameAuth(game));
}

/** The defenders' confidence test. Standing goes to a round; giving way goes straight to the follow-through. */
export function standAgainstAssault(game: GameHandle, stand: {
  elementIds: string[];
  threatLevel: number;
  validity: DirtsideValidityInput;
  handToHandValidity: DirtsideValidityInput | null;
}) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${game.gameId}/assaults/stand`, stand, undefined, gameAuth(game));
}

/** Fights the next round. Which stands come off is the game's to say, in the order they were committed. */
export function fightAssaultRound(game: GameHandle) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${game.gameId}/assaults/round`, {}, undefined, gameAuth(game));
}

/** Tests both sides' nerve after a round. The defender goes first; if it breaks the attacker is never asked. */
export function resolveAssaultAftermath(game: GameHandle, threats: { lightCasualtyThreat: number; heavyCasualtyThreat: number }) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${game.gameId}/assaults/aftermath`, threats, undefined, gameAuth(game));
}

/** The attacker's reaction test to press on. Passing gives it a whole extra activation on the spot. */
export function followThrough(game: GameHandle, threatLevel: number) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${game.gameId}/assaults/follow-through`, { threatLevel }, undefined, gameAuth(game));
}

/**
 * Closes the open activation. Refused while any element has not said what it is doing, or while an
 * assault is still owed a step - except at the follow-through, where ending it declines the test.
 */
export function endActivation(game: GameHandle) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${game.gameId}/activations/current/end`, {}, undefined, gameAuth(game));
}

/** Declines to activate anything. */
export function pass(game: GameHandle, side: string) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${game.gameId}/turns/current/pass`, { side }, undefined, gameAuth(game));
}

/** Closes the turn. */
export function endTurn(game: GameHandle) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${game.gameId}/turns/current/end`, {}, undefined, gameAuth(game));
}

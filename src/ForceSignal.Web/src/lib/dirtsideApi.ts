/**
 * Talking to the Dirtside engine.
 *
 * Kept apart from the match API for the same reason the StarGrunt client is: a different game rather
 * than a mode of the same one. No room code, no seats, one device at the table - which holds the
 * game token minted on create and presents it on every later route. The routes exist only when the
 * server has the engine switched on, so `readFeatures` is asked first.
 */

import { get, post } from './api.ts';
import type { DirtsideGameCreated, DirtsideSnapshot, GameHandle } from '../types.ts';

function gameAuth(game: GameHandle) {
  return { 'X-Game-Token': game.token };
}

/** Starts a game. */
export function createGame(name: string) {
  return post<DirtsideGameCreated>('/api/dirtside/games', { name });
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

/** Puts a platoon on the table. */
export function addPlatoon(game: GameHandle, platoon: {
  id: string;
  name: string;
  side: string;
  kind: string;
  isCybertank: boolean;
  elements: {
    id: string;
    name: string;
    fireControl: string;
    signature: number;
    armourValue: number;
    movement: number;
    weapons: DirtsideWeaponInput[];
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

/** Fires one element's weapon at one designated element. The declaration is binding. */
export function fire(game: GameHandle, shot: {
  elementId: string;
  weapon: string;
  targetUnitId: string;
  targetElementId: string;
  measuredBand: string;
}) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${game.gameId}/activations/current/fire`, shot, undefined, gameAuth(game));
}

/** Closes the open activation. Refused while any element has not said what it is doing. */
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

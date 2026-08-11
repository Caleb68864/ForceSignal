/**
 * Talking to the Dirtside engine.
 *
 * Kept apart from the match API for the same reason the StarGrunt client is: a different game rather
 * than a mode of the same one. No room code, no participant token, one device at the table. The
 * routes exist only when the server has the engine switched on, so `readFeatures` is asked first.
 */

import { get, post } from './api.ts';
import type { DirtsideGameCreated, DirtsideSnapshot } from '../types.ts';

/** Starts a game. */
export function createGame(name: string) {
  return post<DirtsideGameCreated>('/api/dirtside/games', { name });
}

/** Reads a game back. */
export function readGame(gameId: string) {
  return get<DirtsideSnapshot>(`/api/dirtside/games/${gameId}`);
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
export function addPlatoon(gameId: string, platoon: {
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
  return post<DirtsideSnapshot>(`/api/dirtside/games/${gameId}/units`, platoon);
}

/** Opens the next turn. */
export function beginTurn(gameId: string) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${gameId}/turns`, {});
}

/** Settles who takes the first activation this turn. */
export function chooseFirstActivator(gameId: string, side: string, takeIt: boolean) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${gameId}/turns/current/first-activator`, { side, takeIt });
}

/** Turns a platoon's marker over and starts its activation. */
export function beginActivation(gameId: string, side: string, unitId: string) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${gameId}/activations`, { side, unitId });
}

/** Moves one element. How far it went is measured at the table, so it is answered rather than computed. */
export function moveElement(gameId: string, elementId: string, overHalfItsMovement: boolean) {
  return post<DirtsideSnapshot>(
    `/api/dirtside/games/${gameId}/activations/current/moves`,
    { elementId, overHalfItsMovement },
  );
}

/** Declares that an element is sitting this activation out, and therefore the whole turn. */
export function standDown(gameId: string, elementId: string) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${gameId}/activations/current/stand-down`, { elementId });
}

/** Switches an element's area-defence sensors on or off, spending its combat action. */
export function setSensors(gameId: string, elementId: string, live: boolean) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${gameId}/activations/current/sensors`, { elementId, live });
}

/** Fires one element's weapon at one designated element. The declaration is binding. */
export function fire(gameId: string, shot: {
  elementId: string;
  weapon: string;
  targetUnitId: string;
  targetElementId: string;
  measuredBand: string;
}) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${gameId}/activations/current/fire`, shot);
}

/** Closes the open activation. Refused while any element has not said what it is doing. */
export function endActivation(gameId: string) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${gameId}/activations/current/end`, {});
}

/** Declines to activate anything. */
export function pass(gameId: string, side: string) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${gameId}/turns/current/pass`, { side });
}

/** Closes the turn. */
export function endTurn(gameId: string) {
  return post<DirtsideSnapshot>(`/api/dirtside/games/${gameId}/turns/current/end`, {});
}

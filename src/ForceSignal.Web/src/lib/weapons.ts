/**
 * Building and editing the weapon mounts on a ship form.
 */

import { newId } from './api.ts';
import type { FiringArc, ShipForm, WeaponKind, WeaponMount } from '../types.ts';

/**
 * A mount with nothing filled in.
 *
 * It used to arrive as a "Class-2 Beam" firing two dice out to twenty-four - a class name, a damage
 * rating and a reach, none of which the player had entered, in a file whose own header says a
 * starting point the player is "expected to replace" is still a number this app shipped. What is
 * left is structure: a placeholder label, the smallest attack dice and range the server's clamps
 * allow, the first arc and the first kind. None of those is a reading off anybody's card, and all of
 * them are visibly unfilled.
 */
export function newWeaponMount(): WeaponMount {
  return {
    id: newId(),
    name: 'Unnamed Mount',
    attackDice: 1,
    maxRange: 1,
    arcs: ['Fore'],
    kind: 'Beam',
    ammoMax: 0,
    ammoUsed: 0,
    reloadTurns: 0,
  };
}
export function weaponPreset(name: string, attackDice: number, maxRange: number, arcs: FiringArc[], ammoMax = 0, kind: WeaponKind = 'Beam'): WeaponMount {
  return {
    id: newId(),
    name,
    attackDice,
    maxRange,
    arcs,
    kind,
    ammoMax,
    ammoUsed: 0,
    reloadTurns: 0,
  };
}
export function updateWeapon(form: ShipForm, weaponId: string, patch: Partial<WeaponMount>): ShipForm {
  return {
    ...form,
    weapons: form.weapons.map((weapon) => weapon.id === weaponId ? { ...weapon, ...patch } : weapon),
  };
}

/**
 * Building and editing the weapon mounts on a ship form.
 */

import { newId } from './api.ts';
import type { FiringArc, ShipForm, WeaponKind, WeaponMount } from '../types.ts';

export function newWeaponMount(): WeaponMount {
  return {
    id: newId(),
    name: 'Class-2 Beam',
    attackDice: 2,
    maxRange: 24,
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

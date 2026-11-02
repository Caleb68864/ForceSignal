import { useState } from 'react';
import * as api from '../../lib/dirtsideApi.ts';
import { bands } from '../../lib/groundVocabulary.ts';
import type { DirtsidePlatoonState, DirtsideSnapshot, GameHandle } from '../../types.ts';

/**
 * Infantry firing as stands: one check for the unit, then a draw per stand, never pooled.
 *
 * The stands are sent in the order they were ticked, because if the check lets only part of the unit
 * fire, which part is the player's. Every number is off the card or the server; nothing is worked out
 * here.
 */
export function DirtsideFirefightPanel({ game, snapshot, firing, busy, run }: {
  game: GameHandle;
  snapshot: DirtsideSnapshot;
  firing: DirtsidePlatoonState;
  busy: boolean;
  run: (action: () => Promise<DirtsideSnapshot>, note?: string) => Promise<void>;
}) {
  const [stands, setStands] = useState<string[]>([]);
  const [weaponChoice, setWeaponChoice] = useState('');
  const [targetChoice, setTargetChoice] = useState('');
  const [standChoice, setStandChoice] = useState('');
  const [measuredBand, setMeasuredBand] = useState<string>(bands[0]);

  const able = firing.elements.filter((element) => !element.isDestroyed && !element.hasTakenCombatAction && !element.hasStoodDown);
  const weapons = [...new Set(able.flatMap((element) => element.weapons))];
  const weapon = weapons.includes(weaponChoice) ? weaponChoice : (weapons[0] ?? '');
  const targets = snapshot.units.filter((unit) => unit.side !== firing.side && unit.kind === 'DismountedInfantry');
  const target = targets.find((unit) => unit.id === targetChoice) ?? targets[0];
  const targetStands = target?.elements.filter((element) => !element.isDestroyed) ?? [];
  const targetStand = targetStands.find((element) => element.id === standChoice) ?? targetStands[0];
  const named = stands.filter((id) => able.some((element) => element.id === id));

  return (
    <div role="group" aria-label="Firefight">
      <span className="label">Firefight</span>
      <p className="constraint-line">
        Tick the stands in the order you would have them fire. Each one named spends its combat action,
        and if the check lets only part of the unit shoot, the first ones ticked are that part. Any
        firefight marks the target.
      </p>
      <div className="table-fields">
        {able.map((element) => (
          <label key={element.id}>
            {element.name}
            <input
              type="checkbox"
              checked={stands.includes(element.id)}
              onChange={(e) => setStands(e.target.checked
                ? [...stands, element.id]
                : stands.filter((id) => id !== element.id))}
            />
          </label>
        ))}
        <label>
          Small arms
          <select value={weapon} onChange={(e) => setWeaponChoice(e.target.value)}>
            {weapons.map((name) => <option key={name} value={name}>{name}</option>)}
          </select>
        </label>
        <label>
          Target platoon
          <select value={target?.id ?? ''} onChange={(e) => setTargetChoice(e.target.value)}>
            {targets.map((unit) => <option key={unit.id} value={unit.id}>{unit.name}</option>)}
          </select>
        </label>
        <label>
          Target stand
          <select value={targetStand?.id ?? ''} onChange={(e) => setStandChoice(e.target.value)}>
            {targetStands.map((element) => <option key={element.id} value={element.id}>{element.name}</option>)}
          </select>
        </label>
        <label>
          Firefight band
          <select value={measuredBand} onChange={(e) => setMeasuredBand(e.target.value)}>
            {bands.map((band) => <option key={band} value={band}>{band}</option>)}
          </select>
        </label>
        <button
          type="button"
          disabled={busy || named.length === 0 || !target || !targetStand}
          onClick={() => {
            if (target && targetStand) {
              void run(() => api.firefight(game, {
                weapon,
                standIds: named,
                targetUnitId: target.id,
                targetElementId: targetStand.id,
                measuredBand,
              }));
              setStands([]);
            }
          }}
        >
          Open Firefight
        </button>
      </div>
    </div>
  );
}

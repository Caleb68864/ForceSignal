/**
 * Every weapon kind the server can report has an effect.
 *
 * Read off the server's own enum rather than off `types.ts`, because the union there is a copy and
 * a copy is what falls behind: a kind added to `WeaponKind` in CombatContracts.cs and nowhere else
 * would reach the client as a string this registry had never heard of, and the map would draw
 * nothing for a shot that happened.
 */

import { describe, expect, it } from 'vitest';
import { weaponKinds } from '../../constants.ts';
import { fxRegistry, impactsFor, reportedFx } from './fxRegistry.ts';
import { reportedKinds } from './timeline.ts';
import type { FireEvent } from './timeline.ts';
import type { FiringResult } from '../../types.ts';

const contractSources = import.meta.glob('../../../../ForceSignal.Contracts/Matches/MatchContracts.cs', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

/** The event kinds the server can send, read off `TurnEventKinds` in the contracts. */
function serverEventKinds(): string[] {
  const text = Object.values(contractSources)[0] ?? '';
  const body = /public static class TurnEventKinds\s*\{([\s\S]*?)\n\}/.exec(text)?.[1] ?? '';
  return [...body.matchAll(/public const string (\w+) = "(\w+)";/g)].map((match) => match[2]);
}

const serverSources = import.meta.glob('../../../../ForceSignal.Domain/Rules/CombatContracts.cs', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

function serverWeaponKinds(): string[] {
  const text = Object.values(serverSources)[0] ?? '';
  const body = /public enum WeaponKind\s*\{([^}]*)\}/.exec(text)?.[1] ?? '';
  return body
    .split('\n')
    .map((line) => line.replace(/\/\/.*$/, '').trim())
    .filter((line) => line && !line.startsWith('///'))
    .map((line) => line.replace(/[,=].*$/, '').trim())
    .filter(Boolean);
}

describe('the FX registry covers every weapon kind', () => {
  it('has an effect for every member of the server enum', () => {
    // Reached-the-subject: the glob found the file and the reader found the enum.
    expect(Object.keys(serverSources)).toHaveLength(1);
    const kinds = serverWeaponKinds();
    expect(kinds).toEqual(expect.arrayContaining(['Beam', 'PulseTorpedo', 'NeedleBeam']));

    const missing = kinds.filter((kind) => !(kind in fxRegistry));
    expect(missing, 'add an entry to fxRegistry for each').toEqual([]);
  });

  it('knows the same kinds the client offers in the ship form', () => {
    expect(weaponKinds.map((option) => option.key).sort()).toEqual(serverWeaponKinds().sort());
  });

  it('gives every effect a visible duration and a name', () => {
    for (const [key, spec] of Object.entries(fxRegistry)) {
      expect(spec.travelMs, key).toBeGreaterThan(0);
      expect(spec.label, key).not.toBe('');
    }
  });
});

describe('the impact read off a result', () => {
  const event = (patch: Partial<FiringResult>, destroysTarget = false): FireEvent => ({
    kind: 'fire', step: 1, resultIndex: 0, fx: 'Beam', from: { x: 0, y: 0 }, to: { x: 1, y: 1 }, destroysTarget,
    result: { screenReduction: 0, armorDamageApplied: 0, hullDamageApplied: 0, damage: 0, ...patch } as FiringResult,
  });

  it('shimmers when screens took something off, and sparks when damage landed', () => {
    expect(impactsFor(event({ screenReduction: 2 }))).toEqual(['shimmer']);
    expect(impactsFor(event({ screenReduction: 1, armorDamageApplied: 2 }))).toEqual(['shimmer', 'sparks']);
    expect(impactsFor(event({ hullDamageApplied: 1 }, true))).toEqual(['sparks', 'explosion']);
  });

  it('shows a miss when nothing happened, rather than nothing', () => {
    expect(impactsFor(event({}))).toEqual(['miss']);
  });

  it('does not spark on damage the server rolled but did not apply', () => {
    // `damage` is what the dice scored; only the applied fields say a box was crossed off.
    expect(impactsFor(event({ damage: 4 }))).toEqual(['miss']);
  });
});

describe('every kind of turn event has a place and an effect', () => {
  it('shows every kind the server can send', () => {
    // Reached-the-subject: the glob found the contracts and the reader found the kinds.
    const kinds = serverEventKinds();
    expect(kinds).toEqual(expect.arrayContaining(['SalvoStruck', 'ThresholdChecked', 'ShipDestroyed']));

    // The moves and the shots have records of their own; every other kind is a reported event.
    const shownElsewhere = ['MovementResolved', 'WeaponFired'];
    const unplaced = kinds.filter((kind) => !shownElsewhere.includes(kind) && !(kind in reportedKinds));
    expect(unplaced, 'add the kind to reportedKinds in timeline.ts').toEqual([]);
  });

  it('has an effect for every reported kind', () => {
    for (const kind of Object.values(reportedKinds)) {
      expect(reportedFx[kind], kind).toBeDefined();
      expect(reportedFx[kind].label).not.toBe('');
    }
  });
});

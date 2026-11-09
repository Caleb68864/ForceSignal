/**
 * Which effect shows which attack, and which impact shows what the server said happened.
 *
 * Keyed on `FxKey`, so the type checker refuses a registry that leaves a weapon kind out; the guard
 * test also reads the server's enum, so a kind added there and not yet here fails a test rather than
 * quietly drawing nothing. Every effect is this project's own procedural drawing - no sprite sheets,
 * nothing borrowed - so there is nothing to credit in ATTRIBUTION.md.
 *
 * `impactsFor` decides nothing about the shot. It reads the fields the server filled in and picks
 * the picture for each: a shimmer where screens took something off, sparks where armour or hull
 * took damage, a burst where the timeline hung the kill. Comparisons only - the lint rule on this
 * folder refuses arithmetic on a result field.
 */

import type { FireEvent, FxKey } from './timeline.ts';

export type TravelStyle = 'beam' | 'projectile' | 'needle' | 'swarm' | 'flash';

export type FxSpec = {
  travel: TravelStyle;
  /** How long the attack takes to cross, before its impact plays. */
  travelMs: number;
  color: number;
  /** What the playback bar calls it. */
  label: string;
};

export const fxRegistry: Record<FxKey, FxSpec> = {
  Beam: { travel: 'beam', travelMs: 420, color: 0x47f1ff, label: 'Beam' },
  PulseTorpedo: { travel: 'projectile', travelMs: 900, color: 0xffb347, label: 'Torpedo' },
  NeedleBeam: { travel: 'needle', travelMs: 260, color: 0xd9a6ff, label: 'Needle' },
  FighterStrike: { travel: 'swarm', travelMs: 1100, color: 0xf5c766, label: 'Fighter strike' },
  // An older record with no kind. Drawn as a plain flash rather than guessed into a beam.
  Unknown: { travel: 'flash', travelMs: 300, color: 0xffffff, label: 'Attack' },
};

export type ImpactKind = 'shimmer' | 'sparks' | 'explosion' | 'miss';

export const impactMs: Record<ImpactKind, number> = { shimmer: 520, sparks: 480, explosion: 1100, miss: 360 };

export function impactsFor(event: FireEvent): ImpactKind[] {
  const { result } = event;
  const impacts: ImpactKind[] = [];
  if (result.screenReduction > 0) {
    impacts.push('shimmer');
  }

  if (result.armorDamageApplied > 0 || result.hullDamageApplied > 0) {
    impacts.push('sparks');
  }

  if (event.destroysTarget) {
    impacts.push('explosion');
  }

  return impacts.length > 0 ? impacts : ['miss'];
}

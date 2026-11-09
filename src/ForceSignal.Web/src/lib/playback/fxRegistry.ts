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

import type { FireEvent, FxKey, ReportedEvent } from './timeline.ts';

export type TravelStyle = 'beam' | 'projectile' | 'needle' | 'swarm' | 'flash' | 'salvo' | 'tracer' | 'flight' | 'none';

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

export type ImpactKind = 'shimmer' | 'sparks' | 'explosion' | 'miss' | 'intercept' | 'flare';

export const impactMs: Record<ImpactKind, number> = { shimmer: 520, sparks: 480, explosion: 1100, miss: 360, intercept: 420, flare: 640 };

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

/**
 * The effect for each kind of event the server's list reports beyond moves and shots. Keyed on the
 * timeline's kinds, which the guard test holds against the server's `TurnEventKinds`.
 */
export const reportedFx: Record<ReportedEvent['kind'], FxSpec> = {
  flight: { travel: 'flight', travelMs: 900, color: 0xf5c766, label: 'Fighters fly' },
  salvo: { travel: 'salvo', travelMs: 1000, color: 0xff8f5a, label: 'Salvo' },
  'salvo-wasted': { travel: 'none', travelMs: 1, color: 0x9aa6b2, label: 'Salvo wasted' },
  'point-defence': { travel: 'tracer', travelMs: 500, color: 0x9fffd0, label: 'Point defence' },
  'strike-stopped': { travel: 'none', travelMs: 1, color: 0x9aa6b2, label: 'Strike stopped' },
  threshold: { travel: 'none', travelMs: 1, color: 0xffd27a, label: 'Threshold check' },
  destroyed: { travel: 'none', travelMs: 1, color: 0xff7a2f, label: 'Destroyed' },
};

/** The impact for a reported event, read off the server's record as `impactsFor` reads a shot. */
export function impactsForReported(event: ReportedEvent): ImpactKind[] {
  switch (event.kind) {
    case 'salvo':
      return (event.event.armorApplied ?? 0) > 0 || (event.event.hullApplied ?? 0) > 0 ? ['sparks'] : ['miss'];
    case 'point-defence':
      return ['intercept'];
    case 'threshold':
      return ['flare'];
    case 'destroyed':
      return ['explosion'];
    case 'salvo-wasted':
    case 'strike-stopped':
      return ['miss'];
    case 'flight':
      return [];
  }
}

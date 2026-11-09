/**
 * Playback captions say exactly the numbers on the server's record, and no others.
 */

import { describe, expect, it } from 'vitest';
import firingComplete from './fixtures/05-turn1-firing-complete.json' with { type: 'json' };
import { normalizeMatchSnapshot } from '../normalize.ts';
import { captionFor } from './captions.ts';
import { buildTurnTimeline, type FireEvent, type ReportedEvent } from './timeline.ts';
import type { MatchSnapshot } from '../../types.ts';

const snapshot = normalizeMatchSnapshot(structuredClone(firingComplete) as unknown as MatchSnapshot);
const steps = buildTurnTimeline(snapshot, 1).events.filter((event): event is FireEvent | ReportedEvent => event.kind !== 'move');

/** The numbers a step's record carries that a caption may state. */
function recordNumbers(step: FireEvent | ReportedEvent): number[] {
  if (step.kind === 'fire') {
    const { result } = step;
    return [result.armorDamageApplied, result.hullDamageApplied, result.screenReduction];
  }

  const { event } = step;
  return [event.arriving, event.launched, event.kills, event.armorApplied, event.hullApplied]
    .filter((value): value is number => typeof value === 'number')
    .concat([0]);
}

/** Every number written in a caption, once ship names (which may carry digits) are taken out. */
function captionNumbers(caption: string): number[] {
  let text = caption;
  for (const ship of [...snapshot.ships].sort((a, b) => b.name.length - a.name.length)) {
    text = text.split(ship.name).join('');
  }

  return (text.match(/\d+/g) ?? []).map(Number);
}

describe('playback captions', () => {
  it('have one for every step of a real turn, and none is empty', () => {
    // Reached-the-subject: the turn really has every kind of step in it.
    expect(new Set(steps.map((step) => step.kind))).toEqual(new Set(['flight', 'salvo', 'threshold', 'fire', 'destroyed', 'point-defence']));
    for (const step of steps) {
      expect(captionFor(step, snapshot).length, step.kind).toBeGreaterThan(5);
    }
  });

  it('state the damage actually applied, never a number the record does not carry', () => {
    for (const step of steps) {
      const caption = captionFor(step, snapshot);
      const allowed = recordNumbers(step);
      expect(captionNumbers(caption).filter((value) => !allowed.includes(value)), caption).toEqual([]);
      if (step.kind === 'fire' && (step.result.armorDamageApplied > 0 || step.result.hullDamageApplied > 0)) {
        expect(caption).toContain(`${step.result.armorDamageApplied} armour, ${step.result.hullDamageApplied} hull`);
      }

      if (step.kind === 'salvo') {
        expect(caption).toContain(`${step.event.hullApplied ?? 0} hull`);
        expect(caption).toContain(`${step.event.arriving} of ${step.event.launched}`);
      }
    }
  });

  it('name the systems a threshold check took', () => {
    const captions = steps.filter((step) => step.kind === 'threshold').map((step) => captionFor(step, snapshot));
    expect(captions.some((caption) => caption.includes('lost Battery R'))).toBe(true);
  });
});

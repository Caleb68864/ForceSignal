/**
 * The playback clock: what plays when, and what reduced motion turns it into.
 */

import { describe, expect, it } from 'vitest';
import firingComplete from './fixtures/05-turn1-firing-complete.json' with { type: 'json' };
import { normalizeMatchSnapshot } from '../normalize.ts';
import { frameAt, schedulePlayback, startTimeAfter } from './playback.ts';
import { buildTurnTimeline } from './timeline.ts';
import type { MatchSnapshot } from '../../types.ts';

const snapshot = normalizeMatchSnapshot(structuredClone(firingComplete) as unknown as MatchSnapshot);
const timeline = buildTurnTimeline(snapshot, snapshot.turnNumber);
const finalPoses = () => new Map(
  timeline.events.flatMap((event) => (event.kind === 'move' ? [[event.shipId, { ...event.legs.at(-1)?.to, course: event.endCourse }]] : [])),
);

describe('with reduced motion', () => {
  const schedule = schedulePlayback(timeline, { reducedMotion: true });

  it('takes no time at all', () => {
    expect(schedule.duration).toBe(0);
    expect(schedule.steps.length).toBe(timeline.events.filter((event) => event.kind !== 'move').length);
  });

  it('shows the end state from the first frame, with no effect running and every step settled', () => {
    for (const time of [0, 1, 500, 10_000]) {
      const frame = frameAt(schedule, time);
      expect(frame.done).toBe(true);
      expect(frame.active).toEqual([]);
      expect(frame.settled).toBe(schedule.steps.length);
      for (const [shipId, pose] of finalPoses()) {
        expect(frame.poses.get(shipId)).toEqual(pose);
      }
    }
  });
});

describe('with motion', () => {
  const schedule = schedulePlayback(timeline, { reducedMotion: false });

  it('flies every ship from its first leg to where the server has it', () => {
    const start = frameAt(schedule, schedule.moveStart);
    for (const event of timeline.events) {
      if (event.kind === 'move') {
        expect(start.poses.get(event.shipId)).toMatchObject(event.legs[0].from);
      }
    }

    const end = frameAt(schedule, schedule.moveEnd).poses;
    for (const [shipId, pose] of finalPoses()) {
      expect(end.get(shipId)).toEqual(pose);
    }
  });

  it('plays one step at a time, in the server order, with the moves at their place', () => {
    let previousEnd = 0;
    const timelineOrder = timeline.events.filter((event) => event.kind !== 'move');
    schedule.steps.forEach((step, index) => {
      expect(step.event).toBe(timelineOrder[index]);
      expect(step.start).toBeGreaterThanOrEqual(previousEnd);
      previousEnd = step.end;
      const during = frameAt(schedule, (step.start + step.impactAt) / 2);
      expect(during.active.map((active) => active.step)).toEqual(step.impactAt > step.start + 1 ? [step] : during.active.map((active) => active.step));
      expect(during.settled).toBe(index);
    });
    // The moves play after the flight and before the salvo, as the server resolved them.
    const flight = schedule.steps.find((step) => step.event.kind === 'flight');
    const salvo = schedule.steps.find((step) => step.event.kind === 'salvo');
    expect(flight && flight.end).toBeLessThanOrEqual(schedule.moveStart);
    expect(salvo && salvo.start).toBeGreaterThanOrEqual(schedule.moveEnd);
  });

  it('flies a fighter group along its flight, and leaves it where it landed', () => {
    const flight = schedule.steps.find((step) => step.event.kind === 'flight');
    expect(flight).toBeDefined();
    if (!flight || flight.event.kind !== 'flight') {
      return;
    }

    const group = flight.event.event.actorShipId ?? '';
    expect(frameAt(schedule, 0).poses.get(group)).toMatchObject(flight.event.from);
    expect(frameAt(schedule, schedule.duration).poses.get(group)).toMatchObject(flight.event.to);
  });

  it('ends settled, with nothing left running', () => {
    const end = frameAt(schedule, schedule.duration);
    expect(end.done).toBe(true);
    expect(end.active).toEqual([]);
    expect(end.settled).toBe(schedule.steps.length);
  });

  it('can start after the shots a device has already watched', () => {
    expect(startTimeAfter(schedule, 0)).toBe(0);
    expect(startTimeAfter(schedule, 2)).toBe(schedule.steps[2].start);
    expect(startTimeAfter(schedule, schedule.steps.length)).toBe(schedule.duration);
  });
});

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
    expect(schedule.fires.length).toBe(snapshot.firingResults.length);
  });

  it('shows the end state from the first frame, with no effect running and every shot settled', () => {
    for (const time of [0, 1, 500, 10_000]) {
      const frame = frameAt(schedule, time);
      expect(frame.done).toBe(true);
      expect(frame.active).toEqual([]);
      expect(frame.settled).toBe(snapshot.firingResults.length);
      expect(frame.poses).toEqual(finalPoses());
    }
  });
});

describe('with motion', () => {
  const schedule = schedulePlayback(timeline, { reducedMotion: false });

  it('flies every ship from its first leg to where the server has it', () => {
    const start = frameAt(schedule, 0);
    for (const event of timeline.events) {
      if (event.kind === 'move') {
        expect(start.poses.get(event.shipId)).toMatchObject(event.legs[0].from);
      }
    }

    expect(frameAt(schedule, schedule.moveEnd).poses).toEqual(finalPoses());
  });

  it('plays one shot at a time, in the server order, after the moves', () => {
    let previousEnd = schedule.moveEnd;
    schedule.fires.forEach((fire, index) => {
      expect(fire.event.resultIndex).toBe(index);
      expect(fire.start).toBeGreaterThanOrEqual(previousEnd);
      previousEnd = fire.end;
      const during = frameAt(schedule, (fire.start + fire.impactAt) / 2);
      expect(during.active.map((active) => active.fire)).toEqual([fire]);
      expect(during.active[0].phase).toBe('travel');
      expect(during.settled).toBe(index);
    });
  });

  it('ends settled, with nothing left running', () => {
    const end = frameAt(schedule, schedule.duration);
    expect(end.done).toBe(true);
    expect(end.active).toEqual([]);
    expect(end.settled).toBe(schedule.fires.length);
  });

  it('can start after the shots a device has already watched', () => {
    expect(startTimeAfter(schedule, 0)).toBe(0);
    expect(startTimeAfter(schedule, 2)).toBe(schedule.fires[2].start);
    expect(startTimeAfter(schedule, schedule.fires.length)).toBe(schedule.duration);
  });
});

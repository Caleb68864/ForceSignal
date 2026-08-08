import { describe, expect, it } from 'vitest';
import { clampTurnManeuvers, maxLegalTurn, plannedSegments, previewCourse, totalTurnSteps, usableThrust } from './movement.ts';
import type { DraftOrder } from '../types.ts';

const draft = (patch: Partial<DraftOrder> = {}): DraftOrder => ({
  velocityDelta: 0,
  turnSteps: 0,
  turnDirection: 'None',
  turnManeuvers: [],
  salt: 'test-salt',
  ...patch,
});

describe('usableThrust', () => {
  // Drives are lost in halves, and what is left is what may be plotted with.
  it('is what the drives have left', () => {
    expect(usableThrust({ thrustRating: 6, driveDamage: 0 })).toBe(6);
    expect(usableThrust({ thrustRating: 6, driveDamage: 3 })).toBe(3);
  });

  it('never goes below nothing', () => {
    expect(usableThrust({ thrustRating: 4, driveDamage: 9 })).toBe(0);
  });
});

describe('maxLegalTurn', () => {
  // Turn points cannot exceed half the thrust rating, rounded up, and whatever the velocity change
  // already spent comes off first.
  it('is half the thrust rounded up, less what acceleration took', () => {
    expect(maxLegalTurn(6, 0)).toBe(3);
    expect(maxLegalTurn(5, 0)).toBe(3);
    expect(maxLegalTurn(6, 4)).toBe(2);
  });

  it('is nothing when the whole budget went on velocity', () => {
    expect(maxLegalTurn(4, 4)).toBe(0);
    expect(maxLegalTurn(4, 9)).toBe(0);
  });
});

describe('clampTurnManeuvers', () => {
  it('leaves a plot inside the cap alone', () => {
    const maneuvers = [{ direction: 'Port' as const, steps: 2 }];
    expect(totalTurnSteps(draft({ turnManeuvers: maneuvers }))).toBe(2);
    expect(clampTurnManeuvers(maneuvers, 3)).toEqual(maneuvers);
  });

  it('trims a plot that spends more turn than the ship has', () => {
    const trimmed = clampTurnManeuvers(
      [{ direction: 'Port', steps: 2 }, { direction: 'Starboard', steps: 3 }], 3);
    expect(trimmed.reduce((sum, m) => sum + m.steps, 0)).toBeLessThanOrEqual(3);
  });

  it('drops everything when there is no turn to spend', () => {
    expect(clampTurnManeuvers([{ direction: 'Port', steps: 2 }], 0)).toEqual([]);
  });
});

describe('previewCourse', () => {
  it('shows where the helm ends up', () => {
    expect(previewCourse(12, draft({ turnManeuvers: [{ direction: 'Starboard', steps: 1 }] }))).toBe(1);
  });

  // Turning to port from course 1 is the wrap that a plain modulo gets wrong.
  it('wraps to port across the top of the clock', () => {
    expect(previewCourse(1, draft({ turnManeuvers: [{ direction: 'Port', steps: 1 }] }))).toBe(12);
    expect(previewCourse(1, draft({ turnManeuvers: [{ direction: 'Port', steps: 2 }] }))).toBe(11);
  });

  it('leaves a ship that is not turning on its course', () => {
    expect(previewCourse(7, draft())).toBe(7);
  });
});

describe('plannedSegments', () => {
  it('runs a straight leg when nothing turns', () => {
    const segments = plannedSegments(12, 8, draft());
    expect(segments).toHaveLength(1);
    expect(segments[0]).toMatchObject({ course: 12, distance: 8 });
  });

  /**
   * A plotted turn is made half at the start of the move and half at the mid-point, which is what
   * puts a ship where the rulebook's worked examples say it ends up. Rounding down is why a
   * single-point turn happens entirely at the mid-point rather than at the start.
   */
  it('splits a turn across the move rather than pivoting up front', () => {
    const segments = plannedSegments(12, 8, draft({ turnManeuvers: [{ direction: 'Starboard', steps: 2 }] }));
    expect(segments.length).toBeGreaterThan(1);
    expect(segments.reduce((sum, s) => sum + s.distance, 0)).toBeCloseTo(8);
    expect(segments[segments.length - 1].course).toBe(2);
  });

  it('holds a single-point turn until the mid-point', () => {
    const segments = plannedSegments(12, 8, draft({ turnManeuvers: [{ direction: 'Starboard', steps: 1 }] }));
    expect(segments[0].course).toBe(12);
    expect(segments[segments.length - 1].course).toBe(1);
  });
});

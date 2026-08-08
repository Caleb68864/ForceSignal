import { describe, expect, it } from 'vitest';
import { courseAngle, distanceBetweenShips, mapPercent, wrapCourse } from './geometry.ts';
import type { Ship } from '../types.ts';

describe('wrapCourse', () => {
  it('leaves a course already on the clock alone', () => {
    expect(wrapCourse(1)).toBe(1);
    expect(wrapCourse(12)).toBe(12);
  });

  it('wraps past the top of the clock', () => {
    expect(wrapCourse(13)).toBe(1);
    expect(wrapCourse(25)).toBe(1);
  });

  // The clock is one-based, so the correction is not a plain modulo. Turning to port from course 1
  // is the case that gets this wrong.
  it('wraps below the bottom of the clock', () => {
    expect(wrapCourse(0)).toBe(12);
    expect(wrapCourse(-1)).toBe(11);
    expect(wrapCourse(-11)).toBe(1);
    expect(wrapCourse(-12)).toBe(12);
  });
});

describe('courseAngle', () => {
  it('turns a clock point into degrees', () => {
    expect(courseAngle(12)).toBe(360);
    expect(courseAngle(3)).toBe(90);
  });
});

describe('mapPercent', () => {
  it('places a position as a fraction of the table', () => {
    expect(mapPercent(36, 72)).toBe(50);
    expect(mapPercent(0, 72)).toBe(0);
  });
});

describe('distanceBetweenShips', () => {
  const at = (x: number, y: number) => ({ positionX: x, positionY: y } as Ship);

  it('measures a straight line', () => {
    expect(distanceBetweenShips(at(0, 0), at(3, 4))).toBe(5);
  });

  it('is the same measured either way round', () => {
    expect(distanceBetweenShips(at(1, 2), at(9, 8)))
      .toBe(distanceBetweenShips(at(9, 8), at(1, 2)));
  });

  it('is nothing between a ship and itself', () => {
    expect(distanceBetweenShips(at(5, 5), at(5, 5))).toBe(0);
  });
});

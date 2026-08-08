import { describe, expect, it } from 'vitest';
import { bearingArc, hullRowsFor, rangeDisagreesWithMap, torpedoToHitNumber } from './rules.ts';
import type { Ship } from '../types.ts';

/**
 * The arc a target bears in is computed twice - here, to tell the player which arc they are
 * shooting through, and again on the server, which is the authority and will refuse a shot that
 * does not match. So these are really consistency tests: the two have to agree, especially at the
 * boundaries, or the map says one thing and the server says another.
 */
function shipAt(x: number, y: number, course = 12): Ship {
  return {
    id: 'x', fleetId: 'f', name: 'S', className: undefined, thrustRating: 4,
    currentVelocity: 0, currentCourse: course, positionX: x, positionY: y,
    hullMax: 10, hullDamage: 0, armorMax: 0, armorDamage: 0,
    fireControlMax: 1, fireControlDamage: 0, pointDefenseSystems: 0,
    fighterBays: 0, fighterBayDamage: 0, damageControlParties: 0,
    driveDamage: 0, weaponDamage: 0, screenRating: 0, screenDamage: 0,
    weapons: [], isDestroyed: false, hullRows: [], hullRowsCompleted: 0,
    iconKey: 'cruiser', fighterEnduranceMax: 0, fighterEnduranceUsed: 0,
    fighterMaxRange: 0, fighterStatus: 'Docked', homeCarrierShipId: undefined, pointsValue: 0,
  } as Ship;
}

const root3 = Math.sqrt(3);

describe('bearingArc', () => {
  // A ship on course 12 points up the table. These are the six boundaries between the arcs,
  // measured from a round-numbered position - exactly what a player produces by dragging a ship
  // onto a grid intersection. The documented rule is that a boundary reads as the more clockwise
  // arc, which is how the arcs are named.
  it.each([
    [1, -root3, 'ForeStarboard'],
    [1, 0, 'AftStarboard'],
    [1, root3, 'Aft'],
    [-1, root3, 'AftPort'],
    [-1, 0, 'ForePort'],
    [-1, -root3, 'Fore'],
  ])('at offset %s,%s reads as %s', (dx, dy, expected) => {
    expect(bearingArc(shipAt(0, 0), shipAt(dx, dy))).toBe(expected);
  });

  it('does not flip across a boundary on a rounding error', () => {
    const hair = 1e-13;
    expect(bearingArc(shipAt(0, 0), shipAt(1, hair))).toBe('AftStarboard');
    expect(bearingArc(shipAt(0, 0), shipAt(1, -hair))).toBe('AftStarboard');
  });

  it('reads dead ahead for a ship stacked on top of another', () => {
    expect(bearingArc(shipAt(5, 5), shipAt(5, 5))).toBe('Fore');
  });

  it('turns with the firing ship', () => {
    // Directly above: dead ahead on course 12, dead astern on course 6.
    expect(bearingArc(shipAt(0, 0, 12), shipAt(0, -10))).toBe('Fore');
    expect(bearingArc(shipAt(0, 0, 6), shipAt(0, -10))).toBe('Aft');
  });

  it('has nothing to say without a target', () => {
    expect(bearingArc(shipAt(0, 0), undefined)).toBeNull();
  });
});

describe('hullRowsFor', () => {
  it('splits a hull into four rows, giving the remainder to the earlier ones', () => {
    expect(hullRowsFor(12)).toEqual([3, 3, 3, 3]);
    expect(hullRowsFor(10)).toEqual([3, 3, 2, 2]);
    expect(hullRowsFor(9)).toEqual([3, 2, 2, 2]);
  });

  it('gives a hull too small to split one box per row', () => {
    expect(hullRowsFor(3)).toEqual([1, 1, 1]);
    expect(hullRowsFor(1)).toEqual([1]);
  });

  it('has no rows for a hull of nothing', () => {
    expect(hullRowsFor(0)).toEqual([]);
    expect(hullRowsFor(-4)).toEqual([]);
  });
});

describe('torpedoToHitNumber', () => {
  // A torpedo needs 2 inside the first band and one worse every band out, to a ceiling of 6.
  it.each([[1, 2], [6, 2], [7, 3], [12, 3], [24, 5], [30, 6], [90, 6]])(
    'needs %i+ at range %i', (range, needed) => {
      expect(torpedoToHitNumber(range)).toBe(needed);
    });
});

describe('rangeDisagreesWithMap', () => {
  it('says nothing when the declared range and the map agree', () => {
    expect(rangeDisagreesWithMap(10, 10, 'Beam')).toBe(false);
  });

  it('flags a declared range that lands in a different band', () => {
    expect(rangeDisagreesWithMap(10, 20, 'Beam')).toBe(true);
  });
});

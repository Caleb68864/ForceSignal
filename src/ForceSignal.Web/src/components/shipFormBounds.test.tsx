// @vitest-environment jsdom
/**
 * The bounds the new-ship form actually types within.
 *
 * Each of these is written twice - once as `min`/`max` on an input here, once as a `Math.Clamp` in
 * `InMemoryMatchService` - and nothing held the pair together. `shipFormBounds.json` holds them
 * now, and `ShipFormBoundsParityTests` runs the same numbers against the service through
 * `CreateShip`, so a change on either side reddens one of the two suites.
 *
 * This half reads them off the rendered control rather than off `ShipProfileFields`' source, because
 * what bounds a player is the attribute on the input and the clamp inside `onChange`, and a guard
 * that read the source would pass on a form that renders something else.
 */

import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { defaultShipForm } from '../constants.ts';
import bounds from '../lib/shipFormBounds.json?raw';
import { ShipProfileFields } from './ShipCard.tsx';

afterEach(cleanup);

const fixture = JSON.parse(bounds) as {
  bounds: { field: string; label: string; min: number; max: number; why: string }[];
};

describe('the ship form types within the bounds the service clamps to', () => {
  it('gives every bounded control the min and max the fixture names', () => {
    // Reached-the-subject: the fixture really parsed, and it really still carries the fire control
    // entry - the one this file was written for. A fixture reduced to nothing would leave the walk
    // below iterating an empty list and reporting success.
    expect(fixture.bounds.length).toBeGreaterThanOrEqual(7);
    expect(fixture.bounds.some((bound) => bound.field === 'fireControlMax' && bound.max === 6)).toBe(true);

    render(<ShipProfileFields form={defaultShipForm} onChange={() => undefined} />);

    for (const bound of fixture.bounds) {
      const input = screen.getByLabelText(bound.label) as HTMLInputElement;

      expect(input.getAttribute('min'), `${bound.label} min`).toBe(String(bound.min));
      expect(input.getAttribute('max'), `${bound.label} max`).toBe(String(bound.max));
    }
  });

  it('makes every entry argue for itself rather than just naming a number', () => {
    // The list not rotting into decoration. A bound with no reason written beside it is the
    // "it has always been 6" that this repository has twice decided is not an argument.
    for (const bound of fixture.bounds) {
      expect(bound.why.length, `${bound.field} has no reason written`).toBeGreaterThan(10);
    }
  });
});

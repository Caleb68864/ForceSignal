/**
 * The ordnance ceilings the client enforces, held to the fixture the service is held to.
 *
 * The four numbers in `normalizeOrdnanceMarker` were each a restatement of a `Math.Clamp` in
 * `InMemoryMatchService`, and all four had drifted wider - 120 / 48 / 48 / 240 against the service's
 * 72 / 24 / 24 / 120 - so a restored snapshot showed a salvo the server would immediately halve.
 *
 * This half reads them back through `normalizeMatchSnapshot`, the function the app actually calls,
 * rather than off the source of `normalizeOrdnanceMarker`, which is not exported and which a guard
 * reading source could pass over while the front door did something else.
 */

import { describe, expect, it } from 'vitest';
import bounds from './ordnanceBounds.json' with { type: 'json' };
import { normalizeMatchSnapshot } from './normalize.ts';
import type { MatchSnapshot } from '../types.ts';

const fields = ['speed', 'enduranceRemaining', 'attackDice', 'maxRange'] as const;

function markerAsking(value: number) {
  const snapshot = {
    ordnanceMarkers: [{
      id: 'marker-1',
      ownerParticipantId: 'p1',
      name: 'Salvo',
      markerType: 'Missile',
      positionX: 10,
      positionY: 10,
      course: 12,
      speed: value,
      enduranceRemaining: value,
      attackDice: value,
      maxRange: value,
      status: 'Active',
    }],
  } as unknown as MatchSnapshot;

  return normalizeMatchSnapshot(snapshot).ordnanceMarkers[0];
}

describe('the client clamps an ordnance marker to exactly what the service does', () => {
  it('holds a marker asking for far too much to the fixture ceiling', () => {
    // Reached-the-subject: the fixture parsed and still names every field this walk is about. A
    // fixture reduced to `{}` would leave the loop below iterating nothing and reporting success.
    for (const field of fields) {
      expect(bounds.bounds[field], `${field} is missing from the fixture`).toBeGreaterThan(0);
    }

    const marker = markerAsking(9999);

    // And the marker really arrived - a normalizer that dropped it would leave this undefined and
    // every assertion below reading from nothing.
    expect(marker?.id).toBe('marker-1');

    for (const field of fields) {
      expect(marker[field], `${field} ceiling`).toBe(bounds.bounds[field]);
    }
  });

  it('floors a marker asking for less than nothing at the fixture floor', () => {
    const marker = markerAsking(-9999);
    expect(marker?.id).toBe('marker-1');

    for (const field of fields) {
      expect(marker[field], `${field} floor`).toBe(bounds.floors[field]);
    }
  });

  it('passes a value inside the bounds through untouched', () => {
    // The control that must be accepted. A guard that clamped everything to the ceiling would pass
    // the two tests above and be an over-strict fix of exactly the kind this repository has paid
    // for: a table playing a 12-speed salvo has to get 12 back.
    const marker = markerAsking(12);
    expect(marker?.id).toBe('marker-1');

    for (const field of fields) {
      expect(marker[field], `${field} inside the bounds`).toBe(12);
    }
  });
});

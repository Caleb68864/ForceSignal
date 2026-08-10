import { describe, expect, it } from 'vitest';
import { hullRowsFor } from './rules.ts';

/**
 * Arc bearing, the torpedo to-hit table and the range cross-check used to be tested here because
 * the client computed them too. It no longer does - the firing solution query answers all three
 * from the server's own checks - so they are covered where the code now lives, in
 * `InMemoryMatchServiceFiringSolutionTests` and `FiringArcsTests`.
 */

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

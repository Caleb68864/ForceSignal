import { describe, expect, it } from 'vitest';
import { spreadOffsets } from './mapSpread.ts';

describe('fanning out contacts on the same spot (playtest M6)', () => {
  it('leaves a contact on its own exactly where it is', () => {
    expect(spreadOffsets([{ id: 'a', x: 10, y: 10 }, { id: 'b', x: 40, y: 30 }], 2).get('a')).toEqual({ dx: 0, dy: 0 });
  });

  it('gives every stacked contact its own place, far enough apart to tap', () => {
    const stacked = Array.from({ length: 8 }, (_, index) => ({ id: `s${index}`, x: 20, y: 20 + index * 0.1 }));
    const offsets = [...spreadOffsets(stacked, 2).values()];

    expect(new Set(offsets.map((offset) => `${offset.dx},${offset.dy}`)).size).toBe(8);
    for (let a = 0; a < offsets.length; a += 1) {
      for (let b = a + 1; b < offsets.length; b += 1) {
        expect(Math.hypot(offsets[a].dx - offsets[b].dx, offsets[a].dy - offsets[b].dy)).toBeGreaterThanOrEqual(40);
      }
    }
  });
});

import { describe, expect, it } from 'vitest';
import { newestLogEntry } from './newestLogEntry.ts';

describe('the entry a command added to the log', () => {
  it('is the newest one when the log grew', () => {
    expect(newestLogEntry(['a'], ['a', 'b'])).toBe('b');
  });

  it('is nothing when the log did not change', () => {
    expect(newestLogEntry(['a', 'b'], ['a', 'b'])).toBeUndefined();
    expect(newestLogEntry([], [])).toBeUndefined();
  });

  it('is still found once the log is at its cap and the oldest entry was dropped for it', () => {
    // The server keeps the newest 2000 entries, so past that a logged line leaves the length alone.
    const full = Array.from({ length: 2000 }, (_, index) => `entry ${index}`);
    const after = [...full.slice(1), 'Alpha Squad fired on Bravo Squad.'];

    expect(newestLogEntry(full, after)).toBe('Alpha Squad fired on Bravo Squad.');
  });
});

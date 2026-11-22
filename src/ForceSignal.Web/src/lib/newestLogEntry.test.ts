import { describe, expect, it } from 'vitest';
import { newestLogEntry } from './newestLogEntry.ts';

describe('the entry a command added to the log', () => {
  it('is the newest one when the log grew', () => {
    expect(newestLogEntry({ log: ['a'] }, { log: ['a', 'b'] })).toBe('b');
  });

  it('is nothing when the log did not change', () => {
    expect(newestLogEntry({ log: ['a', 'b'] }, { log: ['a', 'b'] })).toBeUndefined();
    expect(newestLogEntry({ log: [] }, { log: [] })).toBeUndefined();
  });

  it('is still found at the cap from an older server, by the newest entry changing', () => {
    const full = Array.from({ length: 5 }, (_, index) => `entry ${index}`);

    expect(newestLogEntry({ log: full }, { log: [...full.slice(1), 'Fired.'] })).toBe('Fired.');
  });

  it('counts the same sentence twice at the cap, by where the log starts', () => {
    // Text alone cannot see this: the newest line is the same before and after.
    const full = [...Array.from({ length: 4 }, (_, index) => `entry ${index}`), 'Alpha tested its nerve.'];

    expect(newestLogEntry(
      { log: full, logStart: 7 },
      { log: [...full.slice(1), 'Alpha tested its nerve.'], logStart: 8 },
    )).toBe('Alpha tested its nerve.');
  });

  it('takes the position over the text when the server sends one', () => {
    expect(newestLogEntry({ log: ['a', 'b'], logStart: 3 }, { log: ['a', 'c'], logStart: 3 })).toBeUndefined();
  });
});

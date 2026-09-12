import { describe, expect, it } from 'vitest';
import { leadershipValuesOf, leadershipValuesSummary } from './leadershipValues.ts';

/**
 * Which numbers a screen may offer as Leadership Values.
 *
 * The add-a-squad panel offered 1, 2 and 3 with "(best)" beside the first, and the add-a-platoon
 * panel took anything from 0 to 9. Both were bounds this app had put on a record card off a page it
 * does not have - and the second was not even a guard, because the server behind it took 99.
 *
 * The invented range here is 2 to 5, deliberately the wrong way round from the one that used to be
 * written in: a screen that still knew about 1 to 3 would offer a 1 no table here has and would
 * leave out the 4 and 5 they do.
 */
describe('the Leadership Values a game plays with', () => {
  const entered = { lowestLeadershipValue: 2, highestLeadershipValue: 5 };

  it('is every number from the lowest entered to the highest', () => {
    expect(leadershipValuesOf(entered)).toEqual([2, 3, 4, 5]);
  });

  it('offers nothing at all when nobody has entered them', () => {
    expect(leadershipValuesOf({})).toEqual([]);
    expect(leadershipValuesOf(null)).toEqual([]);
    expect(leadershipValuesOf(undefined)).toEqual([]);
  });

  it('offers nothing on half a range, rather than inventing the other end', () => {
    expect(leadershipValuesOf({ lowestLeadershipValue: 2 })).toEqual([]);
    expect(leadershipValuesOf({ highestLeadershipValue: 5 })).toEqual([]);
  });

  it('offers nothing when the two ends are the wrong way round', () => {
    expect(leadershipValuesOf({ lowestLeadershipValue: 5, highestLeadershipValue: 2 })).toEqual([]);
  });

  it('takes a single-value range, because one number is a set a table may have', () => {
    expect(leadershipValuesOf({ lowestLeadershipValue: 4, highestLeadershipValue: 4 })).toEqual([4]);
  });

  it('takes values at or below zero, because no floor here is this app\'s to set', () => {
    expect(leadershipValuesOf({ lowestLeadershipValue: -1, highestLeadershipValue: 1 })).toEqual([-1, 0, 1]);
  });

  it('will not build a control out of a mistyped range', () => {
    // A cap on a dropdown and not on a rule: the server has no such ceiling, and a table with a
    // hundred distinct Leadership Values does not exist. Without it a typo builds a select with a
    // million children and takes the tab with it.
    const huge = leadershipValuesOf({ lowestLeadershipValue: 1, highestLeadershipValue: 1_000_000 });

    expect(huge.length).toBeLessThanOrEqual(100);
    expect(huge[0]).toBe(1);
  });

  it('reads the table\'s own two numbers back at them', () => {
    const said = leadershipValuesSummary(entered);

    expect(said).toContain('2 to 5');
    // The bound that used to be recited at a player who typed something else.
    expect(said).not.toContain('1 to 3');
  });

  it('says plainly that nothing has been entered, without saying what would be', () => {
    const said = leadershipValuesSummary({});

    expect(said).toContain('Nobody has entered');
    expect(said).toContain('your own rulebook');
    expect(said).not.toMatch(/\d/);
  });
});

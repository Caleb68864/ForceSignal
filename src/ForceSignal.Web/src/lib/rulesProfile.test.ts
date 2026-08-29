import { describe, expect, it } from 'vitest';
import { blankRulesProfile } from '../types.ts';
import { gapsIn } from './rulesProfile.ts';

/** Enough of a profile to play against, invented so the test proves nothing about any rulebook. */
const complete = () => ({
  ...blankRulesProfile,
  name: 'Invented',
  dieFaces: 8,
  beamDamage: [{ dieFace: 8, screenLevel: 0, damage: 2 }, { dieFace: 5, screenLevel: 0, damage: 1 }],
  beamRangeBandWidth: 10,
  thresholdRowCount: 3,
});

describe('gapsIn', () => {
  it('finds nothing wrong with a complete profile', () => {
    expect(gapsIn(complete())).toEqual([]);
  });

  it('refuses a beam entry for a face the die does not have', () => {
    // A wrongly keyed import once produced face-0 rows the profile accepted without a word.
    const profile = complete();
    profile.beamDamage = [...profile.beamDamage, { dieFace: 0, screenLevel: 0, damage: 1 }];
    expect(gapsIn(profile)).toEqual([expect.stringContaining('8-sided die does not have')]);
  });

  it('refuses point defence and turnaround rows off the die too', () => {
    const pointDefence = { ...complete(), pointDefenseRange: 5, pointDefenseKills: [{ dieFace: 9, kills: 1 }] };
    const turnaround = { ...complete(), carrierTurnaroundRoll: true, turnaround: [{ dieFace: 0, isGroundedForGame: false, turnsBeforeRelaunch: 1 }] };
    expect(gapsIn(pointDefence)).toHaveLength(1);
    expect(gapsIn(turnaround)).toHaveLength(1);
  });
});

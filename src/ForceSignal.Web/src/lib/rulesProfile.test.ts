import { describe, expect, it } from 'vitest';
import { blankRulesProfile } from '../types.ts';
import { gapsIn, readProfileFile } from './rulesProfile.ts';

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

/**
 * An import that arrives with gaps is reported rather than applied.
 *
 * The profile is the one thing this app makes the player type by hand, thirty fields off their own
 * rulebook. A previous fix closed *the wrong sort of file entirely* - a fleet export read as a
 * profile blanked all thirty. It did not close **the right sort of file, missing most of it**:
 * `{ name, dieFaces }` cleared the two-field bar, and the twenty-eight fields it did not carry were
 * spread over the blank profile and written to the form as zeros. Same loss, narrower door.
 *
 * Nothing below names a field that must be present. The check walks every key the blank profile has,
 * so a field **added** to `RulesProfile` tomorrow is covered the day it is added rather than the day
 * somebody remembers to list it here.
 */
describe('readProfileFile', () => {
  it('loads a file that carries the whole profile', () => {
    // The control. If this ever fails the rest of this block proves nothing, because a checker that
    // refuses everything reports every gap correctly and is still useless.
    const result = readProfileFile(JSON.stringify(complete()));

    expect(result.ok).toBe(true);
    expect(result.ok && result.profile.name).toBe('Invented');
    expect(result.ok && result.profile.beamRangeBandWidth).toBe(10);
  });

  it('refuses a two-field file rather than blanking the other twenty-eight', () => {
    const result = readProfileFile(JSON.stringify({ name: 'Half A Layer', dieFaces: 8 }));

    expect(result.ok).toBe(false);
    // Named, not counted only: the player has to know which numbers to go and find.
    expect(result.ok || result.problem).toMatch(/beamRangeBandWidth/);
    expect(result.ok || result.problem).toMatch(/thresholdRowCount/);
    expect(result.ok || result.problem).toMatch(new RegExp(String(Object.keys(blankRulesProfile).length - 2)));
  });

  it('reports any single field a file leaves out, whichever field that is', () => {
    // Reached-the-subject: there really are thirty of them, so this loop is not vacuously green.
    const fields = Object.keys(blankRulesProfile);
    expect(fields.length).toBeGreaterThan(25);

    const accepted: string[] = [];
    for (const field of fields) {
      const partial: Record<string, unknown> = { ...complete() };
      delete partial[field];
      const result = readProfileFile(JSON.stringify(partial));
      if (result.ok || !result.problem.includes(field)) {
        accepted.push(field);
      }
    }

    expect(accepted).toEqual([]);
  });

  it('still refuses a file that is not a profile at all, and unreadable text', () => {
    const fleet = readProfileFile(JSON.stringify({ formatVersion: 1, side: 'blue', units: [] }));
    const rubbish = readProfileFile('not JSON');

    expect(fleet.ok).toBe(false);
    expect(rubbish.ok).toBe(false);
    expect(fleet.ok || fleet.problem).toMatch(/could not be read/i);
  });
});

import { describe, expect, it } from 'vitest';
import { blankRulesProfile, type RulesProfile } from '../types.ts';
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
 * An import that would blank what the player has typed is reported rather than applied.
 *
 * The profile is the one thing this app makes the player type by hand, thirty fields off their own
 * rulebook. A previous fix closed *the wrong sort of file entirely* - a fleet export read as a
 * profile blanked all thirty. It did not close **the right sort of file, missing most of it**:
 * `{ name, dieFaces }` cleared the two-field bar, and the twenty-eight fields it did not carry were
 * spread over the blank profile and written to the form as zeros. Same loss, narrower door.
 *
 * What is refused is the **loss**, not the incompleteness, and the two are not the same. A profile
 * does not need all thirty fields to be usable - the editor's own hints say to leave the torpedo
 * reach and the salvo size at zero if your table does not use them - so a file that omits those is a
 * good file landing on an empty form and a theft landing on a full one.
 *
 * Nothing below names a field that must be present. The check walks every key the blank profile has,
 * so a field **added** to `RulesProfile` tomorrow is covered the day it is added rather than the day
 * somebody remembers to list it here.
 */
describe('readProfileFile', () => {
  it('loads a file that carries the whole profile', () => {
    // The control. If this ever fails the rest of this block proves nothing, because a checker that
    // refuses everything reports every gap correctly and is still useless.
    const result = readProfileFile(JSON.stringify(complete()), complete());

    expect(result.ok).toBe(true);
    expect(result.ok && result.profile.name).toBe('Invented');
    expect(result.ok && result.profile.beamRangeBandWidth).toBe(10);
  });

  it('loads a partial file onto a form with nothing on it', () => {
    // The second control, and the one that matters most: a table that does not play torpedoes or
    // salvos writes a profile that says nothing about them, and that file has to keep working. This
    // is the shape `scripts/two-player-smoke.py` brings, and requiring all thirty fields broke it.
    const partial = { name: 'Seven Fields', dieFaces: 6, beamDamage: [{ dieFace: 6, screenLevel: 0, damage: 2 }], beamRangeBandWidth: 12, maxScreenLevel: 0, thresholdRows: 'FixedRows', thresholdRowCount: 4 };

    const result = readProfileFile(JSON.stringify(partial), blankRulesProfile);

    expect(result.ok).toBe(true);
    expect(result.ok && result.profile.thresholdRowCount).toBe(4);
    // And it really is playable, which is the point of letting it in.
    expect(result.ok && gapsIn(result.profile)).toEqual([]);
  });

  it('refuses a two-field file rather than blanking the twenty-eight already typed', () => {
    const result = readProfileFile(JSON.stringify({ name: 'Half A Layer', dieFaces: 8 }), complete());

    expect(result.ok).toBe(false);
    // Named, not counted only: the player has to know which of their numbers was at stake.
    expect(result.ok || result.problem).toMatch(/beamRangeBandWidth/);
    expect(result.ok || result.problem).toMatch(/thresholdRowCount/);
    expect(result.ok || result.problem).toMatch(/beamDamage/);
  });

  it('reports any single field a file would blank, whichever field that is', () => {
    // Every key of the profile, filled in on the form and left out of the file. Reached-the-subject:
    // there really are thirty of them, so this loop is not vacuously green.
    const fields = Object.keys(blankRulesProfile);
    expect(fields.length).toBeGreaterThan(25);

    const filled = { ...blankRulesProfile, ...everyFieldFilled() };
    const accepted: string[] = [];
    for (const field of fields) {
      const partial: Record<string, unknown> = { ...filled };
      delete partial[field];
      const result = readProfileFile(JSON.stringify(partial), filled);
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

/**
 * A profile with something in every single field, so that leaving any one of them out of a file is
 * a loss. Built off the blank one rather than written out, so a field added tomorrow is filled here
 * the day it is added - and asserted non-blank below, because a field this missed would make the
 * loop above pass for that field without checking anything.
 */
function everyFieldFilled(): RulesProfile {
  const filled: Record<string, unknown> = { ...blankRulesProfile };
  for (const [field, blank] of Object.entries(blankRulesProfile)) {
    filled[field] = typeof blank === 'number' ? 7
      : typeof blank === 'boolean' ? true
        : typeof blank === 'string' ? (field === 'thresholdRows' ? 'ByShipClass' : 'Invented')
          : [{ dieFace: 7, screenLevel: 0, damage: 1, kills: 1, isGroundedForGame: true, turnsBeforeRelaunch: 2 }];
  }

  return filled as unknown as RulesProfile;
}

describe('the fixture the loop above depends on', () => {
  it('really does differ from blank in every field', () => {
    const filled = everyFieldFilled() as unknown as Record<string, unknown>;
    const same = Object.entries(blankRulesProfile)
      .filter(([field, blank]) => JSON.stringify(filled[field]) === JSON.stringify(blank))
      .map(([field]) => field);

    expect(same).toEqual([]);
  });
});

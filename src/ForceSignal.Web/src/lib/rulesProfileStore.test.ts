// @vitest-environment jsdom
/**
 * What this browser has saved, read back.
 *
 * The import path was taught not to blank the player's numbers; this is the same store read through
 * the other door. `savedProfiles()` went straight to `readProfile`, which coerces - it spreads
 * whatever it is handed over the blank profile - so anything in the store that was not a profile of
 * exactly this version's shape came back as a *profile*, correctly named, with zeros in every field
 * this version did not find. The player picked it off the dropdown and their numbers were gone, with
 * nothing anywhere saying so.
 *
 * Two things are refused here and one is not, and the difference is the whole of it. A stored entry
 * that is not a profile is not offered as one. A stored entry that is a profile but carries field
 * names this version does not know is still offered - it is theirs, and most of it reads - with the
 * part that cannot come back named. **A partial profile is not refused at all**, because a profile
 * does not need all thirty fields to be usable and deciding otherwise is this app deciding which
 * optional rules a table plays.
 */

import { beforeEach, describe, expect, it } from 'vitest';
import { blankRulesProfile, type RulesProfile } from '../types.ts';
import { savedProfiles, saveProfile, deleteProfile } from './rulesProfile.ts';

const STORAGE_KEY = 'forcesignal.rulesProfiles';

/** A profile with something in every field, as a player who filled the form in would have. */
function complete(name: string): RulesProfile {
  return {
    ...blankRulesProfile,
    name,
    dieFaces: 8,
    beamDamage: [{ dieFace: 8, screenLevel: 0, damage: 2 }],
    beamRangeBandWidth: 10,
    maxScreenLevel: 2,
    thresholdRowCount: 3,
    needleSystemKillRoll: 8,
    pointDefenseRange: 5,
    pointDefenseKills: [{ dieFace: 8, kills: 1 }],
    missilesPerSalvo: 4,
    salvoAttackRadius: 5,
  };
}

function write(entries: unknown[]) {
  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(entries));
}

function raw(): unknown[] {
  return JSON.parse(window.localStorage.getItem(STORAGE_KEY) ?? '[]') as unknown[];
}

beforeEach(() => window.localStorage.clear());

describe('reading the store', () => {
  it('does not offer a stored entry that is not a profile as though it were one', () => {
    // A fleet export, saved under this key by a hand slip or an older build that shared it. Every
    // field of it is unknown to `RulesProfile`, so coercing it produces thirty zeros wearing a
    // profile's shape.
    const notAProfile = { formatVersion: 1, side: 'blue', units: [{ name: 'Rifles' }] };
    write([notAProfile, complete('Home Fleet')]);

    // Reached-the-subject: the store really was written and really is readable, so anything missing
    // from the answer below was dropped by the reader rather than never stored.
    expect(raw()).toHaveLength(2);

    const store = savedProfiles();

    expect(store.profiles.map((profile) => profile.name)).toEqual(['Home Fleet']);
    expect(store.problems).toHaveLength(1);
    expect(store.problems[0]).toContain('could not be read');
  });

  it('offers a stored profile whose fields this version does not know, and says what will not come back', () => {
    // The version-skew case, and the reason it is reported rather than refused: this is the
    // player's own profile, saved by an app that called two of its fields something else. Most of
    // it still reads. Refusing it would strand them; blanking those two silently is what used to
    // happen.
    write([{ ...complete('Old Hand'), pointDefenceReach: 5, needleKillsOn: 8 }]);

    const store = savedProfiles();

    // Accepted, not refused - this is the control for the check above.
    expect(store.profiles.map((profile) => profile.name)).toEqual(['Old Hand']);
    expect(store.profiles[0].dieFaces).toBe(8);

    expect(store.problems).toHaveLength(1);
    expect(store.problems[0]).toContain('Old Hand');
    expect(store.problems[0]).toContain('pointDefenceReach');
    expect(store.problems[0]).toContain('needleKillsOn');
  });

  it('says nothing at all about a profile it read whole', () => {
    // The other control. A reporter that complains about everything is no more use than one that
    // complains about nothing, and this is the ordinary case: the store holds what this version
    // wrote, and reading it back is silent.
    write([complete('Kitchen Table')]);

    const store = savedProfiles();

    expect(store.profiles).toHaveLength(1);
    expect(store.problems).toEqual([]);
  });

  it('offers a profile that left out the rules its table does not play', () => {
    // The one that must be accepted, and the mistake that broke CI the last time this ground was
    // walked: incompleteness is not loss. Seven fields is a perfectly good profile - the editor's
    // own hints say to leave the torpedo reach and the salvo size at zero - and a reader that
    // refuses it has decided which optional rules a table has to use.
    const sevenFields = {
      name: 'Seven Fields',
      dieFaces: 6,
      beamDamage: [{ dieFace: 6, screenLevel: 0, damage: 2 }],
      beamRangeBandWidth: 12,
      maxScreenLevel: 0,
      thresholdRows: 'FixedRows',
      thresholdRowCount: 4,
    };
    write([sevenFields]);

    const store = savedProfiles();

    expect(store.profiles.map((profile) => profile.name)).toEqual(['Seven Fields']);
    expect(store.profiles[0].beamRangeBandWidth).toBe(12);
    expect(store.problems).toEqual([]);
  });
});

describe('writing the store', () => {
  it('leaves an entry it could not read exactly as it found it', () => {
    // The trap in the fix. Once the reader stops handing back a coerced version of an entry it
    // cannot read, a writer that rebuilds the store from what the reader returned deletes that
    // entry - so the repair for a silent blanking would have become a silent deletion. The store is
    // rewritten from the raw entries instead.
    const notAProfile = { formatVersion: 1, side: 'blue', units: [{ name: 'Rifles' }] };
    write([notAProfile, complete('Home Fleet')]);

    saveProfile(complete('Second Hand'));

    // Reached-the-subject: the save really did happen.
    expect(savedProfiles().profiles.map((profile) => profile.name)).toEqual(['Home Fleet', 'Second Hand']);

    // And the thing it could not read is still there, byte for byte.
    expect(raw()).toContainEqual(notAProfile);
  });

  it('leaves an entry it could not read alone when forgetting a different one', () => {
    const notAProfile = { formatVersion: 1, side: 'blue', units: [] };
    write([notAProfile, complete('Home Fleet')]);

    deleteProfile('Home Fleet');

    expect(savedProfiles().profiles).toEqual([]);
    expect(raw()).toContainEqual(notAProfile);
  });

  it('still saves, forgets and reads back a profile whole', () => {
    // The control that must be accepted for the writers: the ordinary round trip through this
    // browser's store is what the whole feature is for.
    const profile = complete('Kitchen Table');

    saveProfile(profile);
    expect(savedProfiles().profiles).toHaveLength(1);
    expect(savedProfiles().profiles[0]).toEqual(profile);

    deleteProfile('Kitchen Table');
    expect(savedProfiles().profiles).toEqual([]);
    expect(savedProfiles().problems).toEqual([]);
  });
});

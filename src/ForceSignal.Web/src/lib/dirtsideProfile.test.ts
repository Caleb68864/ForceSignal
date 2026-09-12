import { describe, expect, it } from 'vitest';
import {
  emptyProfileDraft,
  profileSummary,
  signatures,
  toProfileInput,
} from './dirtsideProfile.ts';
import { fireControls, postures, qualityDice } from './groundVocabulary.ts';
import type { DirtsideRulesProfile } from '../types.ts';

/**
 * The die tables, between the form and the wire.
 *
 * The thing under test is not arithmetic, it is restraint: a form nobody has filled in must produce
 * no profile rather than a plausible one, and a form half filled in must send exactly the half that
 * was filled in. Both halves have been got wrong in this project before - once by defaulting a
 * blank field to a number, and once by demanding a full set and refusing a legitimate partial one.
 */
describe('the die table draft', () => {
  it('sends nothing at all when nothing has been entered', () => {
    // The whole point. A client that quietly filled these in would be shipping the very numbers the
    // engine has just stopped shipping, and the server would have no way to tell.
    expect(toProfileInput(emptyProfileDraft())).toBeUndefined();
  });

  it('sends exactly the rows that were entered and no others', () => {
    const draft = emptyProfileDraft();
    draft.fireControl.Basic = 'D6';
    draft.signature['2'] = 'D10';

    const input = toProfileInput(draft);

    expect(input?.fireControl).toEqual([{ key: 'Basic', die: 'D6' }]);
    expect(input?.signature).toEqual([{ key: '2', die: 'D10' }]);
    expect(input?.posture).toEqual([]);
    expect(input?.systemsDownRecoveryDie).toBeUndefined();
  });

  it('treats a blank row as unentered rather than as a die', () => {
    const draft = emptyProfileDraft();
    draft.fireControl.Basic = 'D6';
    draft.fireControl.Enhanced = '';
    draft.posture.HullDown = '   ';

    const input = toProfileInput(draft);

    expect(input?.fireControl.map((row) => row.key)).toEqual(['Basic']);
    expect(input?.posture).toEqual([]);
  });

  it('sends the Leadership Values the table entered, and nothing when they entered none', () => {
    // The entry this module never had. Dirtside's service took whatever number reached a command
    // marker - a probe put 99 and -4 on one - because nobody had ever been asked what the set was.
    const draft = emptyProfileDraft();
    draft.lowestLeadershipValue = '2';
    draft.highestLeadershipValue = '5';

    const input = toProfileInput(draft);

    expect(input?.lowestLeadershipValue).toBe(2);
    expect(input?.highestLeadershipValue).toBe(5);
    expect(toProfileInput(emptyProfileDraft())).toBeUndefined();
  });

  it('sends a Leadership Value bound of zero rather than reading it as unentered', () => {
    // Unlike every recovery roll on this form, where zero is this form's spelling of blank. A table
    // whose command markers run from zero has entered a bound.
    const draft = emptyProfileDraft();
    draft.lowestLeadershipValue = '0';
    draft.highestLeadershipValue = '3';

    expect(toProfileInput(draft)?.lowestLeadershipValue).toBe(0);
  });

  it('passes half a range through rather than dropping the half that was typed', () => {
    const draft = emptyProfileDraft();
    draft.highestLeadershipValue = '5';

    const input = toProfileInput(draft);

    expect(input?.highestLeadershipValue).toBe(5);
    expect(input?.lowestLeadershipValue).toBeUndefined();
  });

  it('drops a die this app does not know the name of rather than passing it on', () => {
    // A hand-edited draft, or a saved one from a future version. The vocabulary is the engine's and
    // the client holds it in one place; anything else is not a die and is not sent as one.
    const draft = emptyProfileDraft();
    draft.fireControl.Basic = 'D20';
    draft.fireControl.Enhanced = 'D8';

    expect(toProfileInput(draft)?.fireControl).toEqual([{ key: 'Enhanced', die: 'D8' }]);
  });

  it('sends a repair roll on its own, because a table may have nothing else to enter yet', () => {
    const draft = emptyProfileDraft();
    draft.systemsDownRecoveryDie = 'D6';
    draft.systemsDownRecoveryRoll = '6';

    const input = toProfileInput(draft);

    expect(input?.systemsDownRecoveryDie).toBe('D6');
    expect(input?.systemsDownRecoveryRoll).toBe(6);
    expect(input?.systemsDownRecoveryRollWithBackup).toBe(0);
  });

  it('reads a half-typed number as nothing rather than as part of one', () => {
    const draft = emptyProfileDraft();
    draft.systemsDownRecoveryRoll = '6x';

    expect(toProfileInput(draft)).toBeUndefined();
  });

  it('sends an area-defence reach on its own, because it is the only number interception has', () => {
    // The one piece of interception this app can hold. It is sent alone for the same reason a repair
    // roll is: a table reads its rulebook in whatever order it likes, and demanding the die tables
    // first would be this app inventing a requirement in place of a number.
    const draft = emptyProfileDraft();
    draft.areaDefenceReach = '9';

    expect(toProfileInput(draft)?.areaDefenceReach).toBe(9);
  });

  it('leaves the reach at nothing when nobody typed one, rather than at a plausible distance', () => {
    const draft = emptyProfileDraft();
    draft.systemsDownRecoveryRoll = '6';

    expect(toProfileInput(draft)?.areaDefenceReach).toBe(0);
  });

  it('offers a row for every word the server accepts, and no word it does not', () => {
    // The control that keeps the form and the vocabulary together: the three tables the form draws
    // are exactly the three lists the API parses against, so a rung added to one cannot leave the
    // screen behind.
    const full = emptyProfileDraft();
    for (const level of fireControls) {
      full.fireControl[level] = qualityDice[0];
    }

    for (const posture of postures) {
      full.posture[posture] = qualityDice[0];
    }

    for (const signature of signatures) {
      full.signature[signature] = qualityDice[0];
    }

    const input = toProfileInput(full);

    expect(input?.fireControl.map((row) => row.key)).toEqual([...fireControls]);
    expect(input?.posture.map((row) => row.key)).toEqual([...postures]);
    expect(input?.signature.map((row) => row.key)).toEqual([...signatures]);
  });
});

describe('what the table is told about its dice', () => {
  const blank: DirtsideRulesProfile = {
    fireControl: [],
    posture: [],
    signature: [],
    systemsDownRecoveryDie: null,
    systemsDownRecoveryRoll: 0,
    systemsDownRecoveryRollWithBackup: 0,
    areaDefenceReach: 0,
  };

  it('says plainly that nothing has been entered, and what will happen', () => {
    const said = profileSummary(blank);

    expect(said).toContain('No die tables');
    expect(said).toContain('refused');
  });

  it('says a partial profile is fine and names what is still open', () => {
    const said = profileSummary({ ...blank, fireControl: [{ key: 'Basic', die: 'D6' }] });

    expect(said).toContain('Partly filled in');
    expect(said).toContain('signature');
  });

  it('says so when every row a game can ask for is there', () => {
    const said = profileSummary({
      fireControl: fireControls.map((key) => ({ key, die: 'D6' })),
      posture: postures.map((key) => ({ key, die: 'D6' })),
      signature: signatures.map((key) => ({ key, die: 'D6' })),
      systemsDownRecoveryDie: 'D6',
      systemsDownRecoveryRoll: 6,
      systemsDownRecoveryRollWithBackup: 3,
      areaDefenceReach: 9,
      lowestLeadershipValue: 2,
      highestLeadershipValue: 5,
    });

    expect(said).toContain('Every row');
    expect(said).not.toContain('Partly filled in');
  });

  it('names the area-defence reach as still open, because an interception is what reads it', () => {
    // No shot ever reads this entry, so a table that skipped it hears nothing until somebody tries
    // to shoot a missile down - which is the worst moment to find out. It is named on the summary
    // with the die rows for the same reason they are.
    const said = profileSummary({
      fireControl: fireControls.map((key) => ({ key, die: 'D6' })),
      posture: postures.map((key) => ({ key, die: 'D6' })),
      signature: signatures.map((key) => ({ key, die: 'D6' })),
      systemsDownRecoveryDie: 'D6',
      systemsDownRecoveryRoll: 6,
      systemsDownRecoveryRollWithBackup: 3,
      areaDefenceReach: 0,
    });

    expect(said).toContain('Partly filled in');
    expect(said).toContain('area-defence reach');
  });

  it('does not pretend to know when the server says nothing', () => {
    expect(profileSummary(null)).toContain('does not report');
  });
});

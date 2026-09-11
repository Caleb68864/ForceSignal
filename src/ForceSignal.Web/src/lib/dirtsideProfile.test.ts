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
    });

    expect(said).toContain('Every row');
    expect(said).not.toContain('Partly filled in');
  });

  it('does not pretend to know when the server says nothing', () => {
    expect(profileSummary(null)).toContain('does not report');
  });
});

import { describe, expect, it } from 'vitest';
import {
  emptyStarGruntProfileDraft,
  starGruntProfileIsEmpty,
  starGruntProfileSummary,
  toStarGruntProfileInput,
  visibleBandRows,
} from './starGruntProfile.ts';
import type { StarGruntRulesProfile } from '../types.ts';

/**
 * The StarGrunt range table, between the form and the wire.
 *
 * As with the Dirtside die tables the thing under test is restraint: a form nobody has filled in
 * produces no profile rather than a plausible one, a form half filled in sends exactly that half, and
 * a zero the user typed is not the same as a field they left blank. Every number below is invented.
 */
describe('the range table draft', () => {
  it('sends nothing at all when nothing has been entered', () => {
    // The whole point. A client that quietly filled these in would be shipping the very page the
    // engine has just stopped shipping - a band the size of the firer's die, a walk up the ladder.
    expect(toStarGruntProfileInput(emptyStarGruntProfileDraft())).toBeUndefined();
  });

  it('sends exactly the entries that were made and no others', () => {
    const draft = emptyStarGruntProfileDraft();
    draft.bandInches['8'] = '7';
    draft.rangeDice['2'] = '4';
    draft.softCoverShift = '2';

    const input = toStarGruntProfileInput(draft);

    expect(input).toEqual({
      bandWidths: [{ qualityDie: 8, inches: 7 }],
      rangeDice: [{ bandsOut: 2, die: 4 }],
      softCoverShift: 2,
    });
    // Left off the body rather than sent as zero, so the server reads them as not entered.
    expect(input).not.toHaveProperty('hardCoverShift');
    expect(input).not.toHaveProperty('effectiveBands');
  });

  it('sends a zero the user typed, because for a cover shift it is an answer', () => {
    // The control on the blanks above: "this cover does nothing in our rules" must reach the server
    // as a zero, not vanish as if it had never been typed.
    const draft = emptyStarGruntProfileDraft();
    draft.hardCoverShift = '0';
    draft.meleeCoverShift = ' 0 ';

    expect(toStarGruntProfileInput(draft)).toEqual({
      bandWidths: [],
      rangeDice: [],
      hardCoverShift: 0,
      meleeCoverShift: 0,
    });
  });

  it('sends the Leadership Values the table entered, and nothing when they entered none', () => {
    const draft = emptyStarGruntProfileDraft();
    draft.lowestLeadershipValue = '2';
    draft.highestLeadershipValue = '5';

    expect(toStarGruntProfileInput(draft)).toEqual({
      bandWidths: [],
      rangeDice: [],
      lowestLeadershipValue: 2,
      highestLeadershipValue: 5,
    });
    expect(toStarGruntProfileInput(emptyStarGruntProfileDraft())).toBeUndefined();
  });

  it('sends a Leadership Value bound at or below zero, because no floor here is this app\'s', () => {
    // Zero is not this form's spelling of blank for these two, unlike everywhere a count is asked
    // for. A table whose record cards run from zero has entered a bound, and dropping it would put
    // this app back in the business of deciding which numbers are Leadership Values.
    const draft = emptyStarGruntProfileDraft();
    draft.lowestLeadershipValue = '0';
    draft.highestLeadershipValue = '2';

    expect(toStarGruntProfileInput(draft)).toMatchObject({ lowestLeadershipValue: 0, highestLeadershipValue: 2 });
  });

  it('passes half a range through rather than dropping the half that was typed', () => {
    // The server refuses it and names the end that is missing, which is a better place for the
    // player to meet it than a field that silently did nothing.
    const draft = emptyStarGruntProfileDraft();
    draft.lowestLeadershipValue = '2';

    const input = toStarGruntProfileInput(draft);

    expect(input).toMatchObject({ lowestLeadershipValue: 2 });
    expect(input).not.toHaveProperty('highestLeadershipValue');
  });

  it('drops what is not a number, a die, or a band rather than passing it on', () => {
    const draft = emptyStarGruntProfileDraft();
    draft.bandInches['8'] = 'seven';
    draft.bandInches['6'] = '0';
    draft.bandInches['4'] = '3';
    draft.rangeDice['1'] = '7';
    draft.rangeDice['0'] = '4';
    draft.rangeDice['3'] = '10';
    draft.softCoverShift = '-1';
    draft.effectiveBands = '0';

    const input = toStarGruntProfileInput(draft);

    expect(input?.bandWidths).toEqual([{ qualityDie: 4, inches: 3 }]);
    expect(input?.rangeDice).toEqual([{ bandsOut: 3, die: 10 }]);
    expect(input).not.toHaveProperty('softCoverShift');
    expect(input).not.toHaveProperty('effectiveBands');
  });

  it('sends the rows in band order whatever order they were typed in', () => {
    const draft = emptyStarGruntProfileDraft();
    draft.rangeDice['3'] = '8';
    draft.rangeDice['1'] = '4';

    expect(toStarGruntProfileInput(draft)?.rangeDice.map((row) => row.bandsOut)).toEqual([1, 3]);
  });
});

describe('the range-die rows on screen', () => {
  it('opens on one row, because how many bands a page has is not this app\'s to say', () => {
    expect(visibleBandRows(emptyStarGruntProfileDraft())).toEqual([1]);
  });

  it('grows one row past the last band filled in', () => {
    const draft = emptyStarGruntProfileDraft();
    draft.rangeDice['1'] = '4';
    draft.rangeDice['3'] = '8';

    expect(visibleBandRows(draft)).toEqual([1, 2, 3, 4]);
  });

  it('does not grow for a row cleared back to not entered', () => {
    const draft = emptyStarGruntProfileDraft();
    draft.rangeDice['1'] = '4';
    draft.rangeDice['2'] = '';

    expect(visibleBandRows(draft)).toEqual([1, 2]);
  });
});

describe('the range table summary', () => {
  const blank: StarGruntRulesProfile = { bandWidths: [], rangeDice: [] };

  it('says plainly that a blank table will refuse the first shot', () => {
    expect(starGruntProfileSummary(blank)).toMatch(/first shot will be refused/);
    expect(starGruntProfileIsEmpty(blank)).toBe(true);
  });

  it('counts what was entered and names what was not', () => {
    const partial: StarGruntRulesProfile = {
      bandWidths: [{ qualityDie: 8, inches: 7 }],
      rangeDice: [{ bandsOut: 2, die: 4 }],
      softCoverShift: 0,
    };

    const summary = starGruntProfileSummary(partial);

    expect(summary).toMatch(/^1 band width\(s\) and 1 range die row\(s\) entered/);
    expect(summary).toMatch(/Not entered: reach, hard cover, dug in, cover in a melee, Leadership Values\./);
    // Zero is entered, so soft cover is not in the list of gaps.
    expect(summary).not.toMatch(/soft cover/);
    expect(starGruntProfileIsEmpty(partial)).toBe(false);
  });

  it('says it cannot tell when the server did not report a table', () => {
    // An older server's snapshot, which has no profile on it at all. Not the same as a blank table,
    // and not warning-styled, because this client does not know that anything is missing.
    expect(starGruntProfileSummary(undefined)).toMatch(/does not report/);
    expect(starGruntProfileIsEmpty(undefined)).toBe(false);
  });
});

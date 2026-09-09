import { describe, expect, it } from 'vitest';
import type { DirtsideChitPot } from '../types.ts';
import { chitPotSummary, emptyChitPotDraft, toChitPotInput } from './chitPot.ts';

describe('toChitPotInput', () => {
  it('sends nothing when nothing has been counted', () => {
    // Not an empty pot: no pot. A client that sent `{numericals: [], specials: []}` would be
    // refused, and one that filled the blanks in would be inventing the numbers the server admits
    // it is guessing at.
    expect(toChitPotInput(emptyChitPotDraft())).toBeUndefined();
  });

  it('sends what the user counted, and only that', () => {
    const draft = emptyChitPotDraft();
    draft.numericals = [
      { colour: 'Red', value: '0', count: '13' },
      { colour: 'Green', value: '2', count: '6' },
    ];
    draft.specials = { Boom: '3', Mobility: '', SystemsDownFirer: '0' };

    expect(toChitPotInput(draft)).toEqual({
      numericals: [
        { colour: 'Red', value: 0, count: 13 },
        { colour: 'Green', value: 2, count: 6 },
      ],
      specials: [{ special: 'Boom', count: 3 }],
    });
  });

  it('leaves out a row that is half typed rather than reading it as a zero', () => {
    const draft = emptyChitPotDraft();
    draft.numericals = [
      { colour: 'Red', value: '', count: '13' },
      { colour: 'Red', value: '1', count: '' },
      { colour: 'Red', value: '2', count: 'lots' },
      { colour: 'Yellow', value: '3', count: '4' },
    ];

    expect(toChitPotInput(draft)).toEqual({
      numericals: [{ colour: 'Yellow', value: 3, count: 4 }],
      specials: [],
    });
  });

  it('sends a pot made only of specials', () => {
    const draft = emptyChitPotDraft();
    draft.specials = { Boom: '2' };

    expect(toChitPotInput(draft)).toEqual({ numericals: [], specials: [{ special: 'Boom', count: 2 }] });
  });
});

describe('chitPotSummary', () => {
  const pot = (over: Partial<DirtsideChitPot>): DirtsideChitPot => ({
    numericals: [{ colour: 'Red', value: 0, count: 10 }],
    specials: [{ special: 'Boom', count: 2 }],
    isBuiltInDefaultGuess: false,
    ...over,
  });

  it('counts the bag the players filled', () => {
    expect(chitPotSummary(pot({}))).toContain('12 chits: 10 numbered, 2 special');
    expect(chitPotSummary(pot({}))).toContain('your own sheet');
  });

  it('says in as many words that the fallback counts are a guess', () => {
    // The whole reason the default survives a release: it is never allowed to pass itself off as
    // somebody's reading of a counter sheet.
    const summary = chitPotSummary(pot({ isBuiltInDefaultGuess: true }));

    expect(summary).toContain('not your counts');
    expect(summary).toContain('a guess');
  });

  it('does not invent a bag when the server reports none', () => {
    expect(chitPotSummary(null)).toContain('does not report');
    expect(chitPotSummary(undefined)).toContain('does not report');
  });
});

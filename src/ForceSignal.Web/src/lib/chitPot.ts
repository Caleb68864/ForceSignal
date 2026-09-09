/**
 * The chit pot, between the form the user types into and the counts the API takes.
 *
 * Nothing here is a number. The colours and the four specials are the engine's vocabulary, imported
 * from the one copy of it; how many of each are in the bag is read off the user's own counter sheet
 * and this module never guesses at it. A draft with nothing counted in it produces no pot at all
 * rather than a plausible one, which is the whole point: the server's fallback composition has
 * special counts that are a documented guess, and a client that quietly filled them in would hide
 * that from the one person entitled to know.
 *
 * Rows are added by hand rather than laid out as a grid of the values a sheet "has", because how far
 * the numbers run is the sheet's business and not this app's.
 */

import { chitSpecials } from './groundVocabulary.ts';
import type { DirtsideChitPotInput } from './dirtsideApi.ts';
import type { DirtsideChitPot } from '../types.ts';

/** One row of the numerical half of the form, held as text so a half-typed number is not a zero. */
export type NumericalChitRow = { colour: string; value: string; count: string };

/** What the create form holds while the user is counting. */
export type ChitPotDraft = {
  numericals: NumericalChitRow[];
  /** Special name to the count typed against it. A name with no entry has not been counted. */
  specials: Record<string, string>;
};

/** A form nobody has typed into. */
export function emptyChitPotDraft(): ChitPotDraft {
  return { numericals: [], specials: {} };
}

/** Whether a piece of the form holds a whole number of chits. */
function countOf(text: string): number | null {
  const trimmed = text.trim();
  if (trimmed === '' || !/^\d+$/.test(trimmed)) {
    return null;
  }

  return Number(trimmed);
}

/**
 * The counts to send, or undefined when the user has counted nothing.
 *
 * Rows that are blank or not whole numbers are left out rather than guessed at; a row counted as
 * zero is also left out, because "none of these" and "some of these, zero of them" are the same bag.
 */
export function toChitPotInput(draft: ChitPotDraft): DirtsideChitPotInput | undefined {
  const numericals = draft.numericals
    .map((row) => ({ colour: row.colour, value: countOf(row.value), count: countOf(row.count) }))
    .filter((row) => row.value !== null && row.count !== null && row.count > 0)
    .map((row) => ({ colour: row.colour, value: row.value as number, count: row.count as number }));

  const specials = chitSpecials
    .map((special) => ({ special, count: countOf(draft.specials[special] ?? '') }))
    .filter((row) => row.count !== null && row.count > 0)
    .map((row) => ({ special: row.special, count: row.count as number }));

  return numericals.length + specials.length === 0 ? undefined : { numericals, specials };
}

/**
 * What the table should be told about the bag it is drawing from.
 *
 * The honesty is the point of the sentence. A pot the players counted is reported as theirs; the
 * fallback is reported as ours and as a guess, in those words, because every damage probability in
 * such a game rests on numbers nobody counted.
 */
export function chitPotSummary(pot: DirtsideChitPot | null | undefined): string {
  if (!pot) {
    return 'This server does not report what is in the chit pot.';
  }

  const numbered = pot.numericals.reduce((total, row) => total + row.count, 0);
  const special = pot.specials.reduce((total, row) => total + row.count, 0);
  const counted = `${numbered + special} chits: ${numbered} numbered, ${special} special`;

  return pot.isBuiltInDefaultGuess
    ? `${counted}. These are not your counts: no pot was entered, so the server fell back to a built-in `
      + 'composition whose special-chit counts are a guess. Start a new game with your own counts to '
      + 'play on the sheet in front of you.'
    : `${counted}, as counted off your own sheet.`;
}

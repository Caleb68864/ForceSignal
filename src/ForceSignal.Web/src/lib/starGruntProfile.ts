/**
 * The StarGrunt range table, between the form the user types into and the entries the API takes.
 *
 * Nothing here is a number. The rungs of the quality ladder are the engine's vocabulary, imported
 * from the one copy of it; how wide a band is, which die a target that many bands out rolls, how far
 * small arms reach and what cover is worth are all read off the user's own rulebook, and this module
 * never guesses at any of them. The engine used to: a band was the firer's own die in inches, the
 * range die walked up the ladder a rung a band, and cover was one or two rungs. That was somebody's
 * page, and it is not here.
 *
 * A draft with nothing entered produces no profile at all rather than a plausible one, and a draft
 * half entered sends exactly the half that was entered. There is no fallback on the server to land
 * on: a game with no table plays until somebody fires, and is then told which entry it wants.
 */

import { qualityLadder } from './groundVocabulary.ts';
import type { StarGruntRulesProfileInput } from './starGruntApi.ts';
import type { StarGruntRulesProfile } from '../types.ts';

/**
 * What the create form holds while the user is reading their rulebook.
 *
 * Text rather than numbers, and `''` for anything not entered. Zero is a real answer for a cover
 * shift - "this cover does nothing in our rules" - so it cannot also be this form's spelling of
 * blank, which is the one place this screen differs from the add-a-squad panel's zero.
 */
export type StarGruntProfileDraft = {
  /** Inches per band, keyed by the firing troops' quality die as a face count. */
  bandInches: Record<string, string>;
  /** The die a target rolls, as a face count, keyed by how many bands out it is. */
  rangeDice: Record<string, string>;
  effectiveBands: string;
  softCoverShift: string;
  hardCoverShift: string;
  inPositionShift: string;
  meleeCoverShift: string;
};

/** A form nobody has typed into. */
export function emptyStarGruntProfileDraft(): StarGruntProfileDraft {
  return {
    bandInches: {},
    rangeDice: {},
    effectiveBands: '',
    softCoverShift: '',
    hardCoverShift: '',
    inPositionShift: '',
    meleeCoverShift: '',
  };
}

/** A whole number the user typed, or null when the field holds anything else - blank included. */
function wholeNumber(text: string | undefined): number | null {
  const trimmed = (text ?? '').trim();
  return /^\d+$/.test(trimmed) ? Number(trimmed) : null;
}

/** Whether a piece of the form holds a die this app knows the face count of. */
function dieOf(text: string | undefined): number | null {
  const faces = wholeNumber(text);
  return faces !== null && (qualityLadder as readonly number[]).includes(faces) ? faces : null;
}

/**
 * Which range-die rows the form shows: every band the user has filled in, and one more.
 *
 * Grows as it is filled rather than opening on some number of rows, because how many bands a page
 * has is itself one of the numbers this app does not ship.
 */
export function visibleBandRows(draft: StarGruntProfileDraft): number[] {
  const filled = Object.entries(draft.rangeDice)
    .filter(([, die]) => dieOf(die) !== null)
    .map(([band]) => wholeNumber(band) ?? 0);
  const last = Math.max(0, ...filled);
  return Array.from({ length: last + 1 }, (_, index) => index + 1);
}

/**
 * The profile to send, or undefined when the user has entered nothing at all.
 *
 * Partial is normal and is sent as-is. A table fighting one kind of squad across open ground needs
 * one band width and the rows it shoots at, and demanding the rest before the game could start would
 * be this app inventing a requirement in place of a number - the over-strictness that once refused a
 * legitimate seven-field Full Thrust profile.
 */
export function toStarGruntProfileInput(draft: StarGruntProfileDraft): StarGruntRulesProfileInput | undefined {
  const bandWidths: { qualityDie: number; inches: number }[] = [];
  for (const die of qualityLadder) {
    const inches = wholeNumber(draft.bandInches[String(die)]);
    if (inches !== null && inches > 0) {
      bandWidths.push({ qualityDie: die, inches });
    }
  }

  const rangeDice = Object.entries(draft.rangeDice)
    .map(([band, die]) => ({ bandsOut: wholeNumber(band), die: dieOf(die) }))
    .filter((row): row is { bandsOut: number; die: number } =>
      row.bandsOut !== null && row.bandsOut > 0 && row.die !== null)
    .sort((left, right) => left.bandsOut - right.bandsOut);

  const reach = wholeNumber(draft.effectiveBands);
  const shifts = {
    softCoverShift: wholeNumber(draft.softCoverShift),
    hardCoverShift: wholeNumber(draft.hardCoverShift),
    inPositionShift: wholeNumber(draft.inPositionShift),
    meleeCoverShift: wholeNumber(draft.meleeCoverShift),
  };

  const anyShift = Object.values(shifts).some((value) => value !== null);
  if (bandWidths.length === 0 && rangeDice.length === 0 && !(reach !== null && reach > 0) && !anyShift) {
    return undefined;
  }

  // An unentered number is left off the body rather than sent as zero, so the server reads it as
  // not entered. A zero the user typed is sent, because for a shift it means something.
  const entered = Object.fromEntries(
    Object.entries(shifts).filter(([, value]) => value !== null),
  ) as Partial<Record<keyof typeof shifts, number>>;

  return {
    bandWidths,
    rangeDice,
    ...(reach !== null && reach > 0 ? { effectiveBands: reach } : {}),
    ...entered,
  };
}

/**
 * What the table should be told about the range table it is playing on.
 *
 * Plain about the gap rather than reassuring about it. A table whose range table is empty meets the
 * refusal mid-firefight with a model in hand, so the sentence that saves them that is the one on the
 * screen before the first turn.
 */
export function starGruntProfileSummary(profile: StarGruntRulesProfile | null | undefined): string {
  if (!profile) {
    return 'This server does not report which range table this game is played on.';
  }

  const widths = profile.bandWidths.length;
  const rows = profile.rangeDice.length;
  const unentered = [
    profile.effectiveBands == null ? 'reach' : null,
    profile.softCoverShift == null ? 'soft cover' : null,
    profile.hardCoverShift == null ? 'hard cover' : null,
    profile.inPositionShift == null ? 'dug in' : null,
    profile.meleeCoverShift == null ? 'cover in a melee' : null,
  ].filter((part): part is string => part !== null);

  if (widths + rows === 0 && unentered.length === 5) {
    return 'No range table has been entered for this game. This app ships none of its own, so the '
      + 'first shot will be refused and will say which entry it needs. Start a new game with the '
      + 'numbers from your own rulebook.';
  }

  const counted = `${widths} band width(s) and ${rows} range die row(s) entered, from your own rulebook`;
  return unentered.length === 0
    ? `${counted}.`
    : `${counted}. Not entered: ${unentered.join(', ')}. That is fine until something reads one, `
      + 'which is refused rather than guessed at.';
}

/** Whether a profile carries nothing a shot could be settled with, for the warning styling. */
export function starGruntProfileIsEmpty(profile: StarGruntRulesProfile | null | undefined): boolean {
  return profile != null && profile.bandWidths.length === 0 && profile.rangeDice.length === 0;
}

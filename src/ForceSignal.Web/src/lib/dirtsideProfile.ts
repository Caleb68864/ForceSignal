/**
 * The Dirtside die tables, between the form the user types into and the rows the API takes.
 *
 * Nothing here is a number. The gunnery levels, the postures and the rungs of the quality ladder are
 * the engine's vocabulary, imported from the one copy of it; which die each of them rolls is read
 * off the user's own rulebook and this module never guesses at it. A draft with nothing chosen
 * produces no profile at all rather than a plausible one - and unlike the chit pot beside it there
 * is no server-side fallback to land on, because these tables were never anything a player could
 * have thought was theirs. A game with no rows plays until somebody fires, and is then told which
 * row it wants.
 *
 * The asymmetry is deliberate and is the whole design: the pot's fallback is a guess being wound
 * down over one release, so hiding it would be dishonest; the profile has no guess to hide.
 */

import { fireControls, postures, qualityDice } from './groundVocabulary.ts';
import type { DirtsideRulesProfileInput } from './dirtsideApi.ts';
import type { DirtsideRulesProfile } from '../types.ts';

/** The signatures a record card can carry. Which numbers exist, not what any of them rolls. */
export const signatures = ['1', '2', '3', '4', '5'] as const;

/**
 * What the create form holds while the user is reading their rulebook.
 *
 * A key with no entry, or an entry of `''`, has not been filled in. That is a different thing from a
 * die, and the difference is the only reason the server can name what it is missing.
 */
export type DirtsideProfileDraft = {
  fireControl: Record<string, string>;
  posture: Record<string, string>;
  signature: Record<string, string>;
  systemsDownRecoveryDie: string;
  systemsDownRecoveryRoll: string;
  systemsDownRecoveryRollWithBackup: string;
  areaDefenceReach: string;
};

/** A form nobody has typed into. */
export function emptyProfileDraft(): DirtsideProfileDraft {
  return {
    fireControl: {},
    posture: {},
    signature: {},
    systemsDownRecoveryDie: '',
    systemsDownRecoveryRoll: '',
    systemsDownRecoveryRollWithBackup: '',
    areaDefenceReach: '',
  };
}

/** Whether a piece of the form holds a die this app knows the name of. */
function dieOf(text: string | undefined): string | null {
  const trimmed = (text ?? '').trim();
  return (qualityDice as readonly string[]).includes(trimmed) ? trimmed : null;
}

/** Whether a piece of the form holds a whole number above zero. */
function rollOf(text: string): number {
  const trimmed = text.trim();
  return /^\d+$/.test(trimmed) ? Number(trimmed) : 0;
}

/** The rows of one table, leaving out every key the user has not answered. */
function rowsOf(keys: readonly string[], draft: Record<string, string>) {
  return keys
    .map((key) => ({ key, die: dieOf(draft[key]) }))
    .filter((row) => row.die !== null)
    .map((row) => ({ key: row.key, die: row.die as string }));
}

/**
 * The profile to send, or undefined when the user has entered nothing at all.
 *
 * Partial is normal and is sent as-is. A table whose vehicles are all one gunnery grade never has to
 * enter the other two rows, and demanding a full set before the game could start would be this app
 * inventing a requirement in place of a number - the same over-strictness that once refused a
 * legitimate seven-field Full Thrust profile.
 */
export function toProfileInput(draft: DirtsideProfileDraft): DirtsideRulesProfileInput | undefined {
  const fireControl = rowsOf(fireControls, draft.fireControl);
  const posture = rowsOf(postures, draft.posture);
  const signature = rowsOf(signatures, draft.signature);
  const recoveryDie = dieOf(draft.systemsDownRecoveryDie);
  const recoveryRoll = rollOf(draft.systemsDownRecoveryRoll);
  const recoveryWithBackup = rollOf(draft.systemsDownRecoveryRollWithBackup);
  const areaDefenceReach = rollOf(draft.areaDefenceReach);

  const entered =
    fireControl.length + posture.length + signature.length
    + (recoveryDie ? 1 : 0) + recoveryRoll + recoveryWithBackup + areaDefenceReach;
  if (entered === 0) {
    return undefined;
  }

  return {
    fireControl,
    posture,
    signature,
    ...(recoveryDie ? { systemsDownRecoveryDie: recoveryDie } : {}),
    systemsDownRecoveryRoll: recoveryRoll,
    systemsDownRecoveryRollWithBackup: recoveryWithBackup,
    areaDefenceReach,
  };
}

/**
 * What the table should be told about the dice it is playing on.
 *
 * Plain about the gap rather than reassuring about it. A table whose profile is empty will meet the
 * refusal at the worst possible moment - mid-firefight, with a model in hand - so the sentence that
 * saves them that is the one on the screen before the first turn.
 */
export function profileSummary(profile: DirtsideRulesProfile | null | undefined): string {
  if (!profile) {
    return 'This server does not report which dice this game is settled with.';
  }

  const rows =
    profile.fireControl.length + profile.posture.length + profile.signature.length;
  const repair = profile.systemsDownRecoveryDie ? 1 : 0;
  if (rows + repair === 0) {
    return 'No die tables have been entered for this game. This app ships none of its own, so the '
      + 'first shot will be refused and will say which row it needs. Start a new game with the '
      + 'dice from your own rulebook.';
  }

  const missing = [
    profile.fireControl.length < fireControls.length ? 'gunnery' : null,
    profile.signature.length < signatures.length ? 'signature' : null,
    profile.posture.length < postures.length ? 'posture' : null,
    repair === 0 ? 'systems-down repair' : null,
    // Not a die, and listed here anyway: it is a profile entry a shot never reads but an
    // interception does, so a table that never enters one meets the refusal at the moment somebody
    // tries to shoot a missile down rather than at the moment they fire.
    profile.areaDefenceReach > 0 ? null : 'area-defence reach',
  ].filter((part): part is string => part !== null);

  const counted = `${rows} die table row(s) entered, from your own rulebook`;
  return missing.length === 0
    ? `${counted}. Every row this game can ask for is there.`
    : `${counted}. Partly filled in: ${missing.join(', ')}. That is fine until a shot reads a row `
      + 'nobody entered, which is refused rather than guessed at.';
}

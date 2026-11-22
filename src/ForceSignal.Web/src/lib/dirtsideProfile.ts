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
import { leadershipValuesOf } from './leadershipValues.ts';
import type { DirtsideRulesProfileInput } from './dirtsideApi.ts';
import type { DirtsideFirerWalk, DirtsideRulesProfile } from '../types.ts';

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
  /** How many rungs an Under Fire marker costs a fire-effectiveness check. Blank is not entered; 0 is an answer. */
  rungsLostUnderFire: string;
  /** The smallest number a command marker in this game may carry as a Leadership Value. */
  lowestLeadershipValue: string;
  /** The largest. Both ends or neither - the server refuses half a range where it is entered. */
  highestLeadershipValue: string;
  /** Firer walk: rungs up for a shot at close range. Blank is not entered; 0 is an answer. */
  firerWalkUpAtClose: string;
  /** Rungs down for a shot at long range. */
  firerWalkDownAtLong: string;
  /** Rungs down for a firer that moved, or will move, over half. */
  firerWalkDownMovingOverHalf: string;
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
    rungsLostUnderFire: '',
    lowestLeadershipValue: '',
    highestLeadershipValue: '',
    firerWalkUpAtClose: '',
    firerWalkDownAtLong: '',
    firerWalkDownMovingOverHalf: '',
  };
}

/** Whether a piece of the form holds a die this app knows the name of. */
function dieOf(text: string | undefined): string | null {
  const trimmed = (text ?? '').trim();
  return (qualityDice as readonly string[]).includes(trimmed) ? trimmed : null;
}

/**
 * A whole number the user typed, or null when the field holds anything else - blank included.
 *
 * Separate from {@link rollOf} because zero is a real answer here: a table whose record cards run
 * from 0 has entered a bound, and a nought that read back as 'not entered' would put this app back
 * in the business of deciding which numbers are Leadership Values.
 */
function enteredNumber(text: string): number | null {
  const trimmed = text.trim();
  return /^-?\d+$/.test(trimmed) ? Number(trimmed) : null;
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

  // Sent as a pair or not at all, and a half-filled pair is passed through rather than quietly
  // dropped: the server refuses it where it was entered, which is where the blank field still is.
  const lowest = enteredNumber(draft.lowestLeadershipValue);
  const highest = enteredNumber(draft.highestLeadershipValue);
  const rungsLostUnderFire = enteredNumber(draft.rungsLostUnderFire);
  const firerWalk = firerWalkOf(draft);

  const entered =
    fireControl.length + posture.length + signature.length
    + (recoveryDie ? 1 : 0) + recoveryRoll + recoveryWithBackup + areaDefenceReach
    + (lowest === null ? 0 : 1) + (highest === null ? 0 : 1)
    + (rungsLostUnderFire === null ? 0 : 1)
    + (firerWalk ? 1 : 0);
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
    ...(lowest === null ? {} : { lowestLeadershipValue: lowest }),
    ...(highest === null ? {} : { highestLeadershipValue: highest }),
    ...(rungsLostUnderFire === null ? {} : { rungsLostUnderFire }),
    ...(firerWalk ? { firerWalk } : {}),
  };
}

/**
 * The firer walk the table entered: all three rungs, or nothing.
 *
 * Unlike the Leadership Value pair, half a walk cannot be passed through for the server to refuse.
 * The wire record takes three numbers and a member left out binds to zero there, which is an answer
 * nobody gave - so a part-filled walk is refused here, naming the box still blank. A negative rung is
 * sent as typed and refused by the server in its own words.
 */
function firerWalkOf(draft: DirtsideProfileDraft): DirtsideFirerWalk | undefined {
  const rungs = [
    { value: enteredNumber(draft.firerWalkUpAtClose), name: 'rungs up at close range' },
    { value: enteredNumber(draft.firerWalkDownAtLong), name: 'rungs down at long range' },
    { value: enteredNumber(draft.firerWalkDownMovingOverHalf), name: 'rungs down for moving over half' },
  ];
  const blank = rungs.filter((rung) => rung.value === null);
  if (blank.length === rungs.length) {
    return undefined;
  }
  if (blank.length > 0) {
    throw new Error(
      `The firer walk is part entered: ${blank.map((rung) => rung.name).join(', ')} still blank. `
      + 'Enter all three rungs, or none to play the built-in walk.',
    );
  }
  const [up, down, hurried] = rungs.map((rung) => rung.value as number);
  return { rungsUpAtClose: up, rungsDownAtLong: down, rungsDownMovingOverHalf: hurried };
}

/** Whether any of the firer walk's three boxes holds anything. Half a walk is refused elsewhere. */
export function firerWalkTouched(draft: DirtsideProfileDraft): boolean {
  return [draft.firerWalkUpAtClose, draft.firerWalkDownAtLong, draft.firerWalkDownMovingOverHalf]
    .some((text) => text.trim() !== '');
}

/** What a table starting a game with no firer walk is asked. */
export const builtInWalkQuestion =
  'No firer walk was entered, so this game will play the built-in firer walk: one rung up at close, '
  + 'one down at long, one down for moving over half. That is this app\'s walk, not your rulebook\'s, '
  + 'and it cannot be changed once the game starts. Start anyway?';

/**
 * What the table should be told about the firer walk it is playing.
 *
 * The one entry the server still fills in by itself when nobody entered it, so it is reported the
 * way the fallback chit pot is: as the app's, not the table's. The built-in rungs are named because
 * a table checking a roll needs them, and they are exactly what the server plays.
 */
export function firerWalkSummary(profile: DirtsideRulesProfile | null | undefined): string {
  if (!profile) {
    return 'This server does not report which firer walk this game plays.';
  }

  const walk = profile.firerWalk;
  if (!walk) {
    return 'playing the built-in firer walk - one rung up at close, one down at long, one down for '
      + 'moving over half. That is this app\'s walk, not your rulebook\'s: start a new game with your '
      + 'own rungs to play yours.';
  }

  return `${walk.rungsUpAtClose} up at close, ${walk.rungsDownAtLong} down at long, `
    + `${walk.rungsDownMovingOverHalf} down for moving over half, off your own rulebook.`;
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
    // Not a die either, and the one whose absence stops a platoon going on the table with a number
    // on its command marker rather than stopping a shot.
    leadershipValuesOf(profile).length === 0 ? 'Leadership Values' : null,
  ].filter((part): part is string => part !== null);

  const counted = `${rows} die table row(s) entered, from your own rulebook`;
  return missing.length === 0
    ? `${counted}. Every row this game can ask for is there.`
    : `${counted}. Partly filled in: ${missing.join(', ')}. That is fine until a shot reads a row `
      + 'nobody entered, which is refused rather than guessed at.';
}

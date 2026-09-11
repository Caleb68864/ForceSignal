/**
 * The words the ground-combat API accepts, in one place.
 *
 * These are not the client's choices. Every one is a copy of a `DirtsideWire` or `StarGruntWire`
 * field in `ForceSignal.Contracts`, which is in turn held against the engine enum it stands for by
 * a test in `ForceSignal.Application.Tests`. `scripts/check-ground-vocabulary.py` compares this file
 * to those, so the chain runs from the engine enums to the dropdowns without a link anyone has to
 * remember.
 *
 * They used to be typed out beside the dropdowns that render them, which meant a range band existed
 * in three spellings with none of them authoritative: the API would accept a value no screen
 * offered, and a screen could offer one the API refused, and neither would say anything until a
 * player picked it mid-game.
 *
 * Nothing here is a rules number. A band is a name for a distance the player measures with a tape,
 * and the die list is which dice exist, not which one anything uses.
 */

/** Range bands a shot can be measured at, ordered as a record card reads them. */
export const bands = ['Close', 'Medium', 'Long'] as const;

/** Gunnery levels an element can have. */
export const fireControls = ['Basic', 'Enhanced', 'Superior'] as const;

/** Dice a Dirtside platoon's command marker can name. */
export const qualityDice = ['D4', 'D6', 'D8', 'D10', 'D12'] as const;

/** Where a close assault can stand, as the snapshot reports it. */
export const assaultStages = [
  'AwaitingDefender',
  'AwaitingRound',
  'AwaitingAftermath',
  'AwaitingFollowThrough',
] as const;

/** The same ladder StarGrunt sends as face counts rather than names. */
export const qualityLadder = [4, 6, 8, 10, 12] as const;

/** Colours a numerical damage chit can be printed in. */
export const chitColours = ['Red', 'Yellow', 'Green'] as const;

/**
 * The non-numerical chits, by the name each one goes by. How many of each are in the bag is the
 * player's to count; which ones exist is the engine's.
 */
export const chitSpecials = ['Mobility', 'SystemsDownTarget', 'SystemsDownFirer', 'Boom'] as const;

/**
 * How a validity row says the numbers on the chits read.
 *
 * Lived in `DirtsideAssaultPanel.tsx` beside its own dropdown, which is the state this module was
 * written to end. The server parses a row against `ChitValueScale` and throws by name on anything
 * else, so a word added to the enum and not to the screen would have been accepted by the API and
 * offered nowhere - and the refusal could not name what it would have taken, which is what the band
 * refusal two methods below it does do.
 */
export const valueScales = ['Doubled', 'FaceValue', 'Halved'] as const;

/**
 * The colour sets a validity row may name.
 *
 * Not `chitColours` above, which is what a chit is printed in - this is what a weapon may count, and
 * it carries `All` as well. The engine models it as a flags enum, so pairs and the empty set exist
 * there and are offered by nothing here; that gap is recorded and is a UI decision rather than a
 * spelling one.
 */
export const chitColourSets = ['All', 'Red', 'Yellow', 'Green'] as const;

/**
 * Postures a Dirtside die table has a row for.
 *
 * Not every member of the engine's `DefensivePosture`: `None` is a target doing nothing about being
 * shot at, which throws no second die, so there is no row for it on anybody's card and the server
 * refuses one by name. Which postures exist is the engine's; what die each is worth is the
 * player's, and this list carries none of that.
 */
export const postures = ['SoftCover', 'Evading', 'HullDown', 'TurretDown'] as const;

export type Band = (typeof bands)[number];
export type FireControl = (typeof fireControls)[number];
export type QualityDieName = (typeof qualityDice)[number];
export type AssaultStage = (typeof assaultStages)[number];
export type ChitColourName = (typeof chitColours)[number];
export type ChitSpecialName = (typeof chitSpecials)[number];
export type ValueScaleName = (typeof valueScales)[number];
export type ChitColourSetName = (typeof chitColourSets)[number];
export type PostureName = (typeof postures)[number];

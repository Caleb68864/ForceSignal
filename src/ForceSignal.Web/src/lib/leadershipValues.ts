/**
 * Which numbers count as Leadership Values at a table, read off the game's own rules profile.
 *
 * One module for both ground games, mirroring the one `LeadershipRange` the server reads. The two
 * screens used to disagree about this exactly as the two engines did: StarGrunt's add-a-squad panel
 * offered 1, 2 and 3 with "(best)" beside the first, and a tooltip that said so in words; Dirtside's
 * took any number from 0 to 9. Both were this app putting a bound on the screen that came off
 * somebody's page rather than out of the user's entry.
 *
 * Nothing here invents a value. A game whose profile has not been told what its Leadership Values
 * are gets an empty list, and the panel that reads it offers none and says why.
 */

import type { DirtsideRulesProfile, StarGruntRulesProfile } from '../types.ts';

/**
 * How many options one of these controls will render.
 *
 * A cap on a dropdown, not on a rule: it is here so a mistyped profile cannot build a select with a
 * million children and take the tab down with it. No table's record cards carry a hundred distinct
 * Leadership Values, and the server - which is what actually decides - has no such cap.
 */
const maxLeadershipOptions = 100;

/** A profile of either game, as far as this module reads one. */
type WithLeadershipValues = Pick<
  StarGruntRulesProfile & DirtsideRulesProfile,
  'lowestLeadershipValue' | 'highestLeadershipValue'
>;

/**
 * The Leadership Values this game plays with, lowest first, or an empty list when nobody has said.
 *
 * Both ends or neither. One bound on its own is not a set of values, and a screen that offered the
 * numbers from a lone entry would be inventing the other end - which is the whole habit this replaces.
 */
export function leadershipValuesOf(profile: WithLeadershipValues | null | undefined): number[] {
  const lowest = profile?.lowestLeadershipValue;
  const highest = profile?.highestLeadershipValue;
  if (lowest == null || highest == null || highest < lowest) {
    return [];
  }

  const span = Math.min(highest - lowest + 1, maxLeadershipOptions);
  return Array.from({ length: span }, (_, index) => lowest + index);
}

/**
 * What a screen should say about the Leadership Values this game uses.
 *
 * The entered case reads the players' own two numbers back at them, which is the opposite of the
 * sentence this replaces: that one recited a bound this app was never entitled to know.
 */
export function leadershipValuesSummary(profile: WithLeadershipValues | null | undefined): string {
  const values = leadershipValuesOf(profile);
  return values.length === 0
    ? 'Nobody has entered which Leadership Values this game uses, so no record card can carry one. '
      + 'This app ships none of its own; start a new game with the lowest and the highest from your '
      + 'own rulebook.'
    : `Leadership Values ${values[0]} to ${values[values.length - 1]}, as entered for this game.`;
}

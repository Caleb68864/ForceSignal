import { blankRulesProfile, type RulesProfile } from '../types.ts';

const STORAGE_KEY = 'forcesignal.rulesProfiles';

/**
 * What is still missing before a profile can be played against.
 *
 * The server checks the same things and is the authority - this is here so the form can say what is
 * wrong before a round trip, not so the client can decide. If the two ever disagree, the server wins
 * and the request comes back refused.
 */
export function gapsIn(profile: RulesProfile): string[] {
  const gaps: string[] = [];
  if (!profile.name.trim()) {
    gaps.push('The profile needs a name, so you can tell it from the next one.');
  }
  if (profile.dieFaces < 2) {
    gaps.push('Say how many faces the die has.');
  }
  if (profile.beamDamage.length === 0) {
    gaps.push('Fill in what a beam die scores at each face and screen level.');
  }
  if (profile.beamRangeBandWidth <= 0) {
    gaps.push("Say how wide a beam's range band is.");
  }
  if (profile.thresholdRowCount <= 1) {
    gaps.push("Say how many rows a hull's damage track is drawn in.");
  }
  if (profile.thresholdRows === 'ByShipClass' && (profile.escortRowCount <= 0 || profile.cruiserRowCount <= 0)) {
    gaps.push('Sizing the track by class needs a row count for an escort and for a cruiser.');
  }
  if (profile.maxPartiesPerJob > 0 && (profile.repairRollWithOneParty <= 0 || profile.repairBestRoll <= 0)) {
    gaps.push('Damage control needs the roll one party makes and the best it can get to.');
  }
  if (profile.pointDefenseRange > 0 && profile.pointDefenseKills.length === 0) {
    gaps.push('Point defence has a range but nothing saying what its dice shoot down.');
  }
  if (profile.missilesPerSalvo > 0 && profile.salvoAttackRadius <= 0) {
    gaps.push('A salvo needs to know how close its target must be to the point of aim.');
  }
  if (profile.enhancedNeedleBeams && profile.needleHullDamageRoll <= 0) {
    gaps.push('An enhanced needle needs the roll it draws blood on.');
  }
  if (profile.carrierTurnaroundRoll && profile.turnaround.length === 0) {
    gaps.push('Turnaround is rolled for but nothing says what the faces mean.');
  }
  // A row for a face the die cannot show never matches, so the die scores nothing and the table
  // just looks unlucky. Same check as the server, said while the numbers are still on screen.
  if (profile.dieFaces >= 2) {
    const offTheDie = (entry: { dieFace: number }) => entry.dieFace < 1 || entry.dieFace > profile.dieFaces;
    if (profile.beamDamage.some(offTheDie)) {
      gaps.push(`A beam damage entry names a face the ${profile.dieFaces}-sided die does not have.`);
    }
    if (profile.pointDefenseKills.some(offTheDie)) {
      gaps.push(`A point defence entry names a face the ${profile.dieFaces}-sided die does not have.`);
    }
    if (profile.turnaround.some(offTheDie)) {
      gaps.push(`A turnaround entry names a face the ${profile.dieFaces}-sided die does not have.`);
    }
  }
  return gaps;
}

/** True when a profile has enough in it to play a match against. */
export const isPlayable = (profile: RulesProfile) => gapsIn(profile).length === 0;

/**
 * Whether two profiles hold the same numbers.
 *
 * By content, not by identity, and that is the whole point of it: a profile arrives freshly parsed
 * inside every snapshot, so two objects that are not the same object are still almost always the
 * same numbers. Anything watching for the table's profile to change has to be able to tell those
 * two cases apart, or it fires on every ship anyone moves.
 *
 * Keys are sorted before comparing so that a profile built by the form and one parsed off the wire
 * compare equal despite having been assembled in different orders.
 */
export function sameProfile(left: RulesProfile, right: RulesProfile): boolean {
  return stableJson(left) === stableJson(right);
}

function stableJson(value: unknown): string {
  return JSON.stringify(value, (_key, item: unknown) => {
    if (item === null || typeof item !== 'object' || Array.isArray(item)) {
      return item;
    }
    const sorted: Record<string, unknown> = {};
    for (const key of Object.keys(item as Record<string, unknown>).sort()) {
      sorted[key] = (item as Record<string, unknown>)[key];
    }
    return sorted;
  });
}

/** Profiles this browser has saved, so a set of numbers is entered once and reused. */
export function savedProfiles(): RulesProfile[] {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    const parsed: unknown = raw ? JSON.parse(raw) : [];
    return Array.isArray(parsed) ? parsed.map(readProfile) : [];
  } catch {
    // A corrupt or unreadable store is not worth an error message: it just means no saved profiles.
    return [];
  }
}

/** Saves a profile under its own name, replacing any earlier one by that name. */
export function saveProfile(profile: RulesProfile): RulesProfile[] {
  const kept = savedProfiles().filter((saved) => saved.name !== profile.name);
  const next = [...kept, profile].sort((left, right) => left.name.localeCompare(right.name));
  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
  return next;
}

/** Forgets a saved profile by name. */
export function deleteProfile(name: string): RulesProfile[] {
  const next = savedProfiles().filter((saved) => saved.name !== name);
  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
  return next;
}

/**
 * Reads a profile out of parsed JSON, filling anything absent from the blank profile.
 *
 * Unknown fields are dropped and missing ones default to blank rather than to a number of this
 * app's choosing, so a half-written file comes back as a half-filled form the player can finish
 * instead of a profile that quietly plays wrong.
 */
export function readProfile(value: unknown): RulesProfile {
  const source = (value ?? {}) as Partial<RulesProfile>;
  return {
    ...blankRulesProfile,
    ...source,
    name: typeof source.name === 'string' ? source.name : '',
    beamDamage: Array.isArray(source.beamDamage) ? source.beamDamage : [],
    turnaround: Array.isArray(source.turnaround) ? source.turnaround : [],
    pointDefenseKills: Array.isArray(source.pointDefenseKills) ? source.pointDefenseKills : [],
    thresholdRows: source.thresholdRows === 'ByShipClass' ? 'ByShipClass' : 'FixedRows',
  };
}

/**
 * Whether a parsed file is a rules profile at all, rather than some other JSON.
 *
 * `readProfile` coerces - it has to, because a profile written by an older version is still a
 * profile - and a coercer asked to read a fleet export answers with a profile full of zeros. So the
 * question "is this one of ours?" is asked before it, and the answer is read off the file's own
 * field names: two or more of them means a profile someone wrote, and nothing else in this app
 * writes a file that clears that bar.
 */
export function looksLikeProfile(value: unknown): boolean {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return false;
  }

  const fields = new Set(Object.keys(blankRulesProfile));
  return Object.keys(value).filter((key) => fields.has(key)).length >= 2;
}

/**
 * The profile fields a parsed file does not carry.
 *
 * Walked off `blankRulesProfile` rather than off a list kept here, so a field added to
 * `RulesProfile` tomorrow is covered the day it is added.
 */
export function missingProfileFields(value: unknown): string[] {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return Object.keys(blankRulesProfile);
  }

  const carried = new Set(Object.keys(value));
  return Object.keys(blankRulesProfile).filter((field) => !carried.has(field));
}

/** What an import that could not be read at all says, wherever it failed. */
export const unreadableProfileMessage =
  'That file could not be read as a rules profile, so the numbers on screen have been left as they '
  + 'are. Pick the file you exported from here, or type the numbers in.';

/** Either a whole profile read off a file, or the reason the file was not applied. */
export type ProfileImport =
  | { readonly ok: true; readonly profile: RulesProfile }
  | { readonly ok: false; readonly problem: string };

/**
 * Reads the bytes of a picked file, and refuses anything that would cost the player numbers.
 *
 * Two doors, and both used to be open. The first - a file that is not a profile at all - was closed
 * by `looksLikeProfile`, which asks whether the file carries two or more of these field names. The
 * second is the one that bar cannot see: **a file that is unmistakably one of ours and is missing
 * most of it.** `{ name, dieFaces }` clears a two-field bar, and spreading it over the blank profile
 * wrote zeros into the other twenty-eight fields the player had typed off their own rulebook - a
 * truncated export, a half-written file, or a profile from a version that renamed the rest, and no
 * message either way.
 *
 * So an incomplete file is **reported, not applied**. Not partially applied either: half a profile
 * laid over a whole one is a set of numbers nobody entered, which is the thing this app is for not
 * doing. The names of the missing fields go in the message, because the player has to know which
 * numbers to go and find.
 */
export function readProfileFile(text: string): ProfileImport {
  let payload: unknown;
  try {
    payload = JSON.parse(text);
  } catch {
    return { ok: false, problem: unreadableProfileMessage };
  }

  if (!looksLikeProfile(payload)) {
    return { ok: false, problem: unreadableProfileMessage };
  }

  const missing = missingProfileFields(payload);
  if (missing.length > 0) {
    const total = Object.keys(blankRulesProfile).length;
    // Every one of them by name, however many that is. A count alone would tell the player they
    // have lost something without telling them what, and the list is what they take back to their
    // rulebook.
    return {
      ok: false,
      problem: `That file is a rules profile with ${missing.length} of its ${total} fields missing, so the `
        + 'numbers on screen have been left as they are rather than blanked. Fill these in, or pick '
        + `the file you exported from here: ${missing.join(', ')}.`,
    };
  }

  return { ok: true, profile: readProfile(payload) };
}

/** The profile as a file a player can keep or pass to the rest of the table. */
export function exportProfile(profile: RulesProfile): void {
  const blob = new Blob([JSON.stringify(profile, null, 2)], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `${profile.name.trim().replace(/[^\w-]+/g, '-') || 'rules-profile'}.json`;
  link.click();
  URL.revokeObjectURL(url);
}

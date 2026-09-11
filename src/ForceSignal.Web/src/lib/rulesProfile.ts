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

/** What this browser has saved, and what it is holding that this version cannot give back. */
export type SavedProfiles = {
  /** The profiles, ready to load onto the form. */
  readonly profiles: readonly RulesProfile[];
  /** One sentence per stored entry this version cannot read whole. Empty in the ordinary case. */
  readonly problems: readonly string[];
};

/** The store exactly as it sits, entry by entry, with nothing read into it. */
function storedEntries(): unknown[] {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    const parsed: unknown = raw ? JSON.parse(raw) : [];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    // A store whose text will not parse at all holds nothing this app can hand back entry by entry,
    // and there is no honest per-entry report to make about it either. The next save does overwrite
    // it; refusing to save instead would strand the player with no way out of a state only they can
    // see. What is preserved is the case that can be: entries inside a store that does parse.
    return [];
  }
}

/** The name a stored entry was saved under, for saying which one is being talked about. */
function storedName(entry: unknown): string | null {
  if (typeof entry !== 'object' || entry === null || Array.isArray(entry)) {
    return null;
  }

  const name = (entry as { name?: unknown }).name;
  return typeof name === 'string' && name.trim() ? name : null;
}

/**
 * Profiles this browser has saved, so a set of numbers is entered once and reused.
 *
 * This used to be `parsed.map(readProfile)`, and `readProfile` coerces - that is its job, because a
 * profile written by an older version is still a profile. Asked to read something that is *not* one
 * it answers with a profile full of zeros, so anything that ended up under this key came back
 * looking like a saved set of numbers, offered in the picker, and blanked the form the moment it
 * was chosen. That is the import bug through the other door, and this door had nowhere to report
 * into, so it happened in silence.
 *
 * Two answers, and which one an entry gets is the whole of it:
 *
 * - **Not a profile at all** - it fails the same `looksLikeProfile` bar the import uses. Not
 *   offered, and named in `problems`. It is left in the store untouched.
 * - **A profile carrying field names this version does not have** - a profile saved by a build that
 *   called two of its fields something else. Still offered, because it is the player's and most of
 *   it reads, with the unreadable part named so the blanks on the form are explained rather than
 *   discovered.
 *
 * What is *not* refused is incompleteness. A profile does not need all thirty fields to be usable -
 * the editor's own hints say to leave the torpedo reach and the salvo size at zero - so a stored
 * profile that omits them is a good profile and is handed back without comment.
 */
export function savedProfiles(): SavedProfiles {
  const profiles: RulesProfile[] = [];
  const problems: string[] = [];

  for (const entry of storedEntries()) {
    const named = storedName(entry);
    if (!looksLikeProfile(entry)) {
      problems.push(
        `Something saved in this browser${named ? ` under "${named}"` : ''} could not be read as a `
        + 'rules profile, so it is not offered above. It has been left where it is.',
      );
      continue;
    }

    profiles.push(readProfile(entry));

    const unknown = fieldsNotReadFrom(entry);
    if (unknown.length > 0) {
      problems.push(
        `"${named ?? profiles[profiles.length - 1].name}" was saved with ${unknown.length} field`
        + `${unknown.length === 1 ? '' : 's'} this version does not read, so ${unknown.length === 1
          ? 'it will not'
          : 'they will not'} appear on the form: ${unknown.join(', ')}.`,
      );
    }
  }

  return { profiles, problems };
}

/**
 * Field names a stored entry carries that this version of `RulesProfile` has no place for.
 *
 * The mirror of `fieldsBlankedBy`: that one asks what a file would cost the form, this one asks what
 * the form cannot show of a file. A renamed field is carried through a read and a write untouched -
 * `readProfile` spreads the source over the blank - but nothing ever displays it, so the number the
 * player typed is on disk and off the screen, and the field that replaced it reads zero.
 */
function fieldsNotReadFrom(value: unknown): string[] {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return [];
  }

  const fields = new Set(Object.keys(blankRulesProfile));
  return Object.keys(value).filter((key) => !fields.has(key));
}

/**
 * Saves a profile under its own name, replacing any earlier one by that name.
 *
 * Written back off the *stored* entries rather than off the ones that read cleanly. That is not
 * tidiness: once the reader above stopped handing back a coerced version of an entry it cannot
 * read, a writer built on the reader would have dropped that entry from the store on the next save,
 * and the repair for a silent blanking would have become a silent deletion.
 */
export function saveProfile(profile: RulesProfile): SavedProfiles {
  const kept = storedEntries().filter((entry) => storedName(entry) !== profile.name);
  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(sortedByName([...kept, profile])));
  return savedProfiles();
}

/** Forgets a saved profile by name, and only that one. */
export function deleteProfile(name: string): SavedProfiles {
  const kept = storedEntries().filter((entry) => storedName(entry) !== name);
  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(sortedByName(kept)));
  return savedProfiles();
}

function sortedByName(entries: unknown[]): unknown[] {
  return [...entries].sort((left, right) => (storedName(left) ?? '').localeCompare(storedName(right) ?? ''));
}

/**
 * Reads a profile out of parsed JSON, filling anything absent from the blank profile.
 *
 * Unknown fields are dropped and missing ones default to blank rather than to a number of this
 * app's choosing, so a half-written file comes back as a half-filled form the player can finish
 * instead of a profile that quietly plays wrong.
 */
function readProfile(value: unknown): RulesProfile {
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
function looksLikeProfile(value: unknown): boolean {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return false;
  }

  const fields = new Set(Object.keys(blankRulesProfile));
  return Object.keys(value).filter((key) => fields.has(key)).length >= 2;
}

/**
 * The fields a parsed file would blank, given what is on the form now.
 *
 * Not the same question as "what is the file missing?", and the difference is the whole of it. A
 * profile does not need all thirty fields to be usable - the editor's own hints say to leave the
 * torpedo reach and the salvo size at zero if your table does not use them - so a file that omits
 * them is a perfectly good file, and refusing it would be this app deciding which optional rules a
 * table has to play. What is never acceptable is the *loss*: a field the player has filled in going
 * to zero because the file said nothing about it.
 *
 * So the question asked is the one that matters. A field the file does not carry costs nothing when
 * the form has nothing in it either, and costs the player their own number when it does.
 *
 * Walked off `blankRulesProfile` rather than off a list kept here, so a field added to
 * `RulesProfile` tomorrow is covered the day it is added.
 */
function fieldsBlankedBy(value: unknown, current: RulesProfile): string[] {
  const carried = typeof value === 'object' && value !== null && !Array.isArray(value)
    ? new Set(Object.keys(value))
    : new Set<string>();

  const blank = blankRulesProfile as unknown as Record<string, unknown>;
  const filled = current as unknown as Record<string, unknown>;
  return Object.keys(blank).filter((field) =>
    !carried.has(field) && JSON.stringify(filled[field]) !== JSON.stringify(blank[field]));
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
 * What is refused is the **loss**, not the incompleteness. Those are two different things and
 * getting them confused costs a real capability: a profile does not need all thirty fields to be
 * usable, and a file that leaves out the torpedo and salvo numbers because that table does not play
 * them is a good file. Refusing it would be this app deciding which optional rules a table has to
 * use, which is the same sin in a different coat. So a partial file lands on an empty form, and is
 * turned away only when it would blank something already on the form - by name, all of them,
 * because the player has to know which of their numbers was at stake.
 *
 * @param current What is on the form now, and therefore what there is to lose.
 */
export function readProfileFile(text: string, current: RulesProfile = blankRulesProfile): ProfileImport {
  let payload: unknown;
  try {
    payload = JSON.parse(text);
  } catch {
    return { ok: false, problem: unreadableProfileMessage };
  }

  if (!looksLikeProfile(payload)) {
    return { ok: false, problem: unreadableProfileMessage };
  }

  const blanked = fieldsBlankedBy(payload, current);
  if (blanked.length > 0) {
    return {
      ok: false,
      problem: `That file is a rules profile, but it says nothing about ${blanked.length} field`
        + `${blanked.length === 1 ? '' : 's'} you have already filled in, so it would blank `
        + `${blanked.length === 1 ? 'it' : 'them'}. Nothing on screen has been changed. Start Blank `
        + `first if you meant to replace the lot: ${blanked.join(', ')}.`,
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

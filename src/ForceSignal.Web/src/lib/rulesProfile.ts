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

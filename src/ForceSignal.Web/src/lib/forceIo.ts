/**
 * Moving a StarGrunt force in and out of a file the player owns.
 *
 * A force is worth more than the game it was built for - a platoon is a lot of dice to type - so it
 * carries a format version from its very first write. That is the whole reason this exists in the
 * first slice rather than a saved library: the library can come later, but a file written today has
 * to still open next year.
 *
 * Import is the untrusted path. The file was picked by hand and may have been written by an older
 * version, edited in a text editor, or not be a force at all, so every value is coerced onto the
 * quality ladder or into range rather than trusted.
 */

import type { StarGruntForceFile, StarGruntUnit } from '../types.ts';

/** The version written today. Bump it only when an older file would be read wrongly. */
export const forceFormatVersion = 1;

const ladder = [4, 6, 8, 10, 12];

/** Puts a die onto the quality ladder, falling back to the middle rung. */
export function dieFrom(value: unknown, fallback = 8): number {
  const die = Number(value);
  return ladder.includes(die) ? die : fallback;
}

/** Puts a Leadership Value in range, 1 to 3, where 1 is the best. */
export function leadershipFrom(value: unknown, fallback = 2): number {
  const leadership = Number(value);
  return Number.isInteger(leadership) && leadership >= 1 && leadership <= 3 ? leadership : fallback;
}

const fatigues = ['Fresh', 'Tired', 'Exhausted'];

/** Keeps a fatigue level to one the rules name, falling back to rested. */
export function fatigueFrom(value: unknown, fallback = 'Fresh'): string {
  return typeof value === 'string' && fatigues.includes(value) ? value : fallback;
}

/**
 * Everything one side has on the table, ready to write out.
 *
 * @throws {Error} When a unit's roster or weapon list did not arrive on the snapshot at all.
 */
export function toForceFile(side: string, units: StarGruntUnit[]): StarGruntForceFile {
  const onThisSide = units.filter((unit) => unit.side === side);

  // A snapshot is whatever the server sent: `get<StarGruntSnapshot>` asserts the shape rather than
  // checking it, so a unit from a server written before figures were on the wire, or from a backup
  // restored from one, really does arrive without a `figures` array. Mapping over it threw, and the
  // throw landed in a bare `onClick` - so the Export button did nothing at all, said nothing, and
  // looked exactly like a button that had worked.
  //
  // Refused rather than skipped or filled in. The armour dice live nowhere else on a snapshot, so a
  // file written without them is a roster with the player's own numbers missing, and import reads a
  // figure with no armour die back as a die this app chose. Naming the units is what lets the
  // player tell version skew from a game they have half set up.
  const incomplete = onThisSide.filter((unit) => !Array.isArray(unit.figures) || !Array.isArray(unit.weapons));
  if (incomplete.length > 0) {
    throw new Error(
      `This game did not send a full roster for ${incomplete.map((unit) => unit.name).join(', ')}, so a `
      + 'file written now would be missing figures or weapons. Reopen the game against a server that '
      + 'sends them rather than exporting an incomplete force.',
    );
  }

  return {
    formatVersion: forceFormatVersion,
    side,
    units: onThisSide
      .map((unit) => ({
        id: unit.id,
        name: unit.name,
        level: unit.level,
        qualityDie: unit.qualityDie,
        leadershipValue: unit.leadershipValue,
        fatigue: unit.fatigue,
        // The roster the player entered, each figure with the armour die they chose. Full strength
        // rather than what is left, because a force file is a roster and not a casualty return -
        // which is what the unit's figure list already is, casualties being counted beside it.
        //
        // This used to write a literal 6 per figure, which threw away the player's own numbers on
        // the way into the player's own file and put a rules number this app does not own into it
        // instead. The round trip could not see it: import read back whatever export had written.
        figures: unit.figures.map((figure) => ({ armourDie: figure.armourDie })),
        weapons: unit.weapons.map((weapon) => ({
          name: weapon.name,
          impactDie: weapon.impactDie,
          isSupport: weapon.isSupport,
          isCloseRange: weapon.isCloseRange,
          supportFirepowerDie: weapon.supportFirepowerDie,
          neverJoinsSquadFire: weapon.neverJoinsSquadFire,
        })),
      })),
  };
}

/**
 * Reads a force file back, coercing anything doubtful rather than refusing the file.
 *
 * @throws {Error} When the payload is not a force at all.
 */
export function fromForceFile(payload: unknown): StarGruntForceFile {
  if (typeof payload !== 'object' || payload === null) {
    throw new Error('That file does not hold a force.');
  }

  const raw = payload as Partial<StarGruntForceFile> & { units?: unknown };
  if (!Array.isArray(raw.units)) {
    throw new Error('That file does not hold a force.');
  }

  return {
    // An unversioned file is read as version 1, which is what every file written before the field
    // existed actually is.
    formatVersion: Number(raw.formatVersion) || 1,
    side: typeof raw.side === 'string' && raw.side.trim() ? raw.side.trim() : 'blue',
    units: raw.units.map((entry, index) => {
      const unit = (entry ?? {}) as Record<string, unknown>;
      const figures = Array.isArray(unit.figures) ? unit.figures : [];
      const weapons = Array.isArray(unit.weapons) ? unit.weapons : [];
      return {
        id: typeof unit.id === 'string' && unit.id ? unit.id : `unit-${index + 1}`,
        name: typeof unit.name === 'string' && unit.name.trim() ? unit.name.trim() : `Squad ${index + 1}`,
        level: typeof unit.level === 'string' && unit.level.trim() ? unit.level.trim() : 'Squad',
        qualityDie: dieFrom(unit.qualityDie),
        leadershipValue: leadershipFrom(unit.leadershipValue),
        fatigue: fatigueFrom(unit.fatigue),
        // A squad of nobody is not a squad; a squad of two hundred is a typo.
        figures: (figures.length > 0 ? figures : [{}])
          .slice(0, 40)
          .map((figure) => ({ armourDie: dieFrom((figure as Record<string, unknown>)?.armourDie, 6) })),
        weapons: (weapons.length > 0 ? weapons : [{ name: 'Rifles' }])
          .slice(0, 12)
          .map((weapon, weaponIndex) => {
            const entryWeapon = (weapon ?? {}) as Record<string, unknown>;
            return {
              name: typeof entryWeapon.name === 'string' && entryWeapon.name.trim()
                ? entryWeapon.name.trim()
                : `Weapon ${weaponIndex + 1}`,
              impactDie: dieFrom(entryWeapon.impactDie),
              isSupport: entryWeapon.isSupport === true,
              isCloseRange: entryWeapon.isCloseRange === true,
              supportFirepowerDie: dieFrom(entryWeapon.supportFirepowerDie, 6),
              neverJoinsSquadFire: entryWeapon.neverJoinsSquadFire === true,
            };
          }),
      };
    }),
  };
}

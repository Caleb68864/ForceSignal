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

/**
 * The version written today. Bump it only when an older file would be read wrongly.
 *
 * Not exported. It was, and nothing imported it: the one test about the version asserts against a
 * literal 1 on purpose, because asserting against this constant is the tautology that let a bump to
 * 2 - the change that strands every file already written - pass a green suite.
 */
const forceFormatVersion = 1;

const ladder = [4, 6, 8, 10, 12];

/**
 * The die a file gave, or null when it gave none this app can use.
 *
 * This used to take a fallback rung - 8 for a quality or impact die, 6 for an armour die - so a file
 * that said nothing about a die came back carrying one, and a file that said something off the
 * ladder came back carrying a different number from the one it held. Both are the same defect as the
 * export that wrote a literal 6 for every figure: a rating off a record card, chosen by this app, in
 * the player's own roster and then posted to the server as if the player had typed it.
 *
 * Null is not a refusal on its own. What is done about it is decided at the call site, which knows
 * whether the number is one the file had to carry.
 */
export function dieFrom(value: unknown): number | null {
  const die = Number(value);
  return ladder.includes(die) ? die : null;
}

/**
 * The die a weapon adds to a squad volley, where zero means the card did not give one.
 *
 * Separate from {@link dieFrom} because zero is a real answer here rather than a gap: most weapons
 * never join a volley, so a file saying nothing about this die is a complete file. The server reads
 * zero the same way and refuses such a weapon by name only if it is actually asked to join one.
 */
function supportDieFrom(value: unknown): number {
  return dieFrom(value) ?? 0;
}

/** The Leadership Value a file gave, 1 to 3 where 1 is best, or null when it gave none. */
function leadershipFrom(value: unknown): number | null {
  const leadership = Number(value);
  return Number.isInteger(leadership) && leadership >= 1 && leadership <= 3 ? leadership : null;
}

const fatigues = ['Fresh', 'Tired', 'Exhausted'];

/** Keeps a fatigue level to one the rules name, falling back to rested. */
function fatigueFrom(value: unknown, fallback = 'Fresh'): string {
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
 * Reads a force file back, coercing the words and refusing to invent the numbers.
 *
 * Two different things get confused here and it is worth keeping them apart, because getting it
 * wrong costs something either way.
 *
 * **Incompleteness that costs nobody a number is allowed.** A unit with no weapons on it is a
 * command element; a weapon with no die for joining a volley is most weapons; a unit with no name,
 * level or fatigue gets a word chosen here, because a word is not a rating. Refusing any of those
 * would be this app deciding what a force is allowed to contain, which is the same sin as shipping
 * the numbers.
 *
 * **A missing die is refused, by name.** The import used to answer a missing or off-ladder quality
 * die with 8, an armour die with 6 and an impact die with 8, and a unit that listed no weapons at
 * all with a weapon called Rifles. That is a record card written by this app, put into the player's
 * roster and posted to the server under their name - and, because the server takes what it is given,
 * never questioned again. So the file is turned away with every gap named, and nothing is imported:
 * the player fixes the file, or types the unit in, either way knowing which of their numbers was at
 * stake.
 *
 * @throws {Error} When the payload is not a force at all, or when it leaves out a die.
 */
export function fromForceFile(payload: unknown): StarGruntForceFile {
  if (typeof payload !== 'object' || payload === null) {
    throw new Error('That file does not hold a force.');
  }

  const raw = payload as Partial<StarGruntForceFile> & { units?: unknown };
  if (!Array.isArray(raw.units)) {
    throw new Error('That file does not hold a force.');
  }

  const gaps: string[] = [];

  const units = raw.units.map((entry, index) => {
    const unit = (entry ?? {}) as Record<string, unknown>;
    const figures = Array.isArray(unit.figures) ? unit.figures : [];
    const weapons = Array.isArray(unit.weapons) ? unit.weapons : [];
    const name = typeof unit.name === 'string' && unit.name.trim() ? unit.name.trim() : `Squad ${index + 1}`;
    const missing = (what: string) => gaps.push(`${name} ${what}`);

    const qualityDie = dieFrom(unit.qualityDie);
    if (qualityDie === null) {
      missing(`has no quality die on the ladder (${ladder.join(', ')})`);
    }

    const leadershipValue = leadershipFrom(unit.leadershipValue);
    if (leadershipValue === null) {
      missing('has no Leadership Value from 1 to 3');
    }

    // A squad of nobody is not a squad; a squad of two hundred is a typo. The empty roster used to
    // be answered with one figure on a D6, which invented the man and his armour together.
    if (figures.length === 0) {
      missing('lists no figures at all');
    }

    return {
      id: typeof unit.id === 'string' && unit.id ? unit.id : `unit-${index + 1}`,
      name,
      level: typeof unit.level === 'string' && unit.level.trim() ? unit.level.trim() : 'Squad',
      qualityDie: qualityDie ?? 0,
      leadershipValue: leadershipValue ?? 0,
      fatigue: fatigueFrom(unit.fatigue),
      figures: figures
        .slice(0, 40)
        .map((figure, figureIndex) => {
          const armourDie = dieFrom((figure as Record<string, unknown>)?.armourDie);
          if (armourDie === null) {
            missing(`gives figure ${figureIndex + 1} no armour die`);
          }

          return { armourDie: armourDie ?? 0 };
        }),
      weapons: weapons
        .slice(0, 12)
        .map((weapon, weaponIndex) => {
          const entryWeapon = (weapon ?? {}) as Record<string, unknown>;
          const weaponName = typeof entryWeapon.name === 'string' && entryWeapon.name.trim()
            ? entryWeapon.name.trim()
            : `Weapon ${weaponIndex + 1}`;
          const impactDie = dieFrom(entryWeapon.impactDie);
          if (impactDie === null) {
            missing(`gives ${weaponName} no impact die`);
          }

          return {
            name: weaponName,
            impactDie: impactDie ?? 0,
            isSupport: entryWeapon.isSupport === true,
            isCloseRange: entryWeapon.isCloseRange === true,
            supportFirepowerDie: supportDieFrom(entryWeapon.supportFirepowerDie),
            neverJoinsSquadFire: entryWeapon.neverJoinsSquadFire === true,
          };
        }),
    };
  });

  if (gaps.length > 0) {
    throw new Error(
      'That file leaves out dice this app will not choose for you, so nothing has been imported. '
      + 'Put them in the file, or add the units by hand: '
      + `${gaps.join('; ')}.`,
    );
  }

  return {
    // An unversioned file is read as version 1, which is what every file written before the field
    // existed actually is.
    formatVersion: Number(raw.formatVersion) || 1,
    side: typeof raw.side === 'string' && raw.side.trim() ? raw.side.trim() : 'blue',
    units,
  };
}

import { describe, expect, it } from 'vitest';
import { dieFrom, fromForceFile, toForceFile } from './forceIo.ts';
import type { StarGruntUnit } from '../types.ts';

/**
 * A force file is worth more than the game it was built for - a platoon is a lot of dice to type -
 * so what matters here is that a file written today opens later, and that a file somebody has
 * edited by hand still loads rather than throwing the whole force away.
 */
function unit(overrides: Partial<StarGruntUnit> = {}): StarGruntUnit {
  return {
    id: 'alpha',
    name: 'Alpha Squad',
    side: 'blue',
    level: 'Squad',
    qualityDie: 8,
    leadershipValue: 2,
    fatigue: 'Fresh',
    figures: Array.from({ length: 8 }, () => ({ armourDie: 12 })),
    figuresAlive: 5,
    fullStrength: 8,
    figuresWounded: 1,
    isLeaderDown: false,
    suppressionMarkers: 2,
    confidence: 'Shaken',
    isDisorganised: false,
    isInCover: true,
    nextMoveLeavesCover: false,
    reactionTestCleared: false,
    hasActivated: false,
    weapons: [{ name: 'Rifles', impactDie: 10, isSupport: false, isCloseRange: false, supportFirepowerDie: 6, neverJoinsSquadFire: false }],
    canActivate: true,
    activationBlocker: null,
    weaponLegality: [],
    ...overrides,
  };
}

describe('toForceFile', () => {
  it('stamps the format version so a file written today can be read later', () => {
    // Against the literal, not against `forceFormatVersion`. This read
    // `toBe(forceFormatVersion)`, and toForceFile stamps the field *from that same constant*, so
    // both sides moved together: bumping the constant to 2 - which is exactly the change that would
    // strand every file already written - left the whole suite green. A version assertion that the
    // version can satisfy by changing is not an assertion.
    expect(toForceFile('blue', [unit()]).formatVersion).toBe(1);
  });

  it('still opens a file written by the version that shipped', () => {
    // Literal bytes, kept by hand rather than produced by toForceFile, because "a file written
    // today has to still open next year" cannot be checked by writing one today and reading it
    // back: that only proves the two halves agree with each other. This is what a v1 file is.
    const onDisk = `{
      "formatVersion": 1,
      "side": "blue",
      "units": [{
        "id": "alpha",
        "name": "Alpha Squad",
        "level": "Squad",
        "qualityDie": 10,
        "leadershipValue": 1,
        "fatigue": "Fresh",
        "figures": [{ "armourDie": 12 }, { "armourDie": 4 }],
        "weapons": [{
          "name": "Gauss Rifles",
          "impactDie": 10,
          "isSupport": false,
          "isCloseRange": false,
          "supportFirepowerDie": 6,
          "neverJoinsSquadFire": false
        }]
      }]
    }`;

    const read = fromForceFile(JSON.parse(onDisk));
    const squad = read.units[0];

    // Reached-the-subject: the fixture really did produce a unit rather than being dropped.
    expect(read.units).toHaveLength(1);
    expect(squad.name).toBe('Alpha Squad');

    expect(squad.qualityDie).toBe(10);
    expect(squad.leadershipValue).toBe(1);
    expect(squad.figures.map((figure) => figure.armourDie)).toEqual([12, 4]);
    expect(squad.weapons[0].name).toBe('Gauss Rifles');
    expect(squad.weapons[0].impactDie).toBe(10);
  });

  it('takes only the side asked for', () => {
    const file = toForceFile('blue', [unit(), unit({ id: 'bravo', side: 'red' })]);

    expect(file.units).toHaveLength(1);
    expect(file.units[0].id).toBe('alpha');
  });

  it('writes the roster at full strength, not what is left of it', () => {
    // A force file is a roster, not a casualty return: reimporting a shot-up squad should give
    // back the squad, not the survivors.
    expect(toForceFile('blue', [unit()]).units[0].figures).toHaveLength(8);
  });

  it('writes the armour die the player picked, not one of its own', () => {
    // Export wrote a literal 6 for every figure in every unit. That is both halves of a bad thing
    // at once: a rules number this app does not own, and silent loss of the player's own data in
    // the player's own file. A squad the player put on D12 armour came back on D6, and the round
    // trip below could not see it because import read back what export had written.
    const file = toForceFile('blue', [unit({ figures: [{ armourDie: 12 }, { armourDie: 10 }, { armourDie: 12 }] })]);

    expect(file.units[0].figures.map((figure) => figure.armourDie)).toEqual([12, 10, 12]);
  });

  it('keeps a mixed roster mixed through a whole round trip', () => {
    // A squad may mix armour - that is why figures are listed rather than counted - so the file has
    // to carry each figure's own die rather than one die for the unit.
    const written = toForceFile('blue', [unit({ figures: [{ armourDie: 4 }, { armourDie: 12 }] })]);

    const read = fromForceFile(JSON.parse(JSON.stringify(written)));

    expect(read.units[0].figures.map((figure) => figure.armourDie)).toEqual([4, 12]);
  });
});

describe('fromForceFile', () => {
  it('round-trips a force it wrote', () => {
    const written = toForceFile('blue', [unit()]);

    expect(fromForceFile(JSON.parse(JSON.stringify(written)))).toEqual(written);
  });

  it('reads a file with no version as version one, which is what it is', () => {
    const read = fromForceFile({ side: 'red', units: [{ name: 'Bravo' }] });

    expect(read.formatVersion).toBe(1);
    expect(read.side).toBe('red');
  });

  it('puts a die that is not on the ladder back onto it', () => {
    const read = fromForceFile({ units: [{ name: 'Bravo', qualityDie: 7, leadershipValue: 9 }] });

    expect(read.units[0].qualityDie).toBe(8);
    // Leadership is a value from 1 to 3, not a die, so 9 is not a typo to keep.
    expect(read.units[0].leadershipValue).toBe(2);
  });

  it('gives a squad of nobody one figure rather than none', () => {
    expect(fromForceFile({ units: [{ name: 'Bravo', figures: [] }] }).units[0].figures).toHaveLength(1);
  });

  it('refuses something that is not a force at all', () => {
    expect(() => fromForceFile(null)).toThrow();
    expect(() => fromForceFile({ nope: true })).toThrow();
  });
});

describe('dieFrom', () => {
  it('keeps a die that is on the ladder', () => {
    expect(dieFrom(12)).toBe(12);
  });

  it('falls back for anything else', () => {
    expect(dieFrom(5)).toBe(8);
    expect(dieFrom(undefined, 6)).toBe(6);
  });
});

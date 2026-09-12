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

  it('refuses a unit whose roster the snapshot never carried, by name', () => {
    // Not a fabricated shape: `get<StarGruntSnapshot>` asserts the type rather than checking it, so
    // a snapshot really is whatever the server sent. A server written before figures were on the
    // wire - or a backup restored from one - sends a unit with no `figures` at all, and mapping
    // over it threw. The units are parsed from JSON here rather than built in TypeScript, so this
    // fixture cannot hold anything the wire could not.
    const fromAnOlderServer = JSON.parse(JSON.stringify(unit())) as Record<string, unknown>;
    delete fromAnOlderServer.figures;

    // Reached-the-subject: it is still recognisably the unit, on the side being exported.
    expect(fromAnOlderServer.name).toBe('Alpha Squad');
    expect(fromAnOlderServer.side).toBe('blue');

    expect(() => toForceFile('blue', [fromAnOlderServer as unknown as StarGruntUnit]))
      .toThrow(/Alpha Squad/);
    // The other side of the same snapshot is not this side's problem.
    expect(() => toForceFile('red', [fromAnOlderServer as unknown as StarGruntUnit])).not.toThrow();
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
    // A whole unit rather than a bare name: this test is about the version field, and a fixture
    // that only parsed because the import filled the dice in was standing on the defect below.
    const read = fromForceFile({
      side: 'red',
      units: [{
        name: 'Bravo',
        qualityDie: 8,
        leadershipValue: 2,
        figures: [{ armourDie: 6 }],
        weapons: [{ name: 'Rifles', impactDie: 8 }],
      }],
    });

    expect(read.formatVersion).toBe(1);
    expect(read.side).toBe('red');
  });

  it('refuses something that is not a force at all', () => {
    expect(() => fromForceFile(null)).toThrow();
    expect(() => fromForceFile({ nope: true })).toThrow();
  });
});

/**
 * The import used to answer every gap in a file with a rung off the quality ladder: quality die 8,
 * armour die 6, impact die 8, and - when the file listed no weapons at all - a whole weapon called
 * Rifles with an impact die of its own. Those are readings off a record card, written into the
 * player's roster and then posted to the server as though the player had typed them.
 *
 * Each check below is paired with the control that must still be accepted, because the trap here is
 * the over-strict fix: a file is allowed to be incomplete in every way that costs nobody a number.
 */
describe('a force file with a die missing is not given one', () => {
  /** A file that is complete, so a probe can show its subject was reached before it was broken. */
  function whole() {
    return {
      formatVersion: 1,
      side: 'blue',
      units: [{
        id: 'bravo',
        name: 'Bravo Squad',
        level: 'Squad',
        qualityDie: 10,
        leadershipValue: 1,
        fatigue: 'Fresh',
        figures: [{ armourDie: 12 }, { armourDie: 4 }],
        weapons: [{ name: 'Gauss Rifles', impactDie: 10 }],
      }],
    } as Record<string, unknown>;
  }

  /** Drops one field from the whole file, refusing to pretend when the path is not there. */
  function without(path: string): Record<string, unknown> {
    const file = whole();
    const unit = (file.units as Record<string, unknown>[])[0];
    const holder = path.startsWith('figure.')
      ? (unit.figures as Record<string, unknown>[])[0]
      : path.startsWith('weapon.')
        ? (unit.weapons as Record<string, unknown>[])[0]
        : unit;
    const field = path.replace(/^(figure|weapon)\./, '');

    // Reached-the-subject: the field this probe is about really is on the fixture. A typo in the
    // path would otherwise delete nothing and the probe would claim a clean pass.
    if (!Object.hasOwn(holder, field)) {
      throw new Error(`the fixture has no ${path} to remove, so this probe proves nothing`);
    }

    delete holder[field];
    return file;
  }

  it('reads the whole file, which is the control every case below is a break of', () => {
    const read = fromForceFile(whole());

    expect(read.units).toHaveLength(1);
    expect(read.units[0].qualityDie).toBe(10);
    expect(read.units[0].leadershipValue).toBe(1);
    expect(read.units[0].figures.map((figure) => figure.armourDie)).toEqual([12, 4]);
    expect(read.units[0].weapons[0].impactDie).toBe(10);
  });

  it('refuses a unit with no quality die, by name, rather than putting it on one', () => {
    expect(() => fromForceFile(without('qualityDie'))).toThrow(/Bravo Squad/);
  });

  it('refuses a unit with a quality die that is not on the ladder', () => {
    const file = whole();
    (file.units as Record<string, unknown>[])[0].qualityDie = 7;

    expect(() => fromForceFile(file)).toThrow(/Bravo Squad/);
  });

  it('refuses a unit with no Leadership Value rather than settling on the middle one', () => {
    expect(() => fromForceFile(without('leadershipValue'))).toThrow(/Bravo Squad/);
  });

  it('writes out the gap a snapshot came back with rather than closing it on the way past', () => {
    // The snapshot's Leadership Value is nullable now: a game stored before units carried one comes
    // back saying so, and the server refuses every morale roll by name until somebody enters it.
    // Export must not turn that into a 2 on its way into the player's own file, and the round trip
    // must not read one back - which is exactly the defect export already paid for on armour dice.
    const written = toForceFile('blue', [unit({ name: 'Bravo Squad', leadershipValue: null })]);

    expect(written.units[0].leadershipValue).toBeNull();
    expect(() => fromForceFile(JSON.parse(JSON.stringify(written)))).toThrow(/Bravo Squad/);
  });

  it('refuses a figure with no armour die, which is the number export was fixed for', () => {
    expect(() => fromForceFile(without('figure.armourDie'))).toThrow(/Bravo Squad/);
  });

  it('refuses a weapon with no impact die', () => {
    expect(() => fromForceFile(without('weapon.impactDie'))).toThrow(/Gauss Rifles|Bravo Squad/);
  });

  it('refuses a unit with an empty roster rather than inventing a figure to put in it', () => {
    const file = whole();
    (file.units as Record<string, unknown>[])[0].figures = [];

    expect(() => fromForceFile(file)).toThrow(/Bravo Squad/);
  });

  it('names every unit at fault, not only the first', () => {
    const file = whole();
    const second = whole().units as Record<string, unknown>[];
    second[0].id = 'charlie';
    second[0].name = 'Charlie Squad';
    delete second[0].qualityDie;
    (file.units as Record<string, unknown>[]).push(second[0]);
    delete (file.units as Record<string, unknown>[])[0].qualityDie;

    expect(() => fromForceFile(file)).toThrow(/Bravo Squad[\s\S]*Charlie Squad/);
  });
});

describe('a force file is still allowed to be incomplete where nothing is lost', () => {
  function unitWith(overrides: Record<string, unknown>): Record<string, unknown> {
    return {
      units: [{
        name: 'Bravo Squad',
        qualityDie: 10,
        leadershipValue: 1,
        figures: [{ armourDie: 12 }],
        weapons: [{ name: 'Gauss Rifles', impactDie: 10 }],
        ...overrides,
      }],
    };
  }

  it('carries a unit that lists no weapons rather than issuing it one', () => {
    // A command element carries nothing. The old fallback handed it a weapon called Rifles on a
    // die of this app's choosing, which is a whole line off a record card nobody wrote.
    const read = fromForceFile(unitWith({ weapons: undefined }));

    expect(read.units[0].name).toBe('Bravo Squad');
    expect(read.units[0].weapons).toEqual([]);
  });

  it('carries a weapon whose card gives no die for joining a volley', () => {
    // The ordinary case: most weapons never join one. Zero is "the card did not say", and the
    // server refuses such a weapon by name only when it is actually asked to join a volley.
    const read = fromForceFile(unitWith({ weapons: [{ name: 'Gauss Rifles', impactDie: 10 }] }));

    expect(read.units[0].weapons[0].supportFirepowerDie).toBe(0);
  });

  it('still falls back on the words, which cost nobody a number', () => {
    const read = fromForceFile(unitWith({ name: undefined, level: undefined, fatigue: undefined }));

    expect(read.units[0].level).toBe('Squad');
    expect(read.units[0].fatigue).toBe('Fresh');
    expect(read.units[0].name).toBe('Squad 1');
  });
});

describe('dieFrom', () => {
  it('keeps a die that is on the ladder', () => {
    expect(dieFrom(12)).toBe(12);
  });

  it('hands back nothing at all for anything else, rather than a rung of its own choosing', () => {
    expect(dieFrom(5)).toBeNull();
    expect(dieFrom(undefined)).toBeNull();
    expect(dieFrom('12')).toBe(12);
  });
});

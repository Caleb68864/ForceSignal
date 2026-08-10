import { describe, expect, it } from 'vitest';
import { dieFrom, forceFormatVersion, fromForceFile, toForceFile } from './forceIo.ts';
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
    leadershipDie: 8,
    figuresAlive: 5,
    fullStrength: 8,
    figuresWounded: 1,
    suppressionMarkers: 2,
    confidence: 'Shaken',
    isDisorganised: false,
    isInCover: true,
    hasActivated: false,
    weapons: [{ name: 'Rifles', impactDie: 10, isSupport: false, isCloseRange: false }],
    canActivate: true,
    activationBlocker: null,
    weaponLegality: [],
    ...overrides,
  };
}

describe('toForceFile', () => {
  it('stamps the format version so a file written today can be read later', () => {
    expect(toForceFile('blue', [unit()]).formatVersion).toBe(forceFormatVersion);
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
    const read = fromForceFile({ units: [{ name: 'Bravo', qualityDie: 7, leadershipDie: 'nonsense' }] });

    expect(read.units[0].qualityDie).toBe(8);
    expect(read.units[0].leadershipDie).toBe(8);
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

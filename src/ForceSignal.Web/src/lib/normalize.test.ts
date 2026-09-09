import { describe, expect, it } from 'vitest';
import { expandLegacyArc, normalizeArcs, normalizeBattleView, normalizeFleetColor, normalizeGameHandle, normalizeGameMode, normalizeSession, normalizeShipIconKey, normalizeWeaponMount } from './normalize.ts';
import { defaultShipForm } from '../constants.ts';
import { newWeaponMount } from './weapons.ts';

/**
 * These take `unknown` on purpose: a snapshot comes off the wire and a fleet comes out of a file
 * the player picked, possibly written by an older version. Nothing may be assumed present or
 * well-typed, and nothing may throw part-way through a load.
 */
describe('normalizeArcs', () => {
  it('keeps the arcs a mount was given, in the canonical order', () => {
    expect(normalizeArcs(['ForeStarboard', 'Fore'], null)).toEqual(['Fore', 'ForeStarboard']);
  });

  // Every weapon has the aft arc blacked out, so it can never survive normalization whatever a
  // file claims.
  it('strips the aft arc whatever the source says', () => {
    expect(normalizeArcs(['Aft'], null)).toEqual(['Fore']);
    expect(normalizeArcs(['Fore', 'Aft'], null)).toEqual(['Fore']);
  });

  it('falls back to fore rather than leaving a mount bearing nowhere', () => {
    expect(normalizeArcs([], null)).toEqual(['Fore']);
    expect(normalizeArcs('nonsense', null)).toEqual(['Fore']);
    expect(normalizeArcs(null, null)).toEqual(['Fore']);
  });

  it('expands a mount written before the six arcs existed', () => {
    expect(normalizeArcs(null, 'Port')).toEqual(['AftPort', 'ForePort']);
  });
});

describe('expandLegacyArc', () => {
  // The old arcs were ninety degrees wide, so each becomes the two sixty-degree arcs on that side.
  it('widens a four-arc name into six-arc terms', () => {
    expect(expandLegacyArc('Port')).toEqual(['ForePort', 'AftPort']);
    expect(expandLegacyArc('Starboard')).toEqual(['ForeStarboard', 'AftStarboard']);
    expect(expandLegacyArc('All')).toHaveLength(5);
  });

  it('does not resurrect the blind spot from an old aft mount', () => {
    expect(expandLegacyArc('Aft')).not.toContain('Aft');
  });
});

describe('normalizeShipIconKey', () => {
  it('takes a key it already knows', () => {
    expect(normalizeShipIconKey('carrier')).toBe('carrier');
  });

  it('guesses from the class name when the key is missing', () => {
    expect(normalizeShipIconKey(null, 'Heavy Cruiser')).toBe('cruiser');
    expect(normalizeShipIconKey(undefined, 'Escort Frigate')).toBe('escort');
  });

  it('settles on a cruiser rather than nothing', () => {
    expect(normalizeShipIconKey(null, null)).toBe('cruiser');
    expect(normalizeShipIconKey(42, {})).toBe('cruiser');
  });
});

describe('normalizeFleetColor', () => {
  it('keeps a well-formed colour', () => {
    expect(normalizeFleetColor('#ff8800')).toBe('#ff8800');
  });

  // This value reaches a CSS custom property, so anything that is not plainly a hex colour is
  // replaced rather than passed through.
  it('replaces anything that is not one', () => {
    expect(normalizeFleetColor('red')).toBe('#47f1ff');
    expect(normalizeFleetColor('#ff8800; background: url(x)')).toBe('#47f1ff');
    expect(normalizeFleetColor(null)).toBe('#47f1ff');
  });
});

describe('normalizeSession', () => {
  const session = { matchId: 'm', participantId: 'p', participantToken: 't', joinCode: 'ABC' };

  it('keeps a complete session and nothing else off it', () => {
    expect(normalizeSession({ ...session, extra: true })).toEqual(session);
  });

  // A session missing its match id produced `GET /api/matches/undefined/snapshot` on every load.
  it('rejects one with any field missing, blank or not text', () => {
    expect(normalizeSession({ ...session, matchId: undefined })).toBeNull();
    expect(normalizeSession({ ...session, participantToken: '  ' })).toBeNull();
    expect(normalizeSession({ ...session, joinCode: 42 })).toBeNull();
    expect(normalizeSession('session')).toBeNull();
    expect(normalizeSession(null)).toBeNull();
  });
});

describe('normalizeGameHandle', () => {
  it('needs both the id and the token', () => {
    expect(normalizeGameHandle({ gameId: 'g', token: 't' })).toEqual({ gameId: 'g', token: 't' });
    expect(normalizeGameHandle({ gameId: 'g' })).toBeNull();
    expect(normalizeGameHandle({ gameId: '', token: 't' })).toBeNull();
    expect(normalizeGameHandle('g')).toBeNull();
  });
});

// Both are read straight out of storage on mount and picked a screen. A value that is not one of
// the known literals would have rendered nothing at all.
describe('normalizeGameMode', () => {
  it('accepts only the three engines', () => {
    expect(normalizeGameMode('fullthrust')).toBe('fullthrust');
    expect(normalizeGameMode('stargrunt')).toBe('stargrunt');
    expect(normalizeGameMode('dirtside')).toBe('dirtside');
    expect(normalizeGameMode('Dirtside')).toBeNull();
    expect(normalizeGameMode({ mode: 'dirtside' })).toBeNull();
    expect(normalizeGameMode(null)).toBeNull();
  });
});

describe('normalizeBattleView', () => {
  it('accepts only the three workspace tabs', () => {
    expect(normalizeBattleView('ships')).toBe('ships');
    expect(normalizeBattleView('map')).toBe('map');
    expect(normalizeBattleView('log')).toBe('log');
    expect(normalizeBattleView('fleet')).toBeNull();
    expect(normalizeBattleView(1)).toBeNull();
  });
});

describe('a mount nobody filled in', () => {
  // This app ships no stat blocks, and a starting point the player is "expected to replace" is
  // still a number it shipped. A blank mount used to arrive as a "Class-2 Beam" firing two dice
  // out to twenty-four: a class name, a damage rating and a reach, in three separate copies plus
  // an invisible fourth on the server.
  const published = ['Class-2 Beam'];

  it('carries no class name from any of the copies', () => {
    for (const name of published) {
      expect(newWeaponMount().name).not.toBe(name);
      expect(defaultShipForm.weapons[0].name).not.toBe(name);
      expect(normalizeWeaponMount({}).name).not.toBe(name);
    }
  });

  it('carries no damage rating or reach anybody could mistake for a reading', () => {
    // The floors the server's own clamps impose, which mean "not entered".
    for (const mount of [newWeaponMount(), defaultShipForm.weapons[0], normalizeWeaponMount({})]) {
      expect(mount.attackDice).toBe(1);
      expect(mount.maxRange).toBe(1);
    }
  });

  it('still keeps what the player did enter', () => {
    const mount = normalizeWeaponMount({ name: 'Heavy Battery', attackDice: 4, maxRange: 30 });
    expect(mount.name).toBe('Heavy Battery');
    expect(mount.attackDice).toBe(4);
    expect(mount.maxRange).toBe(30);
  });
});

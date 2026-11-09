// @vitest-environment jsdom
/**
 * The last-shot card says what the server's record says, and nothing it does not.
 */

import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { lastShotBy, ShotResultCard } from './ShotResultCard.tsx';
import type { FiringResult, Ship } from '../types.ts';

afterEach(cleanup);

const target = { id: 'picket', name: 'Picket', isDestroyed: false } as Ship;

function result(patch: Partial<FiringResult>): FiringResult {
  return {
    attackerShipId: 'lance', targetShipId: 'picket', weaponId: 'w', weaponName: 'Battery A', turnNumber: 1,
    range: 19, rangeBand: 'medium', arc: 'Fore', rawDice: 5, rangePenalty: 1, screenReduction: 0, systemPenalty: 0,
    damage: 0, armorDamageApplied: 0, hullDamageApplied: 0, diceRolls: [], weaponKind: 'Beam', toHitNumber: null, isHit: null,
    ...patch,
  };
}

describe('the last shot', () => {
  it('shows every die and the damage actually applied, not the damage rolled', () => {
    render(<ShotResultCard result={result({ diceRolls: [2, 3, 8, 7, 8], damage: 6, hullDamageApplied: 1 })} ships={[{ ...target, isDestroyed: true }]} />);
    expect([...document.querySelectorAll('.die')].map((die) => die.textContent)).toEqual(['2', '3', '8', '7', '8']);
    expect(screen.getByText(/0 armour, 1 hull/).textContent).toContain('Picket destroyed');
    expect(screen.queryByText(/6/)).toBeNull();
  });

  it('says what screens took off', () => {
    render(<ShotResultCard result={result({ diceRolls: [5, 6], screenReduction: 2 })} ships={[target]} />);
    expect(screen.getByText('Screens took off 2.')).toBeTruthy();
    expect(screen.getByText('No damage')).toBeTruthy();
  });

  it('says a torpedo needed and missed, and a needle cut a system out', () => {
    render(<ShotResultCard result={result({ weaponKind: 'PulseTorpedo', diceRolls: [3], toHitNumber: 5, isHit: false })} ships={[target]} />);
    expect(screen.getByText('Needed 5+ to hit: missed.')).toBeTruthy();
    expect(screen.getByText('Missed')).toBeTruthy();
    cleanup();
    render(<ShotResultCard result={result({ weaponKind: 'NeedleBeam', diceRolls: [8], toHitNumber: 8, isHit: true })} ships={[target]} />);
    expect(screen.getByText('Needed 8 to hit: hit.')).toBeTruthy();
    expect(screen.getByText('System cut out')).toBeTruthy();
  });

  it('is the newest record from this ship', () => {
    const results = [result({ weaponName: 'First' }), result({ attackerShipId: 'other', weaponName: 'Theirs' }), result({ weaponName: 'Second' })];
    expect(lastShotBy('lance', results)?.weaponName).toBe('Second');
    expect(lastShotBy('nobody', results)).toBeUndefined();
  });
});

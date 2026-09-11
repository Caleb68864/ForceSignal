// @vitest-environment jsdom
/**
 * What the firing solution actually puts on screen.
 *
 * The server computes three numbers about the attacker and its target and sends them in every
 * firing solution. Two of them - `engagedTargetCount` and `targetScreens` - were declared on the
 * client's `FiringSolution` type and read by no code in either screen, so a player was shown the
 * fire control a ship has and never the fire control it has already spent. Since `PrepareShot`
 * refuses a shot once the engaged targets reach the working fire control, the only way to find out
 * was to pick a target and be refused.
 *
 * This renders both consoles through the same door a player uses - a solution arriving from the
 * fetcher, through the debounce - and asserts the numbers reach the screen. It fails on the state
 * before the wiring, where neither number was rendered anywhere.
 */

import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import type { FiringDraft, FiringSolution, Ship } from '../types.ts';
import { FiringConsole } from './ShipCard.tsx';
import { MapFiringAssistant } from './map/PlayMap.tsx';

afterEach(cleanup);

function ship(overrides: Partial<Ship> = {}): Ship {
  return {
    id: 'attacker',
    fleetId: 'fleet-one',
    name: 'Valiant',
    className: 'Cruiser',
    thrustRating: 4,
    currentVelocity: 8,
    currentCourse: 1,
    positionX: 10,
    positionY: 10,
    hullMax: 10,
    hullDamage: 0,
    armorMax: 0,
    armorDamage: 0,
    fireControlMax: 2,
    fireControlDamage: 0,
    pointDefenseSystems: 0,
    fighterBays: 0,
    fighterBayDamage: 0,
    damageControlParties: 0,
    driveDamage: 0,
    weaponDamage: 0,
    screenRating: 0,
    screenDamage: 0,
    weapons: [{ id: 'mount-one', name: 'Beam', attackDice: 2, maxRange: 24, arcs: ['Fore'], ammoMax: 0, ammoUsed: 0, reloadTurns: 0, kind: 'Beam' }],
    isDestroyed: false,
    iconKey: 'cruiser',
    fighterEnduranceMax: 0,
    fighterEnduranceUsed: 0,
    fighterMaxRange: 0,
    fighterStatus: 'Docked',
    pointsValue: 0,
    effectiveScreens: 0,
    workingFireControl: 2,
    fighterReach: 0,
    repairableSystems: [],
    ...overrides,
  };
}

/**
 * A solution of the shape the server really sends: two firecons, one of them already committed to
 * another target this turn, and a target still generating screens.
 */
const solution: FiringSolution = {
  attackerShipId: 'attacker',
  targetShipId: 'target',
  canFire: true,
  blocker: null,
  targetArc: 'Fore',
  mapRange: 12,
  rangeDisagreesWithMap: false,
  toHitNumber: null,
  workingFireControl: 2,
  engagedTargetCount: 1,
  targetScreens: 3,
  needleTargets: [],
};

const attacker = ship();
const target = ship({ id: 'target', name: 'Intruder', fleetId: 'fleet-two' });
const draft: FiringDraft = { targetShipId: 'target', weaponId: 'mount-one', range: 12 };
const answer = () => Promise.resolve(solution);

const shared = {
  ship: attacker,
  ships: [attacker, target],
  ownedShipIds: new Set([attacker.id]),
  draft,
  // Firing, because that is the phase in which a solution is fetched at all.
  phase: 'Firing' as const,
  firingResults: [],
  onChange: () => undefined,
  onFire: () => undefined,
  volleyOpen: false,
  snapshotVersion: 1,
  onFiringSolution: answer,
  canEndFire: false,
  onCeaseFire: () => undefined,
};

describe('the firing readout states what the solution carries', () => {
  it('says how much fire control is already committed, on the ship card', async () => {
    render(<FiringConsole {...shared} />);

    // Reached-the-subject: the solution really arrived and the readout really rendered from it.
    // Without this the two assertions below could both be passing over a console that never
    // fetched anything, which is how a probe in this repository's history read as three clean
    // passes while never touching its subject.
    await waitFor(() => expect(screen.getByText(/firecon/)).toBeTruthy());

    expect(screen.getByText('2 firecons, 1 engaged')).toBeTruthy();
    expect(screen.getByText('target screens 3')).toBeTruthy();
  });

  it('says the same on the map assistant, which is the other copy of this readout', async () => {
    render(<MapFiringAssistant {...shared} busy={false} />);

    await waitFor(() => expect(screen.getByText(/firecon/)).toBeTruthy());

    expect(screen.getByText('2 firecons, 1 engaged')).toBeTruthy();
    expect(screen.getByText('target screens 3')).toBeTruthy();
  });

  it('says a target is unscreened rather than saying nothing', async () => {
    render(<FiringConsole {...shared} onFiringSolution={() => Promise.resolve({ ...solution, targetScreens: 0, engagedTargetCount: 0 })} />);

    await waitFor(() => expect(screen.getByText(/firecon/)).toBeTruthy());

    // No engaged clause when nothing is engaged: a "0 engaged" on every first shot of a turn would
    // be noise, and the pair only means something once one of them is spent.
    expect(screen.getByText('2 firecons')).toBeTruthy();
    expect(screen.getByText('target unscreened')).toBeTruthy();
  });
});

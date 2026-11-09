/**
 * The next-step banner's words, phase by phase, from both seats at the table.
 */

import { describe, expect, it } from 'vitest';
import { nextStep } from './nextStep.ts';
import { blankRulesProfile, type MatchSnapshot, type OrderStatus, type Participant, type RulesProfile, type Ship } from '../types.ts';

const playable: RulesProfile = {
  ...blankRulesProfile,
  name: 'Invented',
  dieFaces: 8,
  beamDamage: [{ dieFace: 8, screenLevel: 0, damage: 1 }],
  beamRangeBandWidth: 10,
  thresholdRowCount: 3,
};

function player(id: string, name: string, patch: Partial<Participant> = {}): Participant {
  return { id, displayName: name, role: id === 'blue' ? 'Owner' : 'Player', isReady: false, isConnected: true, ordersComplete: false, ...patch };
}

function ship(id: string, fleetId: string, patch: Partial<Ship> = {}): Ship {
  return { id, fleetId, name: id, isDestroyed: false, ...patch } as Ship;
}

function table(patch: Partial<MatchSnapshot> = {}): MatchSnapshot {
  return {
    matchId: 'm', joinCode: 'CODE', name: 'Test', phase: 'FleetSetup', turnNumber: 1, rulesProfileKey: 'custom',
    tableWidth: 72, tableDepth: 48,
    participants: [player('blue', 'Blue'), player('red', 'Red')],
    fleets: [
      { id: 'fb', ownerParticipantId: 'blue', name: 'Blue fleet', fleetColor: '#3b82f6' },
      { id: 'fr', ownerParticipantId: 'red', name: 'Red fleet', fleetColor: '#ef4444' },
    ],
    ships: [ship('b1', 'fb'), ship('b2', 'fb'), ship('r1', 'fr')],
    orderStatuses: [], revealedOrders: [], movementResults: [], firingResults: [], ordnanceMarkers: [], matchLog: [],
    version: 1, pointsLimit: 0, rules: playable,
    ...patch,
  };
}

const status = (shipId: string, owner: string, patch: Partial<OrderStatus> = {}): OrderStatus => ({
  shipId, ownerParticipantId: owner, isCommitted: true, isRevealed: false, verificationFailed: false, ...patch,
});

describe('fleet setup', () => {
  it('sends the host to the rules profile first, and tells the guest who it is waiting on', () => {
    const snapshot = table({ rules: blankRulesProfile, ships: [] });
    expect(nextStep(snapshot, 'blue')).toMatchObject({ yourMove: true, you: expect.stringContaining('rules profile') });
    expect(nextStep(snapshot, 'red')).toMatchObject({ you: expect.stringContaining('Add your ships'), waiting: 'Waiting for Blue to set the rules profile.' });
  });

  it('asks for ships, then for Ready, then names who has not pressed it', () => {
    expect(nextStep(table({ ships: [] }), 'blue').you).toContain('Add your ships');
    expect(nextStep(table(), 'blue').you).toBe('You have 2 ships. When your fleet is complete, press Ready.');
    const ready = table({ participants: [player('blue', 'Blue', { isReady: true }), player('red', 'Red')] });
    expect(nextStep(ready, 'blue')).toEqual({ heading: 'Fleet setup', you: 'You are ready.', waiting: 'Waiting for Red to press Ready.', yourMove: false });
  });
});

describe('order entry', () => {
  it('counts only this player\'s ships, and how many are locked', () => {
    const snapshot = table({ phase: 'OrderEntry', orderStatuses: [status('b1', 'blue'), status('r1', 'red')] });
    expect(nextStep(snapshot, 'blue').you).toContain('Plot your 2 ships (1 locked)');
    expect(nextStep(snapshot, 'red').you).toContain('Plot your 1 ship (1 locked)');
  });

  it('leaves out a destroyed ship', () => {
    const snapshot = table({ phase: 'OrderEntry', ships: [ship('b1', 'fb'), ship('b2', 'fb', { isDestroyed: true }), ship('r1', 'fr')] });
    expect(nextStep(snapshot, 'blue').you).toContain('Plot your 1 ship');
  });

  it('says whose plotting the table is waiting on once yours is in', () => {
    const snapshot = table({ phase: 'OrderEntry', participants: [player('blue', 'Blue', { ordersComplete: true }), player('red', 'Red')] });
    expect(nextStep(snapshot, 'blue')).toMatchObject({ you: 'Your orders are in.', waiting: 'Waiting for Red to finish plotting.', yourMove: false });
  });
});

describe('reveal', () => {
  it('asks the player with sealed orders to reveal, and the other to wait for them', () => {
    const snapshot = table({ phase: 'OrdersLocked', orderStatuses: [status('b1', 'blue', { isRevealed: true }), status('r1', 'red')] });
    expect(nextStep(snapshot, 'red')).toMatchObject({ you: 'Everyone has locked. Press Reveal Fleet Orders.', yourMove: true });
    expect(nextStep(snapshot, 'blue')).toMatchObject({ waiting: 'Waiting for Red to reveal.', yourMove: false });
  });
});

describe('movement', () => {
  it('puts Advance Turn in the host\'s hands and tells the guest so', () => {
    const snapshot = table({ phase: 'Movement' });
    expect(nextStep(snapshot, 'blue')).toMatchObject({ you: expect.stringContaining('Press Advance Turn'), yourMove: true });
    expect(nextStep(snapshot, 'red')).toMatchObject({ waiting: 'Waiting for Blue to press Advance Turn and move the ships.', yourMove: false });
  });
});

describe('firing', () => {
  it('tells the player with the initiative to fire, and the other who is firing', () => {
    const snapshot = table({ phase: 'Firing', firingParticipantId: 'red' });
    expect(nextStep(snapshot, 'red')).toMatchObject({ you: expect.stringContaining('Your turn to fire'), yourMove: true });
    expect(nextStep(snapshot, 'blue')).toMatchObject({ waiting: 'Waiting for Red to fire a ship.', yourMove: false });
  });

  it('names the ship whose volley is still open', () => {
    const snapshot = table({ phase: 'Firing', firingParticipantId: 'blue', firingShipId: 'b2' });
    expect(nextStep(snapshot, 'blue').you).toBe('b2 is firing. Fire its other weapons, then press Done Firing to hand over.');
  });

  it('hands the next turn to the host once every ship has fired', () => {
    const snapshot = table({ phase: 'Firing', firingParticipantId: null, turnNumber: 3 });
    expect(nextStep(snapshot, 'blue').you).toBe('Every ship has fired. Press Advance Turn to start turn 4.');
    expect(nextStep(snapshot, 'red').waiting).toBe('Waiting for Blue to press Advance Turn.');
  });
});

describe('every phase', () => {
  it('always tells each seat something to do or someone to wait for', () => {
    for (const phase of ['FleetSetup', 'OrderEntry', 'OrdersLocked', 'Reveal', 'Movement', 'Firing']) {
      for (const seat of ['blue', 'red']) {
        const step = nextStep(table({ phase, firingParticipantId: 'red' }), seat);
        expect(step.you || step.waiting, `${phase} as ${seat}`).toBeTruthy();
        expect(step.yourMove || step.waiting !== null || step.you.length > 0).toBe(true);
      }
    }
  });
});

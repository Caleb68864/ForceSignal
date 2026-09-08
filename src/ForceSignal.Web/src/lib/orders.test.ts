import { describe, expect, it } from 'vitest';
import type { OrderStatus } from '../types.ts';
import { fleetCopyTargets, isOrderLocked } from './orders.ts';

function status(shipId: string, isCommitted: boolean, isRevealed = false): OrderStatus {
  return { shipId, ownerParticipantId: 'p1', isCommitted, isRevealed, verificationFailed: false };
}

const ship = (id: string, isDestroyed = false) => ({ id, isDestroyed });

describe('isOrderLocked', () => {
  it('is locked between the lock and the reveal, which is the window that matters', () => {
    expect(isOrderLocked([status('a', true)], 'a')).toBe(true);
  });

  it('is not locked before the order is committed', () => {
    expect(isOrderLocked([status('a', false)], 'a')).toBe(false);
  });

  it('is not locked once the order has been revealed', () => {
    // The plaintext is on the table by then; there is nothing left to destroy.
    expect(isOrderLocked([status('a', true, true)], 'a')).toBe(false);
  });

  it('treats a ship with no status, and a missing snapshot, as unlocked', () => {
    expect(isOrderLocked([status('a', true)], 'b')).toBe(false);
    expect(isOrderLocked(undefined, 'a')).toBe(false);
  });
});

describe('fleetCopyTargets', () => {
  it('leaves a locked ship out and reports it, rather than overwriting its plaintext', () => {
    const ships = [ship('a'), ship('b'), ship('c')];
    const statuses = [status('b', true)];

    const { eligible, lockedOut } = fleetCopyTargets(ships, statuses);

    expect(eligible.map((s) => s.id)).toEqual(['a', 'c']);
    expect(lockedOut.map((s) => s.id)).toEqual(['b']);
  });

  it('still skips destroyed ships, and does not count them as locked out', () => {
    const ships = [ship('a'), ship('dead', true)];

    const { eligible, lockedOut } = fleetCopyTargets(ships, []);

    expect(eligible.map((s) => s.id)).toEqual(['a']);
    expect(lockedOut).toEqual([]);
  });

  it('reports nothing eligible when the whole fleet is locked', () => {
    // One tap would otherwise have invalidated every commitment in the squadron.
    const ships = [ship('a'), ship('b')];
    const statuses = [status('a', true), status('b', true)];

    const { eligible, lockedOut } = fleetCopyTargets(ships, statuses);

    expect(eligible).toEqual([]);
    expect(lockedOut.map((s) => s.id)).toEqual(['a', 'b']);
  });

  it('lets a revealed fleet be copied to again', () => {
    const ships = [ship('a'), ship('b')];
    const statuses = [status('a', true, true), status('b', true, true)];

    expect(fleetCopyTargets(ships, statuses).eligible).toHaveLength(2);
  });
});

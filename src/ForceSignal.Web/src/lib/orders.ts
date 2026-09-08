/**
 * What may still be changed about an order, and what has been promised away.
 *
 * A locked order lives in two places that are not the same place. The server holds a hash of it
 * and, by design, nothing else -- that is what makes the reveal worth anything. The plaintext
 * exists only in the local draft on the device that wrote it. So an edit to a draft between the
 * lock and the reveal does not change the order; it destroys the only copy of what the hash was
 * taken of. `Verify` then fails, and once anyone else has revealed, the server discards the order
 * and the ship holds course and speed. The player is not told they lost a manoeuvre, because from
 * the app's point of view nothing went wrong.
 *
 * These predicates are here rather than inline in the screen so they can be tested without a DOM,
 * and so the two callers that need the rule cannot drift apart on what "locked" means.
 */
import type { OrderStatus } from '../types.ts';

/**
 * Whether this ship's order is locked and not yet revealed -- the window in which the local draft
 * is the only copy of the plaintext.
 *
 * `isCommitted && !isRevealed` is the same test the screen already uses to decide which drafts
 * survive a turn rollover, and it is deliberately the same one here.
 */
export function isOrderLocked(orderStatuses: readonly OrderStatus[] | undefined, shipId: string): boolean {
  const status = orderStatuses?.find((entry) => entry.shipId === shipId);
  return Boolean(status?.isCommitted && !status.isRevealed);
}

/**
 * Splits a fleet into the ships that can take a copied order and the ships that must be left alone.
 *
 * A destroyed ship takes no order; giving it one only inflates the "holds course" count. A ship
 * holding a locked order is excluded for the sharper reason above -- and "Copy Fleet" applies to
 * every owned ship at once, so a single tap could do that to a whole squadron.
 */
export function fleetCopyTargets<T extends { id: string; isDestroyed: boolean }>(
  ships: readonly T[],
  orderStatuses: readonly OrderStatus[] | undefined,
): { eligible: T[]; lockedOut: T[] } {
  const alive = ships.filter((ship) => !ship.isDestroyed);
  return {
    eligible: alive.filter((ship) => !isOrderLocked(orderStatuses, ship.id)),
    lockedOut: alive.filter((ship) => isOrderLocked(orderStatuses, ship.id)),
  };
}

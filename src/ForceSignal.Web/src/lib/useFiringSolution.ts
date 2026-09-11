/**
 * Asking the server whether a shot can be taken.
 *
 * The firing rules live on the server and are not simple - ammunition, mounts already fired,
 * fighter endurance, the fire control a needle beam claims for itself - so the console asks rather
 * than guessing. Guessing is what it used to do, and it guessed with only some of the rules.
 */

import { useEffect, useRef, useState } from 'react';
import type { FiringSolution, Ship } from '../types.ts';

/** How long the range box has to stop changing before it is worth asking about. */
const firingSolutionDebounceMs = 120;

/**
 * The server's answer for the shot currently drafted, or null when there is nothing to ask about.
 *
 * Keyed on the snapshot version as well as the draft, because a blocker depends on the match as
 * much as on the draft: another ship firing, a mount knocked out, the turn passing to the other
 * player. Any change to the match re-asks.
 *
 * Every firing console on screen runs one of these, so in the firing phase a fleet of ten asks ten
 * questions per change. They are small reads and the answers must be per-ship, but a batch endpoint
 * is the obvious move if a big table ever feels it.
 */
export function useFiringSolution(
  ship: Ship | undefined,
  targetShipId: string | undefined,
  weaponId: string | undefined,
  range: number,
  snapshotVersion: number,
  enabled: boolean,
  request: (ship: Ship, targetShipId?: string, weaponId?: string, range?: number) => Promise<FiringSolution>,
  delayMs: number = firingSolutionDebounceMs,
): FiringSolution | null {
  const [solution, setSolution] = useState<FiringSolution | null>(null);

  const shipRef = useRef(ship);
  const requestRef = useRef(request);
  shipRef.current = ship;
  requestRef.current = request;

  const key = ship && enabled
    ? JSON.stringify({ v: snapshotVersion, a: ship.id, t: targetShipId ?? null, w: weaponId ?? null, r: range })
    : null;

  useEffect(() => {
    if (key === null) {
      setSolution(null);
      return;
    }

    let cancelled = false;
    const timer = window.setTimeout(() => {
      const currentShip = shipRef.current;
      if (!currentShip) {
        return;
      }

      requestRef.current(currentShip, targetShipId, weaponId, range)
        .then((next) => {
          if (!cancelled) {
            setSolution(next);
          }
        })
        .catch(() => {
          // An unanswered question is not a reason to block the console: the server still refuses
          // what it must when Fire is pressed.
          if (!cancelled) {
            setSolution(null);
          }
        });
    }, delayMs);

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
    // targetShipId/weaponId/range are inside `key`; listing them again would re-fire on identity.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, delayMs]);

  return solution;
}

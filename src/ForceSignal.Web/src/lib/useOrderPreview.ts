/**
 * Asking the server where a draft order lands, without asking on every keystroke.
 *
 * The movement rules live on the server, so a preview is a round trip rather than a calculation.
 * That is fine at the speed a person plots a course, but not at the speed a pointer moves, so the
 * request is held back until the draft stops changing.
 */

import { useEffect, useRef, useState } from 'react';
import { toOrder } from './movement.ts';
import type { DraftOrder, OrderPreview, Ship } from '../types.ts';

/** How long a draft has to stop changing before it is worth asking about. */
const previewDebounceMs = 80;

/**
 * The server's answer for the current draft, or null when there is nothing to preview.
 *
 * The previous answer stays on screen while a new one is in flight. A preview that blanked between
 * every edit would flicker the course line off the map exactly while the player is watching it.
 */
export function useOrderPreview(
  ship: Ship | undefined,
  draft: DraftOrder | undefined,
  request: (ship: Ship, draft: DraftOrder) => Promise<OrderPreview>,
  delayMs: number = previewDebounceMs,
): OrderPreview | null {
  const [preview, setPreview] = useState<OrderPreview | null>(null);

  // Held in refs so the effect depends on what was asked, not on the identity of the objects and
  // callbacks a re-render happens to hand us. Without this, every parent render refires the request.
  const shipRef = useRef(ship);
  const draftRef = useRef(draft);
  const requestRef = useRef(request);
  shipRef.current = ship;
  draftRef.current = draft;
  requestRef.current = request;

  // Everything the answer depends on, and nothing else. The salt changes per draft and cannot
  // change the answer, so it is deliberately left out.
  const key = ship && draft
    ? JSON.stringify({
      shipId: ship.id,
      velocity: ship.currentVelocity,
      course: ship.currentCourse,
      x: ship.positionX,
      y: ship.positionY,
      thrust: ship.thrustRating,
      driveDamage: ship.driveDamage,
      order: toOrder(draft),
    })
    : null;

  useEffect(() => {
    if (key === null) {
      setPreview(null);
      return;
    }

    let cancelled = false;
    const timer = window.setTimeout(() => {
      const currentShip = shipRef.current;
      const currentDraft = draftRef.current;
      if (!currentShip || !currentDraft) {
        return;
      }

      requestRef.current(currentShip, currentDraft)
        .then((next) => {
          if (!cancelled) {
            setPreview(next);
          }
        })
        .catch(() => {
          // A preview that cannot be fetched is not worth interrupting a plot over: the order can
          // still be locked, and the server will say no then if it has to.
          if (!cancelled) {
            setPreview(null);
          }
        });
    }, delayMs);

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [key, delayMs]);

  return preview;
}

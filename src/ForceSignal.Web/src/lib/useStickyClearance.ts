import { useEffect, useRef } from 'react';

/**
 * Keeps whatever is scrolled into view clear of a sticky bar at the top of the page.
 *
 * The "What to do next" line is sticky, so a control brought into view - by keyboard focus, by a
 * browser's find, by a test's click - could land wholly on screen and still under the bar: CI saw
 * Playwright retry a click thirty times while the bar intercepted it. The page's scroll padding is
 * set to the bar's height as it changes (its text wraps differently on every screen), and cleared
 * when the bar goes.
 */
export function useStickyClearance<T extends HTMLElement>() {
  const ref = useRef<T | null>(null);
  useEffect(() => {
    const bar = ref.current;
    const root = document.documentElement;
    if (!bar) {
      return undefined;
    }

    const apply = () => root.style.setProperty('--sticky-clearance', `${Math.ceil(bar.getBoundingClientRect().height)}px`);
    apply();
    const observer = typeof ResizeObserver === 'function' ? new ResizeObserver(apply) : null;
    observer?.observe(bar);
    return () => {
      observer?.disconnect();
      root.style.removeProperty('--sticky-clearance');
    };
  }, []);

  return ref;
}

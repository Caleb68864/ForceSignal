// @vitest-environment jsdom
/**
 * Panning moves the table without re-rendering the map on every pointer move.
 *
 * Each move used to set state, and every ship button, overlay and the inspector rendered again -
 * twenty times for a short drag. On a phone with a full table that was the stutter the timing
 * harness measured (docs/visuals/findings-2026-11-09.md, A3). The drag now writes the transform
 * straight to the table and commits it once, on release.
 */

import { act, cleanup, fireEvent, render } from '@testing-library/react';
import { Profiler } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import firingComplete from '../../lib/playback/fixtures/05-turn1-firing-complete.json' with { type: 'json' };
import { normalizeMatchSnapshot } from '../../lib/normalize.ts';
import { PlayMap } from './PlayMap.tsx';
import type { FiringSolution, MatchSnapshot, OrderPreview } from '../../types.ts';

afterEach(cleanup);

// jsdom has no pointer capture; the map asks for it on every press.
Object.assign(Element.prototype, {
  hasPointerCapture: () => false,
  setPointerCapture: () => undefined,
  releasePointerCapture: () => undefined,
});

const snapshot = normalizeMatchSnapshot(structuredClone(firingComplete) as unknown as MatchSnapshot);
const noop = () => undefined;

function renderMap(onCommit: () => void) {
  return render(
      <Profiler id="map" onRender={onCommit}>
        <PlayMap
          snapshot={snapshot}
          ownedShipIds={new Set()}
          drafts={{}}
          firingDrafts={{}}
          phase={snapshot.phase}
          focusedShipId={null}
          onFocus={noop}
          onDraftChange={noop}
          onFiringDraftChange={noop}
          busy={false}
          onFighterOps={noop}
          onCreateOrdnance={noop}
          onUpdateOrdnance={noop}
          onRemoveOrdnance={noop}
          onPreviewOrder={() => Promise.resolve({} as OrderPreview)}
          onFiringSolution={() => Promise.resolve({} as FiringSolution)}
          onFire={noop}
          onCeaseFire={noop}
          onFlyFighters={noop}
        />
      </Profiler>,
  );
}

describe('dragging the table', () => {
  it('moves it with no render per move, and commits where it ended', () => {
    let commits = 0;
    const { container } = renderMap(() => { commits += 1; });
    const map = container.querySelector<HTMLElement>('.table-map');
    const content = container.querySelector<HTMLElement>('.table-map-content');
    expect(map && content).toBeTruthy();
    if (!map || !content) {
      return;
    }

    fireEvent.pointerDown(map, { pointerId: 1, button: 0, clientX: 100, clientY: 100 });
    const afterDown = commits;
    for (let step = 1; step <= 20; step += 1) {
      fireEvent.pointerMove(map, { pointerId: 1, clientX: 100 + step * 5, clientY: 100 + step * 2 });
    }

    // The table really moved...
    expect(content.style.transform).toBe('translate(100px, 40px) scale(1)');
    // ...with at most the one render that marks the drag as started, not one per move.
    expect(commits - afterDown).toBeLessThanOrEqual(1);

    fireEvent.pointerUp(map, { pointerId: 1, clientX: 200, clientY: 140 });
    expect(content.style.transform).toBe('translate(100px, 40px) scale(1)');
    // And the next render keeps it there: the drag's end is now the viewport.
    fireEvent.keyDown(map, { key: '+' });
    expect(content.style.transform).toMatch(/^translate\(100px, 40px\) scale\(1\.15/);
  });
});

describe('scrolling to zoom', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('zooms at once, and renders the map once when the scrolling stops', () => {
    vi.useFakeTimers();
    let commits = 0;
    const { container } = renderMap(() => { commits += 1; });
    const map = container.querySelector<HTMLElement>('.table-map');
    const content = container.querySelector<HTMLElement>('.table-map-content');
    if (!map || !content) {
      throw new Error('the map did not render');
    }

    const before = commits;
    for (let notch = 0; notch < 10; notch += 1) {
      map.dispatchEvent(new WheelEvent('wheel', { deltaY: -100, clientX: 50, clientY: 50, bubbles: true, cancelable: true, composed: true }));
    }

    // Zoomed in, live, with the markers kept to size...
    const scale = Number(/scale\(([\d.]+)\)/.exec(content.style.transform)?.[1]);
    expect(scale).toBeCloseTo(2.5, 6);
    expect(Number(content.style.getPropertyValue('--marker-scale'))).toBeCloseTo(1 / 2.5, 6);
    // ...and no render for any of the ten notches.
    expect(commits - before).toBe(0);

    act(() => {
      vi.advanceTimersByTime(200);
    });
    expect(commits - before).toBeGreaterThanOrEqual(1);
    expect(commits - before).toBeLessThanOrEqual(2);
    expect(container.textContent).toContain('Zoom 250%');
  });
});

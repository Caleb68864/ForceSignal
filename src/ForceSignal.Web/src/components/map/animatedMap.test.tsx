// @vitest-environment jsdom
/**
 * The animated map is a per-device choice, and a device that cannot run it gets the classic map.
 *
 * jsdom has no WebGL, which makes it exactly the device the fallback is for: the canvas tries to
 * start, cannot, and the map has to go back to drawing its hulls in the DOM and say why.
 */

import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import firingComplete from '../../lib/playback/fixtures/05-turn1-firing-complete.json' with { type: 'json' };
import { normalizeMatchSnapshot } from '../../lib/normalize.ts';
import { PlayMap } from './PlayMap.tsx';
import type { FiringSolution, MatchSnapshot, OrderPreview } from '../../types.ts';

const snapshot = normalizeMatchSnapshot(structuredClone(firingComplete) as unknown as MatchSnapshot);
const noop = () => undefined;

function renderMap() {
  return render(
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
    />,
  );
}

describe('the animated map setting', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.spyOn(console, 'error').mockImplementation(noop);
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('is off until this device turns it on, and remembers the choice', () => {
    const { container } = renderMap();
    expect(container.querySelector('.table-map')?.classList.contains('pixi-renderer')).toBe(false);

    const toggle = screen.getByRole('checkbox', { name: 'Animated map' });
    expect((toggle as HTMLInputElement).checked).toBe(false);
    act(() => {
      fireEvent.click(toggle);
    });
    expect(localStorage.getItem('forcesignal.animatedMap')).toBe('on');
  });

  it('falls back to the classic map, and says so, on a device that cannot run it', async () => {
    localStorage.setItem('forcesignal.animatedMap', 'on');
    const { container } = renderMap();

    // Reached-the-subject: the map really did start in animated mode.
    expect(container.querySelector('.table-map')?.classList.contains('pixi-renderer')).toBe(true);

    await waitFor(() => expect(container.querySelector('.table-map')?.classList.contains('pixi-renderer')).toBe(false), { timeout: 10_000 });
    expect(screen.getByText(/animated map cannot run on this device/i)).toBeTruthy();
    // The classic map is drawing the hulls again, trails and all.
    expect(container.querySelectorAll('.map-ship')).toHaveLength(snapshot.ships.length);
    expect(container.querySelector('.resolved-trail-overlay')).not.toBeNull();
  }, 15_000);
});

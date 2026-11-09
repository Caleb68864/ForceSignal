// @vitest-environment jsdom
/**
 * The canvas puts a ship exactly where the DOM map puts its button.
 *
 * Three ways, because each catches a different slip. A grid of table points through a real Pixi
 * container moved by `applyViewport`, against the CSS rule written out longhand. The same points run
 * back through `tablePointFromClient`, the DOM map's own inverse, which is what a tap goes through.
 * And the real `PlayMap`, rendered and zoomed and panned with the keys a player uses, with its ship
 * buttons' `left`/`top` and its content transform read back off the elements and compared to where
 * the canvas would draw each hull.
 */

import { act, cleanup, fireEvent, render } from '@testing-library/react';
import { Container } from 'pixi.js';
import { afterEach, describe, expect, it } from 'vitest';
import firingComplete from './fixtures/05-turn1-firing-complete.json' with { type: 'json' };
import { PlayMap } from '../../components/map/PlayMap.tsx';
import { tablePointFromClient } from '../mapGeometry.ts';
import { normalizeMatchSnapshot } from '../normalize.ts';
import { applyViewport, markerScale, tableToContent, type MapViewport } from './viewTransform.ts';
import type { FiringSolution, MatchSnapshot, OrderPreview, TablePoint } from '../../types.ts';

afterEach(cleanup);

const table = { tableWidth: 72, tableDepth: 48 };
const content = { width: 900, height: 600 };
const viewports: MapViewport[] = [
  { scale: 1, x: 0, y: 0 },
  { scale: 0.75, x: 40, y: -12 },
  { scale: 2.5, x: -830, y: -410 },
  { scale: 4, x: -1900, y: 300 },
];
const grid: TablePoint[] = [];
for (let x = 0; x <= 72; x += 9) {
  for (let y = 0; y <= 48; y += 8) {
    grid.push({ x, y });
  }
}

/** Where the canvas draws a point: a child of a world container, read back through Pixi itself. */
function pixiScreen(point: TablePoint, viewport: MapViewport, size = content, size2 = table): TablePoint {
  const world = new Container();
  const marker = new Container();
  world.addChild(marker);
  applyViewport(world, viewport);
  const local = tableToContent(point, size2, size);
  marker.position.set(local.x, local.y);
  const global = marker.toGlobal({ x: 0, y: 0 });
  return { x: global.x, y: global.y };
}

/** The CSS, longhand: `left: P%` of the content box, then `translate(x, y) scale(s)` from `0 0`. */
function cssScreen(leftPercent: number, topPercent: number, viewport: MapViewport, size = content): TablePoint {
  return {
    x: viewport.x + viewport.scale * (leftPercent / 100) * size.width,
    y: viewport.y + viewport.scale * (topPercent / 100) * size.height,
  };
}

describe('the canvas and the DOM map agree on where a table point is', () => {
  it('for a grid of points at every zoom and pan', () => {
    expect(grid.length).toBeGreaterThan(40);
    for (const viewport of viewports) {
      for (const point of grid) {
        const css = cssScreen((point.x / table.tableWidth) * 100, (point.y / table.tableDepth) * 100, viewport);
        const pixi = pixiScreen(point, viewport);
        expect(pixi.x).toBeCloseTo(css.x, 6);
        expect(pixi.y).toBeCloseTo(css.y, 6);
      }
    }
  });

  it("and a tap there comes back through the DOM map's own inverse as the same point", () => {
    // The rect is the content box. The real `.table-map` has a 1px border that
    // tablePointFromClient counts and the content box does not - see findings, "coordinates".
    const element = { getBoundingClientRect: () => ({ left: 30, top: 70, width: content.width, height: content.height }) } as HTMLElement;
    for (const viewport of viewports) {
      for (const point of grid) {
        const screen = pixiScreen(point, viewport);
        const back = tablePointFromClient(element, screen.x + 30, screen.y + 70, viewport, table.tableWidth, table.tableDepth);
        expect(back.x).toBeCloseTo(point.x, 6);
        expect(back.y).toBeCloseTo(point.y, 6);
      }
    }
  });

  it('keeps markers the same size on screen, as --marker-scale does', () => {
    for (const viewport of viewports) {
      expect(markerScale(viewport) * viewport.scale).toBeCloseTo(1, 9);
    }
  });
});

describe('against the rendered PlayMap', () => {
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

  function readViewport(contentBox: HTMLElement): MapViewport {
    const match = /translate\((-?[\d.]+)px, (-?[\d.]+)px\) scale\((-?[\d.]+)\)/.exec(contentBox.style.transform);
    expect(match, contentBox.style.transform).not.toBeNull();
    return { x: Number(match?.[1]), y: Number(match?.[2]), scale: Number(match?.[3]) };
  }

  it('puts every hull where its button is, through zooms and pans a player makes', () => {
    const { container } = renderMap();
    const map = container.querySelector<HTMLElement>('.table-map');
    const contentBox = container.querySelector<HTMLElement>('.table-map-content');
    expect(map).not.toBeNull();
    expect(contentBox).not.toBeNull();
    if (!map || !contentBox) {
      return;
    }

    const keys = ['+', '+', 'ArrowLeft', 'ArrowLeft', 'ArrowUp', '+', 'ArrowRight', '-', 'ArrowDown', 'ArrowDown'];
    const states: MapViewport[] = [readViewport(contentBox)];
    for (const key of keys) {
      act(() => {
        fireEvent.keyDown(map, { key });
      });
      states.push(readViewport(contentBox));
    }

    // Reached-the-subject: the keys really moved the map, both ways.
    expect(new Set(states.map((state) => state.scale)).size).toBeGreaterThan(2);
    expect(new Set(states.map((state) => state.x)).size).toBeGreaterThan(2);

    // Read a final time at the last state, with every ship.
    const buttons = [...container.querySelectorAll<HTMLElement>('.map-ship')];
    expect(buttons).toHaveLength(snapshot.ships.length);
    const viewport = readViewport(contentBox);
    buttons.forEach((button, index) => {
      const ship = snapshot.ships[index];
      const css = cssScreen(parseFloat(button.style.left), parseFloat(button.style.top), viewport);
      const pixi = pixiScreen({ x: ship.positionX, y: ship.positionY }, viewport);
      expect(pixi.x, ship.name).toBeCloseTo(css.x, 6);
      expect(pixi.y, ship.name).toBeCloseTo(css.y, 6);
    });

    // And at each intermediate state, for the first and last hull - the map reads one viewport
    // for all of them, so two is enough to catch an axis swapped or a pan applied twice.
    for (const state of states) {
      for (const index of [0, snapshot.ships.length - 1]) {
        const ship = snapshot.ships[index];
        const css = cssScreen(parseFloat(buttons[index].style.left), parseFloat(buttons[index].style.top), state);
        const pixi = pixiScreen({ x: ship.positionX, y: ship.positionY }, state);
        expect(pixi.x).toBeCloseTo(css.x, 6);
        expect(pixi.y).toBeCloseTo(css.y, 6);
      }
    }
  });
});

/**
 * Where a table point lands on screen, for any renderer that draws the map.
 *
 * The DOM map places a marker at `left: mapPercent(x)%` of the content box and then moves the whole
 * content box with `translate(x, y) scale(s)` from a `0 0` origin (`.table-map-content` in
 * style.css). A second renderer that worked this out its own way would put a ship a few pixels off
 * the button the player is about to tap, so both go through here.
 *
 * Nothing here imports a renderer. `applyViewport` takes anything with a settable position and
 * scale, which a Pixi container is, so the function the canvas runs is the one these tests run.
 */

import { mapPercent } from '../geometry.ts';
import type { TablePoint } from '../../types.ts';

export type MapViewport = { scale: number; x: number; y: number };
export type TableSize = { tableWidth: number; tableDepth: number };
export type ContentSize = { width: number; height: number };

/** A table point in the content box's own pixels, before the viewport moves it. */
export function tableToContent(point: TablePoint, table: TableSize, content: ContentSize): TablePoint {
  return {
    x: (mapPercent(point.x, table.tableWidth) / 100) * content.width,
    y: (mapPercent(point.y, table.tableDepth) / 100) * content.height,
  };
}

/** A content-box point after the viewport's translate and scale, relative to the map's top left. */
export function contentToScreen(point: TablePoint, viewport: MapViewport): TablePoint {
  return { x: viewport.x + point.x * viewport.scale, y: viewport.y + point.y * viewport.scale };
}

export function tableToScreen(point: TablePoint, table: TableSize, content: ContentSize, viewport: MapViewport): TablePoint {
  return contentToScreen(tableToContent(point, table, content), viewport);
}

/** Moves a scene-graph node the way `.table-map-content` is moved. */
export function applyViewport(
  node: { position: { set: (x: number, y: number) => void }; scale: { set: (x: number, y: number) => void } },
  viewport: MapViewport,
): void {
  node.position.set(viewport.x, viewport.y);
  node.scale.set(viewport.scale, viewport.scale);
}

/**
 * The scale a marker carries so it stays the same size on screen at every zoom, as `--marker-scale`
 * does for the DOM's ship buttons.
 */
export function markerScale(viewport: MapViewport): number {
  return 1 / viewport.scale;
}

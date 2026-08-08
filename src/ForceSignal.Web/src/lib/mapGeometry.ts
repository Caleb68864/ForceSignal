/**
 * Converting between where a finger touched the screen and where that is on the table.
 *
 * The map is pannable and zoomable, so a click has to be run back through the current viewport
 * transform before it means anything in table units.
 */

import { wrapCourse } from './geometry.ts';
import type { MovementResult, Ship, TablePoint } from '../types.ts';

export function tablePointFromClient(
  element: HTMLElement,
  clientX: number,
  clientY: number,
  viewport: { scale: number; x: number; y: number },
  tableWidth: number,
  tableDepth: number,
) {
  const rect = element.getBoundingClientRect();
  const localX = (clientX - rect.left - viewport.x) / viewport.scale;
  const localY = (clientY - rect.top - viewport.y) / viewport.scale;
  return {
    x: Math.max(0, Math.min(tableWidth, localX / rect.width * tableWidth)),
    y: Math.max(0, Math.min(tableDepth, localY / rect.height * tableDepth)),
  };
}
export function courseFromTablePoint(ship: Ship, targetX: number, targetY: number) {
  const dx = targetX - ship.positionX;
  const dy = targetY - ship.positionY;
  if (Math.abs(dx) < 0.01 && Math.abs(dy) < 0.01) {
    return ship.currentCourse;
  }

  const radians = Math.atan2(dx, -dy);
  const degrees = (radians * 180 / Math.PI + 360) % 360;
  return wrapCourse(Math.round(degrees / 30) || 12);
}
export function measureDistance(line: { start: TablePoint; end: TablePoint }) {
  return Math.hypot(line.end.x - line.start.x, line.end.y - line.start.y);
}
export function measureCourse(line: { start: TablePoint; end: TablePoint }) {
  const dx = line.end.x - line.start.x;
  const dy = line.end.y - line.start.y;
  if (Math.abs(dx) < 0.01 && Math.abs(dy) < 0.01) {
    return 12;
  }

  const radians = Math.atan2(dx, -dy);
  const degrees = (radians * 180 / Math.PI + 360) % 360;
  return wrapCourse(Math.round(degrees / 30) || 12);
}
export function trailPointsForResult(ship: Ship, result: MovementResult): TablePoint[] {
  const end: TablePoint = { x: ship.positionX, y: ship.positionY };
  const segments = result.segments?.length ? result.segments : [{ course: result.endingCourse, distance: result.endingVelocity }];
  const reversed: TablePoint[] = [end];
  let cursor = end;

  for (const segment of [...segments].reverse()) {
    const radians = segment.course * Math.PI / 6;
    cursor = {
      x: cursor.x - Math.sin(radians) * segment.distance,
      y: cursor.y + Math.cos(radians) * segment.distance,
    };
    reversed.unshift(cursor);
  }

  return reversed;
}
/// Zooms to a scale while keeping the table point under (localX, localY) anchored.
export function viewportZoomedAt(
  current: { scale: number; x: number; y: number },
  localX: number,
  localY: number,
  requestedScale: number,
) {
  const nextScale = Math.max(0.75, Math.min(4, requestedScale));
  const mapX = (localX - current.x) / current.scale;
  const mapY = (localY - current.y) / current.scale;
  return clampMapViewport({
    scale: nextScale,
    x: localX - mapX * nextScale,
    y: localY - mapY * nextScale,
  });
}
export function clampMapViewport(viewport: { scale: number; x: number; y: number }) {
  const scale = Math.max(0.75, Math.min(4, viewport.scale));
  const panLimit = 1200 * scale;
  return {
    scale,
    x: Math.max(-panLimit, Math.min(panLimit, viewport.x)),
    y: Math.max(-panLimit, Math.min(panLimit, viewport.y)),
  };
}

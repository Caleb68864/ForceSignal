/**
 * The table as geometry: courses on the twelve-point clock, distances between ships, and the
 * conversions between table units and the fractions the map is drawn in.
 */

import { firingArcs } from '../constants.ts';
import type { FiringArc, Ship } from '../types.ts';

export function wrapCourse(course: number) {
  const zeroBased = ((course - 1) % 12 + 12) % 12;
  return zeroBased + 1;
}
export function courseAngle(course: number) {
  return course * 30;
}
export function mapPercent(value: number, max: number) {
  return Math.max(0, Math.min(100, (value / Math.max(1, max)) * 100));
}
export function distanceBetweenShips(source: Ship, target: Ship) {
  return Math.hypot(target.positionX - source.positionX, target.positionY - source.positionY);
}
export function courseFromPoint(element: HTMLElement, clientX: number, clientY: number) {
  const rect = element.getBoundingClientRect();
  const centerX = rect.left + rect.width / 2;
  const centerY = rect.top + rect.height / 2;
  const radians = Math.atan2(clientX - centerX, centerY - clientY);
  const degrees = (radians * 180 / Math.PI + 360) % 360;
  return wrapCourse(Math.round(degrees / 30) || 12);
}
export function rangeDiameterPercent(range: number, tableSize: number) {
  return Math.max(4, Math.min(240, (range * 2 / Math.max(1, tableSize)) * 100));
}
/// Screen angle of an arc's centreline: each arc sits two clock points from the last.
export function weaponArcAngle(course: number, arc: FiringArc) {
  const offset = Math.max(0, firingArcs.indexOf(arc)) * 2;
  return courseAngle(wrapCourse(course + offset));
}

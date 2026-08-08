/**
 * Small sums over a fleet: what it is worth, and what to call the next copy of a ship.
 */

import { normalizeShipIconKey } from './normalize.ts';
import type { FleetExport, FleetExportShip, Ship } from '../types.ts';

/// Total NPV of a fleet export, the figure a points-limited match is measured against.
export function fleetPoints(fleet: FleetExport) {
  return fleet.ships.reduce((sum, ship) => sum + (ship.pointsValue ?? 0), 0);
}
export function shipsPoints(ships: Ship[]) {
  return ships.reduce((sum, ship) => sum + (ship.pointsValue ?? 0), 0);
}
export function carrierImportRank(ship: FleetExportShip) {
  return normalizeShipIconKey(ship.iconKey, ship.className) === 'carrier' ? 0 : 1;
}
export function nextShipName(name: string) {
  const match = name.match(/^(.*?)(\d+)$/);
  if (!match) {
    return `${name} 2`;
  }

  return `${match[1]}${Number(match[2]) + 1}`;
}

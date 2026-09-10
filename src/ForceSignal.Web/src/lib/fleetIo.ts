/**
 * Moving a fleet in and out of a file the player owns.
 *
 * Import is the untrusted path: the file was picked by hand and may have been written by an older
 * version, edited in a spreadsheet, or not be a fleet at all. Every value is coerced into range,
 * and a row that cannot be read costs that value rather than failing the whole import.
 *
 * What it does *not* do is fill a gap in with a number. Text still falls back - a ship with no name
 * is easier to find as "Valiant" than as an empty cell - but a missing hull, armour, thrust or
 * screen column reads back unentered, because the file is the only place those could have come
 * from and this app does not own them. See the README's Content Policy, and `contentPolicy.test.ts`,
 * which fails if any of them acquires a value again.
 */

import { csvEscape, normalizeHeader, numberFrom, stringFrom, stripFileExtension, wholeNumberFrom } from './format.ts';
import { normalizeFighterStatus, normalizeFleetColor, normalizeShipIconKey, normalizeWeaponMount } from './normalize.ts';
import { newWeaponMount } from './weapons.ts';
import type { Fleet, FleetExport, FleetExportShip, SavedFleet, Ship, ShipForm, WeaponMount } from '../types.ts';

/**
 * What a number the file did not carry reads back as. Zero rather than a plausible value: the
 * server's clamps floor each of these the same way they floor a mount nobody filled in, so an
 * unentered field is visibly unentered instead of quietly being somebody's published stat.
 */
const unentered = 0;

/**
 * The device's fleet library, read back out of storage. It was written by whichever build last ran
 * here, so it goes through the same coercion an imported file does; an entry that is not a fleet
 * at all is dropped rather than allowed to reach a render.
 */
export function normalizeSavedFleets(value: unknown, fallback: ShipForm): SavedFleet[] {
  if (!Array.isArray(value)) {
    return [];
  }

  const entries: SavedFleet[] = [];
  for (const entry of value) {
    if (!entry || typeof entry !== 'object') {
      continue;
    }

    const record = entry as Record<string, unknown>;
    try {
      entries.push({
        savedAt: stringFrom(record.savedAt, new Date(0).toISOString()),
        fleet: normalizeFleetExport(record.fleet, fallback, ''),
      });
    } catch {
      // Not a fleet. The rest of the library is still worth keeping.
    }
  }

  return entries;
}

export function parseWeaponsCell(value: string): WeaponMount[] {
  if (!value.trim()) {
    return [newWeaponMount()];
  }

  return value.split(';').map((entry) => {
    const [name, attackDice, maxRange, arcs, ammoMax, ammoUsed, reloadTurns, destroyed, kind] = entry.split('|');
    // The arcs cell holds one or more arc names joined by '+'; older files hold a single
    // four-arc name here, which normalizeArcs expands.
    return normalizeWeaponMount({ name, attackDice, maxRange, arcs, arc: arcs, ammoMax, ammoUsed, reloadTurns, isDestroyed: destroyed === 'out', kind });
  });
}
export function toFleetExport(fleet: Fleet, ships: Ship[]): FleetExport {
  return {
    schema: 'forcesignal-fleet-1',
    gameSystem: 'space-fleet-compatible',
    name: fleet.name,
    faction: fleet.faction ?? '',
    fleetColor: normalizeFleetColor(fleet.fleetColor),
    ships: ships.map((ship) => ({
      name: ship.name,
      className: ship.className ?? '',
      iconKey: normalizeShipIconKey(ship.iconKey, ship.className),
      thrustRating: ship.thrustRating,
      initialVelocity: ship.currentVelocity,
      initialCourse: ship.currentCourse,
      startX: ship.positionX,
      startY: ship.positionY,
      hullMax: ship.hullMax,
      armorMax: ship.armorMax,
      screenRating: ship.screenRating,
      fireControlMax: ship.fireControlMax ?? 1,
      pointDefenseSystems: ship.pointDefenseSystems ?? 0,
      fighterBays: ship.fighterBays ?? 0,
      damageControlParties: ship.damageControlParties ?? 0,
      weapons: ship.weapons,
      fighterEnduranceMax: ship.fighterEnduranceMax,
      fighterEnduranceUsed: ship.fighterEnduranceUsed,
      fighterMaxRange: ship.fighterMaxRange,
      fighterStatus: ship.fighterStatus,
      homeCarrierShipId: ship.homeCarrierShipId ?? null,
      // Ship ids are per-match, so carrier assignments only survive a transfer by name.
      homeCarrierName: ships.find((candidate) => candidate.id === ship.homeCarrierShipId)?.name ?? null,
      pointsValue: ship.pointsValue ?? 0,
    })),
  };
}
export function parseFleetExport(text: string, fileName: string, fallback: ShipForm): FleetExport {
  if (fileName.toLowerCase().endsWith('.json')) {
    return normalizeFleetExport(JSON.parse(text), fallback, fileName);
  }

  const rows = parseCsv(text).filter((row) => row.some((cell) => cell.trim().length > 0));
  if (rows.length === 0) {
    throw new Error('CSV import is empty.');
  }

  const headers = rows[0].map((header) => normalizeHeader(header));
  const ships = rows.slice(1).map((row, index) => {
    const getValue = (header: string) => row[headers.indexOf(header)]?.trim() ?? '';
    return normalizeFleetExportShip({
      name: getValue('name') || `Imported Ship ${index + 1}`,
      className: getValue('class') || getValue('classname'),
      iconKey: getValue('iconkey') || getValue('icon') || getValue('shipicon'),
      thrustRating: getValue('thrust') || getValue('thrustrating'),
      initialVelocity: getValue('startvelocity') || getValue('initialvelocity') || getValue('velocity'),
      initialCourse: getValue('course') || getValue('initialcourse'),
      startX: getValue('x') || getValue('startx') || getValue('positionx'),
      startY: getValue('y') || getValue('starty') || getValue('positiony'),
      hullMax: getValue('hull') || getValue('hullboxes') || getValue('hullmax'),
      armorMax: getValue('armor') || getValue('armorboxes') || getValue('armormax'),
      screenRating: getValue('screens') || getValue('screenrating'),
      fireControlMax: getValue('firecontrolmax') || getValue('firecons'),
      pointDefenseSystems: getValue('pointdefensesystems') || getValue('pds'),
      fighterBays: getValue('fighterbays') || getValue('bays'),
      damageControlParties: getValue('damagecontrolparties') || getValue('dcp'),
      fighterEnduranceMax: getValue('fighterendurance') || getValue('fighterendurancemax'),
      fighterEnduranceUsed: getValue('fighterused') || getValue('fighterenduranceused'),
      fighterMaxRange: getValue('fighterrange') || getValue('fightermaxrange'),
      fighterStatus: getValue('fighterstatus'),
      homeCarrierName: getValue('homecarriername') || getValue('homecarrier') || getValue('carrier'),
      pointsValue: getValue('pointsvalue') || getValue('points') || getValue('npv'),
      weapons: parseWeaponsCell(getValue('weapons')),
    }, fallback);
  });

  return {
    schema: 'forcesignal-fleet-1',
    gameSystem: 'space-fleet-compatible',
    name: stripFileExtension(fileName) || fallback.fleetName,
    faction: fallback.faction,
    fleetColor: normalizeFleetColor(getValueFromRows(rows, headers, 'fleetcolor') || fallback.fleetColor),
    ships,
  };
}
export function normalizeFleetExport(value: unknown, fallback: ShipForm, fileName: string): FleetExport {
  if (!value || typeof value !== 'object') {
    throw new Error('Fleet JSON must be an object.');
  }

  const record = value as Record<string, unknown>;
  const rawShips = Array.isArray(record.ships) ? record.ships : [];
  return {
    schema: 'forcesignal-fleet-1',
    gameSystem: 'space-fleet-compatible',
    name: stringFrom(record.name, stripFileExtension(fileName) || fallback.fleetName),
    faction: stringFrom(record.faction, fallback.faction),
    fleetColor: normalizeFleetColor(record.fleetColor ?? fallback.fleetColor),
    ships: rawShips.map((ship) => normalizeFleetExportShip(ship, fallback)),
  };
}
export function normalizeFleetExportShip(value: unknown, fallback: ShipForm): FleetExportShip {
  if (!value || typeof value !== 'object') {
    throw new Error('Each imported ship must be an object or CSV row.');
  }

  const record = value as Record<string, unknown>;
  return {
    name: stringFrom(record.name, fallback.name),
    className: stringFrom(record.className ?? record.class, fallback.className),
    iconKey: normalizeShipIconKey(record.iconKey ?? record.icon, stringFrom(record.className ?? record.class, fallback.className)),
    // A column the file did not carry is a number the player did not enter, and `unentered` is what
    // this app writes for that: zero, which the server's own clamps floor exactly as they floor a
    // mount nobody filled in. These used to fall back to the new-ship form's seed, so a CSV naming
    // only a ship came back with thrust 4, hull 12, armour 4, screens 1, firecons 2, point defence 1
    // and damage control 2 - a whole stat block, with no form anywhere for the player to see it had
    // been written for them, and by this project's own rule not ours to write.
    //
    // Read from a constant rather than from `fallback` on purpose: the import's answer to a missing
    // column and the form's opening state are two different questions, and tying them together is
    // what let one of them be decided by the other.
    thrustRating: wholeNumberFrom(record.thrustRating ?? record.thrust, unentered, 0, 20),
    initialVelocity: wholeNumberFrom(record.initialVelocity ?? record.currentVelocity ?? record.velocity, unentered, 0, 999),
    // A heading rather than a quantity: the clock runs 1 to 12 and has no zero, so a file that names
    // no course gets the one the form opens on.
    initialCourse: wholeNumberFrom(record.initialCourse ?? record.currentCourse ?? record.course, 1, 1, 12),
    startX: numberFrom(record.startX ?? record.positionX ?? record.x, unentered, 0, 144),
    startY: numberFrom(record.startY ?? record.positionY ?? record.y, unentered, 0, 96),
    hullMax: wholeNumberFrom(record.hullMax ?? record.hullBoxes ?? record.hull, unentered, 1, 80),
    armorMax: wholeNumberFrom(record.armorMax ?? record.armorBoxes ?? record.armor, unentered, 0, 40),
    screenRating: wholeNumberFrom(record.screenRating ?? record.screens, unentered, 0, 3),
    fireControlMax: wholeNumberFrom(record.fireControlMax ?? record.firecons, unentered, 0, 6),
    pointDefenseSystems: wholeNumberFrom(record.pointDefenseSystems ?? record.pds, unentered, 0, 12),
    fighterBays: wholeNumberFrom(record.fighterBays ?? record.bays, unentered, 0, 12),
    damageControlParties: wholeNumberFrom(record.damageControlParties ?? record.dcp, unentered, 0, 12),
    weapons: Array.isArray(record.weapons) ? record.weapons.map(normalizeWeaponMount) : [newWeaponMount()],
    fighterEnduranceMax: wholeNumberFrom(record.fighterEnduranceMax ?? record.fighterEndurance, unentered, 0, 24),
    fighterEnduranceUsed: wholeNumberFrom(record.fighterEnduranceUsed ?? record.fighterUsed, unentered, 0, 24),
    fighterMaxRange: wholeNumberFrom(record.fighterMaxRange ?? record.fighterRange, unentered, 0, 120),
    fighterStatus: normalizeFighterStatus(record.fighterStatus, fallback.fighterStatus),
    homeCarrierShipId: typeof record.homeCarrierShipId === 'string' ? record.homeCarrierShipId : null,
    homeCarrierName: typeof record.homeCarrierName === 'string' && record.homeCarrierName.trim() ? record.homeCarrierName.trim() : null,
    pointsValue: wholeNumberFrom(record.pointsValue ?? record.points ?? record.npv, 0, 0, 99999),
  };
}
export function fleetExportToCsv(fleet: FleetExport) {
  const rows = [
    ['fleetColor', 'name', 'className', 'iconKey', 'thrustRating', 'initialVelocity', 'initialCourse', 'startX', 'startY', 'hullMax', 'armorMax', 'screenRating', 'fireControlMax', 'pointDefenseSystems', 'fighterBays', 'damageControlParties', 'fighterEnduranceMax', 'fighterEnduranceUsed', 'fighterMaxRange', 'fighterStatus', 'homeCarrierName', 'pointsValue', 'weapons'],
    ...fleet.ships.map((ship) => [
      fleet.fleetColor,
      ship.name,
      ship.className,
      ship.iconKey,
      String(ship.thrustRating),
      String(ship.initialVelocity),
      String(ship.initialCourse),
      String(ship.startX),
      String(ship.startY),
      String(ship.hullMax),
      String(ship.armorMax),
      String(ship.screenRating),
      String(ship.fireControlMax ?? 1),
      String(ship.pointDefenseSystems ?? 0),
      String(ship.fighterBays ?? 0),
      String(ship.damageControlParties ?? 0),
      String(ship.fighterEnduranceMax),
      String(ship.fighterEnduranceUsed),
      String(ship.fighterMaxRange),
      ship.fighterStatus,
      ship.homeCarrierName ?? '',
      String(ship.pointsValue ?? 0),
      ship.weapons.map((weapon) => `${weapon.name}|${weapon.attackDice}|${weapon.maxRange}|${weapon.arcs.join('+')}|${weapon.ammoMax}|${weapon.ammoUsed}|${weapon.reloadTurns}|${weapon.isDestroyed ? 'out' : ''}|${weapon.kind}`).join(';'),
    ]),
  ];

  return `${rows.map((row) => row.map(csvEscape).join(',')).join('\n')}\n`;
}
export function parseCsv(text: string) {
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = '';
  let quoted = false;

  for (let index = 0; index < text.length; index += 1) {
    const char = text[index];
    const next = text[index + 1];

    if (char === '"' && quoted && next === '"') {
      cell += '"';
      index += 1;
      continue;
    }

    if (char === '"') {
      quoted = !quoted;
      continue;
    }

    if (char === ',' && !quoted) {
      row.push(cell);
      cell = '';
      continue;
    }

    if ((char === '\n' || char === '\r') && !quoted) {
      if (char === '\r' && next === '\n') {
        index += 1;
      }
      row.push(cell);
      rows.push(row);
      row = [];
      cell = '';
      continue;
    }

    cell += char;
  }

  row.push(cell);
  rows.push(row);
  return rows;
}
export function getValueFromRows(rows: string[][], headers: string[], header: string) {
  const index = headers.indexOf(header);
  if (index < 0) {
    return '';
  }

  return rows.slice(1).map((row) => row[index]?.trim() ?? '').find(Boolean) ?? '';
}

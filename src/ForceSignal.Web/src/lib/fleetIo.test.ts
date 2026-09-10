import { describe, expect, it } from 'vitest';
import { defaultShipForm } from '../constants.ts';
import { fleetExportToCsv, normalizeFleetExport, normalizeFleetExportShip, normalizeSavedFleets, parseCsv, parseFleetExport, parseWeaponsCell, toFleetExport } from './fleetIo.ts';
import type { Fleet, Ship } from '../types.ts';

/**
 * Import is the untrusted path: the file was picked by hand and may have been written by an older
 * version, edited in a spreadsheet, or not be a fleet at all. What matters is that a file this app
 * wrote opens again, that a hand-edited one is coerced rather than refused, and that nothing in it
 * can throw part-way through a load.
 */
const fleet: Fleet = { id: 'blue', ownerParticipantId: 'p1', name: 'Blue Fleet', faction: 'Custom', fleetColor: '#ff8800' };

function ship(overrides: Partial<Ship> = {}): Ship {
  return {
    id: 'a',
    fleetId: 'blue',
    name: 'Alpha',
    className: 'Cruiser',
    thrustRating: 4,
    currentVelocity: 8,
    currentCourse: 3,
    positionX: 12,
    positionY: 24,
    hullMax: 12,
    hullDamage: 2,
    armorMax: 4,
    armorDamage: 0,
    fireControlMax: 2,
    fireControlDamage: 0,
    pointDefenseSystems: 1,
    fighterBays: 0,
    damageControlParties: 2,
    driveDamage: 0,
    weaponDamage: 0,
    screenRating: 1,
    weapons: [{ id: 'w1', name: 'Beam', attackDice: 2, maxRange: 24, arcs: ['Fore', 'ForePort'], kind: 'Beam', ammoMax: 0, ammoUsed: 0, reloadTurns: 0, isDestroyed: false }],
    isDestroyed: false,
    iconKey: 'cruiser',
    fighterEnduranceMax: 0,
    fighterEnduranceUsed: 0,
    fighterMaxRange: 0,
    fighterStatus: 'Docked',
    pointsValue: 120,
    effectiveScreens: 1,
    workingFireControl: 2,
    fighterReach: 0,
    repairableSystems: [],
    ...overrides,
  };
}

describe('toFleetExport', () => {
  it('writes the schema, the fleet identity and every ship', () => {
    const exported = toFleetExport(fleet, [ship()]);

    expect(exported.schema).toBe('forcesignal-fleet-1');
    expect(exported.name).toBe('Blue Fleet');
    expect(exported.fleetColor).toBe('#ff8800');
    expect(exported.ships).toHaveLength(1);
    expect(exported.ships[0].initialVelocity).toBe(8);
  });

  // Ship ids are per-match, so a carrier assignment only survives a transfer by name.
  it('records a fighter group\'s carrier by name', () => {
    const carrier = ship({ id: 'cv', name: 'Ark', iconKey: 'carrier' });
    const wing = ship({ id: 'f1', name: 'Wing', iconKey: 'fighter-group', homeCarrierShipId: 'cv' });

    expect(toFleetExport(fleet, [carrier, wing]).ships[1].homeCarrierName).toBe('Ark');
  });
});

describe('parseFleetExport', () => {
  it('round-trips a JSON file it wrote', () => {
    const written = toFleetExport(fleet, [ship()]);
    const read = parseFleetExport(JSON.stringify(written), 'blue.forcesignal-fleet.json', defaultShipForm);

    expect(read).toEqual(written);
  });

  it('round-trips a CSV file it wrote', () => {
    const written = toFleetExport(fleet, [ship()]);
    const read = parseFleetExport(fleetExportToCsv(written), 'blue.forcesignal-fleet.csv', defaultShipForm);

    expect(read.ships[0].name).toBe('Alpha');
    expect(read.ships[0].hullMax).toBe(12);
    expect(read.ships[0].weapons[0].arcs).toEqual(['Fore', 'ForePort']);
    expect(read.fleetColor).toBe('#ff8800');
  });

  it('names a CSV fleet after the file and reads loose header spellings', () => {
    const read = parseFleetExport('Name,Class,Thrust,Hull,Armor\nBravo,Destroyer,6,10,2\n', 'Second Fleet.csv', defaultShipForm);

    expect(read.name).toBe('Second Fleet');
    expect(read.ships[0]).toMatchObject({ name: 'Bravo', className: 'Destroyer', thrustRating: 6, hullMax: 10, armorMax: 2 });
  });

  it('refuses an empty CSV and a JSON file that is not an object', () => {
    expect(() => parseFleetExport('\n\n', 'x.csv', defaultShipForm)).toThrow('CSV import is empty.');
    expect(() => parseFleetExport('[]', 'x.json', defaultShipForm)).not.toThrow();
    expect(() => parseFleetExport('null', 'x.json', defaultShipForm)).toThrow('Fleet JSON must be an object.');
  });
});

describe('normalizeFleetExportShip', () => {
  it('clamps every number into range and falls back to the form for what is missing', () => {
    const read = normalizeFleetExportShip({ name: 'Wild', thrust: 99, hull: 0, course: 40, screens: -3 }, defaultShipForm);

    expect(read.thrustRating).toBe(20);
    expect(read.hullMax).toBe(1);
    expect(read.initialCourse).toBe(12);
    expect(read.screenRating).toBe(0);
    expect(read.armorMax).toBe(defaultShipForm.armorMax);
  });

  it('reads the older field names', () => {
    const read = normalizeFleetExportShip({ class: 'Escort', velocity: 6, x: 3, y: 4, firecons: 1, pds: 2, bays: 1, dcp: 3, npv: 40 }, defaultShipForm);

    expect(read).toMatchObject({ className: 'Escort', iconKey: 'escort', initialVelocity: 6, startX: 3, startY: 4, fireControlMax: 1, pointDefenseSystems: 2, fighterBays: 1, damageControlParties: 3, pointsValue: 40 });
  });

  it('gives a ship with no weapons one mount rather than none', () => {
    expect(normalizeFleetExportShip({ name: 'Bare' }, defaultShipForm).weapons).toHaveLength(1);
  });

  it('refuses a row that is not an object', () => {
    expect(() => normalizeFleetExportShip('Alpha', defaultShipForm)).toThrow();
  });

  // The screen ceiling was a flat 3 here - `RulesProfile.maxScreenLevel`, the player's, written into
  // this file as a constant. A table playing to 5 lost two levels off every ship in their own file,
  // silently, on the way in.
  it('reads the screen rating against the ceiling the table entered', () => {
    const read = normalizeFleetExportShip({ name: 'Wall', screens: 5 }, defaultShipForm, 5);

    // Reached-the-subject: it is the row that was passed in, so the 5 below is that row's.
    expect(read.name).toBe('Wall');
    expect(read.screenRating).toBe(5);
  });

  it('reads it as written when no profile has landed, and lets the server clamp', () => {
    // No ceiling of this app's choosing. The server clamps against the profile, which is the only
    // place that limit is actually known.
    expect(normalizeFleetExportShip({ name: 'Wall', screens: 5 }, defaultShipForm).screenRating).toBe(5);
  });

  it('still holds the file to the ceiling the table did enter', () => {
    // The control. A reader that never clamps would satisfy both of the above and be no reader.
    expect(normalizeFleetExportShip({ name: 'Wall', screens: 5 }, defaultShipForm, 2).screenRating).toBe(2);
    // And a number no screen level could be is malformed rather than generous.
    expect(normalizeFleetExportShip({ name: 'Wall', screens: 400 }, defaultShipForm).screenRating).toBe(9);
  });

  it('carries the ceiling through every door into this file', () => {
    // Three entry points read rows, and the ceiling has to reach all of them or one door quietly
    // keeps the old answer.
    const row = { name: 'Wall', screens: 5 };
    const json = parseFleetExport(JSON.stringify({ ships: [row] }), 'fleet.json', defaultShipForm, 5);
    const csv = parseFleetExport('name,screens\nWall,5\n', 'fleet.csv', defaultShipForm, 5);
    const saved = normalizeSavedFleets([{ savedAt: 'x', fleet: { ships: [row] } }], defaultShipForm, 5);

    expect(json.ships[0].screenRating).toBe(5);
    expect(csv.ships[0].screenRating).toBe(5);
    expect(saved[0].fleet.ships[0].screenRating).toBe(5);
  });
});

describe('normalizeFleetExport', () => {
  it('names the fleet after the file when the JSON has no name', () => {
    expect(normalizeFleetExport({ ships: [] }, defaultShipForm, 'Red Fleet.json').name).toBe('Red Fleet');
  });

  it('replaces a colour that is not a hex colour', () => {
    expect(normalizeFleetExport({ fleetColor: 'red; background: url(x)' }, defaultShipForm, 'x.json').fleetColor).toBe('#47f1ff');
  });
});

describe('parseWeaponsCell', () => {
  it('reads the pipe-and-semicolon form the CSV writes', () => {
    const mounts = parseWeaponsCell('Beam|2|24|Fore+ForePort|0|0|0||Beam;Torp|1|30|Fore|3|1|2|out|PulseTorpedo');

    expect(mounts).toHaveLength(2);
    expect(mounts[0].arcs).toEqual(['Fore', 'ForePort']);
    expect(mounts[1]).toMatchObject({ kind: 'PulseTorpedo', ammoMax: 3, ammoUsed: 1, reloadTurns: 2, isDestroyed: true });
  });

  it('expands a four-arc name from an older file', () => {
    expect(parseWeaponsCell('Beam|2|24|Port').at(0)?.arcs).toEqual(['AftPort', 'ForePort']);
  });

  it('gives an empty cell one default mount', () => {
    expect(parseWeaponsCell('  ')).toHaveLength(1);
  });
});

describe('parseCsv', () => {
  it('honours quoted commas, doubled quotes and both line endings', () => {
    expect(parseCsv('a,"b, c","say ""hi"""\r\nd,e,f\n')).toEqual([['a', 'b, c', 'say "hi"'], ['d', 'e', 'f'], ['']]);
  });
});

describe('normalizeSavedFleets', () => {
  const good = { savedAt: '2026-01-01T00:00:00.000Z', fleet: toFleetExport(fleet, [ship()]) };

  it('keeps well-formed entries', () => {
    const library = normalizeSavedFleets([good], defaultShipForm);

    expect(library).toHaveLength(1);
    expect(library[0].fleet.ships[0].name).toBe('Alpha');
  });

  // The library is written by whichever build last ran on the device: a stored string, a list of
  // strings, or an entry from before `fleet` existed all used to reach `.ships.length` in a render.
  it('drops entries that are not fleets and keeps the rest', () => {
    const library = normalizeSavedFleets([good, 'nonsense', null, { savedAt: 'x' }, { fleet: 'not a fleet' }], defaultShipForm);

    expect(library).toHaveLength(1);
  });

  it('coerces a fleet whose ships have drifted rather than dropping it', () => {
    const library = normalizeSavedFleets([{ fleet: { name: 'Old', ships: [{ name: 'Relic', thrust: '7', hull: 'lots' }] } }], defaultShipForm);

    expect(library[0].fleet.ships[0]).toMatchObject({ name: 'Relic', thrustRating: 7, hullMax: defaultShipForm.hullMax });
    expect(library[0].savedAt).toBe(new Date(0).toISOString());
  });

  it('treats anything that is not a list as an empty library', () => {
    expect(normalizeSavedFleets(null, defaultShipForm)).toEqual([]);
    expect(normalizeSavedFleets({ fleet: good.fleet }, defaultShipForm)).toEqual([]);
    expect(normalizeSavedFleets('[]', defaultShipForm)).toEqual([]);
  });
});

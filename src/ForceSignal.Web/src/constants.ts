/**
 * Fixed values the app is built around: the keys it stores things under, the canonical order of
 * the firing arcs, and the starting points a player edits from.
 *
 * The ship presets are conveniences, not rules - every number in them is a starting point the
 * player is expected to replace from their own records.
 */

import { newId } from './lib/api.ts';
import { weaponPreset } from './lib/weapons.ts';
import type { FighterStatus, FiringArc, ShipForm, ShipIconKey, WeaponKind } from './types.ts';

export const officialRulesUrl = 'https://shop.groundzerogames.co.uk/rules.html';
export const sessionKey = 'forcesignal.session';
export const draftsKey = 'forcesignal.drafts';
export const snapshotBackupKey = 'forcesignal.snapshot-backup';
export const fleetLibraryKey = 'forcesignal.fleet-library';
export const displayNameKey = 'forcesignal.display-name';
// Each ground engine remembers one game per device: its id and the token that proves this device
// started it. Without these a refresh lost the whole game.
export const starGruntGameKey = 'forcesignal.stargrunt-game';
export const dirtsideGameKey = 'forcesignal.dirtside-game';
export const defaultShipForm: ShipForm = {
  fleetName: 'Patrol Group',
  faction: 'Custom',
  fleetColor: '#47f1ff',
  name: 'Valiant',
  className: 'Cruiser',
  iconKey: 'cruiser',
  thrustRating: 4,
  currentVelocity: 8,
  currentCourse: 1,
  positionX: 12,
  positionY: 24,
  hullMax: 12,
  armorMax: 4,
  fireControlMax: 2,
  pointDefenseSystems: 1,
  fighterBays: 0,
  damageControlParties: 2,
  screenRating: 1,
  weapons: [{
    id: newId(),
    name: 'Class-2 Beam',
    attackDice: 2,
    maxRange: 24,
    arcs: ['Fore'],
    kind: 'Beam',
    ammoMax: 0,
    ammoUsed: 0,
    reloadTurns: 0,
  }],
  fighterEnduranceMax: 0,
  fighterEnduranceUsed: 0,
  fighterMaxRange: 0,
  fighterStatus: 'Docked',
  homeCarrierShipId: '',
  pointsValue: 0,
};
export const shipIconOptions: { key: ShipIconKey; label: string }[] = [
  { key: 'escort', label: 'Escort' },
  { key: 'frigate', label: 'Frigate' },
  { key: 'destroyer', label: 'Destroyer' },
  { key: 'cruiser', label: 'Cruiser' },
  { key: 'carrier', label: 'Carrier' },
  { key: 'dreadnought', label: 'Dreadnought' },
  { key: 'fighter-group', label: 'Fighter group' },
  { key: 'station', label: 'Station' },
];
export const fighterStatuses: FighterStatus[] = ['Docked', 'Airborne', 'Recovering'];
// Six sixty-degree arcs, clockwise from dead ahead.
/// How far a fighter group flies in a turn. It moves in any direction inside this radius rather than
/// being plotted on a course, which is why it needs no written order.
export const fighterMoveAllowance = 12;
export const firingArcs: FiringArc[] = ['Fore', 'ForeStarboard', 'AftStarboard', 'Aft', 'AftPort', 'ForePort'];
// Every weapon has the aft arc blacked out, so only these five can ever be fired through.
export const firableArcs: FiringArc[] = ['Fore', 'ForeStarboard', 'AftStarboard', 'AftPort', 'ForePort'];
export const arcLabels: Record<FiringArc, string> = {
  Fore: 'Fore',
  ForeStarboard: 'Fore Starboard',
  AftStarboard: 'Aft Starboard',
  Aft: 'Aft',
  AftPort: 'Aft Port',
  ForePort: 'Fore Port',
};
export const arcAbbreviations: Record<FiringArc, string> = {
  Fore: 'F',
  ForeStarboard: 'FS',
  AftStarboard: 'AS',
  Aft: 'A',
  AftPort: 'AP',
  ForePort: 'FP',
};
export const weaponKinds: { key: WeaponKind; label: string; maxRange: number }[] = [
  { key: 'Beam', label: 'Beam battery', maxRange: 36 },
  { key: 'PulseTorpedo', label: 'Pulse torpedo', maxRange: 30 },
  { key: 'NeedleBeam', label: 'Needle beam', maxRange: 9 },
];
export const shipPresets: { label: string; patch: Partial<ShipForm> }[] = [
  {
    label: 'Escort',
    patch: { className: 'Escort', iconKey: 'escort', thrustRating: 6, hullMax: 6, armorMax: 0, screenRating: 0, fireControlMax: 1, pointDefenseSystems: 0, weapons: [weaponPreset('Class-1 Beam', 1, 12, ['Fore'])] },
  },
  {
    label: 'Frigate',
    patch: { className: 'Frigate', iconKey: 'frigate', thrustRating: 5, hullMax: 8, armorMax: 1, screenRating: 0, fireControlMax: 1, pointDefenseSystems: 1, weapons: [weaponPreset('Class-2 Beam', 2, 24, ['ForePort', 'Fore', 'ForeStarboard'])] },
  },
  {
    label: 'Destroyer',
    patch: { className: 'Destroyer', iconKey: 'destroyer', thrustRating: 4, hullMax: 10, armorMax: 2, screenRating: 1, fireControlMax: 1, pointDefenseSystems: 1, weapons: [weaponPreset('Class-2 Beam', 2, 24, ['ForePort', 'Fore', 'ForeStarboard'])] },
  },
  {
    label: 'Cruiser',
    patch: { className: 'Cruiser', iconKey: 'cruiser', thrustRating: 4, hullMax: 12, armorMax: 4, screenRating: 1, fireControlMax: 2, pointDefenseSystems: 2, weapons: [weaponPreset('Class-2 Beam', 2, 24, ['ForePort', 'Fore', 'ForeStarboard']), weaponPreset('Class-1 Beam', 1, 12, [...firableArcs]), weaponPreset('Torpedo Tube', 1, 30, ['Fore'], 0, 'PulseTorpedo')] },
  },
  {
    label: 'Carrier',
    patch: { className: 'Carrier', iconKey: 'carrier', thrustRating: 4, hullMax: 14, armorMax: 5, screenRating: 1, fireControlMax: 2, pointDefenseSystems: 3, fighterBays: 4, weapons: [weaponPreset('Fighter Bay', 3, 12, [...firableArcs])] },
  },
  {
    label: 'Fighters',
    patch: { className: 'Fighter Group', iconKey: 'fighter-group', thrustRating: 6, currentVelocity: 12, hullMax: 6, armorMax: 0, screenRating: 0, fireControlMax: 1, pointDefenseSystems: 0, weapons: [weaponPreset('Fighter Attack', 3, 6, ['Fore'])], fighterEnduranceMax: 6, fighterEnduranceUsed: 0, fighterMaxRange: 24, fighterStatus: 'Docked' },
  },
  {
    label: 'Station',
    patch: { className: 'Station', iconKey: 'station', thrustRating: 0, currentVelocity: 0, hullMax: 18, armorMax: 6, screenRating: 2, fireControlMax: 3, pointDefenseSystems: 4, weapons: [weaponPreset('Heavy Battery', 3, 30, [...firableArcs])] },
  },
];

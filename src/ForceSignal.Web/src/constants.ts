/**
 * Fixed values the app is built around: the keys it stores things under, the canonical order of
 * the firing arcs, and the starting points a player edits from.
 *
 * The ship presets name a class and pick an icon. They deliberately carry no numbers: hull rows,
 * armour, screens, fire control, thrust and weapon reach are the player's, entered from their own
 * records and held by the rules profile. A starting point the player is "expected to replace" is
 * still a number this app shipped, which is the thing it does not do.
 */

import { newWeaponMount } from './lib/weapons.ts';
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
// Where the device was when it last rendered. A refresh used to land every device on Full Thrust /
// Ships, and a ground game was only reachable by clicking back into its engine.
export const gameModeKey = 'forcesignal.game-mode';
export const activeViewKey = 'forcesignal.active-view';
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
  // Built by newWeaponMount rather than written out again: this was the third copy of the same
  // invented "Class-2 Beam", and a mount with no numbers on it has no reason to exist twice.
  weapons: [newWeaponMount()],
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
// The kinds the engine has procedures for, and nothing else. Reach used to be recorded here as
// well - 36, 30 and 9 - which are published numbers, and the app ships none of those. They are
// also already the player's: `torpedoMaximumRange` and `needleBeamRange` live on the rules
// profile, so having them here made a second copy that no one had entered and no one could edit.
export const weaponKinds: { key: WeaponKind; label: string }[] = [
  { key: 'Beam', label: 'Beam battery' },
  { key: 'PulseTorpedo', label: 'Pulse torpedo' },
  { key: 'NeedleBeam', label: 'Needle beam' },
];
// Hull shapes, not stat blocks. A preset names a class and picks its icon so the ships on the map
// are tellable apart at a glance, and stops there: hull rows, armour, screens, fire control, point
// defence, thrust and weapon mounts are numbers, and by this project's own rule the player owns
// every number the procedures read. This list used to carry a full profile for each of seven
// classes. The server was scrubbed of published numbers before it shipped; the client was not, and
// a default that happens to be somebody's published values is still those values.
export const shipPresets: { label: string; patch: Partial<ShipForm> }[] = [
  { label: 'Escort', patch: { className: 'Escort', iconKey: 'escort' } },
  { label: 'Frigate', patch: { className: 'Frigate', iconKey: 'frigate' } },
  { label: 'Destroyer', patch: { className: 'Destroyer', iconKey: 'destroyer' } },
  { label: 'Cruiser', patch: { className: 'Cruiser', iconKey: 'cruiser' } },
  { label: 'Carrier', patch: { className: 'Carrier', iconKey: 'carrier' } },
  { label: 'Fighters', patch: { className: 'Fighter Group', iconKey: 'fighter-group', fighterStatus: 'Docked' } },
  { label: 'Station', patch: { className: 'Station', iconKey: 'station' } },
];

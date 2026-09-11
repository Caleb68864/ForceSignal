/**
 * Reading whatever actually arrived into the shapes the app expects.
 *
 * Everything here takes `unknown` on purpose. A snapshot comes off the wire, a fleet comes out of
 * a file the player picked, and an older export may predate a field entirely - so nothing is
 * assumed to be present or to be the right type, and a value that cannot be understood falls back
 * to something playable rather than throwing part-way through a load.
 */

import { firableArcs, firingArcs, shipIconOptions, weaponKinds } from '../constants.ts';
import { newId } from './api.ts';
import { numberFrom, stringFrom, wholeNumberFrom } from './format.ts';
import { wrapCourse } from './geometry.ts';
import { newWeaponMount } from './weapons.ts';
import type { BattleView, FighterStatus, FiringArc, GameHandle, GameMode, MatchSnapshot, OrdnanceMarker, Session, ShipIconKey, WeaponKind, WeaponMount } from '../types.ts';

/**
 * A session out of this device's storage. Anything short of four non-empty strings is not one: a
 * session written by an older build with the match id missing produced `GET /api/matches/undefined`
 * on every load.
 */
export function normalizeSession(value: unknown): Session | null {
  if (!value || typeof value !== 'object') {
    return null;
  }

  const record = value as Record<string, unknown>;
  const matchId = stringFrom(record.matchId, '');
  const participantId = stringFrom(record.participantId, '');
  const participantToken = stringFrom(record.participantToken, '');
  const joinCode = stringFrom(record.joinCode, '');
  if (!matchId || !participantId || !participantToken || !joinCode) {
    return null;
  }

  return { matchId, participantId, participantToken, joinCode };
}
/** A ground game's handle out of storage: the id and the token, both required. */
export function normalizeGameHandle(value: unknown): GameHandle | null {
  if (!value || typeof value !== 'object') {
    return null;
  }

  const record = value as Record<string, unknown>;
  const gameId = stringFrom(record.gameId, '');
  const token = stringFrom(record.token, '');
  return gameId && token ? { gameId, token } : null;
}

export function normalizeShipIconKey(value: unknown, className?: unknown): ShipIconKey {
  const normalized = typeof value === 'string' ? value.trim().toLowerCase().replaceAll(' ', '-').replaceAll('_', '-') : '';
  if (shipIconOptions.some((option) => option.key === normalized)) {
    return normalized as ShipIconKey;
  }

  // The class name is guarded the same way the key is. Both arrive from a snapshot off the wire or
  // a fleet file the player picked, so neither is known to be a string - and calling a string
  // method on whatever turned up would throw part-way through a load, taking the whole import with
  // it for the sake of one unreadable field.
  const classText = typeof className === 'string' ? className.toLowerCase() : '';
  if (classText.includes('escort')) {
    return 'escort';
  }

  if (classText.includes('frigate')) {
    return 'frigate';
  }

  if (classText.includes('destroyer')) {
    return 'destroyer';
  }

  if (classText.includes('carrier')) {
    return 'carrier';
  }

  if (classText.includes('dreadnought') || classText.includes('battleship')) {
    return 'dreadnought';
  }

  if (classText.includes('fighter')) {
    return 'fighter-group';
  }

  if (classText.includes('station') || classText.includes('base')) {
    return 'station';
  }

  return 'cruiser';
}
export function normalizeFighterStatus(value: unknown, fallback: FighterStatus = 'Docked'): FighterStatus {
  if (typeof value !== 'string') {
    return fallback;
  }

  const normalized = value.trim().toLowerCase();
  if (normalized === 'airborne' || normalized === 'launched' || normalized === 'active') {
    return 'Airborne';
  }

  if (normalized === 'recovering' || normalized === 'returning' || normalized === 'return') {
    return 'Recovering';
  }

  if (normalized === 'docked' || normalized === 'ready') {
    return 'Docked';
  }

  return fallback;
}
export function normalizeOrdnanceStatus(value: unknown) {
  if (typeof value !== 'string') {
    return 'Active';
  }

  const normalized = value.trim().toLowerCase();
  if (normalized === 'resolved' || normalized === 'hit') {
    return 'Resolved';
  }

  if (normalized === 'expired' || normalized === 'spent') {
    return 'Expired';
  }

  return 'Active';
}
function normalizeOrdnanceMarker(value: unknown): OrdnanceMarker | null {
  if (!value || typeof value !== 'object') {
    return null;
  }

  const record = value as Record<string, unknown>;
  const id = typeof record.id === 'string' && record.id.trim() ? record.id : newId();
  const ownerParticipantId = typeof record.ownerParticipantId === 'string' ? record.ownerParticipantId : '';

  return {
    id,
    ownerParticipantId,
    name: stringFrom(record.name, 'Ordnance Marker'),
    markerType: stringFrom(record.markerType, 'Missile'),
    sourceShipId: typeof record.sourceShipId === 'string' ? record.sourceShipId : null,
    targetShipId: typeof record.targetShipId === 'string' ? record.targetShipId : null,
    positionX: numberFrom(record.positionX, 0, 0, 144),
    positionY: numberFrom(record.positionY, 0, 0, 96),
    course: wrapCourse(wholeNumberFrom(record.course, 12, 1, 12)),
    speed: wholeNumberFrom(record.speed, 0, 0, 120),
    enduranceRemaining: wholeNumberFrom(record.enduranceRemaining, 0, 0, 48),
    attackDice: wholeNumberFrom(record.attackDice, 0, 0, 48),
    maxRange: wholeNumberFrom(record.maxRange, 0, 0, 240),
    status: normalizeOrdnanceStatus(record.status),
  };
}
export function normalizeMatchSnapshot(snapshot: MatchSnapshot): MatchSnapshot {
  const rawSnapshot = snapshot as MatchSnapshot & { ordnanceMarkers?: unknown };
  const ordnanceMarkers = Array.isArray(rawSnapshot.ordnanceMarkers)
    ? rawSnapshot.ordnanceMarkers.map(normalizeOrdnanceMarker).filter((marker): marker is OrdnanceMarker => marker !== null)
    : [];

  return {
    ...snapshot,
    participants: snapshot.participants ?? [],
    fleets: snapshot.fleets ?? [],
    ships: snapshot.ships ?? [],
    orderStatuses: snapshot.orderStatuses ?? [],
    revealedOrders: snapshot.revealedOrders ?? [],
    movementResults: snapshot.movementResults ?? [],
    firingResults: snapshot.firingResults ?? [],
    ordnanceMarkers,
    matchLog: snapshot.matchLog ?? [],
  };
}
export function normalizeWeaponMount(value: unknown): WeaponMount {
  if (!value || typeof value !== 'object') {
    return newWeaponMount();
  }

  const record = value as Record<string, unknown>;
  return {
    id: typeof record.id === 'string' && record.id ? record.id : newId(),
    // A mount that arrived with no name, dice or reach is left visibly unfilled rather than being
    // given a class, a damage rating and a range nobody entered. The fallbacks are the floors the
    // server's own clamps impose, which mean "not entered"; "Unnamed Mount" is the label the server
    // already uses for a nameless mount it restores.
    name: stringFrom(record.name, 'Unnamed Mount'),
    attackDice: wholeNumberFrom(record.attackDice ?? record.dice, 1, 1, 12),
    maxRange: wholeNumberFrom(record.maxRange ?? record.range, 1, 1, 72),
    arcs: normalizeArcs(record.arcs, record.arc),
    isDestroyed: record.isDestroyed === true || record.isDestroyed === 'true',
    kind: normalizeWeaponKind(record.kind),
    ammoMax: wholeNumberFrom(record.ammoMax ?? record.ammo, 0, 0, 99),
    ammoUsed: wholeNumberFrom(record.ammoUsed ?? record.used, 0, 0, 99),
    reloadTurns: wholeNumberFrom(record.reloadTurns ?? record.reload, 0, 0, 12),
  };
}
/// Resolves the arcs a mount bears through, accepting either a modern list or the four-arc
/// name written by older exports: each old ninety-degree side arc becomes the two sixty-degree
/// arcs on that side, and the aft arc becomes the two quarters either side of the blind spot.
function normalizeWeaponKind(value: unknown): WeaponKind {
  const kind = stringFrom(value, 'Beam');
  return weaponKinds.some((option) => option.key === kind) ? kind as WeaponKind : 'Beam';
}
export function normalizeArcs(arcs: unknown, legacyArc: unknown): FiringArc[] {
  const listed = Array.isArray(arcs)
    ? arcs.map((entry) => String(entry).trim()).filter((entry): entry is FiringArc => firingArcs.includes(entry as FiringArc))
    : typeof arcs === 'string'
      ? arcs.split(/[+,;]/).map((entry) => entry.trim()).filter((entry): entry is FiringArc => firingArcs.includes(entry as FiringArc))
      : [];
  const resolved = listed.length > 0 ? listed : expandLegacyArc(stringFrom(legacyArc, ''));
  const firable = firableArcs.filter((arc) => resolved.includes(arc));
  return firable.length > 0 ? firable : ['Fore'];
}
export function expandLegacyArc(legacyArc: string): FiringArc[] {
  switch (legacyArc.trim().toLowerCase()) {
    case 'all':
      return [...firableArcs];
    case 'port':
      return ['ForePort', 'AftPort'];
    case 'starboard':
      return ['ForeStarboard', 'AftStarboard'];
    case 'aft':
      return ['AftPort', 'AftStarboard'];
    default: {
      const match = firingArcs.find((arc) => arc.toLowerCase() === legacyArc.trim().toLowerCase().replaceAll(' ', ''));
      return match && match !== 'Aft' ? [match] : ['Fore'];
    }
  }
}
export function normalizeFleetColor(value: unknown) {
  if (typeof value !== 'string') {
    return '#47f1ff';
  }

  return /^#[0-9a-f]{6}$/i.test(value.trim()) ? value.trim() : '#47f1ff';
}

const gameModes: GameMode[] = ['fullthrust', 'stargrunt', 'dirtside'];
const battleViews: BattleView[] = ['ships', 'map', 'log'];
/** The game mode a device stored, if it is one this build knows. */
export function normalizeGameMode(value: unknown): GameMode | null {
  return gameModes.find((mode) => mode === value) ?? null;
}
/** The workspace tab a device stored, if it is one this build knows. */
export function normalizeBattleView(value: unknown): BattleView | null {
  return battleViews.find((view) => view === value) ?? null;
}

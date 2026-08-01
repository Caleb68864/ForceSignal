import { type CSSProperties, type KeyboardEvent, type PointerEvent, useEffect, useMemo, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import * as signalR from '@microsoft/signalr';
import './style.css';

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5225';
const officialRulesUrl = 'https://shop.groundzerogames.co.uk/rules.html';

type TurnDirection = 'None' | 'Port' | 'Starboard';
type FiringArc = 'Fore' | 'Aft' | 'Port' | 'Starboard' | 'All';
type ShipIconKey = 'escort' | 'frigate' | 'destroyer' | 'cruiser' | 'carrier' | 'dreadnought' | 'fighter-group' | 'station';
type FighterStatus = 'Docked' | 'Airborne' | 'Recovering';

type TurnManeuver = {
  direction: Exclude<TurnDirection, 'None'>;
  steps: number;
};

type MovementSegment = {
  course: number;
  distance: number;
};

type Participant = {
  id: string;
  displayName: string;
  role: string;
  isReady: boolean;
  isConnected: boolean;
};

type Fleet = {
  id: string;
  ownerParticipantId: string;
  name: string;
  faction?: string;
  fleetColor: string;
};

type Ship = {
  id: string;
  fleetId: string;
  name: string;
  className?: string;
  thrustRating: number;
  currentVelocity: number;
  currentCourse: number;
  positionX: number;
  positionY: number;
  hullMax: number;
  hullDamage: number;
  armorMax: number;
  armorDamage: number;
  fireControlDamage: number;
  driveDamage: number;
  weaponDamage: number;
  screenRating: number;
  weapons: WeaponMount[];
  isDestroyed: boolean;
  iconKey: ShipIconKey;
  fighterEnduranceMax: number;
  fighterEnduranceUsed: number;
  fighterMaxRange: number;
  fighterStatus: FighterStatus;
  homeCarrierShipId?: string | null;
  pointsValue: number;
};

type WeaponMount = {
  id: string;
  name: string;
  attackDice: number;
  maxRange: number;
  arc: FiringArc;
  ammoMax: number;
  ammoUsed: number;
  reloadTurns: number;
};

type OrdnanceMarker = {
  id: string;
  ownerParticipantId: string;
  name: string;
  markerType: string;
  sourceShipId?: string | null;
  targetShipId?: string | null;
  positionX: number;
  positionY: number;
  course: number;
  speed: number;
  enduranceRemaining: number;
  attackDice: number;
  maxRange: number;
  status: string;
};

type OrderStatus = {
  shipId: string;
  ownerParticipantId: string;
  isCommitted: boolean;
  isRevealed: boolean;
  verificationFailed: boolean;
};

type RevealedOrder = {
  shipId: string;
  velocityDelta: number;
  turnSteps: number;
  turnDirection: TurnDirection;
  turnManeuvers?: TurnManeuver[] | null;
};

type MovementResult = {
  shipId: string;
  startingVelocity: number;
  startingCourse: number;
  endingVelocity: number;
  endingCourse: number;
  segments?: MovementSegment[] | null;
};

type FiringResult = {
  attackerShipId: string;
  targetShipId: string;
  weaponId: string;
  weaponName: string;
  turnNumber: number;
  range: number;
  rangeBand: string;
  arc: FiringArc;
  rawDice: number;
  rangePenalty: number;
  screenReduction: number;
  systemPenalty: number;
  damage: number;
  armorDamageApplied: number;
  hullDamageApplied: number;
};

type MatchLogEntry = {
  sequence: number;
  timestamp: string;
  turnNumber: number;
  phase: string;
  category: string;
  message: string;
};

type MatchSnapshot = {
  matchId: string;
  joinCode: string;
  name: string;
  phase: string;
  turnNumber: number;
  rulesProfileKey: string;
  tableWidth: number;
  tableDepth: number;
  participants: Participant[];
  fleets: Fleet[];
  ships: Ship[];
  orderStatuses: OrderStatus[];
  revealedOrders: RevealedOrder[];
  movementResults: MovementResult[];
  firingResults: FiringResult[];
  ordnanceMarkers: OrdnanceMarker[];
  matchLog: MatchLogEntry[];
  version: number;
  pointsLimit: number;
};

type Session = {
  matchId: string;
  participantId: string;
  participantToken: string;
  joinCode: string;
};

type MatchSeat = {
  participantId: string;
  displayName: string;
  role: string;
  isClaimed: boolean;
  fleetCount: number;
  shipCount: number;
};

type PendingRestore = {
  matchId: string;
  joinCode: string;
  seats: MatchSeat[];
  note: string;
};

type MatchRestored = {
  matchId: string;
  joinCode: string;
  reusedJoinCode: boolean;
  restoredPhase: string;
  lockedOrdersDropped: boolean;
  seats: MatchSeat[];
};

type MatchIdentity = {
  matchId: string;
  joinCode: string;
  hasUnclaimedSeats: boolean;
};

type DraftOrder = {
  velocityDelta: number;
  turnSteps: number;
  turnDirection: TurnDirection;
  turnManeuvers?: TurnManeuver[];
  salt: string;
};

type ShipForm = {
  fleetName: string;
  faction: string;
  fleetColor: string;
  name: string;
  className: string;
  iconKey: ShipIconKey;
  thrustRating: number;
  currentVelocity: number;
  currentCourse: number;
  positionX: number;
  positionY: number;
  hullMax: number;
  armorMax: number;
  screenRating: number;
  weapons: WeaponMount[];
  fighterEnduranceMax: number;
  fighterEnduranceUsed: number;
  fighterMaxRange: number;
  fighterStatus: FighterStatus;
  homeCarrierShipId: string;
  pointsValue: number;
};

type FiringDraft = {
  targetShipId: string;
  weaponId: string;
  range: number;
  arc: FiringArc;
};

type DamageState = Pick<Ship, 'hullDamage' | 'armorDamage' | 'fireControlDamage' | 'driveDamage' | 'weaponDamage'>;

type TablePoint = {
  x: number;
  y: number;
};

type FleetExportShip = {
  name: string;
  className: string;
  iconKey: ShipIconKey;
  thrustRating: number;
  initialVelocity: number;
  initialCourse: number;
  startX: number;
  startY: number;
  hullMax: number;
  armorMax: number;
  screenRating: number;
  weapons: WeaponMount[];
  fighterEnduranceMax: number;
  fighterEnduranceUsed: number;
  fighterMaxRange: number;
  fighterStatus: FighterStatus;
  homeCarrierShipId?: string | null;
  homeCarrierName?: string | null;
  pointsValue: number;
};

type SavedFleet = {
  savedAt: string;
  fleet: FleetExport;
};

type FleetExport = {
  schema: 'forcesignal-fleet-1';
  gameSystem: 'space-fleet-compatible';
  name: string;
  faction: string;
  fleetColor: string;
  ships: FleetExportShip[];
};

class ApiRequestError extends Error {
  constructor(message: string, readonly status: number) {
    super(message);
  }
}

const sessionKey = 'forcesignal.session';
const draftsKey = 'forcesignal.drafts';
const snapshotBackupKey = 'forcesignal.snapshot-backup';
const fleetLibraryKey = 'forcesignal.fleet-library';

const defaultShipForm: ShipForm = {
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
  screenRating: 1,
  weapons: [{
    id: crypto.randomUUID(),
    name: 'Class-2 Beam',
    attackDice: 2,
    maxRange: 24,
    arc: 'Fore',
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

const shipIconOptions: { key: ShipIconKey; label: string }[] = [
  { key: 'escort', label: 'Escort' },
  { key: 'frigate', label: 'Frigate' },
  { key: 'destroyer', label: 'Destroyer' },
  { key: 'cruiser', label: 'Cruiser' },
  { key: 'carrier', label: 'Carrier' },
  { key: 'dreadnought', label: 'Dreadnought' },
  { key: 'fighter-group', label: 'Fighter group' },
  { key: 'station', label: 'Station' },
];

const fighterStatuses: FighterStatus[] = ['Docked', 'Airborne', 'Recovering'];

const shipPresets: { label: string; patch: Partial<ShipForm> }[] = [
  {
    label: 'Escort',
    patch: { className: 'Escort', iconKey: 'escort', thrustRating: 6, hullMax: 6, armorMax: 0, screenRating: 0, weapons: [weaponPreset('Class-1 Beam', 1, 12, 'Fore')] },
  },
  {
    label: 'Frigate',
    patch: { className: 'Frigate', iconKey: 'frigate', thrustRating: 5, hullMax: 8, armorMax: 1, screenRating: 0, weapons: [weaponPreset('Class-2 Beam', 2, 24, 'Fore')] },
  },
  {
    label: 'Destroyer',
    patch: { className: 'Destroyer', iconKey: 'destroyer', thrustRating: 4, hullMax: 10, armorMax: 2, screenRating: 1, weapons: [weaponPreset('Class-2 Beam', 2, 24, 'Fore')] },
  },
  {
    label: 'Cruiser',
    patch: { className: 'Cruiser', iconKey: 'cruiser', thrustRating: 4, hullMax: 12, armorMax: 4, screenRating: 1, weapons: [weaponPreset('Class-2 Beam', 2, 24, 'Fore'), weaponPreset('Class-1 Beam', 1, 12, 'All')] },
  },
  {
    label: 'Carrier',
    patch: { className: 'Carrier', iconKey: 'carrier', thrustRating: 4, hullMax: 14, armorMax: 5, screenRating: 1, weapons: [weaponPreset('Fighter Bay', 3, 12, 'All')] },
  },
  {
    label: 'Fighters',
    patch: { className: 'Fighter Group', iconKey: 'fighter-group', thrustRating: 6, currentVelocity: 12, hullMax: 6, armorMax: 0, screenRating: 0, weapons: [weaponPreset('Fighter Attack', 3, 6, 'All')], fighterEnduranceMax: 6, fighterEnduranceUsed: 0, fighterMaxRange: 24, fighterStatus: 'Docked' },
  },
  {
    label: 'Station',
    patch: { className: 'Station', iconKey: 'station', thrustRating: 0, currentVelocity: 0, hullMax: 18, armorMax: 6, screenRating: 2, weapons: [weaponPreset('Heavy Battery', 3, 30, 'All')] },
  },
];

function App() {
  const [displayName, setDisplayName] = useState('Admiral');
  const [joinCode, setJoinCode] = useState('');
  const [session, setSession] = useState<Session | null>(() => readJson(sessionKey));
  const [snapshot, setSnapshot] = useState<MatchSnapshot | null>(null);
  const [drafts, setDrafts] = useState<Record<string, DraftOrder>>(() => readJson(draftsKey) ?? {});
  const [firingDrafts, setFiringDrafts] = useState<Record<string, FiringDraft>>({});
  const [shipForm, setShipForm] = useState<ShipForm>(defaultShipForm);
  const [tableForm, setTableForm] = useState({ width: 72, depth: 48 });
  const [editingShipId, setEditingShipId] = useState<string | null>(null);
  const [activeFleetId, setActiveFleetId] = useState<string | null>(null);
  const [activeView, setActiveView] = useState<'ships' | 'map' | 'log'>('ships');
  const [mapFocusShipId, setMapFocusShipId] = useState<string | null>(null);
  const [shipCardMode, setShipCardMode] = useState<'helm' | 'fire' | 'damage'>('helm');
  const [publicMode, setPublicMode] = useState(false);
  const [damageUndo, setDamageUndo] = useState<{ shipId: string; shipName: string; before: DamageState } | null>(null);
  const [message, setMessage] = useState('Ready.');
  const [connectionState, setConnectionState] = useState<'live' | 'reconnecting' | 'offline'>('offline');
  const [pendingRestore, setPendingRestore] = useState<PendingRestore | null>(null);
  const [fleetLibrary, setFleetLibrary] = useState<SavedFleet[]>(() => readJson<SavedFleet[]>(fleetLibraryKey) ?? []);
  const [pointsLimitForm, setPointsLimitForm] = useState('0');
  const [newFleetForm, setNewFleetForm] = useState<{ name: string; faction: string; fleetColor: string } | null>(null);
  const fleetImportInputRef = useRef<HTMLInputElement | null>(null);
  const restoreInputRef = useRef<HTMLInputElement | null>(null);
  const spentDraftTurnRef = useRef<string | null>(null);

  useEffect(() => {
    localStorage.setItem(draftsKey, JSON.stringify(drafts));
  }, [drafts]);

  useEffect(() => {
    localStorage.setItem(fleetLibraryKey, JSON.stringify(fleetLibrary));
  }, [fleetLibrary]);

  useEffect(() => {
    if (snapshot) {
      setPointsLimitForm(String(snapshot.pointsLimit ?? 0));
    }
  }, [snapshot?.pointsLimit]);

  useEffect(() => {
    if (snapshot) {
      localStorage.setItem(snapshotBackupKey, JSON.stringify({ savedAt: new Date().toISOString(), snapshot }));
    }
  }, [snapshot]);

  // An order is spent once movement resolves. Drop the local drafts then, so the next turn
  // starts from a clean plot instead of previewing - or silently re-locking - last turn's helm.
  useEffect(() => {
    if (!snapshot || snapshot.phase !== 'Firing') {
      return;
    }

    const turnKey = `${snapshot.matchId}:${snapshot.turnNumber}`;
    if (spentDraftTurnRef.current === turnKey) {
      return;
    }

    spentDraftTurnRef.current = turnKey;
    setDrafts({});
  }, [snapshot?.matchId, snapshot?.turnNumber, snapshot?.phase]);

  useEffect(() => {
    if (!session) {
      return;
    }

    localStorage.setItem(sessionKey, JSON.stringify(session));
    loadSnapshot(session.matchId).catch(handleSessionError);

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${apiBaseUrl}/hubs/match`)
      .withAutomaticReconnect()
      .build();

    connection.on('MatchSnapshotChanged', (matchId: string, version: number, reason: string) => {
      if (matchId === session.matchId) {
        setMessage(`${reason} · v${version}`);
        loadSnapshot(session.matchId).catch(handleSessionError);
      }
    });

    const joinGroup = () => connection.invoke('JoinMatchGroup', session.matchId, session.participantToken);

    connection.onreconnecting(() => setConnectionState('reconnecting'));
    connection.onclose(() => setConnectionState('offline'));

    // Automatic reconnect creates a new connection id, so group membership has to be re-established
    // or the client silently stops receiving snapshot notifications after any network blip.
    connection.onreconnected(() => {
      setConnectionState('live');
      joinGroup().catch(showError(setMessage));
      // Resync independently of the rejoin: if the match is gone (for example the API restarted
      // and dropped in-memory state) this is what surfaces the expired session.
      loadSnapshot(session.matchId).catch(handleSessionError);
    });

    const started = connection
      .start()
      .then(() => {
        setConnectionState('live');
        return joinGroup();
      })
      .catch((error) => {
        setConnectionState('offline');
        showError(setMessage)(error);
      });

    return () => {
      setConnectionState('offline');
      // Stopping while start() is still in flight throws and can leave the connection
      // running, so wait for the handshake to settle before tearing it down.
      started.finally(() => connection.stop().catch(() => undefined));
    };
  }, [session?.matchId, session?.participantToken]);

  const me = snapshot?.participants.find((participant) => participant.id === session?.participantId);
  useEffect(() => {
    if (snapshot) {
      setTableForm({ width: snapshot.tableWidth, depth: snapshot.tableDepth });
    }
  }, [snapshot?.tableWidth, snapshot?.tableDepth]);
  const ownedFleets = useMemo(
    () => snapshot?.fleets.filter((fleet) => fleet.ownerParticipantId === session?.participantId) ?? [],
    [snapshot?.fleets, session?.participantId],
  );
  const ownedShips = useMemo(
    () => snapshot?.ships.filter((ship) => ownedFleets.some((fleet) => fleet.id === ship.fleetId)) ?? [],
    [snapshot?.ships, ownedFleets],
  );
  const activeFleet = ownedFleets.find((fleet) => fleet.id === activeFleetId) ?? ownedFleets[0];
  const activeFleetShipCount = ownedShips.filter((ship) => ship.fleetId === activeFleet?.id).length;
  const activeFleetPoints = shipsPoints(ownedShips.filter((ship) => ship.fleetId === activeFleet?.id));
  const ownedShipIds = useMemo(() => new Set(ownedShips.map((ship) => ship.id)), [ownedShips]);
  const visibleOwnedShipIds = useMemo(() => (publicMode ? new Set<string>() : ownedShipIds), [publicMode, ownedShipIds]);

  async function createMatch() {
    clearLocalMatchState();
    const response = await post<Session & { matchId: string; joinCode: string }>('/api/matches', {
      displayName,
      matchName: `${displayName}'s Match`,
    });
    setSession(response);
    setMessage(`Created room ${response.joinCode}.`);
  }

  async function joinMatch() {
    clearLocalMatchState();
    try {
      const response = await post<Session & { matchId: string; joinCode: string }>('/api/matches/join', {
        displayName,
        joinCode,
      });
      setSession(response);
      setMessage(`Joined room ${response.joinCode}.`);
    } catch (error) {
      // A restored room refuses ordinary joins until every seat is claimed.
      if (!(error instanceof ApiRequestError) || !error.message.toLowerCase().includes('claim')) {
        throw error;
      }

      const identity = await get<MatchIdentity>(`/api/matches/by-code/${encodeURIComponent(joinCode)}`);
      if (!identity.hasUnclaimedSeats) {
        // Someone took the last seat between the join attempt and this lookup.
        setMessage(`Every seat in ${identity.joinCode} has been claimed. Ask the host to restore the backup again if you need a seat.`);
        return;
      }

      setPendingRestore({
        matchId: identity.matchId,
        joinCode: identity.joinCode,
        seats: await get<MatchSeat[]>(`/api/matches/${identity.matchId}/seats`),
        note: 'This room was restored from a backup.',
      });
      setMessage('Claim the seat you were playing.');
    }
  }

  async function restoreFromBackupFile(file: File) {
    clearLocalMatchState();
    let backup: unknown;
    try {
      backup = JSON.parse(await file.text());
    } catch {
      throw new Error(`${file.name} is not readable as JSON. Pick a snapshot exported by ForceSignal.`);
    }

    const restored = await post<MatchRestored>('/api/matches/restore', backup);
    const codeNote = restored.reusedJoinCode ? '' : ' The old room code was taken, so this room has a new one.';
    setPendingRestore({
      matchId: restored.matchId,
      joinCode: restored.joinCode,
      seats: restored.seats,
      note: restored.lockedOrdersDropped
        ? `Restored at ${formatPhase(restored.restoredPhase)}. Locked orders could not be recovered - re-lock to continue.${codeNote}`
        : `Restored at ${formatPhase(restored.restoredPhase)}.${codeNote}`,
    });
    setMessage(`Restored into room ${restored.joinCode}. Claim your seat to take command.`);
  }

  async function claimSeat(restored: PendingRestore, seat: MatchSeat) {
    const claimed = await post<Session>(`/api/matches/${restored.matchId}/seats/${seat.participantId}/claim`, {
      displayName: seat.displayName,
    });
    setPendingRestore(null);
    setSession(claimed);
    setMessage(`Took command as ${seat.displayName}.`);
  }

  async function loadSnapshot(matchId: string) {
    setSnapshot(normalizeMatchSnapshot(await get<MatchSnapshot>(`/api/matches/${matchId}/snapshot`)));
  }

  async function createShipFromForm() {
    if (!session) {
      return;
    }

    const fleet = activeFleet ?? (await post<MatchSnapshot>(`/api/matches/${session.matchId}/fleets`, {
      participantToken: session.participantToken,
      name: shipForm.fleetName,
      faction: shipForm.faction,
      fleetColor: shipForm.fleetColor,
    })).fleets.find((item) => item.ownerParticipantId === session.participantId);
    if (!fleet) {
      throw new Error('Fleet was not returned.');
    }

    setActiveFleetId(fleet.id);
    const created = await post<MatchSnapshot>(`/api/fleets/${fleet.id}/ships`, {
      participantToken: session.participantToken,
      name: shipForm.name,
      className: shipForm.className,
      thrustRating: shipForm.thrustRating,
      initialVelocity: shipForm.currentVelocity,
      initialCourse: shipForm.currentCourse,
      startX: shipForm.positionX,
      startY: shipForm.positionY,
      hullMax: shipForm.hullMax,
      armorMax: shipForm.armorMax,
      screenRating: shipForm.screenRating,
      weapons: shipForm.weapons,
      iconKey: shipForm.iconKey,
      fighterEnduranceMax: shipForm.fighterEnduranceMax,
      fighterEnduranceUsed: shipForm.fighterEnduranceUsed,
      fighterMaxRange: shipForm.fighterMaxRange,
      fighterStatus: shipForm.fighterStatus,
      homeCarrierShipId: shipForm.homeCarrierShipId || null,
      pointsValue: shipForm.pointsValue,
    });
    setSnapshot(created);
    setShipForm((current) => ({
      ...current,
      name: nextShipName(current.name),
    }));
    setMessage('Ship added to fleet.');
  }

  function exportOwnedFleet(format: 'json' | 'csv') {
    const fleet = activeFleet;
    if (!fleet) {
      setMessage('Create a fleet before exporting.');
      return;
    }

    const exportData = toFleetExport(fleet, ownedShips.filter((ship) => ship.fleetId === fleet.id));
    if (exportData.ships.length === 0) {
      setMessage('Add at least one ship before exporting.');
      return;
    }

    const slug = slugify(exportData.name);
    if (format === 'json') {
      downloadText(`${slug}.forcesignal-fleet.json`, 'application/json', `${JSON.stringify(exportData, null, 2)}\n`);
    } else {
      downloadText(`${slug}.forcesignal-fleet.csv`, 'text/csv', fleetExportToCsv(exportData));
    }

    setMessage(`Exported ${exportData.name} as ${format.toUpperCase()}.`);
  }

  async function importFleetFile(file: File) {
    if (!session) {
      return;
    }

    const text = await file.text();
    const exportData = parseFleetExport(text, file.name, shipForm);
    await createFleetFromExport(exportData);
  }

  async function createFleetFromExport(exportData: FleetExport) {
    if (!session) {
      return;
    }

    if (exportData.ships.length === 0) {
      throw new Error('That fleet does not contain any ships.');
    }

    const knownFleetIds = new Set(snapshot?.fleets.map((fleet) => fleet.id) ?? []);
    const fleetSnapshot = await post<MatchSnapshot>(`/api/matches/${session.matchId}/fleets`, {
      participantToken: session.participantToken,
      name: exportData.name,
      faction: exportData.faction,
      fleetColor: exportData.fleetColor,
    });
    const fleet = fleetSnapshot.fleets.find((item) => !knownFleetIds.has(item.id) && item.ownerParticipantId === session.participantId);
    if (!fleet) {
      throw new Error('Imported fleet was not returned.');
    }

    let importedSnapshot = fleetSnapshot;
    const knownShipIds = new Set(snapshot?.ships.map((item) => item.id) ?? []);
    const importedIdsByName = new Map<string, string>();
    // Carriers first so fighter groups can be re-linked to the carrier created in this import.
    const orderedShips = [...exportData.ships].sort((left, right) => carrierImportRank(left) - carrierImportRank(right));
    for (const ship of orderedShips) {
      const carrierKey = (ship.homeCarrierName ?? '').trim().toLowerCase();
      importedSnapshot = await post<MatchSnapshot>(`/api/fleets/${fleet.id}/ships`, {
        participantToken: session.participantToken,
        name: ship.name,
        className: ship.className,
        thrustRating: ship.thrustRating,
        initialVelocity: ship.initialVelocity,
        initialCourse: ship.initialCourse,
        startX: ship.startX,
        startY: ship.startY,
        hullMax: ship.hullMax,
        armorMax: ship.armorMax,
        screenRating: ship.screenRating,
        weapons: ship.weapons,
        iconKey: ship.iconKey,
        fighterEnduranceMax: ship.fighterEnduranceMax,
        fighterEnduranceUsed: ship.fighterEnduranceUsed,
        fighterMaxRange: ship.fighterMaxRange,
        fighterStatus: ship.fighterStatus,
        homeCarrierShipId: (carrierKey ? importedIdsByName.get(carrierKey) : null) ?? null,
        pointsValue: ship.pointsValue,
      });
      for (const created of importedSnapshot.ships) {
        if (!knownShipIds.has(created.id)) {
          knownShipIds.add(created.id);
          importedIdsByName.set(created.name.trim().toLowerCase(), created.id);
        }
      }
    }

    setSnapshot(importedSnapshot);
    setActiveFleetId(fleet.id);
    setMessage(`Brought ${exportData.ships.length} ship${exportData.ships.length === 1 ? '' : 's'} (${fleetPoints(exportData)} pts) into ${exportData.name}.`);
  }

  async function markReady() {
    if (!session) {
      return;
    }

    setSnapshot(await post<MatchSnapshot>(
      `/api/matches/${session.matchId}/participants/me/ready`,
      { isReady: true },
      session.participantToken,
    ));
  }

  async function commit(ship: Ship) {
    if (!session) {
      return;
    }

    const draft = drafts[ship.id] ?? createDraftOrder();
    if (!drafts[ship.id]) {
      setDrafts((current) => ({ ...current, [ship.id]: draft }));
    }
    setSnapshot(await post<MatchSnapshot>(`/api/matches/${session.matchId}/turns/current/orders/commit`, {
      participantToken: session.participantToken,
      shipId: ship.id,
      order: toOrder(draft),
      salt: draft.salt,
    }));
  }

  async function reveal(ship: Ship) {
    if (!session) {
      return;
    }

    const draft = drafts[ship.id];
    if (!draft) {
      setMessage(`${ship.name} has no local order draft to reveal. Re-lock its order from this device first.`);
      return;
    }

    setSnapshot(await post<MatchSnapshot>(`/api/matches/${session.matchId}/turns/current/orders/reveal`, {
      participantToken: session.participantToken,
      shipId: ship.id,
      order: toOrder(draft),
      salt: draft.salt,
    }));
  }

  async function lockOwnedOrders() {
    if (!session || !snapshot) {
      return;
    }

    let latest = snapshot;
    const draftsToStore: Record<string, DraftOrder> = {};
    const unlockedShips = ownedShips.filter((ship) => (
      !ship.isDestroyed && !latest.orderStatuses.find((status) => status.shipId === ship.id)?.isCommitted
    ));
    for (const ship of unlockedShips) {
      const draft = drafts[ship.id] ?? createDraftOrder();
      draftsToStore[ship.id] = draft;
      latest = await post<MatchSnapshot>(`/api/matches/${session.matchId}/turns/current/orders/commit`, {
        participantToken: session.participantToken,
        shipId: ship.id,
        order: toOrder(draft),
        salt: draft.salt,
      });
    }

    if (Object.keys(draftsToStore).length > 0) {
      setDrafts((current) => ({ ...current, ...draftsToStore }));
    }
    setSnapshot(latest);
    setMessage(unlockedShips.length === 0 ? 'All friendly orders were already locked.' : `Locked ${unlockedShips.length} friendly orders.`);
  }

  async function revealOwnedOrders() {
    if (!session || !snapshot) {
      return;
    }

    let latest = snapshot;
    const revealableShips = ownedShips.filter((ship) => {
      const status = latest.orderStatuses.find((item) => item.shipId === ship.id);
      return status?.isCommitted && !status.isRevealed && drafts[ship.id];
    });
    const missingDraftShips = ownedShips.filter((ship) => {
      const status = latest.orderStatuses.find((item) => item.shipId === ship.id);
      return status?.isCommitted && !status.isRevealed && !drafts[ship.id];
    });

    for (const ship of revealableShips) {
      const draft = drafts[ship.id];
      latest = await post<MatchSnapshot>(`/api/matches/${session.matchId}/turns/current/orders/reveal`, {
        participantToken: session.participantToken,
        shipId: ship.id,
        order: toOrder(draft),
        salt: draft.salt,
      });
    }

    setSnapshot(latest);
    setMessage(missingDraftShips.length > 0
      ? `${missingDraftShips.length} locked friendly order${missingDraftShips.length === 1 ? '' : 's'} need the original local draft before reveal.`
      : revealableShips.length === 0 ? 'No friendly locked orders need reveal.' : `Revealed ${revealableShips.length} friendly orders.`);
  }

  async function updateDamage(ship: Ship, patch: Partial<DamageState>, trackUndo = true) {
    if (!session) {
      return;
    }

    if (trackUndo) {
      setDamageUndo({ shipId: ship.id, shipName: ship.name, before: captureDamageState(ship) });
    }

    setSnapshot(await post<MatchSnapshot>(`/api/ships/${ship.id}/damage`, {
      participantToken: session.participantToken,
      hullDamage: ship.hullDamage,
      armorDamage: ship.armorDamage,
      fireControlDamage: ship.fireControlDamage,
      driveDamage: ship.driveDamage,
      weaponDamage: ship.weaponDamage,
      ...patch,
    }));
  }

  async function undoLastDamage() {
    if (!damageUndo || !snapshot) {
      setMessage('No damage change to undo.');
      return;
    }

    const ship = snapshot.ships.find((item) => item.id === damageUndo.shipId);
    if (!ship) {
      setDamageUndo(null);
      setMessage('Undo target is no longer available.');
      return;
    }

    await updateDamage(ship, damageUndo.before, false);
    setMessage(`Restored ${damageUndo.shipName} damage record.`);
    setDamageUndo(null);
  }

  async function updateProfile(ship: Ship, form: ShipForm) {
    if (!session) {
      return;
    }

    setSnapshot(await post<MatchSnapshot>(`/api/ships/${ship.id}/profile`, {
      participantToken: session.participantToken,
      name: form.name,
      className: form.className,
      thrustRating: form.thrustRating,
      currentVelocity: form.currentVelocity,
      currentCourse: form.currentCourse,
      hullMax: form.hullMax,
      armorMax: form.armorMax,
      positionX: form.positionX,
      positionY: form.positionY,
      screenRating: form.screenRating,
      weapons: form.weapons,
      iconKey: form.iconKey,
      fighterEnduranceMax: form.fighterEnduranceMax,
      fighterEnduranceUsed: form.fighterEnduranceUsed,
      fighterMaxRange: form.fighterMaxRange,
      fighterStatus: form.fighterStatus,
      homeCarrierShipId: form.homeCarrierShipId || null,
      pointsValue: form.pointsValue,
    }));
    setEditingShipId(null);
    setMessage(`${form.name} updated.`);
  }

  async function duplicateShip(ship: Ship) {
    if (!session) {
      return;
    }

    setSnapshot(await post<MatchSnapshot>(`/api/ships/${ship.id}/duplicate`, {
      participantToken: session.participantToken,
      name: nextShipName(ship.name),
    }));
    setMessage(`${ship.name} duplicated.`);
  }

  async function repairAll(ship: Ship) {
    await updateDamage(ship, {
      hullDamage: 0,
      armorDamage: 0,
      fireControlDamage: 0,
      driveDamage: 0,
      weaponDamage: 0,
    });
  }

  async function updateFighterOperations(ship: Ship, patch: Partial<Pick<Ship, 'fighterStatus' | 'fighterEnduranceUsed' | 'fighterEnduranceMax' | 'fighterMaxRange' | 'homeCarrierShipId'>>) {
    if (!session) {
      return;
    }

    setSnapshot(await post<MatchSnapshot>(`/api/ships/${ship.id}/fighter-ops`, {
      participantToken: session.participantToken,
      fighterStatus: patch.fighterStatus ?? ship.fighterStatus,
      fighterEnduranceUsed: patch.fighterEnduranceUsed ?? ship.fighterEnduranceUsed,
      fighterEnduranceMax: patch.fighterEnduranceMax ?? ship.fighterEnduranceMax,
      fighterMaxRange: patch.fighterMaxRange ?? ship.fighterMaxRange,
      homeCarrierShipId: patch.homeCarrierShipId === undefined ? ship.homeCarrierShipId ?? null : patch.homeCarrierShipId || null,
    }));
    setMessage(`${ship.name} fighter ops updated.`);
  }

  async function createOrdnanceMarker(sourceShip: Ship, patch: Partial<OrdnanceMarker>) {
    if (!session || !snapshot) {
      return;
    }

    setSnapshot(await post<MatchSnapshot>(`/api/matches/${session.matchId}/ordnance`, {
      participantToken: session.participantToken,
      name: patch.name || `${sourceShip.name} Salvo`,
      markerType: patch.markerType || 'Missile',
      sourceShipId: sourceShip.id,
      targetShipId: patch.targetShipId || null,
      positionX: patch.positionX ?? sourceShip.positionX,
      positionY: patch.positionY ?? sourceShip.positionY,
      course: patch.course ?? sourceShip.currentCourse,
      speed: patch.speed ?? Math.max(6, sourceShip.currentVelocity),
      enduranceRemaining: patch.enduranceRemaining ?? 1,
      attackDice: patch.attackDice ?? 2,
      maxRange: patch.maxRange ?? 24,
      status: patch.status ?? 'Active',
    }));
    setMessage(`${sourceShip.name} launched an ordnance marker.`);
  }

  async function updateOrdnanceMarker(marker: OrdnanceMarker, patch: Partial<OrdnanceMarker>) {
    if (!session) {
      return;
    }

    setSnapshot(await post<MatchSnapshot>(`/api/ordnance/${marker.id}`, {
      participantToken: session.participantToken,
      name: patch.name ?? marker.name,
      markerType: patch.markerType ?? marker.markerType,
      targetShipId: patch.targetShipId === undefined ? marker.targetShipId ?? null : patch.targetShipId || null,
      positionX: patch.positionX ?? marker.positionX,
      positionY: patch.positionY ?? marker.positionY,
      course: patch.course ?? marker.course,
      speed: patch.speed ?? marker.speed,
      enduranceRemaining: patch.enduranceRemaining ?? marker.enduranceRemaining,
      attackDice: patch.attackDice ?? marker.attackDice,
      maxRange: patch.maxRange ?? marker.maxRange,
      status: patch.status ?? marker.status,
    }));
    setMessage(`${marker.name} marker updated.`);
  }

  async function removeOrdnanceMarker(marker: OrdnanceMarker) {
    if (!session) {
      return;
    }

    setSnapshot(await post<MatchSnapshot>(`/api/ordnance/${marker.id}/remove`, {
      participantToken: session.participantToken,
    }));
    setMessage(`${marker.name} marker removed.`);
  }

  async function fireWeapon(ship: Ship, draft: FiringDraft) {
    if (!session) {
      return;
    }

    const fired = await post<MatchSnapshot>(`/api/matches/${session.matchId}/turns/current/fire`, {
      participantToken: session.participantToken,
      attackerShipId: ship.id,
      targetShipId: draft.targetShipId,
      weaponId: draft.weaponId,
      range: draft.range,
      arc: draft.arc,
    });
    setSnapshot(fired);
    const target = fired.ships.find((item) => item.id === draft.targetShipId);
    setMessage(`${ship.name} fired at ${target?.name ?? 'target'} at range ${draft.range}.`);
  }

  async function updatePointsLimit() {
    if (!session) {
      return;
    }

    const limit = Math.max(0, Math.min(99999, Math.round(Number(pointsLimitForm) || 0)));
    setSnapshot(await post<MatchSnapshot>(`/api/matches/${session.matchId}/points-limit`, {
      participantToken: session.participantToken,
      pointsLimit: limit,
    }));
    setMessage(limit === 0 ? 'Points limit cleared.' : `Points limit set to ${limit} per player.`);
  }

  async function createAdditionalFleet() {
    if (!session || !newFleetForm) {
      return;
    }

    const knownFleetIds = new Set(snapshot?.fleets.map((fleet) => fleet.id) ?? []);
    const created = await post<MatchSnapshot>(`/api/matches/${session.matchId}/fleets`, {
      participantToken: session.participantToken,
      name: newFleetForm.name,
      faction: newFleetForm.faction,
      fleetColor: newFleetForm.fleetColor,
    });
    const fleet = created.fleets.find((item) => !knownFleetIds.has(item.id) && item.ownerParticipantId === session.participantId);
    if (!fleet) {
      throw new Error('New fleet was not returned.');
    }

    setSnapshot(created);
    setActiveFleetId(fleet.id);
    setNewFleetForm(null);
    setMessage(`${fleet.name} created. Ships you add now join this fleet.`);
  }

  function saveActiveFleetToLibrary() {
    if (!activeFleet) {
      setMessage('Create a fleet before saving it to the library.');
      return;
    }

    const exportData = toFleetExport(activeFleet, ownedShips.filter((ship) => ship.fleetId === activeFleet.id));
    if (exportData.ships.length === 0) {
      setMessage('Add at least one ship before saving this fleet.');
      return;
    }

    setFleetLibrary((current) => [
      { savedAt: new Date().toISOString(), fleet: exportData },
      ...current.filter((entry) => entry.fleet.name.toLowerCase() !== exportData.name.toLowerCase()),
    ]);
    setMessage(`Saved ${exportData.name} (${fleetPoints(exportData)} pts) to this device's library.`);
  }

  async function bringLibraryFleet(entry: SavedFleet) {
    await createFleetFromExport(entry.fleet);
  }

  function removeLibraryFleet(entry: SavedFleet) {
    setFleetLibrary((current) => current.filter((item) => item !== entry));
    setMessage(`Removed ${entry.fleet.name} from the library.`);
  }

  async function updateTable() {
    if (!session) {
      return;
    }

    setSnapshot(await post<MatchSnapshot>(`/api/matches/${session.matchId}/table`, {
      participantToken: session.participantToken,
      tableWidth: tableForm.width,
      tableDepth: tableForm.depth,
    }));
    setMessage(`Table set to ${tableForm.width} x ${tableForm.depth}.`);
  }

  function exportAfterAction(format: 'json' | 'csv' | 'md') {
    if (!snapshot) {
      return;
    }

    const baseName = `${slugify(snapshot.name)}-turn-${snapshot.turnNumber}-aar`;
    if (format === 'json') {
      downloadText(`${baseName}.json`, 'application/json', `${JSON.stringify(snapshot, null, 2)}\n`);
      setMessage('After-action report exported as JSON.');
      return;
    }

    if (format === 'csv') {
      downloadText(`${baseName}.csv`, 'text/csv', matchLogToCsv(snapshot));
      setMessage('After-action report exported as CSV.');
      return;
    }

    downloadText(`${baseName}.md`, 'text/markdown', matchLogToMarkdown(snapshot));
    setMessage('After-action report exported as Markdown.');
  }

  function exportSnapshotBackup() {
    if (!snapshot) {
      setMessage('No match snapshot to save.');
      return;
    }

    downloadText(`${slugify(snapshot.name)}-turn-${snapshot.turnNumber}-snapshot.json`, 'application/json', `${JSON.stringify({ savedAt: new Date().toISOString(), snapshot }, null, 2)}\n`);
    setMessage('Local match snapshot exported.');
  }

  function exportLastSnapshotBackup() {
    const backup = localStorage.getItem(snapshotBackupKey);
    if (!backup) {
      setMessage('No local snapshot backup found on this device.');
      return;
    }

    const parsedBackup = readJson<{ savedAt?: string; snapshot?: MatchSnapshot }>(snapshotBackupKey);
    const matchName = parsedBackup?.snapshot?.name ? slugify(parsedBackup.snapshot.name) : 'forcesignal';
    const turnNumber = parsedBackup?.snapshot?.turnNumber ?? 'last';
    downloadText(`${matchName}-turn-${turnNumber}-device-backup.json`, 'application/json', `${backup}\n`);
    setMessage('Last local device snapshot exported.');
  }

  function printShipCards() {
    setActiveView('ships');
    window.setTimeout(() => window.print(), 50);
  }

  function applyOrderToOwnedFleet(sourceShipId: string) {
    const sourceDraft = draftFor(sourceShipId, drafts);
    setDrafts((current) => {
      const next = { ...current };
      for (const ship of ownedShips) {
        next[ship.id] = {
          ...sourceDraft,
          salt: draftFor(ship.id, current).salt,
        };
      }

      return next;
    });
    setMessage('Order copied to your fleet.');
  }

  async function advanceTurn() {
    if (!session) {
      return;
    }

    setSnapshot(await post<MatchSnapshot>(
      `/api/matches/${session.matchId}/turns/current/advance`,
      {},
      session.participantToken,
    ));
  }

  function clearSession() {
    clearLocalMatchState();
    setSession(null);
    setSnapshot(null);
  }

  function clearLocalMatchState() {
    localStorage.removeItem(sessionKey);
    localStorage.removeItem(draftsKey);
    setDrafts({});
    setFiringDrafts({});
    setEditingShipId(null);
    setActiveFleetId(null);
  }

  function handleSessionError(error: unknown) {
    if (error instanceof ApiRequestError && error.status === 404) {
      clearSession();
      setMessage('Match session expired. Create or join a room again.');
      return;
    }

    showError(setMessage)(error);
  }

  return (
    <main className={publicMode ? 'shell public-mode' : 'shell'}>
      <section className="topbar">
        <div>
          <h1>ForceSignal</h1>
          <p>Space fleet tabletop companion for synchronized orders and battle records.</p>
        </div>
        <div className="topbar-status">
          <strong>
            {snapshot
              ? `${formatPhase(snapshot.phase)} · Turn ${snapshot.turnNumber}${snapshot.tableWidth ? ` · ${snapshot.tableWidth}x${snapshot.tableDepth}` : ''}`
              : 'No match'}
          </strong>
          {session ? (
            <span className={`link-state ${connectionState}`} title={`Realtime link ${connectionState}`}>
              {connectionState === 'live' ? 'Link live' : connectionState === 'reconnecting' ? 'Reconnecting' : 'Link lost'}
            </span>
          ) : null}
          {publicMode ? (
            // The side panel that owns the toggle is hidden in public mode, so the exit lives here.
            <button className="ghost" type="button" onClick={() => setPublicMode(false)}>Exit Public Display</button>
          ) : null}
        </div>
      </section>

      <section className="legal-notice" aria-label="Unofficial companion notice">
        <div>
          <strong>Unofficial companion</strong>
          <p>ForceSignal is an unofficial tabletop companion. It is not affiliated with, endorsed by, or sponsored by Ground Zero Games.</p>
          <p>Full Thrust and Ground Zero Games are trademarks/property of their respective owners. Use your own legally obtained rules and fleet data.</p>
        </div>
        <a href={officialRulesUrl} target="_blank" rel="noreferrer">Rules</a>
      </section>

      {!session && pendingRestore ? (
        <section className="panel seat-picker" aria-label="Claim a seat">
          <div>
            <span className="label">Restored room</span>
            <h2>{pendingRestore.joinCode}</h2>
            <p className="privacy">{pendingRestore.note} Pick the admiral you were playing - fleets follow the seat.</p>
          </div>
          <p className="auth-status">{message}</p>
          {pendingRestore.seats.map((seat) => (
            <button
              key={seat.participantId}
              type="button"
              className={seat.isClaimed ? 'ghost' : undefined}
              disabled={seat.isClaimed}
              onClick={() => claimSeat(pendingRestore, seat).catch(showError(setMessage))}
            >
              {seat.displayName} · {seat.role} · {seat.fleetCount} fleet{seat.fleetCount === 1 ? '' : 's'}, {seat.shipCount} ship{seat.shipCount === 1 ? '' : 's'}{seat.isClaimed ? ' · taken' : ''}
            </button>
          ))}
          <button className="ghost" type="button" onClick={() => setPendingRestore(null)}>Cancel</button>
        </section>
      ) : !session ? (
        <section className="panel auth-grid" aria-label="Create or join match">
          <label>
            Display name
            <input value={displayName} onChange={(event) => setDisplayName(event.target.value)} />
          </label>
          <button onClick={() => createMatch().catch(showError(setMessage))}>Create Match</button>
          <label>
            Room code
            <input value={joinCode} onChange={(event) => setJoinCode(event.target.value.toUpperCase())} />
          </label>
          <button onClick={() => joinMatch().catch(showError(setMessage))}>Join Match</button>
          <p className="auth-status auth-wide">{message}</p>
          <button className="ghost auth-wide" type="button" onClick={exportLastSnapshotBackup}>Export Last Device Backup</button>
          <button className="ghost auth-wide" type="button" onClick={() => restoreInputRef.current?.click()}>Restore Match From Backup</button>
          <input
            ref={restoreInputRef}
            className="file-input"
            type="file"
            accept=".json,application/json"
            onChange={(event) => {
              const file = event.target.files?.[0];
              event.target.value = '';
              if (file) {
                restoreFromBackupFile(file).catch(showError(setMessage));
              }
            }}
          />
        </section>
      ) : (
        <div className="workspace">
          <aside className="panel side">
            <div>
              <span className="label">Room</span>
              <h2>{snapshot?.joinCode ?? session.joinCode}</h2>
            </div>
            <div>
              <span className="label">You</span>
              <p>{me?.displayName ?? displayName} · {me?.role ?? 'Player'}</p>
            </div>
            <div className="table-setup">
              <span className="label">Table</span>
              <div className="table-fields">
                <label>
                  Width
                  <input type="number" min="24" max="144" value={tableForm.width} onChange={(event) => setTableForm({ ...tableForm, width: Number(event.target.value) })} />
                </label>
                <label>
                  Depth
                  <input type="number" min="24" max="96" value={tableForm.depth} onChange={(event) => setTableForm({ ...tableForm, depth: Number(event.target.value) })} />
                </label>
              </div>
              <button className="ghost" onClick={() => updateTable().catch(showError(setMessage))}>Set Table</button>
            </div>
            <div className="table-setup">
              <span className="label">Points per player</span>
              <div className="table-fields">
                <label>
                  Limit
                  <input
                    type="number"
                    min="0"
                    max="99999"
                    value={pointsLimitForm}
                    onChange={(event) => setPointsLimitForm(event.target.value)}
                  />
                </label>
                <div className="points-readout">
                  <strong>{shipsPoints(ownedShips)}</strong>
                  <small>{(snapshot?.pointsLimit ?? 0) > 0 ? `of ${snapshot?.pointsLimit}` : 'no limit'}</small>
                </div>
              </div>
              <button className="ghost" onClick={() => updatePointsLimit().catch(showError(setMessage))}>Set Limit</button>
              <p className="privacy">0 means unlimited. Only the owner can change it, which is how both sides agree to a mismatch.</p>
            </div>
            <div className="side-actions">
              <span className="label">Match commands</span>
              <button onClick={() => markReady().catch(showError(setMessage))}>Ready</button>
              <button className="ghost" onClick={() => lockOwnedOrders().catch(showError(setMessage))}>Lock Fleet Orders</button>
              <button className="ghost" onClick={() => revealOwnedOrders().catch(showError(setMessage))}>Reveal Fleet Orders</button>
              <button onClick={() => advanceTurn().catch(showError(setMessage))}>Advance Turn</button>
              <button className={publicMode ? 'ghost active' : 'ghost'} onClick={() => {
                setPublicMode((current) => !current);
                setActiveView('map');
              }}>Public Display</button>
              <button className="ghost" onClick={printShipCards}>Print Cards</button>
              <button className="ghost" onClick={exportSnapshotBackup}>Save Snapshot</button>
              <button className="ghost" onClick={clearSession}>Leave Device Session</button>
            </div>
          </aside>

          <section className="panel board" aria-label="Match console">
            <div className="section-head">
              <div>
                <span className="label">Profile</span>
                <h2>{formatRulesProfile(snapshot?.rulesProfileKey)}</h2>
                <small className="profile-note">Bring your own legally obtained rules. No official rules text or fleet lists are bundled.</small>
              </div>
              {snapshot ? (
                <div className="match-counters" aria-label="Match counters">
                  <span>{snapshot.ships.length} {snapshot.ships.length === 1 ? 'ship' : 'ships'}</span>
                  <span>{snapshot.orderStatuses.filter((status) => status.isCommitted).length}/{snapshot.ships.length} locked</span>
                  <span>{snapshot.movementResults.length} resolved</span>
                </div>
              ) : null}
              <p>{message}</p>
            </div>

            {snapshot?.phase === 'FleetSetup' && activeView === 'ships' ? (
              <section className="setup-workflow" aria-label="Fleet setup workflow">
                <div className="setup-title">
                  <div>
                    <span className="label">Fleet setup</span>
                    <h3>{activeFleet?.name ?? shipForm.fleetName}</h3>
                  </div>
                  <p>Add ships, tune stats, and move fleet files before marking ready.</p>
                  <div className="setup-fleet-controls">
                    {ownedFleets.length > 1 ? (
                      <label className="active-fleet-select">
                        Active fleet
                        <select value={activeFleet?.id ?? ''} onChange={(event) => setActiveFleetId(event.target.value || null)}>
                          {ownedFleets.map((fleet) => <option key={fleet.id} value={fleet.id}>{fleet.name}</option>)}
                        </select>
                      </label>
                    ) : null}
                    {ownedFleets.length > 0 ? (
                      <button
                        className="ghost"
                        type="button"
                        onClick={() => setNewFleetForm(newFleetForm
                          ? null
                          : { name: `Fleet ${ownedFleets.length + 1}`, faction: shipForm.faction, fleetColor: shipForm.fleetColor })}
                      >
                        {newFleetForm ? 'Cancel New Fleet' : 'New Fleet'}
                      </button>
                    ) : null}
                  </div>
                </div>
                {newFleetForm ? (
                  <div className="fleet-identity new-fleet">
                    <label>
                      Fleet name
                      <input value={newFleetForm.name} onChange={(event) => setNewFleetForm({ ...newFleetForm, name: event.target.value })} />
                    </label>
                    <label>
                      Faction
                      <input value={newFleetForm.faction} onChange={(event) => setNewFleetForm({ ...newFleetForm, faction: event.target.value })} />
                    </label>
                    <label>
                      Fleet color
                      <input type="color" value={newFleetForm.fleetColor} onChange={(event) => setNewFleetForm({ ...newFleetForm, fleetColor: event.target.value })} />
                    </label>
                    <button type="button" onClick={() => createAdditionalFleet().catch(showError(setMessage))}>Create Fleet</button>
                  </div>
                ) : null}
                <div className="setup-grid">
                  <div className="shipyard">
                    <span className="label">Add ship</span>
                    {ownedFleets.length === 0 ? (
                      <div className="fleet-identity">
                        <label>
                          Fleet name
                          <input value={shipForm.fleetName} onChange={(event) => setShipForm({ ...shipForm, fleetName: event.target.value })} />
                        </label>
                        <label>
                          Faction
                          <input value={shipForm.faction} onChange={(event) => setShipForm({ ...shipForm, faction: event.target.value })} />
                        </label>
                        <label>
                          Fleet color
                          <input type="color" value={shipForm.fleetColor} onChange={(event) => setShipForm({ ...shipForm, fleetColor: event.target.value })} />
                        </label>
                      </div>
                    ) : null}
                    <ShipProfileFields form={shipForm} onChange={setShipForm} />
                    <button onClick={() => createShipFromForm().catch(showError(setMessage))}>Add Ship</button>
                  </div>
                  <div className="fleet-transfer">
                    <span className="label">Fleet transfer</span>
                    <div className="transfer-summary">
                      <strong>{activeFleetShipCount}</strong>
                      <span>{activeFleetShipCount === 1 ? 'ship' : 'ships'} in {activeFleet?.name ?? 'this fleet'} - {activeFleetPoints} pts{(snapshot?.pointsLimit ?? 0) > 0 ? ` of ${snapshot?.pointsLimit}` : ''}</span>
                    </div>
                    <div className="quick-actions">
                      <button className="ghost" onClick={() => exportOwnedFleet('json')}>Export JSON</button>
                      <button className="ghost" onClick={() => exportOwnedFleet('csv')}>Export CSV</button>
                    </div>
                    <button className="ghost" onClick={saveActiveFleetToLibrary}>Save Fleet To Library</button>
                    <button onClick={() => fleetImportInputRef.current?.click()}>Import Fleet</button>
                    <input
                      ref={fleetImportInputRef}
                      className="file-input"
                      type="file"
                      accept=".json,.csv,application/json,text/csv"
                      onChange={(event) => {
                        const file = event.target.files?.[0];
                        event.target.value = '';
                        if (file) {
                          importFleetFile(file).catch(showError(setMessage));
                        }
                      }}
                    />
                    <p className="privacy">Imports are for user-owned fleet data. Do not bundle official fleet lists, SSDs, logos, artwork, or copied rule text.</p>
                    {fleetLibrary.length > 0 ? (
                      <div className="fleet-library">
                        <span className="label">Fleet library - this device</span>
                        {fleetLibrary.map((entry) => {
                          const points = fleetPoints(entry.fleet);
                          const limit = snapshot?.pointsLimit ?? 0;
                          return (
                            <div className="fleet-library-row" key={`${entry.fleet.name}-${entry.savedAt}`}>
                              <div>
                                <strong>{entry.fleet.name}</strong>
                                <small>
                                  {entry.fleet.ships.length} {entry.fleet.ships.length === 1 ? 'ship' : 'ships'} - {points} pts
                                  {limit > 0 && points > limit ? ` - ${points - limit} over` : ''}
                                </small>
                              </div>
                              <div className="quick-actions">
                                <button type="button" onClick={() => bringLibraryFleet(entry).catch(showError(setMessage))}>Bring</button>
                                <button className="ghost" type="button" onClick={() => removeLibraryFleet(entry)}>Remove</button>
                              </div>
                            </div>
                          );
                        })}
                      </div>
                    ) : (
                      <p className="privacy">Save a fleet to build a device library you can bring to any future match.</p>
                    )}
                  </div>
                </div>
              </section>
            ) : null}

            {activeView === 'ships' ? (
            <div className="columns">
              <div>
                <h3>Participants</h3>
                <ul className="list">
                  {snapshot?.participants.map((participant) => (
                    <li key={participant.id}>
                      <span>{participant.displayName}</span>
                      <small>{participant.role} · {participant.isReady ? 'Ready' : 'Setting up'} · {participant.isConnected ? 'Online' : 'Offline'}</small>
                    </li>
                  ))}
                </ul>
              </div>
              <div>
                <h3>Fleets</h3>
                <ul className="list">
                  {snapshot?.fleets.map((fleet) => (
                    <li key={fleet.id}>
                      <span>{fleet.name}</span>
                      <small>{fleet.faction ?? 'No faction'}</small>
                    </li>
                  ))}
                </ul>
              </div>
            </div>
            ) : null}

            <nav className="view-tabs" aria-label="Battle views">
              <button className={activeView === 'ships' ? 'ghost active' : 'ghost'} onClick={() => setActiveView('ships')}>Ships</button>
              <button className={activeView === 'map' ? 'ghost active' : 'ghost'} onClick={() => setActiveView('map')}>Play Map</button>
              <button className={activeView === 'log' ? 'ghost active' : 'ghost'} onClick={() => setActiveView('log')}>Log</button>
            </nav>

            {snapshot ? (
              <PreTurnChecklist
                snapshot={snapshot}
                ownedShipIds={visibleOwnedShipIds}
                damageUndoLabel={damageUndo?.shipName}
                onUndoDamage={() => undoLastDamage().catch(showError(setMessage))}
              />
            ) : null}

            {activeView === 'ships' && snapshot ? (
            <div className="ship-grid">
              {focusedFirstShips(snapshot.ships, mapFocusShipId, ownedShips[0]?.id).map((ship) => {
                const draft = draftFor(ship.id, drafts);
                const status = snapshot.orderStatuses.find((item) => item.shipId === ship.id);
                const result = snapshot.movementResults.find((item) => item.shipId === ship.id);
                // Public display shows every hull as a read-only record: no command controls.
                const canEdit = visibleOwnedShipIds.has(ship.id);
                const isEditing = editingShipId === ship.id;
                const isFocused = mapFocusShipId === ship.id || (!mapFocusShipId && canEdit && ownedShips[0]?.id === ship.id);
                const showShipControls = canEdit && (isFocused || isEditing);
                const shipThrust = usableThrust(ship);
                const maxTurn = maxLegalTurn(shipThrust, draft.velocityDelta);
                const firingDraft = firingDraftFor(ship, snapshot.ships, firingDrafts, ownedShipIds);
                return (
                  <article className={['ship-card', isFocused ? 'focused' : '', showShipControls ? '' : 'compact'].join(' ')} key={ship.id}>
                    <div className="ship-header">
                      <div>
                        <span className="label">Ship record</span>
                        <h3>{ship.name}</h3>
                        <small>{ship.className ?? 'Unclassified'} · Thrust {ship.thrustRating}</small>
                      </div>
                      <strong className={ship.isDestroyed ? 'ship-state destroyed' : 'ship-state'}>
                        {ship.isDestroyed ? 'Destroyed' : 'Operational'}
                      </strong>
                    </div>
                    <div className="ship-alerts">
                      {status?.isCommitted ? <span>Orders locked</span> : <span className="warn">Awaiting orders</span>}
                      {ship.hullDamage > 0 || ship.armorDamage > 0 ? <span className="warn">Damage recorded</span> : <span>Undamaged</span>}
                      {ship.fireControlDamage + ship.driveDamage + ship.weaponDamage > 0 ? <span className="danger">Systems degraded</span> : null}
                      {ship.screenRating > 0 ? <span>Screens {ship.screenRating}</span> : null}
                    </div>

                    {canEdit ? (
                      <div className="card-actions">
                        <button className={isFocused ? 'ghost active' : 'ghost'} onClick={() => setMapFocusShipId(ship.id)}>
                          {isFocused ? 'Focused' : 'Focus'}
                        </button>
                        <button
                          className="ghost"
                          onClick={() => {
                            // Editing also selects, so the selected ship stays the only active planning subject.
                            setMapFocusShipId(ship.id);
                            setEditingShipId(isEditing ? null : ship.id);
                          }}
                        >
                          {isEditing ? 'Close Editor' : 'Edit Stats'}
                        </button>
                        {showShipControls ? <button className="ghost" onClick={() => duplicateShip(ship).catch(showError(setMessage))}>Duplicate</button> : null}
                        {showShipControls ? <button className="ghost" onClick={() => repairAll(ship).catch(showError(setMessage))}>Repair All</button> : null}
                      </div>
                    ) : null}

                    {canEdit && isEditing ? (
                      <ShipEditor
                        ship={ship}
                        onSave={(form) => updateProfile(ship, form).catch(showError(setMessage))}
                        onCancel={() => setEditingShipId(null)}
                      />
                    ) : null}

                    <div className="ship-readouts">
                      <div>
                        <span className="label">Velocity</span>
                        <strong>{ship.currentVelocity}</strong>
                      </div>
                      <div>
                        <span className="label">Course</span>
                        <strong>{ship.currentCourse}</strong>
                      </div>
                      <div>
                        <span className="label">Next</span>
                        <strong>{previewCourse(ship.currentCourse, draft)}</strong>
                      </div>
                      <div>
                        <span className="label">Screens</span>
                        <strong>{ship.screenRating}</strong>
                      </div>
                      <div>
                        <span className="label">Pos</span>
                        <strong>{ship.positionX.toFixed(0)},{ship.positionY.toFixed(0)}</strong>
                      </div>
                    </div>

                    {showShipControls ? (
                      <div className="selected-action-tabs ship-card-tabs" aria-label={`${ship.name} card tools`}>
                        <button className={shipCardMode === 'helm' ? 'ghost active' : 'ghost'} type="button" onClick={() => setShipCardMode('helm')}>Helm</button>
                        <button className={shipCardMode === 'fire' ? 'ghost active' : 'ghost'} type="button" onClick={() => setShipCardMode('fire')}>Fire</button>
                        <button className={shipCardMode === 'damage' ? 'ghost active' : 'ghost'} type="button" onClick={() => setShipCardMode('damage')}>Damage</button>
                      </div>
                    ) : null}

                    {showShipControls && shipCardMode === 'helm' ? (
                      <section className="card-module" aria-label={`${ship.name} helm`}>
                        <span className="label">Helm</span>
                        <CourseCompass
                          currentCourse={ship.currentCourse}
                          thrustRating={shipThrust}
                          draft={draft}
                          canEdit={canEdit}
                          onDraftChange={(patch) => updateDraft(ship.id, patch)}
                        />
                      </section>
                    ) : null}

                    {showShipControls && shipCardMode === 'helm' ? (
                      <>
                        <div className="order-form card-module">
                          <span className="label module-title">Order plot</span>
                          <p className="constraint-line">
                            Thrust spend {Math.abs(draft.velocityDelta) + totalTurnSteps(draft)}/{shipThrust} · turn cap {maxTurn}{ship.driveDamage > 0 ? ` · drive hits -${ship.driveDamage}` : ''}
                          </p>
                          <div className="quick-actions">
                            <button className="ghost" type="button" onClick={() => updateDraft(ship.id, resetOrderDraft(draft))}>Drift</button>
                            <button className="ghost" type="button" onClick={() => updateDraft(ship.id, clampDraftForShip(shipThrust, { ...draft, velocityDelta: shipThrust }))}>Max Accel</button>
                            <button className="ghost" type="button" onClick={() => updateDraft(ship.id, clampDraftForShip(shipThrust, { ...draft, velocityDelta: -shipThrust }))}>Max Decel</button>
                            <button className="ghost" type="button" onClick={() => applyOrderToOwnedFleet(ship.id)}>Copy Fleet</button>
                          </div>
                          <label>
                            Velocity
                            <input
                              type="number"
                              value={draft.velocityDelta}
                              onChange={(event) => updateDraft(ship.id, clampDraftForShip(shipThrust, {
                                ...draft,
                                velocityDelta: Number(event.target.value),
                              }))}
                            />
                          </label>
                          <label>
                            Turn
                            <input
                              type="number"
                              min="0"
                              max={maxTurn}
                              value={totalTurnSteps(draft)}
                              onChange={(event) => updateDraft(ship.id, clampDraftForShip(shipThrust, {
                                ...draft,
                                turnSteps: Number(event.target.value),
                                turnDirection: draft.turnDirection === 'None' ? 'Starboard' : draft.turnDirection,
                                turnManeuvers: Number(event.target.value) > 0
                                  ? [{ direction: draft.turnDirection === 'None' ? 'Starboard' : draft.turnDirection, steps: Number(event.target.value) }]
                                  : [],
                              }))}
                            />
                          </label>
                          <label>
                            Direction
                            <select
                              value={draft.turnDirection}
                              onChange={(event) => updateDraft(ship.id, clampDraftForShip(shipThrust, {
                                ...draft,
                                turnDirection: event.target.value as TurnDirection,
                                turnManeuvers: event.target.value === 'None' || totalTurnSteps(draft) === 0
                                  ? []
                                  : [{ direction: event.target.value as Exclude<TurnDirection, 'None'>, steps: totalTurnSteps(draft) }],
                              }))}
                            >
                              <option>None</option>
                              <option>Port</option>
                              <option>Starboard</option>
                            </select>
                          </label>
                          <div className="turn-sequence" aria-label={`${ship.name} ordered turn sequence`}>
                            <div>
                              <span className="label">Sequence</span>
                              <strong>{formatTurnSequence(draft)}</strong>
                            </div>
                            <div className="turn-chips">
                              {turnManeuversForDraft(draft).map((maneuver, index) => (
                                <button
                                  key={`${maneuver.direction}-${index}`}
                                  className="turn-chip"
                                  type="button"
                                  onClick={() => updateDraft(ship.id, turnPatchFromManeuvers(turnManeuversForDraft(draft).filter((_, itemIndex) => itemIndex !== index)))}
                                  title="Remove this turn"
                                >
                                  {maneuver.direction === 'Port' ? 'P' : 'S'}{maneuver.steps}
                                </button>
                              ))}
                            </div>
                            <div className="quick-actions">
                              <button
                                className="ghost"
                                type="button"
                                disabled={totalTurnSteps(draft) >= maxTurn}
                                onClick={() => updateDraft(ship.id, turnPatchFromManeuvers([...turnManeuversForDraft(draft), { direction: 'Port', steps: 1 }]))}
                              >
                                Port +1
                              </button>
                              <button
                                className="ghost"
                                type="button"
                                disabled={totalTurnSteps(draft) >= maxTurn}
                                onClick={() => updateDraft(ship.id, turnPatchFromManeuvers([...turnManeuversForDraft(draft), { direction: 'Starboard', steps: 1 }]))}
                              >
                                Starboard +1
                              </button>
                              <button className="ghost" type="button" onClick={() => updateDraft(ship.id, turnPatchFromManeuvers([]))}>Clear Turns</button>
                            </div>
                          </div>
                          <button onClick={() => commit(ship).catch(showError(setMessage))}>Lock</button>
                          <button onClick={() => reveal(ship).catch(showError(setMessage))}>Reveal</button>
                        </div>
                      </>
                    ) : null}

                    {showShipControls && shipCardMode === 'fire' ? (
                      <>
                        <FiringConsole
                          ship={ship}
                          ships={snapshot.ships}
                          ownedShipIds={ownedShipIds}
                          draft={firingDraft}
                          phase={snapshot.phase}
                          firingResults={snapshot.firingResults}
                          onChange={(patch) => updateFiringDraft(ship.id, { ...firingDraft, ...patch })}
                          onFire={() => fireWeapon(ship, firingDraft).catch(showError(setMessage))}
                        />
                      </>
                    ) : null}

                    {showShipControls && shipCardMode === 'damage' ? (
                      <>
                        <div className="damage-grid card-module" aria-label={`${ship.name} damage controls`}>
                          <span className="label module-title">Damage control</span>
                          <DamageControl
                            label="Hull"
                            value={ship.hullDamage}
                            max={ship.hullMax}
                            onChange={(value) => updateDamage(ship, { hullDamage: value }).catch(showError(setMessage))}
                          />
                          <DamageControl
                            label="Armor"
                            value={ship.armorDamage}
                            max={ship.armorMax}
                            onChange={(value) => updateDamage(ship, { armorDamage: value }).catch(showError(setMessage))}
                          />
                          <DamageControl
                            label="Firecon"
                            value={ship.fireControlDamage}
                            max={6}
                            onChange={(value) => updateDamage(ship, { fireControlDamage: value }).catch(showError(setMessage))}
                          />
                          <DamageControl
                            label="Drive"
                            value={ship.driveDamage}
                            max={ship.thrustRating}
                            onChange={(value) => updateDamage(ship, { driveDamage: value }).catch(showError(setMessage))}
                          />
                          <DamageControl
                            label="Weapons"
                            value={ship.weaponDamage}
                            max={12}
                            onChange={(value) => updateDamage(ship, { weaponDamage: value }).catch(showError(setMessage))}
                          />
                          <div className="quick-actions damage-actions">
                            <button className="ghost" type="button" onClick={() => repairAll(ship).catch(showError(setMessage))}>Repair</button>
                            <button className="ghost" type="button" onClick={() => updateDamage(ship, { fireControlDamage: ship.fireControlDamage + 1 }).catch(showError(setMessage))}>Firecon Hit</button>
                            <button className="ghost" type="button" onClick={() => updateDamage(ship, { driveDamage: ship.driveDamage + 1 }).catch(showError(setMessage))}>Drive Hit</button>
                            <button className="ghost" type="button" onClick={() => updateDamage(ship, { weaponDamage: ship.weaponDamage + 1 }).catch(showError(setMessage))}>Weapon Hit</button>
                            <button className="ghost" type="button" onClick={() => updateDamage(ship, { fireControlDamage: 0, driveDamage: 0, weaponDamage: 0 }).catch(showError(setMessage))}>Systems Up</button>
                            <button className="ghost" type="button" onClick={() => updateDamage(ship, { hullDamage: ship.hullMax }).catch(showError(setMessage))}>Destroy</button>
                            <button className="ghost" type="button" disabled={!damageUndo} onClick={() => undoLastDamage().catch(showError(setMessage))}>Undo Damage</button>
                          </div>
                        </div>
                      </>
                    ) : null}
                    {!canEdit ? (
                      <div className="damage-grid card-module">
                        <span className="label module-title">Damage report</span>
                        <DamageMeter label="Hull" value={ship.hullDamage} max={ship.hullMax} />
                        <DamageMeter label="Armor" value={ship.armorDamage} max={ship.armorMax} />
                        <DamageMeter label="Firecon" value={ship.fireControlDamage} max={6} />
                        <DamageMeter label="Drive" value={ship.driveDamage} max={ship.thrustRating} />
                        <DamageMeter label="Weapons" value={ship.weaponDamage} max={12} />
                        <p className="privacy">Opponent order remains hidden until reveal.</p>
                      </div>
                    ) : null}
                    <footer>
                      <span>{status?.isCommitted ? 'Locked' : 'Unlocked'}</span>
                      <span>{status?.isRevealed ? 'Revealed' : 'Hidden'}</span>
                      {status?.verificationFailed ? <span className="danger">Failed</span> : null}
                    </footer>
                    {result ? <p className="result">Moves to V{result.endingVelocity}, C{result.endingCourse}</p> : null}
                  </article>
                );
              })}
            </div>
            ) : null}
            {activeView === 'map' && snapshot ? (
              <PlayMap
                snapshot={snapshot}
                ownedShipIds={visibleOwnedShipIds}
                ownerParticipantId={publicMode ? undefined : session.participantId}
                drafts={drafts}
                firingDrafts={firingDrafts}
                phase={snapshot.phase}
                focusedShipId={mapFocusShipId}
                onFocus={setMapFocusShipId}
                onDraftChange={(ship, patch) => {
                  updateDraft(ship.id, patch);
                  setMessage(`${ship.name} map plot: ${formatTurnSequence({ ...draftFor(ship.id, drafts), ...patch })}.`);
                }}
                onFiringDraftChange={(ship, draft) => updateFiringDraft(ship.id, draft)}
                onFighterOps={(ship, patch) => updateFighterOperations(ship, patch).catch(showError(setMessage))}
                onCreateOrdnance={(ship, patch) => createOrdnanceMarker(ship, patch).catch(showError(setMessage))}
                onUpdateOrdnance={(marker, patch) => updateOrdnanceMarker(marker, patch).catch(showError(setMessage))}
                onRemoveOrdnance={(marker) => removeOrdnanceMarker(marker).catch(showError(setMessage))}
                onFire={(ship, draft) => fireWeapon(ship, draft)}
              />
            ) : null}
            {activeView === 'log' ? (
            <section className="match-log" aria-label="Match log">
              <div className="section-head compact">
                <div>
                  <span className="label">After action log</span>
                  <h3>Battle record</h3>
                </div>
                <div className="log-actions">
                  <button className="ghost" onClick={() => exportAfterAction('md')}>MD</button>
                  <button className="ghost" onClick={() => exportAfterAction('csv')}>CSV</button>
                  <button className="ghost" onClick={() => exportAfterAction('json')}>JSON</button>
                </div>
              </div>
              <ol className="log-list">
                {(snapshot?.matchLog ?? []).slice().reverse().map((entry) => (
                  <li key={entry.sequence}>
                    <span>T{entry.turnNumber} · {formatPhase(entry.phase)} · {entry.category} · {formatLogTime(entry.timestamp)}</span>
                    <p>{entry.message}</p>
                  </li>
                ))}
              </ol>
            </section>
            ) : null}
          </section>
        </div>
      )}
    </main>
  );

  function updateDraft(shipId: string, patch: Partial<DraftOrder>) {
    setDrafts((current) => ({
      ...current,
      [shipId]: { ...draftFor(shipId, current), ...patch },
    }));
  }

  function updateFiringDraft(shipId: string, draft: FiringDraft) {
    setFiringDrafts((current) => ({
      ...current,
      [shipId]: draft,
    }));
  }
}

function toOrder(draft: DraftOrder) {
  const maneuvers = turnManeuversForDraft(draft);
  const turnSteps = maneuvers.reduce((sum, maneuver) => sum + maneuver.steps, 0);
  const directions = new Set(maneuvers.map((maneuver) => maneuver.direction));
  return {
    velocityDelta: draft.velocityDelta,
    turnSteps,
    turnDirection: turnSteps === 0 ? 'None' : directions.size === 1 ? maneuvers[0].direction : 'None',
    turnManeuvers: maneuvers,
  };
}

function clampDraftForShip(thrustRating: number, draft: DraftOrder): DraftOrder {
  const velocityDelta = Math.max(-thrustRating, Math.min(thrustRating, draft.velocityDelta));
  const maxTurn = maxLegalTurn(thrustRating, velocityDelta);
  const maneuvers = clampTurnManeuvers(turnManeuversForDraft(draft), maxTurn);
  const turnSteps = maneuvers.reduce((sum, maneuver) => sum + maneuver.steps, 0);
  const directions = new Set(maneuvers.map((maneuver) => maneuver.direction));
  return {
    ...draft,
    velocityDelta,
    turnSteps,
    turnDirection: turnSteps === 0 ? 'None' : directions.size === 1 ? maneuvers[0].direction : draft.turnDirection === 'None' ? maneuvers[0].direction : draft.turnDirection,
    turnManeuvers: maneuvers,
  };
}

function resetOrderDraft(draft: DraftOrder): DraftOrder {
  return {
    ...draft,
    velocityDelta: 0,
    turnSteps: 0,
    turnDirection: 'None',
    turnManeuvers: [],
  };
}

function ShipIcon({ iconKey }: { iconKey: ShipIconKey }) {
  return (
    <svg className="ship-icon-svg" viewBox="0 0 64 64" aria-hidden="true">
      {shipIconPath(iconKey)}
    </svg>
  );
}

function PreTurnChecklist({
  snapshot,
  ownedShipIds,
  damageUndoLabel,
  onUndoDamage,
}: {
  snapshot: MatchSnapshot;
  ownedShipIds: Set<string>;
  damageUndoLabel?: string;
  onUndoDamage: () => void;
}) {
  const items = buildPreTurnChecklist(snapshot, ownedShipIds);
  const blockers = items.filter((item) => item.severity === 'blocker').length;
  const warnings = items.filter((item) => item.severity === 'warning').length;

  return (
    <section className="preturn-checklist" aria-label="Pre-turn checklist">
      <div className="checklist-head">
        <div>
          <span className="label">Pre-turn checklist</span>
          <h3>{blockers === 0 ? 'Clear to proceed' : `${blockers} blocker${blockers === 1 ? '' : 's'}`}</h3>
        </div>
        <div className="checklist-actions">
          <span>{warnings} warn</span>
          <button className="ghost" type="button" disabled={!damageUndoLabel} onClick={onUndoDamage}>
            Undo {damageUndoLabel ? damageUndoLabel.slice(0, 10) : 'Damage'}
          </button>
        </div>
      </div>
      <div className="checklist-items">
        {items.map((item) => (
          <span key={item.id} className={`checklist-item ${item.severity}`}>
            {item.text}
          </span>
        ))}
      </div>
    </section>
  );
}

function shipIconPath(iconKey: ShipIconKey) {
  switch (iconKey) {
    case 'escort':
      return <path d="M32 4 42 29 56 54 38 48 32 61 26 48 8 54 22 29Z" />;
    case 'frigate':
      return <path d="M32 3 44 24 56 51 40 47 32 60 24 47 8 51 20 24Z M24 33h16l-8-17Z" />;
    case 'destroyer':
      return <path d="M32 3 47 24 59 51 41 47 36 60H28l-5-13L5 51 17 24Z M20 36h24l-12-22Z" />;
    case 'carrier':
      return <path d="M21 6h22l8 43-19 10-19-10Z M24 15v31l8 5 8-5V15Z M6 28h14v16H6Z M44 28h14v16H44Z" />;
    case 'dreadnought':
      return <path d="M32 2 50 20 60 49 43 46 37 61H27l-6-15L4 49 14 20Z M20 30h24l-5-11H25Z M23 43h18l-9 9Z" />;
    case 'fighter-group':
      return (
        <>
          <path d="M18 8 28 31 18 56 11 39 4 42 12 29 4 17 11 20Z" />
          <path d="M46 8 60 17 52 29 60 42 53 39 46 56 36 31Z" />
          <path d="M32 15 40 33 32 52 24 33Z" />
        </>
      );
    case 'station':
      return (
        <>
          <path d="M32 8 46 16v32L32 56 18 48V16Z M32 19 26 23v18l6 4 6-4V23Z" />
          <path d="M30 1h4v14h-4ZM30 49h4v14h-4ZM1 30h14v4H1ZM49 30h14v4H49Z" />
        </>
      );
    case 'cruiser':
    default:
      return <path d="M32 3 46 28 56 55 40 49 32 61 24 49 8 55 18 28Z M25 31h14l-7-17Z" />;
  }
}

function ShipProfileFields({ form, onChange }: { form: ShipForm; onChange: (form: ShipForm) => void }) {
  return (
    <div className="profile-fields">
      <div className="preset-strip">
        <span className="label">Presets</span>
        <div className="quick-actions">
          {shipPresets.map((preset) => (
            <button
              key={preset.label}
              className="ghost"
              type="button"
              onClick={() => onChange({
                ...form,
                ...preset.patch,
                weapons: preset.patch.weapons?.map((weapon) => ({ ...weapon, id: crypto.randomUUID() })) ?? form.weapons,
              })}
            >
              {preset.label}
            </button>
          ))}
        </div>
      </div>
      <label>
        Ship name
        <input value={form.name} onChange={(event) => onChange({ ...form, name: event.target.value })} />
      </label>
      <label>
        Class
        <input value={form.className} onChange={(event) => onChange({ ...form, className: event.target.value })} />
      </label>
      <label>
        Ship icon
        <select value={form.iconKey} onChange={(event) => onChange({ ...form, iconKey: event.target.value as ShipIconKey })}>
          {shipIconOptions.map((option) => <option key={option.key} value={option.key}>{option.label}</option>)}
        </select>
      </label>
      <label>
        Thrust
        <input type="number" min="0" max="20" value={form.thrustRating} onChange={(event) => onChange({ ...form, thrustRating: Number(event.target.value) })} />
      </label>
      <label>
        Start velocity
        <input type="number" min="0" value={form.currentVelocity} onChange={(event) => onChange({ ...form, currentVelocity: Number(event.target.value) })} />
      </label>
      <label>
        Course
        <input type="number" min="1" max="12" value={form.currentCourse} onChange={(event) => onChange({ ...form, currentCourse: Number(event.target.value) })} />
      </label>
      <label>
        Position X
        <input type="number" min="0" value={form.positionX} onChange={(event) => onChange({ ...form, positionX: Number(event.target.value) })} />
      </label>
      <label>
        Position Y
        <input type="number" min="0" value={form.positionY} onChange={(event) => onChange({ ...form, positionY: Number(event.target.value) })} />
      </label>
      <label>
        Hull boxes
        <input type="number" min="1" max="80" value={form.hullMax} onChange={(event) => onChange({ ...form, hullMax: Number(event.target.value) })} />
      </label>
      <label>
        Armor boxes
        <input type="number" min="0" max="40" value={form.armorMax} onChange={(event) => onChange({ ...form, armorMax: Number(event.target.value) })} />
      </label>
      <label>
        Screens
        <input type="number" min="0" max="3" value={form.screenRating} onChange={(event) => onChange({ ...form, screenRating: Number(event.target.value) })} />
      </label>
      <label>
        Points (NPV)
        <input type="number" min="0" max="99999" value={form.pointsValue} onChange={(event) => onChange({ ...form, pointsValue: Number(event.target.value) })} />
      </label>
      {isFighterGroupForm(form) ? (
        <>
          <label>
            Fighter status
            <select value={form.fighterStatus} onChange={(event) => onChange({ ...form, fighterStatus: event.target.value as FighterStatus })}>
              {fighterStatuses.map((status) => <option key={status}>{status}</option>)}
            </select>
          </label>
          <label>
            Endurance used
            <input type="number" min="0" max={form.fighterEnduranceMax || 24} value={form.fighterEnduranceUsed} onChange={(event) => onChange({ ...form, fighterEnduranceUsed: Number(event.target.value) })} />
          </label>
          <label>
            Endurance max
            <input type="number" min="1" max="24" value={form.fighterEnduranceMax || 6} onChange={(event) => onChange({ ...form, fighterEnduranceMax: Number(event.target.value) })} />
          </label>
          <label>
            Max range
            <input type="number" min="1" max="120" value={form.fighterMaxRange || 24} onChange={(event) => onChange({ ...form, fighterMaxRange: Number(event.target.value) })} />
          </label>
        </>
      ) : null}
      <div className="weapon-editor">
        <div className="section-head compact">
          <div>
            <span className="label">Weapons</span>
            <h3>{form.weapons.length} {form.weapons.length === 1 ? 'mount' : 'mounts'}</h3>
          </div>
          <button
            className="ghost"
            type="button"
            onClick={() => onChange({
              ...form,
              weapons: [...form.weapons, newWeaponMount()],
            })}
          >
            Add
          </button>
        </div>
        {form.weapons.map((weapon) => (
          <div className="weapon-row" key={weapon.id}>
            <label>
              Name
              <input value={weapon.name} onChange={(event) => onChange(updateWeapon(form, weapon.id, { name: event.target.value }))} />
            </label>
            <label>
              Dice
              <input type="number" min="1" max="12" value={weapon.attackDice} onChange={(event) => onChange(updateWeapon(form, weapon.id, { attackDice: Number(event.target.value) }))} />
            </label>
            <label>
              Range
              <input type="number" min="1" max="72" value={weapon.maxRange} onChange={(event) => onChange(updateWeapon(form, weapon.id, { maxRange: Number(event.target.value) }))} />
            </label>
            <label>
              Arc
              <select value={weapon.arc} onChange={(event) => onChange(updateWeapon(form, weapon.id, { arc: event.target.value as FiringArc }))}>
                {firingArcs.map((arc) => <option key={arc}>{arc}</option>)}
              </select>
            </label>
            <label>
              Ammo
              <input type="number" min="0" max="99" value={weapon.ammoMax} onChange={(event) => onChange(updateWeapon(form, weapon.id, { ammoMax: Number(event.target.value), ammoUsed: Math.min(weapon.ammoUsed, Number(event.target.value)) }))} />
            </label>
            <label>
              Used
              <input type="number" min="0" max={weapon.ammoMax || 99} value={weapon.ammoUsed} onChange={(event) => onChange(updateWeapon(form, weapon.id, { ammoUsed: Number(event.target.value) }))} />
            </label>
            <label>
              Reload
              <input type="number" min="0" max="12" value={weapon.reloadTurns} onChange={(event) => onChange(updateWeapon(form, weapon.id, { reloadTurns: Number(event.target.value) }))} />
            </label>
            <button
              className="ghost"
              type="button"
              onClick={() => onChange({
                ...form,
                weapons: form.weapons.filter((item) => item.id !== weapon.id),
              })}
            >
              Remove
            </button>
          </div>
        ))}
      </div>
    </div>
  );
}

function ShipEditor({ ship, onSave, onCancel }: { ship: Ship; onSave: (form: ShipForm) => void; onCancel: () => void }) {
  const [form, setForm] = useState<ShipForm>({
    fleetName: '',
    faction: '',
    fleetColor: '#47f1ff',
    name: ship.name,
    className: ship.className ?? '',
    iconKey: normalizeShipIconKey(ship.iconKey, ship.className),
    thrustRating: ship.thrustRating,
    currentVelocity: ship.currentVelocity,
    currentCourse: ship.currentCourse,
    positionX: ship.positionX,
    positionY: ship.positionY,
    hullMax: ship.hullMax,
    armorMax: ship.armorMax,
    screenRating: ship.screenRating,
    weapons: ship.weapons.length > 0 ? ship.weapons : [newWeaponMount()],
    fighterEnduranceMax: ship.fighterEnduranceMax,
    fighterEnduranceUsed: ship.fighterEnduranceUsed,
    fighterMaxRange: ship.fighterMaxRange,
    fighterStatus: ship.fighterStatus,
    homeCarrierShipId: ship.homeCarrierShipId ?? '',
    pointsValue: ship.pointsValue ?? 0,
  });

  return (
    <section className="ship-editor" aria-label={`Edit ${ship.name}`}>
      <div className="section-head compact">
        <div>
          <span className="label">Profile editor</span>
          <h3>{ship.name}</h3>
        </div>
      </div>
      <ShipProfileFields form={form} onChange={setForm} />
      <div className="card-actions">
        <button onClick={() => onSave(form)}>Save Stats</button>
        <button className="ghost" onClick={onCancel}>Cancel</button>
      </div>
    </section>
  );
}

function PlayMap({
  snapshot,
  ownedShipIds,
  ownerParticipantId,
  drafts,
  firingDrafts,
  phase,
  focusedShipId,
  onFocus,
  onDraftChange,
  onFiringDraftChange,
  onFighterOps,
  onCreateOrdnance,
  onUpdateOrdnance,
  onRemoveOrdnance,
  onFire,
}: {
  snapshot: MatchSnapshot;
  ownedShipIds: Set<string>;
  ownerParticipantId?: string;
  drafts: Record<string, DraftOrder>;
  firingDrafts: Record<string, FiringDraft>;
  phase: string;
  focusedShipId: string | null;
  onFocus: (shipId: string | null) => void;
  onDraftChange: (ship: Ship, patch: Partial<DraftOrder>) => void;
  onFiringDraftChange: (ship: Ship, draft: FiringDraft) => void;
  onFighterOps: (ship: Ship, patch: Partial<Pick<Ship, 'fighterStatus' | 'fighterEnduranceUsed' | 'fighterEnduranceMax' | 'fighterMaxRange' | 'homeCarrierShipId'>>) => void;
  onCreateOrdnance: (ship: Ship, patch: Partial<OrdnanceMarker>) => void;
  onUpdateOrdnance: (marker: OrdnanceMarker, patch: Partial<OrdnanceMarker>) => void;
  onRemoveOrdnance: (marker: OrdnanceMarker) => void;
  onFire: (ship: Ship, draft: FiringDraft) => Promise<void>;
}) {
  const selectedShip = snapshot.ships.find((ship) => ship.id === focusedShipId)
    ?? snapshot.ships.find((ship) => ownedShipIds.has(ship.id) && !ship.isDestroyed)
    ?? snapshot.ships[0];
  const tableRef = useRef<HTMLDivElement | null>(null);
  const dragRef = useRef<{
    pointerId: number;
    startX: number;
    startY: number;
    originX: number;
    originY: number;
    didMove: boolean;
    longPressId: number | null;
  } | null>(null);
  const markerDragRef = useRef<{
    pointerId: number;
    shipId: string;
    startX: number;
    startY: number;
    didMove: boolean;
  } | null>(null);
  const markerLongPressRef = useRef<{ pointerId: number; timeoutId: number } | null>(null);
  const pointersRef = useRef(new Map<number, TablePoint>());
  const pinchRef = useRef<{ distance: number; scale: number } | null>(null);
  const [viewport, setViewport] = useState({ scale: 1, x: 0, y: 0 });
  const [isPanning, setIsPanning] = useState(false);
  const [measureMode, setMeasureMode] = useState(false);
  const [inspectorMode, setInspectorMode] = useState<'status' | 'helm' | 'fire' | 'ops'>('status');
  const [measureLine, setMeasureLine] = useState<{ start: TablePoint; end: TablePoint } | null>(null);
  const [mapNotice, setMapNotice] = useState('Pan ready');
  const [hoveredShipId, setHoveredShipId] = useState<string | null>(null);
  const selectedDraft = selectedShip ? draftFor(selectedShip.id, drafts) : null;
  const selectedFiringDraft = selectedShip ? firingDraftFor(selectedShip, snapshot.ships, firingDrafts, ownedShipIds) : null;
  const selectedCanPlot = selectedShip ? ownedShipIds.has(selectedShip.id) && !selectedShip.isDestroyed : false;
  const selectedPlannedCourse = selectedShip && selectedDraft ? previewCourse(selectedShip.currentCourse, selectedDraft) : null;
  const selectedFleet = snapshot.fleets.find((fleet) => fleet.id === selectedShip?.fleetId);
  const selectedFleetColor = normalizeFleetColor(selectedFleet?.fleetColor);
  const selectedIsFighterGroup = selectedShip ? isFighterGroup(selectedShip) : false;
  const selectedIsCarrier = selectedShip ? normalizeShipIconKey(selectedShip.iconKey, selectedShip.className) === 'carrier' : false;
  const hoveredShip = hoveredShipId ? snapshot.ships.find((ship) => ship.id === hoveredShipId) : undefined;
  const ordnanceMarkers = snapshot.ordnanceMarkers ?? [];
  const contactRanges = selectedShip
    ? snapshot.ships
      .filter((ship) => ship.id !== selectedShip.id)
      .map((ship) => ({ ship, range: distanceBetweenShips(selectedShip, ship) }))
      .sort((left, right) => left.range - right.range)
      .slice(0, 4)
    : [];

  const inspectorModeAllowed = (mode: 'status' | 'helm' | 'fire' | 'ops') => {
    if (!selectedShip || mode === 'status') {
      return true;
    }

    if (mode === 'helm') {
      return selectedCanPlot;
    }

    if (mode === 'fire') {
      return ownedShipIds.has(selectedShip.id);
    }

    return ownedShipIds.has(selectedShip.id) && (selectedIsFighterGroup || selectedIsCarrier);
  };

  // Selecting a different contact must not leave a tool panel open that the new
  // selection is not entitled to (e.g. carrier ops on an opponent hull).
  useEffect(() => {
    if (!inspectorModeAllowed(inspectorMode)) {
      setInspectorMode('status');
    }
  }, [selectedShip?.id, inspectorMode, selectedCanPlot, selectedIsFighterGroup, selectedIsCarrier]);

  useEffect(() => {
    const element = tableRef.current;
    if (!element) {
      return undefined;
    }

    const handleWheel = (event: WheelEvent) => {
      if (!event.composedPath().includes(element)) {
        return;
      }

      event.preventDefault();
      event.stopPropagation();
      event.stopImmediatePropagation();

      const rect = element.getBoundingClientRect();
      const localX = event.clientX - rect.left;
      const localY = event.clientY - rect.top;
      const deltaPixels = event.deltaMode === 1 ? event.deltaY * 16 : event.deltaY;
      const delta = Math.max(-0.35, Math.min(0.35, -deltaPixels * 0.0015));
      setViewport((current) => {
        const next = viewportZoomedAt(current, localX, localY, current.scale + delta);
        setMapNotice(`Zoom ${Math.round(next.scale * 100)}%`);
        return next;
      });
    };

    document.addEventListener('wheel', handleWheel, { passive: false, capture: true });
    return () => document.removeEventListener('wheel', handleWheel, { capture: true });
  }, []);

  function setZoom(nextScale: number) {
    setViewport((current) => clampMapViewport({ ...current, scale: nextScale }));
    setMapNotice(`Zoom ${Math.round(Math.max(0.75, Math.min(4, nextScale)) * 100)}%`);
  }

  function zoomBy(delta: number) {
    setViewport((current) => {
      const next = clampMapViewport({ ...current, scale: current.scale + delta });
      setMapNotice(`Zoom ${Math.round(next.scale * 100)}%`);
      return next;
    });
  }

  function resetMap() {
    setViewport({ scale: 1, x: 0, y: 0 });
    setMapNotice('Table fit restored');
  }

  function panMapBy(deltaX: number, deltaY: number) {
    setViewport((current) => clampMapViewport({
      ...current,
      x: current.x + deltaX,
      y: current.y + deltaY,
    }));
  }

  function centerSelectedShip() {
    if (!selectedShip || !tableRef.current) {
      setMapNotice('No contact selected');
      return;
    }

    const rect = tableRef.current.getBoundingClientRect();
    const shipX = rect.width * mapPercent(selectedShip.positionX, snapshot.tableWidth) / 100;
    const shipY = rect.height * mapPercent(selectedShip.positionY, snapshot.tableDepth) / 100;
    setViewport((current) => clampMapViewport({
      ...current,
      x: rect.width / 2 - shipX * current.scale,
      y: rect.height / 2 - shipY * current.scale,
    }));
    setMapNotice(`${selectedShip.name} centered`);
  }

  function focusNextContact() {
    if (snapshot.ships.length === 0) {
      return;
    }

    const currentIndex = selectedShip ? snapshot.ships.findIndex((ship) => ship.id === selectedShip.id) : -1;
    const nextShip = snapshot.ships[(currentIndex + 1 + snapshot.ships.length) % snapshot.ships.length];
    onFocus(nextShip.id);
    setMapNotice(`${nextShip.name} selected`);
  }

  function clearMarkerLongPress() {
    if (markerLongPressRef.current) {
      window.clearTimeout(markerLongPressRef.current.timeoutId);
      markerLongPressRef.current = null;
    }
  }

  /// Right click and long press on a marker resolve to the same actions: select the ship,
  /// and for an opposing contact hand it to the selected friendly ship as a target.
  function markerActionsFor(ship: Ship, isOwned: boolean) {
    if (isOwned && !ship.isDestroyed) {
      onFocus(ship.id);
      setInspectorMode('helm');
      setMapNotice(`${ship.name} selected. Right-click or long press open table space to plot course.`);
      return;
    }

    if (selectedShip && ownedShipIds.has(selectedShip.id) && ship.id !== selectedShip.id) {
      // Targeting acts on the selected friendly ship, so it stays the planning subject
      // even though the gesture landed on the opposing marker.
      onFocus(selectedShip.id);
      updateMapFiringDraft(selectedShip, { targetShipId: ship.id });
      setInspectorMode('fire');
      setMapNotice(`${ship.name} set as target for ${selectedShip.name}.`);
      return;
    }

    onFocus(ship.id);
  }

  function clearLongPress() {
    if (dragRef.current?.longPressId) {
      window.clearTimeout(dragRef.current.longPressId);
      dragRef.current.longPressId = null;
    }
  }

  function measureFromClientPoint(clientX: number, clientY: number, startNew: boolean) {
    if (!tableRef.current) {
      return;
    }

    const point = tablePointFromClient(tableRef.current, clientX, clientY, viewport, snapshot.tableWidth, snapshot.tableDepth);
    setMeasureLine((current) => {
      const next = startNew || !current ? { start: point, end: point } : { ...current, end: point };
      setMapNotice(`Measure ${measureDistance(next).toFixed(1)} in · course ${measureCourse(next)}`);
      return next;
    });
  }

  function updateMapFiringDraft(ship: Ship, patch: Partial<FiringDraft>) {
    const current = firingDraftFor(ship, snapshot.ships, firingDrafts, ownedShipIds);
    const target = snapshot.ships.find((item) => item.id === (patch.targetShipId ?? current.targetShipId));
    const weapon = ship.weapons.find((item) => item.id === (patch.weaponId ?? current.weaponId)) ?? ship.weapons[0];
    const range = target ? Math.max(1, Math.round(distanceBetweenShips(ship, target))) : current.range;
    const keepsArc = !patch.arc
      && allowedFiringArcs(weapon).includes(current.arc)
      && patch.weaponId === undefined
      && patch.targetShipId === undefined;
    const next: FiringDraft = {
      ...current,
      ...patch,
      weaponId: weapon?.id ?? current.weaponId,
      targetShipId: target?.id ?? current.targetShipId,
      range: patch.range ?? range,
      arc: patch.arc ?? (keepsArc ? current.arc : suggestFiringArc(ship, target, weapon)),
    };
    onFiringDraftChange(ship, next);
    if (target) {
      setMapNotice(`${ship.name} solution: ${weapon?.name ?? 'weapon'} on ${target.name}, range ${next.range}, ${next.arc}`);
    }
  }

  function plotFromClientPoint(clientX: number, clientY: number, shipOverride?: Ship) {
    const shipToPlot = shipOverride ?? selectedShip;
    if (!shipToPlot || !ownedShipIds.has(shipToPlot.id) || shipToPlot.isDestroyed || !tableRef.current) {
      setMapNotice('Select an operational friendly ship');
      return false;
    }

    const point = tablePointFromClient(tableRef.current, clientX, clientY, viewport, snapshot.tableWidth, snapshot.tableDepth);
    const targetCourse = courseFromTablePoint(shipToPlot, point.x, point.y);
    const draft = draftFor(shipToPlot.id, drafts);
    const maxTurn = maxLegalTurn(usableThrust(shipToPlot), draft.velocityDelta);
    const plannedCourse = previewCourse(shipToPlot.currentCourse, draft);
    const remainingTurns = Math.max(0, maxTurn - totalTurnSteps(draft));

    if (remainingTurns <= 0 && targetCourse !== plannedCourse) {
      setMapNotice(`${shipToPlot.name} has no turn points left`);
      return false;
    }

    const patch = appendTurnPatchForCourse(draft, plannedCourse, targetCourse, maxTurn);
    const nextDraft = { ...draft, ...patch };
    onDraftChange(shipToPlot, patch);
    onFocus(shipToPlot.id);
    setInspectorMode('helm');
    setMapNotice(`${shipToPlot.name}: ${formatTurnSequence(nextDraft)} to course ${previewCourse(shipToPlot.currentCourse, nextDraft)}`);
    return true;
  }

  function selectInspectorMode(mode: 'status' | 'helm' | 'fire' | 'ops') {
    setInspectorMode(mode);
    if (mode === 'helm') {
      setMapNotice(selectedCanPlot ? 'Helm: drag a ship marker or right-click open table space.' : 'Helm is read-only for this contact.');
    } else if (mode === 'fire') {
      setMapNotice('Fire: right-click a contact to target it, then confirm range and arc.');
    } else if (mode === 'ops') {
      setMapNotice('Ops: fighter range and carrier operations for the selected ship.');
    } else {
      setMapNotice('Status: quiet table view with selected ship readouts.');
    }
  }

  return (
    <section className="play-map-view" aria-label="Estimated play map">
      <div className="map-head">
        <div>
          <span className="label">Play map</span>
          <h3>{snapshot.tableWidth} x {snapshot.tableDepth} table</h3>
        </div>
        <p>{mapNotice}</p>
      </div>
      <div className="map-controls" aria-label="Map controls">
        <label className="map-contact-select">
          <span className="label">Contact</span>
          <select value={selectedShip?.id ?? ''} onChange={(event) => onFocus(event.target.value || null)}>
            {snapshot.ships.map((ship) => (
              <option key={ship.id} value={ship.id}>
                {ship.name}{ownedShipIds.has(ship.id) ? ' - yours' : ' - contact'}
              </option>
            ))}
          </select>
        </label>
        <button className="ghost map-icon-button" type="button" title="Zoom out" aria-label="Zoom out" onClick={() => zoomBy(-0.25)}>-</button>
        <input
          aria-label="Map zoom"
          type="range"
          min="0.75"
          max="4"
          step="0.05"
          value={viewport.scale}
          onChange={(event) => setZoom(Number(event.target.value))}
        />
        <button className="ghost map-icon-button" type="button" title="Zoom in" aria-label="Zoom in" onClick={() => zoomBy(0.25)}>+</button>
        <button className="ghost" type="button" onClick={centerSelectedShip}>Center</button>
        <button className="ghost" type="button" onClick={focusNextContact}>Next</button>
        <button className="ghost" type="button" onClick={resetMap}>Fit</button>
        <button className={measureMode ? 'ghost active' : 'ghost'} type="button" onClick={() => {
          setMeasureMode((current) => {
            const next = !current;
            if (!next) {
              setMeasureLine(null);
            }
            setMapNotice(next ? 'Measure mode: drag across the table' : 'Measure cleared');
            return next;
          });
        }}>Measure</button>
        <span>{Math.round(viewport.scale * 100)}%</span>
      </div>
      <div
        ref={tableRef}
        tabIndex={0}
        role="application"
        aria-label="Tactical play map. Use arrow keys to pan, plus and minus to zoom, and Home to fit."
        className={isPanning ? 'table-map panning' : 'table-map'}
        style={{ aspectRatio: `${snapshot.tableWidth} / ${snapshot.tableDepth}` }}
        onKeyDown={(event) => {
          const panStep = event.shiftKey ? 96 : 32;
          if (event.key === '+' || event.key === '=') {
            event.preventDefault();
            zoomBy(0.15);
          } else if (event.key === '-' || event.key === '_') {
            event.preventDefault();
            zoomBy(-0.15);
          } else if (event.key === 'Home') {
            event.preventDefault();
            resetMap();
          } else if (event.key === 'ArrowLeft') {
            event.preventDefault();
            panMapBy(panStep, 0);
          } else if (event.key === 'ArrowRight') {
            event.preventDefault();
            panMapBy(-panStep, 0);
          } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            panMapBy(0, panStep);
          } else if (event.key === 'ArrowDown') {
            event.preventDefault();
            panMapBy(0, -panStep);
          }
        }}
        onContextMenu={(event) => {
          event.preventDefault();
          plotFromClientPoint(event.clientX, event.clientY);
        }}
        onPointerDown={(event) => {
          if (event.button === 2) {
            return;
          }

          event.preventDefault();
          event.currentTarget.focus();
          pointersRef.current.set(event.pointerId, { x: event.clientX, y: event.clientY });
          if (pointersRef.current.size === 2 && !measureMode) {
            // Second finger down: switch from panning to pinch zoom.
            clearLongPress();
            const [first, second] = [...pointersRef.current.values()];
            pinchRef.current = { distance: Math.max(1, Math.hypot(second.x - first.x, second.y - first.y)), scale: viewport.scale };
            if (dragRef.current) {
              dragRef.current.didMove = true;
            }
            setIsPanning(false);
            setMapNotice('Pinch to zoom');
            return;
          }
          if (pointersRef.current.size > 2) {
            return;
          }
          if (measureMode) {
            measureFromClientPoint(event.clientX, event.clientY, true);
            dragRef.current = {
              pointerId: event.pointerId,
              startX: event.clientX,
              startY: event.clientY,
              originX: viewport.x,
              originY: viewport.y,
              didMove: true,
              longPressId: null,
            };
            setIsPanning(false);
            return;
          }
          if (!event.currentTarget.hasPointerCapture(event.pointerId)) {
            try {
              event.currentTarget.setPointerCapture(event.pointerId);
            } catch {
              setMapNotice('Pointer capture unavailable; tap still plots');
            }
          }
          const longPressId = window.setTimeout(() => {
            if (dragRef.current?.pointerId === event.pointerId) {
              dragRef.current.didMove = true;
            }
            plotFromClientPoint(event.clientX, event.clientY);
            clearLongPress();
          }, 550);
          dragRef.current = {
            pointerId: event.pointerId,
            startX: event.clientX,
            startY: event.clientY,
            originX: viewport.x,
            originY: viewport.y,
            didMove: false,
            longPressId,
          };
          setIsPanning(true);
          setMapNotice('Panning table');
        }}
        onPointerMove={(event) => {
          if (pointersRef.current.has(event.pointerId)) {
            pointersRef.current.set(event.pointerId, { x: event.clientX, y: event.clientY });
          }

          const pinch = pinchRef.current;
          if (pinch && pointersRef.current.size >= 2 && tableRef.current) {
            event.preventDefault();
            const [first, second] = [...pointersRef.current.values()];
            const distance = Math.max(1, Math.hypot(second.x - first.x, second.y - first.y));
            const rect = tableRef.current.getBoundingClientRect();
            const localX = (first.x + second.x) / 2 - rect.left;
            const localY = (first.y + second.y) / 2 - rect.top;
            setViewport((current) => {
              const next = viewportZoomedAt(current, localX, localY, pinch.scale * (distance / pinch.distance));
              setMapNotice(`Zoom ${Math.round(next.scale * 100)}%`);
              return next;
            });
            return;
          }

          const drag = dragRef.current;
          if (!drag || drag.pointerId !== event.pointerId) {
            return;
          }

          event.preventDefault();
          if (measureMode) {
            measureFromClientPoint(event.clientX, event.clientY, false);
            return;
          }
          const moved = Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY);
          if (moved <= 6) {
            return;
          }

          drag.didMove = true;
          clearLongPress();
          setViewport((current) => clampMapViewport({
            ...current,
            x: drag.originX + event.clientX - drag.startX,
            y: drag.originY + event.clientY - drag.startY,
          }));
        }}
        onPointerUp={(event) => {
          pointersRef.current.delete(event.pointerId);
          if (pinchRef.current) {
            // Leaving a pinch: drop the pan anchor so the remaining finger does not jump the table.
            pinchRef.current = pointersRef.current.size >= 2 ? pinchRef.current : null;
            if (!pinchRef.current) {
              clearLongPress();
              dragRef.current = null;
              setIsPanning(false);
            }
            if (event.currentTarget.hasPointerCapture(event.pointerId)) {
              event.currentTarget.releasePointerCapture(event.pointerId);
            }
            return;
          }

          const drag = dragRef.current;
          if (drag?.pointerId === event.pointerId) {
            clearLongPress();
            if (measureMode) {
              measureFromClientPoint(event.clientX, event.clientY, false);
            } else if (!drag.didMove) {
              plotFromClientPoint(event.clientX, event.clientY);
            } else {
              setMapNotice('Table view repositioned');
            }
            if (event.currentTarget.hasPointerCapture(event.pointerId)) {
              event.currentTarget.releasePointerCapture(event.pointerId);
            }
            dragRef.current = null;
            setIsPanning(false);
          }
        }}
        onPointerCancel={(event) => {
          pointersRef.current.delete(event.pointerId);
          if (pointersRef.current.size < 2) {
            pinchRef.current = null;
          }
          if (event.currentTarget.hasPointerCapture(event.pointerId)) {
            event.currentTarget.releasePointerCapture(event.pointerId);
          }
          clearLongPress();
          dragRef.current = null;
          setIsPanning(false);
        }}
      >
        <div
          className="table-map-content"
          style={{
            transform: `translate(${viewport.x}px, ${viewport.y}px) scale(${viewport.scale})`,
            '--marker-scale': `${1 / viewport.scale}`,
          } as CSSProperties}
        >
          <span className="table-centerline horizontal" />
          <span className="table-centerline vertical" />
          {snapshot.ships.length === 0 ? (
            <div className="map-empty-state">
              <strong>No contacts</strong>
              <span>Add ships during fleet setup to populate the table.</span>
            </div>
          ) : null}
          {selectedShip && inspectorMode === 'ops' ? (
            <FighterRangeOverlay
              ship={selectedShip}
              ships={snapshot.ships}
              tableWidth={snapshot.tableWidth}
              tableDepth={snapshot.tableDepth}
            />
          ) : null}
          {selectedShip && inspectorMode === 'fire' ? (
            <WeaponRangeOverlay
              ship={selectedShip}
              targets={snapshot.ships.filter((ship) => ship.id !== selectedShip.id && !ship.isDestroyed)}
              tableWidth={snapshot.tableWidth}
              tableDepth={snapshot.tableDepth}
            />
          ) : null}
          <ResolvedMovementTrailOverlay
            snapshot={snapshot}
            focusedShipId={selectedShip?.id}
          />
          {selectedShip && selectedDraft && inspectorMode === 'helm' ? (
            <MovementPreviewOverlay
              ship={selectedShip}
              draft={selectedDraft}
              tableWidth={snapshot.tableWidth}
              tableDepth={snapshot.tableDepth}
              color={selectedFleetColor}
            />
          ) : null}
          {measureLine ? (
            <MeasureOverlay
              line={measureLine}
              tableWidth={snapshot.tableWidth}
              tableDepth={snapshot.tableDepth}
            />
          ) : null}
          <OrdnanceMarkerOverlay
            markers={ordnanceMarkers}
            tableWidth={snapshot.tableWidth}
            tableDepth={snapshot.tableDepth}
            onSelect={(marker) => setMapNotice(`${marker.name}: ${marker.markerType} ${normalizeOrdnanceStatus(marker.status)}, endurance ${marker.enduranceRemaining}`)}
          />
          {snapshot.ships.map((ship) => {
            const fleet = snapshot.fleets.find((item) => item.id === ship.fleetId);
            const owner = snapshot.participants.find((participant) => participant.id === fleet?.ownerParticipantId);
            const isOwned = ownedShipIds.has(ship.id);
            const fleetColor = normalizeFleetColor(fleet?.fleetColor);
            return (
              <button
                key={ship.id}
                className={[
                  'map-ship',
                  isOwned ? 'owned' : 'opponent',
                  ship.isDestroyed ? 'destroyed' : '',
                  selectedShip?.id === ship.id ? 'focused' : '',
                ].join(' ')}
                style={{
                  left: `${mapPercent(ship.positionX, snapshot.tableWidth)}%`,
                  top: `${mapPercent(ship.positionY, snapshot.tableDepth)}%`,
                  '--course': `${courseAngle(ship.currentCourse)}deg`,
                  '--fleet-color': fleetColor,
                } as CSSProperties}
                title={`${ship.name} ${ship.positionX.toFixed(1)},${ship.positionY.toFixed(1)} V${ship.currentVelocity} C${ship.currentCourse}`}
                onPointerEnter={() => {
                  setHoveredShipId(ship.id);
                  setMapNotice(`${ship.name}: V${ship.currentVelocity} C${ship.currentCourse} hull ${ship.hullDamage}/${ship.hullMax}`);
                }}
                onPointerLeave={() => setHoveredShipId((current) => (current === ship.id ? null : current))}
                onPointerDown={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  onFocus(ship.id);
                  markerLongPressRef.current = {
                    pointerId: event.pointerId,
                    timeoutId: window.setTimeout(() => {
                      markerLongPressRef.current = null;
                      // Drop the pending drag so releasing after a long press does not also plot.
                      if (markerDragRef.current?.pointerId === event.pointerId) {
                        markerDragRef.current = null;
                      }
                      markerActionsFor(ship, isOwned);
                    }, 550),
                  };
                  if (isOwned && !ship.isDestroyed) {
                    setInspectorMode('helm');
                    markerDragRef.current = {
                      pointerId: event.pointerId,
                      shipId: ship.id,
                      startX: event.clientX,
                      startY: event.clientY,
                      didMove: false,
                    };
                    if (!event.currentTarget.hasPointerCapture(event.pointerId)) {
                      try {
                        event.currentTarget.setPointerCapture(event.pointerId);
                      } catch {
                        setMapNotice('Pointer capture unavailable; marker tap still selects');
                      }
                    }
                    setMapNotice(`${ship.name}: drag from marker to plot course.`);
                    return;
                  }

                  setMapNotice(`${ship.name}: V${ship.currentVelocity} C${ship.currentCourse} at ${ship.positionX.toFixed(1)},${ship.positionY.toFixed(1)}`);
                }}
                onPointerMove={(event) => {
                  const drag = markerDragRef.current;
                  if (!drag || drag.pointerId !== event.pointerId || drag.shipId !== ship.id) {
                    return;
                  }

                  event.preventDefault();
                  event.stopPropagation();
                  const moved = Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY);
                  if (moved > 8) {
                    drag.didMove = true;
                    clearMarkerLongPress();
                    setMapNotice(`${ship.name}: release to plot course.`);
                  }
                }}
                onPointerUp={(event) => {
                  clearMarkerLongPress();
                  const drag = markerDragRef.current;
                  if (!drag || drag.pointerId !== event.pointerId || drag.shipId !== ship.id) {
                    return;
                  }

                  event.preventDefault();
                  event.stopPropagation();
                  if (drag.didMove) {
                    plotFromClientPoint(event.clientX, event.clientY, ship);
                  }
                  if (event.currentTarget.hasPointerCapture(event.pointerId)) {
                    event.currentTarget.releasePointerCapture(event.pointerId);
                  }
                  markerDragRef.current = null;
                }}
                onPointerCancel={(event) => {
                  clearMarkerLongPress();
                  if (markerDragRef.current?.pointerId === event.pointerId) {
                    markerDragRef.current = null;
                  }
                  if (event.currentTarget.hasPointerCapture(event.pointerId)) {
                    event.currentTarget.releasePointerCapture(event.pointerId);
                  }
                }}
                onContextMenu={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  markerActionsFor(ship, isOwned);
                }}
                onFocus={() => onFocus(ship.id)}
              >
                <span className="ship-icon-shell">
                  <ShipIcon iconKey={normalizeShipIconKey(ship.iconKey, ship.className)} />
                </span>
                <span className="map-status-badges" aria-hidden="true">
                  {ship.screenRating > 0 ? <i className="screen">S</i> : null}
                  {ship.driveDamage > 0 ? <i className="drive">D</i> : null}
                  {ship.fireControlDamage > 0 ? <i className="fire-control">FC</i> : null}
                  {ship.isDestroyed ? <i className="destroyed">X</i> : null}
                </span>
                <strong>{ship.name.slice(0, 3).toUpperCase()}</strong>
                <em>{owner?.displayName ?? 'Player'}</em>
              </button>
            );
          })}
          {hoveredShip ? (
            <MapContactCard
              ship={hoveredShip}
              fleet={snapshot.fleets.find((fleet) => fleet.id === hoveredShip.fleetId)}
              owner={snapshot.participants.find((participant) => participant.id === snapshot.fleets.find((fleet) => fleet.id === hoveredShip.fleetId)?.ownerParticipantId)}
              result={snapshot.movementResults.find((item) => item.shipId === hoveredShip.id)}
              draft={ownedShipIds.has(hoveredShip.id) ? draftFor(hoveredShip.id, drafts) : undefined}
              isOwned={ownedShipIds.has(hoveredShip.id)}
              tableWidth={snapshot.tableWidth}
              tableDepth={snapshot.tableDepth}
            />
          ) : null}
          {selectedShip && selectedPlannedCourse && inspectorMode === 'helm' ? (
            <span
              className={selectedCanPlot ? 'planned-bearing-map active' : 'planned-bearing-map'}
              style={{
                left: `${mapPercent(selectedShip.positionX, snapshot.tableWidth)}%`,
                top: `${mapPercent(selectedShip.positionY, snapshot.tableDepth)}%`,
                '--course': `${courseAngle(selectedPlannedCourse)}deg`,
              } as CSSProperties}
            />
          ) : null}
        </div>
      </div>
      {selectedShip ? (
        <div className="map-inspector">
          <div>
            <span className="label">Selected contact</span>
            <h3>{selectedShip.name}</h3>
            <small>{selectedShip.className ?? 'Unclassified'} · {ownedShipIds.has(selectedShip.id) ? 'Your fleet' : 'Opponent'}</small>
            <div className="selected-action-tabs" aria-label="Selected ship tools">
              <button className={inspectorMode === 'status' ? 'ghost active' : 'ghost'} type="button" onClick={() => selectInspectorMode('status')}>Status</button>
              <button className={inspectorMode === 'helm' ? 'ghost active' : 'ghost'} type="button" disabled={!selectedCanPlot} onClick={() => selectInspectorMode('helm')}>Helm</button>
              <button className={inspectorMode === 'fire' ? 'ghost active' : 'ghost'} type="button" disabled={!ownedShipIds.has(selectedShip.id)} onClick={() => selectInspectorMode('fire')}>Fire</button>
              <button className={inspectorMode === 'ops' ? 'ghost active' : 'ghost'} type="button" disabled={!ownedShipIds.has(selectedShip.id) || (!selectedIsFighterGroup && !selectedIsCarrier)} onClick={() => selectInspectorMode('ops')}>Ops</button>
            </div>
            {inspectorMode === 'status' && contactRanges.length > 0 ? (
              <div className="contact-ranges">
                <span className="label">Nearest contacts</span>
                {contactRanges.map(({ ship, range }) => (
                  <button
                    key={ship.id}
                    className="ghost"
                    type="button"
                    onClick={() => onFocus(ship.id)}
                  >
                    {ship.name} <strong>{range.toFixed(1)}</strong>
                  </button>
                ))}
              </div>
            ) : null}
            {inspectorMode === 'helm' ? (
              <div className="selected-tool-panel">
                <span className="label">Helm plotting</span>
                <p className="privacy">{selectedCanPlot ? 'Right-click, tap, or long press open table space to plot this ship within remaining thrust.' : 'Opponent helm is read-only.'}</p>
                <strong>{selectedDraft ? formatTurnSequence(selectedDraft) : 'No turn'} · planned C{selectedPlannedCourse ?? selectedShip.currentCourse}</strong>
              </div>
            ) : null}
            {inspectorMode === 'fire' && selectedShip && ownedShipIds.has(selectedShip.id) ? (
              <MapFiringAssistant
                ship={selectedShip}
                ships={snapshot.ships}
                ownedShipIds={ownedShipIds}
                draft={selectedFiringDraft ?? firingDraftFor(selectedShip, snapshot.ships, firingDrafts, ownedShipIds)}
                phase={phase}
                firingResults={snapshot.firingResults}
                onChange={(patch) => updateMapFiringDraft(selectedShip, patch)}
                onFire={() => onFire(selectedShip, selectedFiringDraft ?? firingDraftFor(selectedShip, snapshot.ships, firingDrafts, ownedShipIds)).catch((error) => setMapNotice(error instanceof Error ? error.message : String(error)))}
              />
            ) : null}
            {inspectorMode === 'fire' && selectedShip && ownedShipIds.has(selectedShip.id) ? (
              <OrdnanceLaunchPanel
                ship={selectedShip}
                targets={snapshot.ships.filter((ship) => ship.id !== selectedShip.id && !ship.isDestroyed)}
                onLaunch={(patch) => onCreateOrdnance(selectedShip, patch)}
              />
            ) : null}
            {inspectorMode === 'ops' && selectedShip && ownedShipIds.has(selectedShip.id) && selectedIsFighterGroup ? (
              <FighterOpsPanel
                ship={selectedShip}
                carriers={snapshot.ships.filter((ship) => ship.id !== selectedShip.id && normalizeShipIconKey(ship.iconKey, ship.className) === 'carrier')}
                onChange={(patch) => onFighterOps(selectedShip, patch)}
              />
            ) : null}
            {inspectorMode === 'ops' && selectedShip && ownedShipIds.has(selectedShip.id) && selectedIsCarrier ? (
              <CarrierOpsPanel
                carrier={selectedShip}
                fighters={snapshot.ships.filter((ship) => isFighterGroup(ship))}
              />
            ) : null}
            {inspectorMode === 'fire' && ordnanceMarkers.length > 0 ? (
              <OrdnanceMarkerList
                markers={ordnanceMarkers}
                canEdit={(marker) => Boolean(ownerParticipantId) && marker.ownerParticipantId === ownerParticipantId}
                onUpdate={onUpdateOrdnance}
                onRemove={onRemoveOrdnance}
              />
            ) : null}
          </div>
          <div className="ship-readouts">
            <div>
              <span className="label">Position</span>
              <strong>{selectedShip.positionX.toFixed(1)},{selectedShip.positionY.toFixed(1)}</strong>
            </div>
            <div>
              <span className="label">Velocity</span>
              <strong>{selectedShip.currentVelocity}</strong>
            </div>
            <div>
              <span className="label">Course</span>
              <strong>{selectedShip.currentCourse}</strong>
            </div>
            <div>
              <span className="label">Planned</span>
              <strong>{selectedPlannedCourse ?? selectedShip.currentCourse}</strong>
            </div>
            <div>
              <span className="label">Turn</span>
              <strong>{selectedDraft ? formatTurnSequence(selectedDraft) : 'No turn'}</strong>
            </div>
            <div>
              <span className="label">Hull</span>
              <strong>{selectedShip.hullDamage}/{selectedShip.hullMax}</strong>
            </div>
            <div>
              <span className="label">Armor</span>
              <strong>{selectedShip.armorDamage}/{selectedShip.armorMax}</strong>
            </div>
            <div>
              <span className="label">Screens</span>
              <strong>{selectedShip.screenRating}</strong>
            </div>
          </div>
        </div>
      ) : null}
    </section>
  );
}

/// Hover readout for a contact: heading, this turn's resolved move, and the damage record.
function MapContactCard({
  ship,
  fleet,
  owner,
  result,
  draft,
  isOwned,
  tableWidth,
  tableDepth,
}: {
  ship: Ship;
  fleet?: Fleet;
  owner?: Participant;
  result?: MovementResult;
  draft?: DraftOrder;
  isOwned: boolean;
  tableWidth: number;
  tableDepth: number;
}) {
  const left = mapPercent(ship.positionX, tableWidth);
  const top = mapPercent(ship.positionY, tableDepth);
  const plannedCourse = draft ? previewCourse(ship.currentCourse, draft) : null;
  const plannedTurn = draft ? formatTurnSequence(draft) : null;
  const systems = [
    ship.fireControlDamage > 0 ? `firecon ${ship.fireControlDamage}` : null,
    ship.driveDamage > 0 ? `drive ${ship.driveDamage}/${ship.thrustRating}` : null,
    ship.weaponDamage > 0 ? `weapons ${ship.weaponDamage}` : null,
  ].filter(Boolean);

  return (
    <div
      className={`map-contact-card ${left > 62 ? 'flip' : ''} ${isOwned ? 'owned' : 'opponent'}`}
      style={{ left: `${left}%`, top: `${top}%`, '--fleet-color': normalizeFleetColor(fleet?.fleetColor) } as CSSProperties}
      aria-hidden="true"
    >
      <div className="contact-card-head">
        <span
          className={`contact-card-glyph ${ship.isDestroyed ? 'destroyed' : ''}`}
          style={{ '--course': `${courseAngle(ship.currentCourse)}deg` } as CSSProperties}
        >
          <ShipIcon iconKey={normalizeShipIconKey(ship.iconKey, ship.className)} />
        </span>
        <span className="contact-card-title">
          <strong>{ship.name}</strong>
          <small>{ship.className ?? 'Unclassified'} · {owner?.displayName ?? 'Player'}{isOwned ? ' · yours' : ''}</small>
        </span>
        {ship.isDestroyed ? <em className="destroyed">Destroyed</em> : null}
      </div>

      <dl className="contact-card-rows">
        <div>
          <dt>Heading</dt>
          <dd>V{ship.currentVelocity} · C{ship.currentCourse}{plannedCourse !== null && plannedCourse !== ship.currentCourse ? ` → C${plannedCourse}` : ''}</dd>
        </div>
        {plannedTurn && plannedTurn !== 'No turn' ? (
          <div>
            <dt>Plotted</dt>
            <dd>{plannedTurn}{draft && draft.velocityDelta !== 0 ? ` · dV ${draft.velocityDelta > 0 ? '+' : ''}${draft.velocityDelta}` : ''}</dd>
          </div>
        ) : null}
        {result ? (
          <div>
            <dt>Moved</dt>
            <dd>v{result.startingVelocity}/c{result.startingCourse} → v{result.endingVelocity}/c{result.endingCourse}</dd>
          </div>
        ) : null}
        <div>
          <dt>Position</dt>
          <dd>{ship.positionX.toFixed(1)}, {ship.positionY.toFixed(1)}</dd>
        </div>
        <div>
          <dt>Hull</dt>
          <dd className={ship.hullDamage >= Math.ceil(ship.hullMax / 2) ? 'hurt' : ''}>{ship.hullDamage}/{ship.hullMax}{ship.hullDamage >= Math.ceil(ship.hullMax / 2) && !ship.isDestroyed ? ' · crippled' : ''}</dd>
        </div>
        <div>
          <dt>Armor</dt>
          <dd>{ship.armorDamage}/{ship.armorMax}{ship.screenRating > 0 ? ` · screens ${ship.screenRating}` : ''}</dd>
        </div>
        <div>
          <dt>Systems</dt>
          <dd className={systems.length > 0 ? 'hurt' : ''}>{systems.length > 0 ? systems.join(' · ') : 'all nominal'}</dd>
        </div>
      </dl>
    </div>
  );
}

function FighterRangeOverlay({ ship, ships, tableWidth, tableDepth }: { ship: Ship; ships: Ship[]; tableWidth: number; tableDepth: number }) {
  if (!isFighterGroup(ship) || ship.isDestroyed) {
    return null;
  }

  const maxRange = ship.fighterMaxRange || 24;
  const enduranceRange = fighterEnduranceRange(ship);
  const homeCarrier = ship.homeCarrierShipId
    ? ships.find((candidate) => candidate.id === ship.homeCarrierShipId)
    : null;
  const maxRangeCenter = homeCarrier ?? ship;
  const maxLeft = mapPercent(maxRangeCenter.positionX, tableWidth);
  const maxTop = mapPercent(maxRangeCenter.positionY, tableDepth);
  const enduranceLeft = mapPercent(ship.positionX, tableWidth);
  const enduranceTop = mapPercent(ship.positionY, tableDepth);

  return (
    <div className="fighter-range-overlay" aria-hidden="true">
      <span
        className="fighter-range max"
        style={{
          left: `${maxLeft}%`,
          top: `${maxTop}%`,
          width: `${rangeDiameterPercent(maxRange, tableWidth)}%`,
          height: `${rangeDiameterPercent(maxRange, tableDepth)}%`,
        }}
      >
        <em>FTR MAX {maxRange}</em>
      </span>
      <span
        className="fighter-range endurance"
        style={{
          left: `${enduranceLeft}%`,
          top: `${enduranceTop}%`,
          width: `${rangeDiameterPercent(enduranceRange, tableWidth)}%`,
          height: `${rangeDiameterPercent(enduranceRange, tableDepth)}%`,
        }}
      >
        <em>END {Math.max(0, ship.fighterEnduranceMax - ship.fighterEnduranceUsed)}</em>
      </span>
    </div>
  );
}

function FighterOpsPanel({
  ship,
  carriers,
  onChange,
}: {
  ship: Ship;
  carriers: Ship[];
  onChange: (patch: Partial<Pick<Ship, 'fighterStatus' | 'fighterEnduranceUsed' | 'fighterEnduranceMax' | 'fighterMaxRange' | 'homeCarrierShipId'>>) => void;
}) {
  const enduranceRemaining = Math.max(0, ship.fighterEnduranceMax - ship.fighterEnduranceUsed);
  return (
    <div className="fighter-ops-panel">
      <span className="label">Fighter ops · {ship.fighterStatus} · {enduranceRemaining} turns left</span>
      <label>
        Status
        <select value={ship.fighterStatus} onChange={(event) => onChange({ fighterStatus: event.target.value as FighterStatus })}>
          {fighterStatuses.map((status) => <option key={status}>{status}</option>)}
        </select>
      </label>
      <label>
        Home carrier
        <select value={ship.homeCarrierShipId ?? ''} onChange={(event) => onChange({ homeCarrierShipId: event.target.value || null })}>
          <option value="">Unassigned</option>
          {carriers.map((carrier) => <option key={carrier.id} value={carrier.id}>{carrier.name}</option>)}
        </select>
      </label>
      <label>
        Used
        <input type="number" min="0" max={ship.fighterEnduranceMax || 24} value={ship.fighterEnduranceUsed} onChange={(event) => onChange({ fighterEnduranceUsed: Number(event.target.value) })} />
      </label>
      <label>
        Max
        <input type="number" min="1" max="24" value={ship.fighterEnduranceMax || 6} onChange={(event) => onChange({ fighterEnduranceMax: Number(event.target.value) })} />
      </label>
      <label>
        Range
        <input type="number" min="1" max="120" value={ship.fighterMaxRange || 24} onChange={(event) => onChange({ fighterMaxRange: Number(event.target.value) })} />
      </label>
      <button className="ghost" type="button" onClick={() => onChange({ fighterStatus: 'Airborne', fighterEnduranceUsed: 0 })}>Launch</button>
      <button className="ghost" type="button" onClick={() => onChange({ fighterStatus: 'Recovering' })}>Return</button>
      <button className="ghost" type="button" onClick={() => onChange({ fighterEnduranceUsed: ship.fighterEnduranceUsed + 1 })}>Spend Turn</button>
      <button type="button" onClick={() => onChange({ fighterStatus: 'Docked', fighterEnduranceUsed: 0 })}>Recover</button>
    </div>
  );
}

function OrdnanceMarkerOverlay({
  markers,
  tableWidth,
  tableDepth,
  onSelect,
}: {
  markers: OrdnanceMarker[];
  tableWidth: number;
  tableDepth: number;
  onSelect: (marker: OrdnanceMarker) => void;
}) {
  if (markers.length === 0) {
    return null;
  }

  return (
    <div className="ordnance-overlay" aria-label="Launched ordnance markers">
      {markers.map((marker) => (
        <button
          key={marker.id}
          type="button"
          className={`ordnance-marker ${normalizeOrdnanceStatus(marker.status).toLowerCase()}`}
          style={{
            left: `${mapPercent(marker.positionX, tableWidth)}%`,
            top: `${mapPercent(marker.positionY, tableDepth)}%`,
            '--course': `${courseAngle(marker.course)}deg`,
          } as CSSProperties}
          title={`${marker.name} ${marker.markerType} ${normalizeOrdnanceStatus(marker.status)}`}
          onPointerDown={(event) => {
            event.stopPropagation();
            onSelect(marker);
          }}
        >
          <span />
          <strong>{marker.markerType.slice(0, 3).toUpperCase()}</strong>
          <em>{marker.enduranceRemaining}</em>
        </button>
      ))}
    </div>
  );
}

function CarrierOpsPanel({ carrier, fighters }: { carrier: Ship; fighters: Ship[] }) {
  const assigned = fighters.filter((fighter) => fighter.homeCarrierShipId === carrier.id);
  const airborne = assigned.filter((fighter) => fighter.fighterStatus === 'Airborne');
  const recovering = assigned.filter((fighter) => fighter.fighterStatus === 'Recovering');
  const damagedLimit = carrier.driveDamage > 0 ? Math.max(0, assigned.length - carrier.driveDamage) : assigned.length;

  return (
    <div className="carrier-ops-panel">
      <span className="label">Carrier ops</span>
      <div>
        <strong>{assigned.length}</strong>
        <small>assigned groups</small>
      </div>
      <div>
        <strong>{airborne.length}</strong>
        <small>airborne</small>
      </div>
      <div>
        <strong>{recovering.length}</strong>
        <small>recovering</small>
      </div>
      <div>
        <strong>{damagedLimit}</strong>
        <small>damage-adjusted bay estimate</small>
      </div>
      {carrier.driveDamage > 0 ? <p className="privacy">Drive damage is flagged as a launch/recovery constraint for tabletop adjudication.</p> : null}
    </div>
  );
}

function OrdnanceLaunchPanel({
  ship,
  targets,
  onLaunch,
}: {
  ship: Ship;
  targets: Ship[];
  onLaunch: (patch: Partial<OrdnanceMarker>) => void;
}) {
  const [draft, setDraft] = useState({
    name: `${ship.name} Salvo`,
    markerType: 'Missile',
    targetShipId: targets[0]?.id ?? '',
    speed: Math.max(6, ship.currentVelocity),
    enduranceRemaining: 1,
    attackDice: 2,
    maxRange: 24,
  });

  useEffect(() => {
    setDraft((current) => ({
      ...current,
      name: current.name || `${ship.name} Salvo`,
      targetShipId: targets.some((target) => target.id === current.targetShipId) ? current.targetShipId : targets[0]?.id ?? '',
    }));
  }, [ship.id, targets.map((target) => target.id).join('|')]);

  return (
    <div className="ordnance-panel">
      <span className="label">Ordnance / salvo</span>
      <label>
        Name
        <input value={draft.name} onChange={(event) => setDraft({ ...draft, name: event.target.value })} />
      </label>
      <label>
        Type
        <select value={draft.markerType} onChange={(event) => setDraft({ ...draft, markerType: event.target.value })}>
          <option>Missile</option>
          <option>Salvo</option>
          <option>Torpedo</option>
          <option>Drone</option>
        </select>
      </label>
      <label>
        Target
        <select value={draft.targetShipId} onChange={(event) => setDraft({ ...draft, targetShipId: event.target.value })}>
          <option value="">No target</option>
          {targets.map((target) => <option key={target.id} value={target.id}>{target.name}</option>)}
        </select>
      </label>
      <label>
        Speed
        <input type="number" min="0" max="72" value={draft.speed} onChange={(event) => setDraft({ ...draft, speed: Number(event.target.value) })} />
      </label>
      <label>
        Endurance
        <input type="number" min="0" max="24" value={draft.enduranceRemaining} onChange={(event) => setDraft({ ...draft, enduranceRemaining: Number(event.target.value) })} />
      </label>
      <label>
        Dice
        <input type="number" min="0" max="24" value={draft.attackDice} onChange={(event) => setDraft({ ...draft, attackDice: Number(event.target.value) })} />
      </label>
      <button type="button" onClick={() => onLaunch({
        ...draft,
        targetShipId: draft.targetShipId || null,
        positionX: ship.positionX,
        positionY: ship.positionY,
        course: ship.currentCourse,
        status: 'Active',
      })}>Launch Marker</button>
    </div>
  );
}

function OrdnanceMarkerList({
  markers,
  canEdit,
  onUpdate,
  onRemove,
}: {
  markers: OrdnanceMarker[];
  canEdit: (marker: OrdnanceMarker) => boolean;
  onUpdate: (marker: OrdnanceMarker, patch: Partial<OrdnanceMarker>) => void;
  onRemove: (marker: OrdnanceMarker) => void;
}) {
  return (
    <div className="ordnance-list">
      <span className="label">Active ordnance</span>
      {markers.map((marker) => (
        <div key={marker.id} className="ordnance-list-row">
          <strong>{marker.name}</strong>
          <small>{marker.markerType} · {normalizeOrdnanceStatus(marker.status)} · E{marker.enduranceRemaining} · C{marker.course}/V{marker.speed}</small>
          <div className="quick-actions">
            <button className="ghost" type="button" disabled={!canEdit(marker)} onClick={() => onUpdate(marker, { enduranceRemaining: Math.max(0, marker.enduranceRemaining - 1), status: marker.enduranceRemaining <= 1 ? 'Expired' : marker.status })}>Tick</button>
            <button className="ghost" type="button" disabled={!canEdit(marker)} onClick={() => onUpdate(marker, { status: 'Resolved' })}>Resolve</button>
            <button className="ghost" type="button" disabled={!canEdit(marker)} onClick={() => onRemove(marker)}>Remove</button>
          </div>
        </div>
      ))}
    </div>
  );
}

function WeaponRangeOverlay({ ship, targets, tableWidth, tableDepth }: { ship: Ship; targets: Ship[]; tableWidth: number; tableDepth: number }) {
  if (ship.weapons.length === 0 || ship.isDestroyed) {
    return null;
  }

  const usableWeapons = ship.weapons.slice(0, 5);
  return (
    <div className="weapon-map-overlay" aria-hidden="true">
      {usableWeapons.map((weapon, index) => {
        const left = mapPercent(ship.positionX, tableWidth);
        const top = mapPercent(ship.positionY, tableDepth);
        const width = rangeDiameterPercent(weapon.maxRange, tableWidth);
        const height = rangeDiameterPercent(weapon.maxRange, tableDepth);
        const arcAngle = weaponArcAngle(ship.currentCourse, weapon.arc);
        return (
          <span
            key={weapon.id}
            className={weapon.arc === 'All' ? 'weapon-range all' : 'weapon-range'}
            style={{
              left: `${left}%`,
              top: `${top}%`,
              width: `${width}%`,
              height: `${height}%`,
              '--arc-index': index,
              '--arc-angle': `${arcAngle}deg`,
            } as CSSProperties}
          >
            <i className="weapon-range-band half" />
            <i className="weapon-range-band close" />
            {weapon.arc !== 'All' ? (
              <>
                <i
                  className="weapon-arc-wedge"
                  style={{ transform: `translate(-50%, -50%) rotate(${arcAngle}deg)` }}
                />
                <i className="weapon-arc-spoke" />
                <em className="weapon-arc-label">{weapon.arc} {weapon.maxRange}</em>
              </>
            ) : (
              <em className="weapon-arc-label all">All {weapon.maxRange}</em>
            )}
          </span>
        );
      })}
      {targets.slice(0, 8).map((target) => {
        const inRange = ship.weapons.some((weapon) => distanceBetweenShips(ship, target) <= weapon.maxRange);
        return (
          <svg key={target.id} className={inRange ? 'target-range-line in-range' : 'target-range-line'} viewBox="0 0 100 100" preserveAspectRatio="none">
            <line
              x1={mapPercent(ship.positionX, tableWidth)}
              y1={mapPercent(ship.positionY, tableDepth)}
              x2={mapPercent(target.positionX, tableWidth)}
              y2={mapPercent(target.positionY, tableDepth)}
            />
          </svg>
        );
      })}
    </div>
  );
}

function ResolvedMovementTrailOverlay({ snapshot, focusedShipId }: { snapshot: MatchSnapshot; focusedShipId?: string }) {
  if (snapshot.movementResults.length === 0) {
    return null;
  }

  return (
    <svg className="resolved-trail-overlay" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true">
      {snapshot.movementResults.map((result) => {
        const ship = snapshot.ships.find((item) => item.id === result.shipId);
        if (!ship) {
          return null;
        }

        const points = trailPointsForResult(ship, result).map((point) => `${mapPercent(point.x, snapshot.tableWidth)},${mapPercent(point.y, snapshot.tableDepth)}`).join(' ');
        return (
          <polyline
            key={result.shipId}
            className={focusedShipId === result.shipId ? 'focused' : ''}
            points={points}
          />
        );
      })}
    </svg>
  );
}

function MeasureOverlay({ line, tableWidth, tableDepth }: { line: { start: TablePoint; end: TablePoint }; tableWidth: number; tableDepth: number }) {
  const startX = mapPercent(line.start.x, tableWidth);
  const startY = mapPercent(line.start.y, tableDepth);
  const endX = mapPercent(line.end.x, tableWidth);
  const endY = mapPercent(line.end.y, tableDepth);
  const labelX = (startX + endX) / 2;
  const labelY = (startY + endY) / 2;

  return (
    <svg className="measure-overlay" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true">
      <line x1={startX} y1={startY} x2={endX} y2={endY} />
      <circle cx={startX} cy={startY} r="0.9" />
      <circle cx={endX} cy={endY} r="0.9" />
      <text x={labelX} y={labelY}>{measureDistance(line).toFixed(1)} / C{measureCourse(line)}</text>
    </svg>
  );
}

function MapFiringAssistant({
  ship,
  ships,
  ownedShipIds,
  draft,
  phase,
  firingResults,
  onChange,
  onFire,
}: {
  ship: Ship;
  ships: Ship[];
  ownedShipIds: Set<string>;
  draft: FiringDraft;
  phase: string;
  firingResults: FiringResult[];
  onChange: (patch: Partial<FiringDraft>) => void;
  onFire: () => void;
}) {
  const targetOptions = firingTargetOptions(ship, ships, ownedShipIds);
  const weapon = ship.weapons.find((item) => item.id === draft.weaponId) ?? ship.weapons[0];
  const target = targetOptions.find((candidate) => candidate.id === draft.targetShipId) ?? targetOptions[0];
  const estimatedRange = target ? Math.max(1, Math.round(distanceBetweenShips(ship, target))) : 0;
  const allowedArcs = allowedFiringArcs(weapon);
  const inRange = Boolean(weapon) && draft.range > 0 && draft.range <= (weapon?.maxRange ?? 0);
  const weaponSpent = Boolean(weapon) && firingResults.some((result) => result.attackerShipId === ship.id && result.weaponId === weapon?.id);
  const ammoEmpty = Boolean(weapon) && weapon!.ammoMax > 0 && weapon!.ammoUsed >= weapon!.ammoMax;
  const canFire = phase === 'Firing' && Boolean(target) && Boolean(weapon) && inRange && !ship.isDestroyed && !weaponSpent && !ammoEmpty;
  const firingNote = !weapon
    ? 'No weapon mounted'
    : !target
      ? 'No target selected'
      : weaponSpent
        ? 'Weapon spent'
        : ammoEmpty
          ? 'Ammo empty'
        : draft.range > weapon.maxRange
        ? `Out of range by ${draft.range - weapon.maxRange}`
        : phase === 'Firing'
          ? 'Ready'
          : 'Firing phase closed';

  return (
    <div className="map-firing-assistant">
      <span className="label">Map firing · {firingNote}</span>
      <label>
        Target
        <select value={draft.targetShipId} onChange={(event) => onChange({ targetShipId: event.target.value })}>
          {targetOptions.map((option) => (
            <option key={option.id} value={option.id}>{option.name}{ownedShipIds.has(option.id) ? ' - yours' : ''}</option>
          ))}
        </select>
      </label>
      <label>
        Weapon
        <select value={draft.weaponId} onChange={(event) => onChange({ weaponId: event.target.value })}>
          {ship.weapons.map((mount) => {
            const spent = firingResults.some((result) => result.attackerShipId === ship.id && result.weaponId === mount.id);
            const ammo = mount.ammoMax > 0 ? ` · ammo ${mount.ammoUsed}/${mount.ammoMax}` : '';
            return <option key={mount.id} value={mount.id}>{mount.name} · {mount.maxRange}{ammo}{spent ? ' · spent' : ''}</option>;
          })}
        </select>
      </label>
      <label>
        Arc
        <select value={draft.arc} onChange={(event) => onChange({ arc: event.target.value as FiringArc })}>
          {allowedArcs.map((arc) => <option key={arc}>{arc}</option>)}
        </select>
      </label>
      <label>
        Range
        <input type="number" min="1" max={weapon?.maxRange ?? 72} value={draft.range} onChange={(event) => onChange({ range: Number(event.target.value) })} />
      </label>
      <button className="ghost" type="button" disabled={!target} onClick={() => onChange({ range: estimatedRange, arc: suggestFiringArc(ship, target, weapon) })}>Use Map Solution</button>
      <button type="button" disabled={!canFire} onClick={onFire}>Fire</button>
    </div>
  );
}

function MovementPreviewOverlay({
  ship,
  draft,
  tableWidth,
  tableDepth,
  color,
}: {
  ship: Ship;
  draft: DraftOrder;
  tableWidth: number;
  tableDepth: number;
  color: string;
}) {
  const endpoint = estimateDraftEndpoint(ship, draft, tableWidth, tableDepth);
  const startX = mapPercent(ship.positionX, tableWidth);
  const startY = mapPercent(ship.positionY, tableDepth);
  const endX = mapPercent(endpoint.x, tableWidth);
  const endY = mapPercent(endpoint.y, tableDepth);
  const hasMovement = Math.abs(endX - startX) > 0.1 || Math.abs(endY - startY) > 0.1;

  if (!hasMovement && totalTurnSteps(draft) === 0 && draft.velocityDelta === 0) {
    return null;
  }

  return (
    <svg className="movement-preview-overlay" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true">
      <line
        x1={startX}
        y1={startY}
        x2={endX}
        y2={endY}
        style={{ '--fleet-color': color } as CSSProperties}
      />
      <circle cx={endX} cy={endY} r="1.2" style={{ '--fleet-color': color } as CSSProperties} />
    </svg>
  );
}

function FiringConsole({
  ship,
  ships,
  ownedShipIds,
  draft,
  phase,
  firingResults,
  onChange,
  onFire,
}: {
  ship: Ship;
  ships: Ship[];
  ownedShipIds: Set<string>;
  draft: FiringDraft;
  phase: string;
  firingResults: FiringResult[];
  onChange: (patch: Partial<FiringDraft>) => void;
  onFire: () => void;
}) {
  const targetOptions = firingTargetOptions(ship, ships, ownedShipIds);
  const weapon = ship.weapons.find((item) => item.id === draft.weaponId) ?? ship.weapons[0];
  const target = targetOptions.find((candidate) => candidate.id === draft.targetShipId) ?? targetOptions[0];
  const estimatedRange = target ? Math.max(1, Math.round(distanceBetweenShips(ship, target))) : null;
  const allowedArcs = allowedFiringArcs(weapon);
  const weaponSpent = Boolean(weapon) && firingResults.some((result) => result.attackerShipId === ship.id && result.weaponId === weapon?.id);
  const ammoEmpty = Boolean(weapon) && weapon!.ammoMax > 0 && weapon!.ammoUsed >= weapon!.ammoMax;
  const inRange = Boolean(weapon) && draft.range > 0 && draft.range <= (weapon?.maxRange ?? 0);
  const canFire = phase === 'Firing' && targetOptions.length > 0 && ship.weapons.length > 0 && !ship.isDestroyed && !weaponSpent && !ammoEmpty && inRange;
  const rangeStatus = weapon && estimatedRange
    ? estimatedRange <= weapon.maxRange ? `Estimated range ${estimatedRange}; in range.` : `Estimated range ${estimatedRange}; outside ${weapon.maxRange}.`
    : 'Pick a target and weapon.';
  const fireStatus = weaponSpent ? `${weapon?.name} spent this turn.` : ammoEmpty ? `${weapon?.name} has no ammunition remaining.` : rangeStatus;

  return (
    <div className="firing-console card-module" aria-label={`${ship.name} firing controls`}>
      <span className="label module-title">Firing solution</span>
      <p className="constraint-line">{phase === 'Firing' ? `Weapons free. ${fireStatus}` : `Resolve movement to open firing. ${rangeStatus}`}</p>
      <label>
        Target
        <select value={draft.targetShipId} onChange={(event) => onChange({ targetShipId: event.target.value })}>
          {targetOptions.map((option) => (
            <option key={option.id} value={option.id}>{option.name}{ownedShipIds.has(option.id) ? ' - yours' : ''}</option>
          ))}
        </select>
      </label>
      <label>
        Weapon
        <select
          value={draft.weaponId}
          onChange={(event) => {
            const nextWeapon = ship.weapons.find((item) => item.id === event.target.value);
            onChange({ weaponId: event.target.value, arc: nextWeapon?.arc ?? 'Fore' });
          }}
        >
          {ship.weapons.map((mount) => {
            const spent = firingResults.some((result) => result.attackerShipId === ship.id && result.weaponId === mount.id);
            const ammo = mount.ammoMax > 0 ? ` · ammo ${mount.ammoUsed}/${mount.ammoMax}` : '';
            return <option key={mount.id} value={mount.id}>{mount.name} · {mount.attackDice}D/{mount.maxRange}{ammo}{spent ? ' · spent' : ''}</option>;
          })}
        </select>
      </label>
      <label>
        Arc
        <select value={draft.arc} onChange={(event) => onChange({ arc: event.target.value as FiringArc })}>
          {allowedArcs.map((arc) => <option key={arc}>{arc}</option>)}
        </select>
      </label>
      <label>
        Range
        <input type="number" min="1" max={weapon?.maxRange ?? 72} value={draft.range} onChange={(event) => onChange({ range: Number(event.target.value) })} />
      </label>
      <button
        className="ghost"
        type="button"
        disabled={!estimatedRange}
        onClick={() => estimatedRange ? onChange({ range: estimatedRange }) : undefined}
      >
        Use Map Range
      </button>
      <button disabled={!canFire} onClick={onFire}>Fire</button>
    </div>
  );
}

function CourseCompass({
  currentCourse,
  thrustRating,
  draft,
  canEdit,
  onDraftChange,
}: {
  currentCourse: number;
  thrustRating: number;
  draft: DraftOrder;
  canEdit: boolean;
  onDraftChange: (patch: Partial<DraftOrder>) => void;
}) {
  const selectedCourse = previewCourse(currentCourse, draft);
  const courses = Array.from({ length: 12 }, (_, index) => index + 1);
  const currentAngle = courseAngle(currentCourse);
  const selectedAngle = courseAngle(selectedCourse);
  const maxTurn = maxLegalTurn(thrustRating, draft.velocityDelta);
  const turnTotal = totalTurnSteps(draft);

  function updateFromPointer(event: PointerEvent<HTMLDivElement>) {
    if (!canEdit) {
      return;
    }

    if (!event.currentTarget.hasPointerCapture(event.pointerId)) {
      try {
        event.currentTarget.setPointerCapture(event.pointerId);
      } catch {
        return;
      }
    }
    const targetCourse = courseFromPoint(event.currentTarget, event.clientX, event.clientY);
    onDraftChange(turnPatchForCourse(currentCourse, targetCourse, maxTurn, draft.turnDirection));
  }

  function updateFromKeyboard(event: KeyboardEvent<HTMLDivElement>) {
    if (!canEdit) {
      return;
    }

    if (event.key === 'Home') {
      event.preventDefault();
      onDraftChange(turnPatchFromManeuvers([]));
      return;
    }

    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') {
      return;
    }

    event.preventDefault();
    const direction: TurnDirection = event.key === 'ArrowLeft' ? 'Port' : 'Starboard';
    const sameDirection = draft.turnDirection === direction;
    const turnSteps = Math.min(maxTurn, sameDirection ? turnTotal + 1 : 1);
    onDraftChange(turnPatchFromManeuvers(turnSteps === 0 ? [] : [{ direction, steps: turnSteps }]));
  }

  return (
    <div className="compass" aria-label={`Current course ${currentCourse}, selected course ${selectedCourse}, maximum turn ${maxTurn}`}>
      <div className="helm-meta">
        <span>PORT LIMIT {wrapCourse(currentCourse - maxTurn)}</span>
        <strong>HELM {selectedCourse}</strong>
        <span>STARBOARD LIMIT {wrapCourse(currentCourse + maxTurn)}</span>
      </div>
      <div
        className={canEdit ? 'compass-face interactive' : 'compass-face'}
        role={canEdit ? 'slider' : 'img'}
        tabIndex={canEdit ? 0 : undefined}
        aria-label="Course change helm"
        aria-valuemin={0}
        aria-valuemax={maxTurn}
        aria-valuenow={turnTotal}
        aria-valuetext={`${formatTurnSequence(draft)}; selected course ${selectedCourse}`}
        onPointerDown={updateFromPointer}
        onPointerMove={(event) => {
          if (event.buttons === 1) {
            updateFromPointer(event);
          }
        }}
        onKeyDown={updateFromKeyboard}
      >
        {courses.map((course) => (
          <span
            key={course}
            className={[
              'course-mark',
              course === currentCourse ? 'current' : '',
              course === selectedCourse ? 'selected' : '',
            ].join(' ')}
            style={{ '--angle': `${courseAngle(course)}deg` } as CSSProperties}
          >
            {course}
          </span>
        ))}
        <span className="turn-limit port-limit" style={{ transform: `rotate(${courseAngle(wrapCourse(currentCourse - maxTurn))}deg)` }} />
        <span className="turn-limit starboard-limit" style={{ transform: `rotate(${courseAngle(wrapCourse(currentCourse + maxTurn))}deg)` }} />
        <span className="bearing current-bearing" style={{ transform: `rotate(${currentAngle}deg)` }} />
        <span className="bearing selected-bearing" style={{ transform: `rotate(${selectedAngle}deg)` }}>
          <span />
        </span>
        <span className="ship-glyph" style={{ transform: `rotate(${currentAngle}deg)` }} aria-hidden="true" />
        <span className="compass-core">
          <small>{formatTurnSequence(draft)}</small>
          <strong>{turnTotal}</strong>
          <em>MAX {maxTurn}</em>
        </span>
      </div>
      <p className="helm-hint">{canEdit ? 'Drag, tap, or use left/right arrows.' : 'Opponent helm is read-only.'}</p>
    </div>
  );
}

function DamageControl({ label, value, max, onChange }: { label: string; value: number; max: number; onChange: (value: number) => void }) {
  return (
    <div className="damage-control">
      <DamageMeter label={label} value={value} max={max} />
      <div className="damage-buttons">
        <button type="button" onClick={() => onChange(Math.max(0, value - 1))}>-</button>
        <button type="button" onClick={() => onChange(Math.min(max, value + 1))}>+</button>
      </div>
    </div>
  );
}

function DamageMeter({ label, value, max }: { label: string; value: number; max: number }) {
  const cells = Array.from({ length: max }, (_, index) => index < value);
  return (
    <div className="damage-meter">
      <div className="damage-meter-head">
        <span>{label}</span>
        <strong>{value}/{max}</strong>
      </div>
      <div className="damage-cells">
        {cells.map((isDamaged, index) => (
          <span key={index} className={isDamaged ? 'damaged' : ''} />
        ))}
      </div>
    </div>
  );
}

function turnManeuversForDraft(draft: DraftOrder): TurnManeuver[] {
  if (Array.isArray(draft.turnManeuvers) && draft.turnManeuvers.length > 0) {
    return draft.turnManeuvers
      .filter((maneuver): maneuver is TurnManeuver => (
        (maneuver.direction === 'Port' || maneuver.direction === 'Starboard')
        && Number.isFinite(maneuver.steps)
        && maneuver.steps > 0
      ))
      .map((maneuver) => ({ direction: maneuver.direction, steps: Math.round(maneuver.steps) }));
  }

  if (draft.turnSteps > 0 && draft.turnDirection !== 'None') {
    return [{ direction: draft.turnDirection, steps: draft.turnSteps }];
  }

  return [];
}

function clampTurnManeuvers(maneuvers: TurnManeuver[], maxTurn: number) {
  let remaining = Math.max(0, maxTurn);
  const clamped: TurnManeuver[] = [];

  for (const maneuver of maneuvers) {
    if (remaining <= 0) {
      break;
    }

    const steps = Math.min(remaining, Math.max(0, Math.round(maneuver.steps)));
    if (steps > 0) {
      clamped.push({ direction: maneuver.direction, steps });
      remaining -= steps;
    }
  }

  return clamped;
}

function totalTurnSteps(draft: DraftOrder) {
  return turnManeuversForDraft(draft).reduce((sum, maneuver) => sum + maneuver.steps, 0);
}

function formatTurnSequence(draft: DraftOrder) {
  const maneuvers = turnManeuversForDraft(draft);
  return maneuvers.length === 0
    ? 'No turn'
    : maneuvers.map((maneuver) => `${maneuver.direction === 'Port' ? 'P' : 'S'}${maneuver.steps}`).join(' ');
}

function turnPatchFromManeuvers(maneuvers: TurnManeuver[]): Partial<DraftOrder> {
  const clean = maneuvers.filter((maneuver) => maneuver.steps > 0);
  const turnSteps = clean.reduce((sum, maneuver) => sum + maneuver.steps, 0);
  const directions = new Set(clean.map((maneuver) => maneuver.direction));

  return {
    turnSteps,
    turnDirection: turnSteps === 0 ? 'None' : directions.size === 1 ? clean[0].direction : 'None',
    turnManeuvers: clean,
  };
}

function previewCourse(currentCourse: number, draft: DraftOrder) {
  return turnManeuversForDraft(draft).reduce((course, maneuver) => (
    wrapCourse(course + (maneuver.direction === 'Starboard' ? maneuver.steps : -maneuver.steps))
  ), currentCourse);
}

/// Thrust actually available for plotting once drive damage is recorded.
function usableThrust(ship: Pick<Ship, 'thrustRating' | 'driveDamage'>) {
  return Math.max(0, ship.thrustRating - ship.driveDamage);
}

function maxLegalTurn(thrustRating: number, velocityDelta: number) {
  const remainingThrust = Math.max(0, thrustRating - Math.abs(velocityDelta));
  return Math.min(Math.ceil(thrustRating / 2), remainingThrust);
}

function turnPatchForCourse(currentCourse: number, targetCourse: number, maxTurn: number, preferredDirection: TurnDirection): Partial<DraftOrder> {
  if (maxTurn === 0 || targetCourse === currentCourse) {
    return turnPatchFromManeuvers([]);
  }

  const starboard = (targetCourse - currentCourse + 12) % 12;
  const port = (currentCourse - targetCourse + 12) % 12;
  const useStarboard = starboard < port || (starboard === port && preferredDirection !== 'Port');
  const rawSteps = useStarboard ? starboard : port;
  const turnSteps = Math.min(rawSteps, maxTurn);

  return turnPatchFromManeuvers(turnSteps === 0 ? [] : [{ direction: useStarboard ? 'Starboard' : 'Port', steps: turnSteps }]);
}

function appendTurnPatchForCourse(draft: DraftOrder, currentCourse: number, targetCourse: number, maxTurn: number): Partial<DraftOrder> {
  const existing = turnManeuversForDraft(draft);
  const remaining = Math.max(0, maxTurn - existing.reduce((sum, maneuver) => sum + maneuver.steps, 0));
  if (remaining <= 0 || targetCourse === currentCourse) {
    return turnPatchFromManeuvers(existing);
  }

  const patch = turnPatchForCourse(currentCourse, targetCourse, remaining, draft.turnDirection);
  return turnPatchFromManeuvers([...existing, ...turnManeuversForDraft({ ...draft, ...patch })]);
}

function courseFromPoint(element: HTMLElement, clientX: number, clientY: number) {
  const rect = element.getBoundingClientRect();
  const centerX = rect.left + rect.width / 2;
  const centerY = rect.top + rect.height / 2;
  const radians = Math.atan2(clientX - centerX, centerY - clientY);
  const degrees = (radians * 180 / Math.PI + 360) % 360;
  return wrapCourse(Math.round(degrees / 30) || 12);
}

function wrapCourse(course: number) {
  const zeroBased = ((course - 1) % 12 + 12) % 12;
  return zeroBased + 1;
}

function courseAngle(course: number) {
  return course * 30;
}

function mapPercent(value: number, max: number) {
  return Math.max(0, Math.min(100, (value / Math.max(1, max)) * 100));
}

function distanceBetweenShips(source: Ship, target: Ship) {
  return Math.hypot(target.positionX - source.positionX, target.positionY - source.positionY);
}

function estimateDraftEndpoint(ship: Ship, draft: DraftOrder, tableWidth: number, tableDepth: number): { x: number; y: number } {
  const endingVelocity = Math.max(0, ship.currentVelocity + draft.velocityDelta);
  const endingCourse = previewCourse(ship.currentCourse, draft);
  const radians = endingCourse * Math.PI / 6;
  return {
    x: Math.max(0, Math.min(tableWidth, ship.positionX + Math.sin(radians) * endingVelocity)),
    y: Math.max(0, Math.min(tableDepth, ship.positionY - Math.cos(radians) * endingVelocity)),
  };
}

function rangeDiameterPercent(range: number, tableSize: number) {
  return Math.max(4, Math.min(240, (range * 2 / Math.max(1, tableSize)) * 100));
}

function isFighterGroup(ship: Pick<Ship, 'iconKey' | 'className'>) {
  return normalizeShipIconKey(ship.iconKey, ship.className) === 'fighter-group'
    || (ship.className ?? '').toLowerCase().includes('fighter');
}

function isFighterGroupForm(form: Pick<ShipForm, 'iconKey' | 'className'>) {
  return normalizeShipIconKey(form.iconKey, form.className) === 'fighter-group'
    || form.className.toLowerCase().includes('fighter');
}

function focusedFirstShips(ships: Ship[], focusedShipId: string | null, fallbackShipId?: string) {
  const priorityShipId = focusedShipId ?? fallbackShipId;
  if (!priorityShipId) {
    return ships;
  }

  return [...ships].sort((left, right) => {
    if (left.id === priorityShipId) {
      return -1;
    }

    if (right.id === priorityShipId) {
      return 1;
    }

    return 0;
  });
}

function fighterEnduranceRange(ship: Ship) {
  const remaining = Math.max(0, ship.fighterEnduranceMax - ship.fighterEnduranceUsed);
  const velocityReach = Math.max(1, ship.currentVelocity) * Math.max(1, remaining);
  const maxRange = ship.fighterMaxRange || 24;
  return Math.max(1, Math.min(maxRange, velocityReach));
}

function weaponArcAngle(course: number, arc: FiringArc) {
  switch (arc) {
    case 'Aft':
      return courseAngle(wrapCourse(course + 6));
    case 'Port':
      return courseAngle(wrapCourse(course - 3));
    case 'Starboard':
      return courseAngle(wrapCourse(course + 3));
    case 'Fore':
    case 'All':
    default:
      return courseAngle(course);
  }
}

function suggestFiringArc(ship: Ship, target?: Ship, weapon?: WeaponMount): FiringArc {
  if (!target || !weapon) {
    return weapon?.arc ?? 'Fore';
  }

  if (weapon.arc !== 'All') {
    return weapon.arc;
  }

  const targetCourse = courseFromTablePoint(ship, target.positionX, target.positionY);
  const clockwise = (targetCourse - ship.currentCourse + 12) % 12;
  if (clockwise <= 1 || clockwise >= 11) {
    return 'Fore';
  }

  if (clockwise >= 5 && clockwise <= 7) {
    return 'Aft';
  }

  return clockwise < 6 ? 'Starboard' : 'Port';
}

function normalizeShipIconKey(value: unknown, className?: string): ShipIconKey {
  const normalized = typeof value === 'string' ? value.trim().toLowerCase().replaceAll(' ', '-').replaceAll('_', '-') : '';
  if (shipIconOptions.some((option) => option.key === normalized)) {
    return normalized as ShipIconKey;
  }

  const classText = (className ?? '').toLowerCase();
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

function normalizeFighterStatus(value: unknown, fallback: FighterStatus = 'Docked'): FighterStatus {
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

function normalizeOrdnanceStatus(value: unknown) {
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
  const id = typeof record.id === 'string' && record.id.trim() ? record.id : crypto.randomUUID();
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

function normalizeMatchSnapshot(snapshot: MatchSnapshot): MatchSnapshot {
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

function normalizeFleetColor(value: unknown) {
  if (typeof value !== 'string') {
    return '#47f1ff';
  }

  return /^#[0-9a-f]{6}$/i.test(value.trim()) ? value.trim() : '#47f1ff';
}

function tablePointFromClient(
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

function courseFromTablePoint(ship: Ship, targetX: number, targetY: number) {
  const dx = targetX - ship.positionX;
  const dy = targetY - ship.positionY;
  if (Math.abs(dx) < 0.01 && Math.abs(dy) < 0.01) {
    return ship.currentCourse;
  }

  const radians = Math.atan2(dx, -dy);
  const degrees = (radians * 180 / Math.PI + 360) % 360;
  return wrapCourse(Math.round(degrees / 30) || 12);
}

function measureDistance(line: { start: TablePoint; end: TablePoint }) {
  return Math.hypot(line.end.x - line.start.x, line.end.y - line.start.y);
}

function measureCourse(line: { start: TablePoint; end: TablePoint }) {
  const dx = line.end.x - line.start.x;
  const dy = line.end.y - line.start.y;
  if (Math.abs(dx) < 0.01 && Math.abs(dy) < 0.01) {
    return 12;
  }

  const radians = Math.atan2(dx, -dy);
  const degrees = (radians * 180 / Math.PI + 360) % 360;
  return wrapCourse(Math.round(degrees / 30) || 12);
}

function trailPointsForResult(ship: Ship, result: MovementResult): TablePoint[] {
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
function viewportZoomedAt(
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

function clampMapViewport(viewport: { scale: number; x: number; y: number }) {
  const scale = Math.max(0.75, Math.min(4, viewport.scale));
  const panLimit = 1200 * scale;
  return {
    scale,
    x: Math.max(-panLimit, Math.min(panLimit, viewport.x)),
    y: Math.max(-panLimit, Math.min(panLimit, viewport.y)),
  };
}

/// Total NPV of a fleet export, the figure a points-limited match is measured against.
function fleetPoints(fleet: FleetExport) {
  return fleet.ships.reduce((sum, ship) => sum + (ship.pointsValue ?? 0), 0);
}

function shipsPoints(ships: Ship[]) {
  return ships.reduce((sum, ship) => sum + (ship.pointsValue ?? 0), 0);
}

function carrierImportRank(ship: FleetExportShip) {
  return normalizeShipIconKey(ship.iconKey, ship.className) === 'carrier' ? 0 : 1;
}

function nextShipName(name: string) {
  const match = name.match(/^(.*?)(\d+)$/);
  if (!match) {
    return `${name} 2`;
  }

  return `${match[1]}${Number(match[2]) + 1}`;
}

const firingArcs: FiringArc[] = ['Fore', 'Aft', 'Port', 'Starboard', 'All'];

function firingDraftFor(ship: Ship, ships: Ship[], drafts: Record<string, FiringDraft>, ownedShipIds?: Set<string>): FiringDraft {
  const current = drafts[ship.id];
  const isTargetable = (candidate: Ship) => candidate.id !== ship.id && !candidate.isDestroyed;
  const target = ships.find((candidate) => candidate.id === current?.targetShipId && isTargetable(candidate))
    ?? firingTargetOptions(ship, ships, ownedShipIds)[0];
  const weapon = ship.weapons.find((mount) => mount.id === current?.weaponId) ?? ship.weapons[0];
  const arcs = allowedFiringArcs(weapon);
  return {
    targetShipId: target?.id ?? '',
    weaponId: weapon?.id ?? '',
    // Default to the measured distance to the resolved target. A fixed default would let one
    // click on Fire resolve an attack at a range the table geometry does not support.
    range: Math.max(1, current?.range ?? (target ? Math.round(distanceBetweenShips(ship, target)) : 12)),
    arc: current?.arc && arcs.includes(current.arc) ? current.arc : weapon?.arc ?? 'Fore',
  };
}

/// Selectable targets for a firing solution: hostile contacts first, nearest first.
/// Friendly hulls stay selectable for deliberate crossfire but are never the default.
function firingTargetOptions(ship: Ship, ships: Ship[], ownedShipIds?: Set<string>): Ship[] {
  return ships
    .filter((candidate) => candidate.id !== ship.id && !candidate.isDestroyed)
    .map((candidate) => ({
      candidate,
      friendly: ownedShipIds?.has(candidate.id) ?? false,
      range: distanceBetweenShips(ship, candidate),
    }))
    .sort((left, right) => Number(left.friendly) - Number(right.friendly) || left.range - right.range)
    .map((entry) => entry.candidate);
}

function allowedFiringArcs(weapon?: WeaponMount): FiringArc[] {
  if (!weapon || weapon.arc === 'All') {
    return firingArcs;
  }

  return [weapon.arc];
}

function newWeaponMount(): WeaponMount {
  return {
    id: crypto.randomUUID(),
    name: 'Class-2 Beam',
    attackDice: 2,
    maxRange: 24,
    arc: 'Fore',
    ammoMax: 0,
    ammoUsed: 0,
    reloadTurns: 0,
  };
}

function weaponPreset(name: string, attackDice: number, maxRange: number, arc: FiringArc, ammoMax = 0): WeaponMount {
  return {
    id: crypto.randomUUID(),
    name,
    attackDice,
    maxRange,
    arc,
    ammoMax,
    ammoUsed: 0,
    reloadTurns: 0,
  };
}

function updateWeapon(form: ShipForm, weaponId: string, patch: Partial<WeaponMount>): ShipForm {
  return {
    ...form,
    weapons: form.weapons.map((weapon) => weapon.id === weaponId ? { ...weapon, ...patch } : weapon),
  };
}

function normalizeWeaponMount(value: unknown): WeaponMount {
  if (!value || typeof value !== 'object') {
    return newWeaponMount();
  }

  const record = value as Record<string, unknown>;
  const arc = stringFrom(record.arc, 'Fore') as FiringArc;
  return {
    id: typeof record.id === 'string' && record.id ? record.id : crypto.randomUUID(),
    name: stringFrom(record.name, 'Class-2 Beam'),
    attackDice: wholeNumberFrom(record.attackDice ?? record.dice, 2, 1, 12),
    maxRange: wholeNumberFrom(record.maxRange ?? record.range, 24, 1, 72),
    arc: firingArcs.includes(arc) ? arc : 'Fore',
    ammoMax: wholeNumberFrom(record.ammoMax ?? record.ammo, 0, 0, 99),
    ammoUsed: wholeNumberFrom(record.ammoUsed ?? record.used, 0, 0, 99),
    reloadTurns: wholeNumberFrom(record.reloadTurns ?? record.reload, 0, 0, 12),
  };
}

function parseWeaponsCell(value: string): WeaponMount[] {
  if (!value.trim()) {
    return [newWeaponMount()];
  }

  return value.split(';').map((entry) => {
    const [name, attackDice, maxRange, arc, ammoMax, ammoUsed, reloadTurns] = entry.split('|');
    return normalizeWeaponMount({ name, attackDice, maxRange, arc, ammoMax, ammoUsed, reloadTurns });
  });
}

function toFleetExport(fleet: Fleet, ships: Ship[]): FleetExport {
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

function parseFleetExport(text: string, fileName: string, fallback: ShipForm): FleetExport {
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

function normalizeFleetExport(value: unknown, fallback: ShipForm, fileName: string): FleetExport {
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

function normalizeFleetExportShip(value: unknown, fallback: ShipForm): FleetExportShip {
  if (!value || typeof value !== 'object') {
    throw new Error('Each imported ship must be an object or CSV row.');
  }

  const record = value as Record<string, unknown>;
  return {
    name: stringFrom(record.name, fallback.name),
    className: stringFrom(record.className ?? record.class, fallback.className),
    iconKey: normalizeShipIconKey(record.iconKey ?? record.icon, stringFrom(record.className ?? record.class, fallback.className)),
    thrustRating: wholeNumberFrom(record.thrustRating ?? record.thrust, fallback.thrustRating, 0, 20),
    initialVelocity: wholeNumberFrom(record.initialVelocity ?? record.currentVelocity ?? record.velocity, fallback.currentVelocity, 0, 999),
    initialCourse: wholeNumberFrom(record.initialCourse ?? record.currentCourse ?? record.course, fallback.currentCourse, 1, 12),
    startX: numberFrom(record.startX ?? record.positionX ?? record.x, fallback.positionX, 0, 144),
    startY: numberFrom(record.startY ?? record.positionY ?? record.y, fallback.positionY, 0, 96),
    hullMax: wholeNumberFrom(record.hullMax ?? record.hullBoxes ?? record.hull, fallback.hullMax, 1, 80),
    armorMax: wholeNumberFrom(record.armorMax ?? record.armorBoxes ?? record.armor, fallback.armorMax, 0, 40),
    screenRating: wholeNumberFrom(record.screenRating ?? record.screens, fallback.screenRating, 0, 3),
    weapons: Array.isArray(record.weapons) ? record.weapons.map(normalizeWeaponMount) : [newWeaponMount()],
    fighterEnduranceMax: wholeNumberFrom(record.fighterEnduranceMax ?? record.fighterEndurance, fallback.fighterEnduranceMax, 0, 24),
    fighterEnduranceUsed: wholeNumberFrom(record.fighterEnduranceUsed ?? record.fighterUsed, fallback.fighterEnduranceUsed, 0, 24),
    fighterMaxRange: wholeNumberFrom(record.fighterMaxRange ?? record.fighterRange, fallback.fighterMaxRange, 0, 120),
    fighterStatus: normalizeFighterStatus(record.fighterStatus, fallback.fighterStatus),
    homeCarrierShipId: typeof record.homeCarrierShipId === 'string' ? record.homeCarrierShipId : null,
    homeCarrierName: typeof record.homeCarrierName === 'string' && record.homeCarrierName.trim() ? record.homeCarrierName.trim() : null,
    pointsValue: wholeNumberFrom(record.pointsValue ?? record.points ?? record.npv, 0, 0, 99999),
  };
}

function fleetExportToCsv(fleet: FleetExport) {
  const rows = [
    ['fleetColor', 'name', 'className', 'iconKey', 'thrustRating', 'initialVelocity', 'initialCourse', 'startX', 'startY', 'hullMax', 'armorMax', 'screenRating', 'fighterEnduranceMax', 'fighterEnduranceUsed', 'fighterMaxRange', 'fighterStatus', 'homeCarrierName', 'pointsValue', 'weapons'],
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
      String(ship.fighterEnduranceMax),
      String(ship.fighterEnduranceUsed),
      String(ship.fighterMaxRange),
      ship.fighterStatus,
      ship.homeCarrierName ?? '',
      String(ship.pointsValue ?? 0),
      ship.weapons.map((weapon) => `${weapon.name}|${weapon.attackDice}|${weapon.maxRange}|${weapon.arc}|${weapon.ammoMax}|${weapon.ammoUsed}|${weapon.reloadTurns}`).join(';'),
    ]),
  ];

  return `${rows.map((row) => row.map(csvEscape).join(',')).join('\n')}\n`;
}

function matchLogToCsv(snapshot: MatchSnapshot) {
  const summaryRows = [
    ['section', 'name', 'detail'],
    ['match', snapshot.name, `room ${snapshot.joinCode}, turn ${snapshot.turnNumber}, ${formatPhase(snapshot.phase)}`],
    ['table', `${snapshot.tableWidth} x ${snapshot.tableDepth}`, 'inches'],
    ...snapshot.fleets.map((fleet) => [
      'fleet',
      fleet.name,
      `${fleet.faction ?? 'no faction'}, ${snapshot.participants.find((participant) => participant.id === fleet.ownerParticipantId)?.displayName ?? 'unknown'}`,
    ]),
    ...snapshot.ships.map((ship) => [
      'ship',
      ship.name,
      `pos ${ship.positionX.toFixed(1)},${ship.positionY.toFixed(1)}, V${ship.currentVelocity}/C${ship.currentCourse}, hull ${ship.hullDamage}/${ship.hullMax}, armor ${ship.armorDamage}/${ship.armorMax}, screens ${ship.screenRating}${ship.isDestroyed ? ', destroyed' : ''}`,
    ]),
    ...(snapshot.ordnanceMarkers ?? []).map((marker) => [
      'ordnance',
      marker.name,
      `${marker.markerType} ${normalizeOrdnanceStatus(marker.status)}, pos ${marker.positionX.toFixed(1)},${marker.positionY.toFixed(1)}, endurance ${marker.enduranceRemaining}`,
    ]),
  ];

  const logRows = [
    ['sequence', 'timestamp', 'turnNumber', 'phase', 'category', 'message'],
    ...snapshot.matchLog.map((entry) => [
      String(entry.sequence),
      entry.timestamp,
      String(entry.turnNumber),
      entry.phase,
      entry.category,
      entry.message,
    ]),
  ];

  const toCsv = (rows: string[][]) => rows.map((row) => row.map(csvEscape).join(',')).join('\n');
  return `${toCsv(summaryRows)}\n\n${toCsv(logRows)}\n`;
}

function matchLogToMarkdown(snapshot: MatchSnapshot) {
  const lines = [
    `# ${snapshot.name} After-Action Report`,
    '',
    `Room: ${snapshot.joinCode}`,
    `Table: ${snapshot.tableWidth} x ${snapshot.tableDepth} inches`,
    `Turn: ${snapshot.turnNumber}`,
    `Phase: ${formatPhase(snapshot.phase)}`,
    `Profile: ${formatRulesProfile(snapshot.rulesProfileKey)}`,
    '',
    '## Fleets',
    ...snapshot.fleets.map((fleet) => {
      const owner = snapshot.participants.find((participant) => participant.id === fleet.ownerParticipantId);
      return `- ${fleet.name}${fleet.faction ? ` (${fleet.faction})` : ''}: ${owner?.displayName ?? 'Unknown'}`;
    }),
    '',
    '## Ships',
    ...snapshot.ships.map((ship) => `- ${ship.name}: pos ${ship.positionX.toFixed(1)},${ship.positionY.toFixed(1)}, V${ship.currentVelocity}/C${ship.currentCourse}, hull ${ship.hullDamage}/${ship.hullMax}, armor ${ship.armorDamage}/${ship.armorMax}, screens ${ship.screenRating}${isFighterGroup(ship) ? `, fighters ${ship.fighterStatus} endurance ${ship.fighterEnduranceUsed}/${ship.fighterEnduranceMax} range ${ship.fighterMaxRange}` : ''}${ship.isDestroyed ? ', destroyed' : ''}`),
    '',
    '## Ordnance',
    ...((snapshot.ordnanceMarkers ?? []).length > 0
      ? (snapshot.ordnanceMarkers ?? []).map((marker) => `- ${marker.name}: ${marker.markerType} ${normalizeOrdnanceStatus(marker.status)}, pos ${marker.positionX.toFixed(1)},${marker.positionY.toFixed(1)}, V${marker.speed}/C${marker.course}, endurance ${marker.enduranceRemaining}, dice ${marker.attackDice}, max range ${marker.maxRange}`)
      : ['- None']),
    '',
    '## Battle Log',
    ...snapshot.matchLog.map((entry) => `- T${entry.turnNumber} ${formatPhase(entry.phase)} ${entry.category} ${formatLogTime(entry.timestamp)}: ${entry.message}`),
    '',
  ];

  return `${lines.join('\n')}\n`;
}

function parseCsv(text: string) {
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

function csvEscape(value: string) {
  return /[",\r\n]/.test(value) ? `"${value.replaceAll('"', '""')}"` : value;
}

function getValueFromRows(rows: string[][], headers: string[], header: string) {
  const index = headers.indexOf(header);
  if (index < 0) {
    return '';
  }

  return rows.slice(1).map((row) => row[index]?.trim() ?? '').find(Boolean) ?? '';
}

function downloadText(fileName: string, mimeType: string, text: string) {
  const url = URL.createObjectURL(new Blob([text], { type: `${mimeType};charset=utf-8` }));
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  // Revoking in the same tick can cancel the download in some browsers.
  window.setTimeout(() => URL.revokeObjectURL(url), 0);
}

function formatLogTime(timestamp: string) {
  const date = new Date(timestamp);
  if (Number.isNaN(date.getTime())) {
    return '--:--';
  }

  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
}

function wholeNumberFrom(value: unknown, fallback: number, min: number, max: number) {
  const parsed = typeof value === 'number' ? value : Number(value);
  if (!Number.isFinite(parsed)) {
    return fallback;
  }

  return Math.max(min, Math.min(max, Math.round(parsed)));
}

function numberFrom(value: unknown, fallback: number, min: number, max: number) {
  const parsed = typeof value === 'number' ? value : Number(value);
  if (!Number.isFinite(parsed)) {
    return fallback;
  }

  return Math.max(min, Math.min(max, parsed));
}

function stringFrom(value: unknown, fallback: string) {
  return typeof value === 'string' && value.trim() ? value.trim() : fallback;
}

function normalizeHeader(value: string) {
  return value.trim().toLowerCase().replace(/[^a-z0-9]/g, '');
}

function stripFileExtension(fileName: string) {
  return fileName.replace(/\.[^.]+$/, '').replace(/\.forcesignal-fleet$/i, '').trim();
}

function slugify(value: string) {
  return value.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'fleet';
}

function formatPhase(phase: string) {
  return phase.replace(/([a-z])([A-Z])/g, '$1 $2');
}

function formatRulesProfile(profileKey?: string) {
  if (!profileKey || profileKey === 'full-thrust-light-cinematic') {
    return 'Cinematic space fleet profile';
  }

  return profileKey
    .split(/[-_]/)
    .filter(Boolean)
    .map((part) => `${part.charAt(0).toUpperCase()}${part.slice(1)}`)
    .join(' ');
}

function captureDamageState(ship: Ship): DamageState {
  return {
    hullDamage: ship.hullDamage,
    armorDamage: ship.armorDamage,
    fireControlDamage: ship.fireControlDamage,
    driveDamage: ship.driveDamage,
    weaponDamage: ship.weaponDamage,
  };
}

function buildPreTurnChecklist(snapshot: MatchSnapshot, ownedShipIds: Set<string>) {
  const items: { id: string; text: string; severity: 'ok' | 'warning' | 'blocker' }[] = [];
  const liveShips = snapshot.ships.filter((ship) => !ship.isDestroyed);
  const ownedLiveShips = liveShips.filter((ship) => ownedShipIds.has(ship.id));
  const statuses = new Map(snapshot.orderStatuses.map((status) => [status.shipId, status]));
  const missingPositions = liveShips.filter((ship) => ship.positionX < 0 || ship.positionY < 0 || ship.positionX > snapshot.tableWidth || ship.positionY > snapshot.tableDepth);

  if (snapshot.phase === 'OrderEntry') {
    const unlocked = liveShips.filter((ship) => !statuses.get(ship.id)?.isCommitted);
    const friendlyUnlocked = ownedLiveShips.filter((ship) => !statuses.get(ship.id)?.isCommitted);
    items.push({
      id: 'orders',
      text: unlocked.length === 0 ? 'All live ships have locked orders' : `${unlocked.length} live ship${unlocked.length === 1 ? '' : 's'} missing orders`,
      severity: unlocked.length === 0 ? 'ok' : friendlyUnlocked.length > 0 ? 'blocker' : 'warning',
    });
  }

  if (snapshot.phase === 'Movement') {
    const unrevealed = liveShips.filter((ship) => {
      const status = statuses.get(ship.id);
      return status?.isCommitted && !status.isRevealed;
    });
    items.push({
      id: 'reveals',
      text: unrevealed.length === 0 ? 'All locked orders revealed' : `${unrevealed.length} order${unrevealed.length === 1 ? '' : 's'} still unrevealed`,
      severity: unrevealed.length === 0 ? 'ok' : 'blocker',
    });
  }

  if (snapshot.phase === 'Firing') {
    const armed = ownedLiveShips.filter((ship) => ship.weapons.length > 0 && ship.weaponDamage < ship.weapons.length);
    const fired = new Set(snapshot.firingResults.map((result) => `${result.attackerShipId}:${result.weaponId}`));
    const availableShots = armed.flatMap((ship) => ship.weapons.map((weapon) => ({ ship, weapon }))).filter(({ ship, weapon }) => !fired.has(`${ship.id}:${weapon.id}`));
    items.push({
      id: 'fire',
      text: availableShots.length === 0 ? 'No friendly unfired weapons detected' : `${availableShots.length} friendly weapon${availableShots.length === 1 ? '' : 's'} not logged`,
      severity: availableShots.length === 0 ? 'ok' : 'warning',
    });
  }

  const destroyedWithOrders = snapshot.ships.filter((ship) => ship.isDestroyed && statuses.get(ship.id)?.isCommitted);
  if (destroyedWithOrders.length > 0) {
    items.push({ id: 'destroyed-orders', text: `${destroyedWithOrders.length} destroyed ship${destroyedWithOrders.length === 1 ? ' still has' : 's still have'} orders`, severity: 'warning' });
  }

  const destroyedFired = snapshot.firingResults.filter((result) => snapshot.ships.find((ship) => ship.id === result.attackerShipId)?.isDestroyed);
  if (destroyedFired.length > 0) {
    items.push({ id: 'destroyed-fire', text: `${destroyedFired.length} shot${destroyedFired.length === 1 ? '' : 's'} logged from destroyed ships`, severity: 'warning' });
  }

  const activeOrdnance = (snapshot.ordnanceMarkers ?? []).filter((marker) => normalizeOrdnanceStatus(marker.status) === 'Active');
  if (activeOrdnance.length > 0) {
    items.push({
      id: 'ordnance',
      text: `${activeOrdnance.length} active ordnance marker${activeOrdnance.length === 1 ? ' needs' : 's need'} movement/resolution checks`,
      severity: 'warning',
    });
  }

  const fighterTrouble = liveShips.filter((ship) => isFighterGroup(ship) && ship.fighterStatus !== 'Docked' && ship.fighterEnduranceUsed >= ship.fighterEnduranceMax);
  if (fighterTrouble.length > 0) {
    items.push({
      id: 'fighters',
      text: `${fighterTrouble.length} fighter group${fighterTrouble.length === 1 ? '' : 's'} at endurance limit`,
      severity: 'blocker',
    });
  }

  const carrierOps = liveShips.filter((ship) => normalizeShipIconKey(ship.iconKey, ship.className) === 'carrier');
  const airborneFighters = liveShips.filter((ship) => isFighterGroup(ship) && ship.fighterStatus !== 'Docked');
  if (carrierOps.length > 0 && airborneFighters.length > 0) {
    items.push({
      id: 'carrier-ops',
      text: `${airborneFighters.length} airborne/recovering fighter group${airborneFighters.length === 1 ? '' : 's'} to reconcile with carriers`,
      severity: 'warning',
    });
  }

  if ((snapshot.pointsLimit ?? 0) > 0) {
    const overStrength = snapshot.participants
      .map((participant) => {
        const fleetIds = new Set(snapshot.fleets.filter((fleet) => fleet.ownerParticipantId === participant.id).map((fleet) => fleet.id));
        const total = snapshot.ships.filter((ship) => fleetIds.has(ship.fleetId)).reduce((sum, ship) => sum + (ship.pointsValue ?? 0), 0);
        return { name: participant.displayName, over: total - snapshot.pointsLimit };
      })
      .filter((entry) => entry.over > 0);
    items.push({
      id: 'points',
      text: overStrength.length === 0
        ? `All fleets inside the ${snapshot.pointsLimit} point limit`
        : overStrength.map((entry) => `${entry.name} is ${entry.over} over the ${snapshot.pointsLimit} point limit`).join('; '),
      severity: overStrength.length === 0 ? 'ok' : 'blocker',
    });
  }

  const crippled = liveShips.filter((ship) => ship.hullDamage >= Math.ceil(ship.hullMax / 2));
  if (crippled.length > 0) {
    items.push({
      id: 'crippled',
      text: `${crippled.length} ship${crippled.length === 1 ? '' : 's'} at or past half hull`,
      severity: 'warning',
    });
  }

  const deadInSpace = liveShips.filter((ship) => ship.thrustRating > 0 && ship.driveDamage >= ship.thrustRating);
  if (deadInSpace.length > 0) {
    items.push({
      id: 'dead-in-space',
      text: `${deadInSpace.length} ship${deadInSpace.length === 1 ? '' : 's'} with drives disabled`,
      severity: 'blocker',
    });
  }

  items.push({
    id: 'positions',
    text: missingPositions.length === 0 ? 'All live ships are inside table bounds' : `${missingPositions.length} live ship${missingPositions.length === 1 ? '' : 's'} outside table bounds`,
    severity: missingPositions.length === 0 ? 'ok' : 'warning',
  });

  return items;
}

function draftFor(shipId: string, drafts: Record<string, DraftOrder>): DraftOrder {
  return drafts[shipId] ?? createDraftOrder();
}

function createDraftOrder(): DraftOrder {
  return {
    velocityDelta: 0,
    turnSteps: 0,
    turnDirection: 'None',
    turnManeuvers: [],
    salt: crypto.randomUUID(),
  };
}

async function get<T>(path: string): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`);
  if (!response.ok) {
    throw await createApiError(response);
  }

  return response.json();
}

async function post<T>(path: string, body: unknown, token?: string): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { 'X-Participant-Token': token } : {}),
    },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    throw await createApiError(response);
  }

  return response.json();
}

async function createApiError(response: Response) {
  const fallback = `Request failed with status ${response.status}.`;
  const contentType = response.headers.get('content-type') ?? '';
  if (!contentType.includes('application/problem+json') && !contentType.includes('application/json')) {
    return new ApiRequestError(fallback, response.status);
  }

  const problem = await response.json() as { title?: string; detail?: string };
  return new ApiRequestError(problem.detail ?? problem.title ?? fallback, response.status);
}

function readJson<T>(key: string): T | null {
  const value = localStorage.getItem(key);
  if (!value) {
    return null;
  }

  try {
    return JSON.parse(value) as T;
  } catch {
    localStorage.removeItem(key);
    return null;
  }
}

function showError(setMessage: (message: string) => void) {
  return (error: unknown) => {
    setMessage(error instanceof Error ? error.message : 'Something went wrong.');
  };
}

createRoot(document.getElementById('app')!).render(<App />);

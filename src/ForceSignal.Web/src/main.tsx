import { useEffect, useMemo, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import * as signalR from '@microsoft/signalr';
import './style.css';
import { CourseCompass, DamageControl, DamageControlPanel, DamageMeter, FiringConsole, PreTurnChecklist, ShipEditor, ShipProfileFields } from './components/ShipCard.tsx';
import { PlayMap } from './components/map/PlayMap.tsx';
import { carrierImportRank, fleetPoints, nextShipName, shipsPoints } from './lib/fleetMath.ts';
import { bearingArc, captureDamageState, effectiveScreens, firingDraftFor, firingTurnBlocker, focusedFirstShips, hullRowsOf } from './lib/rules.ts';
import { normalizeMatchSnapshot } from './lib/normalize.ts';
import { matchLogToCsv, matchLogToMarkdown } from './lib/reporting.ts';
import { fleetExportToCsv, parseFleetExport, toFleetExport } from './lib/fleetIo.ts';
import { clampDraftForShip, createDraftOrder, draftFor, formatTurnSequence, maxLegalTurn, previewCourse, resetOrderDraft, toOrder, totalTurnSteps, turnManeuversForDraft, turnPatchFromManeuvers, usableThrust } from './lib/movement.ts';
import { defaultShipForm, draftsKey, fleetLibraryKey, officialRulesUrl, sessionKey, snapshotBackupKey } from './constants.ts';
import { downloadText, formatLogTime, formatPhase, formatRulesProfile, slugify } from './lib/format.ts';
import {
  ApiRequestError,
  apiBaseUrl,
  get,
  post,
  readJson,
  showError,
  writeStorage,
} from './lib/api.ts';
import type {
  DamageState,
  DraftOrder,
  FiringDraft,
  FleetExport,
  MatchIdentity,
  MatchRestored,
  MatchSeat,
  MatchSnapshot,
  OrdnanceMarker,
  OrderPreview,
  PendingRestore,
  RepairJob,
  SavedFleet,
  Session,
  Ship,
  ShipForm,
  TurnDirection,
  } from './types.ts';

function App() {
  const [displayName, setDisplayName] = useState('Admiral');
  const [joinCode, setJoinCode] = useState('');
  const [session, setSession] = useState<Session | null>(() => readJson(sessionKey));
  const [snapshot, setSnapshotState] = useState<MatchSnapshot | null>(null);
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
  const [storageWarning, setStorageWarning] = useState<string | null>(null);
  // What the other player is doing, kept apart from what the app is telling *you*. Background
  // traffic used to share one line with error messages and simply overwrote them.
  const [activity, setActivity] = useState('');
  // True while an action that changes the game is in flight. Buttons were freely double-tappable,
  // and on a slow link a player who saw nothing happen would tap again - firing the same weapon
  // twice, or skipping a phase with two taps of Advance Turn. The guards that would have caught it
  // are computed from a snapshot that has not come back yet, so the UI did not even grey out.
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);

  /**
   * Runs one game-changing action, refusing to start a second while the first is still going.
   * The ref is what actually enforces it: two taps in the same frame both read the old state.
   */
  function run(work: () => Promise<unknown>) {
    if (busyRef.current) {
      return;
    }

    busyRef.current = true;
    setBusy(true);
    work()
      .catch(showError(setMessage))
      .finally(() => {
        busyRef.current = false;
        setBusy(false);
      });
  }
  const [connectionState, setConnectionState] = useState<'live' | 'reconnecting' | 'offline'>('offline');
  const [pendingRestore, setPendingRestore] = useState<PendingRestore | null>(null);
  const [fleetLibrary, setFleetLibrary] = useState<SavedFleet[]>(() => readJson<SavedFleet[]>(fleetLibraryKey) ?? []);
  const [pointsLimitForm, setPointsLimitForm] = useState('0');
  const [newFleetForm, setNewFleetForm] = useState<{ name: string; faction: string; fleetColor: string } | null>(null);
  const fleetImportInputRef = useRef<HTMLInputElement | null>(null);
  const restoreInputRef = useRef<HTMLInputElement | null>(null);
  const spentDraftTurnRef = useRef<string | null>(null);
  /**
   * The newest slice of the battle log, newest first.
   *
   * The log runs to thousands of entries in a long game, and this used to reverse the whole array
   * and render every entry on every render of the app - which includes every keystroke and every
   * plot on the map, not just log changes. On a tablet that is a guaranteed stutter. The reversal
   * is now memoized on the log itself, and only the recent tail is put in the DOM; the export
   * still writes the whole thing, which is what an after-action record is for.
   */
  const logPageSize = 300;
  const recentLog = useMemo(
    () => (snapshot?.matchLog ?? []).slice(-logPageSize).reverse(),
    [snapshot?.matchLog]);
  const logHiddenCount = Math.max(0, (snapshot?.matchLog ?? []).length - recentLog.length);


  const snapshotVersionRef = useRef(-1);

  /**
   * The only way a snapshot reaches the board.
   *
   * Two players acting inside the same second produce overlapping work: every mutation returns the
   * state it produced, and every realtime notification starts a fresh fetch. Nothing ordered them,
   * so a reply that left the server first could land last and put the board back to a state that
   * had already been superseded. Damage appeared to un-apply, and the checklist and firing guards
   * were computed from it. It corrected itself on the next push - unless that was the turn's last
   * event, in which case the table resolved the turn against a board that was quietly out of date.
   *
   * The server stamps every snapshot with a version that only ever increases, so an older one is
   * simply dropped. This is also the one place normalization happens: before this, only the
   * explicit refetch normalized, and the twenty-odd mutation replies went in raw.
   */
  function applySnapshot(next: MatchSnapshot | null) {
    if (next === null) {
      snapshotVersionRef.current = -1;
      setSnapshotState(null);
      return;
    }

    if (next.version <= snapshotVersionRef.current) {
      return;
    }

    snapshotVersionRef.current = next.version;
    setSnapshotState(normalizeMatchSnapshot(next));
  }

  // The drafts hold the salts that make a locked order revealable, so this is the one write in the
  // app that must not be quietly lost. It is also written first, before the much larger snapshot
  // backup below, so a store that is filling up sheds the backup rather than the salts.
  useEffect(() => {
    if (!writeStorage(draftsKey, drafts) && Object.keys(drafts).length > 0) {
      localStorage.removeItem(snapshotBackupKey);
      if (!writeStorage(draftsKey, drafts)) {
        setStorageWarning(
          'This browser will not store your order keys. Do not close the tab before revealing, and export your order keys to be safe.');
        return;
      }
    }

    setStorageWarning(null);
  }, [drafts]);

  useEffect(() => {
    writeStorage(fleetLibraryKey, fleetLibrary);
  }, [fleetLibrary]);

  useEffect(() => {
    if (snapshot) {
      setPointsLimitForm(String(snapshot.pointsLimit ?? 0));
    }
  }, [snapshot?.pointsLimit]);

  // The local backup is a convenience, not a guarantee - an explicit snapshot export is the real
  // recovery path - so it is written without the battle log. The log is the bulk of a long match's
  // state and is what pushes this store over its quota; the export keeps the whole thing.
  useEffect(() => {
    if (!snapshot) {
      return;
    }

    const stored = { savedAt: new Date().toISOString(), snapshot: { ...snapshot, matchLog: [] } };
    if (!writeStorage(snapshotBackupKey, stored)) {
      localStorage.removeItem(snapshotBackupKey);
    }
  }, [snapshot]);

  // An order is spent once movement resolves. Drop the local drafts then, so the next turn starts
  // from a clean plot instead of previewing - or silently re-locking - last turn's helm.
  //
  // A draft holds the salt without which a locked order can never be revealed, and the salt exists
  // nowhere else. So a draft whose order is still sealed is kept even though the phase has moved
  // on: this used to clear everything the moment any snapshot arrived in the firing phase, which
  // meant the opponent advancing the turn while one of your ships was locked-but-unrevealed
  // destroyed the only copy of that salt, permanently and with no warning.
  //
  // The stale firing drafts go at the same moment. Ships move every turn, so a range typed last
  // turn describes a distance that no longer exists, and the console would happily have resolved a
  // close-range volley the map disagreed with.
  useEffect(() => {
    if (!snapshot || snapshot.phase !== 'Firing') {
      return;
    }

    const turnKey = `${snapshot.matchId}:${snapshot.turnNumber}`;
    if (spentDraftTurnRef.current === turnKey) {
      return;
    }

    spentDraftTurnRef.current = turnKey;
    setFiringDrafts({});
    setDrafts((current) => {
      const unresolved: Record<string, DraftOrder> = {};
      for (const [shipId, draft] of Object.entries(current)) {
        const status = snapshot.orderStatuses.find((entry) => entry.shipId === shipId);
        if (status?.isCommitted && !status.isRevealed) {
          unresolved[shipId] = draft;
        }
      }

      return unresolved;
    });
  }, [snapshot?.matchId, snapshot?.turnNumber, snapshot?.phase]);

  useEffect(() => {
    if (!session) {
      return;
    }

    localStorage.setItem(sessionKey, JSON.stringify(session));
    loadSnapshot(session.matchId).catch(handleSessionError);

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${apiBaseUrl}/hubs/match`)
      // The default policy tries four times over thirty seconds and then stops for good. A venue
      // wifi drop lasting longer than that left the client permanently deaf to notifications while
      // still feeling alive, because REST calls kept working - so the board quietly diverged from
      // the table for the rest of the game. This one keeps trying, backing off to half a minute.
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (context) =>
          Math.min(30_000, 1_000 * 2 ** Math.min(context.previousRetryCount, 5)),
      })
      .build();

    connection.on('MatchSnapshotChanged', (matchId: string, version: number, reason: string) => {
      if (matchId === session.matchId) {
        // Background traffic goes to the activity line, never to the message line. Writing it to
        // the message line overwrote whatever error the player was reading, so a failed action
        // vanished a fraction of a second later and left no evidence it had happened at all.
        setActivity(`${reason} · v${version}`);
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
    // The old state is cleared only once the new room exists. Clearing first threw away the order
    // keys of the match already in progress whenever the request failed - a typo'd code, or now a
    // rate-limited attempt that never reached the match logic at all.
    const response = await post<Session & { matchId: string; joinCode: string }>('/api/matches', {
      displayName,
      matchName: `${displayName}'s Match`,
    });
    clearLocalMatchState();
    setSession(response);
    setMessage(`Created room ${response.joinCode}.`);
  }

  async function joinMatch() {
    try {
      const response = await post<Session & { matchId: string; joinCode: string }>('/api/matches/join', {
        displayName,
        joinCode,
      });
      clearLocalMatchState();
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
        seats: await get<MatchSeat[]>(
          `/api/matches/${identity.matchId}/seats?joinCode=${encodeURIComponent(identity.joinCode)}`),
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
      // The room code is what proves this device belongs at the table. A restored match has no
      // prior token to present, and a match id proves nothing - it is handed out by the room-code
      // lookup and echoed in every notification.
      joinCode: restored.joinCode,
    });
    setPendingRestore(null);
    setSession(claimed);
    setMessage(`Took command as ${seat.displayName}.`);
  }

  async function loadSnapshot(matchId: string) {
    // The snapshot is the whole game, so it is only for the people at the table. The token proves
    // this device is one of them; the match id proves nothing, since it is handed out by the
    // room-code lookup and echoed in every notification.
    applySnapshot(await get<MatchSnapshot>(`/api/matches/${matchId}/snapshot`, session?.participantToken));
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
      fireControlMax: shipForm.fireControlMax,
      pointDefenseSystems: shipForm.pointDefenseSystems,
      fighterBays: shipForm.fighterBays,
      damageControlParties: shipForm.damageControlParties,
      weapons: shipForm.weapons,
      iconKey: shipForm.iconKey,
      fighterEnduranceMax: shipForm.fighterEnduranceMax,
      fighterEnduranceUsed: shipForm.fighterEnduranceUsed,
      fighterMaxRange: shipForm.fighterMaxRange,
      fighterStatus: shipForm.fighterStatus,
      homeCarrierShipId: shipForm.homeCarrierShipId || null,
      pointsValue: shipForm.pointsValue,
    });
    applySnapshot(created);
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
        fireControlMax: ship.fireControlMax ?? 1,
        pointDefenseSystems: ship.pointDefenseSystems ?? 0,
        fighterBays: ship.fighterBays ?? 0,
        damageControlParties: ship.damageControlParties ?? 0,
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

    applySnapshot(importedSnapshot);
    setActiveFleetId(fleet.id);
    setMessage(`Brought ${exportData.ships.length} ship${exportData.ships.length === 1 ? '' : 's'} (${fleetPoints(exportData)} pts) into ${exportData.name}.`);
  }

  async function markReady() {
    if (!session) {
      return;
    }

    applySnapshot(await post<MatchSnapshot>(
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
    applySnapshot(await post<MatchSnapshot>(`/api/matches/${session.matchId}/turns/current/orders/commit`, {
      participantToken: session.participantToken,
      shipId: ship.id,
      order: toOrder(draft),
      salt: draft.salt,
    }));
  }

  /**
   * Asks the server where a draft would put a ship. Read-only: it locks nothing, logs nothing, and
   * deliberately does not touch the snapshot, so plotting does not wake the rest of the table.
   */
  async function previewOrder(ship: Ship, draft: DraftOrder) {
    if (!session) {
      throw new Error('A session is required to preview an order.');
    }

    return post<OrderPreview>(`/api/matches/${session.matchId}/turns/current/orders/preview`, {
      participantToken: session.participantToken,
      shipId: ship.id,
      order: toOrder(draft),
    });
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

    applySnapshot(await post<MatchSnapshot>(`/api/matches/${session.matchId}/turns/current/orders/reveal`, {
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
      !ship.isDestroyed
      && !latest.orderStatuses.find((status) => status.shipId === ship.id)?.isCommitted
      // A ship the player never plotted needs no order: it holds course and speed.
      && Boolean(drafts[ship.id])
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

    // Saying "that is my plotting done" is what closes order entry. Ships without an order hold
    // their course and speed rather than blocking the turn.
    latest = await post<MatchSnapshot>(`/api/matches/${session.matchId}/turns/current/orders/complete`, {
      participantToken: session.participantToken,
    });
    applySnapshot(latest);
    const holding = ownedShips.filter((ship) => (
      !ship.isDestroyed && !latest.orderStatuses.find((status) => status.shipId === ship.id)?.isCommitted
    )).length;
    const lockedNote = unlockedShips.length === 0 ? 'No new orders to lock' : `Locked ${unlockedShips.length} order${unlockedShips.length === 1 ? '' : 's'}`;
    setMessage(holding === 0
      ? `${lockedNote}. Plotting closed.`
      : `${lockedNote}. Plotting closed; ${holding} ship${holding === 1 ? ' holds' : 's hold'} course and speed.`);
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

    applySnapshot(latest);
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

    applySnapshot(await post<MatchSnapshot>(`/api/ships/${ship.id}/damage`, {
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

    applySnapshot(await post<MatchSnapshot>(`/api/ships/${ship.id}/profile`, {
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
      fireControlMax: form.fireControlMax,
      pointDefenseSystems: form.pointDefenseSystems,
      fighterBays: form.fighterBays,
      damageControlParties: form.damageControlParties,
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

    applySnapshot(await post<MatchSnapshot>(`/api/ships/${ship.id}/duplicate`, {
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

    applySnapshot(await post<MatchSnapshot>(`/api/ships/${ship.id}/fighter-ops`, {
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

    applySnapshot(await post<MatchSnapshot>(`/api/matches/${session.matchId}/ordnance`, {
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

    applySnapshot(await post<MatchSnapshot>(`/api/ordnance/${marker.id}`, {
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

    applySnapshot(await post<MatchSnapshot>(`/api/ordnance/${marker.id}/remove`, {
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
      arc: bearingArc(ship, snapshot?.ships.find((item) => item.id === draft.targetShipId)),
      targetSystem: draft.targetSystem ?? null,
      targetSystemWeaponId: draft.targetSystemWeaponId ?? null,
    });
    applySnapshot(fired);
    const target = fired.ships.find((item) => item.id === draft.targetShipId);
    setMessage(`${ship.name} fired at ${target?.name ?? 'target'} at range ${draft.range}.`);
  }

  async function attemptRepairs(ship: Ship, jobs: RepairJob[]) {
    if (!session || jobs.length === 0) {
      return;
    }

    const repaired = await post<MatchSnapshot>(`/api/ships/${ship.id}/repair`, {
      participantToken: session.participantToken,
      jobs: jobs.map((job) => ({ kind: job.kind, weaponId: job.weaponId ?? null, parties: job.parties })),
    });
    applySnapshot(repaired);
    const note = repaired.matchLog.find((entry) => entry.category === 'Repair');
    setMessage(note?.message ?? `${ship.name} worked its damage control.`);
  }

  async function moveFighterGroup(ship: Ship, x: number, y: number) {
    if (!session) {
      return;
    }

    const flown = await post<MatchSnapshot>(`/api/matches/${session.matchId}/fighters/move`, {
      participantToken: session.participantToken,
      shipId: ship.id,
      positionX: x,
      positionY: y,
    });
    applySnapshot(flown);
    setMessage(`${ship.name} flew to ${x.toFixed(1)}, ${y.toFixed(1)}.`);
  }

  async function ceaseFire(ship: Ship) {
    if (!session) {
      return;
    }

    const closed = await post<MatchSnapshot>(`/api/matches/${session.matchId}/turns/current/cease-fire`, {
      participantToken: session.participantToken,
      shipId: ship.id,
    });
    applySnapshot(closed);
    setMessage(`${ship.name} finished firing. Any threshold checks it earned have been rolled.`);
  }

  async function updateRulesLayer(layer: string) {
    if (!session) {
      return;
    }

    const switched = await post<MatchSnapshot>(`/api/matches/${session.matchId}/rules-layer`, {
      participantToken: session.participantToken,
      rulesLayer: layer,
    });
    applySnapshot(switched);
    setMessage(switched.matchLog.find((entry) => entry.message.startsWith('Rules layer set'))?.message
      ?? `Rules layer set to ${layer}.`);
  }

  async function updatePointsLimit() {
    if (!session) {
      return;
    }

    const limit = Math.max(0, Math.min(99999, Math.round(Number(pointsLimitForm) || 0)));
    applySnapshot(await post<MatchSnapshot>(`/api/matches/${session.matchId}/points-limit`, {
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

    applySnapshot(created);
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

    applySnapshot(await post<MatchSnapshot>(`/api/matches/${session.matchId}/table`, {
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

    applySnapshot(await post<MatchSnapshot>(
      `/api/matches/${session.matchId}/turns/current/advance`,
      {},
      session.participantToken,
    ));
  }

  function clearSession() {
    clearLocalMatchState();
    setSession(null);
    applySnapshot(null);
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
          {/* Creative Commons Attribution requires the artists to be credited wherever the icons
              are used, which includes a build someone else is running. This is that credit; the
              other two copies are ATTRIBUTION.md and the icon sheet's own header. */}
          <p className="icon-credit">
            Ground unit icons made by Lorc, Delapouite, Skoll and sbed, available on{' '}
            <a href="https://game-icons.net" target="_blank" rel="noreferrer">game-icons.net</a>
            {' '}under{' '}
            <a href="https://creativecommons.org/licenses/by/3.0/" target="_blank" rel="noreferrer">CC BY 3.0</a>.
            Ship icons are original to ForceSignal.
          </p>
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
              disabled={seat.isClaimed || busy}
              onClick={() => run(() => claimSeat(pendingRestore, seat))}
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
          <button onClick={() => run(createMatch)} disabled={busy}>Create Match</button>
          <label>
            Room code
            <input value={joinCode} onChange={(event) => setJoinCode(event.target.value.toUpperCase())} />
          </label>
          <button onClick={() => run(joinMatch)} disabled={busy}>Join Match</button>
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
              <span className="label">Rules layer</span>
              <div className="table-fields">
                <label>
                  Layer
                  <select
                    value={snapshot?.rulesLayer ?? 'LightCinematic'}
                    disabled={snapshot?.phase !== 'FleetSetup'}
                    onChange={(event) => updateRulesLayer(event.target.value).catch(showError(setMessage))}
                  >
                    <option value="LightCinematic">Light / 2nd edition</option>
                    <option value="FleetBook">Fleet Book</option>
                  </select>
                </label>
              </div>
              <p className="privacy">
                Settled during fleet setup. The Fleet Book layer drops level-three screens, flies fighter
                groups 24 instead of 12, and reaches 12 with a needle beam instead of 9.
              </p>
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
              <button onClick={() => run(markReady)} disabled={busy}>Ready</button>
              <button className="ghost" onClick={() => run(lockOwnedOrders)} disabled={busy}>Lock Fleet Orders</button>
              <button className="ghost" onClick={() => run(revealOwnedOrders)} disabled={busy}>Reveal Fleet Orders</button>
              <button onClick={() => run(advanceTurn)} disabled={busy}>Advance Turn</button>
              <button className={publicMode ? 'ghost active' : 'ghost'} onClick={() => {
                setPublicMode((current) => !current);
                setActiveView('map');
              }}>Public Display</button>
              <button className="ghost" onClick={printShipCards}>Print Cards</button>
              <button className="ghost" onClick={exportSnapshotBackup}>Save Snapshot</button>
              <button
                className="ghost"
                onClick={() => {
                  // Directly beneath Save Snapshot, and it drops the seat this device is holding.
                  if (window.confirm('Leave this match on this device? Save a snapshot first if you want to come back to it.')) {
                    clearSession();
                  }
                }}
              >Leave Device Session</button>
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
              <p aria-live="polite">{message}</p>
              {activity ? <p className="activity-line" aria-live="polite">{activity}</p> : null}
              {storageWarning ? (
                // Losing the order keys means a locked order can never be revealed, which stops the
                // turn dead. That is worth interrupting someone over rather than logging quietly.
                <p className="storage-warning" role="alert">{storageWarning}</p>
              ) : null}
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
                    <ShipProfileFields
                      form={shipForm}
                      onChange={setShipForm}
                      maxScreenLevel={snapshot?.rulesLayer === 'FleetBook' ? 2 : 3}
                    />
                    <button onClick={() => run(createShipFromForm)} disabled={busy}>Add Ship</button>
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
                      {effectiveScreens(ship) > 0 ? <span>Screens {effectiveScreens(ship)}</span> : null}
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
                        <strong>{effectiveScreens(ship)}</strong>
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
                          <button onClick={() => run(() => commit(ship))} disabled={busy}>Lock</button>
                          <button onClick={() => run(() => reveal(ship))} disabled={busy}>Reveal</button>
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
                          onFire={() => run(() => fireWeapon(ship, firingDraft))}
                          volleyOpen={snapshot.firingShipId === ship.id}
                          turnProblem={firingTurnBlocker(ship, snapshot, session.participantId)}
                          canEndFire={snapshot.firingParticipantId === session.participantId && !(snapshot.activatedShipIds ?? []).includes(ship.id)}
                          onCeaseFire={() => run(() => ceaseFire(ship))}
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
                            rows={hullRowsOf(ship)}
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
                            max={ship.fireControlMax ?? 1}
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
                            <button
                              className="ghost"
                              type="button"
                              onClick={() => {
                                // Sits in a row of ordinary damage buttons and writes the hull
                                // straight to its maximum. A mis-tap kills a ship outright, and
                                // the single-slot undo is gone the moment anything else is
                                // recorded.
                                if (window.confirm(`Mark ${ship.name} destroyed? This fills its hull damage.`)) {
                                  run(() => updateDamage(ship, { hullDamage: ship.hullMax }));
                                }
                              }}
                            >Destroy</button>
                            <button className="ghost" type="button" disabled={!damageUndo} onClick={() => undoLastDamage().catch(showError(setMessage))}>Undo Damage</button>
                          </div>
                        </div>
                        {snapshot.phase === 'OrderEntry' || snapshot.phase === 'OrdersLocked' ? (
                          <DamageControlPanel
                            ship={ship}
                            onRepair={(jobs) => attemptRepairs(ship, jobs).catch(showError(setMessage))}
                          />
                        ) : null}
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
                onPreviewOrder={previewOrder}
                onFire={(ship, draft) => fireWeapon(ship, draft)}
                onFlyFighters={(ship, x, y) => moveFighterGroup(ship, x, y)}
              onCeaseFire={(ship) => ceaseFire(ship)}
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
                {recentLog.map((entry) => (
                  <li key={entry.sequence}>
                    <span>T{entry.turnNumber} · {formatPhase(entry.phase)} · {entry.category} · {formatLogTime(entry.timestamp)}</span>
                    <p>{entry.message}</p>
                  </li>
                ))}
              </ol>
              {logHiddenCount > 0 ? (
                <p className="log-truncated">
                  Showing the most recent {recentLog.length} of {(snapshot?.matchLog ?? []).length} entries.
                  Export the log for the full after-action record.
                </p>
              ) : null}
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




createRoot(document.getElementById('app')!).render(<App />);

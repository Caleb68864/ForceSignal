/**
 * The tactical map: the table drawn to scale, with everything on it and the overlays that explain
 * what is in range of what.
 *
 * This is the view a table spends the firing phase looking at, so the overlays are the point
 * rather than decoration - weapon arcs, fighter reach, resolved movement trails, and the ruler.
 * PlayMap owns the viewport and the pointer handling; everything else here draws one layer.
 */

import { useEffect, useRef, useState, type CSSProperties } from 'react';
import { ShipIcon } from '../../components/ShipCard.tsx';
import { fighterMoveAllowance, fighterStatuses, firableArcs } from '../../constants.ts';
import { courseAngle, distanceBetweenShips, mapPercent, rangeDiameterPercent, weaponArcAngle } from '../../lib/geometry.ts';
import { clampMapViewport, courseFromTablePoint, measureCourse, measureDistance, tablePointFromClient, trailPointsForResult, viewportZoomedAt } from '../../lib/mapGeometry.ts';
import { appendTurnPatchForCourse, draftFor, estimateDraftEndpoint, formatTurnSequence, maxLegalTurn, previewCourse, totalTurnSteps, usableThrust } from '../../lib/movement.ts';
import { normalizeFleetColor, normalizeOrdnanceStatus, normalizeShipIconKey } from '../../lib/normalize.ts';
import { arcBlocker, arcLabel, bearingArc, describeArcs, effectiveScreens, fighterEnduranceRange, fireControlBlocker, firingDraftFor, firingTargetOptions, firingTurnBlocker, isFighterGroup, needleTargets, torpedoToHitNumber, workingFireControl } from '../../lib/rules.ts';
import type { DraftOrder, FighterStatus, FiringDraft, FiringResult, Fleet, MatchSnapshot, MovementResult, OrdnanceMarker, Participant, Ship, TablePoint } from '../../types.ts';

export function PlayMap({
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
  onCeaseFire,
  onFlyFighters,
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
  onCeaseFire: (ship: Ship) => Promise<void>;
  onFlyFighters: (ship: Ship, x: number, y: number) => Promise<void>;
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

  // A long press is half a second of pending timer. Switching away from the map inside that window
  // unmounted this component while the timer was still armed, and it then fired into a component
  // that was gone - assigning a firing target the player had started to press and deliberately
  // moved away from. Cancel both timers on the way out.
  useEffect(() => () => {
    clearLongPress();
    clearMarkerLongPress();
    // The cleanup runs once, on unmount; the two clear functions close over refs rather than state.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

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
    const next: FiringDraft = {
      ...current,
      ...patch,
      weaponId: weapon?.id ?? current.weaponId,
      targetShipId: target?.id ?? current.targetShipId,
      range: patch.range ?? range,
    };
    onFiringDraftChange(ship, next);
    if (target) {
      const arc = bearingArc(ship, target);
      setMapNotice(`${ship.name} solution: ${weapon?.name ?? 'weapon'} on ${target.name}, range ${next.range}, bears ${arc ? arcLabel(arc) : 'unknown'}`);
    }
  }

  function plotFromClientPoint(clientX: number, clientY: number, shipOverride?: Ship) {
    const shipToPlot = shipOverride ?? selectedShip;
    if (!shipToPlot || !ownedShipIds.has(shipToPlot.id) || shipToPlot.isDestroyed || !tableRef.current) {
      setMapNotice('Select an operational friendly ship');
      return false;
    }

    const point = tablePointFromClient(tableRef.current, clientX, clientY, viewport, snapshot.tableWidth, snapshot.tableDepth);

    // A fighter group takes no orders: it simply flies to the spot, up to its allowance.
    if (isFighterGroup(shipToPlot)) {
      const reach = Math.hypot(point.x - shipToPlot.positionX, point.y - shipToPlot.positionY);
      if (reach > fighterMoveAllowance) {
        setMapNotice(`${shipToPlot.name} can fly ${fighterMoveAllowance}; that spot is ${reach.toFixed(1)} away`);
        return false;
      }

      onFlyFighters(shipToPlot, point.x, point.y).catch((error) => setMapNotice(error instanceof Error ? error.message : String(error)));
      onFocus(shipToPlot.id);
      return true;
    }

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
                volleyOpen={snapshot.firingShipId === selectedShip.id}
                turnProblem={firingTurnBlocker(selectedShip, snapshot, ownerParticipantId ?? '')}
                canEndFire={Boolean(ownerParticipantId) && snapshot.firingParticipantId === ownerParticipantId && !(snapshot.activatedShipIds ?? []).includes(selectedShip.id)}
                onCeaseFire={() => onCeaseFire(selectedShip).catch((error) => setMapNotice(error instanceof Error ? error.message : String(error)))}
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
              <strong>{effectiveScreens(selectedShip)}</strong>
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
          <dd className={ship.hullDamage >= Math.ceil(ship.hullMax / 2) ? 'hurt' : ''}>{ship.hullDamage}/{ship.hullMax}{ship.hullDamage >= Math.ceil(ship.hullMax / 2) && !ship.isDestroyed ? ' · half hull' : ''}</dd>
        </div>
        <div>
          <dt>Armor</dt>
          <dd>{ship.armorDamage}/{ship.armorMax}{effectiveScreens(ship) > 0 ? ` · screens ${effectiveScreens(ship)}` : ''}</dd>
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
      <label title="How far this launcher can throw a salvo: 24 for a standard load, 36 for extended range.">
        Reach
        <input type="number" min="1" max="120" value={draft.maxRange} onChange={(event) => setDraft({ ...draft, maxRange: Number(event.target.value) })} />
      </label>
      <p className="privacy">
        A salvo is aimed at a point, not a ship. It launches on the firing ship and can be dragged to its
        point of aim within that reach; after movement it strikes the closest enemy within 6.
      </p>
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
        const allRound = weapon.arcs.length >= firableArcs.length;
        return (
          <span
            key={weapon.id}
            className={allRound ? 'weapon-range all' : 'weapon-range'}
            style={{
              left: `${left}%`,
              top: `${top}%`,
              width: `${width}%`,
              height: `${height}%`,
              '--arc-index': index,
              '--arc-angle': `${weaponArcAngle(ship.currentCourse, weapon.arcs[0] ?? 'Fore')}deg`,
            } as CSSProperties}
          >
            <i className="weapon-range-band half" />
            <i className="weapon-range-band close" />
            {allRound ? (
              <em className="weapon-arc-label all">All round {weapon.maxRange}</em>
            ) : (
              <>
                {weapon.arcs.map((arc) => (
                  <i
                    key={arc}
                    className="weapon-arc-wedge"
                    style={{ transform: `translate(-50%, -50%) rotate(${weaponArcAngle(ship.currentCourse, arc)}deg)` }}
                  />
                ))}
                <i className="weapon-arc-spoke" />
                <em className="weapon-arc-label">{describeArcs(weapon.arcs)} {weapon.maxRange}</em>
              </>
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
  volleyOpen,
  turnProblem,
  canEndFire,
  onCeaseFire,
}: {
  ship: Ship;
  ships: Ship[];
  ownedShipIds: Set<string>;
  draft: FiringDraft;
  phase: string;
  firingResults: FiringResult[];
  onChange: (patch: Partial<FiringDraft>) => void;
  onFire: () => void;
  volleyOpen: boolean;
  turnProblem: string | null;
  canEndFire: boolean;
  onCeaseFire: () => void;
}) {
  const targetOptions = firingTargetOptions(ship, ships, ownedShipIds);
  const weapon = ship.weapons.find((item) => item.id === draft.weaponId) ?? ship.weapons[0];
  const target = targetOptions.find((candidate) => candidate.id === draft.targetShipId) ?? targetOptions[0];
  const estimatedRange = target ? Math.max(1, Math.round(distanceBetweenShips(ship, target))) : 0;
  const targetArc = bearingArc(ship, target);
  const arcProblem = arcBlocker(ship, target, weapon);
  const fireControlProblem = fireControlBlocker(ship, target, firingResults);
  const mountLost = Boolean(weapon?.isDestroyed);
  const needsSystem = weapon?.kind === 'NeedleBeam' && !draft.targetSystem;
  const inRange = Boolean(weapon) && draft.range > 0 && draft.range <= (weapon?.maxRange ?? 0);
  const weaponSpent = Boolean(weapon) && firingResults.some((result) => result.attackerShipId === ship.id && result.weaponId === weapon?.id);
  const ammoEmpty = Boolean(weapon) && weapon!.ammoMax > 0 && weapon!.ammoUsed >= weapon!.ammoMax;
  const canFire = phase === 'Firing' && Boolean(target) && Boolean(weapon) && inRange && !ship.isDestroyed && !weaponSpent && !ammoEmpty && !arcProblem && !mountLost && !fireControlProblem && !turnProblem && !needsSystem;
  const firingNote = needsSystem
    ? 'Name the system this needle is aimed at'
    : turnProblem
    ? turnProblem
    : !weapon
    ? 'No weapon mounted'
    : mountLost
      ? 'Mount knocked out'
      : fireControlProblem
        ? fireControlProblem
      : !target
      ? 'No target selected'
      : arcProblem
        ? arcProblem
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
            return <option key={mount.id} value={mount.id}>{mount.name} · {mount.maxRange}{ammo}{mount.isDestroyed ? ' · knocked out' : spent ? ' · spent' : ''}</option>;
          })}
        </select>
      </label>
      <div className="bearing-readout">
        <span className="label">Bearing</span>
        <strong>{targetArc ? arcLabel(targetArc) : 'no target'}</strong>
        <small>{weapon ? describeArcs(weapon.arcs) : 'no mount'}</small>
        <small>{workingFireControl(ship)} firecon{workingFireControl(ship) === 1 ? '' : 's'}</small>
        {weapon?.kind === 'PulseTorpedo' ? <small>needs {torpedoToHitNumber(draft.range)}+ to hit</small> : null}
        {weapon?.kind === 'NeedleBeam' ? <small>takes a system on a 6</small> : null}
      </div>
      {weapon?.kind === 'NeedleBeam' ? (
        <label>
          Aim at
          <select
            value={draft.targetSystem ? `${draft.targetSystem}:${draft.targetSystemWeaponId ?? ''}` : ''}
            onChange={(event) => {
              const [kind, weaponId] = event.target.value.split(':');
              onChange({ targetSystem: kind || undefined, targetSystemWeaponId: weaponId || undefined });
            }}
          >
            <option value="">Pick a system</option>
            {needleTargets(target).map((option) => (
              <option key={option.key} value={`${option.kind}:${option.weaponId ?? ''}`}>{option.label}</option>
            ))}
          </select>
        </label>
      ) : null}
      <label>
        Range
        <input type="number" min="1" max={weapon?.maxRange ?? 72} value={draft.range} onChange={(event) => onChange({ range: Number(event.target.value) })} />
      </label>
      <button className="ghost" type="button" disabled={!target} onClick={() => onChange({ range: estimatedRange })}>Use Map Solution</button>
      <button type="button" disabled={!canFire} onClick={onFire}>Fire</button>
      {canEndFire ? (
        <button className="ghost volley-close" type="button" onClick={onCeaseFire}>{volleyOpen ? 'Done Firing' : 'Hold Fire'}</button>
      ) : null}
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

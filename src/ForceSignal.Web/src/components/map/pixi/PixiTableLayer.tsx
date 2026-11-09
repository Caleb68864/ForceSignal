/**
 * The Pixi canvas under the DOM map, and the turn playback bar over it.
 *
 * Loaded only when a device turns the animated map on, as its own chunk, so the classic map pays
 * nothing for it. If the canvas cannot start, `onUnavailable` hands the table back to the DOM.
 * The DOM keeps every ship button, the inspector, the ruler and the overlays - this layer only
 * paints underneath them, and the buttons go see-through so taps, keyboard focus and screen
 * readers work exactly as they do on the DOM map.
 *
 * Playback: when the firing phase opens, the turn's moves fly; when new shots arrive, only those
 * play. Skip jumps to the result, Replay plays the whole turn again, and Effects off (or a
 * reduced-motion preference) shows results at once with no animation.
 */

import { useEffect, useMemo, useRef, useState } from 'react';
import { frameAt, prefersReducedMotion, schedulePlayback, startTimeAfter, type PlaybackSchedule } from '../../../lib/playback/playback.ts';
import { captionFor } from '../../../lib/playback/captions.ts';
import { buildTurnTimeline, type MoveEvent } from '../../../lib/playback/timeline.ts';
import type { MapViewport } from '../../../lib/playback/viewTransform.ts';
import type { MatchSnapshot } from '../../../types.ts';
import { TableScene } from './pixiScene.ts';

const effectsKey = 'forcesignal.mapEffects';

function readEffects(): boolean {
  try {
    return localStorage.getItem(effectsKey) !== 'off';
  } catch {
    return true;
  }
}

function writeEffects(on: boolean) {
  try {
    localStorage.setItem(effectsKey, on ? 'on' : 'off');
  } catch {
    // A private window: the toggle still works for this visit.
  }
}

export default function PixiTableLayer({
  snapshot,
  viewport,
  focusedShipId,
  mapElement,
  onUnavailable,
  viewportPreviewRef,
}: {
  snapshot: MatchSnapshot;
  viewport: MapViewport;
  focusedShipId?: string;
  mapElement: HTMLElement | null;
  /** Called when this device cannot run the canvas, so the map can fall back to drawing in the DOM. */
  onUnavailable?: () => void;
  /** Filled with a function the map calls on every move of a drag, before the viewport is committed. */
  viewportPreviewRef?: { current: ((next: MapViewport) => void) | null };
}) {
  const hostRef = useRef<HTMLDivElement | null>(null);
  const sceneRef = useRef<TableScene | null>(null);
  const [ready, setReady] = useState(false);
  const [effects, setEffects] = useState(readEffects);
  const [time, setTime] = useState<number | null>(null);
  const clockRef = useRef<{ origin: number; frame: number } | null>(null);

  // Which turn is on show: this one, or the last one while it is being replayed.
  const [replayTurn, setReplayTurn] = useState<number | null>(null);
  const timeline = useMemo(() => buildTurnTimeline(snapshot, replayTurn ?? snapshot.turnNumber), [snapshot, replayTurn]);
  const reducedMotion = !effects || prefersReducedMotion();
  const schedule = useMemo(() => schedulePlayback(timeline, { reducedMotion }), [timeline, reducedMotion]);
  const moves = useMemo(() => timeline.events.filter((event): event is MoveEvent => event.kind === 'move'), [timeline]);
  const stepCount = schedule.steps.length;
  const shotCount = schedule.steps.filter((step) => step.event.kind === 'fire').length;
  const seen = useRef<{ key: string; steps: number } | null>(null);
  const canReplayLastTurn = timeline.status !== 'resolved' && Boolean(snapshot.lastTurn);

  // Mount the canvas once.
  useEffect(() => {
    const host = hostRef.current;
    if (!host) {
      return undefined;
    }

    const scene = new TableScene();
    sceneRef.current = scene;
    let live = true;
    scene.init(host, snapshot.matchId).then((ok) => {
      if (ok && live) {
        performance.mark('forcesignal:pixi-first-paint');
        setReady(true);
      }
    }).catch((error: unknown) => {
      // No WebGL, a lost context, a blocked GPU: the classic map draws everything the canvas would.
      console.error('[ForceSignal] Pixi map failed to start', error);
      if (live) {
        onUnavailable?.();
      }
    });
    return () => {
      live = false;
      scene.destroy();
      sceneRef.current = null;
    };
    // The match id only seeds the starfield; a new match remounts the map anyway.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Follow the map's size. clientWidth is the content box, which is what `.table-map-content` fills.
  useEffect(() => {
    if (!ready || !mapElement) {
      return undefined;
    }

    const apply = () => sceneRef.current?.resize({ width: mapElement.clientWidth, height: mapElement.clientHeight });
    apply();
    const observer = new ResizeObserver(apply);
    observer.observe(mapElement);
    return () => observer.disconnect();
  }, [ready, mapElement]);

  useEffect(() => {
    if (ready) {
      sceneRef.current?.setViewport(viewport);
    }
  }, [ready, viewport]);

  // A drag moves the canvas directly, frame by frame, without waiting for the map to re-render.
  useEffect(() => {
    if (!ready || !viewportPreviewRef) {
      return undefined;
    }

    viewportPreviewRef.current = (next) => sceneRef.current?.setViewport(next);
    return () => {
      viewportPreviewRef.current = null;
    };
  }, [ready, viewportPreviewRef]);

  // Start playing whatever is new: the whole turn when it first resolves, only the fresh steps after.
  useEffect(() => {
    const key = `${snapshot.matchId}:${timeline.turnNumber}:${timeline.status}`;
    const previous = seen.current;
    seen.current = { key, steps: stepCount };
    if (timeline.status !== 'resolved' || schedule.duration === 0) {
      return;
    }

    if (!previous || previous.key !== key) {
      play(0);
    } else if (stepCount > previous.steps) {
      play(startTimeAfter(schedule, previous.steps));
    }
    // play is stable enough: it only reads refs and the schedule passed in.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [snapshot.matchId, timeline, schedule, stepCount]);

  function stopClock() {
    if (clockRef.current) {
      cancelAnimationFrame(clockRef.current.frame);
      clockRef.current = null;
    }
  }

  function play(from: number) {
    stopClock();
    if (schedule.duration === 0) {
      setTime(null);
      return;
    }

    const origin = performance.now() - from;
    const tick = () => {
      const t = performance.now() - origin;
      if (t >= schedule.duration) {
        clockRef.current = null;
        setTime(null);
        setReplayTurn(null);
        return;
      }

      setTime(t);
      clockRef.current = { origin, frame: requestAnimationFrame(tick) };
    };
    clockRef.current = { origin, frame: requestAnimationFrame(tick) };
  }

  useEffect(() => stopClock, []);

  const frame = frameAt(schedule, time ?? schedule.duration);
  const playing = time !== null;

  // Tell the DOM layer to hide its labels while hulls are mid-flight, so a name is not left
  // floating where the ship is about to arrive.
  useEffect(() => {
    if (mapElement) {
      mapElement.dataset.playback = playing ? 'running' : 'idle';
    }
  }, [mapElement, playing]);

  // Redrawn when what is on the table changes, not on every render of the map around it: a pan
  // re-renders PlayMap on each pointer move, and the scene already follows the viewport above.
  useEffect(() => {
    if (!ready) {
      return;
    }

    const current = frameAt(schedule, time ?? schedule.duration);
    sceneRef.current?.draw({ snapshot, moves, frame: current, pendingKills: pendingKills(schedule, current.settled, time !== null), focusedShipId, playing: time !== null });
  }, [ready, snapshot, moves, schedule, time, focusedShipId]);

  const movesRunning = playing && time !== null && time >= schedule.moveStart && time < schedule.moveEnd;
  const current = frame.active[0]?.step.event ?? (movesRunning ? null : undefined);

  return (
    <>
      <div ref={hostRef} className="pixi-map-host" aria-hidden="true" />
      <div
        className="map-playback-bar"
        onPointerDown={(event) => event.stopPropagation()}
        onContextMenu={(event) => event.stopPropagation()}
      >
        <span className="label">Turn {timeline.turnNumber}{replayTurn !== null ? ' · replay' : ''}</span>
        <p aria-live="polite">
          {current === null ? 'Ships moving' : current ? captionFor(current, snapshot) : statusLine(timeline.status, shotCount)}
        </p>
        <button className="ghost" type="button" disabled={!playing} onClick={() => { stopClock(); setTime(null); setReplayTurn(null); }}>Skip</button>
        {canReplayLastTurn ? (
          // Between turns the last one is kept whole on the snapshot, so it can be watched again.
          <button className="ghost map-replay" type="button" hidden={reducedMotion} onClick={() => setReplayTurn(snapshot.lastTurn?.turnNumber ?? null)}>
            Replay turn {snapshot.lastTurn?.turnNumber}
          </button>
        ) : (
          <button className="ghost map-replay" type="button" hidden={reducedMotion} disabled={timeline.status !== 'resolved'} onClick={() => play(0)}>Replay</button>
        )}
        <label className="map-effects-toggle">
          <input type="checkbox" checked={effects} onChange={(event) => { setEffects(event.target.checked); writeEffects(event.target.checked); }} />
          Effects
        </label>
      </div>
    </>
  );
}

/** Ships the playback will blow up later, still drawn whole until their moment comes. */
function pendingKills(schedule: PlaybackSchedule, settled: number, playing: boolean): Set<string> {
  if (!playing) {
    return new Set();
  }

  const ids = schedule.steps.slice(settled).flatMap(({ event }) => {
    if (event.kind === 'destroyed') {
      return event.event.targetShipId ? [event.event.targetShipId] : [];
    }

    return event.kind === 'fire' && event.destroysTarget ? [event.result.targetShipId] : [];
  });
  return new Set(ids);
}

function statusLine(status: string, shots: number): string {
  switch (status) {
    case 'resolved':
      return shots > 0 ? `${shots} shot${shots === 1 ? '' : 's'} this turn` : 'Movement resolved';
    case 'awaiting-movement':
      return 'Orders revealed - waiting for movement';
    default:
      return 'Nothing resolved yet this turn';
  }
}

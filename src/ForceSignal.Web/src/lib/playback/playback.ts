/**
 * When each event of a timeline plays, and what the table looks like at any moment of it.
 *
 * Kept apart from the renderer so it can be tested without one: the canvas asks `frameAt` for a
 * time and draws what comes back. Moves all play together - the turn's movement is simultaneous -
 * and each shot then plays alone, in the server's order, so a table can follow one at a time.
 *
 * With reduced motion asked for, the schedule has no length and every frame is the end state with
 * no effects running: the result arrives at once, which is what the preference asks for.
 */

import { fxRegistry, impactMs, impactsFor, type FxSpec, type ImpactKind } from './fxRegistry.ts';
import type { FireEvent, MoveEvent, TurnTimeline } from './timeline.ts';
import type { TablePoint } from '../../types.ts';

export type PlaybackOptions = { reducedMotion: boolean; moveMs?: number; gapMs?: number };

export type ScheduledFire = { event: FireEvent; start: number; impactAt: number; end: number; spec: FxSpec; impacts: ImpactKind[] };

export type PlaybackSchedule = {
  moves: MoveEvent[];
  moveEnd: number;
  fires: ScheduledFire[];
  duration: number;
};

export type ShipPose = { x: number; y: number; course: number };

export type ActiveFx = {
  fire: ScheduledFire;
  phase: 'travel' | 'impact';
  /** 0 to 1 through the phase. */
  progress: number;
};

export type PlaybackFrame = {
  /** Only ships with a move this turn; everyone else stands where the snapshot has them. */
  poses: Map<string, ShipPose>;
  active: ActiveFx[];
  /** Shots whose impact has played, so their result can be shown as settled. */
  settled: number;
  done: boolean;
};

export function schedulePlayback(timeline: TurnTimeline, options: PlaybackOptions): PlaybackSchedule {
  const moves = timeline.events.filter((event): event is MoveEvent => event.kind === 'move');
  const shots = timeline.events.filter((event): event is FireEvent => event.kind === 'fire');
  if (options.reducedMotion) {
    return {
      moves,
      moveEnd: 0,
      fires: shots.map((event) => ({ event, start: 0, impactAt: 0, end: 0, spec: fxRegistry[event.fx], impacts: impactsFor(event) })),
      duration: 0,
    };
  }

  const gap = options.gapMs ?? 180;
  const moveEnd = moves.length > 0 ? options.moveMs ?? 1600 : 0;
  let cursor = moveEnd;
  const fires = shots.map((event) => {
    const spec = fxRegistry[event.fx];
    const impacts = impactsFor(event);
    const start = cursor + gap;
    const impactAt = start + spec.travelMs;
    const end = impactAt + Math.max(...impacts.map((impact) => impactMs[impact]));
    cursor = end;
    return { event, start, impactAt, end, spec, impacts };
  });

  return { moves, moveEnd, fires, duration: cursor };
}

/** Where the schedule should start to show only shots after the first `alreadySeen` of them. */
export function startTimeAfter(schedule: PlaybackSchedule, alreadySeen: number): number {
  if (alreadySeen <= 0) {
    return 0;
  }

  return schedule.fires[alreadySeen]?.start ?? schedule.duration;
}

export function frameAt(schedule: PlaybackSchedule, time: number): PlaybackFrame {
  const t = Math.max(0, Math.min(time, schedule.duration));
  const moveProgress = schedule.moveEnd === 0 ? 1 : t / schedule.moveEnd;
  const poses = new Map<string, ShipPose>();
  for (const move of schedule.moves) {
    poses.set(move.shipId, poseAlong(move, easeInOut(Math.min(1, moveProgress))));
  }

  const active: ActiveFx[] = [];
  let settled = 0;
  for (const fire of schedule.fires) {
    if (schedule.duration === 0 || t >= fire.end) {
      settled += 1;
    } else if (t >= fire.impactAt) {
      active.push({ fire, phase: 'impact', progress: (t - fire.impactAt) / (fire.end - fire.impactAt) });
    } else if (t >= fire.start) {
      active.push({ fire, phase: 'travel', progress: (t - fire.start) / (fire.impactAt - fire.start) });
    }
  }

  return { poses, active, settled, done: t >= schedule.duration };
}

/** A ship's place part-way along its legs, measured by distance so a long leg takes longer. */
function poseAlong(move: MoveEvent, progress: number): ShipPose {
  const lengths = move.legs.map((leg) => Math.hypot(leg.to.x - leg.from.x, leg.to.y - leg.from.y));
  const total = lengths.reduce((sum, length) => sum + length, 0);
  if (move.legs.length === 0) {
    return { x: 0, y: 0, course: move.endCourse };
  }

  if (total === 0 || progress >= 1) {
    const last = move.legs[move.legs.length - 1];
    return { ...last.to, course: move.endCourse };
  }

  let remaining = progress * total;
  for (let index = 0; index < move.legs.length; index += 1) {
    const leg = move.legs[index];
    if (remaining <= lengths[index] || index === move.legs.length - 1) {
      const along = lengths[index] === 0 ? 1 : remaining / lengths[index];
      return { ...lerp(leg.from, leg.to, Math.min(1, along)), course: leg.course };
    }

    remaining -= lengths[index];
  }

  return { ...move.legs[move.legs.length - 1].to, course: move.endCourse };
}

function lerp(from: TablePoint, to: TablePoint, amount: number): TablePoint {
  return { x: from.x + (to.x - from.x) * amount, y: from.y + (to.y - from.y) * amount };
}

function easeInOut(value: number): number {
  return value < 0.5 ? 2 * value * value : 1 - ((-2 * value + 2) ** 2) / 2;
}

/** Whether this device asked for less motion. Read once per playback, so a change applies next time. */
export function prefersReducedMotion(): boolean {
  try {
    return typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  } catch {
    return false;
  }
}

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

import { fxRegistry, impactMs, impactsFor, impactsForReported, reportedFx, type FxSpec, type ImpactKind } from './fxRegistry.ts';
import type { FireEvent, MoveEvent, ReportedEvent, TurnTimeline } from './timeline.ts';
import type { TablePoint } from '../../types.ts';

export type PlaybackOptions = { reducedMotion: boolean; moveMs?: number; gapMs?: number };

/** A shot or a reported event, with when it travels and when it lands. */
export type ScheduledStep = { event: FireEvent | ReportedEvent; start: number; impactAt: number; end: number; spec: FxSpec; impacts: ImpactKind[] };

export type PlaybackSchedule = {
  moves: MoveEvent[];
  moveStart: number;
  moveEnd: number;
  /** Everything but the moves, one at a time, in the server's order. */
  steps: ScheduledStep[];
  duration: number;
};

export type ShipPose = { x: number; y: number; course: number };

export type ActiveFx = {
  step: ScheduledStep;
  phase: 'travel' | 'impact';
  /** 0 to 1 through the phase. */
  progress: number;
};

export type PlaybackFrame = {
  /** Only ships that move or fly this turn; everyone else stands where the snapshot has them. */
  poses: Map<string, ShipPose>;
  /** Only ordnance markers that move this turn, by marker id; the rest stand where the snapshot has them. */
  markers: Map<string, TablePoint>;
  active: ActiveFx[];
  /** Steps whose impact has played, so their result can be shown as settled. */
  settled: number;
  done: boolean;
};

export function schedulePlayback(timeline: TurnTimeline, options: PlaybackOptions): PlaybackSchedule {
  const moves = timeline.events.filter((event): event is MoveEvent => event.kind === 'move');
  const others = timeline.events.filter((event): event is FireEvent | ReportedEvent => event.kind !== 'move');
  const specFor = (event: FireEvent | ReportedEvent) => (event.kind === 'fire' ? fxRegistry[event.fx] : reportedFx[event.kind]);
  const impactsOf = (event: FireEvent | ReportedEvent) => (event.kind === 'fire' ? impactsFor(event) : impactsForReported(event));

  if (options.reducedMotion) {
    return {
      moves,
      moveStart: 0,
      moveEnd: 0,
      steps: others.map((event) => ({ event, start: 0, impactAt: 0, end: 0, spec: specFor(event), impacts: impactsOf(event) })),
      duration: 0,
    };
  }

  const gap = options.gapMs ?? 180;
  const moveMs = options.moveMs ?? 1600;
  const moveStep = moves[0]?.step;
  let cursor = 0;
  let moveStart = 0;
  let moveEnd = 0;
  let movesScheduled = moves.length === 0;
  const steps: ScheduledStep[] = [];

  for (const event of others) {
    // The moves play at their place in the order, which is after any fighter flights.
    if (!movesScheduled && moveStep !== undefined && event.step > moveStep) {
      moveStart = cursor;
      moveEnd = cursor + moveMs;
      cursor = moveEnd;
      movesScheduled = true;
    }

    const spec = specFor(event);
    const impacts = impactsOf(event);
    const start = cursor + gap;
    const impactAt = start + spec.travelMs;
    const end = impactAt + Math.max(0, ...impacts.map((impact) => impactMs[impact]));
    cursor = end;
    steps.push({ event, start, impactAt, end, spec, impacts });
  }

  if (!movesScheduled) {
    moveStart = cursor;
    moveEnd = cursor + moveMs;
    cursor = moveEnd;
  }

  return { moves, moveStart, moveEnd, steps, duration: cursor };
}

/** Where the schedule should start to show only the steps after the first `alreadySeen` of them. */
export function startTimeAfter(schedule: PlaybackSchedule, alreadySeen: number): number {
  if (alreadySeen <= 0) {
    return 0;
  }

  return schedule.steps[alreadySeen]?.start ?? schedule.duration;
}

export function frameAt(schedule: PlaybackSchedule, time: number): PlaybackFrame {
  const t = Math.max(0, Math.min(time, schedule.duration));
  const span = schedule.moveEnd - schedule.moveStart;
  const moveProgress = span <= 0 ? (t >= schedule.moveEnd ? 1 : 0) : Math.min(1, Math.max(0, (t - schedule.moveStart) / span));
  const poses = new Map<string, ShipPose>();
  for (const move of schedule.moves) {
    poses.set(move.shipId, poseAlong(move, schedule.duration === 0 ? 1 : easeInOut(moveProgress)));
  }

  const active: ActiveFx[] = [];
  const markers = new Map<string, TablePoint>();
  let settled = 0;
  for (const step of schedule.steps) {
    if (step.event.kind === 'ordnance-moved' && step.event.event.markerId) {
      // A marker sits at the start of its move until its turn comes, then glides to the end of it.
      const along = schedule.duration === 0 || t >= step.impactAt ? 1 : t <= step.start ? 0 : (t - step.start) / (step.impactAt - step.start);
      markers.set(step.event.event.markerId, lerp(step.event.from, step.event.to, easeInOut(along)));
    }

    if (step.event.kind === 'flight' && step.event.event.actorShipId) {
      // A flight is a move of its own: the group crosses during its travel and stays where it landed.
      const along = schedule.duration === 0 || t >= step.impactAt ? 1 : t <= step.start ? 0 : (t - step.start) / (step.impactAt - step.start);
      const at = lerp(step.event.from, step.event.to, easeInOut(along));
      poses.set(step.event.event.actorShipId, { ...at, course: headingOf(step.event.from, step.event.to) });
    }

    if (schedule.duration === 0 || t >= step.end) {
      settled += 1;
    } else if (t >= step.impactAt) {
      active.push({ step, phase: 'impact', progress: (t - step.impactAt) / Math.max(1, step.end - step.impactAt) });
    } else if (t >= step.start) {
      active.push({ step, phase: 'travel', progress: (t - step.start) / Math.max(1, step.impactAt - step.start) });
    }
  }

  return { poses, markers, active, settled, done: t >= schedule.duration };
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

/** The clock heading from one point to another, as a group flying it faces (the server turns it the same way). */
function headingOf(from: TablePoint, to: TablePoint): number {
  const degrees = (Math.atan2(to.x - from.x, -(to.y - from.y)) * 180 / Math.PI + 360) % 360;
  return Math.round(degrees / 30) || 12;
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

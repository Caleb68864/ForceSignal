/**
 * The playback timeline, held to snapshots the real server produced.
 *
 * The fixtures come from `PlaybackFixtureCapture` in the application tests, which plays one busy
 * turn through `InMemoryMatchService` with the invented rules set. Regenerate them there when the
 * snapshot contract changes; do not edit them by hand, or these tests stop being about the server.
 *
 * The `it.fails` cases are the gaps in the findings doc, pinned. Each one passes today because the
 * snapshot cannot say what playback needs; when the server starts saying it, the case starts
 * failing, and that is the cue to flip it to `it`.
 */

import { describe, expect, it } from 'vitest';
import movementRevealed from './fixtures/02-turn1-movement-revealed.json' with { type: 'json' };
import firingOpened from './fixtures/03-turn1-firing-opened.json' with { type: 'json' };
import firingMid from './fixtures/04-turn1-firing-mid.json' with { type: 'json' };
import firingComplete from './fixtures/05-turn1-firing-complete.json' with { type: 'json' };
import turnTwo from './fixtures/06-turn2-order-entry.json' with { type: 'json' };
import orderEntry from './fixtures/01-turn1-order-entry.json' with { type: 'json' };
import { normalizeMatchSnapshot } from '../normalize.ts';
import { buildTurnTimeline, type FireEvent, type MoveEvent } from './timeline.ts';
import type { MatchSnapshot } from '../../types.ts';

function load(raw: unknown): MatchSnapshot {
  return normalizeMatchSnapshot(structuredClone(raw) as MatchSnapshot);
}

const complete = load(firingComplete);
const shipId = (snapshot: MatchSnapshot, name: string) => {
  const ship = snapshot.ships.find((item) => item.name === name);
  expect(ship, `${name} is in the fixture`).toBeDefined();
  return ship?.id ?? '';
};
const moves = (snapshot: MatchSnapshot) => buildTurnTimeline(snapshot, snapshot.turnNumber).events.filter((event): event is MoveEvent => event.kind === 'move');
const fires = (snapshot: MatchSnapshot) => buildTurnTimeline(snapshot, snapshot.turnNumber).events.filter((event): event is FireEvent => event.kind === 'fire');

describe('movement events follow the segments exactly', () => {
  it('has one move per movement result, each leg the course of its segment and, unless the edge stopped it, the distance', () => {
    // Reached-the-subject: the fixture really carries multi-leg moves.
    expect(complete.movementResults.length).toBeGreaterThanOrEqual(5);
    expect(complete.movementResults.some((result) => (result.segments?.length ?? 0) > 1)).toBe(true);

    const events = moves(complete);
    expect(events.map((event) => event.shipId)).toEqual(complete.movementResults.map((result) => result.shipId));

    for (const event of events) {
      const result = complete.movementResults.find((item) => item.shipId === event.shipId);
      const segments = result?.segments ?? [];
      expect(event.legs).toHaveLength(segments.length);
      // The legs are the server's path as flown, point for point.
      expect([event.legs[0].from, ...event.legs.map((leg) => leg.to)]).toEqual(result?.path);
      event.legs.forEach((leg, index) => {
        expect(leg.course).toBe(segments[index].course);
        const length = Math.hypot(leg.to.x - leg.from.x, leg.to.y - leg.from.y);
        if (result?.clamped) {
          // The edge stopped it: never further than ordered.
          expect(length).toBeLessThanOrEqual(segments[index].distance + 0.001);
        } else {
          // The server rounds each step to a thousandth of a unit.
          expect(length).toBeCloseTo(segments[index].distance, 2);
        }
      });
    }
  });

  it('ends every move where the server says the ship now is', () => {
    for (const event of moves(complete)) {
      const ship = complete.ships.find((item) => item.id === event.shipId);
      expect(event.legs.at(-1)?.to).toEqual({ x: ship?.positionX, y: ship?.positionY });
    }
  });

  it('starts the Lance exactly where it was set up, at 18,24', () => {
    const lance = moves(complete).find((event) => event.shipId === shipId(complete, 'Lance'));
    expect(lance?.legs[0].from).toEqual({ x: 18, y: 24 });
  });

  it('starts a hull the table edge stopped where it really started (66,8), not where walking back puts it', () => {
    // Gap M1, closed: the server sends the path as flown, edge and all.
    const runner = moves(complete).find((event) => event.shipId === shipId(complete, 'Edge Runner'));
    expect(runner?.legs[0].from).toEqual({ x: 66, y: 8 });
  });

  it('flies a ship that held course with no order', () => {
    // Gap M2, closed: a drifting ship has a movement result, marked as drifting.
    expect(complete.movementResults.find((result) => result.shipId === shipId(complete, 'Drifter'))?.drifted).toBe(true);
    expect(moves(complete).some((event) => event.shipId === shipId(complete, 'Drifter'))).toBe(true);
  });

  it('does not fly a ship whose order was discarded because it was wrecked first', () => {
    // Gap M3, closed: once movement resolves, a ship whose order was discarded has no result.
    expect(moves(complete).some((event) => event.shipId === shipId(complete, 'Wreck'))).toBe(false);
  });
});

describe('firing events keep the server order', () => {
  it('lists the shots in exactly the order of snapshot.firingResults, one step each', () => {
    const events = fires(complete);
    expect(events.length).toBeGreaterThanOrEqual(4);
    expect(events.map((event) => event.resultIndex)).toEqual(complete.firingResults.map((_, index) => index));
    const steps = events.map((event) => event.step);
    expect(steps).toEqual([...steps].sort((a, b) => a - b));
    expect(new Set(steps).size).toBe(steps.length);
  });

  it('places everything else where the server resolved it', () => {
    // Reached-the-subject: the capture came from a server that sends the list.
    expect(complete.turnEvents?.length).toBeGreaterThan(5);
    const kinds = buildTurnTimeline(complete, 1).events.map((event) => event.kind);
    const first = (kind: string) => kinds.indexOf(kind as never);
    // The flight before the moves; the salvo after them and before any shot; point defence
    // straight before the fighter strike it met; the kill straight after the shot that did it.
    expect(first('flight')).toBeLessThan(first('move'));
    expect(first('move')).toBeLessThan(first('salvo'));
    expect(first('salvo')).toBeLessThan(first('fire'));
    const timeline = buildTurnTimeline(complete, 1).events;
    const defence = timeline.findIndex((event) => event.kind === 'point-defence');
    const strike = timeline[defence + 1];
    expect(strike.kind === 'fire' && strike.fx).toBe('FighterStrike');
    const kill = timeline.findIndex((event) => event.kind === 'destroyed');
    const killer = timeline[kill - 1];
    expect(killer.kind).toBe('fire');
    expect(killer.kind === 'fire' && killer.result.targetShipId).toBe(shipId(complete, 'Picket'));
  });

  it('carries the systems a threshold check took, as the server named them', () => {
    const checks = buildTurnTimeline(complete, 1).events.filter((event) => event.kind === 'threshold');
    expect(checks.flatMap((event) => (event.kind === 'threshold' ? event.event.lostSystems ?? [] : []))).toContain('Battery R');
  });

  it('puts every move before every shot', () => {
    const events = buildTurnTimeline(complete, complete.turnNumber).events;
    const lastMove = events.findLastIndex((event) => event.kind === 'move');
    const firstFire = events.findIndex((event) => event.kind === 'fire');
    expect(lastMove).toBeLessThan(firstFire);
  });
});

describe('every FiringResult produces exactly one event, and nothing is invented', () => {
  it('carries each server record by reference, once', () => {
    const events = fires(complete);
    expect(events).toHaveLength(complete.firingResults.length);
    complete.firingResults.forEach((result, index) => {
      expect(events.filter((event) => event.result === result)).toHaveLength(1);
      expect(events[index].result).toBe(result);
    });
  });

  it('adds no fields to a result and changes none', () => {
    const before = JSON.stringify(complete.firingResults);
    buildTurnTimeline(complete, complete.turnNumber);
    expect(JSON.stringify(complete.firingResults)).toBe(before);
  });

  it('tells a fighter strike from a beam by the attacker, since both arrive as Beam', () => {
    const hawk = fires(complete).find((event) => event.result.attackerShipId === shipId(complete, 'Hawk Flight'));
    expect(hawk?.result.weaponKind).toBe('Beam');
    expect(hawk?.fx).toBe('FighterStrike');
    expect(fires(complete).map((event) => event.fx)).toEqual(expect.arrayContaining(['Beam', 'PulseTorpedo', 'NeedleBeam', 'FighterStrike']));
  });

  it('shows the kill as its own event, for the Picket and nothing else', () => {
    const kills = buildTurnTimeline(complete, 1).events.filter((event) => event.kind === 'destroyed');
    expect(kills.map((event) => event.kind === 'destroyed' && event.event.targetShipId)).toEqual([shipId(complete, 'Picket')]);
    // With the server's list the shot does not also claim it.
    expect(fires(complete).some((event) => event.destroysTarget)).toBe(false);
  });

  it('shows the salvo that struck the Bulwark at the end of movement (gap F1, closed)', () => {
    const salvo = buildTurnTimeline(complete, 1).events.find((event) => event.kind === 'salvo');
    expect(salvo?.kind === 'salvo' && salvo.event.targetShipId).toBe(shipId(complete, 'Bulwark'));
  });

  it('falls back to moves then shots, with an attributed kill, for a snapshot with no event list', () => {
    const older = { ...complete, turnEvents: undefined };
    const events = buildTurnTimeline(older, 1).events;
    expect(events.every((event) => event.kind === 'move' || event.kind === 'fire')).toBe(true);
    const kills = events.filter((event) => event.kind === 'fire' && event.destroysTarget);
    expect(kills.map((event) => event.kind === 'fire' && event.result.targetShipId)).toEqual([shipId(complete, 'Picket')]);
  });
});

describe('the same snapshot gives an identical timeline on every device', () => {
  it('is equal across separate parses of the same wire JSON', () => {
    const first = buildTurnTimeline(load(firingComplete), 1);
    const second = buildTurnTimeline(load(JSON.parse(JSON.stringify(firingComplete))), 1);
    expect(second).toEqual(first);
    expect(JSON.stringify(second)).toBe(JSON.stringify(first));
  });

  it('does not depend on the order of keys in the JSON', () => {
    const reversed = JSON.parse(JSON.stringify(firingComplete), (_key, value) => (
      value && typeof value === 'object' && !Array.isArray(value)
        ? Object.fromEntries(Object.entries(value).reverse())
        : value
    ));
    // Deep equality, not string equality: each result is the server's own object, so it keeps the
    // key order it arrived in, and that is not part of what the timeline says.
    expect(buildTurnTimeline(load(reversed), 1)).toEqual(buildTurnTimeline(load(firingComplete), 1));
  });
});

describe('empty and partial turns', () => {
  it('is empty before anything resolves', () => {
    expect(buildTurnTimeline(load(orderEntry), 1)).toEqual({ turnNumber: 1, status: 'empty', events: [] });
  });

  it('waits rather than flying ships from where they have not left yet', () => {
    const revealed = load(movementRevealed);
    // Reached-the-subject: the results really are on the snapshot already.
    expect(revealed.movementResults.length).toBeGreaterThan(0);
    expect(buildTurnTimeline(revealed, 1)).toEqual({ turnNumber: 1, status: 'awaiting-movement', events: [] });
  });

  it('has the moves, and what came with them, but no shots when firing has only just opened', () => {
    const opened = buildTurnTimeline(load(firingOpened), 1);
    expect(opened.status).toBe('resolved');
    expect(opened.events.some((event) => event.kind === 'move')).toBe(true);
    expect(opened.events.some((event) => event.kind === 'fire')).toBe(false);
    // The flight before the moves and the salvo at their end have already happened.
    expect(opened.events.map((event) => event.kind)).toEqual(expect.arrayContaining(['flight', 'salvo']));
  });

  it('grows by appending as shots land, never reordering what was already there', () => {
    const mid = buildTurnTimeline(load(firingMid), 1).events;
    const full = buildTurnTimeline(complete, 1).events;
    expect(mid.length).toBeGreaterThan(moves(complete).length);
    expect(mid.length).toBeLessThan(full.length);
    expect(JSON.stringify(full.slice(0, mid.length))).toBe(JSON.stringify(mid));
  });

  it('replays the previous turn from the next one, exactly as it played', () => {
    const next = load(turnTwo);
    expect(next.firingResults).toHaveLength(0);
    expect(next.lastTurn?.turnNumber).toBe(1);
    expect(buildTurnTimeline(next, 1)).toEqual(buildTurnTimeline(complete, 1));
  });

  it('says any turn older than that is unavailable', () => {
    expect(buildTurnTimeline(load(turnTwo), 0)).toEqual({ turnNumber: 0, status: 'unavailable', events: [] });
  });

  it('keeps a shot whose ship has left the table rather than deciding it never happened', () => {
    const snapshot = load(firingComplete);
    const attacker = snapshot.firingResults[0].attackerShipId;
    const without = { ...snapshot, ships: snapshot.ships.filter((ship) => ship.id !== attacker) };
    expect(fires(without)).toHaveLength(snapshot.firingResults.length);
  });
});

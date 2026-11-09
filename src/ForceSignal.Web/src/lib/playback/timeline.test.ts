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
  it('has one move per movement result, each leg the course and distance of its segment', () => {
    // Reached-the-subject: the fixture really carries multi-leg moves.
    expect(complete.movementResults.length).toBeGreaterThanOrEqual(5);
    expect(complete.movementResults.some((result) => (result.segments?.length ?? 0) > 1)).toBe(true);

    const events = moves(complete);
    expect(events.map((event) => event.shipId)).toEqual(complete.movementResults.map((result) => result.shipId));

    for (const event of events) {
      const result = complete.movementResults.find((item) => item.shipId === event.shipId);
      const segments = result?.segments ?? [];
      expect(event.legs).toHaveLength(segments.length);
      event.legs.forEach((leg, index) => {
        const radians = segments[index].course * Math.PI / 6;
        expect(leg.course).toBe(segments[index].course);
        expect(leg.to.x - leg.from.x).toBeCloseTo(Math.sin(radians) * segments[index].distance, 6);
        expect(leg.to.y - leg.from.y).toBeCloseTo(-Math.cos(radians) * segments[index].distance, 6);
        if (index > 0) {
          expect(leg.from).toEqual(event.legs[index - 1].to);
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

  it('starts the Lance where it was set up, at 18,24', () => {
    const lance = moves(complete).find((event) => event.shipId === shipId(complete, 'Lance'));
    expect(lance?.legs[0].from.x).toBeCloseTo(18, 3);
    expect(lance?.legs[0].from.y).toBeCloseTo(24, 3);
  });

  it.fails('starts a hull the table edge stopped where it really started (66,8), not where walking back puts it', () => {
    // The server clamps at the edge and reports the unclamped distance; nothing on the snapshot
    // says where the move began. Needs MovementResult.startX/startY (findings, gap M1).
    const runner = moves(complete).find((event) => event.shipId === shipId(complete, 'Edge Runner'));
    expect(runner?.legs[0].from).toEqual({ x: 66, y: 8 });
  });

  it.fails('flies a ship that held course with no order', () => {
    // A drifting ship moves its full velocity and leaves no movement result (gap M2).
    expect(moves(complete).some((event) => event.shipId === shipId(complete, 'Drifter'))).toBe(true);
  });

  it.fails('does not fly a ship whose order was discarded because it was wrecked first', () => {
    // Its commitment keeps a result for a move it never made, and the Picket - destroyed later, in
    // firing - looks the same from here (gap M3).
    expect(moves(complete).some((event) => event.shipId === shipId(complete, 'Wreck'))).toBe(false);
  });
});

describe('firing events keep the server order', () => {
  it('lists the shots in exactly the order of snapshot.firingResults', () => {
    const events = fires(complete);
    expect(events.length).toBeGreaterThanOrEqual(4);
    expect(events.map((event) => event.resultIndex)).toEqual(complete.firingResults.map((_, index) => index));
    expect(events.map((event) => event.step)).toEqual(events.map((_, index) => index + 1));
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

  it('hangs the kill on the shot that holed the destroyed Picket, and on nothing else', () => {
    const picket = shipId(complete, 'Picket');
    const kills = fires(complete).filter((event) => event.destroysTarget);
    expect(kills).toHaveLength(1);
    expect(kills[0].result.targetShipId).toBe(picket);
  });

  it.fails('shows the salvo that struck the Bulwark at the end of movement', () => {
    // Reported only as a log sentence (gap F1).
    expect(buildTurnTimeline(complete, complete.turnNumber).events.some((event) => event.kind !== 'move' && event.kind !== 'fire')).toBe(true);
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

  it('has the moves and no shots when firing has only just opened', () => {
    const opened = buildTurnTimeline(load(firingOpened), 1);
    expect(opened.status).toBe('resolved');
    expect(opened.events.every((event) => event.kind === 'move')).toBe(true);
  });

  it('grows by appending as shots land, never reordering what was already there', () => {
    const mid = buildTurnTimeline(load(firingMid), 1).events;
    const full = buildTurnTimeline(complete, 1).events;
    expect(mid.length).toBeGreaterThan(moves(complete).length);
    expect(mid.length).toBeLessThan(full.length);
    expect(JSON.stringify(full.slice(0, mid.length))).toBe(JSON.stringify(mid));
  });

  it('says a past turn is unavailable, because the server cleared it', () => {
    const next = load(turnTwo);
    expect(next.firingResults).toHaveLength(0);
    expect(buildTurnTimeline(next, 1)).toEqual({ turnNumber: 1, status: 'unavailable', events: [] });
  });

  it('keeps a shot whose ship has left the table rather than deciding it never happened', () => {
    const snapshot = load(firingComplete);
    const attacker = snapshot.firingResults[0].attackerShipId;
    const without = { ...snapshot, ships: snapshot.ships.filter((ship) => ship.id !== attacker) };
    expect(fires(without)).toHaveLength(snapshot.firingResults.length);
  });
});

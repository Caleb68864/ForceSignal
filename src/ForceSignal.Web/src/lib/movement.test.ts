import { describe, expect, it } from 'vitest';
import { clampTurnManeuvers, maxLegalTurn, previewCourse, totalTurnSteps, usableThrust } from './movement.ts';
// Read as text rather than imported as a module so the same bytes the C# guard reads off disk are
// the bytes this suite checks, and so the fixture needs no tsconfig change to be importable.
import turnLimitCases from './turnLimitCases.json?raw';
import type { DraftOrder } from '../types.ts';

const draft = (patch: Partial<DraftOrder> = {}): DraftOrder => ({
  velocityDelta: 0,
  turnSteps: 0,
  turnDirection: 'None',
  turnManeuvers: [],
  salt: 'test-salt',
  ...patch,
});

describe('usableThrust', () => {
  // Drives are lost in halves, and what is left is what may be plotted with.
  it('is what the drives have left', () => {
    expect(usableThrust({ thrustRating: 6, driveDamage: 0 })).toBe(6);
    expect(usableThrust({ thrustRating: 6, driveDamage: 3 })).toBe(3);
  });

  it('never goes below nothing', () => {
    expect(usableThrust({ thrustRating: 4, driveDamage: 9 })).toBe(0);
  });
});

describe('maxLegalTurn', () => {
  // Turn points cannot exceed half the thrust rating, rounded up, and whatever the velocity change
  // already spent comes off first - while the ship is under way.
  it('is half the thrust rounded up, less what acceleration took', () => {
    expect(maxLegalTurn(6, 0, 8)).toBe(3);
    expect(maxLegalTurn(5, 0, 8)).toBe(3);
    expect(maxLegalTurn(6, 4, 8)).toBe(2);
  });

  it('is nothing when the whole budget went on velocity', () => {
    expect(maxLegalTurn(4, 4, 8)).toBe(0);
    expect(maxLegalTurn(4, 9, 8)).toBe(0);
  });

  // The carve-out this file had no copy of. A ship at rest that is not accelerating rotates on the
  // spot to any heading, spending no thrust, so the half-thrust cap does not apply - including the
  // ship nobody has entered a thrust rating for yet, which is every ship as it is created.
  it('lets a ship at rest rotate to any heading', () => {
    expect(maxLegalTurn(4, 0, 0)).toBe(12);
    expect(maxLegalTurn(0, 0, 0)).toBe(12);
  });

  it('is an ordinary move again as soon as a ship at rest accelerates', () => {
    expect(maxLegalTurn(4, 2, 0)).toBe(2);
  });
});

describe('the helm and the resolver agree', () => {
  /**
   * The parity guard. This function is a second implementation of a rule the server owns, kept
   * because a compass cannot wait for a round trip, and allowed to exist only because this fixture
   * holds it to the first: `TurnLimitParityTests` runs these same cases against
   * `FullThrustLightCinematicRules.MaxTurnSteps`, so moving either side reddens one of the suites.
   */
  const fixture = JSON.parse(turnLimitCases) as {
    cases: { name: string; currentVelocity: number; thrustRating: number; velocityDelta: number; maxTurnSteps: number }[];
  };

  it('answers every shared case the same way the resolver does', () => {
    // Reached-the-subject: the fixture really loaded, and it really still carries both the ordinary
    // case and the carve-out. A fixture that parsed to nothing would leave this test walking an
    // empty list and reporting success, which is the failure this repository has recorded most.
    expect(fixture.cases.length).toBeGreaterThanOrEqual(10);
    expect(fixture.cases.some((item) => item.currentVelocity === 0 && item.velocityDelta === 0 && item.maxTurnSteps === 12)).toBe(true);
    expect(fixture.cases.some((item) => item.currentVelocity > 0)).toBe(true);

    for (const item of fixture.cases) {
      expect(
        maxLegalTurn(item.thrustRating, item.velocityDelta, item.currentVelocity),
        item.name,
      ).toBe(item.maxTurnSteps);
    }
  });
});

describe('clampTurnManeuvers', () => {
  it('leaves a plot inside the cap alone', () => {
    const maneuvers = [{ direction: 'Port' as const, steps: 2 }];
    expect(totalTurnSteps(draft({ turnManeuvers: maneuvers }))).toBe(2);
    expect(clampTurnManeuvers(maneuvers, 3)).toEqual(maneuvers);
  });

  it('trims a plot that spends more turn than the ship has', () => {
    const trimmed = clampTurnManeuvers(
      [{ direction: 'Port', steps: 2 }, { direction: 'Starboard', steps: 3 }], 3);
    expect(trimmed.reduce((sum, m) => sum + m.steps, 0)).toBeLessThanOrEqual(3);
  });

  it('drops everything when there is no turn to spend', () => {
    expect(clampTurnManeuvers([{ direction: 'Port', steps: 2 }], 0)).toEqual([]);
  });
});

describe('previewCourse', () => {
  it('shows where the helm ends up', () => {
    expect(previewCourse(12, draft({ turnManeuvers: [{ direction: 'Starboard', steps: 1 }] }))).toBe(1);
  });

  // Turning to port from course 1 is the wrap that a plain modulo gets wrong.
  it('wraps to port across the top of the clock', () => {
    expect(previewCourse(1, draft({ turnManeuvers: [{ direction: 'Port', steps: 1 }] }))).toBe(12);
    expect(previewCourse(1, draft({ turnManeuvers: [{ direction: 'Port', steps: 2 }] }))).toBe(11);
  });

  it('leaves a ship that is not turning on its course', () => {
    expect(previewCourse(7, draft())).toBe(7);
  });
});

// The split-turn geometry these used to cover now has one implementation, on the server, and is
// tested there: `InMemoryMatchServicePreviewTests` asserts that the preview walks the same
// segments the move is resolved into, and that its endpoint is where the ship actually ends up.

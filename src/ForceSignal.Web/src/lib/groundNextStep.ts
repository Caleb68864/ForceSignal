/**
 * Where a ground game's turn has got to, in words, and what the table does next.
 *
 * The screens printed the engine's phase name - "Turn 1 · ChoosingFirstActivator" - and left the
 * table to work out from a row of buttons which one the game was waiting for. Full Thrust has had a
 * sticky line saying what to do next since its two-device play; this is the same line for the
 * ground games, worked out only from what the snapshot already says.
 */

export type GroundTurn = {
  turnNumber: number;
  phase: string;
  sides: readonly string[];
  activeSide?: string | null;
  /** The name of the unit part-way through an activation, if any. */
  activatingName?: string | null;
  /** StarGrunt says which side holds the choice of who goes first; Dirtside does not. */
  firstActivationChooser?: string | null;
  unitCount: number;
  /**
   * Each unit's side and whether it can still go: activate now, or has yet to this turn. Optional,
   * so a caller that has not got it still gets the plain step.
   */
  units?: readonly { side: string; canActivate: boolean; hasActivated: boolean; isOut?: boolean }[];
};

/** The one button the table presses next, highlighted on the screen. */
export type GroundPrimary = 'begin-turn' | 'choose-first' | 'activate' | 'end-activation' | 'pass' | 'end-turn' | null;

export type GroundStep = { heading: string; you: string; primary: GroundPrimary; side?: string | null };

const phaseWordsByName: Record<string, string> = {
  NotStarted: 'Setting up',
  ChoosingFirstActivator: 'Choosing who goes first',
  Activating: 'Activations',
  TurnEnded: 'Turn over',
};

/** The phase as a table would say it. An unknown name - a newer server - is shown as sent. */
export function phaseWords(phase: string): string {
  return phaseWordsByName[phase] ?? phase;
}

/** What to do next. `unit` is what the game calls one: "platoon" for Dirtside, "unit" for StarGrunt. */
export function groundNextStep(turn: GroundTurn, unit: string): GroundStep {
  const heading = turn.turnNumber > 0
    ? `Turn ${turn.turnNumber} · ${phaseWords(turn.phase)}`
    : phaseWords(turn.phase);

  switch (turn.phase) {
    case 'NotStarted':
      return turn.sides.length < 2 || turn.unitCount < 2
        ? { heading, you: `Put a ${unit} on the table for each side, then Begin Turn.`, primary: null }
        : { heading, you: 'Begin Turn when both sides are on the table.', primary: 'begin-turn' };
    case 'ChoosingFirstActivator':
      return {
        heading,
        you: turn.firstActivationChooser
          ? `${turn.firstActivationChooser} chooses who goes first.`
          : 'Choose which side goes first.',
        primary: 'choose-first',
        side: turn.firstActivationChooser ?? null,
      };
    case 'Activating': {
      if (turn.activatingName) {
        return { heading, you: `Finish ${turn.activatingName}'s activation, then End Activation.`, primary: 'end-activation' };
      }

      // Playtest M9: with every unit gone the screen still said "blue to act", and End Turn was
      // refused in words nobody could act on. Worked out from the roster when the screen has it.
      const live = (turn.units ?? []).filter((candidate) => !candidate.isOut);
      if (live.length > 0 && live.every((candidate) => candidate.hasActivated)) {
        return { heading, you: `Every ${unit} has gone. End Turn.`, primary: 'end-turn' };
      }

      const side = turn.activeSide ?? null;
      if (side && turn.units && !live.some((candidate) => candidate.side === side && candidate.canActivate)) {
        return { heading, you: `${side} has nothing it can activate now: press ${side} passes.`, primary: 'pass', side };
      }

      return {
        heading,
        you: side ? `${side} activates a ${unit}, or passes.` : `Activate a ${unit}, or pass.`,
        primary: 'activate',
        side,
      };
    }
    case 'TurnEnded':
      return { heading, you: `Begin Turn to start turn ${turn.turnNumber + 1}.`, primary: 'begin-turn' };
    default:
      return { heading, you: '', primary: null };
  }
}

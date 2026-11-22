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
};

export type GroundStep = { heading: string; you: string };

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
      return {
        heading,
        you: turn.sides.length < 2 || turn.unitCount < 2
          ? `Put a ${unit} on the table for each side, then Begin Turn.`
          : 'Begin Turn when both sides are on the table.',
      };
    case 'ChoosingFirstActivator':
      return {
        heading,
        you: turn.firstActivationChooser
          ? `${turn.firstActivationChooser} chooses who goes first.`
          : 'Choose which side goes first.',
      };
    case 'Activating':
      if (turn.activatingName) {
        return { heading, you: `Finish ${turn.activatingName}'s activation, then End Activation.` };
      }

      return {
        heading,
        you: turn.activeSide
          ? `${turn.activeSide} activates a ${unit}, or passes.`
          : `Activate a ${unit}, or pass.`,
      };
    case 'TurnEnded':
      return { heading, you: `Begin Turn to start turn ${turn.turnNumber + 1}.` };
    default:
      return { heading, you: '' };
  }
}

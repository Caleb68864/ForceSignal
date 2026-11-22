/**
 * The step a player at this point in a match needs next.
 *
 * Read off the match's own state rather than guessed: no match yet is joining, a match without a
 * playable profile is the rules step, and each phase after that is the step that acts in it.
 */
export function helpStepFor(match: { phase: string; rulesPlayable: boolean; shipCount: number } | null): string {
  if (!match) {
    return 'join';
  }

  switch (match.phase) {
    case 'FleetSetup':
      return !match.rulesPlayable ? 'rules' : match.shipCount === 0 ? 'fleet' : 'ready';
    case 'OrderEntry':
      return 'plot';
    case 'OrdersLocked':
      return 'lock';
    case 'Reveal':
    case 'Movement':
      return 'move';
    case 'Firing':
      return 'fire';
    default:
      return 'join';
  }
}

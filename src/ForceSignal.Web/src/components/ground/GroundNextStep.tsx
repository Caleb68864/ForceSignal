import { groundNextStep, type GroundTurn } from '../../lib/groundNextStep.ts';

/**
 * Where the turn has got to and what the table does next, in the same sticky line Full Thrust uses,
 * so it stays in view while the buttons it points at are scrolled to.
 */
export function GroundNextStep({ turn, unit }: { turn: GroundTurn; unit: string }) {
  const step = groundNextStep(turn, unit);
  return (
    <section className="next-step your-move" aria-label="What to do next">
      <div className="next-step-guide">
        <span className="label">{step.heading}</span>
        {step.you ? <strong>{step.you}</strong> : null}
      </div>
    </section>
  );
}

import type { GroundStep } from '../../lib/groundNextStep.ts';

/**
 * Where the turn has got to and what the table does next, in the same sticky line Full Thrust uses,
 * so it stays in view while the buttons it points at are scrolled to.
 */
export function GroundNextStep({ step }: { step: GroundStep }) {
  return (
    <section className="next-step your-move" aria-label="What to do next">
      <div className="next-step-guide">
        <span className="label">{step.heading}</span>
        {step.you ? <strong>{step.you}</strong> : null}
      </div>
    </section>
  );
}

/** The class a turn button wears: the plain (filled) button when it is the one to press, else ghost. */
export function primaryWhen(isPrimary: boolean) {
  return isPrimary ? undefined : 'ghost';
}

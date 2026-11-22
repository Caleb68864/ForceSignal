import type { GroundStep } from '../../lib/groundNextStep.ts';
import { useStickyClearance } from '../../lib/useStickyClearance.ts';

/**
 * Where the turn has got to and what the table does next, in the same sticky line Full Thrust uses,
 * so it stays in view while the buttons it points at are scrolled to.
 */
export type GroundAction = { label: string; onClick: () => void; disabled: boolean };

/**
 * The same banner as Full Thrust's, with the one command to press now in it (design suggestion 1):
 * on a phone the banner is under the thumb at the bottom while the turn buttons are at the top. The
 * action says "Next:" so it is never mistaken for the turn button it repeats.
 */
export function GroundNextStep({ step, action }: { step: GroundStep; action?: GroundAction | null }) {
  const bar = useStickyClearance<HTMLElement>();
  return (
    <section ref={bar} className="next-step your-move" aria-label="What to do next">
      <div className="next-step-guide">
        <span className="label">{step.heading}</span>
        {step.you ? <strong>{step.you}</strong> : null}
        {action ? (
          <button type="button" className="next-step-action" disabled={action.disabled} onClick={action.onClick}>
            Next: {action.label}
          </button>
        ) : null}
      </div>
    </section>
  );
}

/** The class a turn button wears: the plain (filled) button when it is the one to press, else ghost. */
export function primaryWhen(isPrimary: boolean) {
  return isPrimary ? undefined : 'ghost';
}

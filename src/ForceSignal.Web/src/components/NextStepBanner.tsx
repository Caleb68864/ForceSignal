/**
 * The line that says where the match is, what to do next, and what just happened.
 *
 * Sticky, so it is in view wherever the player has scrolled - the old message line sat at the top
 * of the board while every command button sat at the bottom of the sidebar, so a refused Ready
 * looked exactly like nothing happening. A refusal is an alert in its own colour; a success fades
 * after a few seconds and is cleared when the phase moves on, so a "Ship added to fleet." from
 * setup is not still on screen three phases later.
 */

import { useEffect } from 'react';
import type { NextStep } from '../lib/nextStep.ts';

export type NoticeTone = 'success' | 'error' | 'info';
/** `at` is when it was said, so a phase change can tell a fresh message from an old one. */
export type Notice = { text: string; tone: NoticeTone; key: number; at: number };

const successLingerMs = 8000;

/** The one match command to press now, carried in the banner so it is always under the thumb. */
export type BannerAction = { label: string; onClick: () => void; disabled: boolean };

export function NextStepBanner({ step, notice, onDismiss, action }: { step: NextStep | null; notice: Notice; onDismiss: () => void; action?: BannerAction | null }) {
  useEffect(() => {
    if (!notice.text || notice.tone === 'error') {
      return undefined;
    }

    const timer = window.setTimeout(onDismiss, successLingerMs);
    return () => window.clearTimeout(timer);
  }, [notice, onDismiss]);

  return (
    <section className={step?.yourMove ? 'next-step your-move' : 'next-step'} data-testid="next-step" aria-label="What to do next">
      {step ? (
        <div className="next-step-guide">
          <span className="label">{step.heading}</span>
          {step.you ? <strong>{step.you}</strong> : null}
          {step.waiting ? <span className="next-step-waiting">{step.waiting}</span> : null}
          {action ? (
            <button type="button" className="next-step-action" disabled={action.disabled} onClick={action.onClick}>{action.label}</button>
          ) : null}
        </div>
      ) : null}
      {/* Always mounted, so a screen reader is already listening when the first message lands. */}
      <div className={`next-step-notice ${notice.tone}`} role={notice.tone === 'error' ? 'alert' : 'status'}>
        {notice.text ? (
          <>
            <span>{notice.text}</span>
            <button className="ghost" type="button" aria-label="Dismiss message" onClick={onDismiss}>×</button>
          </>
        ) : null}
      </div>
    </section>
  );
}

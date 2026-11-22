import type { ReactNode } from 'react';

/**
 * A rules reminder folded behind a "?" (playtest m19's follow-up). The ground screens explained every
 * panel in a paragraph that sat open on every turn; at a table those paragraphs are read once. The
 * text stays in the page - a closed details element - so it is one tap away and a screen reader can
 * still reach it.
 */
export function Explain({ topic, children }: { topic: string; children: ReactNode }) {
  return (
    <details className="explain">
      <summary>
        <span aria-hidden="true">?</span>
        <span className="visually-hidden">How {topic} works</span>
      </summary>
      <p className="constraint-line">{children}</p>
    </details>
  );
}

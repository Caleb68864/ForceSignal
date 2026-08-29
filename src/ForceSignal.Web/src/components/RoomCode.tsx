/**
 * The room code, with a way to hand it over that is not reading it aloud twice.
 *
 * The clipboard API needs a secure context, which a laptop reached over the LAN by plain HTTP is
 * not, so the fallback selects the code on screen for the platform's own copy gesture.
 */

import { useEffect, useRef, useState } from 'react';

export function RoomCode({ code }: { code: string }) {
  const codeRef = useRef<HTMLHeadingElement | null>(null);
  const [note, setNote] = useState<string | null>(null);

  useEffect(() => {
    if (!note) {
      return;
    }

    const timer = window.setTimeout(() => setNote(null), 2_000);
    return () => window.clearTimeout(timer);
  }, [note]);

  async function copy() {
    try {
      if (typeof navigator.clipboard?.writeText === 'function') {
        await navigator.clipboard.writeText(code);
        setNote('Copied');
        return;
      }
    } catch {
      // Refused or unavailable; fall through to selecting the text.
    }

    const selection = window.getSelection();
    if (codeRef.current && selection) {
      selection.selectAllChildren(codeRef.current);
      setNote('Selected - press copy');
      return;
    }

    setNote('Copy it by hand');
  }

  return (
    <div className="room-code">
      <h2 ref={codeRef}>{code}</h2>
      <div className="quick-actions">
        <button className="ghost" type="button" onClick={() => void copy()}>Copy code</button>
        {note ? <small aria-live="polite">{note}</small> : null}
      </div>
    </div>
  );
}

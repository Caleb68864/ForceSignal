/**
 * The room code, with a way to hand it over that is not reading it aloud twice.
 *
 * The clipboard API needs a secure context, which a laptop reached over the LAN by plain HTTP is
 * not, so the fallback selects the code on screen for the platform's own copy gesture.
 *
 * The invite link opens the app with the code already filled in (`?join=`), so the other player
 * types only their name. It goes through the phone's own share sheet where there is one.
 */

/** The address that opens this app with a room code filled in. */
export function inviteLink(code: string, location: { origin: string; pathname: string } = window.location): string {
  return `${location.origin}${location.pathname}?join=${encodeURIComponent(code)}`;
}

import { useEffect, useRef, useState } from 'react';

export function RoomCode({ code }: { code: string }) {
  const codeRef = useRef<HTMLHeadingElement | null>(null);
  const linkRef = useRef<HTMLElement | null>(null);
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

  async function share() {
    const url = inviteLink(code);
    try {
      if (typeof navigator.share === 'function') {
        await navigator.share({ title: 'Join my ForceSignal room', text: `Room code ${code}`, url });
        return;
      }

      if (typeof navigator.clipboard?.writeText === 'function') {
        await navigator.clipboard.writeText(url);
        setNote('Invite link copied');
        return;
      }
    } catch {
      // Cancelled, refused or unavailable; fall through to selecting the link.
    }

    const selection = window.getSelection();
    if (linkRef.current && selection) {
      selection.selectAllChildren(linkRef.current);
      setNote('Link selected - press copy');
    }
  }

  return (
    <div className="room-code">
      <h2 ref={codeRef}>{code}</h2>
      <div className="quick-actions">
        <button className="ghost" type="button" onClick={() => void copy()}>Copy code</button>
        <button className="ghost" type="button" onClick={() => void share()}>Share invite link</button>
        {note ? <small aria-live="polite">{note}</small> : null}
      </div>
      <small className="invite-link" ref={linkRef}>{inviteLink(code)}</small>
    </div>
  );
}

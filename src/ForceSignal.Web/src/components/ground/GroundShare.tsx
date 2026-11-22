import { useEffect, useRef, useState } from 'react';
import { fromGameCode, toGameCode } from '../../lib/groundShare.ts';
import type { GroundLinkState } from '../../lib/groundLive.ts';
import type { GameHandle } from '../../types.ts';

const linkWords: Record<GroundLinkState, string> = {
  live: 'Link live',
  reconnecting: 'Reconnecting',
  offline: 'Link lost',
};

/**
 * The game code, for opening this game on a second device at the table, and whether this device is
 * following changes made on the others.
 */
export function GroundShare({ game, link }: { game: GameHandle; link: GroundLinkState }) {
  const codeRef = useRef<HTMLElement | null>(null);
  const [open, setOpen] = useState(false);
  const [note, setNote] = useState<string | null>(null);
  const code = toGameCode(game);

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
      // Refused, or not a secure context; fall through to selecting the text.
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
    <div className="card-module" aria-label="Other devices">
      <span className="label module-title">Other devices</span>
      <p className="constraint-line" role="status">{linkWords[link]}</p>
      {open ? (
        <>
          <p className="privacy">
            Paste this code into Join a Game on another device at the table. Whoever holds it can play
            this game from there, so share it only with the people playing.
          </p>
          <code className="game-code" ref={codeRef}>{code}</code>
          <div className="quick-actions">
            <button className="ghost" type="button" onClick={() => void copy()}>Copy Game Code</button>
            <button className="ghost" type="button" onClick={() => setOpen(false)}>Hide Game Code</button>
            {note ? <small aria-live="polite">{note}</small> : null}
          </div>
        </>
      ) : (
        <button className="ghost" type="button" onClick={() => setOpen(true)}>Show Game Code</button>
      )}
    </div>
  );
}

/** Opens a game started on another device, from its game code. */
export function GroundJoin({ busy, onJoin }: { busy: boolean; onJoin: (game: GameHandle) => void }) {
  const [text, setText] = useState('');
  const [problem, setProblem] = useState<string | null>(null);

  function join() {
    const game = fromGameCode(text);
    if (!game) {
      setProblem('That is not a game code. Copy it again from the device the game was started on.');
      return;
    }

    setProblem(null);
    onJoin(game);
  }

  return (
    <div className="card-module" aria-label="Join a game">
      <span className="label module-title">Join a game</span>
      <p className="privacy">Started on another device? Paste its game code to play it here too.</p>
      <label>
        Game code
        <input value={text} autoComplete="off" spellCheck={false} onChange={(event) => setText(event.target.value)} />
      </label>
      <button type="button" disabled={busy || text.trim() === ''} onClick={join}>Join Game</button>
      {problem ? <p className="constraint-line" role="alert">{problem}</p> : null}
    </div>
  );
}

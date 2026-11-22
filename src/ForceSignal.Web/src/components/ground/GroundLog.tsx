import { useMemo } from 'react';

/**
 * How many log lines a ground screen puts in the DOM. The same page size as the Full Thrust battle
 * log, for the same reason: the server keeps up to 2000 lines and every one rode into the DOM on
 * every render - every keystroke on the add-a-platoon form included - which on a tablet stutters.
 */
export const groundLogPageSize = 300;

/**
 * A ground game's log, oldest first, showing only the newest {@link groundLogPageSize} lines. The
 * whole log stays in the snapshot this is handed; only what is drawn is cut.
 */
export function GroundLog({
  log,
  logStart,
  listClassName,
  ordered,
}: {
  log: readonly string[];
  logStart?: number;
  listClassName?: string;
  ordered?: boolean;
}) {
  const shown = useMemo(() => log.slice(-groundLogPageSize), [log]);
  if (log.length === 0) {
    return <p className="constraint-line">Nothing has happened yet.</p>;
  }

  // A line's place in the game's whole history: stable as the cap drops lines off the front, so
  // React keeps each row rather than re-rendering all of them when the window slides.
  const first = (logStart ?? 0) + log.length - shown.length;
  const items = shown.map((entry, index) => <li key={first + index}>{entry}</li>);
  return (
    <>
      {log.length > shown.length ? (
        <p className="log-truncated">
          Showing the most recent {shown.length} of {log.length} lines.
        </p>
      ) : null}
      {ordered ? <ol className={listClassName}>{items}</ol> : <ul className={listClassName}>{items}</ul>}
    </>
  );
}

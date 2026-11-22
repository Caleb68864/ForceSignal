import { useMemo, useState } from 'react';
import { formatLogTime, formatPhase } from '../lib/format.ts';
import type { MatchLogEntry } from '../types.ts';

/**
 * The battle record, newest first, made readable (playtest m15). By turn five it was 300 rows, most
 * of them one-per-ship state snapshots, and the Log tab took most of a second to open.
 *
 * - A run of ship-state snapshots folds into one row that opens on a tap.
 * - A filter narrows it to one kind of entry.
 * - Only a page of rows is drawn at once, with a control to draw more.
 *
 * The export still writes every entry; this only changes what is drawn.
 */
const filters: { key: string; label: string; categories: string[] | null }[] = [
  { key: 'all', label: 'Everything', categories: null },
  { key: 'fire', label: 'Fire and damage', categories: ['Fire', 'Damage', 'Threshold', 'Repair', 'PointDefense', 'Ordnance', 'Range', 'Initiative'] },
  { key: 'movement', label: 'Movement and orders', categories: ['Movement', 'Orders', 'Fighters'] },
  { key: 'turns', label: 'Turns and phases', categories: ['Phase', 'Summary', 'Battle'] },
  { key: 'setup', label: 'Setup and admin', categories: ['Setup', 'Session', 'Admin'] },
];

const pageSize = 60;

type Row = { kind: 'entry'; entry: MatchLogEntry } | { kind: 'snapshots'; entries: MatchLogEntry[] };

/** Folds each run of consecutive ship-state snapshots into one row. */
export function foldSnapshots(entries: readonly MatchLogEntry[]): Row[] {
  const rows: Row[] = [];
  for (const entry of entries) {
    const last = rows[rows.length - 1];
    if (entry.category === 'Snapshot' && last?.kind === 'snapshots') {
      last.entries.push(entry);
    } else if (entry.category === 'Snapshot') {
      rows.push({ kind: 'snapshots', entries: [entry] });
    } else {
      rows.push({ kind: 'entry', entry });
    }
  }

  return rows;
}

export function BattleLog({ entries }: { entries: readonly MatchLogEntry[] }) {
  const [filter, setFilter] = useState('all');
  const [shown, setShown] = useState(pageSize);
  const categories = filters.find((option) => option.key === filter)?.categories ?? null;
  const rows = useMemo(
    () => foldSnapshots(categories ? entries.filter((entry) => categories.includes(entry.category)) : entries),
    [entries, categories],
  );
  const visible = rows.slice(0, shown);

  return (
    <>
      <label className="log-filter">
        Show
        <select value={filter} onChange={(event) => { setFilter(event.target.value); setShown(pageSize); }}>
          {filters.map((option) => <option key={option.key} value={option.key}>{option.label}</option>)}
        </select>
      </label>
      {/* Scrolls on its own, so it takes focus: a keyboard could not reach its entries (axe:
          scrollable-region-focusable). */}
      <ol className="log-list" tabIndex={0} aria-label="Battle record entries">
        {visible.map((row) => row.kind === 'entry' ? (
          <li key={row.entry.sequence}>
            <span>T{row.entry.turnNumber} · {formatPhase(row.entry.phase)} · {row.entry.category} · {formatLogTime(row.entry.timestamp)}</span>
            <p>{row.entry.message}</p>
          </li>
        ) : (
          <li key={`snapshots-${row.entries[0].sequence}`} className="log-snapshots">
            <details>
              <summary>
                T{row.entries[0].turnNumber} · {formatPhase(row.entries[0].phase)} · {row.entries.length} ship state{row.entries.length === 1 ? '' : 's'}
              </summary>
              {row.entries.map((entry) => <p key={entry.sequence}>{entry.message}</p>)}
            </details>
          </li>
        ))}
      </ol>
      {rows.length > visible.length ? (
        <button className="ghost" type="button" onClick={() => setShown((current) => current + pageSize)}>
          Show {Math.min(pageSize, rows.length - visible.length)} older
        </button>
      ) : null}
    </>
  );
}

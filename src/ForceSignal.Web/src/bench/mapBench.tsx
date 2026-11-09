/**
 * The tactical map on its own, for measuring.
 *
 * Renders the real PlayMap - DOM, or Pixi with ?renderer=pixi - against the busy server-captured
 * turn in the playback fixtures, multiplied out to ?ships=N hulls. The extra hulls and their shots
 * are copies of the captured ones moved across the table: bench data for timing the renderer, never
 * something the app would show.
 *
 * Publishes `window.__bench` for e2e/measureMap.mjs: when the map first painted, and how long each
 * React commit took.
 */

import { Profiler, StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import '../style.css';
import { PlayMap } from '../components/map/PlayMap.tsx';
import { normalizeMatchSnapshot } from '../lib/normalize.ts';
import captured from '../lib/playback/fixtures/05-turn1-firing-complete.json' with { type: 'json' };
import type { FiringSolution, MatchSnapshot, OrderPreview } from '../types.ts';

type BenchState = { firstCommitAt?: number; firstPaintAt?: number; commits: { phase: string; ms: number; at: number }[] };
const bench: BenchState = { commits: [] };
(window as unknown as { __bench: BenchState }).__bench = bench;

const params = new URLSearchParams(window.location.search);
const shipCount = Math.max(1, Number(params.get('ships') ?? 8));
const base = normalizeMatchSnapshot(structuredClone(captured) as unknown as MatchSnapshot);

function multiplied(): MatchSnapshot {
  const ships = [];
  const movementResults = [];
  const firingResults = [];
  const idMap = new Map<string, string>();
  for (let index = 0; index < shipCount; index += 1) {
    const source = base.ships[index % base.ships.length];
    const copy = Math.floor(index / base.ships.length);
    const id = copy === 0 ? source.id : `${source.id}-${copy}`;
    idMap.set(`${source.id}#${copy}`, id);
    // Spread the copies in a lattice so they do not sit on top of one another.
    const dx = copy === 0 ? 0 : ((copy * 13) % 50) - 25;
    const dy = copy === 0 ? 0 : ((copy * 7) % 30) - 15;
    ships.push({
      ...source,
      id,
      name: copy === 0 ? source.name : `${source.name} ${copy + 1}`,
      positionX: Math.min(base.tableWidth, Math.max(0, source.positionX + dx)),
      positionY: Math.min(base.tableDepth, Math.max(0, source.positionY + dy)),
    });
    const move = base.movementResults.find((result) => result.shipId === source.id);
    if (move) {
      movementResults.push({ ...move, shipId: id });
    }
  }

  const copies = Math.ceil(shipCount / base.ships.length);
  for (let copy = 0; copy < copies; copy += 1) {
    for (const result of base.firingResults) {
      const attackerShipId = idMap.get(`${result.attackerShipId}#${copy}`);
      const targetShipId = idMap.get(`${result.targetShipId}#${copy}`);
      if (attackerShipId && targetShipId) {
        firingResults.push({ ...result, attackerShipId, targetShipId });
      }
    }
  }

  return { ...base, ships, movementResults, firingResults };
}

const snapshot = multiplied();
const noop = () => undefined;

function onRender(_id: string, phase: string, actualDuration: number) {
  const at = performance.now();
  bench.commits.push({ phase, ms: actualDuration, at });
  if (bench.firstCommitAt === undefined) {
    bench.firstCommitAt = at;
    // The frame after the first commit is the first one with the map on it.
    requestAnimationFrame(() => requestAnimationFrame(() => {
      bench.firstPaintAt = performance.now();
    }));
  }
}

const root = document.getElementById('app');
if (root) {
  createRoot(root).render(
    <StrictMode>
      <Profiler id="map" onRender={onRender}>
        <main className="bench-shell" style={{ padding: 12 }}>
          <PlayMap
            snapshot={snapshot}
            ownedShipIds={new Set(snapshot.ships.filter((ship) => ship.fleetId === snapshot.fleets[0]?.id).map((ship) => ship.id))}
            drafts={{}}
            firingDrafts={{}}
            phase={snapshot.phase}
            focusedShipId={null}
            onFocus={noop}
            onDraftChange={noop}
            onFiringDraftChange={noop}
            busy={false}
            onFighterOps={noop}
            onCreateOrdnance={noop}
            onUpdateOrdnance={noop}
            onRemoveOrdnance={noop}
            onPreviewOrder={() => Promise.resolve({ isValid: true, errors: [], segments: [], path: [] } as unknown as OrderPreview)}
            onFiringSolution={() => Promise.resolve({ canFire: false, needleTargets: [] } as unknown as FiringSolution)}
            onFire={noop}
            onCeaseFire={noop}
            onFlyFighters={noop}
          />
        </main>
      </Profiler>
    </StrictMode>,
  );
}

import { useState } from 'react';
import * as api from '../../lib/dirtsideApi.ts';
import { wholeNumberFrom } from '../../lib/format.ts';
import type { DirtsideSnapshot, GameHandle } from '../../types.ts';

/**
 * Nerve: the tests and declarations a table owes outside the shot and the move.
 *
 * When a test is owed is the table's call, and the threat level comes off the table's own sheet, so
 * it opens at zero and this screen does not suggest one. What a test does is the server's.
 */
export function DirtsideNervePanel({ game, snapshot, busy, run }: {
  game: GameHandle;
  snapshot: DirtsideSnapshot;
  busy: boolean;
  run: (action: () => Promise<DirtsideSnapshot>, note?: string) => Promise<void>;
}) {
  const [chosenId, setChosenId] = useState('');
  const [threatLevel, setThreatLevel] = useState(0);

  // The activating platoon if nothing has been picked, because that is whose move is waiting.
  const unit = snapshot.units.find((candidate) => candidate.id === chosenId)
    ?? snapshot.units.find((candidate) => candidate.id === snapshot.activatingUnitId)
    ?? snapshot.units[0];
  if (!unit) {
    return null;
  }

  return (
    <div className="card-module" aria-label="Nerve">
      <span className="label module-title">Nerve</span>
      <p className="constraint-line">
        Take a confidence test when your rules say one is owed, at the threat level your own table
        gives the event. A reaction test is for the platoon activating, before a move it owes one for.
      </p>
      <div className="table-fields">
        <label>
          Platoon
          <select value={unit.id} onChange={(e) => setChosenId(e.target.value)}>
            {snapshot.units.map((candidate) => <option key={candidate.id} value={candidate.id}>{candidate.name}</option>)}
          </select>
        </label>
        <label title="Off your own threat table.">
          Nerve threat level
          <input type="number" min="0" max="99" value={threatLevel} onChange={(e) => setThreatLevel(wholeNumberFrom(e.target.value, 0, 0, 99))} />
        </label>
        <button
          type="button"
          disabled={busy}
          onClick={() => void run(() => api.takeConfidenceTest(game, unit.id, threatLevel))}
        >
          Confidence Test
        </button>
        <button
          type="button"
          disabled={busy || unit.canTakeReactionTest !== true}
          title={unit.whyItCannotTakeReactionTest ?? undefined}
          onClick={() => void run(() => api.takeReactionTest(game, unit.id, threatLevel))}
        >
          Reaction Test
        </button>
        <label title="Whether its next move takes it toward the enemy or out of cover. Your eyeball, not this app's.">
          Next move advances
          <input
            type="checkbox"
            checked={unit.nextMoveAdvancesOnTheEnemy === true}
            disabled={busy}
            onChange={(e) => {
              const advances = e.target.checked;
              void run(() => api.declareAdvance(game, unit.id, advances));
            }}
          />
        </label>
        <label title="Whether it has come out of integrity. Measured at the table.">
          Disorganised
          <input
            type="checkbox"
            checked={unit.isDisorganised}
            disabled={busy}
            onChange={(e) => {
              const isDisorganised = e.target.checked;
              void run(() => api.setDisorganised(game, unit.id, isDisorganised));
            }}
          />
        </label>
      </div>
      {unit.canTakeReactionTest !== true && unit.whyItCannotTakeReactionTest ? (
        <p className="constraint-line">{unit.whyItCannotTakeReactionTest}</p>
      ) : null}
    </div>
  );
}

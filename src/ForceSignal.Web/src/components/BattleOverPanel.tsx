import type { MatchSnapshot } from '../types.ts';

/**
 * The end of a Full Thrust battle (playtest M1): why it is over, what each side has left, and the two
 * ways to keep it - the after-action log and a snapshot that can be restored to play on.
 */
export function BattleOverPanel({
  snapshot,
  onExportLog,
  onSaveSnapshot,
}: {
  snapshot: MatchSnapshot;
  onExportLog: () => void;
  onSaveSnapshot: () => void;
}) {
  if (!snapshot.battleOver) {
    return null;
  }

  const ownerOf = new Map(snapshot.fleets.map((fleet) => [fleet.id, fleet.ownerParticipantId]));
  const sides = snapshot.participants
    .map((participant) => {
      const ships = snapshot.ships.filter((ship) => ownerOf.get(ship.fleetId) === participant.id);
      const afloat = ships.filter((ship) => !ship.isDestroyed);
      return {
        id: participant.id,
        name: participant.displayName,
        total: ships.length,
        afloat: afloat.length,
        lost: ships.filter((ship) => ship.isDestroyed).map((ship) => ship.name),
      };
    })
    .filter((side) => side.total > 0);

  return (
    <section className="battle-over" aria-label="Battle over">
      <span className="label">Battle over · turn {snapshot.turnNumber}</span>
      <h3>{snapshot.battleOver.message}</h3>
      <ul className="battle-over-sides">
        {sides.map((side) => (
          <li key={side.id}>
            <strong>{side.name}</strong>: {side.afloat} of {side.total} {side.total === 1 ? 'ship' : 'ships'} still afloat
            {side.lost.length > 0 ? <small> · lost {side.lost.join(', ')}</small> : null}
          </li>
        ))}
      </ul>
      <div className="quick-actions">
        <button type="button" onClick={onExportLog}>Export Battle Log</button>
        <button type="button" className="ghost" onClick={onSaveSnapshot}>Save Snapshot</button>
      </div>
    </section>
  );
}

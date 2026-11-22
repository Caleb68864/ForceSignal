import { clampDraftForShip, draftFor, formatTurnSequence, maxLegalTurn, totalTurnSteps, turnManeuversForDraft, turnPatchFromManeuvers, usableThrust } from '../lib/movement.ts';
import type { DraftOrder, OrderStatus, Ship } from '../types.ts';

/**
 * Every ship's order on one row each (design suggestion 2). Eight ships took about thirty taps a
 * turn - focus a card, open the helm, plot, move to the next card. The common orders are here in a
 * line: speed down or up a step, a turn a step to port or starboard, back to drift, and the same
 * order for every ship. The card's helm is still there for anything finer.
 */
export function FleetOrdersTable({
  ships,
  drafts,
  orderStatuses,
  onDraftChange,
  onCopyToFleet,
}: {
  ships: Ship[];
  drafts: Record<string, DraftOrder>;
  orderStatuses: OrderStatus[];
  onDraftChange: (shipId: string, patch: Partial<DraftOrder>) => void;
  onCopyToFleet: (shipId: string) => void;
}) {
  const live = ships.filter((ship) => !ship.isDestroyed);
  if (live.length < 2) {
    return null;
  }

  return (
    <section className="fleet-orders card-module" aria-label="Fleet orders">
      <span className="label module-title">Fleet orders</span>
      <table>
        <thead>
          <tr>
            <th scope="col">Ship</th>
            <th scope="col">Speed</th>
            <th scope="col">Turn</th>
            <th scope="col"><span className="visually-hidden">Actions</span></th>
          </tr>
        </thead>
        <tbody>
          {live.map((ship) => {
            const draft = draftFor(ship.id, drafts);
            const thrust = usableThrust(ship);
            const locked = Boolean(orderStatuses.find((status) => status.shipId === ship.id)?.isCommitted);
            const maxTurn = maxLegalTurn(thrust, draft.velocityDelta, ship.currentVelocity);
            const speed = (delta: number) => onDraftChange(ship.id, clampDraftForShip(thrust, { ...draft, velocityDelta: draft.velocityDelta + delta }, ship.currentVelocity));
            const turn = (direction: 'Port' | 'Starboard') => onDraftChange(ship.id, turnPatchFromManeuvers([...turnManeuversForDraft(draft), { direction, steps: 1 }]));
            return (
              <tr key={ship.id}>
                <th scope="row">
                  {ship.name}
                  <small>{locked ? 'Locked' : `v${ship.currentVelocity} c${ship.currentCourse}`}</small>
                </th>
                <td>
                  <div className="fleet-orders-step">
                    <button type="button" className="ghost" disabled={locked} aria-label={`Slow ${ship.name}`} onClick={() => speed(-1)}>−</button>
                    <strong>{draft.velocityDelta > 0 ? `+${draft.velocityDelta}` : draft.velocityDelta}</strong>
                    <button type="button" className="ghost" disabled={locked} aria-label={`Speed up ${ship.name}`} onClick={() => speed(1)}>+</button>
                  </div>
                </td>
                <td>
                  <div className="fleet-orders-step">
                    <button type="button" className="ghost" disabled={locked || totalTurnSteps(draft) >= maxTurn} aria-label={`Turn ${ship.name} to port`} onClick={() => turn('Port')}>P</button>
                    <strong>{formatTurnSequence(draft)}</strong>
                    <button type="button" className="ghost" disabled={locked || totalTurnSteps(draft) >= maxTurn} aria-label={`Turn ${ship.name} to starboard`} onClick={() => turn('Starboard')}>S</button>
                  </div>
                </td>
                <td>
                  <div className="fleet-orders-step">
                    <button type="button" className="ghost" disabled={locked} aria-label={`Drift ${ship.name}`} onClick={() => onDraftChange(ship.id, { velocityDelta: 0, ...turnPatchFromManeuvers([]) })}>Drift</button>
                    <button type="button" className="ghost" disabled={locked} aria-label={`Give every ship ${ship.name}'s order`} onClick={() => onCopyToFleet(ship.id)}>To all</button>
                  </div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </section>
  );
}

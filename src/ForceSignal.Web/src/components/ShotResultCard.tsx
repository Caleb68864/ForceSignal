/**
 * What this ship's last shot did, right where the Fire button is.
 *
 * Pressing Fire used to change nothing in the console but a "spent" beside the weapon: the dice and
 * the damage were only in the Log tab. Every number here is off the server's FiringResult - the
 * dice it rolled, what screens took off, what armour and hull actually took - and nothing is worked
 * out. In particular the dice are not marked as hits: which faces scored is the profile's beam
 * table, and that reading belongs to the server, whose damage figure is shown beside them.
 */

import type { FiringResult, Ship } from '../types.ts';

/** The last result this ship fired this turn, if it has fired. */
export function lastShotBy(shipId: string, firingResults: FiringResult[]): FiringResult | undefined {
  return firingResults.filter((result) => result.attackerShipId === shipId).at(-1);
}

export function ShotResultCard({ result, ships }: { result: FiringResult; ships: Ship[] }) {
  const target = ships.find((ship) => ship.id === result.targetShipId);
  const landed = result.armorDamageApplied > 0 || result.hullDamageApplied > 0;
  const rolledToHit = result.toHitNumber !== null && result.toHitNumber !== undefined;
  const destroyed = Boolean(target?.isDestroyed) && result.hullDamageApplied > 0;

  return (
    <section className={`shot-result ${landed ? 'landed' : 'no-damage'}`} aria-label="Last shot" aria-live="polite">
      <span className="label">Last shot · {result.weaponName} → {target?.name ?? 'target'}</span>
      <div className="shot-dice" aria-label="Dice rolled">
        {result.diceRolls.length === 0
          ? <small>No dice left to roll at this range.</small>
          : result.diceRolls.map((face, index) => <span key={`${index}-${face}`} className="die">{face}</span>)}
      </div>
      {rolledToHit ? (
        <small>Needed {result.toHitNumber}{result.weaponKind === 'NeedleBeam' ? '' : '+'} to hit: {result.isHit ? 'hit' : 'missed'}.</small>
      ) : null}
      <strong>
        {landed
          ? `${result.armorDamageApplied} armour, ${result.hullDamageApplied} hull`
          : result.isHit === false ? 'Missed'
            // A needle that hits takes a system rather than boxes; which one is in the Log.
            : result.weaponKind === 'NeedleBeam' && result.isHit ? 'System cut out' : 'No damage'}
        {destroyed ? ` · ${target?.name} destroyed` : ''}
      </strong>
      {result.screenReduction > 0 ? <small>Screens took off {result.screenReduction}.</small> : null}
      <small className="shot-note">Threshold checks roll when this ship finishes firing; they are in the Log.</small>
    </section>
  );
}

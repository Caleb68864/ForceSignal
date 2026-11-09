/**
 * The sentence the playback bar shows for each step, so a player can follow the turn in words.
 *
 * Every number in a caption is a field of the server's record for that step - nothing is added up,
 * compared to a target or worked out here, and the caption test holds every caption to exactly the
 * numbers on its record. Ship names come off the snapshot.
 */

import type { FireEvent, ReportedEvent } from './timeline.ts';
import type { MatchSnapshot } from '../../types.ts';

export function captionFor(step: FireEvent | ReportedEvent, snapshot: Pick<MatchSnapshot, 'ships' | 'ordnanceMarkers'>): string {
  const name = (id?: string | null) => snapshot.ships.find((ship) => ship.id === id)?.name ?? 'Unknown';
  // A marker removed since keeps its event; it is just called what it was.
  const marker = (id?: string | null) => snapshot.ordnanceMarkers.find((item) => item.id === id)?.name ?? 'Ordnance';

  if (step.kind === 'fire') {
    const { result } = step;
    const landed = result.armorDamageApplied > 0 || result.hullDamageApplied > 0;
    const outcome = landed
      ? `${result.armorDamageApplied} armour, ${result.hullDamageApplied} hull`
      : result.isHit === false ? 'missed' : 'no damage';
    const screens = result.screenReduction > 0 ? ` · screens took off ${result.screenReduction}` : '';
    const kill = step.destroysTarget ? ' · destroyed' : '';
    return `${name(result.attackerShipId)} → ${name(result.targetShipId)} · ${result.weaponName} · ${outcome}${screens}${kill}`;
  }

  const event = step.event;
  switch (step.kind) {
    case 'flight':
      return `${name(event.actorShipId)} flies`;
    case 'salvo': {
      const landed = (event.armorApplied ?? 0) > 0 || (event.hullApplied ?? 0) > 0;
      return `Salvo strikes ${name(event.targetShipId)} · ${event.arriving ?? 0} of ${event.launched ?? 0} missiles arrive`
        + ` · point defence stops ${event.kills ?? 0}`
        + ` · ${landed ? `${event.armorApplied ?? 0} armour, ${event.hullApplied ?? 0} hull` : 'no damage'}`;
    }
    case 'salvo-wasted':
      return 'A salvo finds nothing in reach';
    case 'point-defence':
      return `${name(event.actorShipId)} point defence fires at ${name(event.targetShipId)} · ${event.kills ?? 0} shot down`;
    case 'strike-stopped':
      return `${name(event.actorShipId)} is wiped out before it can attack ${name(event.targetShipId)}`;
    case 'threshold': {
      const lost = event.lostSystems ?? [];
      return `${name(event.targetShipId)} threshold check · ${lost.length > 0 ? `lost ${lost.join(', ')}` : 'nothing lost'}`;
    }
    case 'destroyed':
      return `${name(event.targetShipId)} destroyed`;
    case 'ordnance-launched':
      return event.actorShipId ? `${name(event.actorShipId)} launches ${marker(event.markerId)}` : `${marker(event.markerId)} placed`;
    case 'ordnance-moved':
      return `${marker(event.markerId)} moves${event.markerStatus === 'Expired' ? ' and runs out of endurance' : ''}`;
  }
}

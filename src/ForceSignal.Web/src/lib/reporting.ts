/**
 * Writing the battle log out for an after-action review.
 *
 * These take the whole log rather than what is on screen: the log view shows only a recent tail,
 * but an exported record is meant to be the complete account of the game.
 */

import { csvEscape, formatLogTime, formatPhase, formatRulesProfile } from './format.ts';
import { normalizeOrdnanceStatus } from './normalize.ts';
import { isFighterGroup } from './rules.ts';
import type { MatchSnapshot } from '../types.ts';

export function matchLogToCsv(snapshot: MatchSnapshot) {
  const summaryRows = [
    ['section', 'name', 'detail'],
    ['match', snapshot.name, `room ${snapshot.joinCode}, turn ${snapshot.turnNumber}, ${formatPhase(snapshot.phase)}`],
    ['table', `${snapshot.tableWidth} x ${snapshot.tableDepth}`, 'inches'],
    ...snapshot.fleets.map((fleet) => [
      'fleet',
      fleet.name,
      `${fleet.faction ?? 'no faction'}, ${snapshot.participants.find((participant) => participant.id === fleet.ownerParticipantId)?.displayName ?? 'unknown'}`,
    ]),
    ...snapshot.ships.map((ship) => [
      'ship',
      ship.name,
      `pos ${ship.positionX.toFixed(1)},${ship.positionY.toFixed(1)}, V${ship.currentVelocity}/C${ship.currentCourse}, hull ${ship.hullDamage}/${ship.hullMax}, armor ${ship.armorDamage}/${ship.armorMax}, screens ${ship.effectiveScreens}${ship.isDestroyed ? ', destroyed' : ''}`,
    ]),
    ...(snapshot.ordnanceMarkers ?? []).map((marker) => [
      'ordnance',
      marker.name,
      `${marker.markerType} ${normalizeOrdnanceStatus(marker.status)}, pos ${marker.positionX.toFixed(1)},${marker.positionY.toFixed(1)}, endurance ${marker.enduranceRemaining}`,
    ]),
  ];

  const logRows = [
    ['sequence', 'timestamp', 'turnNumber', 'phase', 'category', 'message'],
    ...snapshot.matchLog.map((entry) => [
      String(entry.sequence),
      entry.timestamp,
      String(entry.turnNumber),
      entry.phase,
      entry.category,
      entry.message,
    ]),
  ];

  const toCsv = (rows: string[][]) => rows.map((row) => row.map(csvEscape).join(',')).join('\n');
  return `${toCsv(summaryRows)}\n\n${toCsv(logRows)}\n`;
}
export function matchLogToMarkdown(snapshot: MatchSnapshot) {
  const lines = [
    `# ${snapshot.name} After-Action Report`,
    '',
    `Room: ${snapshot.joinCode}`,
    `Table: ${snapshot.tableWidth} x ${snapshot.tableDepth} inches`,
    `Turn: ${snapshot.turnNumber}`,
    `Phase: ${formatPhase(snapshot.phase)}`,
    `Profile: ${formatRulesProfile(snapshot.rulesProfileKey)}`,
    '',
    '## Fleets',
    ...snapshot.fleets.map((fleet) => {
      const owner = snapshot.participants.find((participant) => participant.id === fleet.ownerParticipantId);
      return `- ${fleet.name}${fleet.faction ? ` (${fleet.faction})` : ''}: ${owner?.displayName ?? 'Unknown'}`;
    }),
    '',
    '## Ships',
    ...snapshot.ships.map((ship) => `- ${ship.name}: pos ${ship.positionX.toFixed(1)},${ship.positionY.toFixed(1)}, V${ship.currentVelocity}/C${ship.currentCourse}, hull ${ship.hullDamage}/${ship.hullMax}, armor ${ship.armorDamage}/${ship.armorMax}, screens ${ship.screenRating}${isFighterGroup(ship) ? `, fighters ${ship.fighterStatus} endurance ${ship.fighterEnduranceUsed}/${ship.fighterEnduranceMax} range ${ship.fighterMaxRange}` : ''}${ship.isDestroyed ? ', destroyed' : ''}`),
    '',
    '## Ordnance',
    ...((snapshot.ordnanceMarkers ?? []).length > 0
      ? (snapshot.ordnanceMarkers ?? []).map((marker) => `- ${marker.name}: ${marker.markerType} ${normalizeOrdnanceStatus(marker.status)}, pos ${marker.positionX.toFixed(1)},${marker.positionY.toFixed(1)}, V${marker.speed}/C${marker.course}, endurance ${marker.enduranceRemaining}, dice ${marker.attackDice}, max range ${marker.maxRange}`)
      : ['- None']),
    '',
    '## Battle Log',
    ...snapshot.matchLog.map((entry) => `- T${entry.turnNumber} ${formatPhase(entry.phase)} ${entry.category} ${formatLogTime(entry.timestamp)}: ${entry.message}`),
    '',
  ];

  return `${lines.join('\n')}\n`;
}

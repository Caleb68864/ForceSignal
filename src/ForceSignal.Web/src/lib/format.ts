/**
 * Turning values into text a player reads, and reading caller-supplied text back into values.
 *
 * The three coercions at the bottom are the ones an imported file goes through: a fleet file is
 * user-supplied data, so every number that comes out of one is clamped into a range rather than
 * trusted.
 */

export function formatLogTime(timestamp: string) {
  const date = new Date(timestamp);
  if (Number.isNaN(date.getTime())) {
    return '--:--';
  }

  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
}
export function formatPhase(phase: string) {
  return phase.replace(/([a-z])([A-Z])/g, '$1 $2');
}
/**
 * The rules profile a match is played against, by the name its players gave it.
 *
 * This used to read the engine key, which every match has the same of, and so every board said
 * "Cinematic space fleet profile" - including one with no profile entered at all, which is the one
 * state a new host most needs told about.
 */
export function formatRulesProfile(rules?: { name?: string } | null) {
  return rules?.name?.trim() || 'No rules profile yet';
}

/**
 * What another device just did, in words, for the activity line.
 *
 * The server names each change with an event name (`ParticipantReadyChanged`), and the line used to
 * print that name and a version number. Every name the API sends has a sentence here - the
 * activityText test reads them off the API's own source - and anything newer still gets a sentence
 * rather than a raw name.
 */
export function describeActivity(reason: string): string {
  return activitySentences[reason] ?? 'The table was updated.';
}

const activitySentences: Record<string, string> = {
  ParticipantConnectionChanged: 'A player connected or disconnected.',
  ParticipantJoined: 'A player joined the room.',
  SeatClaimed: 'A player took their seat.',
  MatchRestored: 'The match was restored from a backup.',
  ParticipantReadyChanged: 'A player changed their ready state.',
  RulesProfileChanged: 'The host changed the rules profile.',
  TableUpdated: 'The table size changed.',
  PointsLimitUpdated: 'The points limit changed.',
  FleetChanged: 'A fleet changed.',
  FleetCreated: 'A fleet was created.',
  ShipCreated: 'A ship was added.',
  ShipProfileUpdated: 'A ship was edited.',
  ShipDuplicated: 'A ship was duplicated.',
  ShipDamageUpdated: 'A damage record was changed.',
  RepairsAttempted: 'A damage control party went to work.',
  FighterOperationsUpdated: 'Fighter operations changed.',
  FighterGroupMoved: 'A fighter group flew.',
  OrdnanceMarkerCreated: 'Ordnance was launched.',
  OrdnanceMarkerUpdated: 'An ordnance marker changed.',
  OrdnanceMarkerRemoved: 'An ordnance marker was removed.',
  OrderLockStatusChanged: 'A player locked or unlocked an order.',
  OrderCommitted: 'A player locked an order.',
  OrdersDeclaredComplete: 'A player finished plotting.',
  OrdersDeclarationWithdrawn: 'A player went back to plotting.',
  OrdersRevealed: 'A player revealed their orders.',
  OrderRevealFailed: 'A revealed order did not match what was locked.',
  TurnAdvanced: 'The match moved to the next phase.',
  WeaponFired: 'A weapon fired.',
  FighterStrikeStopped: 'A fighter strike was stopped by point defence.',
  FireCompleted: 'A ship finished firing.',
};
export function slugify(value: string) {
  return value.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'fleet';
}
export function stripFileExtension(fileName: string) {
  return fileName.replace(/\.[^.]+$/, '').replace(/\.forcesignal-fleet$/i, '').trim();
}
export function csvEscape(value: string) {
  return /[",\r\n]/.test(value) ? `"${value.replaceAll('"', '""')}"` : value;
}
export function normalizeHeader(value: string) {
  return value.trim().toLowerCase().replace(/[^a-z0-9]/g, '');
}
export function downloadText(fileName: string, mimeType: string, text: string) {
  const url = URL.createObjectURL(new Blob([text], { type: `${mimeType};charset=utf-8` }));
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  // Revoking in the same tick can cancel the download in some browsers.
  window.setTimeout(() => URL.revokeObjectURL(url), 0);
}
export function wholeNumberFrom(value: unknown, fallback: number, min: number, max: number) {
  const parsed = typeof value === 'number' ? value : Number(value);
  if (!Number.isFinite(parsed)) {
    return fallback;
  }

  return Math.max(min, Math.min(max, Math.round(parsed)));
}
export function numberFrom(value: unknown, fallback: number, min: number, max: number) {
  const parsed = typeof value === 'number' ? value : Number(value);
  if (!Number.isFinite(parsed)) {
    return fallback;
  }

  return Math.max(min, Math.min(max, parsed));
}
export function stringFrom(value: unknown, fallback: string) {
  return typeof value === 'string' && value.trim() ? value.trim() : fallback;
}

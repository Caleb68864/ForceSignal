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
export function formatRulesProfile(profileKey?: string) {
  if (!profileKey || profileKey === 'full-thrust-light-cinematic') {
    return 'Cinematic space fleet profile';
  }

  return profileKey
    .split(/[-_]/)
    .filter(Boolean)
    .map((part) => `${part.charAt(0).toUpperCase()}${part.slice(1)}`)
    .join(' ');
}
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

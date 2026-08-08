/**
 * Talking to the API, and to this browser's own storage.
 *
 * Both are places where a failure has to be handled rather than thrown into a render, which is why
 * the storage writer reports whether it landed instead of assuming.
 */export const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5225';
export class ApiRequestError extends Error {
  constructor(message: string, readonly status: number) {
    super(message);
  }
}
export async function get<T>(path: string, token?: string): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    headers: token ? { 'X-Participant-Token': token } : undefined,
  });
  if (!response.ok) {
    throw await createApiError(response);
  }

  return response.json();
}
export async function post<T>(path: string, body: unknown, token?: string): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { 'X-Participant-Token': token } : {}),
    },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    throw await createApiError(response);
  }

  return response.json();
}
async function createApiError(response: Response) {
  const fallback = `Request failed with status ${response.status}.`;
  const contentType = response.headers.get('content-type') ?? '';
  if (!contentType.includes('application/problem+json') && !contentType.includes('application/json')) {
    return new ApiRequestError(fallback, response.status);
  }

  const problem = await response.json() as { title?: string; detail?: string };
  return new ApiRequestError(problem.detail ?? problem.title ?? fallback, response.status);
}
export function readJson<T>(key: string): T | null {
  const value = localStorage.getItem(key);
  if (!value) {
    return null;
  }

  try {
    return JSON.parse(value) as T;
  } catch {
    localStorage.removeItem(key);
    return null;
  }
}
/**
 * Writes a value to local storage without letting a full store take the app down with it.
 *
 * Every one of these writes used to be unguarded, inside an effect. A browser that has hit its
 * quota throws from `setItem`, the throw escapes the effect, and the player loses the whole screen
 * mid-game. Quota is reachable in a long match because the entire snapshot - battle log included -
 * is rewritten on every update.
 *
 * Returns whether the write landed, so a caller that is storing something it cannot afford to lose
 * can say so rather than assuming.
 */
export function writeStorage(key: string, value: unknown): boolean {
  try {
    localStorage.setItem(key, JSON.stringify(value));
    return true;
  } catch {
    return false;
  }
}
/**
 * A random identifier, on any browser that can load this page.
 *
 * `crypto.randomUUID` exists only in a secure context, which means HTTPS or localhost. ForceSignal
 * is meant to be self-hosted on a laptop at a table and reached over the LAN by plain HTTP, and in
 * that setup every device except the host's own has no `randomUUID` at all. Calling it while the
 * module is still evaluating - which is what a default form value does - threw before React had
 * mounted and left those devices staring at a blank page, with the host unable to reproduce it
 * because their own machine is on localhost.
 *
 * `crypto.getRandomValues` is available in an insecure context, so the fallback is a version 4
 * identifier built from it. It matters that this is real entropy rather than something like
 * Math.random: the same function mints the salt that hides a movement order, and a guessable salt
 * would let an opponent unpick a commitment before it is revealed.
 */
export function newId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }

  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  // Stamp the version and variant bits so the result is a well-formed v4 identifier.
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
export function showError(setMessage: (message: string) => void) {
  return (error: unknown) => {
    setMessage(error instanceof Error ? error.message : 'Something went wrong.');
  };
}


/**
 * Talking to the API, and to this browser's own storage.
 *
 * Both are places where a failure has to be handled rather than thrown into a render, which is why
 * the storage writer reports whether it landed instead of assuming, and why nothing that comes
 * back from either is trusted to be the shape it was written in.
 */
export const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5225';
/**
 * How long a request may go unanswered before it is given up on. A hung socket used to leave the
 * busy guard set for good: every button in the app stayed disabled until the page was reloaded.
 */
export const requestTimeoutMs = 15_000;
export class ApiRequestError extends Error {
  /** `status` is 0 when no response arrived at all: the host is down, unreachable, or too slow. */
  constructor(message: string, readonly status: number) {
    super(message);
  }
}
export async function get<T>(path: string, token?: string, headers?: Record<string, string>): Promise<T> {
  const response = await send(path, {
    headers: { ...(token ? { 'X-Participant-Token': token } : {}), ...headers },
  });
  if (!response.ok) {
    throw await createApiError(response);
  }

  return readBody<T>(response);
}
export async function post<T>(path: string, body: unknown, token?: string, headers?: Record<string, string>): Promise<T> {
  const response = await send(path, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { 'X-Participant-Token': token } : {}),
      ...headers,
    },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    throw await createApiError(response);
  }

  return readBody<T>(response);
}
/**
 * One fetch, with the two failures that produce no response at all turned into something a person
 * at a table can act on. "Failed to fetch" is what the browser says when the host is down or this
 * device is on the wrong network, which for a self-hosted game is the likeliest error there is.
 */
async function send(path: string, init: RequestInit): Promise<Response> {
  try {
    return await fetch(`${apiBaseUrl}${path}`, { ...init, signal: AbortSignal.timeout(requestTimeoutMs) });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'TimeoutError') {
      throw new ApiRequestError(`The ForceSignal server did not answer within ${requestTimeoutMs / 1000} seconds. Try again.`, 0);
    }

    if (error instanceof TypeError) {
      throw new ApiRequestError(
        'Could not reach the ForceSignal server. Check the host is running and this device is on the same network.', 0);
    }

    throw error;
  }
}
/**
 * A successful reply that cannot be read as JSON is a server fault, not a syntax error to show
 * verbatim. The status is kept so the caller can still tell what kind of reply it was.
 */
async function readBody<T>(response: Response): Promise<T> {
  const text = await response.text();
  try {
    return JSON.parse(text) as T;
  } catch {
    throw new ApiRequestError(`The server replied with status ${response.status} but no readable body.`, response.status);
  }
}
async function createApiError(response: Response) {
  const fallback = `Request failed with status ${response.status}.`;
  const contentType = response.headers.get('content-type') ?? '';
  if (!contentType.includes('application/problem+json') && !contentType.includes('application/json')) {
    return new ApiRequestError(fallback, response.status);
  }

  // A problem body that does not parse must still produce an error carrying the status: the
  // expired-session recovery is keyed on a 404, and a SyntaxError in its place left the player
  // with a dead session and no way back short of clearing storage.
  try {
    const problem = await response.json() as { title?: unknown; detail?: unknown };
    const detail = typeof problem.detail === 'string' ? problem.detail : undefined;
    const title = typeof problem.title === 'string' ? problem.title : undefined;
    return new ApiRequestError(detail ?? title ?? fallback, response.status);
  } catch {
    return new ApiRequestError(fallback, response.status);
  }
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
 * Reads a stored value through a validator, and forgets it if it does not pass.
 *
 * Storage is written by whatever version of this app last ran on the device, and can be edited by
 * hand, so it is no more trusted than the wire. A session missing its match id, or a library that
 * is not a list, used to reach a render and take the whole screen down with it.
 */
export function readStored<T>(key: string, normalize: (value: unknown) => T | null): T | null {
  const value = normalize(readJson<unknown>(key));
  if (value === null) {
    localStorage.removeItem(key);
  }

  return value;
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

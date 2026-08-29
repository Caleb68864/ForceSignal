import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiRequestError, get, post, readStored, requestTimeoutMs } from './api.ts';

/**
 * The reply is the one thing the client cannot control. What matters is that every way a request
 * can go wrong comes back as an error that carries the status - the expired-session recovery is
 * keyed on it - and that the two no-response failures are described in words a person at a table
 * can act on.
 */
function reply(status: number, body: string, contentType = 'application/json') {
  return new Response(body, { status, headers: { 'content-type': contentType } });
}

function stubFetch(handler: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>) {
  const fetchMock = vi.fn(handler);
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('get and post', () => {
  it('returns the parsed body of a successful reply', async () => {
    stubFetch(async () => reply(200, '{"joinCode":"ABC"}'));

    await expect(get<{ joinCode: string }>('/api/x')).resolves.toEqual({ joinCode: 'ABC' });
  });

  it('sends the participant token and any extra headers', async () => {
    const fetchMock = stubFetch(async () => reply(200, '{}'));

    await post('/api/x', { a: 1 }, 'participant-token', { 'X-Game-Token': 'game-token' });

    const headers = fetchMock.mock.calls[0][1]?.headers as Record<string, string>;
    expect(headers['X-Participant-Token']).toBe('participant-token');
    expect(headers['X-Game-Token']).toBe('game-token');
    expect(headers['Content-Type']).toBe('application/json');
  });

  it('gives every request a timeout', async () => {
    const fetchMock = stubFetch(async () => reply(200, '{}'));

    await get('/api/x');

    expect(fetchMock.mock.calls[0][1]?.signal).toBeInstanceOf(AbortSignal);
    expect(requestTimeoutMs).toBe(15_000);
  });

  // A 200 with an empty body used to surface as "Unexpected end of JSON input", verbatim.
  it('turns an unreadable success body into an error that keeps the status', async () => {
    stubFetch(async () => reply(200, ''));

    const error = await get('/api/x').catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiRequestError);
    expect((error as ApiRequestError).status).toBe(200);
  });

  it('reads the detail out of a problem body', async () => {
    stubFetch(async () => reply(400, '{"title":"Bad Request","detail":"Order is required."}', 'application/problem+json'));

    const error = await post('/api/x', {}).catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiRequestError);
    expect((error as ApiRequestError).message).toBe('Order is required.');
    expect((error as ApiRequestError).status).toBe(400);
  });

  // A malformed problem body threw a SyntaxError instead of an ApiRequestError, and the 404 that
  // clears an expired session was never seen.
  it('keeps the status when a problem body does not parse', async () => {
    stubFetch(async () => reply(404, '{not json', 'application/problem+json'));

    const error = await get('/api/x').catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiRequestError);
    expect((error as ApiRequestError).status).toBe(404);
    expect((error as ApiRequestError).message).toBe('Request failed with status 404.');
  });

  it('ignores a problem body whose fields are not text', async () => {
    stubFetch(async () => reply(500, '{"title":42,"detail":{"x":1}}', 'application/problem+json'));

    const error = await get('/api/x').catch((caught: unknown) => caught);

    expect((error as ApiRequestError).message).toBe('Request failed with status 500.');
  });

  it('describes a host that cannot be reached in plain words', async () => {
    stubFetch(async () => {
      throw new TypeError('Failed to fetch');
    });

    const error = await get('/api/x').catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiRequestError);
    expect((error as ApiRequestError).status).toBe(0);
    expect((error as ApiRequestError).message).toContain('Could not reach the ForceSignal server');
  });

  it('describes a request that timed out', async () => {
    stubFetch(async () => {
      throw new DOMException('The operation was aborted due to timeout', 'TimeoutError');
    });

    const error = await get('/api/x').catch((caught: unknown) => caught);

    expect((error as ApiRequestError).status).toBe(0);
    expect((error as ApiRequestError).message).toContain('did not answer within 15 seconds');
  });
});

describe('readStored', () => {
  function stubStorage(initial: Record<string, string>) {
    const store = new Map(Object.entries(initial));
    vi.stubGlobal('localStorage', {
      getItem: (key: string) => store.get(key) ?? null,
      setItem: (key: string, value: string) => store.set(key, value),
      removeItem: (key: string) => store.delete(key),
    });
    return store;
  }

  it('returns what the validator accepts', () => {
    stubStorage({ key: '{"id":"a"}' });

    expect(readStored('key', (value) => (value && typeof value === 'object' ? value : null))).toEqual({ id: 'a' });
  });

  it('forgets a value the validator rejects', () => {
    const store = stubStorage({ key: '"not an object"' });

    expect(readStored('key', (value) => (value && typeof value === 'object' ? value : null))).toBeNull();
    expect(store.has('key')).toBe(false);
  });

  it('forgets a value that is not JSON at all', () => {
    const store = stubStorage({ key: '{oops' });

    expect(readStored('key', (value) => value)).toBeNull();
    expect(store.has('key')).toBe(false);
  });
});

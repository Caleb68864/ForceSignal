// @vitest-environment jsdom
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { MatchSnapshot, Session } from './types.ts';

const get = vi.fn<(path: string, token?: string) => Promise<unknown>>();
const post = vi.fn<(path: string, body?: unknown, token?: string) => Promise<unknown>>();

// Only the two functions that talk to the server are replaced. Everything else in the module - the
// storage helpers, the error shape the screen catches on - is the real thing, because the screen is
// what is under test.
vi.mock('./lib/api.ts', async (importActual) => ({
  ...await importActual<typeof import('./lib/api.ts')>(),
  get: (path: string, token?: string) => get(path, token),
  post: (path: string, body?: unknown, token?: string) => post(path, body, token),
}));

vi.mock('./lib/starGruntApi.ts', () => ({
  readFeatures: () => Promise.resolve({ starGrunt: false, dirtside: false }),
}));

// The hub is a live connection to a server that is not there. The screen only needs it to build,
// start and accept a subscription; nothing in this test is driven through it.
vi.mock('@microsoft/signalr', () => {
  class HubConnectionBuilder {
    withUrl() { return this; }
    withAutomaticReconnect() { return this; }
    build() {
      return {
        on: () => undefined,
        onreconnecting: () => undefined,
        onclose: () => undefined,
        onreconnected: () => undefined,
        start: () => Promise.resolve(),
        stop: () => Promise.resolve(),
        invoke: () => Promise.resolve(),
      };
    }
  }

  return { HubConnectionBuilder };
});

function session(room: string): Session & { matchId: string; joinCode: string } {
  return {
    matchId: `match-${room}`,
    participantId: `participant-${room}`,
    participantToken: `token-${room}`,
    joinCode: room,
  };
}

function snapshot(room: string, version: number): MatchSnapshot {
  return {
    matchId: `match-${room}`,
    joinCode: room,
    name: `${room} Match`,
    phase: 'FleetSetup',
    turnNumber: 1,
    rulesProfileKey: 'custom',
    tableWidth: 72,
    tableDepth: 48,
    participants: [{
      id: `participant-${room}`,
      displayName: 'Admiral',
      role: 'Owner',
      isReady: false,
      isConnected: true,
    }] as MatchSnapshot['participants'],
    fleets: [],
    ships: [],
    orderStatuses: [],
    revealedOrders: [],
    movementResults: [],
    firingResults: [],
    ordnanceMarkers: [],
    matchLog: [],
    version,
    pointsLimit: 0,
  };
}

describe('leaving one room and joining the next', () => {
  beforeEach(() => {
    localStorage.clear();
    get.mockReset();
    post.mockReset();
    vi.resetModules();
    document.body.innerHTML = '<div id="app"></div>';
  });

  afterEach(() => {
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
  });

  it('shows the room this device just joined, not the one it left', async () => {
    // The first room's snapshot is held open, so the test decides when it lands.
    let deliverFirstSnapshot: (value: MatchSnapshot) => void = () => undefined;
    const firstSnapshot = new Promise<MatchSnapshot>((resolve) => {
      deliverFirstSnapshot = resolve;
    });

    post.mockImplementation((path) => Promise.resolve(
      path === '/api/matches' ? session('ALPHA') : session('BRAVO')));
    get.mockImplementation((path) => path.startsWith('/api/matches/match-ALPHA')
      ? firstSnapshot
      : Promise.resolve(snapshot('BRAVO', 1)));

    await import('./main.tsx');

    fireEvent.click(await screen.findByRole('button', { name: 'Create Match' }));
    await screen.findByText('Created room ALPHA.');

    // Leave before that first snapshot has arrived. This is the ordinary case, not a contrived one:
    // the screen refetches the whole snapshot on every hub notification and never cancels the
    // request, so any of them can still be in the air when the player taps Leave.
    vi.stubGlobal('confirm', () => true);
    fireEvent.click(screen.getByRole('button', { name: 'Leave Device Session' }));
    await screen.findByRole('button', { name: 'Join Match' });

    // And now it lands, on a screen that has already let go of that match. Version 9 is not a large
    // number - it is four or five actions into a game - but it is larger than the version any fresh
    // room starts at, which is all it takes.
    deliverFirstSnapshot(snapshot('ALPHA', 9));

    fireEvent.change(screen.getByLabelText('Room code'), { target: { value: 'BRAVO' } });
    fireEvent.click(screen.getByRole('button', { name: 'Join Match' }));
    await screen.findByText('Joined room BRAVO.');

    // Snapshot versions are per match and every room starts again at one, so a high water mark left
    // behind by the last match makes the new room's first snapshot look stale and it is dropped on
    // the floor. The join succeeds, the server returns the right board, and the screen goes on
    // showing the previous match's room code - until some later room happens to out-number it.
    await waitFor(() => {
      expect(screen.getByText('BRAVO')).toBeTruthy();
    });
    expect(screen.queryByText('ALPHA')).toBeNull();
  });
});

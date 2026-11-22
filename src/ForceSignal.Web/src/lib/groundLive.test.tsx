// @vitest-environment jsdom
import { act, cleanup, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { GameHandle } from '../types.ts';
import { useGroundLive, type GroundLinkState } from './groundLive.ts';

const hub = vi.hoisted(() => ({
  url: '',
  handlers: {} as Record<string, (...args: unknown[]) => void>,
  reconnected: undefined as undefined | (() => void),
  invoke: vi.fn(),
  stop: vi.fn(),
}));

vi.mock('@microsoft/signalr', () => {
  class HubConnectionBuilder {
    withUrl(url: string) { hub.url = url; return this; }
    withAutomaticReconnect() { return this; }
    configureLogging() { return this; }
    build() {
      return {
        on: (event: string, handler: (...args: unknown[]) => void) => { hub.handlers[event] = handler; },
        onreconnecting: () => undefined,
        onclose: () => undefined,
        onreconnected: (handler: () => void) => { hub.reconnected = handler; },
        start: () => Promise.resolve(),
        stop: () => { hub.stop(); return Promise.resolve(); },
        invoke: (...args: unknown[]) => hub.invoke(...args),
      };
    }
  }

  return { HubConnectionBuilder, LogLevel: { None: 6 } };
});

const game: GameHandle = { gameId: 'game-1', token: 'token-1' };
let seen: GroundLinkState = 'offline';

function Follower({ onChanged, following = game }: { onChanged: (version: number | null) => void; following?: GameHandle | null }) {
  seen = useGroundLive('dirtside', following, onChanged);
  return null;
}

describe('following a ground game', () => {
  beforeEach(() => {
    hub.handlers = {};
    hub.reconnected = undefined;
    hub.invoke.mockReset().mockResolvedValue(undefined);
    hub.stop.mockReset();
    seen = 'offline';
  });

  afterEach(cleanup);

  it('joins with the game code, says it is live, and reads once on joining', async () => {
    const onChanged = vi.fn();
    await act(async () => { render(<Follower onChanged={onChanged} />); });

    expect(hub.url).toMatch(/\/hubs\/ground$/);
    expect(hub.invoke).toHaveBeenCalledWith('JoinGroundGame', 'dirtside', 'game-1', 'token-1');
    expect(seen).toBe('live');
    expect(onChanged).toHaveBeenCalledWith(null);
  });

  it('passes on a push for this game and no other', async () => {
    const onChanged = vi.fn();
    await act(async () => { render(<Follower onChanged={onChanged} />); });
    onChanged.mockClear();

    act(() => {
      hub.handlers.GroundGameChanged('dirtside', 'game-1', 7);
      hub.handlers.GroundGameChanged('dirtside', 'game-2', 8);
      hub.handlers.GroundGameChanged('stargrunt', 'game-1', 9);
    });

    expect(onChanged.mock.calls).toEqual([[7]]);
  });

  it('joins again after a reconnect, and reads again for whatever was missed', async () => {
    const onChanged = vi.fn();
    await act(async () => { render(<Follower onChanged={onChanged} />); });
    hub.invoke.mockClear();
    onChanged.mockClear();

    await act(async () => { hub.reconnected?.(); });

    expect(hub.invoke).toHaveBeenCalledWith('JoinGroundGame', 'dirtside', 'game-1', 'token-1');
    expect(onChanged).toHaveBeenCalledWith(null);
  });

  it('follows nothing without a game, and lets go when the screen does', async () => {
    const view = await act(async () => render(<Follower onChanged={vi.fn()} following={null} />));
    expect(hub.invoke).not.toHaveBeenCalled();
    expect(seen).toBe('offline');

    view.unmount();
    await act(async () => { render(<Follower onChanged={vi.fn()} />); });
    cleanup();
    expect(hub.stop).toHaveBeenCalled();
  });
});

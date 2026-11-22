import * as signalR from '@microsoft/signalr';
import { useEffect, useRef, useState } from 'react';
import { apiBaseUrl } from './api.ts';
import type { GameHandle } from '../types.ts';

export type GroundEngine = 'dirtside' | 'stargrunt';
export type GroundLinkState = 'live' | 'reconnecting' | 'offline';

/**
 * Follows a ground game from this device, so a change made on another device holding the game's code
 * reaches this screen without a reload.
 *
 * `onChanged` is called with the version the server says the game is now at, after every push for
 * this game and after every (re)join - a reconnect may have missed pushes, so the screen re-reads
 * then too. Whether the version is newer than what is on screen is the caller's to judge, because
 * this device's own commands already brought it up to date.
 *
 * A start that fails is tried again on a backoff, as the Full Thrust link does: a laptop hosting the
 * game may come up after the phone has opened the page.
 */
export function useGroundLive(
  engine: GroundEngine,
  game: GameHandle | null,
  onChanged: (version: number | null) => void,
): GroundLinkState {
  const [state, setState] = useState<GroundLinkState>('offline');
  const changed = useRef(onChanged);
  useEffect(() => {
    changed.current = onChanged;
  }, [onChanged]);

  const gameId = game?.gameId;
  const token = game?.token;
  useEffect(() => {
    if (!gameId || !token) {
      setState('offline');
      return;
    }

    let stopped = false;
    let retry: number | undefined;
    let attempts = 0;
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${apiBaseUrl}/hubs/ground`)
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (context) =>
          Math.min(30_000, 1_000 * 2 ** Math.min(context.previousRetryCount, 5)),
      })
      .build();

    connection.on('GroundGameChanged', (pushedEngine: string, pushedGameId: string, version: number) => {
      if (pushedEngine === engine && pushedGameId === gameId) {
        changed.current(version);
      }
    });

    const join = async () => {
      await connection.invoke('JoinGroundGame', engine, gameId, token);
      if (!stopped) {
        setState('live');
        changed.current(null);
      }
    };

    connection.onreconnecting(() => setState('reconnecting'));
    connection.onreconnected(() => {
      join().catch((error: unknown) => {
        console.warn('[ForceSignal] could not follow the ground game again', error);
        setState('offline');
      });
    });
    connection.onclose(() => {
      if (!stopped) {
        setState('offline');
      }
    });

    const start = () => {
      connection.start()
        .then(join)
        .catch((error: unknown) => {
          if (stopped) {
            return;
          }

          console.warn('[ForceSignal] ground game link did not start', error);
          setState('offline');
          attempts += 1;
          retry = window.setTimeout(start, Math.min(30_000, 1_000 * 2 ** Math.min(attempts, 5)));
        });
    };
    start();

    return () => {
      stopped = true;
      window.clearTimeout(retry);
      void connection.stop();
    };
  }, [engine, gameId, token]);

  return state;
}

import type { GameHandle } from '../types.ts';

/**
 * The code that opens a ground game on another device: the game's id and its token, nothing else.
 *
 * The token is the whole credential - every route for the game demands it, and the server mints it
 * from 256 random bits - so whoever holds this code can play the game, which is what a second device
 * at the same table needs. It is never put in a link: a link lands in browser history, chat
 * previews and server logs, and this is a code to read off one screen and type or paste into another.
 */
export function toGameCode(game: GameHandle): string {
  return `${game.gameId}:${game.token}`;
}

const gameIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const tokenPattern = /^[0-9a-f]{64}$/;

/**
 * Reads a pasted game code, or answers null when it is not one. Forgiving about what pasting does to
 * text - surrounding spaces, a line break, upper case - and about nothing else.
 */
export function fromGameCode(text: string): GameHandle | null {
  const cleaned = text.replace(/\s+/g, '').toLowerCase();
  const [gameId, token, ...rest] = cleaned.split(':');
  if (rest.length > 0 || !gameId || !token || !gameIdPattern.test(gameId) || !tokenPattern.test(token)) {
    return null;
  }

  return { gameId, token };
}

import { describe, expect, it } from 'vitest';
import { fromGameCode, toGameCode } from './groundShare.ts';

const game = { gameId: '0f8fad5b-d9cb-469f-a165-70867728950e', token: 'a'.repeat(64) };

describe('the code that opens a ground game on another device', () => {
  it('reads back the game it was written from', () => {
    expect(fromGameCode(toGameCode(game))).toEqual(game);
  });

  it('forgives what pasting does to text', () => {
    expect(fromGameCode(`  ${toGameCode(game).toUpperCase()}\n`)).toEqual(game);
    expect(fromGameCode(`${game.gameId}:\n${game.token}`)).toEqual(game);
  });

  it('is not fooled by something that only looks like one', () => {
    expect(fromGameCode('')).toBeNull();
    expect(fromGameCode(game.gameId)).toBeNull();
    expect(fromGameCode(`${game.gameId}:${'a'.repeat(63)}`)).toBeNull();
    expect(fromGameCode(`${game.gameId}:${'g'.repeat(64)}`)).toBeNull();
    expect(fromGameCode(`not-a-guid:${game.token}`)).toBeNull();
    expect(fromGameCode(`${toGameCode(game)}:extra`)).toBeNull();
  });
});

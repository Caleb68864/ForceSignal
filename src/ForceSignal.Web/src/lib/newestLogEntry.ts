/**
 * The entry a command added to a ground game's log, or undefined when it added none.
 *
 * Both ground engines cap the log (`GroundGameGuards.MaxLogEntries`, 2000) by dropping the oldest
 * entries, so in a long game a command that logs a line leaves the length where it was. The snapshot
 * says how many lines the cap has dropped (`logStart`), so `logStart + log.length` keeps counting
 * every line the game ever wrote, and a rise in it is a line added - the same sentence twice
 * included.
 *
 * A server from before `logStart` sends none. Then a log that grew has logged, and one that did not
 * grow but whose newest entry changed has too; the very same sentence again at the cap is missed,
 * because without a position it cannot be told apart from nothing happening.
 */
export type LoggedSnapshot = { log: readonly string[]; logStart?: number | null };

export function newestLogEntry(before: LoggedSnapshot | null | undefined, after: LoggedSnapshot): string | undefined {
  const log = after.log;
  if (log.length === 0) {
    return undefined;
  }

  const newest = log[log.length - 1];
  const earlier = before?.log ?? [];
  if (typeof after.logStart === 'number' && typeof before?.logStart === 'number') {
    return after.logStart + log.length > before.logStart + earlier.length ? newest : undefined;
  }

  if (log.length > earlier.length) {
    return newest;
  }

  return newest !== earlier[earlier.length - 1] ? newest : undefined;
}

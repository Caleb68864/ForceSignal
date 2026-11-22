/**
 * The entry a command added to a ground game's log, or undefined when it added none.
 *
 * Both ground engines cap the log (`GroundGameGuards.MaxLogEntries`, 2000) by dropping the oldest
 * entries, so in a long game a command that logs a line leaves the length where it was. Reading
 * "it grew" as "it logged" then said nothing for every command after the cap. So a log that did not
 * grow but whose newest entry changed has logged one too. A repeat of the very same sentence at the
 * cap is still missed - it cannot be told apart from nothing happening without an id on the entry.
 */
export function newestLogEntry(before: readonly string[], after: readonly string[]): string | undefined {
  if (after.length === 0) {
    return undefined;
  }

  const newest = after[after.length - 1];
  if (after.length > before.length) {
    return newest;
  }

  return newest !== before[before.length - 1] ? newest : undefined;
}

/** A name in the possessive: "Anvil's", and "Red Lancers'" for one ending in s (playtest p1). */
export function possessive(name: string): string {
  return /s$/i.test(name) ? `${name}'` : `${name}'s`;
}

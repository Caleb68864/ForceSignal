/**
 * What `rulesProfile.ts` offers the rest of the app.
 *
 * A name exported from this module is an invitation to read or write the player's own numbers from
 * somewhere else, and this is the one file this project says the player owns. Four of the things it
 * exported had no importer anywhere: `looksLikeProfile`, which is live and meaningful and is called
 * only by `readProfileFile` one screen below it; `readProfile` and `fieldsBlankedBy`, the same
 * shape; and `isPlayable`, which nothing called at all. An export nobody imports is a decision
 * nobody made - it says "read profiles this way" to a reader who then has two ways to do it, one of
 * which coerces, which is the bug that has now been fixed twice in this module.
 *
 * So this asks the question rather than naming the answer: **every value this module exports has to
 * be imported by something else.** Add an export here tomorrow and the suite fails until it either
 * has a caller or stops being public. That is the only kind of guard that catches the next one.
 *
 * Two boundaries, both deliberate:
 *
 * - **Values only, not types.** `ProfileImport` names the return of an exported function and
 *   `SavedProfiles` names the return of another; a caller has to be able to write those names down
 *   even when it never imports them by hand.
 * - **This module only.** The same walk over the whole of `src/lib` finds nine more names in six
 *   other modules. Every one of them is a real question and none of them is this one, and widening
 *   the guard to cover them would be nine judgements made by a regular expression.
 */

import { describe, expect, it } from 'vitest';

/**
 * Every source file in the app, as text.
 *
 * Read through Vite's own glob rather than `node:fs` because this package has no Node types and is
 * not going to grow a dependency to hold a guard.
 */
const sources = import.meta.glob('../**/*.{ts,tsx}', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

/** Vite normalises glob keys, so the module is found by its path rather than assumed to be at one. */
const MODULE = Object.keys(sources).filter((path) => path.endsWith('/rulesProfile.ts'));

/** Names the module makes public as values - `export function`, `export const`, `export class`. */
function exportedValues(text: string): string[] {
  return [...text.matchAll(/^export\s+(?:async\s+)?(?:function|const|class|let)\s+([A-Za-z0-9_]+)/gm)]
    .map((match) => match[1]);
}

/**
 * Names some other file imports from `rulesProfile.ts`.
 *
 * Read off the import statements rather than by searching each file for the word, because the word
 * turns up in prose: this module and its functions are discussed by name in several doc comments
 * and in two test files, and a plain search would have called every one of those a caller.
 */
function importedElsewhere(): Set<string> {
  const imported = new Set<string>();

  for (const [path, text] of Object.entries(sources)) {
    if (MODULE.includes(path)) {
      continue;
    }

    for (const statement of text.matchAll(/import\s+(?:type\s+)?\{([^}]*)\}\s*from\s*['"]([^'"]+)['"]/g)) {
      if (!statement[2].endsWith('/rulesProfile.ts')) {
        continue;
      }

      for (const specifier of statement[1].split(',')) {
        const name = specifier.trim().replace(/^type\s+/, '').split(/\s+as\s+/)[0].trim();
        if (name) {
          imported.add(name);
        }
      }
    }
  }

  return imported;
}

describe('the rules profile module offers nothing nobody takes', () => {
  it('has every exported value imported by something else', () => {
    // Reached-the-subject: the glob really found this one file and only this one, so what follows
    // is about `rulesProfile.ts` rather than about an empty string.
    expect(MODULE).toHaveLength(1);

    const text = sources[MODULE[0]];
    const exported = exportedValues(text);
    const imported = importedElsewhere();

    // Reached-the-subject, three ways. The file was really read, it really does export things, and
    // - this is the one that matters - the import reader really finds imports. A broken reader that
    // returned an empty set would call every export dead and read as a pile of findings.
    expect(text.length).toBeGreaterThan(1000);
    expect(Object.keys(sources).length).toBeGreaterThan(20);
    expect(exported.length).toBeGreaterThan(4);
    expect(imported.size).toBeGreaterThan(4);

    // The control that must be accepted: these are the module's real public surface, imported by
    // the editor and by its own tests, and they must still be seen as imported however this guard
    // is read. Without it, a reader that found nothing would pass this file by deleting it.
    expect([...imported]).toEqual(expect.arrayContaining(['readProfileFile', 'savedProfiles', 'gapsIn']));

    expect(exported.filter((name) => !imported.has(name))).toEqual([]);
  });
});

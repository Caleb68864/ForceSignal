/**
 * What every module in `src/lib` offers the rest of the app.
 *
 * A name exported from here is an invitation to do something from somewhere else. An export nobody
 * imports is a decision nobody made: it says "read profiles this way" or "coerce a die that way" to
 * a reader who then has two ways to do it, one of which invents numbers - which is the bug that has
 * been fixed twice in `rulesProfile.ts` and three times in `forceIo.ts`.
 *
 * This began as a walk over `rulesProfile.ts` alone, where four such names were found and removed.
 * The same walk over the whole of `src/lib` found nine more in six other modules, and widening the
 * guard was deliberately left undone at the time, because doing it would have meant nine judgements
 * made by a regular expression. The nine judgements have since been made by hand - eight of those
 * names stopped being public and one argued its way onto the list below - so the guard can now cover
 * everything without deciding anything on anybody's behalf.
 *
 * Three boundaries, each of which is a judgement rather than a convenience:
 *
 * - **Values only, not types.** `ProfileImport` names the return of an exported function and
 *   `SavedProfiles` names the return of another; a caller has to be able to write those names down
 *   even when it never imports them by hand.
 * - **A namespace import consumes the whole module.** `import * as api from './starGruntApi.ts'`
 *   is how both API clients are used, and nothing short of a type checker can say which names inside
 *   it are reached. Counting those 41 functions as dead would have been the regular expression
 *   making the judgement; the test below asserts that this rule is load-bearing rather than
 *   theoretical, so it cannot quietly become a way to hide a dead module.
 * - **An importer need not be TypeScript.** `assaultStages` is read by `check-ground-vocabulary.py`,
 *   which CI runs, and that script matches on the `export` keyword - so taking the keyword off would
 *   break the gate. That is the one exemption, and it is exactly the case a widened regular
 *   expression would have got wrong.
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

/**
 * Exports that stay public with no TypeScript importer, each with the reason.
 *
 * To add one you have to write down who reads it, given that nothing in this codebase does. That is
 * the point: the list is one line long, and the line is an argument rather than a name.
 */
const readElsewhere: Record<string, string> = {
  'groundVocabulary.ts:assaultStages':
    'scripts/check-ground-vocabulary.py reads it out of this source in CI, matching on '
    + '`export const assaultStages = [...] as const;` - so the keyword is the interface',
};

/**
 * The modules this guard is about: `src/lib`, less its own tests.
 *
 * Both shapes of key, because Vite normalises a glob against the importing file: a sibling of this
 * test comes back as `./forceIo.ts` and anything further off as `../components/ShipCard.tsx`. A
 * filter that assumed the second form matched nothing at all and the whole walk passed on an empty
 * list, which is the wrong-glob-key failure this file's own reached-the-subject checks exist for.
 */
const modules = Object.keys(sources)
  .filter((path) => (/^\.\/[^/]+\.ts$/.test(path) || /\/lib\/[^/]+\.ts$/.test(path)) && !path.endsWith('.test.ts'))
  .sort();

function fileName(path: string): string {
  return path.slice(path.lastIndexOf('/') + 1);
}

/** The module with this file name, asserted to exist rather than assumed. */
function moduleNamed(name: string): string {
  const found = modules.filter((path) => path.endsWith(`/${name}`));
  expect(found, `${name} is not in src/lib`).toHaveLength(1);
  return found[0];
}

/** Names a module makes public as values - `export function`, `export const`, `export class`. */
function exportedValues(text: string): string[] {
  return [...text.matchAll(/^export\s+(?:async\s+)?(?:function|const|class|let)\s+([A-Za-z0-9_]+)/gm)]
    .map((match) => match[1]);
}

/**
 * What the rest of the app takes from one module: the named imports, and whether any file pulls the
 * whole module in under a namespace.
 *
 * Read off the import statements rather than by searching each file for the word, because the word
 * turns up in prose: these modules and their functions are discussed by name in doc comments and in
 * test files, and a plain search would have called every one of those a caller.
 */
function takenFrom(module: string): { names: Set<string>; wholeModule: boolean } {
  const names = new Set<string>();
  let wholeModule = false;
  const suffix = `/${fileName(module)}`;

  for (const [path, text] of Object.entries(sources)) {
    if (path === module) {
      continue;
    }

    for (const statement of text.matchAll(/import\s+(?:type\s+)?\{([^}]*)\}\s*from\s*['"]([^'"]+)['"]/g)) {
      if (!statement[2].endsWith(suffix)) {
        continue;
      }

      for (const specifier of statement[1].split(',')) {
        const name = specifier.trim().replace(/^type\s+/, '').split(/\s+as\s+/)[0].trim();
        if (name) {
          names.add(name);
        }
      }
    }

    for (const statement of text.matchAll(/import\s+\*\s+as\s+[A-Za-z0-9_]+\s+from\s*['"]([^'"]+)['"]/g)) {
      if (statement[1].endsWith(suffix)) {
        wholeModule = true;
      }
    }
  }

  return { names, wholeModule };
}

describe('the library offers nothing nobody takes', () => {
  it('has every exported value imported by something else, or an argument for why not', () => {
    // Reached-the-subject, four ways. The glob really read files, it really found the library, the
    // modules really export things, and - the one that matters - the import reader really finds
    // imports. A broken reader that returned nothing would call every export dead and read as a
    // pile of findings, which is how a probe in this project's history died on a wrong glob key.
    expect(Object.keys(sources).length).toBeGreaterThan(20);
    expect(modules.length).toBeGreaterThan(10);

    const orphans: string[] = [];
    let exportsSeen = 0;
    let importsSeen = 0;

    for (const module of modules) {
      const exported = exportedValues(sources[module]);
      const taken = takenFrom(module);
      exportsSeen += exported.length;
      importsSeen += taken.names.size;

      if (taken.wholeModule) {
        continue;
      }

      for (const name of exported) {
        if (!taken.names.has(name) && !(`${fileName(module)}:${name}` in readElsewhere)) {
          orphans.push(`${fileName(module)}:${name}`);
        }
      }
    }

    expect(exportsSeen).toBeGreaterThan(50);
    expect(importsSeen).toBeGreaterThan(50);

    // The control that must be accepted: these are real public surface, imported by the screens and
    // by the suite, and they must still be seen as imported however this guard is read. Without it,
    // a reader that found nothing would pass this file by deleting the library.
    const profile = takenFrom(moduleNamed('rulesProfile.ts'));
    expect([...profile.names]).toEqual(expect.arrayContaining(['readProfileFile', 'savedProfiles', 'gapsIn']));

    expect(orphans).toEqual([]);
  });

  it('treats a module as consumed only where something really imports it whole', () => {
    // The namespace rule is the widest thing this guard does, so what it excuses is asserted rather
    // than assumed. `starGruntApi.ts` is reached as a namespace by the screen that plays the game
    // and by one named import besides, so the rule really is carrying the rest of it - and
    // `rulesProfile.ts` is not reached that way at all, so the rule is doing work on one shape of
    // module rather than quietly excusing the library.
    const api = moduleNamed('starGruntApi.ts');
    const taken = takenFrom(api);
    expect(taken.wholeModule).toBe(true);

    const excused = exportedValues(sources[api]).filter((name) => !taken.names.has(name));
    expect(excused.length).toBeGreaterThan(10);

    expect(takenFrom(moduleNamed('rulesProfile.ts')).wholeModule).toBe(false);
  });

  it('holds every exemption to naming an export that is really there', () => {
    // The list not rotting. An exemption for a name somebody has since renamed or unexported is an
    // argument nobody is making any more, and a list of those would let the next real one through
    // under a familiar-looking name.
    for (const [key, reason] of Object.entries(readElsewhere)) {
      const [file, name] = key.split(':');

      expect(exportedValues(sources[moduleNamed(file)])).toContain(name);
      expect(reason.length).toBeGreaterThan(20);
    }
  });
});

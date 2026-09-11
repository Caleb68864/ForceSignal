import { describe, expect, it } from 'vitest';
import spriteMarkup from '../assets/unit-icons.svg?raw';
import { unitIconOptions } from './UnitIcon.tsx';

/**
 * Every source file in the app, read through Vite's own glob rather than `node:fs` - this package
 * has no Node types and is not going to grow a dependency to hold a guard. The same reasoning, and
 * the same mechanism, as `libSurface.test.ts`.
 */
const sources = import.meta.glob('../**/*.{ts,tsx}', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

/** The app's own modules: not tests, and not the icon module whose reachability is in question. */
const productionSources = Object.entries(sources)
  .filter(([path]) => !/\.test\.tsx?$/.test(path) && !path.endsWith('/UnitIcon.tsx'));

/**
 * Modules the bundle can reach that actually <em>import</em> the named component module.
 *
 * Import statements rather than the word, which is the same judgement `libSurface.test.ts` makes
 * and for a sharper reason here: the first version of this counted any mention, and `main.tsx`'s
 * own comment about the credit names `UnitIcon.tsx` - so the detector reported the icons wired up
 * on a tree where nothing imports them. A guard that answers yes to a sentence about itself is
 * worse than no guard.
 *
 * Takes the module name so the check below can be run against one that <em>is</em> imported. There
 * is no way to control it with the icon module itself: `import.meta.glob` never includes the file
 * that calls it, so this test's own import of `UnitIcon.tsx` is invisible from in here - which is
 * exactly the false green the first attempt at a control walked into.
 */
function importersOf(moduleName: string): string[] {
  const importStatement = new RegExp(`from\\s*['"][^'"]*/${moduleName}(\\.tsx)?['"]`);
  return productionSources
    .filter(([, text]) => importStatement.test(text))
    .map(([path]) => path)
    .sort();
}

/**
 * The icon sheet is the one piece of artwork in this app that ForceSignal did not draw. It is used
 * under Creative Commons Attribution, so the credit is not decoration - dropping it breaks the
 * licence. These check both that the icons are usable and that the attribution is still there.
 */
describe('the unit icon sheet', () => {
  it('has a symbol for every unit type the app offers', () => {
    for (const option of unitIconOptions) {
      expect(spriteMarkup).toContain(`id="gi-${option.key}"`);
    }
  });

  it('offers no unit type without artwork behind it', () => {
    const ids = [...spriteMarkup.matchAll(/id="gi-([a-z-]+)"/g)].map((match) => match[1]);
    expect(ids.sort()).toEqual(unitIconOptions.map((option) => option.key).sort());
  });

  // game-icons ship a full-bleed black square behind each glyph. Left in, every icon renders as a
  // black tile on a dark map and the whole set looks broken.
  it('has had the black backing square stripped out', () => {
    expect(spriteMarkup).not.toContain('M0 0h512v512H0z');
  });

  // An explicit white fill would beat the CSS, and units could not be tinted by side.
  it('leaves the glyphs to take their colour from the page', () => {
    expect(spriteMarkup).not.toContain('fill="#fff"');
    expect(spriteMarkup).not.toContain('fill="#ffffff"');
  });

  it('keeps every symbol on the coordinate system its paths were drawn for', () => {
    const symbols = [...spriteMarkup.matchAll(/<symbol[^>]*>/g)].map((match) => match[0]);
    expect(symbols).toHaveLength(unitIconOptions.length);
    for (const symbol of symbols) {
      expect(symbol).toContain('viewBox="0 0 512 512"');
    }
  });

  it('carries real path data rather than an empty shell', () => {
    const paths = [...spriteMarkup.matchAll(/ d="([^"]+)"/g)].map((match) => match[1]);
    expect(paths.length).toBeGreaterThanOrEqual(unitIconOptions.length);
    for (const path of paths) {
      expect(path.length).toBeGreaterThan(50);
    }
  });

  it('still credits every artist, which is what the licence is for', () => {
    for (const artist of ['Lorc', 'Delapouite', 'Skoll', 'sbed']) {
      expect(spriteMarkup).toContain(artist);
    }

    expect(spriteMarkup).toContain('game-icons.net');
    expect(spriteMarkup).toContain('Creative Commons Attribution 3.0');
  });
});

/**
 * The credit in the notice panel, held to what the app actually ships.
 *
 * The sheet was licensed under Creative Commons Attribution, which asks for the credit wherever the
 * work is *distributed* - and a public repository distributes it. So the credit stays while the
 * files are here, and the thing that had to change was the claim rather than the credit: the panel
 * used to read as though the app drew these icons, and no screen does. `UnitIcon.tsx` is imported by
 * nothing but this file, so the set is tree-shaken out and the built bundle contains none of it.
 *
 * These two tests are why that stays true in both directions. They are a pair on purpose: one of
 * them fails the moment somebody wires the icons up without going back to the stronger sentence, and
 * the other fails if the credit is deleted while the artwork is still in the tree - which is the
 * failure that would actually break the licence.
 */
describe('unitIconCreditIsTrue', () => {
  const credit = sources['../main.tsx'] ?? '';

  it('is reading the notice panel it claims to be reading', () => {
    // Reached-the-subject. A glob key that stopped matching would hand every assertion below an
    // empty string, and `expect('').toContain(...)` fails loudly - but `not.toContain` would pass,
    // which is the half that would have gone quiet. So the source is checked first.
    expect(credit).toContain('Unofficial companion');
    expect(credit).toContain('data-icon-status=');
    expect(productionSources.length).toBeGreaterThan(10);
  });

  it('can see an importer when there is one to see', () => {
    // The control, and the one that stops the check below being a scan that reads nothing. A
    // detector that matched nothing would report "no screen uses the icons" forever and agree with
    // the shipping tree by accident - which is how this project's probes have failed before.
    //
    // Controlled against a sibling that really is imported rather than against the icon module,
    // because `import.meta.glob` excludes the file that calls it: this test's own import of
    // `UnitIcon.tsx` cannot be seen from in here, and the first attempt at a control was asserting
    // on something structurally invisible.
    expect(importersOf('ShipCard')).toEqual(['../main.tsx', './map/PlayMap.tsx']);
  });

  it('credits the artists while the artwork is in the tree', () => {
    // The artwork is here - the sheet imported at the top of this file is proof - so the credit is
    // a licence obligation and not a courtesy.
    expect(spriteMarkup.length).toBeGreaterThan(0);
    for (const artist of ['Lorc', 'Delapouite', 'Skoll', 'sbed']) {
      expect(credit).toContain(artist);
    }

    expect(credit).toContain('game-icons.net');
    expect(credit).toContain('creativecommons.org/licenses/by/3.0/');
  });

  it('says the icons are shipped rather than shown, for exactly as long as that is so', () => {
    // The half that makes this more than a spell-check. The panel states its claim as a marker
    // rather than as prose, because the prose is JSX and reflows - the first version of this check
    // matched on the sentence, the sentence was split across two source lines, and *both* branches
    // passed against a string that appeared nowhere in the file. So the claim is an attribute now,
    // which cannot be line-wrapped, and it is compared against what the import graph actually says.
    const importers = importersOf('UnitIcon');
    const claim = /data-icon-status="([a-z-]+)"/.exec(credit)?.[1];

    expect(claim).toBe(importers.length === 0 ? 'shipped-not-shown' : 'shipped-and-shown');
  });
});

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

/** True when anything the bundle can reach names the icon module. */
function anyScreenUsesTheIcons(): boolean {
  return productionSources.some(([, text]) => /\bUnitIcon\b/.test(text));
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
    expect(productionSources.length).toBeGreaterThan(10);
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
    // The control that must be accepted, and the half that makes this more than a spell-check: if
    // the module ever becomes reachable, the "no screen draws one yet" sentence is a false
    // statement in the other direction and this fails until somebody rewrites it.
    if (anyScreenUsesTheIcons()) {
      expect(credit).not.toContain('no screen draws one yet');
      return;
    }

    expect(credit).toContain('no screen draws one yet');
  });
});

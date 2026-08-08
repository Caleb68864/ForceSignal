import { describe, expect, it } from 'vitest';
import spriteMarkup from '../assets/unit-icons.svg?raw';
import { unitIconOptions } from './UnitIcon.tsx';

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

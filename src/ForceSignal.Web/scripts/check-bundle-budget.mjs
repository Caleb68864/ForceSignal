/* global console, process, URL -- a Node script */
// Holds the built web app to its size budget. Run after `npm run build` (scripts/verify.ps1 does).
//
// Two numbers, both gzip, because gzip is what a phone on venue wifi actually downloads:
// - the entry chunk every device loads to open the app, and
// - everything loaded later, which today is the animated map: Pixi and the playback layer.
// And one rule: no Pixi renderer code in the entry chunk. The animated map is a per-device choice;
// a device that leaves it off should not pay for it. A static import of anything in
// src/components/map/pixi from the main app would quietly break that, and this is what notices.
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { gzipSync } from 'node:zlib';

const budget = {
  entryKb: 145,
  lazyKb: 160,
};

const dist = new URL('../dist/', import.meta.url).pathname;
const html = readFileSync(join(dist, 'index.html'), 'utf8');
const entryName = /<script type="module"[^>]*src="\/assets\/([^"]+\.js)"/.exec(html)?.[1];
if (!entryName) {
  console.error('No entry script in dist/index.html - run `npm run build` first.');
  process.exit(1);
}

const kb = (bytes) => Math.round((bytes / 1024) * 10) / 10;
const gzipped = (name) => gzipSync(readFileSync(join(dist, 'assets', name)), { level: 9 }).length;
const scripts = readdirSync(join(dist, 'assets')).filter((name) => name.endsWith('.js'));
const entry = gzipped(entryName);
const lazy = scripts.filter((name) => name !== entryName).reduce((total, name) => total + gzipped(name), 0);
const entryText = readFileSync(join(dist, 'assets', entryName), 'utf8');
// Strings that live inside the code of Pixi and the animated map, never in a chunk's file name: the
// entry chunk lists the lazy chunks by name for preloading, so a file name would always be found.
const leaked = ['PixiJS', 'pixi-map-canvas', 'forcesignal:pixi-first-paint'].filter((marker) => entryText.includes(marker));

const problems = [];
if (kb(entry) > budget.entryKb) {
  problems.push(`entry chunk ${entryName} is ${kb(entry)} kB gzip, over the ${budget.entryKb} kB budget`);
}

if (kb(lazy) > budget.lazyKb) {
  problems.push(`lazily loaded chunks total ${kb(lazy)} kB gzip, over the ${budget.lazyKb} kB budget`);
}

if (leaked.length > 0) {
  problems.push(`the entry chunk contains animated-map code (${leaked.join(', ')}): something imports it statically`);
}

console.log(`entry ${kb(entry)} / ${budget.entryKb} kB gzip · lazy ${kb(lazy)} / ${budget.lazyKb} kB gzip (${scripts.length - 1} chunks)`);
if (problems.length > 0) {
  for (const problem of problems) {
    console.error(`Bundle budget: ${problem}.`);
  }

  process.exit(1);
}

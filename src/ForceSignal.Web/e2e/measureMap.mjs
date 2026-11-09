/* global process, console, window, document, performance, requestAnimationFrame -- node script whose page.evaluate callbacks run in the browser */
// Times the tactical map, classic against animated, for docs/visuals/findings.
//
//   npx vite build --config vite.bench.config.ts && npx vite preview --config vite.bench.config.ts &
//   CHROMIUM_PATH=/usr/bin/chromium node e2e/measureMap.mjs > results.json
//
// For each renderer, ship count and device profile it loads the bench page and records: time to
// the first frame with the map on it; frame rate and slow frames while the turn plays back; the
// same while a scripted pan and zoom runs; how long React spent committing during that pan; and
// the JS heap afterwards. "Mobile" is a 390x844 phone viewport with the CPU throttled four times.
import { chromium } from '@playwright/test';

const base = process.env.BENCH_URL ?? 'http://localhost:4173/bench/map.html';
const renderers = (process.env.RENDERERS ?? 'dom,pixi').split(',');
const counts = (process.env.SHIPS ?? '10,30,60').split(',').map(Number);
const profiles = (process.env.PROFILES ?? 'desktop,mobile').split(',');
const runs = Number(process.env.RUNS ?? 3);

const browser = await chromium.launch({
  executablePath: process.env.CHROMIUM_PATH || undefined,
  args: (process.env.CHROMIUM_ARGS ?? '').split(' ').filter(Boolean),
});

async function frameStats(page, action) {
  await page.evaluate(() => {
    const times = [];
    window.__frames = times;
    window.__framesOn = true;
    const tick = (t) => {
      times.push(t);
      if (window.__framesOn) requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
  });
  const commitsBefore = await page.evaluate(() => window.__bench.commits.length);
  await action();
  return page.evaluate((from) => {
    window.__framesOn = false;
    const t = window.__frames;
    const deltas = t.slice(1).map((value, index) => value - t[index]).sort((a, b) => a - b);
    const span = t.length > 1 ? t[t.length - 1] - t[0] : 0;
    const commits = window.__bench.commits.slice(from).map((c) => c.ms);
    return {
      fps: span > 0 ? Math.round(((t.length - 1) / span) * 1000 * 10) / 10 : 0,
      p95FrameMs: Math.round((deltas[Math.floor(deltas.length * 0.95)] ?? 0) * 10) / 10,
      slowFrames: deltas.filter((d) => d > 50).length,
      commits: commits.length,
      commitMsTotal: Math.round(commits.reduce((a, b) => a + b, 0) * 10) / 10,
      commitMsMax: Math.round(Math.max(0, ...commits) * 10) / 10,
    };
  }, commitsBefore);
}

async function measureOnce(renderer, ships, profile) {
  const mobile = profile === 'mobile';
  const context = await browser.newContext(mobile
    ? { viewport: { width: 390, height: 844 }, deviceScaleFactor: 3, isMobile: true, hasTouch: true }
    : { viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 });
  const page = await context.newPage();
  const cdp = await context.newCDPSession(page);
  await cdp.send('Performance.enable');
  if (mobile) {
    await cdp.send('Emulation.setCPUThrottlingRate', { rate: 4 });
  }

  const url = `${base}?ships=${ships}${renderer === 'pixi' ? '&renderer=pixi' : ''}`;
  await page.goto(url, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__bench?.firstPaintAt !== undefined, null, { timeout: 60_000 });
  if (renderer === 'pixi') {
    await page.waitForFunction(() => performance.getEntriesByName('forcesignal:pixi-first-paint').length > 0, null, { timeout: 60_000 });
  }

  const firstPaint = await page.evaluate((pixi) => {
    const pixiMark = performance.getEntriesByName('forcesignal:pixi-first-paint')[0]?.startTime;
    return Math.round(pixi ? Math.max(pixiMark ?? 0, window.__bench.firstPaintAt) : window.__bench.firstPaintAt);
  }, renderer === 'pixi');

  // Playback (Pixi only): the moves and every shot, watched to the end or 12 s.
  const playback = renderer === 'pixi'
    ? await frameStats(page, () => page.waitForFunction(() => document.querySelector('.table-map')?.dataset.playback === 'idle', null, { timeout: 12_000 }).catch(() => undefined))
    : null;

  // Start the drag on open table: on a phone the map is small, and its middle can be a ship (which
  // a press selects rather than pans) or the playback bar.
  const map = await page.locator('.table-map').boundingBox();
  const start = await page.evaluate(({ x, y, width, height }) => {
    for (let row = 1; row < 10; row += 1) {
      for (let column = 1; column < 10; column += 1) {
        const px = x + (width * column) / 10;
        const py = y + (height * row) / 10;
        const hit = document.elementFromPoint(px, py);
        if (hit && (hit.classList.contains('table-map') || hit.classList.contains('table-map-content'))) {
          return { x: px, y: py };
        }
      }
    }

    return { x: x + width / 2, y: y + height / 2 };
  }, map);
  const cx = start.x;
  const cy = start.y;
  const panZoom = await frameStats(page, async () => {
    await page.mouse.move(cx, cy);
    await page.mouse.down();
    for (let step = 1; step <= 40; step += 1) {
      await page.mouse.move(cx + Math.sin(step / 6) * 120, cy + Math.cos(step / 6) * 60);
    }
    await page.mouse.up();
    for (let step = 0; step < 10; step += 1) {
      await page.mouse.wheel(0, step < 5 ? -120 : 120);
      await page.waitForTimeout(30);
    }
    await page.waitForTimeout(150);
  });

  const metrics = Object.fromEntries((await cdp.send('Performance.getMetrics')).metrics.map((m) => [m.name, m.value]));
  const gl = await page.evaluate(() => {
    const canvas = document.createElement('canvas');
    const context = canvas.getContext('webgl');
    const info = context?.getExtension('WEBGL_debug_renderer_info');
    return info ? context.getParameter(info.UNMASKED_RENDERER_WEBGL) : context ? 'webgl (renderer hidden)' : 'no webgl';
  });
  const errors = [];
  page.on('pageerror', (error) => errors.push(String(error)));
  await context.close();
  return { firstPaint, playback, panZoom, heapMb: Math.round((metrics.JSHeapUsedSize / 1048576) * 10) / 10, nodes: metrics.Nodes, gl, errors };
}

const median = (values) => {
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.floor(sorted.length / 2)];
};

const results = [];
for (const profile of profiles) {
  for (const renderer of renderers) {
    for (const ships of counts) {
      const samples = [];
      for (let run = 0; run < runs; run += 1) {
        samples.push(await measureOnce(renderer, ships, profile));
      }

      const pick = (path) => median(samples.map((sample) => path(sample) ?? 0));
      const row = {
        profile, renderer, ships,
        firstPaintMs: pick((s) => s.firstPaint),
        playbackFps: renderer === 'pixi' ? pick((s) => s.playback.fps) : null,
        playbackSlowFrames: renderer === 'pixi' ? pick((s) => s.playback.slowFrames) : null,
        panFps: pick((s) => s.panZoom.fps),
        panP95FrameMs: pick((s) => s.panZoom.p95FrameMs),
        panSlowFrames: pick((s) => s.panZoom.slowFrames),
        panReactCommitMs: pick((s) => s.panZoom.commitMsTotal),
        panReactCommitMaxMs: pick((s) => s.panZoom.commitMsMax),
        heapMb: pick((s) => s.heapMb),
        domNodes: pick((s) => s.nodes),
        gl: samples[0].gl,
      };
      results.push(row);
      console.error(JSON.stringify(row));
    }
  }
}

await browser.close();
console.log(JSON.stringify(results, null, 2));

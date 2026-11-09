// Browser checks for the animated map. Runs against the bench page, which
// needs no API. No browser is downloaded: point CHROMIUM_PATH at an installed Chromium.
import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: 'e2e',
  timeout: 60_000,
  use: {
    baseURL: 'http://localhost:4173',
    launchOptions: { executablePath: process.env.CHROMIUM_PATH || undefined },
  },
  webServer: {
    command: 'npx vite build --config vite.bench.config.ts && npx vite preview --config vite.bench.config.ts',
    url: 'http://localhost:4173/bench/map.html',
    reuseExistingServer: true,
    timeout: 120_000,
  },
});

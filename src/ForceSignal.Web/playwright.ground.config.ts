// Browser checks for the ground screens, against a real API with both ground engines switched on
// and the Vite dev server in front of it. Separate from playwright.config.ts, whose bench page needs
// no API. No browser is downloaded: point CHROMIUM_PATH at an installed Chromium, or install one
// with `npx playwright install chromium`.
//
// The servers are started here unless something already answers on their ports (CI starts its own
// pair; locally a running dev pair is reused). GROUND_API_PORT / GROUND_WEB_PORT move them.
import { defineConfig } from '@playwright/test';

const apiPort = process.env.GROUND_API_PORT ?? '8080';
const webPort = process.env.GROUND_WEB_PORT ?? '6297';

export default defineConfig({
  testDir: 'e2e-ground',
  timeout: 60_000,
  use: {
    baseURL: `http://localhost:${webPort}`,
    launchOptions: { executablePath: process.env.CHROMIUM_PATH || undefined },
  },
  webServer: [
    {
      command: `dotnet run --project ../ForceSignal.Api --no-launch-profile --urls http://localhost:${apiPort}`,
      url: `http://localhost:${apiPort}/health`,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        FORCESIGNAL_FEATURES_STARGRUNT: 'true',
        FORCESIGNAL_FEATURES_DIRTSIDE: 'true',
      },
      reuseExistingServer: true,
      timeout: 180_000,
    },
    {
      command: `npx vite --port ${webPort} --strictPort`,
      url: `http://localhost:${webPort}`,
      env: { VITE_API_BASE_URL: `http://localhost:${apiPort}` },
      reuseExistingServer: true,
      timeout: 120_000,
    },
  ],
});

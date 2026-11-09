/* global process, HTMLCanvasElement -- read by the Node test runner, and inside the page's init script */
// Without WebGL the animated map still draws (Pixi falls back to a 2D canvas); a device that cannot
// draw at all gets the classic map and is told why.
import { expect, test } from '@playwright/test';

test.use({
  launchOptions: {
    executablePath: process.env.CHROMIUM_PATH || undefined,
    args: ['--disable-3d-apis', '--disable-webgl', '--disable-gpu'],
  },
});

test('still draws the animated map without WebGL', async ({ page }) => {
  await page.goto('/bench/map.html?ships=8&renderer=pixi');
  expect(await page.evaluate(() => Boolean(document.createElement('canvas').getContext('webgl')))).toBe(false);
  await expect(page.locator('.pixi-map-host canvas')).toHaveAttribute('data-ships', '8');
  await expect(page.locator('.table-map')).toHaveClass(/pixi-renderer/);
});

test('falls back to the classic map when the page cannot draw on a canvas at all', async ({ page }) => {
  await page.addInitScript(() => {
    HTMLCanvasElement.prototype.getContext = () => null;
  });
  await page.goto('/bench/map.html?ships=8&renderer=pixi');
  await expect(page.getByText(/animated map cannot run on this device/i)).toBeVisible({ timeout: 15_000 });
  await expect(page.locator('.table-map')).not.toHaveClass(/pixi-renderer/);
  await expect(page.locator('.map-ship')).toHaveCount(8);
});

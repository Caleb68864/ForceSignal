// The animated map as a per-device setting, and the classic map as its fallback.
import { expect, test } from '@playwright/test';

test('turning the animated map on draws the canvas, and stays on after a reload', async ({ page }) => {
  await page.goto('/bench/map.html?ships=8');
  await expect(page.locator('.pixi-map-host')).toHaveCount(0);

  await page.getByRole('checkbox', { name: 'Animated map' }).check();
  await expect(page.locator('.pixi-map-host canvas')).toHaveAttribute('data-ships', '8');

  await page.reload();
  await expect(page.locator('.pixi-map-host canvas')).toHaveAttribute('data-ships', '8');
});

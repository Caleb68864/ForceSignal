// The animated map renders, plays a turn, and honours reduced motion.
import { expect, test, type Page } from '@playwright/test';

function watchErrors(page: Page) {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(String(error)));
  page.on('console', (message) => {
    if (message.type() === 'error' && !message.text().includes('favicon') && !message.text().includes('404')) {
      errors.push(message.text());
    }
  });
  return errors;
}

test('the Pixi map draws every hull under the DOM buttons, without errors', async ({ page }) => {
  const errors = watchErrors(page);
  await page.goto('/bench/map.html?renderer=pixi&ships=8');
  const canvas = page.locator('.pixi-map-host canvas');
  await expect(canvas).toHaveAttribute('data-ships', '8');
  await expect(page.locator('.map-ship')).toHaveCount(8);

  const map = await page.locator('.table-map').boundingBox();
  const box = await canvas.boundingBox();
  expect(box?.width).toBeGreaterThan(200);
  // The canvas fills the map's content box: the map less its 1px border.
  expect(Math.abs((box?.width ?? 0) - ((map?.width ?? 0) - 2))).toBeLessThanOrEqual(1);
  expect(errors).toEqual([]);
});

test('a resolved turn plays, and Skip lands on the result', async ({ page }) => {
  await page.goto('/bench/map.html?renderer=pixi&ships=8');
  const map = page.locator('.table-map');
  await expect(map).toHaveAttribute('data-playback', 'running');
  await page.getByRole('button', { name: 'Skip' }).click();
  await expect(map).toHaveAttribute('data-playback', 'idle');
  await expect(page.locator('.map-playback-bar p')).toHaveText('4 shots this turn');
  await expect(page.locator('.pixi-map-host canvas')).toHaveAttribute('data-effects', '0');
});

test.describe('with reduced motion', () => {
  test.use({ reducedMotion: 'reduce' });

  test('shows the result at once, with no effect ever drawn', async ({ page }) => {
    await page.goto('/bench/map.html?renderer=pixi&ships=8');
    const canvas = page.locator('.pixi-map-host canvas');
    await expect(canvas).toHaveAttribute('data-ships', '8');
    await expect(page.locator('.table-map')).toHaveAttribute('data-playback', 'idle');
    await expect(canvas).toHaveAttribute('data-effects', '0');
    // Nothing to replay when nothing animates: the button is not offered at all.
    await expect(page.locator('.map-replay')).toBeHidden();
  });
});

test('the DOM map is the default and downloads none of Pixi', async ({ page }) => {
  const requested: string[] = [];
  page.on('request', (request) => requested.push(request.url()));
  await page.goto('/bench/map.html?ships=8');
  await expect(page.locator('.map-ship')).toHaveCount(8);
  await page.waitForTimeout(500);
  expect(page.locator('.pixi-map-host')).toHaveCount(0);
  expect(requested.filter((url) => /PixiTableLayer|RenderTargetSystem/.test(url))).toEqual([]);
});

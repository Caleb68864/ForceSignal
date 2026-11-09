/* global getComputedStyle -- runs inside the page */
// Ordnance markers glide to a new position rather than jump, on either map, unless motion is reduced.
import { expect, test } from '@playwright/test';

async function glide(page: import('@playwright/test').Page) {
  const marker = page.locator('.ordnance-marker').first();
  await expect(marker).toBeVisible();
  return marker.evaluate((element) => {
    const style = getComputedStyle(element);
    return { property: style.transitionProperty, duration: style.transitionDuration };
  });
}

test('an ordnance marker glides when it moves', async ({ page }) => {
  await page.goto('/bench/map.html?ships=8&renderer=dom');
  const { property, duration } = await glide(page);
  expect(property).toContain('left');
  expect(property).toContain('top');
  expect(duration).toContain('0.9s');
});

test.describe('with reduced motion', () => {
  test.use({ reducedMotion: 'reduce' });

  test('an ordnance marker jumps straight to where it is', async ({ page }) => {
    await page.goto('/bench/map.html?ships=8&renderer=dom');
    const { property } = await glide(page);
    expect(property).not.toContain('left');
  });
});

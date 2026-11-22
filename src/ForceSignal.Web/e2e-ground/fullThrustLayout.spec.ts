// Full Thrust's board at desktop size, against a real API.
import { expect, test } from '@playwright/test';
import { openSeededMatch } from './fullThrustSeed.ts';

test.use({ viewport: { width: 1440, height: 900 } });

test('a compact ship card does not stretch to the expanded card beside it (playtest M5)', async ({ page, request }) => {
  await openSeededMatch(page, request);
  const cards = page.locator('article.ship-card');
  await expect(cards.first()).toBeVisible();
  await page.getByRole('button', { name: 'Ships', exact: true }).click().catch(() => undefined);

  const compact = page.locator('article.ship-card.compact').first();
  await expect(compact).toBeVisible();
  const chipHeights = await compact.locator('.ship-alerts span').evaluateAll((spans) => spans.map((span) => span.getBoundingClientRect().height));
  const buttonHeights = await compact.locator('.card-actions button').evaluateAll((buttons) => buttons.map((button) => button.getBoundingClientRect().height));

  expect(chipHeights.length).toBeGreaterThan(0);
  for (const height of chipHeights) {
    expect(height).toBeLessThan(40);
  }
  for (const height of buttonHeights) {
    expect(height).toBeLessThan(60);
  }
});

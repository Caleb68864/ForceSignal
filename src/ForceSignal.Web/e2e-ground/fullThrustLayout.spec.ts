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

test('the map labels sister ships so they can be told apart (playtest M6)', async ({ page, request }) => {
  await openSeededMatch(page, request, { blueShips: ['Blue 1', 'Blue 2', 'Blue 3'], redShips: ['Red 1'] });
  await page.getByRole('button', { name: 'Play Map', exact: true }).click();

  const labels = page.locator('.table-map button strong');
  await expect(labels.first()).toBeVisible();
  const texts = await labels.allInnerTexts();
  expect(texts).toEqual(expect.arrayContaining(['B1', 'B2', 'B3', 'R1']));
});

test('ships on the same spot fan out on the map instead of hiding each other (playtest M6)', async ({ page, request }) => {
  await openSeededMatch(page, request, { blueShips: ['Blue 1', 'Blue 2', 'Blue 3', 'Blue 4'], stacked: true });
  await page.getByRole('button', { name: 'Play Map', exact: true }).click();

  const markers = page.locator('.table-map button.map-ship');
  await expect(markers.first()).toBeVisible();
  const boxes = (await markers.evaluateAll((buttons) => buttons.map((button) => {
    const box = button.getBoundingClientRect();
    return { label: button.querySelector('strong')?.textContent ?? '', x: box.x + box.width / 2, y: box.y + box.height / 2 };
  }))).filter((box) => box.label.startsWith('B'));

  expect(boxes).toHaveLength(4);
  for (let a = 0; a < boxes.length; a += 1) {
    for (let b = a + 1; b < boxes.length; b += 1) {
      expect(Math.hypot(boxes[a].x - boxes[b].x, boxes[a].y - boxes[b].y)).toBeGreaterThan(30);
    }
  }
});

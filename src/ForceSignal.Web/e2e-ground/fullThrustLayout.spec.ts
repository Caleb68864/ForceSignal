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

test('a dropdown shows its whole choice (playtest m11)', async ({ page, request }) => {
  await openSeededMatch(page, request, { ready: false });
  await page.locator('details.setup-shipyard summary').click();
  await expect(page.locator('select').first()).toBeVisible();

  const cut = await page.evaluate(() => {
    const canvas = document.createElement('canvas').getContext('2d') as CanvasRenderingContext2D;
    return [...document.querySelectorAll('select')]
      .filter((select) => select.getBoundingClientRect().width > 0)
      .map((select) => {
        const style = getComputedStyle(select);
        canvas.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
        const text = select.selectedOptions[0]?.textContent ?? '';
        // The text, the padding either side and the arrow.
        return { text, room: Math.round(select.getBoundingClientRect().width - canvas.measureText(text).width - 36) };
      })
      .filter((select) => select.room < 0)
      .map((select) => select.text);
  });
  expect(cut).toEqual([]);
});

for (const [width, height] of [[390, 844], [1440, 900]] as const) {
  test(`the map's readouts keep each number on one line at ${width}px (playtest p2)`, async ({ page, request }) => {
    await page.setViewportSize({ width, height });
    await openSeededMatch(page, request);
    await page.getByRole('button', { name: 'Play Map', exact: true }).click();
    const values = page.locator('.map-inspector .ship-readouts strong');
    await expect(values.first()).toBeVisible();

    const wrapped = await values.evaluateAll((cells) => cells
      .filter((cell) => {
        const line = parseFloat(getComputedStyle(cell).lineHeight) || cell.getBoundingClientRect().height;
        return cell.getBoundingClientRect().height > line * 1.5;
      })
      .map((cell) => cell.textContent));
    expect(wrapped).toEqual([]);
    // Nor cut short instead.
    const cut = await values.evaluateAll((cells) => cells.filter((cell) => cell.scrollWidth > cell.clientWidth + 1).map((cell) => cell.textContent));
    expect(cut).toEqual([]);
  });
}

import { expect, test } from '@playwright/test';
import { openSeededMatch } from './fullThrustSeed.ts';

// Drag from a ship's marker to plot its course, for any ship the player owns - not only the one
// focused when the drag began. The roadmap said the map allowed this for the focused ship only;
// checked with a mouse and a finger, it already worked for any, and these guard that it stays so.
test('dragging from an unfocused friendly ship plots that ship', async ({ page, request }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openSeededMatch(page, request);
  await page.getByRole('button', { name: 'Play Map', exact: true }).click();

  const markers = page.locator('button.map-ship.owned');
  await expect(markers).toHaveCount(2);
  const focusedIndex = await markers.evaluateAll((buttons) => buttons.findIndex((button) => button.classList.contains('focused')));
  const other = markers.nth(focusedIndex === 0 ? 1 : 0);
  const name = (await other.getAttribute('title') ?? '').split(' ')[0];
  await other.scrollIntoViewIfNeeded();
  const box = await other.boundingBox();
  if (!box) throw new Error('the marker has no box');
  const startX = box.x + box.width / 2;
  const startY = box.y + box.height / 2;

  await page.mouse.move(startX, startY);
  await page.mouse.down();
  await page.mouse.move(startX + 40, startY + 10, { steps: 4 });
  await page.mouse.move(startX + 80, startY + 20, { steps: 4 });
  await page.mouse.up();

  await expect(page.getByText(new RegExp(`^${name}: .* to course \\d+`))).toBeVisible();
});

test.describe('on a touch phone', () => {
  test.use({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true });

  test('a finger dragged from an unfocused friendly ship plots that ship', async ({ page, request }) => {
    await openSeededMatch(page, request);
    await page.getByRole('button', { name: 'Play Map', exact: true }).click();
    const markers = page.locator('button.map-ship.owned');
    await expect(markers).toHaveCount(2);
    const focusedIndex = await markers.evaluateAll((buttons) => buttons.findIndex((button) => button.classList.contains('focused')));
    const other = markers.nth(focusedIndex === 0 ? 1 : 0);
    const name = (await other.getAttribute('title') ?? '').split(' ')[0];
    await other.scrollIntoViewIfNeeded();
    const box = await other.boundingBox();
    if (!box) throw new Error('the marker has no box');
    const x = box.x + box.width / 2;
    const y = box.y + box.height / 2;
    const cdp = await page.context().newCDPSession(page);
    const touch = (type: string, px: number, py: number) => cdp.send('Input.dispatchTouchEvent', { type, touchPoints: type === 'touchEnd' ? [] : [{ x: px, y: py }] });
    await touch('touchStart', x, y);
    for (let step = 1; step <= 8; step += 1) await touch('touchMove', x + step * 8, y + step * 3);
    await touch('touchEnd', x + 64, y + 24);
    await expect(page.getByText(new RegExp(`^${name}: .* to course \\d+`))).toBeVisible();
  });
});

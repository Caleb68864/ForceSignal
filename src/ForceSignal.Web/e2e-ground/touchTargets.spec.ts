// The first screens a table sees, at phone and tablet size: nothing pushes the page sideways, every
// control is big enough to hit with a thumb, and a link inside a sentence reads as part of it.
import { expect, test, type Page } from '@playwright/test';
import { openEngine } from './openEngine.ts';

/** Controls shorter or narrower than this are hard to hit on a touch screen. */
const smallest = 40;

async function smallTargets(page: Page) {
  return page.evaluate((min) => [...document.querySelectorAll('button, select, input:not([type=hidden]):not([type=checkbox]), a[href]')]
    .filter((element) => {
      const box = element.getBoundingClientRect();
      // An inline link inside running text is sized by the text, as any sentence's is; it is
      // judged by the sentence-link check below instead.
      const inline = element.tagName === 'A' && element.closest('p') !== null;
      return box.width > 0 && box.height > 0 && !inline && (box.height < min || box.width < min);
    })
    .map((element) => `${element.tagName.toLowerCase()} "${(element.getAttribute('aria-label') ?? element.textContent ?? '').trim().slice(0, 40)}" ${Math.round(element.getBoundingClientRect().width)}x${Math.round(element.getBoundingClientRect().height)}`),
  smallest);
}

async function horizontalOverflow(page: Page) {
  return page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
}

for (const [width, height, device] of [[390, 844, 'phone'], [820, 1180, 'tablet']] as const) {
  test.describe(`at ${device} size`, () => {
    test.use({ viewport: { width, height }, hasTouch: true });

    test('the start screen and both ground setup screens are touchable and fit', async ({ page }) => {
      await page.goto('/');
      await expect(page.getByRole('button', { name: 'Create Match' })).toBeVisible();
      expect(await horizontalOverflow(page)).toBe(0);
      expect(await smallTargets(page)).toEqual([]);

      await openEngine(page, 'Dirtside II');
      expect(await horizontalOverflow(page)).toBe(0);
      expect(await smallTargets(page)).toEqual([]);

      await page.getByRole('button', { name: 'Back to Full Thrust' }).click();
      await openEngine(page, 'StarGrunt II');
      expect(await horizontalOverflow(page)).toBe(0);
      expect(await smallTargets(page)).toEqual([]);
    });

    test('a checkbox is a checkbox, not a 40-60 px white square (playtest M4)', async ({ page }) => {
      await openEngine(page, 'Dirtside II');
      page.on('dialog', (dialog) => void dialog.accept());
      await page.getByRole('button', { name: 'Start Game' }).click();
      await expect(page.getByLabel('Add platoon')).toBeVisible();
      const boxes = await page.locator('input[type=checkbox]').evaluateAll((elements) => elements
        .map((element) => element.getBoundingClientRect())
        .filter((box) => box.width > 0)
        .map((box) => [Math.round(box.width), Math.round(box.height)]));
      expect(boxes.length).toBeGreaterThan(0);
      for (const [boxWidth, boxHeight] of boxes) {
        expect(boxWidth).toBeLessThanOrEqual(28);
        expect(boxHeight).toBeLessThanOrEqual(28);
        expect(boxWidth).toBeGreaterThanOrEqual(20);
      }
    });

    test('a link inside a sentence sits on the sentence\'s line rather than over the next', async ({ page }) => {
      await page.goto('/');
      const credit = page.locator('.icon-credit');
      const lineHeight = await credit.evaluate((element) => parseFloat(getComputedStyle(element).lineHeight) || 16);
      for (const link of await credit.locator('a').all()) {
        const box = await link.boundingBox();
        expect(box?.height ?? 0).toBeLessThanOrEqual(lineHeight * 1.5);
      }
    });
  });
}

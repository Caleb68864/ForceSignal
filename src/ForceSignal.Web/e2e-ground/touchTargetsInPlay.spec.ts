// Touch targets in play, on touch screens: 44px, the size a thumb can hit (playtest m17 found most
// buttons 34-40px on tablets and phones held sideways, the arc toggles 34px and the banner's close
// 25x19).
import { expect, test, type Page } from '@playwright/test';
import { openSeededMatch } from './fullThrustSeed.ts';

const smallest = 44;

async function smallTargets(page: Page) {
  return page.evaluate((min) => [...document.querySelectorAll('button, select, input:not([type=hidden]):not([type=checkbox]):not([type=radio]):not([type=color]), summary')]
    .filter((element) => {
      const box = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      return box.width > 0 && box.height > 0 && style.visibility !== 'hidden' && (box.height < min - 0.5 || box.width < min - 0.5);
    })
    .map((element) => `${element.tagName.toLowerCase()} "${(element.getAttribute('aria-label') ?? element.textContent ?? '').trim().slice(0, 30)}" ${Math.round(element.getBoundingClientRect().width)}x${Math.round(element.getBoundingClientRect().height)}`),
  smallest);
}

for (const [width, height, device] of [[390, 844, 'phone'], [844, 390, 'phone held sideways'], [820, 1180, 'tablet']] as const) {
  test.describe(`on a ${device}`, () => {
    test.use({ viewport: { width, height }, hasTouch: true });

    test('a Full Thrust match in order entry has nothing too small to hit', async ({ page, request }) => {
      await openSeededMatch(page, request);
      await expect(page.locator('article.ship-card').first()).toBeVisible();
      const found = new Set(await smallTargets(page));
      await page.getByRole('button', { name: 'Fire', exact: true }).first().click();
      for (const target of await smallTargets(page)) found.add(target);
      await page.getByRole('button', { name: 'Damage', exact: true }).first().click();
      for (const target of await smallTargets(page)) found.add(target);
      await page.getByRole('button', { name: 'Play Map', exact: true }).click();
      for (const target of await smallTargets(page)) found.add(target);
      expect([...found]).toEqual([]);
    });
  });
}

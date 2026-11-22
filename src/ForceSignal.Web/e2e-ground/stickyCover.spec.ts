// The sticky "What to do next" line must never sit over a control that is brought into view -
// by keyboard focus, or by a test's click (CI: "<section class=next-step> intercepts pointer
// events" on Add Platoon at phone size, 30 retries running).
import { expect, test } from '@playwright/test';
import { openEngine } from './openEngine.ts';

test.use({ viewport: { width: 390, height: 844 }, hasTouch: true });

test('a control focused under the sticky line is scrolled clear of it', async ({ page }) => {
  await openEngine(page, 'Dirtside II');
  page.on('dialog', (dialog) => void dialog.accept());
  await page.getByRole('button', { name: 'Start Game' }).click();
  const add = page.getByLabel('Add platoon').getByRole('button', { name: 'Add Platoon' });
  await expect(add).toBeVisible();

  // Leave the button wholly on screen but under the line, as a scroll can, then focus it. On a phone
  // the line is fixed to the bottom edge, so the button is parked just above that edge.
  const overlap = await add.evaluate(async (button) => {
    const bar = document.querySelector('.next-step') as HTMLElement;
    const barBox = bar.getBoundingClientRect();
    const buttonBox = button.getBoundingClientRect();
    const target = barBox.top > window.innerHeight / 2
      ? buttonBox.top + window.scrollY - (window.innerHeight - buttonBox.height - 10)
      : buttonBox.top + window.scrollY - 10;
    window.scrollTo(0, target);
    await new Promise((resolve) => requestAnimationFrame(() => resolve(null)));
    button.focus();
    await new Promise((resolve) => requestAnimationFrame(() => resolve(null)));
    const after = button.getBoundingClientRect();
    const barAfter = bar.getBoundingClientRect();
    return Math.max(0, Math.min(after.bottom, barAfter.bottom) - Math.max(after.top, barAfter.top));
  });

  expect(overlap).toBe(0);
  await add.click({ timeout: 5_000, trial: true });
});

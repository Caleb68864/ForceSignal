import { expect, type Page } from '@playwright/test';

/**
 * Opens the start screen and switches to a ground engine.
 *
 * The engine buttons appear only once `/api/features` has answered. A read that fails - a dev server
 * still optimising its dependencies on the first load, say - hides them and offers Check Again, which
 * is the app's own remedy, so it is used here rather than failing on a transient the screen already
 * handles. A read that keeps failing still fails the test, on the expectation below.
 */
export async function openEngine(page: Page, engine: 'Dirtside II' | 'StarGrunt II') {
  await page.goto('/');
  const button = page.getByRole('button', { name: engine });
  const checkAgain = page.getByRole('button', { name: 'Check Again' });
  await expect(button.or(checkAgain)).toBeVisible({ timeout: 30_000 });
  if (await checkAgain.isVisible()) {
    await checkAgain.click();
  }

  await expect(button).toBeVisible({ timeout: 30_000 });
  await button.click();
}

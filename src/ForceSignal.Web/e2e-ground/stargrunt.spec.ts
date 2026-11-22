// A StarGrunt game played through the real screen against a real API: create it with the players'
// Leadership Values, put a squad a side on the table, and take one activation through to its end.
import { expect, test } from '@playwright/test';
import { openEngine } from './openEngine.ts';

test('a StarGrunt game is created, manned and plays an activation', async ({ page }) => {
  const problems: string[] = [];
  page.on('pageerror', (error) => problems.push(error.message));

  await openEngine(page, 'StarGrunt II');

  const setup = page.getByLabel('StarGrunt setup');
  await setup.getByLabel('Lowest Leadership Value').fill('1');
  await setup.getByLabel('Highest Leadership Value').fill('3');
  await setup.getByRole('button', { name: 'Start Game' }).click();
  await expect(page.getByText(/Started .*Add a squad a side\./)).toBeVisible();

  const add = page.getByLabel('Add a unit');
  for (const [name, side] of [['Alpha Squad', 'blue'], ['Bravo Squad', 'red']]) {
    await add.getByLabel('Name').fill(name);
    await add.getByLabel('Side').fill(side);
    await add.getByLabel('Quality').selectOption({ label: 'D8' });
    await add.getByLabel('Leadership').selectOption({ label: '2' });
    await add.getByLabel('Figures').fill('4');
    await add.getByLabel('Armour').selectOption({ label: 'D8' });
    await add.getByLabel('Impact').selectOption({ label: 'D8' });
    await add.getByRole('button', { name: 'Add Squad' }).click();
    await expect(page.getByText(`${name} joined ${side}.`)).toBeVisible();
  }

  await page.getByRole('button', { name: 'Begin Turn' }).click();
  await expect(page.getByText('Turn opened.')).toBeVisible();
  // The side with fewer units has the choice; with one a side either may be asked, so take
  // whichever button the screen offers.
  await page.getByRole('button', { name: 'blue goes first' }).click();
  await page.getByRole('button', { name: /^Activate (Alpha|Bravo) Squad$/ }).and(page.locator(':enabled')).first().click();
  const activation = page.getByLabel(/ activation$/);
  await expect(activation.getByText(/is activated/)).toBeVisible();

  await activation.getByRole('button', { name: 'Move', exact: true }).click();
  await expect(page.getByText(/: Move\.$/)).toBeVisible();
  await activation.getByRole('button', { name: 'End Activation' }).click();
  await expect(page.getByText(/ is done\.$/)).toBeVisible();

  expect(problems).toEqual([]);
});

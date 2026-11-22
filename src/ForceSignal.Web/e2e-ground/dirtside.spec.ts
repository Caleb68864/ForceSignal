// A Dirtside game played through the real screen against a real API: create it with the players'
// own dice, put a platoon a side on the table, and take one activation through to its end.
import { expect, test } from '@playwright/test';

test('a Dirtside game is created, armed and plays an activation', async ({ page }) => {
  const problems: string[] = [];
  page.on('pageerror', (error) => problems.push(error.message));

  await page.goto('/');
  await page.getByRole('button', { name: 'Dirtside II' }).click();

  // The players' own dice, and their own firer walk, typed on the create screen.
  await page.getByLabel('Basic gunnery').selectOption('D6');
  await page.getByLabel('Signature 3').selectOption('D6');
  await page.getByLabel('Firer walk: rungs up at close').fill('1');
  await page.getByLabel('Rungs down at long').fill('1');
  await page.getByLabel('Rungs down moving over half').fill('1');
  await page.getByRole('button', { name: 'Start Game' }).click();
  await expect(page.getByText('Firer walk: 1 up at close, 1 down at long, 1 down for moving over half')).toBeVisible();

  const addPlatoon = page.getByLabel('Add platoon');
  for (const [name, side] of [['Anvil', 'blue'], ['Hammer', 'red']]) {
    await addPlatoon.getByLabel('Name', { exact: true }).fill(name);
    await addPlatoon.getByLabel('Side').fill(side);
    await addPlatoon.getByRole('spinbutton', { name: 'Signature', exact: true }).fill('3');
    await addPlatoon.getByRole('spinbutton', { name: 'Armour', exact: true }).fill('3');
    await addPlatoon.getByRole('spinbutton', { name: 'Movement', exact: true }).fill('12');
    await addPlatoon.getByRole('spinbutton', { name: 'Chits', exact: true }).fill('3');
    await addPlatoon.getByRole('button', { name: 'Add Platoon' }).click();
    await expect(page.getByLabel('Platoons').getByText(name, { exact: false }).first()).toBeVisible();
  }

  await page.getByRole('button', { name: 'Begin Turn' }).click();
  await page.getByRole('button', { name: 'blue Goes First' }).click();
  await page.getByRole('button', { name: 'Activate Anvil' }).click();
  const activation = page.getByLabel('Activation');
  await expect(activation.getByText('Anvil is activated')).toBeVisible();

  await activation.getByRole('button', { name: /^Move / }).first().click();
  await expect(activation.getByText('Every element has said what it is doing.')).toBeVisible();
  await activation.getByRole('button', { name: 'End Activation' }).click();
  await expect(page.getByText('Activation ended.')).toBeVisible();

  expect(problems).toEqual([]);
});

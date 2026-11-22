// One Dirtside table on two devices: the game started on one is opened on the other from its game
// code, and a change made on either reaches the other without a reload.
import { expect, test } from '@playwright/test';
import { openEngine } from './openEngine.ts';

test('a Dirtside game is played from two devices', async ({ browser }) => {
  const first = await (await browser.newContext()).newPage();
  const second = await (await browser.newContext()).newPage();
  const problems: string[] = [];
  for (const page of [first, second]) {
    page.on('pageerror', (error) => problems.push(error.message));
  }

  await openEngine(first, 'Dirtside II');
  await first.getByLabel('Firer walk: rungs up at close').fill('1');
  await first.getByLabel('Rungs down at long').fill('1');
  await first.getByLabel('Rungs down moving over half').fill('1');
  await first.getByRole('button', { name: 'Start Game' }).click();
  await expect(first.getByText('Link live')).toBeVisible();

  await first.getByRole('button', { name: 'Show Game Code' }).click();
  const code = (await first.locator('.game-code').innerText()).trim();

  await openEngine(second, 'Dirtside II');
  await second.getByLabel('Game code').fill(code);
  await second.getByRole('button', { name: 'Join Game' }).click();
  await expect(second.getByText(/^Joined /)).toBeVisible();
  await expect(second.getByText('Link live')).toBeVisible();

  // A platoon added on the first device appears on the second.
  const add = first.getByLabel('Add platoon');
  await add.getByLabel('Name', { exact: true }).fill('Anvil');
  await add.getByRole('spinbutton', { name: 'Signature', exact: true }).fill('3');
  await add.getByRole('spinbutton', { name: 'Armour', exact: true }).fill('3');
  await add.getByRole('spinbutton', { name: 'Movement', exact: true }).fill('12');
  await add.getByRole('button', { name: 'Add Platoon' }).click();
  await expect(second.getByLabel('Platoons').getByText('Anvil').first()).toBeVisible();

  // And one added on the second device appears on the first.
  const addThere = second.getByLabel('Add platoon');
  await addThere.getByLabel('Name', { exact: true }).fill('Hammer');
  await addThere.getByLabel('Side').fill('red');
  await addThere.getByRole('spinbutton', { name: 'Signature', exact: true }).fill('3');
  await addThere.getByRole('spinbutton', { name: 'Armour', exact: true }).fill('3');
  await addThere.getByRole('spinbutton', { name: 'Movement', exact: true }).fill('12');
  await addThere.getByRole('button', { name: 'Add Platoon' }).click();
  await expect(first.getByLabel('Platoons').getByText('Hammer').first()).toBeVisible();

  // Then the turn begun on the first shows on the second.
  await first.getByRole('button', { name: 'Begin Turn', exact: true }).click();
  await expect(second.getByText(/^Turn 1 /)).toBeVisible();

  // A code with one character wrong opens nothing.
  const third = await (await browser.newContext()).newPage();
  await openEngine(third, 'Dirtside II');
  const wrong = code.slice(0, -1) + (code.endsWith('a') ? 'b' : 'a');
  await third.getByLabel('Game code').fill(wrong);
  await third.getByRole('button', { name: 'Join Game' }).click();
  await expect(third.getByText('That game code does not open a game on this server.')).toBeVisible();

  expect(problems).toEqual([]);
});

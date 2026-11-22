// One StarGrunt table on two devices: the game started on one is opened on the other from its game
// code, and a change made on either reaches the other without a reload.
import { expect, test, type Page } from '@playwright/test';
import { openEngine } from './openEngine.ts';

async function addSquad(page: Page, name: string, side: string) {
  const add = page.getByLabel('Add a unit');
  await add.getByLabel('Name').fill(name);
  await add.getByLabel('Side').fill(side);
  await add.getByLabel('Quality').selectOption({ label: 'D8' });
  await add.getByLabel('Leadership').selectOption({ label: '2' });
  await add.getByLabel('Figures').fill('4');
  await add.getByLabel('Armour').selectOption({ label: 'D8' });
  await add.getByLabel('Impact').selectOption({ label: 'D8' });
  await add.getByRole('button', { name: 'Add Squad' }).click();
}

test('a StarGrunt game is played from two devices', async ({ browser }) => {
  const first = await (await browser.newContext()).newPage();
  const second = await (await browser.newContext()).newPage();
  const problems: string[] = [];
  for (const page of [first, second]) {
    page.on('pageerror', (error) => problems.push(error.message));
  }

  await openEngine(first, 'StarGrunt II');
  const setup = first.getByLabel('StarGrunt setup');
  await setup.getByLabel('Lowest Leadership Value').fill('1');
  await setup.getByLabel('Highest Leadership Value').fill('3');
  await setup.getByRole('button', { name: 'Start Game' }).click();
  await expect(first.getByText('Link live')).toBeVisible();

  await first.getByRole('button', { name: 'Show Game Code' }).click();
  const code = (await first.locator('.game-code').innerText()).trim();

  await openEngine(second, 'StarGrunt II');
  await second.getByLabel('Game code').fill(code);
  await second.getByRole('button', { name: 'Join Game' }).click();
  await expect(second.getByText(/^Joined /)).toBeVisible();
  await expect(second.getByText('Link live')).toBeVisible();

  // A squad added on each device appears on the other.
  await addSquad(first, 'Alpha Squad', 'blue');
  await expect(second.getByRole('button', { name: 'Activate Alpha Squad' })).toBeVisible();
  await addSquad(second, 'Bravo Squad', 'red');
  await expect(first.getByRole('button', { name: 'Activate Bravo Squad' })).toBeVisible();

  // The turn begun on one shows on the other, and so does the activation it then opens.
  await second.getByRole('button', { name: 'Begin Turn' }).click();
  await expect(first.getByText('Turn 1', { exact: false }).first()).toBeVisible();
  await first.getByRole('button', { name: 'blue goes first' }).click();
  await first.getByRole('button', { name: 'Activate Alpha Squad' }).click();
  await expect(second.getByText('Alpha Squad is activated')).toBeVisible();

  expect(problems).toEqual([]);
});

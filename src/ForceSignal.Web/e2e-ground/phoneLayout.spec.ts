// At phone size the button the table needs next is near the top, not under everything else
// (playtest M10).
import { expect, test, type Page } from '@playwright/test';
import { openSeededMatch } from './fullThrustSeed.ts';
import { openEngine } from './openEngine.ts';

test.use({ viewport: { width: 390, height: 844 }, hasTouch: true });

async function top(page: Page, label: string | RegExp) {
  const box = await page.getByLabel(label).first().boundingBox();
  return box?.y ?? Number.POSITIVE_INFINITY;
}

test('StarGrunt puts the activated unit above the roster', async ({ page }) => {
  await openEngine(page, 'StarGrunt II');
  const setup = page.getByLabel('StarGrunt setup');
  await setup.getByLabel('Lowest Leadership Value').fill('1');
  await setup.getByLabel('Highest Leadership Value').fill('3');
  await setup.getByRole('button', { name: 'Start Game' }).click();
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
  await page.getByRole('button', { name: 'blue goes first' }).click();
  await page.getByRole('button', { name: 'Activate Alpha Squad' }).click();
  await expect(page.getByLabel('Alpha Squad activation')).toBeVisible();

  expect(await top(page, 'Alpha Squad activation')).toBeLessThan(await top(page, 'Activate Bravo Squad'));
  expect(await top(page, 'Alpha Squad fire')).toBeLessThan(await top(page, 'Activate Bravo Squad'));
});

test('Dirtside puts the activated platoon above the platoon list', async ({ page }) => {
  await openEngine(page, 'Dirtside II');
  page.on('dialog', (dialog) => void dialog.accept());
  await page.getByRole('button', { name: 'Start Game' }).click();
  const add = page.getByLabel('Add platoon');
  for (const [name, side] of [['Anvil', 'blue'], ['Hammer', 'red']]) {
    await add.getByLabel('Name', { exact: true }).fill(name);
    await add.getByLabel('Side').fill(side);
    await add.getByRole('spinbutton', { name: 'Signature', exact: true }).fill('3');
    await add.getByRole('spinbutton', { name: 'Armour', exact: true }).fill('3');
    await add.getByRole('spinbutton', { name: 'Movement', exact: true }).fill('12');
    await add.getByRole('button', { name: 'Add Platoon' }).click();
    await expect(page.getByLabel('Platoons').getByText(name).first()).toBeVisible();
  }
  await page.getByRole('button', { name: 'Begin Turn' }).click();
  await page.getByRole('button', { name: 'blue Goes First' }).click();
  await page.getByRole('button', { name: 'Activate Anvil' }).click();
  await expect(page.getByLabel('Activation', { exact: true })).toBeVisible();

  expect(await top(page, 'Activation')).toBeLessThan(await top(page, 'Platoons'));
});

test('a Full Thrust ship card lays its readouts out three across', async ({ page, request }) => {
  await openSeededMatch(page, request);
  const readouts = page.locator('article.ship-card .ship-readouts').first().locator(':scope > div');
  await expect(readouts.first()).toBeVisible();
  const tops = await readouts.evaluateAll((cells) => cells.slice(0, 3).map((cell) => Math.round(cell.getBoundingClientRect().top)));
  expect(new Set(tops).size).toBe(1);
});

test('Full Thrust folds the shipyard away once there is a fleet, so Ready is not 6,000px down', async ({ page, request }) => {
  await openSeededMatch(page, request, { ready: false });
  const ready = page.getByRole('button', { name: 'Ready', exact: true }).first();
  await expect(ready).toBeVisible();

  const shipyard = page.locator('details.setup-shipyard');
  await expect(shipyard).not.toHaveAttribute('open', '');
  await expect(page.getByText(/2 ships in your fleet/)).toBeVisible();
  const pageHeight = await page.evaluate(() => document.documentElement.scrollHeight);
  expect(pageHeight).toBeLessThan(5000);

  // Nor does a ship card carry the helm, the order plot and Lock in setup, where they can only be refused.
  await expect(page.locator('article.ship-card').getByRole('button', { name: 'Lock', exact: true })).toHaveCount(0);

  await shipyard.locator('summary').click();
  await expect(page.getByText('Add ship', { exact: true }).first()).toBeVisible();
});

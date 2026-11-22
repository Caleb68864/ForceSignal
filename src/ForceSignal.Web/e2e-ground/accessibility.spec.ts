// axe-core over every main screen of all three games, at phone, tablet and desktop size. The
// playtest found no violations on master; this keeps it that way as the screens change.
import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { openSeededMatch } from './fullThrustSeed.ts';
import { openEngine } from './openEngine.ts';

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');

type Violation = { id: string; impact: string | null; nodes: { target: string[] }[] };

async function violations(page: Page): Promise<string[]> {
  await page.addScriptTag({ path: axePath });
  const found = await page.evaluate(async () => {
    const result = await (window as unknown as { axe: { run: (context: Document, options: object) => Promise<{ violations: Violation[] }> } })
      .axe.run(document, { resultTypes: ['violations'] });
    return result.violations;
  });
  return found.map((violation) => `${violation.id} (${violation.impact}): ${violation.nodes.slice(0, 3).map((node) => node.target.join(' ')).join(' | ')}`);
}

const sizes = [[390, 844, 'phone'], [820, 1180, 'tablet'], [1440, 900, 'desktop']] as const;

for (const [width, height, device] of sizes) {
  test.describe(`at ${device} size`, () => {
    test.use({ viewport: { width, height } });

    test('the start screen', async ({ page }) => {
      await page.goto('/');
      await expect(page.getByRole('button', { name: 'Create Match' })).toBeVisible();
      expect(await violations(page)).toEqual([]);
    });

    test('a Full Thrust match: ships, map and log', async ({ page, request }) => {
      await openSeededMatch(page, request);
      await expect(page.locator('article.ship-card').first()).toBeVisible();
      expect(await violations(page)).toEqual([]);
      await page.getByRole('button', { name: 'Play Map', exact: true }).click();
      await expect(page.locator('.table-map')).toBeVisible();
      expect(await violations(page)).toEqual([]);
      await page.getByRole('button', { name: 'Log', exact: true }).click();
      expect(await violations(page)).toEqual([]);
    });

    test('a Dirtside game mid-activation', async ({ page }) => {
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
      expect(await violations(page)).toEqual([]);
    });

    test('a StarGrunt game mid-activation', async ({ page }) => {
      await openEngine(page, 'StarGrunt II');
      const setup = page.getByLabel('StarGrunt setup');
      await setup.getByLabel('Lowest Leadership Value').fill('1');
      await setup.getByLabel('Highest Leadership Value').fill('3');
      await setup.getByRole('button', { name: 'Start Game' }).click();
      const unit = page.getByLabel('Add a unit');
      for (const [name, side] of [['Alpha Squad', 'blue'], ['Bravo Squad', 'red']]) {
        await unit.getByLabel('Name').fill(name);
        await unit.getByLabel('Side').fill(side);
        await unit.getByLabel('Quality').selectOption({ label: 'D8' });
        await unit.getByLabel('Leadership').selectOption({ label: '2' });
        await unit.getByLabel('Figures').fill('4');
        await unit.getByLabel('Armour').selectOption({ label: 'D8' });
        await unit.getByLabel('Impact').selectOption({ label: 'D8' });
        await unit.getByRole('button', { name: 'Add Squad' }).click();
        await expect(page.getByText(`${name} joined ${side}.`)).toBeVisible();
      }
      await page.getByRole('button', { name: 'Begin Turn' }).click();
      await page.getByRole('button', { name: 'blue goes first' }).click();
      await page.getByRole('button', { name: 'Activate Alpha Squad' }).click();
      await expect(page.getByLabel('Alpha Squad activation')).toBeVisible();
      expect(await violations(page)).toEqual([]);
    });
  });
}

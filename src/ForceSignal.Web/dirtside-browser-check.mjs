// Drives the Dirtside screen in a real browser: clicks the buttons a player would click and
// reports what the page says back. Run against a dev server with the Dirtside flag on.
//
// Not part of the test suite - a throwaway harness, kept only long enough to prove the screen
// works, because everything it exercises is already covered at the API level.
import { chromium } from 'playwright';

const WEB = process.env.WEB ?? 'http://localhost:5191';

const browser = await chromium.launch();
const page = await browser.newPage();

const problems = [];
page.on('console', (message) => {
  if (message.type() === 'error') {
    problems.push(`console: ${message.text()}`);
  }
});
page.on('pageerror', (error) => problems.push(`pageerror: ${error.message}`));

const log = (label, value) => console.log(`${label.padEnd(28)} ${value}`);

async function panelText() {
  return (await page.locator('section[aria-label="Dirtside"]').innerText()).replace(/\s+/g, ' ').toLowerCase();
}

await page.goto(WEB, { waitUntil: 'networkidle' });

// Into Dirtside.
await page.getByRole('button', { name: 'Dirtside II' }).click();
await page.getByRole('button', { name: 'Start Game' }).click();
await page.waitForSelector('text=Add a platoon');
log('started', await page.locator('section[aria-label="Dirtside"] h2').innerText());

// Two platoons, off the "record card" fields.
async function addPlatoon(name, side) {
  const panel = page.locator('div[aria-label="Add platoon"]');
  await panel.getByLabel('Name', { exact: true }).fill(name);
  await panel.getByLabel('Side', { exact: true }).fill(side);
  await panel.getByRole('button', { name: 'Add Platoon' }).click();
  await page.waitForTimeout(250);
}

await addPlatoon('Alpha Troop', 'blue');
await addPlatoon('Bravo Troop', 'red');
log('platoons on table', (await page.locator('div[aria-label="Platoons"] button').count()) + ' activate buttons');

// Open the turn and take the first activation.
await page.getByRole('button', { name: 'Begin Turn' }).click();
await page.waitForTimeout(200);
await page.getByRole('button', { name: 'blue Goes First' }).click();
await page.waitForTimeout(200);

await page.locator('div[aria-label="Platoons"] .table-fields', { hasText: 'Alpha Troop' })
  .getByRole('button', { name: 'Activate' }).click();
await page.waitForTimeout(300);
log('activation open', (await panelText()).includes('is activated') ? 'yes' : 'NO');

// End Activation must be refused, with the server's own words on the page.
const endButton = page.getByRole('button', { name: 'End Activation' });
log('end disabled at first', (await endButton.isDisabled()) ? 'yes' : 'NO');
const why = await page.locator('section[aria-label="Dirtside"] .constraint-line')
  .filter({ hasText: /not said what to do/i }).first().innerText().catch(() => '(none shown)');
log('reason shown', why.replace(/\s+/g, ' '));

// Move the first element, then fire that same one. The bug this catches: the fire panel used to
// fall through to "the first element that has not chosen", so moving vehicle 1 and clicking Fire
// shot with vehicle 2.
const first = page.locator('div[aria-label="Activation"] .table-fields').first();
const firstName = (await first.locator('.label').innerText()).split(' · ')[0].trim();
await first.getByRole('button', { name: 'Aim This One' }).click();
await first.getByRole('button', { name: 'Move' }).click();
await page.waitForTimeout(250);
await page.getByRole('button', { name: 'Fire', exact: true }).click();
await page.waitForTimeout(400);
log('aimed element', firstName);

const logEntries = await page.locator('div[aria-label="Battle log"] li').allInnerTexts();
log('log lines', String(logEntries.length));
logEntries.forEach((entry) => console.log(`    ${entry}`));

// The same element cannot fire twice: the button is now disabled rather than refusing on click.
const fireAgain = page.getByRole('button', { name: 'Fire', exact: true });
const movedAndShot = logEntries.every((entry) => entry.toLowerCase().includes(firstName.toLowerCase()));
log('same vehicle moved+fired', movedAndShot ? 'yes' : 'NO - it fired a different one');
log('move now disabled', (await first.getByRole('button', { name: 'Move' }).isDisabled()) ? 'yes' : 'NO');
log('stand down disabled', (await first.getByRole('button', { name: 'Stand Down' }).isDisabled()) ? 'yes' : 'NO');

// Stand the other element down, and the activation should close.
const second = page.locator('div[aria-label="Activation"] .table-fields').nth(1);
await second.getByRole('button', { name: 'Stand Down' }).click();
await page.waitForTimeout(300);
log('end enabled after all chose', (await endButton.isDisabled()) ? 'NO' : 'yes');

await endButton.click();
await page.waitForTimeout(300);
log('activation closed', (await panelText()).includes('is activated') ? 'NO' : 'yes');

await page.screenshot({ path: 'dirtside-browser-check.png', fullPage: true });
log('console errors', problems.length === 0 ? 'none' : problems.join(' | '));

await browser.close();
process.exit(problems.length === 0 ? 0 : 1);

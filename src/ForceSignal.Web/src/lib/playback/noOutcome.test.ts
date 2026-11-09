/**
 * Playback may read an outcome; it may not compute one.
 *
 * The rule itself is in eslint.config.js and `npm test` already lints. This file is what keeps the
 * rule honest: an AST selector with a typo matches nothing and reports nothing, which looks exactly
 * like clean code. So each probe below is a line the rule exists to refuse, linted as if it lived in
 * the playback folder, and each has to be refused. A line that only reads a result has to pass, or
 * the rule would be blocking the very thing playback is for.
 */

import { ESLint } from 'eslint';
import { describe, expect, it } from 'vitest';

const eslint = new ESLint();

async function messagesFor(code: string, filePath = 'src/lib/playback/probe.ts'): Promise<string[]> {
  const [result] = await eslint.lintText(code, { filePath });
  return result.messages.map((message) => `${message.ruleId}: ${message.message}`);
}

const refused: Record<string, string> = {
  'adds up damage': 'export const total = (r: { armorDamageApplied: number; hullDamageApplied: number }) => r.armorDamageApplied + r.hullDamageApplied;',
  'works out what screens would have stopped': 'export const raw = (r: { damage: number; screenReduction: number }) => r.damage + r.screenReduction;',
  'takes damage off a hull': 'export function hit(ship: { hullDamage: number }, n: number) { ship.hullDamage += n; }',
  'counts hits among the dice': 'export const hits = (r: { diceRolls: number[] }) => r.diceRolls.filter((d) => d >= 5).length;',
  'compares a die to a target': 'export const hit = (r: { diceRolls: number[]; toHitNumber: number }) => r.diceRolls[0] >= 5;',
  'rolls its own die': 'export const roll = () => Math.ceil(Math.random() * 6);',
  'reaches for the rules module': "import { firingTargetOptions } from '../rules.ts';\nexport const f = firingTargetOptions;",
};

describe('the no-outcome rule on playback code', () => {
  for (const [name, code] of Object.entries(refused)) {
    it(`refuses code that ${name}`, async () => {
      const messages = await messagesFor(code);
      expect(messages.some((message) => /no-restricted-(syntax|imports)/.test(message)), messages.join('\n')).toBe(true);
    });
  }

  it('lets code read a result and compare it, which is all playback needs', async () => {
    const messages = await messagesFor([
      'export const shimmer = (r: { screenReduction: number }) => r.screenReduction > 0;',
      "export const label = (r: { damage: number; diceRolls: number[] }) => `${r.damage} (${r.diceRolls.join(', ')})`;",
    ].join('\n'));
    expect(messages).toEqual([]);
  });

  it('covers the Pixi layer too', async () => {
    const messages = await messagesFor('export const total = (r: { damage: number }) => r.damage * 2;', 'src/components/map/pixi/probe.ts');
    expect(messages.some((message) => message.startsWith('no-restricted-syntax'))).toBe(true);
  });

  it('leaves the rest of the app alone, where the rule does not belong', async () => {
    const messages = await messagesFor('export const total = (r: { damage: number }) => r.damage * 2;', 'src/lib/format.ts');
    expect(messages).toEqual([]);
  });
});

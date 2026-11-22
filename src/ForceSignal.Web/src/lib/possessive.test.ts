import { describe, expect, it } from 'vitest';
import { possessive } from './possessive.ts';
import { groundNextStep } from './groundNextStep.ts';

describe('a name in the possessive (playtest p1)', () => {
  it('reads as English for a name ending in s', () => {
    expect(possessive('Anvil')).toBe("Anvil's");
    expect(possessive('Red Lancers')).toBe("Red Lancers'");
    expect(groundNextStep({ turnNumber: 1, phase: 'Activating', sides: ['a', 'b'], unitCount: 2, activatingName: 'Red Lancers' }, 'unit').you)
      .toBe("Finish Red Lancers' activation, then End Activation.");
  });
});

import { describe, expect, it } from 'vitest';
import { groundNextStep, phaseWords } from './groundNextStep.ts';

const base = { turnNumber: 1, sides: ['blue', 'red'], unitCount: 2 };

describe('where a ground turn has got to', () => {
  it('names the phase in words, and shows a name it does not know as sent', () => {
    expect(phaseWords('ChoosingFirstActivator')).toBe('Choosing who goes first');
    expect(phaseWords('SomethingNew')).toBe('SomethingNew');
  });

  it('asks for a unit a side before the first turn', () => {
    const step = groundNextStep({ ...base, turnNumber: 0, phase: 'NotStarted', sides: ['blue'], unitCount: 1 }, 'platoon');
    expect(step.heading).toBe('Setting up');
    expect(step.you).toBe('Put a platoon on the table for each side, then Begin Turn.');
    expect(groundNextStep({ ...base, turnNumber: 0, phase: 'NotStarted' }, 'platoon').you).toContain('Begin Turn');
  });

  it('says who chooses who goes first when the game knows', () => {
    expect(groundNextStep({ ...base, phase: 'ChoosingFirstActivator', firstActivationChooser: 'red' }, 'unit').you)
      .toBe('red chooses who goes first.');
    expect(groundNextStep({ ...base, phase: 'ChoosingFirstActivator' }, 'platoon').you)
      .toBe('Choose which side goes first.');
  });

  it('names whose go it is, and the activation still open', () => {
    expect(groundNextStep({ ...base, phase: 'Activating', activeSide: 'blue' }, 'platoon').you)
      .toBe('blue activates a platoon, or passes.');
    expect(groundNextStep({ ...base, phase: 'Activating', activeSide: 'blue', activatingName: 'Anvil' }, 'platoon').you)
      .toBe("Finish Anvil's activation, then End Activation.");
  });

  it('points at the next turn once this one is over', () => {
    const step = groundNextStep({ ...base, turnNumber: 3, phase: 'TurnEnded' }, 'unit');
    expect(step.heading).toBe('Turn 3 · Turn over');
    expect(step.you).toBe('Begin Turn to start turn 4.');
  });
});

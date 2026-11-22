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

describe('the one button to press (playtest M9)', () => {
  const unit = (side: string, canActivate: boolean, hasActivated: boolean) => ({ side, canActivate, hasActivated });

  it('points at End Turn once every unit has gone, instead of "blue to act"', () => {
    const step = groundNextStep({ ...base, phase: 'Activating', activeSide: 'blue', units: [unit('blue', false, true), unit('red', false, true)] }, 'unit');
    expect(step.primary).toBe('end-turn');
    expect(step.you).toBe('Every unit has gone. End Turn.');
  });

  it('tells a side with nothing it can activate to pass', () => {
    const step = groundNextStep({ ...base, phase: 'Activating', activeSide: 'blue', units: [unit('blue', false, true), unit('red', false, false)] }, 'platoon');
    expect(step.primary).toBe('pass');
    expect(step.side).toBe('blue');
    expect(step.you).toBe('blue has nothing it can activate now: press blue passes.');
  });

  it('highlights Begin Turn, the first-activator choice, an activation and End Activation in their phases', () => {
    expect(groundNextStep({ ...base, turnNumber: 0, phase: 'NotStarted' }, 'unit').primary).toBe('begin-turn');
    expect(groundNextStep({ ...base, phase: 'ChoosingFirstActivator' }, 'unit').primary).toBe('choose-first');
    expect(groundNextStep({ ...base, phase: 'Activating', activeSide: 'blue', units: [unit('blue', true, false)] }, 'unit').primary).toBe('activate');
    expect(groundNextStep({ ...base, phase: 'Activating', activatingName: 'Anvil' }, 'unit').primary).toBe('end-activation');
    expect(groundNextStep({ ...base, phase: 'TurnEnded' }, 'unit').primary).toBe('begin-turn');
  });
});

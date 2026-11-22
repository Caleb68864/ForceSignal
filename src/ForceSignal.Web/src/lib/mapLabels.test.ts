import { describe, expect, it } from 'vitest';
import { mapLabels } from './mapLabels.ts';

const named = (...names: string[]) => names.map((name, index) => ({ id: `s${index}`, name }));
const labelsOf = (...names: string[]) => [...mapLabels(named(...names)).values()];

describe('map labels (playtest M6)', () => {
  it('tells numbered sister ships apart instead of calling them all BLU', () => {
    expect(labelsOf('Blue 1', 'Blue 2', 'Blue 3', 'Blue 8')).toEqual(['B1', 'B2', 'B3', 'B8']);
  });

  it('uses initials when they are unique, and more letters when they are not', () => {
    expect(labelsOf('Red Lancer', 'Mark')).toEqual(['RL', 'M']);
    expect(labelsOf('Valiant', 'Vigilant')).toEqual(['VAL', 'VIG']);
  });

  it('numbers two ships that share a name outright', () => {
    const labels = labelsOf('Hawk', 'Hawk');
    expect(new Set(labels).size).toBe(2);
  });

  it('gives every contact on a busy map a different label', () => {
    const names = ['Blue 1', 'Blue 2', 'Valiant', 'Vigilant', 'Victory', 'Red 1', 'Red Lancer', 'Ranger', 'Hawk Flight', 'Hawk Flight'];
    const labels = labelsOf(...names);
    expect(new Set(labels).size).toBe(names.length);
    for (const label of labels) {
      expect(label.length).toBeLessThanOrEqual(6);
    }
  });
});

import { describe, expect, it } from 'vitest';
import { wordsFor } from './groundVocabulary.ts';

describe('the engine\'s code names in words (playtest m6)', () => {
  it('says each as a person would', () => {
    expect(['SoftCover', 'HullDown', 'TurretDown', 'Evading', 'GoInPosition', 'SystemsDownTarget', 'FaceValue', 'Move'].map(wordsFor))
      .toEqual(['Soft cover', 'Hull down', 'Turret down', 'Evading', 'Go in position', 'Systems down (target)', 'Face value', 'Move']);
  });
});

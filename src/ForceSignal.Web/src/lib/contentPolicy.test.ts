/**
 * The content policy, held to by a test rather than by a comment.
 *
 * README: *the engine owns procedures, the player owns every number those procedures read.* The app
 * ships no rules numbers, no default profiles, no stat blocks.
 *
 * The previous guard against this was a blacklist of one string - `'Class-2 Beam'` - and renaming
 * the invented mount to `'Class-3 Beam'` walked straight past it with the whole suite green. So
 * nothing here names a bad value. Every check below asks the opposite question: **is there a number
 * on its way to a player that no player entered?** A new field added to `ShipForm` has to be
 * classified before this file will pass, which is the only kind of guard that catches the next one
 * rather than the last one.
 */

import { describe, expect, it } from 'vitest';
import { defaultShipForm, newOrdnanceDraft } from '../constants.ts';
import { normalizeFleetExportShip } from './fleetIo.ts';
import { newWeaponMount } from './weapons.ts';
import { normalizeWeaponMount } from './normalize.ts';
import { emptyStarGruntProfileDraft } from './starGruntProfile.ts';
import {
  newAssaultForm,
  newShotForm,
  newThreatLevel,
  newUnitForm,
} from '../components/ground/StarGruntView.tsx';

/**
 * The only numbers on a new-ship form that may be non-zero, each with the reason it is not a rules
 * number. Anything else numeric must open at zero, which is this app's spelling of "not entered" -
 * the server's own clamps floor it, exactly as they do for a mount nobody filled in.
 *
 * To add a field here you have to write down why it is not something the player owns. That is the
 * point: the list is short, and every entry is an argument rather than a value.
 */
const notARulesNumber: Record<string, string> = {
  // A heading, not a quantity. The engine reads a 12-point clock and there is no zero on it.
  currentCourse: 'a heading on a 12-point clock, which has no zero',
  // Where the model is put on the felt. The table settles this with a tape measure; the player
  // drags it. Bounded by the table rather than by any rule.
  positionX: 'a place on the table, not a number a rule reads',
  positionY: 'a place on the table, not a number a rule reads',
};

/** The same list for the ordnance launch form, which is a different form with a different field. */
const notARulesNumberOnASalvo: Record<string, string> = {
  // A salvo leaves the rail at the launching ship's speed. That is where the ship is and how fast
  // it is going - a fact about the table, like the model's place on the felt.
  speed: "the launching ship's own velocity, carried across rather than chosen",
};

function numericFields(form: Record<string, unknown>): [string, number][] {
  return Object.entries(form).filter((entry): entry is [string, number] => typeof entry[1] === 'number');
}

describe('the new-ship form ships no rules numbers', () => {
  it('opens every number the engine reads at zero', () => {
    const numbers = numericFields(defaultShipForm as unknown as Record<string, unknown>);

    // Reached-the-subject: the form really is a form, with numbers on it to check.
    expect(numbers.length).toBeGreaterThan(8);

    const shipped = numbers
      .filter(([field, value]) => value !== 0 && !(field in notARulesNumber))
      .map(([field, value]) => `${field} = ${value}`);

    expect(shipped).toEqual([]);
  });

  it('holds every exemption to being a real field, so the list cannot rot', () => {
    for (const field of Object.keys(notARulesNumber)) {
      expect(Object.hasOwn(defaultShipForm, field)).toBe(true);
      expect(typeof (defaultShipForm as unknown as Record<string, unknown>)[field]).toBe('number');
    }
  });
});

describe('the ordnance launch form ships no rules numbers either', () => {
  it('opens every number the engine reads at zero, bar the ship\'s own speed', () => {
    // It opened on speed 6, endurance 1, two attack dice and a reach of 24 - a whole salvo nobody
    // had entered - and the 24 was the sharp one, because the server invented the same number
    // independently and then refused a point of aim on the strength of it.
    const draft = newOrdnanceDraft({ name: 'Valiant', currentVelocity: 9 });

    // Reached-the-subject: it really is that ship's draft, and there are numbers on it to check.
    expect(draft.name).toBe('Valiant Salvo');
    expect(numericFields(draft).length).toBeGreaterThan(3);

    const shipped = numericFields(draft)
      .filter(([field, value]) => value !== 0 && !(field in notARulesNumberOnASalvo))
      .map(([field, value]) => `${field} = ${value}`);

    expect(shipped).toEqual([]);
  });

  it('carries the launching ship\'s own speed across, which is not a rule', () => {
    // The other half: blanking the seed must not blank the one number that is a fact about the
    // table rather than about anybody's rulebook - and the exemption has to name a real field.
    const draft = newOrdnanceDraft({ name: 'Valiant', currentVelocity: 9 });

    for (const field of Object.keys(notARulesNumberOnASalvo)) {
      expect(Object.hasOwn(draft, field)).toBe(true);
    }

    expect(draft.speed).toBe(9);
  });
});

describe('a fleet import invents nothing the file did not carry', () => {
  it('leaves every rules number of a sparse row unentered', () => {
    // A CSV that names only a ship used to come back with thrust 4, hull 12, armour 4, screens 1,
    // firecons 2, damage control 2, point defence 1 and velocity 8 - a whole stat block, with no
    // form anywhere for the player to notice it had been written for them.
    const ship = normalizeFleetExportShip({ name: 'Valiant' }, defaultShipForm);

    // Reached-the-subject: the row really was read, and the one thing it did carry came through.
    expect(ship.name).toBe('Valiant');

    const numbers = numericFields(ship as unknown as Record<string, unknown>);
    expect(numbers.length).toBeGreaterThan(8);

    const invented = numbers
      .filter(([field, value]) => value !== 0 && !(field in notARulesNumber) && field !== 'initialCourse')
      .map(([field, value]) => `${field} = ${value}`);

    expect(invented).toEqual([]);
  });

  it('still reads every number the file did carry', () => {
    // The other half: blanking the fallback must not blank the import.
    const ship = normalizeFleetExportShip(
      { name: 'Valiant', hullMax: 9, armorMax: 3, thrustRating: 5, screenRating: 2, fireControlMax: 4 },
      defaultShipForm,
    );

    expect(ship.hullMax).toBe(9);
    expect(ship.armorMax).toBe(3);
    expect(ship.thrustRating).toBe(5);
    expect(ship.screenRating).toBe(2);
    expect(ship.fireControlMax).toBe(4);
  });
});

/**
 * The same walk over the StarGrunt screen's forms, which this file could not see.
 *
 * It walked `constants.ts` and `fleetIo.ts` and stopped there, so the add-a-squad panel - a screen
 * whose own caption reads "Transcribed off your own record card. No stats are supplied here." - kept
 * a whole record card in its opening state: quality D8, Leadership 2, eight figures, armour D6 and
 * an impact die of 10. The guard gap was half the finding; a fix that left this file walking two
 * modules would let the next one in by the same door.
 *
 * These are the only numbers those forms may open on, each with the reason it is not a rules number.
 */
const notARulesNumberOnTheGroundForms: Record<string, string> = {
  // How many figures of a unit went down in an assault, and how many pairs fought. Counts off the
  // table in front of the players, like a model's place on the felt - and an assault of no pairs,
  // or a settle-up with nobody down, is not a thing that happened. The engine refuses both.
  pairs: 'how many figures paired off, counted on the table; an assault of no pairs did not happen',
  downed: 'how many figures went down, counted on the table; the engine refuses a settle-up of none',
};

function groundFormNumbers(name: string, form: Record<string, unknown>): string[] {
  return numericFields(form)
    .filter(([field, value]) => value !== 0 && !(field in notARulesNumberOnTheGroundForms))
    .map(([field, value]) => `${name}.${field} = ${value}`);
}

describe('the StarGrunt screen ships no rules numbers either', () => {
  it('opens the add-a-squad form on nothing at all', () => {
    // Reached-the-subject: it really is the add-unit form, with the numbers on it to check.
    expect(newUnitForm.weaponName.length).toBeGreaterThan(0);
    expect(numericFields(newUnitForm as unknown as Record<string, unknown>).length).toBeGreaterThan(3);

    expect(groundFormNumbers('unit', newUnitForm as unknown as Record<string, unknown>)).toEqual([]);
  });

  it('opens the fire panel on nothing at all', () => {
    expect(numericFields(newShotForm as unknown as Record<string, unknown>).length).toBeGreaterThan(1);

    expect(groundFormNumbers('shot', newShotForm as unknown as Record<string, unknown>)).toEqual([]);
  });

  it('opens the assault panel on nothing but what the table counted', () => {
    expect(numericFields(newAssaultForm).length).toBeGreaterThan(4);

    expect(groundFormNumbers('assault', newAssaultForm)).toEqual([]);
  });

  it('opens the threat level unentered, which is what its own caption promises', () => {
    // "The threat level is the one your own table gives the event" - said on screen, beside a
    // control that opened on 2.
    expect(newThreatLevel).toBe(0);
  });

  it('opens the range table on nothing at all', () => {
    // The page the engine used to carry as arithmetic - band widths, a range die per band, reach and
    // cover - and the one form on this screen whose blank is not zero, because zero is an answer for
    // a cover shift. So the walk is over every part of it rather than over its numbers: there are
    // none, and every text field and every table has to open empty.
    const draft = emptyStarGruntProfileDraft() as Record<string, unknown>;

    // Reached-the-subject: it really is the draft, with every part the create form edits on it.
    expect(Object.keys(draft).length).toBeGreaterThan(5);
    expect(numericFields(draft)).toEqual([]);

    const entered = Object.entries(draft)
      .filter(([, value]) => (typeof value === 'string' ? value !== '' : Object.keys(value as object).length > 0))
      .map(([field]) => field);

    expect(entered).toEqual([]);
  });

  it('holds every exemption to being a real field, so the list cannot rot', () => {
    const onTheForms = { ...newUnitForm, ...newShotForm, ...newAssaultForm } as Record<string, unknown>;

    for (const field of Object.keys(notARulesNumberOnTheGroundForms)) {
      expect(Object.hasOwn(onTheForms, field)).toBe(true);
      expect(typeof onTheForms[field]).toBe('number');
    }
  });
});

describe('a mount nobody filled in carries no class name', () => {
  // Not a blacklist. A weapon class in this game is named by a number - "Class-2 Beam" - so a
  // placeholder that carries a digit is a reading off somebody's card whatever the digit is.
  // Renaming the invented mount to 'Class-3 Beam' is what walked past the previous guard.
  it('has no digit in its label, on any of the paths that build one', () => {
    for (const mount of [newWeaponMount(), defaultShipForm.weapons[0], normalizeWeaponMount({})]) {
      expect(mount.name).not.toMatch(/\d/);
      expect(mount.name.trim()).not.toBe('');
    }
  });
});

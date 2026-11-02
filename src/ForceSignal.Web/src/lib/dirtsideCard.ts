/**
 * What the add-platoon form holds: a platoon's record cards, as the player reads them in.
 *
 * Nothing numeric here is off anybody's card until somebody types it. Zero is this app's spelling of
 * "not entered", as it is on StarGrunt's form; the two numbers that open otherwise are counts of the
 * things being described rather than readings (see `contentPolicy.test.ts`). The non-numeric row
 * below is the API's own default for a row: every colour, face value, specials counting. It says
 * nothing about any weapon until a player changes it, and a weapon card with the wrong row is visible
 * on the screen in a way a wrong number on the wire is not.
 */

import type { DirtsideValidityInput, DirtsideWeaponInput } from './dirtsideApi.ts';

export type WeaponCard = Required<DirtsideWeaponInput>;

/** One kind of element in a platoon, and how many of it there are. */
export type ElementCard = {
  copies: number;
  name: string;
  fireControl: string;
  signature: number;
  armourValue: number;
  movement: number;
  hasBackupSystems: boolean;
  // Text, because blank is an answer: the card does not give it, and the element cannot assault.
  assaultChits: string;
  killThreshold: string;
  weapons: WeaponCard[];
};

/** A band's row before anybody has read it in. */
function newValidityRow(): DirtsideValidityInput {
  return { colours: 'All', valueScale: 'FaceValue', specialsCount: true, isIneffective: false };
}

export function newWeaponCard(): WeaponCard {
  return {
    name: 'Main Gun',
    chitCount: 0,
    barrels: 1,
    isFixedMount: false,
    isInterceptable: false,
    close: newValidityRow(),
    medium: newValidityRow(),
    long: newValidityRow(),
  };
}

export function newElementCard(): ElementCard {
  return {
    copies: 1,
    name: 'Vehicle',
    fireControl: 'Basic',
    signature: 0,
    armourValue: 0,
    movement: 0,
    hasBackupSystems: false,
    assaultChits: '',
    killThreshold: '',
    weapons: [newWeaponCard()],
  };
}

function optional(text: string, max: number) {
  if (text.trim() === '') {
    return undefined;
  }

  const parsed = Math.round(Number(text));
  return Number.isFinite(parsed) ? Math.max(0, Math.min(max, parsed)) : undefined;
}

/**
 * Lays the cards out as the elements the API takes, numbering each name across the platoon so two
 * cards that share a name do not produce two "Vehicle 1"s in the log.
 */
export function toElementInputs(cards: ElementCard[], newId: () => string) {
  const used = new Map<string, number>();

  return cards.flatMap((card) => {
    const assaultChits = optional(card.assaultChits, 20);
    const killThreshold = optional(card.killThreshold, 99);

    return Array.from({ length: Math.max(1, card.copies) }, () => {
      const number = (used.get(card.name) ?? 0) + 1;
      used.set(card.name, number);

      return {
        id: newId(),
        name: `${card.name} ${number}`,
        fireControl: card.fireControl,
        signature: card.signature,
        armourValue: card.armourValue,
        movement: card.movement,
        weapons: card.weapons.map((weapon) => ({ ...weapon })),
        hasBackupSystems: card.hasBackupSystems,
        ...(assaultChits === undefined ? {} : { assaultChits }),
        ...(killThreshold === undefined ? {} : { killThreshold }),
      };
    });
  });
}

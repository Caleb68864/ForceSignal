// @vitest-environment jsdom
/**
 * The rendered half of the content policy - the companion to `lib/contentPolicy.test.ts`.
 *
 * That file walks the numbers this app *stores*. This one walks the numbers it *says*, which is the
 * hole it did not cover: `ShipCard` and `PlayMap` stated four of the player's own numbers as prose -
 * "inside 6mu", "one repairs on a 6, and up to three on the same job need only 4 or better", "takes
 * a system on a 6", "24 for a standard load, 36 for extended range" - in tooltips and captions the
 * player cannot overwrite, which flatly contradict a profile that says otherwise. A default is a
 * number offered; these were the app telling the table what their rulebook says.
 *
 * Same shape of guard as its companion, for the same reason: **nothing here names a bad value.** The
 * previous guard against this class was a blacklist of one string, and renaming the invented mount
 * walked straight past it. So each panel is rendered against a ship whose every number is zero and a
 * table that has entered no profile, and then **every digit on screen has to be accounted for**. A
 * new hardcoded number in any of this prose fails this file until somebody classifies it.
 */

import { cleanup, render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { blankRulesProfile, type FiringDraft, type RulesProfile, type Ship } from '../types.ts';
import { DamageControlPanel, FiringConsole, ShipProfileFields } from './ShipCard.tsx';
import { MapFiringAssistant, OrdnanceLaunchPanel } from './map/PlayMap.tsx';
import { defaultShipForm } from '../constants.ts';

/**
 * Numbers the fixtures themselves put on screen, each with the reason it is not a rules number.
 *
 * To add one you have to write down where it came from. That is the point: anything not on this
 * list came from the component, and a number that came from the component is a number no player
 * entered.
 */
const fromTheFixture: Record<string, string> = {
  '0': 'every number on the fixture ship is zero, which is this app\'s spelling of "not entered"',
  '1': 'the single damage-control party the fixture puts aboard, without which that panel renders '
    + 'its "no parties left" branch and states nothing at all',
};

/** A ship with nothing entered on it, and a name and mount carrying no digits of their own. */
function bareShip(overrides: Partial<Ship> = {}): Ship {
  return {
    id: 'ship-one',
    fleetId: 'fleet-one',
    name: 'Valiant',
    className: 'Cruiser',
    thrustRating: 0,
    currentVelocity: 0,
    currentCourse: 0,
    positionX: 0,
    positionY: 0,
    hullMax: 0,
    hullDamage: 0,
    armorMax: 0,
    armorDamage: 0,
    fireControlMax: 0,
    fireControlDamage: 0,
    pointDefenseSystems: 0,
    fighterBays: 0,
    fighterBayDamage: 0,
    damageControlParties: 0,
    driveDamage: 0,
    weaponDamage: 0,
    screenRating: 0,
    screenDamage: 0,
    // A needle beam, because the needle sentence only renders for one.
    weapons: [{ id: 'mount-one', name: 'Needle', attackDice: 0, maxRange: 0, arcs: ['Fore'], ammoMax: 0, ammoUsed: 0, reloadTurns: 0, kind: 'NeedleBeam' }],
    isDestroyed: false,
    iconKey: 'cruiser',
    fighterEnduranceMax: 0,
    fighterEnduranceUsed: 0,
    fighterMaxRange: 0,
    fighterStatus: 'Docked',
    pointsValue: 0,
    effectiveScreens: 0,
    workingFireControl: 0,
    fighterReach: 0,
    repairableSystems: [],
    ...overrides,
  };
}

const draft: FiringDraft = { targetShipId: '', weaponId: 'mount-one', range: 0 };
// Movement rather than Firing, so no firing solution is fetched: the numbers on a solution come off
// the wire and are the player's already.
const phase = 'Movement';
const noSolution = () => new Promise<never>(() => undefined);

/** Every digit run in what a panel actually says: its text, and the tooltips hanging off it. */
function digitsShown(container: HTMLElement): string[] {
  const titles = [...container.querySelectorAll('[title]')].map((node) => node.getAttribute('title') ?? '');
  const said = [container.textContent ?? '', ...titles].join(' ');
  return [...said.matchAll(/\d+/g)].map((match) => match[0]);
}

function unaccountedFor(container: HTMLElement): string[] {
  return [...new Set(digitsShown(container))].filter((digits) => !(digits in fromTheFixture));
}

/** An invented profile. ForceSignal ships none; these exist to be watched arriving on screen. */
const inventedProfile: RulesProfile = {
  ...blankRulesProfile,
  name: 'Invented Layer',
  dieFaces: 8,
  maxScreenLevel: 5,
  pointDefenseRange: 7,
  needleSystemKillRoll: 3,
  maxPartiesPerJob: 4,
  repairRollWithOneParty: 9,
  repairBestRoll: 2,
  salvoAttackRadius: 5,
};

describe('a table that has entered no profile is told no numbers', () => {
  afterEach(cleanup);

  it('says none on the ship profile form', () => {
    const { container } = render(<ShipProfileFields form={defaultShipForm} onChange={() => undefined} />);

    // Reached-the-subject: the form really rendered, tooltips and all.
    expect(container.querySelectorAll('[title]').length).toBeGreaterThan(2);
    expect(unaccountedFor(container)).toEqual([]);
  });

  it('says none on the firing console', () => {
    const ship = bareShip();
    const { container } = render(
      <FiringConsole
        ship={ship}
        ships={[ship]}
        ownedShipIds={new Set([ship.id])}
        draft={draft}
        phase={phase}
        firingResults={[]}
        onChange={() => undefined}
        onFire={() => undefined}
        volleyOpen={false}
        snapshotVersion={0}
        onFiringSolution={noSolution}
        canEndFire={false}
        onCeaseFire={() => undefined}
      />,
    );

    expect(container.textContent).toContain('Needle');
    expect(unaccountedFor(container)).toEqual([]);
  });

  it('says none on the map firing assistant', () => {
    const ship = bareShip();
    const { container } = render(
      <MapFiringAssistant
        ship={ship}
        ships={[ship]}
        ownedShipIds={new Set([ship.id])}
        draft={draft}
        phase={phase}
        firingResults={[]}
        onChange={() => undefined}
        busy={false}
        onFire={() => undefined}
        volleyOpen={false}
        snapshotVersion={0}
        onFiringSolution={noSolution}
        canEndFire={false}
        onCeaseFire={() => undefined}
      />,
    );

    expect(container.textContent).toContain('Needle');
    expect(unaccountedFor(container)).toEqual([]);
  });

  it('says none on the damage control panel', () => {
    const ship = bareShip({
      damageControlParties: 1,
      repairableSystems: [{ key: 'drive', label: 'Drive', kind: 'Drive' }],
    });
    const { container } = render(<DamageControlPanel ship={ship} onRepair={() => undefined} />);

    // Reached-the-subject: past both early returns, into the panel that assigns parties.
    expect(container.textContent).toContain('assigned');
    expect(unaccountedFor(container)).toEqual([]);
  });

  it('says none on the ordnance launch panel', () => {
    const ship = bareShip();
    const { container } = render(
      <OrdnanceLaunchPanel ship={ship} targets={[]} busy={false} onLaunch={() => undefined} />,
    );

    expect(container.textContent).toContain('aimed at a point');
    expect(unaccountedFor(container)).toEqual([]);
  });
});

describe('a table that has entered a profile is told its own numbers', () => {
  afterEach(cleanup);

  // The control for all of the above. A panel that has been stripped of every number reports no
  // unaccounted digits and is useless; these are the same sentences with a profile behind them.
  it('states the point defence reach and repair odds the profile carries', () => {
    const { container } = render(
      <ShipProfileFields form={defaultShipForm} onChange={() => undefined} rules={inventedProfile} />,
    );

    const titles = [...container.querySelectorAll('[title]')].map((node) => node.getAttribute('title') ?? '');
    expect(titles.some((title) => title.includes('inside 7mu'))).toBe(true);
    expect(titles.some((title) => title.includes('one party repairs on a 9') && title.includes('4 on one job') && title.includes('2 or better'))).toBe(true);
    // The screen ceiling is the profile's too - it was a hardcoded 3 on this form's other call site.
    expect((container.querySelector('input[max="5"]') as HTMLInputElement | null)?.value).toBe('0');
  });

  it('states the needle roll the profile carries, on both consoles', () => {
    const ship = bareShip();
    const props = {
      ship,
      ships: [ship],
      ownedShipIds: new Set([ship.id]),
      draft,
      phase,
      rules: inventedProfile,
      firingResults: [],
      onChange: () => undefined,
      onFire: () => undefined,
      volleyOpen: false,
      snapshotVersion: 0,
      onFiringSolution: noSolution,
      canEndFire: false,
      onCeaseFire: () => undefined,
    };

    const console = render(<FiringConsole {...props} />);
    expect(console.container.textContent).toContain('takes a system on a 3');

    const map = render(<MapFiringAssistant {...props} busy={false} />);
    expect(map.container.textContent).toContain('takes a system on a 3');
  });

  it('states the repair odds and the salvo radius the profile carries', () => {
    const ship = bareShip({
      damageControlParties: 1,
      repairableSystems: [{ key: 'drive', label: 'Drive', kind: 'Drive' }],
    });
    const parties = render(<DamageControlPanel ship={ship} rules={inventedProfile} onRepair={() => undefined} />);
    expect(parties.container.textContent).toContain('One party repairs on a 9; 4 on one job need 2 or better.');

    const salvo = render(
      <OrdnanceLaunchPanel ship={ship} targets={[]} busy={false} rules={inventedProfile} onLaunch={() => undefined} />,
    );
    expect(salvo.container.textContent).toContain('strikes the closest enemy within 5.');
  });
});

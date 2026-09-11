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
import { blankRulesProfile, type FiringDraft, type RulesProfile, type Ship, type ShipForm } from '../types.ts';
import { DamageControlPanel, FiringConsole, ShipProfileFields } from './ShipCard.tsx';
import { FighterOpsPanel, MapFiringAssistant, OrdnanceLaunchPanel } from './map/PlayMap.tsx';
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

/**
 * A ship form with nothing entered on it at all.
 *
 * `defaultShipForm` itself opens on a course of 1 and a place on the felt of 12 by 24, which the
 * companion walk exempts with an argument - but those are the fixture's numbers, not the
 * component's, and one of them is 24. Exempting a 24 here would have made this file blind to the
 * fighter reach of 24 the panels were stating, which is the exact shape of accidental pass every
 * guard in this repository has been bitten by. So the fixture enters nothing, and every digit that
 * comes back came from the component.
 */
function bareForm(overrides: Partial<ShipForm> = {}): ShipForm {
  return { ...defaultShipForm, currentCourse: 0, positionX: 0, positionY: 0, ...overrides };
}

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

/**
 * Every digit run in what a panel actually says: its text, the tooltips hanging off it, and the
 * values sitting in its controls.
 *
 * The controls are the half this file could not see, and the half that was still wrong. A number in
 * a box the player is looking at is the app telling them what they entered, which is a stronger
 * claim than a tooltip makes - and `value={form.fighterEnduranceMax || 6}` made it, on a field whose
 * state was zero. The box said six turns of endurance; the caption beside it said none left.
 *
 * `min` and `max` are deliberately not read. Those bound what can be typed rather than stating what
 * was, the server clamps to the same bounds and calls them "a bound on abuse, not a rule", and
 * reading them would turn this guard into an argument about every numeric input on the screen.
 */
function digitsShown(container: HTMLElement): string[] {
  const titles = [...container.querySelectorAll('[title]')].map((node) => node.getAttribute('title') ?? '');
  const entered = [...container.querySelectorAll('input, select, textarea')]
    .map((node) => (node as HTMLInputElement | HTMLSelectElement).value);
  const said = [container.textContent ?? '', ...titles, ...entered].join(' ');
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
    const { container } = render(<ShipProfileFields form={bareForm()} onChange={() => undefined} />);

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

/**
 * The fighter fields, which neither half of this guard could reach.
 *
 * The engine's `NormalizeFighterEnduranceMax` and `NormalizeFighterMaxRange` were fixed to stop
 * inventing six turns of endurance and twenty-four of reach - and the client went on stating both
 * anyway, in the boxes, as `ship.fighterEnduranceMax || 6` and `ship.fighterMaxRange || 24`. The
 * companion walk cannot see them because they are not defaults on a form object but expressions in
 * a `value` prop; this file could not see them because it read text and tooltips and not the
 * controls; and neither reaches the fighter branch at all, since it renders only for a group.
 *
 * The boxes contradicted the caption a line above them, which read the real zero: "Docked - 0 turns
 * left", with a Max of 6 beside it. And they were not harmless display. `min={1}` on the same
 * controls meant a player who typed 0 - meaning "we play no endurance rule" - had it clamped back
 * up to 1 and written to the server.
 */
describe('a fighter group nobody has rated is told no numbers either', () => {
  afterEach(cleanup);

  /** A group, so the fighter fields render at all. */
  const group = { iconKey: 'fighter-group' as const, className: 'Fighters' };

  it('says none on the ship profile form', () => {
    const { container } = render(<ShipProfileFields form={bareForm(group)} onChange={() => undefined} />);

    // Reached-the-subject: this really is the fighter branch, which the cruiser fixture never
    // renders. Without this the check below passes on a form that has no fighter fields on it.
    expect(container.textContent).toContain('Endurance max');
    expect(container.textContent).toContain('Max range');

    expect(unaccountedFor(container)).toEqual([]);
  });

  it('says none on the map fighter ops panel', () => {
    const { container } = render(
      <FighterOpsPanel ship={bareShip(group)} carriers={[]} busy={false} onChange={() => undefined} />,
    );

    // Reached-the-subject: the panel rendered its three boxes.
    expect(container.querySelectorAll('input[type="number"]').length).toBe(3);

    expect(unaccountedFor(container)).toEqual([]);
  });

  it('states the endurance and reach a group has been given', () => {
    // The control that must be accepted. A panel that showed zero whatever the ship carried would
    // pass both checks above and lose the player their own numbers.
    const { container } = render(
      <FighterOpsPanel
        ship={bareShip({ ...group, fighterEnduranceMax: 9, fighterEnduranceUsed: 2, fighterMaxRange: 36 })}
        carriers={[]}
        busy={false}
        onChange={() => undefined}
      />,
    );

    const entered = [...container.querySelectorAll('input[type="number"]')].map((node) => (node as HTMLInputElement).value);
    expect(entered).toEqual(['2', '9', '36']);
    expect(container.textContent).toContain('7 turns left');
  });

  it('lets a group be rated at nothing, which is a table that plays no endurance rule', () => {
    // The other half of the control, and the over-strict trap on this one: `min={1}` meant a player
    // who typed a deliberate zero had it clamped back up to one and sent to a server that accepts
    // zero perfectly well.
    const { container } = render(
      <FighterOpsPanel ship={bareShip(group)} carriers={[]} busy={false} onChange={() => undefined} />,
    );

    for (const node of container.querySelectorAll('input[type="number"]')) {
      expect(node.getAttribute('min')).toBe('0');
    }
  });
});

describe('a table that has entered a profile is told its own numbers', () => {
  afterEach(cleanup);

  // The control for all of the above. A panel that has been stripped of every number reports no
  // unaccounted digits and is useless; these are the same sentences with a profile behind them.
  it('states the point defence reach and repair odds the profile carries', () => {
    const { container } = render(
      <ShipProfileFields form={bareForm()} onChange={() => undefined} rules={inventedProfile} />,
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

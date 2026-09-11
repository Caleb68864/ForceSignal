import { useState } from 'react';
import * as api from '../../lib/dirtsideApi.ts';
import type { DirtsideValidityInput } from '../../lib/dirtsideApi.ts';
import { wholeNumberFrom } from '../../lib/format.ts';
// The lists the API accepts, not this panel's idea of them. See groundVocabulary.ts.
import { chitColourSets, valueScales } from '../../lib/groundVocabulary.ts';
import type { DirtsideElementState, DirtsidePlatoonState, DirtsideSnapshot, GameHandle } from '../../types.ts';


const plainValidity: DirtsideValidityInput = {
  colours: 'All',
  valueScale: 'FaceValue',
  specialsCount: true,
  isIneffective: false,
};

type Run = (action: () => Promise<DirtsideSnapshot>, note?: string) => Promise<void>;

/**
 * Close assault, from the order to go in to the follow-through.
 *
 * The game holds the assault between steps and says which one is owed next, so this panel is a
 * switch on `assault.stage`. Nothing is worked out here: the threat levels and what the chits may
 * count are the players', off their own tables, and which stands come off is the game's.
 */
export function DirtsideAssaultPanel({ game, snapshot, activating, busy, run }: {
  game: GameHandle;
  snapshot: DirtsideSnapshot;
  activating: DirtsidePlatoonState;
  busy: boolean;
  run: Run;
}) {
  const assault = snapshot.assault ?? null;
  const enemies = snapshot.units.filter((unit) => unit.side !== activating.side);

  const [launch, setLaunch] = useState({
    targetUnitId: '',
    elementIds: [] as string[],
    threatLevel: 1,
    validity: plainValidity,
    handToHand: null as DirtsideValidityInput | null,
  });
  const [stand, setStand] = useState({
    elementIds: [] as string[],
    threatLevel: 1,
    validity: plainValidity,
    handToHand: null as DirtsideValidityInput | null,
  });
  const [aftermath, setAftermath] = useState({ light: 1, heavy: 2 });
  const [followThreat, setFollowThreat] = useState(1);

  if (!assault) {
    const target = enemies.find((unit) => unit.id === launch.targetUnitId) ?? enemies[0] ?? null;
    const canGo = activating.elements.filter(
      (element) => !element.isDestroyed && !element.isSystemsDown && !element.hasTakenCombatAction && !element.hasStoodDown,
    );
    const going = launch.elementIds.filter((id) => canGo.some((element) => element.id === id));

    return (
      <div className="card-module" aria-label="Close assault">
        <span className="label module-title">Close assault</span>
        <p className="constraint-line">
          Every element you send spends its combat action whether or not the troops go. The threat
          level and what the chits may count come off your own tables; the platoon needs its quality
          die and leadership value, and each element its assault chits and kill threshold, or the
          order is refused.
        </p>
        <div className="table-fields">
          <label>
            Target platoon
            <select
              value={target?.id ?? ''}
              onChange={(event) => setLaunch((current) => ({ ...current, targetUnitId: event.target.value }))}
            >
              {enemies.map((unit) => <option key={unit.id} value={unit.id}>{unit.name}</option>)}
            </select>
          </label>
          <label title="What the order asks of the attackers, off your own table.">
            Threat level
            <input
              type="number"
              min="0"
              max="9"
              value={launch.threatLevel}
              onChange={(event) => setLaunch((current) => ({ ...current, threatLevel: wholeNumberFrom(event.target.value, 0, 0, 9) }))}
            />
          </label>
        </div>
        <span className="label">Elements going in</span>
        {canGo.length === 0 ? <p className="constraint-line">Nothing in this platoon can still act.</p> : null}
        <ElementPicks elements={canGo} chosen={going} onChange={(elementIds) => setLaunch((current) => ({ ...current, elementIds }))} />
        <ValidityFields
          value={launch.validity}
          onChange={(validity) => setLaunch((current) => ({ ...current, validity }))}
          title="What the attackers' chits may count in the first round, set by the cover the defenders are in."
        />
        <HandToHandToggle
          value={launch.handToHand}
          from={launch.validity}
          onChange={(handToHand) => setLaunch((current) => ({ ...current, handToHand }))}
        />
        <div className="quick-actions">
          <button
            type="button"
            disabled={busy || !target || going.length === 0}
            onClick={() => void run(() => api.launchAssault(game, {
              targetUnitId: target?.id ?? '',
              elementIds: going,
              threatLevel: launch.threatLevel,
              validity: launch.validity,
              handToHandValidity: launch.handToHand,
            }))}
          >
            Launch Assault
          </button>
        </div>
      </div>
    );
  }

  const attacker = snapshot.units.find((unit) => unit.id === assault.attackerUnitId) ?? null;
  const defender = snapshot.units.find((unit) => unit.id === assault.defenderUnitId) ?? null;
  const standing = (unit: DirtsidePlatoonState | null, ids: string[]) =>
    ids.map((id) => unit?.elements.find((element) => element.id === id)?.name ?? id).join(', ') || 'none';
  const canStand = (defender?.elements ?? []).filter((element) => !element.isDestroyed);
  const holding = stand.elementIds.filter((id) => canStand.some((element) => element.id === id));

  return (
    <div className="card-module" aria-label="Close assault">
      <span className="label module-title">Close assault</span>
      <p className="constraint-line">
        {`${attacker?.name ?? assault.attackerUnitId} is assaulting ${defender?.name ?? assault.defenderUnitId} · round ${assault.round}`}
      </p>
      <p className="constraint-line">{`${attacker?.name ?? 'Attacker'} · Still standing: ${standing(attacker, assault.attackerElementIds)}`}</p>
      <p className="constraint-line">
        {`${defender?.name ?? 'Defender'} · Still standing: ${assault.defenderElementIds.length === 0 && assault.stage === 'AwaitingDefender' ? 'not yet committed' : standing(defender, assault.defenderElementIds)}`}
      </p>

      {assault.stage === 'AwaitingDefender' ? (
        <>
          <p className="constraint-line">
            The defenders take a confidence test. Standing goes to a round; giving way goes straight
            to the attackers' follow-through. A platoon whose nerve has already gone gives way without
            a test.
          </p>
          <label title="What holding asks of the defenders, off your own table.">
            Threat level
            <input
              type="number"
              min="0"
              max="9"
              value={stand.threatLevel}
              onChange={(event) => setStand((current) => ({ ...current, threatLevel: wholeNumberFrom(event.target.value, 0, 0, 9) }))}
            />
          </label>
          <span className="label">Elements holding</span>
          <ElementPicks elements={canStand} chosen={holding} onChange={(elementIds) => setStand((current) => ({ ...current, elementIds }))} />
          <ValidityFields
            value={stand.validity}
            onChange={(validity) => setStand((current) => ({ ...current, validity }))}
            title="What the defenders' chits may count in the first round, set by the cover the attackers came from."
          />
          <HandToHandToggle
            value={stand.handToHand}
            from={stand.validity}
            onChange={(handToHand) => setStand((current) => ({ ...current, handToHand }))}
          />
          <div className="quick-actions">
            <button
              type="button"
              disabled={busy || holding.length === 0}
              onClick={() => void run(() => api.standAgainstAssault(game, {
                elementIds: holding,
                threatLevel: stand.threatLevel,
                validity: stand.validity,
                handToHandValidity: stand.handToHand,
              }))}
            >
              Stand
            </button>
          </div>
        </>
      ) : null}

      {assault.stage === 'AwaitingRound' ? (
        <>
          <p className="constraint-line">
            Both sides draw and the game removes the stands that go, in the order they were committed.
            Move the models the log names.
          </p>
          <div className="quick-actions">
            <button type="button" disabled={busy} onClick={() => void run(() => api.fightAssaultRound(game))}>
              Fight Round {assault.round}
            </button>
          </div>
        </>
      ) : null}

      {assault.stage === 'AwaitingAftermath' ? (
        <>
          <p className="constraint-line">
            Each side tests its nerve against what it just lost, the defender first. Both threat
            levels come off your own table; if the defender breaks the attacker is never asked, and
            if both hold the next round is hand to hand.
          </p>
          <div className="table-fields">
            <label title="The threat a side faces after light losses, off your own table.">
              Light casualty threat
              <input
                type="number"
                min="0"
                max="9"
                value={aftermath.light}
                onChange={(event) => setAftermath((current) => ({ ...current, light: wholeNumberFrom(event.target.value, 0, 0, 9) }))}
              />
            </label>
            <label title="The threat a side faces after heavy losses, off your own table.">
              Heavy casualty threat
              <input
                type="number"
                min="0"
                max="9"
                value={aftermath.heavy}
                onChange={(event) => setAftermath((current) => ({ ...current, heavy: wholeNumberFrom(event.target.value, 0, 0, 9) }))}
              />
            </label>
          </div>
          <div className="quick-actions">
            <button
              type="button"
              disabled={busy}
              onClick={() => void run(() => api.resolveAssaultAftermath(game, {
                lightCasualtyThreat: aftermath.light,
                heavyCasualtyThreat: aftermath.heavy,
              }))}
            >
              Resolve Aftermath
            </button>
          </div>
        </>
      ) : null}

      {assault.stage === 'AwaitingFollowThrough' ? (
        <>
          <p className="constraint-line">
            The position is taken. The attackers may test to press on: a pass gives them a whole
            extra activation on the spot. End Activation declines the test and consolidates where
            they are.
          </p>
          <label title="What pressing on asks of the attackers, off your own table.">
            Threat level
            <input
              type="number"
              min="0"
              max="9"
              value={followThreat}
              onChange={(event) => setFollowThreat(wholeNumberFrom(event.target.value, 0, 0, 9))}
            />
          </label>
          <div className="quick-actions">
            <button type="button" disabled={busy} onClick={() => void run(() => api.followThrough(game, followThreat))}>
              Follow Through
            </button>
          </div>
        </>
      ) : null}
    </div>
  );
}

function ElementPicks({ elements, chosen, onChange }: {
  elements: DirtsideElementState[];
  chosen: string[];
  onChange: (ids: string[]) => void;
}) {
  return (
    <div className="table-fields">
      {elements.map((element) => (
        <label key={element.id}>
          {element.name}
          <input
            type="checkbox"
            checked={chosen.includes(element.id)}
            onChange={(event) => onChange(
              event.target.checked
                ? [...chosen, element.id]
                : chosen.filter((id) => id !== element.id),
            )}
          />
        </label>
      ))}
    </div>
  );
}

/** What the chits may count. Off the card; the prefix keeps two sets of these apart on one screen. */
function ValidityFields({ value, onChange, title, prefix }: {
  value: DirtsideValidityInput;
  onChange: (next: DirtsideValidityInput) => void;
  title?: string;
  prefix?: string;
}) {
  const name = (plain: string, prefixed: string) => (prefix ? `${prefix} ${prefixed}` : plain);
  return (
    <div className="table-fields" title={title}>
      <label>
        {name('Colours that count', 'colours')}
        <select value={value.colours} onChange={(event) => onChange({ ...value, colours: event.target.value })}>
          {chitColourSets.map((colour) => <option key={colour} value={colour}>{colour}</option>)}
        </select>
      </label>
      <label>
        {name('Value scale', 'value scale')}
        <select value={value.valueScale} onChange={(event) => onChange({ ...value, valueScale: event.target.value })}>
          {valueScales.map((scale) => <option key={scale} value={scale}>{scale}</option>)}
        </select>
      </label>
      <label title="True when the special chits do something.">
        {name('Specials count', 'specials count')}
        <input type="checkbox" checked={value.specialsCount} onChange={(event) => onChange({ ...value, specialsCount: event.target.checked })} />
      </label>
      <label title="True when these chits cannot harm the target at all.">
        {name('Ineffective', 'ineffective')}
        <input type="checkbox" checked={value.isIneffective} onChange={(event) => onChange({ ...value, isIneffective: event.target.checked })} />
      </label>
    </div>
  );
}

/** From the second round on the cover has stopped mattering. Left out when there was none to lose. */
function HandToHandToggle({ value, from, onChange }: {
  value: DirtsideValidityInput | null;
  from: DirtsideValidityInput;
  onChange: (next: DirtsideValidityInput | null) => void;
}) {
  return (
    <>
      <label title="Tick when the chits read differently once the fighting is hand to hand. Leave it when there was no cover to lose.">
        Hand-to-hand reads differently
        <input type="checkbox" checked={value !== null} onChange={(event) => onChange(event.target.checked ? { ...from } : null)} />
      </label>
      {value ? <ValidityFields value={value} onChange={onChange} prefix="Hand-to-hand" /> : null}
    </>
  );
}

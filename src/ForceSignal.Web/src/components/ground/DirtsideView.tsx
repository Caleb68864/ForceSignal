import { useState } from 'react';
import * as api from '../../lib/dirtsideApi.ts';
import type { DirtsideElementState, DirtsidePlatoonState, DirtsideSnapshot } from '../../types.ts';

const bands = ['Close', 'Medium', 'Long'];
const fireControls = ['Basic', 'Enhanced', 'Superior'];
const colours = ['All', 'Red', 'Yellow', 'Green'];

const newId = () => Math.random().toString(36).slice(2, 10);

/**
 * The Dirtside screen: vehicle-scale ground combat.
 *
 * A platoon activates and the elements inside it decide, so this screen is built around the element
 * rather than the unit. Each one takes one move and one combat action in whichever order it likes,
 * or neither - and the activation cannot close until every element still on the table has said
 * which, because sitting out gives up its go for the whole turn.
 *
 * Every disabled button and every refusal reason comes off the snapshot. The client renders; the
 * game decides.
 */
export function DirtsideView() {
  const [game, setGame] = useState<string | null>(null);
  const [snapshot, setSnapshot] = useState<DirtsideSnapshot | null>(null);
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);

  const [gameName, setGameName] = useState('Dirtside');
  const [platoonForm, setPlatoonForm] = useState({
    name: 'Alpha Troop',
    side: 'blue',
    kind: 'Armour',
    isCybertank: false,
    elements: 2,
    fireControl: 'Basic',
    signature: 3,
    armourValue: 3,
    movement: 12,
    elementName: 'Vehicle',
    weaponName: 'Main Gun',
    chitCount: 3,
    barrels: 1,
    isFixedMount: false,
    colours: 'All',
  });

  const [shot, setShot] = useState({
    elementId: '',
    weapon: '',
    targetUnitId: '',
    targetElementId: '',
    measuredBand: 'Close',
    overHalf: false,
  });

  async function run(action: () => Promise<DirtsideSnapshot>, note?: string) {
    setBusy(true);
    try {
      const next = await action();
      setSnapshot(next);
      setMessage(note ?? next.log[next.log.length - 1] ?? '');
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error));
    } finally {
      setBusy(false);
    }
  }

  async function start() {
    setBusy(true);
    try {
      const created = await api.createGame(gameName);
      setGame(created.gameId);
      setSnapshot(created.snapshot);
      setMessage(`Started ${created.snapshot.name}.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error));
    } finally {
      setBusy(false);
    }
  }

  if (!game || !snapshot) {
    return (
      <section className="panel" aria-label="Dirtside">
        <h2>Dirtside II</h2>
        <p className="privacy">
          Vehicle-scale ground combat. One device at the table, no room code. Every number you enter
          comes off your own record cards - this app ships none of them.
        </p>
        <label>
          Game name
          <input value={gameName} onChange={(event) => setGameName(event.target.value)} />
        </label>
        <button type="button" disabled={busy} onClick={() => void start()}>Start Game</button>
        {message ? <p className="constraint-line">{message}</p> : null}
      </section>
    );
  }

  const activating = snapshot.units.find((unit) => unit.id === snapshot.activatingUnitId) ?? null;
  const enemies = activating
    ? snapshot.units.filter((unit) => unit.side !== activating.side)
    : [];
  const targetUnit = enemies.find((unit) => unit.id === shot.targetUnitId) ?? enemies[0] ?? null;
  const canStillFire = (element: DirtsideElementState) =>
    !element.isDestroyed && !element.isSystemsDown && !element.hasTakenCombatAction && !element.hasStoodDown;
  const firing = activating?.elements.find((element) => element.id === shot.elementId)
    ?? activating?.elements.find(canStillFire)
    ?? null;

  return (
    <section className="panel" aria-label="Dirtside">
      <h2>{snapshot.name}</h2>
      <p className="constraint-line">
        Turn {snapshot.turnNumber} · {snapshot.phase}
        {snapshot.activeSide ? ` · ${snapshot.activeSide} to go` : ''}
      </p>
      {message ? <p className="constraint-line">{message}</p> : null}

      <div className="quick-actions">
        <button type="button" disabled={busy} onClick={() => void run(() => api.beginTurn(game))}>
          Begin Turn
        </button>
        {snapshot.sides.map((side) => (
          <button
            key={`first-${side}`}
            className="ghost"
            type="button"
            disabled={busy}
            onClick={() => void run(() => api.chooseFirstActivator(game, side, true))}
          >
            {side} Goes First
          </button>
        ))}
        {snapshot.sides.map((side) => (
          <button
            key={`pass-${side}`}
            className="ghost"
            type="button"
            disabled={busy}
            onClick={() => void run(() => api.pass(game, side))}
          >
            {side} Passes
          </button>
        ))}
        <button className="ghost" type="button" disabled={busy} onClick={() => void run(() => api.endTurn(game))}>
          End Turn
        </button>
      </div>

      <div className="card-module" aria-label="Platoons">
        <span className="label module-title">Platoons</span>
        {snapshot.units.length === 0 ? (
          <p className="constraint-line">Nothing on the table yet.</p>
        ) : null}
        {snapshot.units.map((unit) => (
          <PlatoonCard
            key={unit.id}
            unit={unit}
            isActivating={unit.id === snapshot.activatingUnitId}
            busy={busy}
            onActivate={() => void run(() => api.beginActivation(game, unit.side, unit.id))}
          />
        ))}
      </div>

      {activating ? (
        <div className="card-module" aria-label="Activation">
          <span className="label module-title">{activating.name} is activated</span>
          <p className="constraint-line">
            Each element takes one move and one combat action, in whichever order it likes, or
            neither. The activation cannot close until every element has said which - sitting out
            gives up its go for the whole turn.
          </p>

          {activating.elements.filter((element) => !element.isDestroyed).map((element) => (
            <div key={element.id} className="table-fields">
              <span className="label">
                {element.name}
                {element.hasStoodDown ? ' · stood down' : ''}
                {element.hasMoved ? ' · moved' : ''}
                {element.hasTakenCombatAction ? ' · acted' : ''}
                {element.isDamaged ? ' · damaged' : ''}
                {element.isSystemsDown ? ' · systems down' : ''}
                {element.movedOverHalf ? ' · moved far' : ''}
                {element.areaDefenceSensorsLive ? ' · sensors live' : ''}
              </span>
              <button
                type="button"
                className="ghost"
                disabled={busy || element.hasMoved || element.hasStoodDown}
                title={element.hasStoodDown ? 'It sat this one out, so it is out for the turn.' : undefined}
                onClick={() => void run(() => api.moveElement(game, element.id, shot.overHalf))}
              >
                Move
              </button>
              <button
                type="button"
                className="ghost"
                disabled={busy || element.hasMoved || element.hasTakenCombatAction || element.hasStoodDown}
                title="Sitting out gives up its go for the whole turn, so it cannot follow a move or a shot."
                onClick={() => void run(() => api.standDown(game, element.id))}
              >
                Stand Down
              </button>
              <button
                type="button"
                className="ghost"
                disabled={busy || element.hasTakenCombatAction || element.hasStoodDown}
                title="Spends its one combat action, and buys interception for the rest of the turn."
                onClick={() => void run(() => api.setSensors(game, element.id, !element.areaDefenceSensorsLive))}
              >
                Sensors {element.areaDefenceSensorsLive ? 'Off' : 'On'}
              </button>
              <button
                type="button"
                className="ghost"
                disabled={busy || !canStillFire(element)}
                onClick={() => setShot((current) => ({ ...current, elementId: element.id, weapon: '' }))}
              >
                Aim This One
              </button>
            </div>
          ))}

          <label title="Measured with a tape at the table, so it is answered rather than worked out.">
            That move covers over half its movement
            <input
              type="checkbox"
              checked={shot.overHalf}
              onChange={(event) => setShot((current) => ({ ...current, overHalf: event.target.checked }))}
            />
          </label>

          <span className="label module-title">Direct fire</span>
          <p className="constraint-line">
            The declaration is binding. A shot at something an earlier shot destroyed is refused
            rather than re-pointed, which is the cost of information you did not have.
          </p>
          <div className="table-fields">
            <label>
              Firing element
              <select
                value={firing?.id ?? ''}
                onChange={(event) => setShot((current) => ({ ...current, elementId: event.target.value }))}
              >
                {activating.elements.filter(canStillFire).map((element) => (
                  <option key={element.id} value={element.id}>{element.name}</option>
                ))}
              </select>
            </label>
            <label>
              Weapon
              <select
                value={shot.weapon || firing?.weapons[0] || ''}
                onChange={(event) => setShot((current) => ({ ...current, weapon: event.target.value }))}
              >
                {(firing?.weapons ?? []).map((weapon) => (
                  <option key={weapon} value={weapon}>{weapon}</option>
                ))}
              </select>
            </label>
            <label>
              Target platoon
              <select
                value={targetUnit?.id ?? ''}
                onChange={(event) => setShot((current) => ({
                  ...current,
                  targetUnitId: event.target.value,
                  targetElementId: '',
                }))}
              >
                {enemies.map((unit) => <option key={unit.id} value={unit.id}>{unit.name}</option>)}
              </select>
            </label>
            <label>
              Target element
              <select
                value={shot.targetElementId || targetUnit?.elements.find((e) => !e.isDestroyed)?.id || ''}
                onChange={(event) => setShot((current) => ({ ...current, targetElementId: event.target.value }))}
              >
                {(targetUnit?.elements ?? []).filter((element) => !element.isDestroyed).map((element) => (
                  <option key={element.id} value={element.id}>{element.name}</option>
                ))}
              </select>
            </label>
            <label title="The band the tape says the shot falls in.">
              Measured band
              <select
                value={shot.measuredBand}
                onChange={(event) => setShot((current) => ({ ...current, measuredBand: event.target.value }))}
              >
                {bands.map((band) => <option key={band} value={band}>{band}</option>)}
              </select>
            </label>
          </div>
          <div className="quick-actions">
            <button
              type="button"
              disabled={busy || !firing || !targetUnit}
              onClick={() => void run(() => api.fire(game, {
                elementId: firing?.id ?? '',
                weapon: shot.weapon || firing?.weapons[0] || '',
                targetUnitId: targetUnit?.id ?? '',
                targetElementId:
                  shot.targetElementId
                  || targetUnit?.elements.find((element) => !element.isDestroyed)?.id
                  || '',
                measuredBand: shot.measuredBand,
              }))}
            >
              Fire
            </button>
            <button
              type="button"
              className="ghost"
              disabled={busy || !snapshot.canEndActivation}
              title={snapshot.whyActivationCannotEnd ?? undefined}
              onClick={() => void run(() => api.endActivation(game))}
            >
              End Activation
            </button>
          </div>
          {!snapshot.canEndActivation && snapshot.whyActivationCannotEnd ? (
            <p className="constraint-line">{snapshot.whyActivationCannotEnd}</p>
          ) : null}
        </div>
      ) : null}

      <div className="card-module" aria-label="Add platoon">
        <span className="label module-title">Add a platoon</span>
        <p className="constraint-line">
          Every number here comes off your own record card. Signature runs 1 for the largest down to
          5 for the smallest. A weapon on a fixed mount can be fired before moving, never after.
        </p>
        <div className="table-fields">
          <label>Name<input value={platoonForm.name} onChange={(e) => setPlatoonForm({ ...platoonForm, name: e.target.value })} /></label>
          <label>Side<input value={platoonForm.side} onChange={(e) => setPlatoonForm({ ...platoonForm, side: e.target.value })} /></label>
          <label>
            Kind
            <select value={platoonForm.kind} onChange={(e) => setPlatoonForm({ ...platoonForm, kind: e.target.value })}>
              <option value="Armour">Armour</option>
              <option value="DismountedInfantry">Dismounted infantry</option>
            </select>
          </label>
          <label title="A cybertank carries no confidence marker at all, so nothing about morale applies to it.">
            Cybertank
            <input type="checkbox" checked={platoonForm.isCybertank} onChange={(e) => setPlatoonForm({ ...platoonForm, isCybertank: e.target.checked })} />
          </label>
          <label>Elements<input type="number" min="1" max="12" value={platoonForm.elements} onChange={(e) => setPlatoonForm({ ...platoonForm, elements: Number(e.target.value) })} /></label>
          <label title="Each element is numbered from this, so the log reads 'Alpha Troop&apos;s Vehicle 1'.">Element name<input value={platoonForm.elementName} onChange={(e) => setPlatoonForm({ ...platoonForm, elementName: e.target.value })} /></label>
          <label>
            Fire control
            <select value={platoonForm.fireControl} onChange={(e) => setPlatoonForm({ ...platoonForm, fireControl: e.target.value })}>
              {fireControls.map((level) => <option key={level} value={level}>{level}</option>)}
            </select>
          </label>
          <label>Signature<input type="number" min="1" max="5" value={platoonForm.signature} onChange={(e) => setPlatoonForm({ ...platoonForm, signature: Number(e.target.value) })} /></label>
          <label>Armour<input type="number" min="0" max="30" value={platoonForm.armourValue} onChange={(e) => setPlatoonForm({ ...platoonForm, armourValue: Number(e.target.value) })} /></label>
          <label>Movement<input type="number" min="0" max="60" value={platoonForm.movement} onChange={(e) => setPlatoonForm({ ...platoonForm, movement: Number(e.target.value) })} /></label>
          <label>Weapon<input value={platoonForm.weaponName} onChange={(e) => setPlatoonForm({ ...platoonForm, weaponName: e.target.value })} /></label>
          <label title="How many chits each hit draws.">Chits<input type="number" min="0" max="20" value={platoonForm.chitCount} onChange={(e) => setPlatoonForm({ ...platoonForm, chitCount: Number(e.target.value) })} /></label>
          <label title="Weapons of the same type in the mount. They fire together, at one target.">Barrels<input type="number" min="1" max="8" value={platoonForm.barrels} onChange={(e) => setPlatoonForm({ ...platoonForm, barrels: Number(e.target.value) })} /></label>
          <label title="Aimed by pointing the whole vehicle.">
            Fixed mount
            <input type="checkbox" checked={platoonForm.isFixedMount} onChange={(e) => setPlatoonForm({ ...platoonForm, isFixedMount: e.target.checked })} />
          </label>
          <label title="Which chit colours this weapon's hits may count, off your own card.">
            Chit colours
            <select value={platoonForm.colours} onChange={(e) => setPlatoonForm({ ...platoonForm, colours: e.target.value })}>
              {colours.map((colour) => <option key={colour} value={colour}>{colour}</option>)}
            </select>
          </label>
        </div>
        <button
          type="button"
          disabled={busy}
          onClick={() => {
            const row = { colours: platoonForm.colours };
            void run(() => api.addPlatoon(game, {
              id: newId(),
              name: platoonForm.name,
              side: platoonForm.side,
              kind: platoonForm.kind,
              isCybertank: platoonForm.isCybertank,
              elements: Array.from({ length: Math.max(1, platoonForm.elements) }, (_, index) => ({
                id: newId(),
                name: `${platoonForm.elementName} ${index + 1}`,
                fireControl: platoonForm.fireControl,
                signature: platoonForm.signature,
                armourValue: platoonForm.armourValue,
                movement: platoonForm.movement,
                weapons: [{
                  name: platoonForm.weaponName,
                  chitCount: platoonForm.chitCount,
                  barrels: platoonForm.barrels,
                  isFixedMount: platoonForm.isFixedMount,
                  close: row,
                  medium: row,
                  long: row,
                }],
              })),
            }), `${platoonForm.name} joined ${platoonForm.side}.`);
          }}
        >
          Add Platoon
        </button>
      </div>

      <div className="card-module" aria-label="Battle log">
        <span className="label module-title">Battle log</span>
        {snapshot.log.length === 0 ? <p className="constraint-line">Nothing has happened yet.</p> : null}
        <ol>
          {snapshot.log.map((entry, index) => <li key={`${index}-${entry}`}>{entry}</li>)}
        </ol>
      </div>
    </section>
  );
}

function PlatoonCard({ unit, isActivating, busy, onActivate }: {
  unit: DirtsidePlatoonState;
  isActivating: boolean;
  busy: boolean;
  onActivate: () => void;
}) {
  return (
    <div className="table-fields">
      <span className="label">
        {unit.name} · {unit.side}
        {unit.isCybertank ? ' · cybertank' : ` · ${unit.confidence}`}
        {unit.isUnderFire ? ' · under fire' : ''}
        {unit.isDisorganised ? ' · disorganised' : ''}
        {unit.hasActivated ? ' · gone' : ''}
      </span>
      <span className="constraint-line">
        {unit.elements.map((element) => describe(element)).join(' · ')}
      </span>
      <button
        type="button"
        disabled={busy || !unit.canActivate || isActivating}
        title={unit.whyItCannotActivate ?? undefined}
        onClick={onActivate}
      >
        {isActivating ? 'Activated' : 'Activate'}
      </button>
    </div>
  );
}

function describe(element: DirtsideElementState) {
  if (element.isDestroyed) {
    return `${element.name}: out`;
  }

  const marks = [
    element.isDamaged ? 'damaged' : null,
    element.isSystemsDown ? 'systems down' : null,
    element.areaDefenceSensorsLive ? 'sensors live' : null,
  ].filter(Boolean);
  return `${element.name}${marks.length ? `: ${marks.join(', ')}` : ''}`;
}

/**
 * The StarGrunt screen: one device, passed around the table.
 *
 * Nothing here decides whether an action is allowed. Every disabled button and every reason beside
 * it comes off the snapshot, from the same checks the server enforces - which is the rule the Full
 * Thrust side paid for twice.
 *
 * The app holds no positions and computes no line of sight. Range and cover are declared per shot,
 * because that is how the game is played: with a tape measure and an eyeball.
 */

import { useState } from 'react';
import { ApiRequestError, newId } from '../../lib/api.ts';
import * as api from '../../lib/starGruntApi.ts';
import type { StarGruntSnapshot, StarGruntUnit } from '../../types.ts';

const ladder = [4, 6, 8, 10, 12];
const covers = ['None', 'Soft', 'Hard'];
const unarmedActions = ['Move', 'Dash', 'Reorganise', 'Rally', 'Observe', 'Communicate', 'RemoveSuppression', 'GoInPosition'];

type UnitForm = {
  name: string;
  side: string;
  qualityDie: number;
  leadershipDie: number;
  figures: number;
  armourDie: number;
  weaponName: string;
  impactDie: number;
};

type ShotForm = {
  targetId: string;
  weaponName: string;
  firepowerDie: number;
  supportDie: number;
  useSupport: boolean;
  distanceInches: number;
  cover: string;
  inPosition: boolean;
};

export function StarGruntView() {
  const [snapshot, setSnapshot] = useState<StarGruntSnapshot | null>(null);
  const [message, setMessage] = useState('Start a game to begin.');
  const [busy, setBusy] = useState(false);
  const [gameName, setGameName] = useState('Hill 43');
  const [unitForm, setUnitForm] = useState<UnitForm>({
    name: 'Alpha Squad',
    side: 'blue',
    qualityDie: 8,
    leadershipDie: 8,
    figures: 8,
    armourDie: 6,
    weaponName: 'Rifles',
    impactDie: 10,
  });
  const [shot, setShot] = useState<ShotForm>({
    targetId: '',
    weaponName: '',
    firepowerDie: 8,
    supportDie: 8,
    useSupport: false,
    distanceInches: 10,
    cover: 'None',
    inPosition: false,
  });

  /** Runs a command and keeps whatever the server said about it. */
  async function run(work: () => Promise<StarGruntSnapshot>, note?: string) {
    setBusy(true);
    try {
      setSnapshot(await work());
      if (note) {
        setMessage(note);
      }
    } catch (error) {
      // A refusal is the server's sentence, shown as written rather than reworded here.
      setMessage(error instanceof ApiRequestError ? error.message : 'That did not work.');
    } finally {
      setBusy(false);
    }
  }

  async function start() {
    setBusy(true);
    try {
      const created = await api.createGame(gameName);
      setSnapshot(created.snapshot);
      setMessage(`Started ${created.snapshot.name}. Add a squad a side.`);
    } catch (error) {
      setMessage(error instanceof ApiRequestError ? error.message : 'That did not work.');
    } finally {
      setBusy(false);
    }
  }

  if (!snapshot) {
    return (
      <section className="stargrunt card-module" aria-label="StarGrunt setup">
        <span className="label module-title">StarGrunt II</span>
        <p className="constraint-line">
          Infantry scale, one device at the table. Bring your own rules: every die below is one you read off
          your own record card.
        </p>
        <label>
          Game name
          <input value={gameName} onChange={(event) => setGameName(event.target.value)} />
        </label>
        <button type="button" disabled={busy} onClick={start}>Start Game</button>
        <p className="constraint-line">{message}</p>
      </section>
    );
  }

  const game = snapshot.gameId;
  const activating = snapshot.units.find((unit) => unit.id === snapshot.activatingUnitId);
  const canPlay = snapshot.units.length >= 2 && snapshot.sides.length === 2;
  const targets = snapshot.units.filter((unit) => unit.id !== activating?.id && unit.figuresAlive > 0);

  return (
    <section className="stargrunt" aria-label="StarGrunt game">
      <div className="card-module">
        <span className="label module-title">{snapshot.name}</span>
        <p className="constraint-line">
          Turn {snapshot.turnNumber} · {snapshot.phase}
          {snapshot.activeSide ? ` · ${snapshot.activeSide} to act` : ''}
          {snapshot.firstActivationChooser ? ` · ${snapshot.firstActivationChooser} chooses who goes first` : ''}
        </p>
        <p className="constraint-line">{message}</p>
        <div className="quick-actions">
          <button
            className="ghost"
            type="button"
            disabled={busy || !canPlay}
            onClick={() => run(() => api.beginTurn(game), 'Turn opened.')}
          >
            Begin Turn
          </button>
          {snapshot.sides.map((side) => (
            <button
              key={`first-${side}`}
              className="ghost"
              type="button"
              disabled={busy || snapshot.phase !== 'ChoosingFirstActivator'}
              onClick={() => run(() => api.chooseFirstActivator(game, side, true), `${side} takes the first activation.`)}
            >
              {side} goes first
            </button>
          ))}
          {snapshot.sides.map((side) => (
            <button
              key={`pass-${side}`}
              className="ghost"
              type="button"
              disabled={busy}
              onClick={() => run(() => api.pass(game, side), `${side} passed.`)}
            >
              {side} passes
            </button>
          ))}
          <button
            className="ghost"
            type="button"
            disabled={busy}
            onClick={() => run(() => api.endTurn(game), 'Turn ended.')}
          >
            End Turn
          </button>
        </div>
      </div>

      <div className="stargrunt-roster">
        {snapshot.units.map((unit) => (
          <UnitCard
            key={unit.id}
            unit={unit}
            isActivating={unit.id === snapshot.activatingUnitId}
            busy={busy}
            onActivate={() => run(
              () => api.beginActivation(game, unit.side, unit.id),
              `${unit.name} activated.`,
            )}
          />
        ))}
      </div>

      {activating ? (
        <div className="card-module" aria-label={`${activating.name} activation`}>
          <span className="label module-title">{activating.name} is activated</span>
          <div className="quick-actions">
            {unarmedActions.map((action) => (
              <button
                key={action}
                className="ghost"
                type="button"
                disabled={busy}
                onClick={() => run(() => api.takeStep(game, action), `${activating.name}: ${action}.`)}
              >
                {action}
              </button>
            ))}
            <button
              type="button"
              disabled={busy}
              onClick={() => run(() => api.endActivation(game), `${activating.name} is done.`)}
            >
              End Activation
            </button>
          </div>

          <FirePanel
            firer={activating}
            targets={targets}
            shot={shot}
            busy={busy}
            onChange={(patch) => setShot((current) => ({ ...current, ...patch }))}
            onFire={() => run(() => api.fire(game, {
              firerId: activating.id,
              targetId: shot.targetId || targets[0]?.id || '',
              weaponName: shot.weaponName || activating.weapons[0]?.name || '',
              firepowerDie: shot.firepowerDie,
              supportDice: shot.useSupport ? [shot.supportDie] : [],
              distanceInches: shot.distanceInches,
              cover: shot.cover,
              inPosition: shot.inPosition,
            }))}
          />
        </div>
      ) : null}

      <AddUnitPanel
        form={unitForm}
        busy={busy}
        onChange={(patch) => setUnitForm((current) => ({ ...current, ...patch }))}
        onAdd={() => run(
          () => api.addUnit(game, {
            id: newId(),
            name: unitForm.name,
            side: unitForm.side,
            level: 'Squad',
            qualityDie: unitForm.qualityDie,
            leadershipDie: unitForm.leadershipDie,
            figures: Array.from({ length: Math.max(1, unitForm.figures) }, () => ({ armourDie: unitForm.armourDie })),
            weapons: [{ name: unitForm.weaponName, impactDie: unitForm.impactDie, isSupport: false, isCloseRange: false }],
          }),
          `${unitForm.name} joined ${unitForm.side}.`,
        )}
      />

      <div className="card-module" aria-label="StarGrunt log">
        <span className="label module-title">Log</span>
        {snapshot.log.length === 0
          ? <p className="constraint-line">Nothing has happened yet.</p>
          : <ul className="stargrunt-log">{snapshot.log.map((entry, index) => <li key={`${index}-${entry}`}>{entry}</li>)}</ul>}
      </div>
    </section>
  );
}

function UnitCard({
  unit,
  isActivating,
  busy,
  onActivate,
}: {
  unit: StarGruntUnit;
  isActivating: boolean;
  busy: boolean;
  onActivate: () => void;
}) {
  const wiped = unit.figuresAlive <= 0;
  return (
    <div className={`card-module stargrunt-unit${isActivating ? ' activating' : ''}${wiped ? ' wiped' : ''}`}>
      <span className="label module-title">{unit.name}</span>
      <div className="ship-readouts">
        <div><span className="label">Side</span><strong>{unit.side}</strong></div>
        <div><span className="label">Figures</span><strong>{unit.figuresAlive}/{unit.fullStrength}</strong></div>
        <div><span className="label">Wounded</span><strong>{unit.figuresWounded}</strong></div>
        <div><span className="label">Suppression</span><strong>{unit.suppressionMarkers}</strong></div>
        <div><span className="label">Confidence</span><strong>{unit.confidence}</strong></div>
        <div><span className="label">Quality</span><strong>D{unit.qualityDie}</strong></div>
      </div>
      <p className="constraint-line">
        {wiped ? 'Wiped out.' : unit.canActivate ? 'Ready to activate.' : unit.activationBlocker ?? 'Not now.'}
        {unit.isInCover ? ' In cover.' : ''}
        {unit.isDisorganised ? ' Disorganised.' : ''}
      </p>
      <button type="button" disabled={busy || !unit.canActivate} onClick={onActivate}>Activate</button>
    </div>
  );
}

function FirePanel({
  firer,
  targets,
  shot,
  busy,
  onChange,
  onFire,
}: {
  firer: StarGruntUnit;
  targets: StarGruntUnit[];
  shot: ShotForm;
  busy: boolean;
  onChange: (patch: Partial<ShotForm>) => void;
  onFire: () => void;
}) {
  const weaponName = shot.weaponName || firer.weapons[0]?.name || '';
  const legality = firer.weaponLegality.find((weapon) => weapon.name === weaponName);
  const hasTarget = targets.length > 0;
  const canFire = Boolean(legality?.canFire) && hasTarget && !busy;

  return (
    <div className="firing-console card-module" aria-label={`${firer.name} fire`}>
      <span className="label module-title">Fire</span>
      <p className="constraint-line">
        {!hasTarget ? 'Nothing to shoot at.' : legality?.canFire ? 'Ready.' : legality?.blocker ?? 'Pick a weapon.'}
      </p>
      <label>
        Target
        <select value={shot.targetId || targets[0]?.id || ''} onChange={(event) => onChange({ targetId: event.target.value })}>
          {targets.map((target) => (
            <option key={target.id} value={target.id}>{target.name} · {target.figuresAlive} figures</option>
          ))}
        </select>
      </label>
      <label>
        Weapon
        <select value={weaponName} onChange={(event) => onChange({ weaponName: event.target.value })}>
          {firer.weapons.map((weapon) => {
            const spent = firer.weaponLegality.find((entry) => entry.name === weapon.name);
            return (
              <option key={weapon.name} value={weapon.name}>
                {weapon.name} · D{weapon.impactDie}{spent?.canFire ? '' : ' · spent'}
              </option>
            );
          })}
        </select>
      </label>
      <label title="From your own rules, off how many figures are actually shooting. The app ships no firepower table.">
        Firepower
        <select value={shot.firepowerDie} onChange={(event) => onChange({ firepowerDie: Number(event.target.value) })}>
          {ladder.map((die) => <option key={die} value={die}>D{die}</option>)}
        </select>
      </label>
      <label>
        Support die
        <select
          value={shot.useSupport ? shot.supportDie : 0}
          onChange={(event) => {
            const value = Number(event.target.value);
            onChange({ useSupport: value > 0, supportDie: value > 0 ? value : shot.supportDie });
          }}
        >
          <option value={0}>None</option>
          {ladder.map((die) => <option key={die} value={die}>D{die}</option>)}
        </select>
      </label>
      <label>
        Range
        <input
          type="number"
          min="1"
          value={shot.distanceInches}
          onChange={(event) => onChange({ distanceInches: Number(event.target.value) })}
        />
      </label>
      <label>
        Cover
        <select value={shot.cover} onChange={(event) => onChange({ cover: event.target.value })}>
          {covers.map((cover) => <option key={cover} value={cover}>{cover}</option>)}
        </select>
      </label>
      <label>
        In position
        <input type="checkbox" checked={shot.inPosition} onChange={(event) => onChange({ inPosition: event.target.checked })} />
      </label>
      <button type="button" disabled={!canFire} onClick={onFire}>Fire</button>
    </div>
  );
}

function AddUnitPanel({
  form,
  busy,
  onChange,
  onAdd,
}: {
  form: UnitForm;
  busy: boolean;
  onChange: (patch: Partial<UnitForm>) => void;
  onAdd: () => void;
}) {
  return (
    <div className="card-module" aria-label="Add a unit">
      <span className="label module-title">Add a squad</span>
      <p className="constraint-line">
        Transcribed off your own record card. No stats are supplied here.
      </p>
      <label>
        Name
        <input value={form.name} onChange={(event) => onChange({ name: event.target.value })} />
      </label>
      <label>
        Side
        <input value={form.side} onChange={(event) => onChange({ side: event.target.value })} />
      </label>
      <label>
        Quality
        <select value={form.qualityDie} onChange={(event) => onChange({ qualityDie: Number(event.target.value) })}>
          {ladder.map((die) => <option key={die} value={die}>D{die}</option>)}
        </select>
      </label>
      <label>
        Leadership
        <select value={form.leadershipDie} onChange={(event) => onChange({ leadershipDie: Number(event.target.value) })}>
          {ladder.map((die) => <option key={die} value={die}>D{die}</option>)}
        </select>
      </label>
      <label>
        Figures
        <input
          type="number"
          min="1"
          max="20"
          value={form.figures}
          onChange={(event) => onChange({ figures: Number(event.target.value) })}
        />
      </label>
      <label>
        Armour
        <select value={form.armourDie} onChange={(event) => onChange({ armourDie: Number(event.target.value) })}>
          {ladder.map((die) => <option key={die} value={die}>D{die}</option>)}
        </select>
      </label>
      <label>
        Weapon
        <input value={form.weaponName} onChange={(event) => onChange({ weaponName: event.target.value })} />
      </label>
      <label>
        Impact
        <select value={form.impactDie} onChange={(event) => onChange({ impactDie: Number(event.target.value) })}>
          {ladder.map((die) => <option key={die} value={die}>D{die}</option>)}
        </select>
      </label>
      <button type="button" disabled={busy} onClick={onAdd}>Add Squad</button>
    </div>
  );
}

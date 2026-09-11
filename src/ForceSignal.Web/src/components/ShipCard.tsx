/**
 * The controls that hang off a single ship: its icon, its editable profile, its firing console,
 * its helm, and its damage record.
 */

import { useState, type CSSProperties, type KeyboardEvent, type PointerEvent } from 'react';
import { arcAbbreviations, fighterStatuses, firableArcs, shipIconOptions, shipPresets, weaponKinds } from '../constants.ts';
import { newId } from '../lib/api.ts';
import { numberFrom, wholeNumberFrom } from '../lib/format.ts';
import { courseAngle, courseFromPoint, distanceBetweenShips, wrapCourse } from '../lib/geometry.ts';
import { formatTurnSequence, maxLegalTurn, previewCourse, totalTurnSteps, turnPatchForCourse, turnPatchFromManeuvers } from '../lib/movement.ts';
import { normalizeShipIconKey } from '../lib/normalize.ts';
import { arcLabel, buildPreTurnChecklist, damageControlNote, describeArcs, firingTargetOptions, isFighterGroupForm, needleSystemNote, partiesPerJobCap, pointDefenceNote, repairOddsNote } from '../lib/rules.ts';
import { useFiringSolution } from '../lib/useFiringSolution.ts';
import { newWeaponMount, updateWeapon } from '../lib/weapons.ts';
import type { DraftOrder, FighterStatus, FiringDraft, FiringResult, FiringSolution, MatchSnapshot, RepairJob, RulesProfile, Ship, ShipForm, ShipIconKey, TurnDirection, WeaponKind } from '../types.ts';

export function ShipIcon({ iconKey }: { iconKey: ShipIconKey }) {
  return (
    <svg className="ship-icon-svg" viewBox="0 0 64 64" aria-hidden="true">
      {shipIconPath(iconKey)}
    </svg>
  );
}
function shipIconPath(iconKey: ShipIconKey) {
  switch (iconKey) {
    case 'escort':
      return <path d="M32 4 42 29 56 54 38 48 32 61 26 48 8 54 22 29Z" />;
    case 'frigate':
      return <path d="M32 3 44 24 56 51 40 47 32 60 24 47 8 51 20 24Z M24 33h16l-8-17Z" />;
    case 'destroyer':
      return <path d="M32 3 47 24 59 51 41 47 36 60H28l-5-13L5 51 17 24Z M20 36h24l-12-22Z" />;
    case 'carrier':
      return <path d="M21 6h22l8 43-19 10-19-10Z M24 15v31l8 5 8-5V15Z M6 28h14v16H6Z M44 28h14v16H44Z" />;
    case 'dreadnought':
      return <path d="M32 2 50 20 60 49 43 46 37 61H27l-6-15L4 49 14 20Z M20 30h24l-5-11H25Z M23 43h18l-9 9Z" />;
    case 'fighter-group':
      return (
        <>
          <path d="M18 8 28 31 18 56 11 39 4 42 12 29 4 17 11 20Z" />
          <path d="M46 8 60 17 52 29 60 42 53 39 46 56 36 31Z" />
          <path d="M32 15 40 33 32 52 24 33Z" />
        </>
      );
    case 'station':
      return (
        <>
          <path d="M32 8 46 16v32L32 56 18 48V16Z M32 19 26 23v18l6 4 6-4V23Z" />
          <path d="M30 1h4v14h-4ZM30 49h4v14h-4ZM1 30h14v4H1ZM49 30h14v4H49Z" />
        </>
      );
    case 'cruiser':
    default:
      return <path d="M32 3 46 28 56 55 40 49 32 61 24 49 8 55 18 28Z M25 31h14l-7-17Z" />;
  }
}
export function PreTurnChecklist({
  snapshot,
  ownedShipIds,
  damageUndoLabel,
  onUndoDamage,
}: {
  snapshot: MatchSnapshot;
  ownedShipIds: Set<string>;
  damageUndoLabel?: string;
  onUndoDamage: () => void;
}) {
  const items = buildPreTurnChecklist(snapshot, ownedShipIds);
  const blockers = items.filter((item) => item.severity === 'blocker').length;
  const warnings = items.filter((item) => item.severity === 'warning').length;

  return (
    <section className="preturn-checklist" aria-label="Pre-turn checklist">
      <div className="checklist-head">
        <div>
          <span className="label">Pre-turn checklist</span>
          <h3>{blockers === 0 ? 'Clear to proceed' : `${blockers} blocker${blockers === 1 ? '' : 's'}`}</h3>
        </div>
        <div className="checklist-actions">
          <span>{warnings} warn</span>
          <button className="ghost" type="button" disabled={!damageUndoLabel} onClick={onUndoDamage}>
            Undo {damageUndoLabel ? damageUndoLabel.slice(0, 10) : 'Damage'}
          </button>
        </div>
      </div>
      <div className="checklist-items">
        {items.map((item) => (
          <span key={item.id} className={`checklist-item ${item.severity}`}>
            {item.text}
          </span>
        ))}
      </div>
    </section>
  );
}
/**
 * @param rules The table's own profile, or undefined before the first snapshot lands. Every number
 * this form states comes off it: a screen ceiling of 3 and two tooltips full of published rolls were
 * this app telling the table what their rulebook says.
 */
export function ShipProfileFields({ form, onChange, rules }: { form: ShipForm; onChange: (form: ShipForm) => void; rules?: RulesProfile }) {
  const maxScreenLevel = rules?.maxScreenLevel ?? 0;
  return (
    <div className="profile-fields">
      <div className="preset-strip">
        <span className="label">Presets</span>
        <div className="quick-actions">
          {shipPresets.map((preset) => (
            <button
              key={preset.label}
              className="ghost"
              type="button"
              onClick={() => onChange({
                ...form,
                ...preset.patch,
                weapons: preset.patch.weapons?.map((weapon) => ({ ...weapon, id: newId() })) ?? form.weapons,
              })}
            >
              {preset.label}
            </button>
          ))}
        </div>
      </div>
      <label>
        Ship name
        <input value={form.name} onChange={(event) => onChange({ ...form, name: event.target.value })} />
      </label>
      <label>
        Class
        <input value={form.className} onChange={(event) => onChange({ ...form, className: event.target.value })} />
      </label>
      <label>
        Ship icon
        <select value={form.iconKey} onChange={(event) => onChange({ ...form, iconKey: event.target.value as ShipIconKey })}>
          {shipIconOptions.map((option) => <option key={option.key} value={option.key}>{option.label}</option>)}
        </select>
      </label>
      <label>
        Thrust
        <input type="number" min="0" max="20" value={form.thrustRating} onChange={(event) => onChange({ ...form, thrustRating: wholeNumberFrom(event.target.value, 0, 0, 20) })} />
      </label>
      <label>
        Start velocity
        <input type="number" min="0" value={form.currentVelocity} onChange={(event) => onChange({ ...form, currentVelocity: wholeNumberFrom(event.target.value, 0, 0, 999) })} />
      </label>
      <label>
        Course
        <input type="number" min="1" max="12" value={form.currentCourse} onChange={(event) => onChange({ ...form, currentCourse: wholeNumberFrom(event.target.value, 1, 1, 12) })} />
      </label>
      <label>
        Position X
        <input type="number" min="0" value={form.positionX} onChange={(event) => onChange({ ...form, positionX: numberFrom(event.target.value, 0, 0, 144) })} />
      </label>
      <label>
        Position Y
        <input type="number" min="0" value={form.positionY} onChange={(event) => onChange({ ...form, positionY: numberFrom(event.target.value, 0, 0, 96) })} />
      </label>
      <label>
        Hull boxes
        <input type="number" min="1" max="80" value={form.hullMax} onChange={(event) => onChange({ ...form, hullMax: wholeNumberFrom(event.target.value, 1, 1, 80) })} />
      </label>
      <label>
        Armor boxes
        <input type="number" min="0" max="40" value={form.armorMax} onChange={(event) => onChange({ ...form, armorMax: wholeNumberFrom(event.target.value, 0, 0, 40) })} />
      </label>
      <label>
        Screens
        <input type="number" min="0" max={maxScreenLevel} value={form.screenRating} onChange={(event) => onChange({ ...form, screenRating: wholeNumberFrom(event.target.value, 0, 0, maxScreenLevel) })} />
      </label>
      <label title="Each working fire control system directs fire at one target, and each rolls separately at a threshold check.">
        Firecons
        <input type="number" min="0" max="6" value={form.fireControlMax} onChange={(event) => onChange({ ...form, fireControlMax: wholeNumberFrom(event.target.value, 0, 0, 6) })} />
      </label>
      <label title={pointDefenceNote(rules)}>
        Point defence
        <input type="number" min="0" max="12" value={form.pointDefenseSystems} onChange={(event) => onChange({ ...form, pointDefenseSystems: wholeNumberFrom(event.target.value, 0, 0, 12) })} />
      </label>
      <label title="Fighter bays. Each holds one group; a carrier must hold course and speed to launch or recover, and a bay lost to a threshold check takes the group inside it.">
        Fighter bays
        <input type="number" min="0" max="12" value={form.fighterBays} onChange={(event) => onChange({ ...form, fighterBays: wholeNumberFrom(event.target.value, 0, 0, 12) })} />
      </label>
      <label title={damageControlNote(rules)}>
        Damage control
        <input type="number" min="0" max="12" value={form.damageControlParties} onChange={(event) => onChange({ ...form, damageControlParties: wholeNumberFrom(event.target.value, 0, 0, 12) })} />
      </label>
      <label>
        Points (NPV)
        <input type="number" min="0" max="99999" value={form.pointsValue} onChange={(event) => onChange({ ...form, pointsValue: wholeNumberFrom(event.target.value, 0, 0, 99999) })} />
      </label>
      {isFighterGroupForm(form) ? (
        <>
          <label>
            Fighter status
            <select value={form.fighterStatus} onChange={(event) => onChange({ ...form, fighterStatus: event.target.value as FighterStatus })}>
              {fighterStatuses.map((status) => <option key={status}>{status}</option>)}
            </select>
          </label>
          {/* Endurance and reach are the group's own, off the player's list. These boxes read
              `form.fighterEnduranceMax || 6` and `form.fighterMaxRange || 24`, so a group nobody
              had rated showed six turns and a reach of 24 while its state held zero and the server
              - which was fixed to stop inventing exactly those two numbers - was sent nothing. The
              `min="1"` was the other half: a table that plays no endurance rule and types 0 had it
              clamped up to 1 and written down. Zero is unentered here, and the server agrees. */}
          <label>
            Endurance used
            <input type="number" min="0" max={form.fighterEnduranceMax} value={form.fighterEnduranceUsed} onChange={(event) => onChange({ ...form, fighterEnduranceUsed: wholeNumberFrom(event.target.value, 0, 0, form.fighterEnduranceMax) })} />
          </label>
          <label>
            Endurance max
            <input type="number" min="0" max="24" value={form.fighterEnduranceMax} onChange={(event) => onChange({ ...form, fighterEnduranceMax: wholeNumberFrom(event.target.value, 0, 0, 24) })} />
          </label>
          <label>
            Max range
            <input type="number" min="0" max="120" value={form.fighterMaxRange} onChange={(event) => onChange({ ...form, fighterMaxRange: wholeNumberFrom(event.target.value, 0, 0, 120) })} />
          </label>
        </>
      ) : null}
      <div className="weapon-editor">
        <div className="section-head compact">
          <div>
            <span className="label">Weapons</span>
            <h3>{form.weapons.length} {form.weapons.length === 1 ? 'mount' : 'mounts'}</h3>
          </div>
          <button
            className="ghost"
            type="button"
            onClick={() => onChange({
              ...form,
              weapons: [...form.weapons, newWeaponMount()],
            })}
          >
            Add
          </button>
        </div>
        {form.weapons.map((weapon) => (
          <div className="weapon-row" key={weapon.id}>
            <label>
              Name
              <input value={weapon.name} onChange={(event) => onChange(updateWeapon(form, weapon.id, { name: event.target.value }))} />
            </label>
            <label>
              Type
              <select
                value={weapon.kind}
                onChange={(event) => {
                  // Changing the kind changes which procedure the engine runs, and nothing else.
                  // It used to also clamp the reach to a published maximum and force a torpedo to
                  // one shot - both numbers this app is not the source of. The player's figures
                  // stay as typed; the rules profile they entered is what the server holds them to.
                  onChange(updateWeapon(form, weapon.id, { kind: event.target.value as WeaponKind }));
                }}
              >
                {weaponKinds.map((option) => <option key={option.key} value={option.key}>{option.label}</option>)}
              </select>
            </label>
            <label>
              {weapon.kind === 'PulseTorpedo' ? 'Tubes' : 'Dice'}
              <input type="number" min="1" max="12" value={weapon.attackDice} onChange={(event) => onChange(updateWeapon(form, weapon.id, { attackDice: wholeNumberFrom(event.target.value, 1, 1, 12) }))} />
            </label>
            <label>
              Range
              <input type="number" min="1" max="72" value={weapon.maxRange} onChange={(event) => onChange(updateWeapon(form, weapon.id, { maxRange: wholeNumberFrom(event.target.value, 1, 1, 72) }))} />
            </label>
            <div className="arc-toggles" role="group" aria-label={`${weapon.name} arcs`}>
              <span className="label">Arcs</span>
              <div className="arc-toggle-row">
                {firableArcs.map((arc) => {
                  const bears = weapon.arcs.includes(arc);
                  return (
                    <button
                      key={arc}
                      type="button"
                      className={bears ? 'arc-toggle on' : 'arc-toggle'}
                      aria-pressed={bears}
                      title={arcLabel(arc)}
                      onClick={() => onChange(updateWeapon(form, weapon.id, {
                        arcs: bears
                          ? weapon.arcs.filter((entry) => entry !== arc)
                          : firableArcs.filter((entry) => entry === arc || weapon.arcs.includes(entry)),
                      }))}
                    >
                      {arcAbbreviations[arc]}
                    </button>
                  );
                })}
              </div>
            </div>
            <label>
              Ammo
              <input type="number" min="0" max="99" value={weapon.ammoMax} onChange={(event) => onChange(updateWeapon(form, weapon.id, { ammoMax: wholeNumberFrom(event.target.value, 0, 0, 99), ammoUsed: Math.min(weapon.ammoUsed, wholeNumberFrom(event.target.value, 0, 0, 99)) }))} />
            </label>
            <label>
              Used
              <input type="number" min="0" max={weapon.ammoMax || 99} value={weapon.ammoUsed} onChange={(event) => onChange(updateWeapon(form, weapon.id, { ammoUsed: wholeNumberFrom(event.target.value, 0, 0, weapon.ammoMax || 99) }))} />
            </label>
            <label>
              Reload
              <input type="number" min="0" max="12" value={weapon.reloadTurns} onChange={(event) => onChange(updateWeapon(form, weapon.id, { reloadTurns: wholeNumberFrom(event.target.value, 0, 0, 12) }))} />
            </label>
            <button
              className="ghost"
              type="button"
              onClick={() => onChange({
                ...form,
                weapons: form.weapons.filter((item) => item.id !== weapon.id),
              })}
            >
              Remove
            </button>
          </div>
        ))}
      </div>
    </div>
  );
}
export function ShipEditor({ ship, rules, onSave, onCancel }: { ship: Ship; rules?: RulesProfile; onSave: (form: ShipForm) => void; onCancel: () => void }) {
  const [form, setForm] = useState<ShipForm>({
    fleetName: '',
    faction: '',
    fleetColor: '#47f1ff',
    name: ship.name,
    className: ship.className ?? '',
    iconKey: normalizeShipIconKey(ship.iconKey, ship.className),
    thrustRating: ship.thrustRating,
    currentVelocity: ship.currentVelocity,
    currentCourse: ship.currentCourse,
    positionX: ship.positionX,
    positionY: ship.positionY,
    hullMax: ship.hullMax,
    armorMax: ship.armorMax,
    screenRating: ship.screenRating,
    fireControlMax: ship.fireControlMax ?? 1,
    pointDefenseSystems: ship.pointDefenseSystems ?? 0,
    fighterBays: ship.fighterBays ?? 0,
    damageControlParties: ship.damageControlParties ?? 0,
    weapons: ship.weapons.length > 0 ? ship.weapons : [newWeaponMount()],
    fighterEnduranceMax: ship.fighterEnduranceMax,
    fighterEnduranceUsed: ship.fighterEnduranceUsed,
    fighterMaxRange: ship.fighterMaxRange,
    fighterStatus: ship.fighterStatus,
    homeCarrierShipId: ship.homeCarrierShipId ?? '',
    pointsValue: ship.pointsValue ?? 0,
  });

  // What the ship looked like when this editor opened. The form is seeded once and deliberately
  // never re-seeded, because re-seeding would throw away whatever the player is part-way through
  // typing. The cost of that is a lost update: saving writes the whole profile, including velocity,
  // position, hull and the entire weapon list, so a save made while the editor sat open would
  // silently revert anything that happened to the ship in the meantime - the opponent's fire
  // resolving, most obviously. Comparing against this tells us whether that is about to happen.
  const [openedWith] = useState(() => JSON.stringify(captureShipState(ship)));
  const drifted = JSON.stringify(captureShipState(ship)) !== openedWith;

  return (
    <section className="ship-editor" aria-label={`Edit ${ship.name}`}>
      <div className="section-head compact">
        <div>
          <span className="label">Profile editor</span>
          <h3>{ship.name}</h3>
        </div>
      </div>
      <ShipProfileFields form={form} onChange={setForm} rules={rules} />
      {drifted ? (
        <p className="editor-drift" role="alert">
          {ship.name} changed while this was open - most likely damage resolving. Saving now writes
          the numbers as they were when you started editing and will undo those changes.
        </p>
      ) : null}
      <div className="card-actions">
        <button
          onClick={() => {
            if (!drifted || window.confirm(
              `${ship.name} has changed since you opened this. Save anyway and overwrite those changes?`)) {
              onSave(form);
            }
          }}
        >Save Stats</button>
        <button className="ghost" onClick={onCancel}>Cancel</button>
      </div>
    </section>
  );
}

/**
 * The parts of a ship a profile save would overwrite. Compared as a whole rather than field by
 * field, because the question is only ever "did anything change", never "what".
 */
function captureShipState(ship: Ship) {
  return {
    currentVelocity: ship.currentVelocity,
    currentCourse: ship.currentCourse,
    positionX: ship.positionX,
    positionY: ship.positionY,
    hullMax: ship.hullMax,
    hullDamage: ship.hullDamage,
    armorMax: ship.armorMax,
    armorDamage: ship.armorDamage,
    screenRating: ship.screenRating,
    screenDamage: ship.screenDamage,
    fireControlDamage: ship.fireControlDamage,
    driveDamage: ship.driveDamage,
    weapons: ship.weapons.map((weapon) => `${weapon.id}:${weapon.isDestroyed}:${weapon.ammoUsed}`),
  };
}
export function FiringConsole({
  ship,
  ships,
  ownedShipIds,
  draft,
  phase,
  rules,
  firingResults,
  onChange,
  onFire,
  volleyOpen,
  snapshotVersion,
  onFiringSolution,
  canEndFire,
  onCeaseFire,
}: {
  ship: Ship;
  ships: Ship[];
  ownedShipIds: Set<string>;
  draft: FiringDraft;
  phase: string;
  rules?: RulesProfile;
  firingResults: FiringResult[];
  onChange: (patch: Partial<FiringDraft>) => void;
  onFire: () => void;
  volleyOpen: boolean;
  snapshotVersion: number;
  onFiringSolution: (ship: Ship, targetShipId?: string, weaponId?: string, range?: number) => Promise<FiringSolution>;
  canEndFire: boolean;
  onCeaseFire: () => void;
}) {
  const targetOptions = firingTargetOptions(ship, ships, ownedShipIds);
  const weapon = ship.weapons.find((item) => item.id === draft.weaponId) ?? ship.weapons[0];
  const target = targetOptions.find((candidate) => candidate.id === draft.targetShipId) ?? targetOptions[0];
  const estimatedRange = target ? Math.max(1, Math.round(distanceBetweenShips(ship, target))) : null;
  const solution = useFiringSolution(
    ship,
    target?.id,
    weapon?.id,
    draft.range,
    snapshotVersion,
    phase === 'Firing',
    onFiringSolution,
  );
  // Naming a needle's system is a fact about the draft rather than about the rules, so it stays
  // here: the server cannot judge a system that has not been picked yet.
  const needsSystem = weapon?.kind === 'NeedleBeam' && !draft.targetSystem;
  const canFire = phase === 'Firing' && Boolean(solution?.canFire) && !needsSystem;
  const rangeStatus = weapon && estimatedRange
    ? estimatedRange <= weapon.maxRange ? `Estimated range ${estimatedRange}; in range.` : `Estimated range ${estimatedRange}; outside ${weapon.maxRange}.`
    : 'Pick a target and weapon.';
  // The table is the authority on distance, so a disagreement is said out loud, not enforced.
  const rangeDoubt = solution?.rangeDisagreesWithMap
    ? `Declared ${draft.range}, map measures ${solution.mapRange.toFixed(1)}. The table decides; fix the range or the ship positions if that gap is wrong.`
    : null;
  const fireStatus = needsSystem
    ? 'Name the system this needle is aimed at.'
    : solution?.blocker
      ? `${solution.blocker}`
      : rangeStatus;
  const spentShot = weapon ? firingResults.find((result) => result.attackerShipId === ship.id && result.weaponId === weapon.id) : undefined;

  return (
    <div className="firing-console card-module" aria-label={`${ship.name} firing controls`}>
      <span className="label module-title">Firing solution</span>
      <p className="constraint-line">{phase === 'Firing' ? `Weapons free. ${fireStatus}` : `Resolve movement to open firing. ${rangeStatus}`}</p>
      <label>
        Target
        <select value={draft.targetShipId} onChange={(event) => onChange({ targetShipId: event.target.value })}>
          {targetOptions.map((option) => (
            <option key={option.id} value={option.id}>{option.name}{ownedShipIds.has(option.id) ? ' - yours' : ''}</option>
          ))}
        </select>
      </label>
      <label>
        Weapon
        <select
          value={draft.weaponId}
          onChange={(event) => {
            onChange({ weaponId: event.target.value });
          }}
        >
          {ship.weapons.map((mount) => {
            const spent = firingResults.some((result) => result.attackerShipId === ship.id && result.weaponId === mount.id);
            const ammo = mount.ammoMax > 0 ? ` · ammo ${mount.ammoUsed}/${mount.ammoMax}` : '';
            const lostNote = mount.isDestroyed ? mount.isNeedleKilled ? ' · needled, beyond repair' : ' · knocked out' : spent ? ' · spent' : '';
            return <option key={mount.id} value={mount.id}>{mount.name} · {mount.attackDice}D/{mount.maxRange}{ammo}{lostNote}</option>;
          })}
        </select>
      </label>
      <div className="bearing-readout">
        <span className="label">Bearing</span>
        <strong>{solution?.targetArc ?? 'no target'}</strong>
        <small>{weapon ? describeArcs(weapon.arcs) : 'no mount'}</small>
        <small>{solution ? `${solution.workingFireControl} firecon${solution.workingFireControl === 1 ? '' : 's'}` : ''}</small>
        {solution?.toHitNumber ? <small>needs {solution.toHitNumber}+ to hit</small> : null}
        {weapon?.kind === 'NeedleBeam' && needleSystemNote(rules) ? <small>{needleSystemNote(rules)}</small> : null}
      </div>
      {weapon?.kind === 'NeedleBeam' ? (
        <label>
          Aim at
          <select
            value={draft.targetSystem ? `${draft.targetSystem}:${draft.targetSystemWeaponId ?? ''}` : ''}
            onChange={(event) => {
              const [kind, weaponId] = event.target.value.split(':');
              onChange({ targetSystem: kind || undefined, targetSystemWeaponId: weaponId || undefined });
            }}
          >
            <option value="">Pick a system</option>
            {(solution?.needleTargets ?? []).map((option) => (
              <option key={option.key} value={`${option.kind}:${option.weaponId ?? ''}`}>{option.label}</option>
            ))}
          </select>
        </label>
      ) : null}
      <label>
        Range
        <input type="number" min="1" max={weapon?.maxRange ?? 72} value={draft.range} onChange={(event) => onChange({ range: wholeNumberFrom(event.target.value, 1, 1, weapon?.maxRange ?? 72) })} />
      </label>
      <button
        className="ghost"
        type="button"
        disabled={!estimatedRange}
        onClick={() => estimatedRange ? onChange({ range: estimatedRange }) : undefined}
      >
        Use Map Range
      </button>
      <button disabled={!canFire} onClick={onFire}>Fire</button>
      {canEndFire ? (
        <button className="ghost volley-close" type="button" onClick={onCeaseFire}>{volleyOpen ? 'Done Firing' : 'Hold Fire'}</button>
      ) : null}
      {rangeDoubt ? <p className="constraint-line range-doubt">{rangeDoubt}</p> : null}
      {canEndFire ? (
        <p className="constraint-line">
          {volleyOpen
            ? 'Finishing rolls the threshold checks this fire earned and passes the turn.'
            : 'Holding fire uses this turn to fire and passes to the other player.'}
        </p>
      ) : null}
      {spentShot ? (
        <p className="constraint-line dice-readout">
          {spentShot.weaponKind === 'NeedleBeam'
            ? `Needed ${spentShot.toHitNumber}, rolled ${(spentShot.diceRolls ?? []).join(', ')}: ${spentShot.isHit ? 'system cut out.' : 'nothing hit.'}`
            : spentShot.weaponKind === 'PulseTorpedo'
              ? `Needed ${spentShot.toHitNumber}+, rolled ${(spentShot.diceRolls ?? []).join(', ')}${spentShot.isHit ? '' : ' and missed'} for ${spentShot.damage} damage (${spentShot.armorDamageApplied} armor, ${spentShot.hullDamageApplied} hull).`
              : `Rolled ${(spentShot.diceRolls ?? []).length > 0 ? (spentShot.diceRolls ?? []).join(', ') : 'no dice'}${spentShot.screenReduction > 0 ? ` - screens stopped ${spentShot.screenReduction}` : ''} for ${spentShot.damage} damage (${spentShot.armorDamageApplied} armor, ${spentShot.hullDamageApplied} hull).`}
        </p>
      ) : null}
    </div>
  );
}
export function CourseCompass({
  currentCourse,
  currentVelocity,
  thrustRating,
  draft,
  canEdit,
  onDraftChange,
}: {
  currentCourse: number;
  /**
   * What the ship is doing now, which is half of what the turn limit depends on: a ship at rest
   * that is not accelerating rotates to any heading for free. Without it this compass drew its
   * limits at half thrust around a stationary ship and the helm could not plot a legal rotation.
   */
  currentVelocity: number;
  thrustRating: number;
  draft: DraftOrder;
  canEdit: boolean;
  onDraftChange: (patch: Partial<DraftOrder>) => void;
}) {
  const selectedCourse = previewCourse(currentCourse, draft);
  const courses = Array.from({ length: 12 }, (_, index) => index + 1);
  const currentAngle = courseAngle(currentCourse);
  const selectedAngle = courseAngle(selectedCourse);
  const maxTurn = maxLegalTurn(thrustRating, draft.velocityDelta, currentVelocity);
  const turnTotal = totalTurnSteps(draft);

  function updateFromPointer(event: PointerEvent<HTMLDivElement>) {
    if (!canEdit) {
      return;
    }

    if (!event.currentTarget.hasPointerCapture(event.pointerId)) {
      try {
        event.currentTarget.setPointerCapture(event.pointerId);
      } catch {
        return;
      }
    }
    const targetCourse = courseFromPoint(event.currentTarget, event.clientX, event.clientY);
    onDraftChange(turnPatchForCourse(currentCourse, targetCourse, maxTurn, draft.turnDirection));
  }

  function updateFromKeyboard(event: KeyboardEvent<HTMLDivElement>) {
    if (!canEdit) {
      return;
    }

    if (event.key === 'Home') {
      event.preventDefault();
      onDraftChange(turnPatchFromManeuvers([]));
      return;
    }

    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') {
      return;
    }

    event.preventDefault();
    const direction: TurnDirection = event.key === 'ArrowLeft' ? 'Port' : 'Starboard';
    const sameDirection = draft.turnDirection === direction;
    const turnSteps = Math.min(maxTurn, sameDirection ? turnTotal + 1 : 1);
    onDraftChange(turnPatchFromManeuvers(turnSteps === 0 ? [] : [{ direction, steps: turnSteps }]));
  }

  return (
    <div className="compass" aria-label={`Current course ${currentCourse}, selected course ${selectedCourse}, maximum turn ${maxTurn}`}>
      <div className="helm-meta">
        <span>PORT LIMIT {wrapCourse(currentCourse - maxTurn)}</span>
        <strong>HELM {selectedCourse}</strong>
        <span>STARBOARD LIMIT {wrapCourse(currentCourse + maxTurn)}</span>
      </div>
      <div
        className={canEdit ? 'compass-face interactive' : 'compass-face'}
        role={canEdit ? 'slider' : 'img'}
        tabIndex={canEdit ? 0 : undefined}
        aria-label="Course change helm"
        aria-valuemin={0}
        aria-valuemax={maxTurn}
        aria-valuenow={turnTotal}
        aria-valuetext={`${formatTurnSequence(draft)}; selected course ${selectedCourse}`}
        onPointerDown={updateFromPointer}
        onPointerMove={(event) => {
          if (event.buttons === 1) {
            updateFromPointer(event);
          }
        }}
        onKeyDown={updateFromKeyboard}
      >
        {courses.map((course) => (
          <span
            key={course}
            className={[
              'course-mark',
              course === currentCourse ? 'current' : '',
              course === selectedCourse ? 'selected' : '',
            ].join(' ')}
            style={{ '--angle': `${courseAngle(course)}deg` } as CSSProperties}
          >
            {course}
          </span>
        ))}
        <span className="turn-limit port-limit" style={{ transform: `rotate(${courseAngle(wrapCourse(currentCourse - maxTurn))}deg)` }} />
        <span className="turn-limit starboard-limit" style={{ transform: `rotate(${courseAngle(wrapCourse(currentCourse + maxTurn))}deg)` }} />
        <span className="bearing current-bearing" style={{ transform: `rotate(${currentAngle}deg)` }} />
        <span className="bearing selected-bearing" style={{ transform: `rotate(${selectedAngle}deg)` }}>
          <span />
        </span>
        <span className="ship-glyph" style={{ transform: `rotate(${currentAngle}deg)` }} aria-hidden="true" />
        <span className="compass-core">
          <small>{formatTurnSequence(draft)}</small>
          <strong>{turnTotal}</strong>
          <em>MAX {maxTurn}</em>
        </span>
      </div>
      <p className="helm-hint">{canEdit ? 'Drag, tap, or use left/right arrows.' : 'Opponent helm is read-only.'}</p>
    </div>
  );
}
/// Damage control between turns: put parties on jobs, then run them. What one party repairs on, how
/// far each extra lowers it, and how many may pile onto one job are all the table's own numbers off
/// their profile - this panel reads them and states none of its own.
export function DamageControlPanel({ ship, rules, onRepair }: { ship: Ship; rules?: RulesProfile; onRepair: (jobs: RepairJob[]) => void }) {
  const [assignments, setAssignments] = useState<Record<string, number>>({});
  const parties = ship.damageControlParties ?? 0;
  const jobs = ship.repairableSystems;
  const spent = Object.values(assignments).reduce((total, count) => total + count, 0);
  const needleLosses = ship.weapons.filter((mount) => mount.isNeedleKilled).length;

  if (parties === 0) {
    return (
      <div className="damage-grid card-module">
        <span className="label module-title">Damage control parties</span>
        <p className="privacy">No parties left aboard{needleLosses > 0 ? ', and needled systems could not be repaired anyway' : ''}.</p>
      </div>
    );
  }

  if (jobs.length === 0) {
    return (
      <div className="damage-grid card-module">
        <span className="label module-title">Damage control parties</span>
        <p className="privacy">
          {parties} part{parties === 1 ? 'y' : 'ies'} standing by; nothing repairable is down
          {needleLosses > 0 ? `. ${needleLosses} needled system${needleLosses === 1 ? '' : 's'} beyond help` : ''}.
        </p>
      </div>
    );
  }

  // The cap was a hardcoded 3 - `MaxPartiesPerJob`, and the player's. A table whose profile allows
  // four was stopped at three by a number that appears nowhere in it.
  const perJobCap = partiesPerJobCap(rules, parties);
  const step = (key: string, delta: number) => setAssignments((current) => {
    const next = Math.max(0, Math.min(perJobCap, (current[key] ?? 0) + delta));
    const others = Object.entries(current).reduce((total, [id, count]) => id === key ? total : total + count, 0);
    return others + next > parties ? current : { ...current, [key]: next };
  });

  return (
    <div className="damage-grid card-module" aria-label={`${ship.name} damage control`}>
      <span className="label module-title">Damage control parties</span>
      <p className="privacy">{spent} of {parties} assigned.{repairOddsNote(rules) ? ` ${repairOddsNote(rules)}` : ''}</p>
      {jobs.map((job) => (
        <div className="repair-row" key={job.key}>
          <span>{job.label}</span>
          <div className="quick-actions">
            <button className="ghost" type="button" aria-label={`Fewer parties on ${job.label}`} onClick={() => step(job.key, -1)}>-</button>
            <strong>{assignments[job.key] ?? 0}</strong>
            <button className="ghost" type="button" aria-label={`More parties on ${job.label}`} onClick={() => step(job.key, 1)}>+</button>
          </div>
        </div>
      ))}
      <button
        type="button"
        disabled={spent === 0}
        onClick={() => {
          onRepair(jobs
            .filter((job) => (assignments[job.key] ?? 0) > 0)
            .map((job) => ({ kind: job.kind, weaponId: job.weaponId, parties: assignments[job.key] })));
          setAssignments({});
        }}
      >
        Run Damage Control
      </button>
      {needleLosses > 0 ? (
        <p className="privacy">{needleLosses} system{needleLosses === 1 ? '' : 's'} cut out by needle fire cannot be repaired.</p>
      ) : null}
    </div>
  );
}
export function DamageControl({ label, value, max, rows, disabled, onChange }: {
  label: string;
  value: number;
  max: number;
  rows?: number[];
  disabled?: boolean;
  onChange: (value: number) => void;
}) {
  return (
    <div className="damage-control">
      <DamageMeter label={label} value={value} max={max} rows={rows} />
      <div className="damage-buttons">
        <button type="button" aria-label={`Reduce ${label} damage`} disabled={disabled} onClick={() => onChange(Math.max(0, value - 1))}>-</button>
        <button type="button" aria-label={`Add ${label} damage`} disabled={disabled} onClick={() => onChange(Math.min(max, value + 1))}>+</button>
      </div>
    </div>
  );
}
export function DamageMeter({ label, value, max, rows }: { label: string; value: number; max: number; rows?: number[] }) {
  const cells = Array.from({ length: max }, (_, index) => index < value);
  // A row boundary is where a threshold check fires, so mark the last box of each row but the last.
  const rowEnds = new Set<number>();
  if (rows && rows.length > 1) {
    let box = 0;
    rows.slice(0, -1).forEach((row) => {
      box += row;
      rowEnds.add(box - 1);
    });
  }

  return (
    <div className="damage-meter">
      <div className="damage-meter-head">
        <span>{label}</span>
        <strong>{value}/{max}</strong>
      </div>
      <div className="damage-cells">
        {cells.map((isDamaged, index) => (
          <span
            key={index}
            className={`${isDamaged ? 'damaged' : ''}${rowEnds.has(index) ? ' row-end' : ''}`.trim()}
            title={rowEnds.has(index) ? 'End of hull row - threshold check' : undefined}
          />
        ))}
      </div>
    </div>
  );
}

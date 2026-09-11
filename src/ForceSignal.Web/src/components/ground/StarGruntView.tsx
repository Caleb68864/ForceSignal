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

import { useEffect, useRef, useState } from 'react';
import { starGruntGameKey } from '../../constants.ts';
import { ApiRequestError, newId, readStored, writeStorage } from '../../lib/api.ts';
import { fromForceFile, toForceFile } from '../../lib/forceIo.ts';
import { downloadText, wholeNumberFrom } from '../../lib/format.ts';
// The ladder the API accepts, not this screen's idea of it. See groundVocabulary.ts.
import { qualityLadder as ladder } from '../../lib/groundVocabulary.ts';
import { normalizeGameHandle } from '../../lib/normalize.ts';
import * as api from '../../lib/starGruntApi.ts';
import type { StarGruntProfileDraft } from '../../lib/starGruntProfile.ts';
import {
  emptyStarGruntProfileDraft,
  starGruntProfileIsEmpty,
  starGruntProfileSummary,
  toStarGruntProfileInput,
  visibleBandRows,
} from '../../lib/starGruntProfile.ts';
import type { GameHandle, StarGruntSnapshot, StarGruntUnit } from '../../types.ts';
const covers = ['None', 'Soft', 'Hard'];
// Only the actions that are pure declarations live here. Anything that rolls or names another unit
// - firing, shaking off suppression, rallying, reorganising - has a command and a button of its own,
// because this route carries no die source and no second unit.
const unarmedActions = ['Move', 'Dash', 'Observe', 'Communicate', 'GoInPosition'];
const commandLadder = ['Squad', 'Platoon', 'Company', 'Battalion', 'Regiment'];

/** Where a command level sits on the ladder, for deciding who may rally whom. */
function commandRank(level: string) {
  const rank = commandLadder.indexOf(level);
  return rank < 0 ? 0 : rank;
}

type UnitForm = {
  name: string;
  side: string;
  qualityDie: number;
  leadershipValue: number;
  figures: number;
  armourDie: number;
  weaponName: string;
  impactDie: number;
  fatigue: string;
  level: string;
};

type ShotForm = {
  targetId: string;
  weaponName: string;
  firepowerDie: number;
  supportWeapons: string[];
  distanceInches: number;
  cover: string;
  inPosition: boolean;
};

/**
 * What the three forms on this screen open on, hoisted out of the component so the content policy
 * can be held to by a test rather than by a caption.
 *
 * `lib/contentPolicy.test.ts` walks these. It could not before - they were `useState` seeds inside
 * the component and nothing outside could see them - and that gap is why an add-a-squad panel
 * captioned "No stats are supplied here" opened on a whole record card.
 *
 * Zero is this screen's spelling of "not entered", the same as the ship form's. Every die select
 * carries an unentered option so a form that has been filled in nowhere does not *look* filled in,
 * and the server refuses a die that is not on the ladder rather than choosing one.
 */
export const newUnitForm: UnitForm = {
  name: 'Alpha Squad',
  side: 'blue',
  qualityDie: 0,
  leadershipValue: 0,
  figures: 0,
  armourDie: 0,
  weaponName: 'Rifles',
  impactDie: 0,
  fatigue: 'Fresh',
  level: 'Squad',
};

/** What the fire panel opens on. The firepower die is off the player's own table, not this one. */
export const newShotForm: ShotForm = {
  targetId: '',
  weaponName: '',
  firepowerDie: 0,
  supportWeapons: [],
  distanceInches: 0,
  cover: 'None',
  inPosition: false,
};

/** What the close-assault panel opens on. */
export const newAssaultForm = {
  defenderId: '',
  terror: false,
  // Counted on the table rather than read off a rulebook: how many figures paired off, and how
  // many went down. Neither has a meaningful zero - the engine refuses an assault of no pairs and
  // a settle-up with nobody down - so these are the two exemptions this screen argues for.
  pairs: 1,
  threatLevel: 0,
  attackerShift: 0,
  defenderShift: 0,
  downed: 1,
  wonTheAssault: true,
  // Blank rather than typical. These started life as the published bands, which is the same
  // mistake as writing them into the engine - a default that happens to be somebody's numbers is
  // still those numbers, shipped.
  deadUpTo: 0,
  woundedUpTo: 0,
  // The die those two bands are read against. It was not on this form at all and the engine threw
  // a D6 regardless, so a table whose chart is written for a D10 had its top band made unreachable.
  fateDie: 0,
  defendersInCover: true,
  // Whether the men on each side are in power armour, which doubles a melee score after the roll.
  // The contract carried these, `StarGruntGameService` passed them, and `CloseAssault.Fight`
  // applies them - and this screen, the only caller of `fightMelee` anywhere, sent a literal false
  // for both. So a table fielding power-armoured troopers fought every melee at half strength and
  // nothing on screen said the option existed.
  //
  // False is the right opening value and is not a content-policy default: it is "these men are not
  // in power armour", which is a fact about the figures on the table rather than a number off
  // anybody's card - the same argument `defendersInCover` and `terror` already make.
  attackerPowerArmour: false,
  defenderPowerArmour: false,
};

/**
 * The threat level the confidence and reaction tests open on.
 *
 * The caption beside this control has always said "the threat level is the one your own table gives
 * the event", and the control opened on 2 - so the app supplied one anyway, and a player who took a
 * test without touching it tested against a number off nobody's table.
 */
export const newThreatLevel = 0;

/** The threat levels a player may pick, with the unentered one this app opens on. */
const threatLevels = [0, 1, 2, 3, 4, 5, 6];

/**
 * What a die select says before anybody has picked one.
 *
 * Blanking a form is only half of it. A `<select>` holding a value none of its options carry shows
 * whichever option happens to be first, so a quality select seeded at zero over a ladder of
 * 4 to 12 would read "D4" while sending nothing - the form would look filled in and be empty, which
 * is worse than the number it replaced. So "not entered" is a value the control can actually hold.
 */
const unentered = 0;

export function StarGruntView() {
  // The game this device started, kept across a refresh. Read back on mount below.
  const [handle, setHandle] = useState<GameHandle | null>(() => readStored(starGruntGameKey, normalizeGameHandle));
  const [snapshot, setSnapshot] = useState<StarGruntSnapshot | null>(null);
  const [message, setMessage] = useState('Start a game to begin.');
  const [messageIsError, setMessageIsError] = useState(false);
  const [busy, setBusy] = useState(false);
  // The ref is what actually stops a double tap: two clicks in the same frame both read the old
  // state, and `disabled={busy}` only takes effect after the re-render.
  const busyRef = useRef(false);
  const [gameName, setGameName] = useState('Hill 43');
  const [unitForm, setUnitForm] = useState<UnitForm>(newUnitForm);
  const fileInput = useRef<HTMLInputElement | null>(null);
  const [importSide, setImportSide] = useState('blue');
  // Read off the player's own threat table, which this app does not ship.
  const [threatLevel, setThreatLevel] = useState(newThreatLevel);
  const [assault, setAssault] = useState(newAssaultForm);
  const [shot, setShot] = useState<ShotForm>(newShotForm);
  // The range table off the user's own rulebook. Starts empty and stays empty unless they read it
  // in. There is no fallback behind it - a shot that needs an entry nobody made is refused, naming
  // it - which is why the form says so rather than letting them find out mid-firefight.
  const [profile, setProfile] = useState<StarGruntProfileDraft>(emptyStarGruntProfileDraft);

  /** Sets one of the range table's plain numbers, keeping whatever was typed as typed. */
  function setProfileNumber(
    field: 'effectiveBands' | 'softCoverShift' | 'hardCoverShift' | 'inPositionShift' | 'meleeCoverShift',
    text: string,
  ) {
    setProfile((current) => ({ ...current, [field]: text }));
  }

  /** Sets one row of one of the range table's two tables, or clears it. */
  function setProfileRow(table: 'bandInches' | 'rangeDice', key: string, text: string) {
    setProfile((current) => ({ ...current, [table]: { ...current[table], [key]: text } }));
  }

  function say(text: string) {
    setMessage(text);
    setMessageIsError(false);
  }

  function fail(error: unknown) {
    // A refusal is the server's sentence, shown as written rather than reworded here.
    setMessage(error instanceof ApiRequestError ? error.message : 'That did not work.');
    setMessageIsError(true);
  }

  /**
   * Runs a command and keeps whatever the server said about it, refusing to start a second while
   * the first is still going.
   */
  async function run(work: () => Promise<StarGruntSnapshot>, note?: string) {
    if (busyRef.current) {
      return;
    }

    busyRef.current = true;
    setBusy(true);
    try {
      setSnapshot(await work());
      if (note) {
        say(note);
      }
    } catch (error) {
      fail(error);
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  }

  async function start() {
    if (busyRef.current) {
      return;
    }

    busyRef.current = true;
    setBusy(true);
    try {
      const created = await api.createGame(gameName, toStarGruntProfileInput(profile));
      const next = { gameId: created.gameId, token: created.token };
      writeStorage(starGruntGameKey, next);
      setHandle(next);
      setSnapshot(created.snapshot);
      say(`Started ${created.snapshot.name}. Add a squad a side.`);
    } catch (error) {
      fail(error);
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  }

  function leave() {
    if (window.confirm('Leave this game on this device? It stays on the server, but this device will not find it again.')) {
      localStorage.removeItem(starGruntGameKey);
      setHandle(null);
      setSnapshot(null);
      say('Start a game to begin.');
    }
  }

  // A stored game is reopened on mount. One the server no longer has, or no longer lets this
  // device into, is forgotten rather than left to fail every action.
  useEffect(() => {
    if (!handle || snapshot) {
      return;
    }

    let cancelled = false;
    api.readGame(handle)
      .then((next) => {
        if (!cancelled) {
          setSnapshot(next);
          setMessage(`Reopened ${next.name}.`);
          setMessageIsError(false);
        }
      })
      .catch((error: unknown) => {
        if (cancelled) {
          return;
        }

        if (error instanceof ApiRequestError && [401, 403, 404].includes(error.status)) {
          localStorage.removeItem(starGruntGameKey);
          setHandle(null);
          setMessage('The game this device last played is no longer available. Start a new one.');
        } else {
          setMessage(error instanceof ApiRequestError ? error.message : 'That did not work.');
        }
        setMessageIsError(true);
      });
    return () => {
      cancelled = true;
    };
  }, [handle, snapshot]);

  if (!handle || !snapshot) {
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
        <fieldset>
          <legend>Range table</legend>
          <p className="privacy">
            The numbers off your own rulebook's range page. This app ships none of them and there is
            no fallback: a shot that needs an entry you have not made is refused, and says which. You
            only need what your table will use - one kind of troops shooting across open ground needs
            one band width and the rows it shoots at. It can only be set when the game is started.
          </p>
          <div className="row">
            {ladder.map((die) => (
              <label key={`band-${die}`}>
                Band for D{die} troops, inches
                <input
                  inputMode="numeric"
                  placeholder="Not entered"
                  value={profile.bandInches[String(die)] ?? ''}
                  onChange={(event) => setProfileRow('bandInches', String(die), event.target.value)}
                />
              </label>
            ))}
          </div>
          <div className="row">
            {/* One row per band filled in, and one more: how many bands a page has is its own. */}
            {visibleBandRows(profile).map((band) => (
              <label key={`range-${band}`}>
                Range die, {band} band{band === 1 ? '' : 's'} out
                <select
                  value={profile.rangeDice[String(band)] ?? ''}
                  onChange={(event) => setProfileRow('rangeDice', String(band), event.target.value)}
                >
                  <option value="">Not entered</option>
                  {ladder.map((die) => <option key={die} value={String(die)}>D{die}</option>)}
                </select>
              </label>
            ))}
          </div>
          <div className="row">
            <label title="How many bands out small arms still have an effective shot at a target in the open.">
              Bands of effective range
              <input
                inputMode="numeric"
                placeholder="Not entered"
                value={profile.effectiveBands}
                onChange={(event) => setProfileNumber('effectiveBands', event.target.value)}
              />
            </label>
            <label title="Rungs soft cover moves the range die and the armour die up. Zero is an answer.">
              Soft cover, rungs
              <input
                inputMode="numeric"
                placeholder="Not entered"
                value={profile.softCoverShift}
                onChange={(event) => setProfileNumber('softCoverShift', event.target.value)}
              />
            </label>
            <label title="Rungs hard cover moves the range die and the armour die up. Zero is an answer.">
              Hard cover, rungs
              <input
                inputMode="numeric"
                placeholder="Not entered"
                value={profile.hardCoverShift}
                onChange={(event) => setProfileNumber('hardCoverShift', event.target.value)}
              />
            </label>
            <label title="Rungs a target settled into its position adds, on top of any cover.">
              Dug in, rungs
              <input
                inputMode="numeric"
                placeholder="Not entered"
                value={profile.inPositionShift}
                onChange={(event) => setProfileNumber('inPositionShift', event.target.value)}
              />
            </label>
            <label title="Rungs cover is worth to a defender in the first round of a melee.">
              Melee cover, rungs
              <input
                inputMode="numeric"
                placeholder="Not entered"
                value={profile.meleeCoverShift}
                onChange={(event) => setProfileNumber('meleeCoverShift', event.target.value)}
              />
            </label>
          </div>
        </fieldset>
        <button type="button" disabled={busy || Boolean(handle)} onClick={start}>
          {handle ? 'Reopening last game...' : 'Start Game'}
        </button>
        {handle ? <button className="ghost" type="button" onClick={leave}>Forget Last Game</button> : null}
        <p className="constraint-line" aria-live="polite" role={messageIsError ? 'alert' : undefined}>{message}</p>
      </section>
    );
  }

  const game = handle;
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
        {/*
          On every screen rather than only at setup, so a table arguing about a shot can read which
          numbers the game is settling it with. Warning-styled when there is nothing to settle one
          with, so the news arrives before a model is in somebody's hand.
        */}
        <p className={starGruntProfileIsEmpty(snapshot.profile) ? 'constraint-line warning' : 'constraint-line'}>
          Range table: {starGruntProfileSummary(snapshot.profile)}
        </p>
        <p className="constraint-line" aria-live="polite" role={messageIsError ? 'alert' : undefined}>{message}</p>
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
          <button className="ghost" type="button" disabled={busy} onClick={leave}>Leave Game</button>
        </div>
      </div>

      <div className="card-module">
        <span className="label module-title">Confidence test</span>
        <p className="constraint-line">
          Taken the moment something bad happens, to whichever unit it happened to - not an action, and
          not only on its own go. The threat level is the one your own table gives the event.
        </p>
        <label>
          Threat level
          <select value={threatLevel} onChange={(event) => setThreatLevel(Number(event.target.value))}>
            {threatLevels.map((level) => (
              <option key={level} value={level}>{level === unentered ? 'Not entered' : level}</option>
            ))}
          </select>
        </label>
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
            onTest={() => run(
              () => api.confidenceTest(game, unit.id, threatLevel),
              `${unit.name} tested its nerve.`,
            )}
            onToggleDisorganised={() => run(
              () => api.setDisorganised(game, unit.id, !unit.isDisorganised),
              `${unit.name} is ${unit.isDisorganised ? 'back in order' : 'disorganised'}.`,
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
              className="ghost"
              type="button"
              disabled={busy}
              onClick={() => run(
                () => api.setLeavesCover(game, activating.id, !activating.nextMoveLeavesCover),
                activating.nextMoveLeavesCover ? 'Move is under cover.' : 'Move leaves cover.',
              )}
            >
              {activating.nextMoveLeavesCover ? 'Move Stays In Cover' : 'Move Leaves Cover'}
            </button>
            {activating.nextMoveLeavesCover && !activating.reactionTestCleared ? (
              <button
                type="button"
                disabled={busy}
                onClick={() => run(
                  () => api.reactionTest(game, activating.id, threatLevel),
                  `${activating.name} steeled itself.`,
                )}
              >
                Reaction Test
              </button>
            ) : null}
            {activating.isDisorganised ? (
              <button
                type="button"
                disabled={busy}
                onClick={() => run(() => api.reorganise(game, activating.id), `${activating.name} reorganised.`)}
              >
                Reorganise
              </button>
            ) : null}
            {snapshot.units
              // Rallying comes from above, so only subordinates on the same side are offered.
              .filter((other) => other.id !== activating.id
                && other.side === activating.side
                && commandRank(other.level) < commandRank(activating.level))
              .map((other) => (
                <button
                  key={`rally-${other.id}`}
                  className="ghost"
                  type="button"
                  disabled={busy}
                  onClick={() => run(() => api.rally(game, activating.id, other.id), `Rallying ${other.name}.`)}
                >
                  Rally {other.name}
                </button>
              ))}
            {activating.suppressionMarkers > 0 ? (
              <button
                type="button"
                disabled={busy}
                onClick={() => run(
                  () => api.removeSuppression(game, activating.id),
                  `${activating.name} tried to get its head up.`,
                )}
              >
                Get Heads Up ({activating.suppressionMarkers} pinned)
              </button>
            ) : null}
            <button
              type="button"
              disabled={busy}
              onClick={() => run(() => api.endActivation(game), `${activating.name} is done.`)}
            >
              End Activation
            </button>
          </div>

          <div className="card-module" aria-label="Close assault">
            <span className="label module-title">Close assault</span>
            <p className="constraint-line">
              A charge spends the whole activation. Threat levels, weapon shifts and the bands that
              read a downed figure all come off your own tables. What this works out is the odds the
              defenders face, and it will not let a unit that has lost its nerve charge at all. Who
              fights whom is yours to pair off - the attacker takes one each, the defender allocates
              the rest.
            </p>
            <label>
              Target
              <select
                value={assault.defenderId || targets[0]?.id || ''}
                onChange={(event) => setAssault((current) => ({ ...current, defenderId: event.target.value }))}
              >
                {targets.map((target) => (
                  <option key={target.id} value={target.id}>{target.name} · {target.figuresAlive} figures</option>
                ))}
              </select>
            </label>
            <label title="Agreed between the players before the game, not something this app decides.">
              Terror
              <input
                type="checkbox"
                checked={assault.terror}
                onChange={(event) => setAssault((current) => ({ ...current, terror: event.target.checked }))}
              />
            </label>
            <label>
              Pairs
              <input
                type="number"
                min="1"
                max="20"
                value={assault.pairs}
                onChange={(event) => setAssault((current) => ({ ...current, pairs: wholeNumberFrom(event.target.value, 1, 1, 20) }))}
              />
            </label>
            <label title="What the charge asks of the attackers, off your own table.">
              Charge threat
              <input
                type="number"
                min="0"
                max="9"
                value={assault.threatLevel}
                onChange={(event) => setAssault((current) => ({ ...current, threatLevel: wholeNumberFrom(event.target.value, 0, 0, 9) }))}
              />
            </label>
            <label title="Die types the attacker's close-combat weapon is worth, off your own table.">
              Attacker shift
              <input
                type="number"
                min="0"
                max="4"
                value={assault.attackerShift}
                onChange={(event) => setAssault((current) => ({ ...current, attackerShift: wholeNumberFrom(event.target.value, 0, 0, 4) }))}
              />
            </label>
            <label title="Die types the defender's close-combat weapon is worth.">
              Defender shift
              <input
                type="number"
                min="0"
                max="4"
                value={assault.defenderShift}
                onChange={(event) => setAssault((current) => ({ ...current, defenderShift: wholeNumberFrom(event.target.value, 0, 0, 4) }))}
              />
            </label>
            <label title="Cover helps a defender in the first round only, once the attackers are in among them.">
              Defenders in cover
              <input
                type="checkbox"
                checked={assault.defendersInCover}
                onChange={(event) => setAssault((current) => ({ ...current, defendersInCover: event.target.checked }))}
              />
            </label>
            <label title="Power armour doubles a figure's score after the roll rather than shifting the die before it.">
              Attackers in power armour
              <input
                type="checkbox"
                checked={assault.attackerPowerArmour}
                onChange={(event) => setAssault((current) => ({ ...current, attackerPowerArmour: event.target.checked }))}
              />
            </label>
            <label title="Power armour doubles a figure's score after the roll rather than shifting the die before it.">
              Defenders in power armour
              <input
                type="checkbox"
                checked={assault.defenderPowerArmour}
                onChange={(event) => setAssault((current) => ({ ...current, defenderPowerArmour: event.target.checked }))}
              />
            </label>
            <div className="quick-actions">
              <button
                type="button"
                disabled={busy || targets.length === 0}
                onClick={() => run(
                  () => api.declareCharge(game, activating.id, assault.defenderId || targets[0].id, assault.threatLevel),
                  `${activating.name} was ordered in.`,
                )}
              >
                Charge
              </button>
              <button
                className="ghost"
                type="button"
                disabled={busy || targets.length === 0}
                onClick={() => run(
                  () => api.defenderStands(game, activating.id, assault.defenderId || targets[0].id, assault.terror),
                  'The defenders were tested.',
                )}
              >
                Defender Stands?
              </button>
              <button
                className="ghost"
                type="button"
                disabled={busy || targets.length === 0}
                onClick={() => run(
                  () => api.fightMelee(game, {
                    attackerId: activating.id,
                    defenderId: assault.defenderId || targets[0].id,
                    pairings: Array.from({ length: Math.max(1, assault.pairs) }, () => ({
                      attackerShift: assault.attackerShift,
                      defenderShift: assault.defenderShift,
                      attackerPowerArmour: assault.attackerPowerArmour,
                      defenderPowerArmour: assault.defenderPowerArmour,
                    })),
                    defendersInCover: assault.defendersInCover,
                  }),
                  'A round of melee was fought.',
                )}
              >
                Fight Round
              </button>
            </div>
            <p className="constraint-line">
              Once somebody holds the ground, count this unit's down and read them off your own
              table. A stunned man gets up again on the winning side and is taken on the losing one,
              which is why it waits until the fighting stops.
            </p>
            <label>
              Down
              <input
                type="number"
                min="1"
                max="20"
                value={assault.downed}
                onChange={(event) => setAssault((current) => ({ ...current, downed: wholeNumberFrom(event.target.value, 1, 1, 20) }))}
              />
            </label>
            <label title="Rolls up to and including this are dead. Off your own table - this app has no suggestion.">
              Dead up to
              <input
                type="number"
                min="1"
                max="12"
                value={assault.deadUpTo}
                onChange={(event) => setAssault((current) => ({ ...current, deadUpTo: wholeNumberFrom(event.target.value, 1, 1, 12) }))}
              />
            </label>
            <label title="The die your own table reads those bands against. Without it the bands say nothing, so the game asks rather than picking one.">
              Settled on
              <select
                value={assault.fateDie}
                onChange={(event) => setAssault((current) => ({ ...current, fateDie: Number(event.target.value) }))}
              >
                <option value={unentered}>Not entered</option>
                {ladder.map((face) => <option key={face} value={face}>D{face}</option>)}
              </select>
            </label>
            <label title="Rolls above dead and up to this are wounded; anything higher is stunned.">
              Wounded up to
              <input
                type="number"
                min="1"
                max="12"
                value={assault.woundedUpTo}
                onChange={(event) => setAssault((current) => ({ ...current, woundedUpTo: wholeNumberFrom(event.target.value, 1, 1, 12) }))}
              />
            </label>
            <label title="True when this unit's side holds the ground at the finish.">
              Held the ground
              <input
                type="checkbox"
                checked={assault.wonTheAssault}
                onChange={(event) => setAssault((current) => ({ ...current, wonTheAssault: event.target.checked }))}
              />
            </label>
            <div className="quick-actions">
              <button
                className="ghost"
                type="button"
                disabled={busy}
                onClick={() => run(
                  () => api.settleTheDowned(
                    game,
                    activating.id,
                    assault.downed,
                    assault.wonTheAssault,
                    assault.deadUpTo,
                    assault.woundedUpTo,
                    assault.fateDie,
                  ),
                  `${activating.name} counted its down.`,
                )}
              >
                Settle Downed
              </button>
            </div>
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
              supportWeapons: shot.supportWeapons,
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
            level: unitForm.level,
            qualityDie: unitForm.qualityDie,
            leadershipValue: unitForm.leadershipValue,
            // Exactly as many as were entered. `Math.max(1, ...)` put a figure in a squad nobody had
            // counted, and through the armour select that figure came with a die as well.
            figures: Array.from({ length: unitForm.figures }, () => ({ armourDie: unitForm.armourDie })),
            weapons: [{
              name: unitForm.weaponName,
              impactDie: unitForm.impactDie,
              isSupport: false,
              isCloseRange: false,
              // The panel above builds a weapon that is not a support weapon, so no die is added to
              // anybody's volley. It carried a 6 regardless - a die rating off a record card
              // nobody had opened.
              supportFirepowerDie: 0,
              neverJoinsSquadFire: false,
            }],
            fatigue: unitForm.fatigue,
          }),
          `${unitForm.name} joined ${unitForm.side}.`,
        )}
      />

      <div className="card-module" aria-label="Force transfer">
        <span className="label module-title">Force transfer</span>
        <p className="constraint-line">
          A force is a lot of dice to type. Export writes the roster at full strength with a format
          version, so the file still opens later.
        </p>
        <label>
          Side
          <input value={importSide} onChange={(event) => setImportSide(event.target.value)} />
        </label>
        <div className="quick-actions">
          {snapshot.sides.map((side) => (
            <button
              key={`export-${side}`}
              className="ghost"
              type="button"
              // Export is the one control here that does not go through `run()`, because it writes
              // a file rather than asking the server anything - and it was the one control with no
              // error handling at all. A snapshot whose units carry no figure roster made
              // `toForceFile` throw out of a bare event handler: nothing downloaded, `say` never
              // ran, no message appeared, and the button looked like it had worked. Import, three
              // elements below, has caught its own failures since it was written.
              onClick={() => {
                try {
                  downloadText(
                    `${side}-force.json`,
                    'application/json',
                    JSON.stringify(toForceFile(side, snapshot.units), null, 2),
                  );
                } catch (error) {
                  setMessage(error instanceof Error ? error.message : 'That force could not be written to a file.');
                  setMessageIsError(true);
                  return;
                }

                say(`Exported ${side}.`);
              }}
            >
              Export {side}
            </button>
          ))}
          <button className="ghost" type="button" onClick={() => fileInput.current?.click()}>Import Force</button>
        </div>
        <input
          ref={fileInput}
          type="file"
          accept="application/json,.json"
          hidden
          onChange={async (event) => {
            const file = event.target.files?.[0];
            event.target.value = '';
            if (!file) {
              return;
            }

            try {
              const force = fromForceFile(JSON.parse(await file.text()));
              const side = importSide.trim() || force.side;
              for (const imported of force.units) {
                // Fresh ids: the same file may be imported for both sides, and a game cannot hold
                // two units under one name.
                await api.addUnit(game, { ...imported, id: newId(), side });
              }

              setSnapshot(await api.readGame(game));
              say(`Imported ${force.units.length} unit(s) into ${side}.`);
            } catch (error) {
              setMessage(error instanceof Error ? error.message : 'That file could not be read.');
              setMessageIsError(true);
            }
          }}
        />
      </div>

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
  onTest,
  onToggleDisorganised,
}: {
  unit: StarGruntUnit;
  isActivating: boolean;
  busy: boolean;
  onActivate: () => void;
  onTest: () => void;
  onToggleDisorganised: () => void;
}) {
  const wiped = unit.figuresAlive <= 0;
  return (
    <div className={`card-module stargrunt-unit${isActivating ? ' activating' : ''}${wiped ? ' wiped' : ''}`}>
      <span className="label module-title">{unit.name}</span>
      <div className="ship-readouts">
        <div><span className="label">Side</span><strong>{unit.side}</strong></div>
        <div><span className="label">Figures</span><strong>{unit.figuresAlive}/{unit.fullStrength}</strong></div>
        <div><span className="label">Casualties</span><strong>{unit.figuresWounded}</strong></div>
        <div><span className="label">Suppression</span><strong>{unit.suppressionMarkers}</strong></div>
        <div><span className="label">Confidence</span><strong>{unit.confidence}</strong></div>
        <div><span className="label">Fatigue</span><strong>{unit.fatigue}</strong></div>
        <div><span className="label">Quality</span><strong>D{unit.qualityDie}</strong></div>
      </div>
      <p className="constraint-line">
        {wiped ? 'Wiped out.' : unit.canActivate ? 'Ready to activate.' : unit.activationBlocker ?? 'Not now.'}
        {unit.isInCover ? ' In cover.' : ''}
        {unit.isDisorganised ? ' Disorganised.' : ''}
        {unit.isLeaderDown ? ' Leader down.' : ''}
        {unit.nextMoveLeavesCover ? (unit.reactionTestCleared ? ' Ready to go.' : ' Next move leaves cover.') : ''}
      </p>
      <div className="quick-actions">
        <button type="button" disabled={busy || !unit.canActivate} onClick={onActivate}>Activate</button>
        <button className="ghost" type="button" disabled={busy} onClick={onTest}>Confidence Test</button>
        <button className="ghost" type="button" disabled={busy} onClick={onToggleDisorganised}>
          {unit.isDisorganised ? 'In Order' : 'Scattered'}
        </button>
      </div>
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
          <option value={unentered}>Not entered</option>
          {ladder.map((die) => <option key={die} value={die}>D{die}</option>)}
        </select>
      </label>
      {firer.weapons.filter((weapon) => weapon.isSupport && !weapon.neverJoinsSquadFire).map((weapon) => (
        <label key={`support-${weapon.name}`} title="Adds its die to the volley. It cannot also fire on its own this activation.">
          Add {weapon.name}
          <input
            type="checkbox"
            checked={shot.supportWeapons.includes(weapon.name)}
            onChange={(event) => onChange({
              supportWeapons: event.target.checked
                ? [...shot.supportWeapons, weapon.name]
                : shot.supportWeapons.filter((name) => name !== weapon.name),
            })}
          />
        </label>
      ))}
      <label>
        Range
        <input
          type="number"
          min="0"
          value={shot.distanceInches}
          onChange={(event) => onChange({ distanceInches: wholeNumberFrom(event.target.value, 0, 0, 999) })}
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
          <option value={unentered}>Not entered</option>
          {ladder.map((die) => <option key={die} value={die}>D{die}</option>)}
        </select>
      </label>
      <label title="Leadership Value from your record card: 1 to 3, and 1 is the best.">
        Leadership
        <select value={form.leadershipValue} onChange={(event) => onChange({ leadershipValue: Number(event.target.value) })}>
          <option value={unentered}>Not entered</option>
          {[1, 2, 3].map((value) => <option key={value} value={value}>{value}{value === 1 ? ' (best)' : ''}</option>)}
        </select>
      </label>
      <label>
        Figures
        <input
          type="number"
          min="0"
          max="20"
          value={form.figures}
          onChange={(event) => onChange({ figures: wholeNumberFrom(event.target.value, 0, 0, 20) })}
        />
      </label>
      <label>
        Armour
        <select value={form.armourDie} onChange={(event) => onChange({ armourDie: Number(event.target.value) })}>
          <option value={unentered}>Not entered</option>
          {ladder.map((die) => <option key={die} value={die}>D{die}</option>)}
        </select>
      </label>
      <label title="Rallying comes from above, so a commander needs a level above the units it steadies.">
        Command level
        <select value={form.level} onChange={(event) => onChange({ level: event.target.value })}>
          {commandLadder.map((level) => <option key={level} value={level}>{level}</option>)}
        </select>
      </label>
      <label title="Sets where confidence starts and how far a rally can bring it back.">
        Fatigue
        <select value={form.fatigue} onChange={(event) => onChange({ fatigue: event.target.value })}>
          {['Fresh', 'Tired', 'Exhausted'].map((level) => <option key={level} value={level}>{level}</option>)}
        </select>
      </label>
      <label>
        Weapon
        <input value={form.weaponName} onChange={(event) => onChange({ weaponName: event.target.value })} />
      </label>
      <label>
        Impact
        <select value={form.impactDie} onChange={(event) => onChange({ impactDie: Number(event.target.value) })}>
          <option value={unentered}>Not entered</option>
          {ladder.map((die) => <option key={die} value={die}>D{die}</option>)}
        </select>
      </label>
      <button type="button" disabled={busy} onClick={onAdd}>Add Squad</button>
    </div>
  );
}

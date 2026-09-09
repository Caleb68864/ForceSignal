import { useRef, useState } from 'react';
import { blankRulesProfile, type RulesProfile } from '../types.ts';
import { wholeNumberFrom } from '../lib/format.ts';
import { deleteProfile, exportProfile, gapsIn, readProfile, sameProfile, savedProfiles, saveProfile } from '../lib/rulesProfile.ts';

type Props = {
  /** The profile the match is currently played against. */
  value: RulesProfile;
  /** True while the profile can still be changed, which is during fleet setup only. */
  editable: boolean;
  /** Sends a completed profile to the server. */
  onApply: (profile: RulesProfile) => void;
};

/**
 * The form a player fills in with the numbers from their own rulebook and record cards.
 *
 * ForceSignal ships no rules numbers at all - not here, not on the server, not as a helpful
 * starting suggestion. This form starts blank, saves what you type in this browser so you only do
 * it once, and reads and writes JSON so a table can share one file between them.
 */
export function RulesProfileEditor({ value, editable, onApply }: Props) {
  const [draft, setDraft] = useState<RulesProfile>(value);
  const [saved, setSaved] = useState<RulesProfile[]>(savedProfiles);
  const [open, setOpen] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);

  // The form is on screen before the first snapshot lands, so the profile it was seeded with was
  // always the blank one, and it stayed blank for the rest of the session: a match that already had
  // a profile - restored, or joined after the owner set it - showed zeros for every number, and a
  // player who touched Save wrote those zeros over the real ones. So the form follows the table.
  //
  // Only when the table's own numbers change, though, and compared by content rather than by
  // identity: a snapshot arrives on every mutation anyone makes, and each one carries a freshly
  // parsed profile object. Adopting on identity would wipe whatever was half-typed each time the
  // opponent moved a ship.
  const [lastFromTable, setLastFromTable] = useState<RulesProfile>(value);
  if (!sameProfile(lastFromTable, value)) {
    setLastFromTable(value);
    setDraft(value);
  }

  const gaps = gapsIn(draft);
  const patch = (change: Partial<RulesProfile>) => setDraft((current) => ({ ...current, ...change }));

  function importFile(file: File) {
    file.text()
      .then((text) => setDraft(readProfile(JSON.parse(text) as unknown)))
      .catch(() => setDraft(blankRulesProfile));
  }

  return (
    <div className="table-setup" aria-label="Rules profile">
      <span className="label">Rules profile</span>
      <p className="privacy">
        ForceSignal ships no rules numbers. Enter the ones from your own rulebook and record cards
        once, and this browser keeps them for next time. A match cannot start until the profile is
        complete, and it is settled during fleet setup.
      </p>

      <div className="table-fields">
        <label>
          Saved profiles
          <select
            value=""
            onChange={(event) => {
              const picked = saved.find((profile) => profile.name === event.target.value);
              if (picked) {
                setDraft(picked);
              }
            }}
          >
            <option value="">{saved.length ? 'Load a saved profile...' : 'Nothing saved yet'}</option>
            {saved.map((profile) => <option key={profile.name} value={profile.name}>{profile.name}</option>)}
          </select>
        </label>
        <span className="constraint-line">
          Playing against: <strong>{value.name || 'nothing yet'}</strong>
        </span>
      </div>

      <div className="quick-actions">
        <button type="button" className="ghost" onClick={() => setOpen((current) => !current)}>
          {open ? 'Hide numbers' : 'Edit numbers'}
        </button>
        <button type="button" className="ghost" onClick={() => fileInput.current?.click()}>Import JSON</button>
        <button type="button" className="ghost" onClick={() => exportProfile(draft)}>Export JSON</button>
        <input
          ref={fileInput}
          type="file"
          accept="application/json,.json"
          style={{ display: 'none' }}
          onChange={(event) => {
            const file = event.target.files?.[0];
            if (file) {
              importFile(file);
            }
            event.target.value = '';
          }}
        />
      </div>

      {open ? (
        <div className="rules-profile-fields">
          <Group title="The profile">
            <Text label="Name" value={draft.name} onChange={(name) => patch({ name })} />
            <Num label="Die faces" value={draft.dieFaces} onChange={(dieFaces) => patch({ dieFaces })} />
          </Group>

          <Group title="Beams" hint="What one die scores at each face against each screen level. Leave a face out and it scores nothing.">
            <Num label="Range band (mu)" value={draft.beamRangeBandWidth} onChange={(v) => patch({ beamRangeBandWidth: v })} />
            <Num label="Highest screen level" value={draft.maxScreenLevel} onChange={(v) => patch({ maxScreenLevel: v })} />
            <BeamGrid profile={draft} onChange={patch} />
          </Group>

          <Group title="Pulse torpedoes" hint="Leave the reach at zero if your table does not use them.">
            <Num label="Longest reach (mu)" value={draft.torpedoMaximumRange} onChange={(v) => patch({ torpedoMaximumRange: v })} />
            <Num label="Band width (mu)" value={draft.torpedoBandWidth} onChange={(v) => patch({ torpedoBandWidth: v })} />
            <Num label="Needed in the closest band" value={draft.torpedoBestToHit} onChange={(v) => patch({ torpedoBestToHit: v })} />
          </Group>

          <Group title="Needle beams">
            <Num label="Reach when the mount says none (mu)" value={draft.needleBeamRange} onChange={(v) => patch({ needleBeamRange: v })} />
            <Num label="Takes the system on" value={draft.needleSystemKillRoll} onChange={(v) => patch({ needleSystemKillRoll: v })} />
            <Check label="Also holes the hull" value={draft.enhancedNeedleBeams} onChange={(v) => patch({ enhancedNeedleBeams: v })} />
            <Num label="Draws blood on" value={draft.needleHullDamageRoll} onChange={(v) => patch({ needleHullDamageRoll: v })} />
          </Group>

          <Group title="Hull damage track" hint="Completing the last row is the ship's destruction rather than another check.">
            <label>
              Rows
              <select
                value={draft.thresholdRows}
                onChange={(event) => patch({ thresholdRows: event.target.value === 'ByShipClass' ? 'ByShipClass' : 'FixedRows' })}
              >
                <option value="FixedRows">The same for every hull</option>
                <option value="ByShipClass">By the hull's size band</option>
              </select>
            </label>
            <Num label="Rows in a track" value={draft.thresholdRowCount} onChange={(v) => patch({ thresholdRowCount: v })} />
            <Num label="Rows for an escort" value={draft.escortRowCount} onChange={(v) => patch({ escortRowCount: v })} />
            <Num label="Rows for a cruiser" value={draft.cruiserRowCount} onChange={(v) => patch({ cruiserRowCount: v })} />
          </Group>

          <Group title="Damage control" hint="Each extra party on a job lowers the number by one, down to the best roll.">
            <Num label="Parties one job can hold" value={draft.maxPartiesPerJob} onChange={(v) => patch({ maxPartiesPerJob: v })} />
            <Num label="One party repairs on" value={draft.repairRollWithOneParty} onChange={(v) => patch({ repairRollWithOneParty: v })} />
            <Num label="Best it can get to" value={draft.repairBestRoll} onChange={(v) => patch({ repairBestRoll: v })} />
          </Group>

          <Group title="Fighters and flight decks">
            <Num label="A group flies (mu)" value={draft.fighterMoveAllowance} onChange={(v) => patch({ fighterMoveAllowance: v })} />
            <Check label="Flight rate follows the bays" value={draft.carrierRatesFollowBays} onChange={(v) => patch({ carrierRatesFollowBays: v })} />
            <Num label="A carrier works (groups)" value={draft.trueCarrierAllowance} onChange={(v) => patch({ trueCarrierAllowance: v })} />
            <Num label="Any other ship works (groups)" value={draft.otherShipAllowance} onChange={(v) => patch({ otherShipAllowance: v })} />
            <Check label="A landed group rolls for turnaround" value={draft.carrierTurnaroundRoll} onChange={(v) => patch({ carrierTurnaroundRoll: v })} />
            <TurnaroundRows profile={draft} onChange={patch} />
          </Group>

          <Group title="Point defence" hint="A face left out shoots nothing down. Set the chaining face to zero if no face earns another die.">
            <Num label="Reach (mu)" value={draft.pointDefenseRange} onChange={(v) => patch({ pointDefenseRange: v })} />
            <Num label="Chains on face" value={draft.pointDefenseChainOnFace} onChange={(v) => patch({ pointDefenseChainOnFace: v })} />
            <PointDefenceRows profile={draft} onChange={patch} />
          </Group>

          <Group title="Salvo missiles" hint="Leave the salvo size at zero if your table does not use them.">
            <Num label="Missiles in a salvo" value={draft.missilesPerSalvo} onChange={(v) => patch({ missilesPerSalvo: v })} />
            <Num label="Attack radius (mu)" value={draft.salvoAttackRadius} onChange={(v) => patch({ salvoAttackRadius: v })} />
          </Group>
        </div>
      ) : null}

      {gaps.length > 0 ? (
        <ul className="constraint-line" aria-label="What is still missing">
          {gaps.map((gap) => <li key={gap}>{gap}</li>)}
        </ul>
      ) : null}

      <div className="quick-actions">
        <button
          type="button"
          disabled={gaps.length > 0}
          onClick={() => {
            setSaved(saveProfile(draft));
            onApply(draft);
          }}
        >
          {editable ? 'Save & Play Against This' : 'Save To This Browser'}
        </button>
        <button
          type="button"
          className="ghost"
          disabled={!saved.some((profile) => profile.name === draft.name)}
          onClick={() => setSaved(deleteProfile(draft.name))}
        >
          Forget Saved
        </button>
        <button type="button" className="ghost" onClick={() => setDraft(blankRulesProfile)}>Start Blank</button>
      </div>
      {!editable ? (
        <p className="constraint-line">
          The profile is settled during fleet setup, so it cannot change now. You can still save it
          to this browser for the next match.
        </p>
      ) : null}
    </div>
  );
}

function Group({ title, hint, children }: { title: string; hint?: string; children: React.ReactNode }) {
  return (
    <div className="card-module">
      <span className="label module-title">{title}</span>
      {hint ? <p className="constraint-line">{hint}</p> : null}
      <div className="table-fields">{children}</div>
    </div>
  );
}

function Num({ label, value, onChange }: { label: string; value: number; onChange: (value: number) => void }) {
  return (
    <label>
      {label}
      <input type="number" min="0" max="99" value={value} onChange={(event) => onChange(wholeNumberFrom(event.target.value, 0, 0, 99))} />
    </label>
  );
}

function Text({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
  return (
    <label>
      {label}
      <input value={value} onChange={(event) => onChange(event.target.value)} />
    </label>
  );
}

function Check({ label, value, onChange }: { label: string; value: boolean; onChange: (value: boolean) => void }) {
  return (
    <label>
      {label}
      <input type="checkbox" checked={value} onChange={(event) => onChange(event.target.checked)} />
    </label>
  );
}

/** The beam table as a grid of faces against screen levels, sized by the profile's own numbers. */
function BeamGrid({ profile, onChange }: { profile: RulesProfile; onChange: (change: Partial<RulesProfile>) => void }) {
  const faces = Array.from({ length: Math.max(0, Math.min(20, profile.dieFaces)) }, (_, index) => index + 1).reverse();
  const screens = Array.from({ length: Math.max(0, Math.min(9, profile.maxScreenLevel)) + 1 }, (_, index) => index);

  if (faces.length === 0) {
    return <p className="constraint-line">Set the number of die faces first, and the grid appears.</p>;
  }

  const damageAt = (dieFace: number, screenLevel: number) =>
    profile.beamDamage.find((entry) => entry.dieFace === dieFace && entry.screenLevel === screenLevel)?.damage ?? 0;

  const set = (dieFace: number, screenLevel: number, damage: number) => {
    const rest = profile.beamDamage.filter((entry) => !(entry.dieFace === dieFace && entry.screenLevel === screenLevel));
    onChange({ beamDamage: damage > 0 ? [...rest, { dieFace, screenLevel, damage }] : rest });
  };

  return (
    <table className="rules-grid">
      <thead>
        <tr>
          <th scope="col">Face</th>
          {screens.map((screen) => <th key={screen} scope="col">Screens {screen}</th>)}
        </tr>
      </thead>
      <tbody>
        {faces.map((face) => (
          <tr key={face}>
            <th scope="row">{face}</th>
            {screens.map((screen) => (
              <td key={screen}>
                <input
                  type="number"
                  min="0"
                  max="99"
                  aria-label={`Face ${face} against screens ${screen}`}
                  value={damageAt(face, screen)}
                  onChange={(event) => set(face, screen, wholeNumberFrom(event.target.value, 0, 0, 99))}
                />
              </td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/** What each point-defence face shoots down. */
function PointDefenceRows({ profile, onChange }: { profile: RulesProfile; onChange: (change: Partial<RulesProfile>) => void }) {
  const faces = Array.from({ length: Math.max(0, Math.min(20, profile.dieFaces)) }, (_, index) => index + 1).reverse();
  const killsAt = (dieFace: number) => profile.pointDefenseKills.find((entry) => entry.dieFace === dieFace)?.kills ?? 0;
  const set = (dieFace: number, kills: number) => {
    const rest = profile.pointDefenseKills.filter((entry) => entry.dieFace !== dieFace);
    onChange({ pointDefenseKills: kills > 0 ? [...rest, { dieFace, kills }] : rest });
  };

  return (
    <table className="rules-grid">
      <thead><tr><th scope="col">Face</th><th scope="col">Shoots down</th></tr></thead>
      <tbody>
        {faces.map((face) => (
          <tr key={face}>
            <th scope="row">{face}</th>
            <td>
              <input
                type="number"
                min="0"
                max="99"
                aria-label={`Point defence face ${face}`}
                value={killsAt(face)}
                onChange={(event) => set(face, wholeNumberFrom(event.target.value, 0, 0, 99))}
              />
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/** What each turnaround face means for a group that has just landed. */
function TurnaroundRows({ profile, onChange }: { profile: RulesProfile; onChange: (change: Partial<RulesProfile>) => void }) {
  if (!profile.carrierTurnaroundRoll) {
    return null;
  }

  const faces = Array.from({ length: Math.max(0, Math.min(20, profile.dieFaces)) }, (_, index) => index + 1);
  const entryAt = (dieFace: number) => profile.turnaround.find((entry) => entry.dieFace === dieFace);
  const set = (dieFace: number, isGroundedForGame: boolean, turnsBeforeRelaunch: number) => {
    const rest = profile.turnaround.filter((entry) => entry.dieFace !== dieFace);
    const isDefault = !isGroundedForGame && turnsBeforeRelaunch === 1;
    onChange({ turnaround: isDefault ? rest : [...rest, { dieFace, isGroundedForGame, turnsBeforeRelaunch }] });
  };

  return (
    <table className="rules-grid">
      <thead><tr><th scope="col">Face</th><th scope="col">Grounded</th><th scope="col">Turns on the deck</th></tr></thead>
      <tbody>
        {faces.map((face) => {
          const entry = entryAt(face);
          const grounded = entry?.isGroundedForGame ?? false;
          const turns = entry?.turnsBeforeRelaunch ?? 1;
          return (
            <tr key={face}>
              <th scope="row">{face}</th>
              <td>
                <input
                  type="checkbox"
                  aria-label={`Face ${face} grounds the group`}
                  checked={grounded}
                  onChange={(event) => set(face, event.target.checked, turns)}
                />
              </td>
              <td>
                <input
                  type="number"
                  min="0"
                  max="20"
                  aria-label={`Face ${face} turns on the deck`}
                  value={turns}
                  onChange={(event) => set(face, grounded, wholeNumberFrom(event.target.value, 0, 0, 20))}
                />
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}

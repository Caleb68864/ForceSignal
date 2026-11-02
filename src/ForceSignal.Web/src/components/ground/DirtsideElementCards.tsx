import type { DirtsideValidityInput } from '../../lib/dirtsideApi.ts';
import type { ElementCard, WeaponCard } from '../../lib/dirtsideCard.ts';
import { newElementCard, newWeaponCard } from '../../lib/dirtsideCard.ts';
import { wholeNumberFrom } from '../../lib/format.ts';
import { bands, chitColourSets, fireControls, valueScales } from '../../lib/groundVocabulary.ts';

const bandKeys = { Close: 'close', Medium: 'medium', Long: 'long' } as const;

/**
 * The record cards of one platoon: as many kinds of element as it has, each with as many weapons as
 * it carries, each weapon with a validity row per band.
 *
 * The rows matter separately because a damaged firer's shot resolves a band worse and reads validity
 * at that band, so three identical rows hide a mechanic the engine gets right.
 */
export function DirtsideElementCards({ cards, onChange }: {
  cards: ElementCard[];
  onChange: (cards: ElementCard[]) => void;
}) {
  function setCard(index: number, change: Partial<ElementCard>) {
    onChange(cards.map((card, at) => (at === index ? { ...card, ...change } : card)));
  }

  return (
    <>
      {cards.map((card, index) => {
        const label = `element card ${index + 1}`;

        function setWeapon(at: number, change: Partial<WeaponCard>) {
          setCard(index, { weapons: card.weapons.map((weapon, i) => (i === at ? { ...weapon, ...change } : weapon)) });
        }

        return (
          <fieldset key={index} aria-label={`Element card ${index + 1}`}>
            <legend>Element card {index + 1}</legend>
            <div className="table-fields">
              <label title="How many elements in the platoon are built off this card.">Copies<input type="number" min="1" max="12" value={card.copies} onChange={(e) => setCard(index, { copies: wholeNumberFrom(e.target.value, 1, 1, 12) })} /></label>
              <label title="Each element is numbered from this, so the log reads 'Alpha Troop&apos;s Vehicle 1'.">Element name<input value={card.name} onChange={(e) => setCard(index, { name: e.target.value })} /></label>
              <label>
                Fire control
                <select value={card.fireControl} onChange={(e) => setCard(index, { fireControl: e.target.value })}>
                  {fireControls.map((level) => <option key={level} value={level}>{level}</option>)}
                </select>
              </label>
              <label>Signature<input type="number" min="0" max="5" value={card.signature} onChange={(e) => setCard(index, { signature: wholeNumberFrom(e.target.value, 0, 0, 5) })} /></label>
              <label>Armour<input type="number" min="0" max="30" value={card.armourValue} onChange={(e) => setCard(index, { armourValue: wholeNumberFrom(e.target.value, 0, 0, 30) })} /></label>
              <label>Movement<input type="number" min="0" max="60" value={card.movement} onChange={(e) => setCard(index, { movement: wholeNumberFrom(e.target.value, 0, 0, 60) })} /></label>
              <label title="Bought at design time. Makes a Systems Down marker easier to get off.">
                Backup systems
                <input type="checkbox" checked={card.hasBackupSystems} onChange={(e) => setCard(index, { hasBackupSystems: e.target.checked })} />
              </label>
              <label title="How many chits each element draws in a close assault, off its card. Blank when the card does not give it; the element then cannot assault.">
                Assault chits
                <input type="number" min="0" max="20" value={card.assaultChits} onChange={(e) => setCard(index, { assaultChits: e.target.value })} />
              </label>
              <label title="The valid total that removes an element in a close assault or a firefight, off its card. Blank when the card does not give it; the element then cannot assault or be fired on in a firefight.">
                Kill threshold
                <input type="number" min="0" max="99" value={card.killThreshold} onChange={(e) => setCard(index, { killThreshold: e.target.value })} />
              </label>
            </div>

            {card.weapons.map((weapon, at) => (
              <fieldset key={at} aria-label={`Weapon ${at + 1} of ${label}`}>
                <legend>Weapon {at + 1}</legend>
                <div className="table-fields">
                  <label>Weapon<input value={weapon.name} onChange={(e) => setWeapon(at, { name: e.target.value })} /></label>
                  <label title="How many chits each hit draws.">Chits<input type="number" min="0" max="20" value={weapon.chitCount} onChange={(e) => setWeapon(at, { chitCount: wholeNumberFrom(e.target.value, 0, 0, 20) })} /></label>
                  <label title="Weapons of the same type in the mount. They fire together, at one target.">Barrels<input type="number" min="1" max="8" value={weapon.barrels} onChange={(e) => setWeapon(at, { barrels: wholeNumberFrom(e.target.value, 1, 1, 8) })} /></label>
                  <label title="Aimed by pointing the whole vehicle.">
                    Fixed mount
                    <input type="checkbox" checked={weapon.isFixedMount} onChange={(e) => setWeapon(at, { isFixedMount: e.target.checked })} />
                  </label>
                  {/*
                    Recorded off the card, and recorded is all it is. Interception has no resolution
                    in this engine, and the route exists to refuse and to name the rules that are
                    missing. See the roadmap entry.
                  */}
                  <label title="True when an area-defence gun could shoot down what this weapon throws. Recorded off your card; interception is refused by this app, which has no rules for resolving one.">
                    Interceptable
                    <input type="checkbox" checked={weapon.isInterceptable} onChange={(e) => setWeapon(at, { isInterceptable: e.target.checked })} />
                  </label>
                </div>
                {bands.map((band) => (
                  <ValidityRow
                    key={band}
                    band={band}
                    row={weapon[bandKeys[band]]}
                    onChange={(row) => setWeapon(at, { [bandKeys[band]]: row })}
                  />
                ))}
                <button
                  type="button"
                  aria-label={`Remove weapon ${at + 1} of ${label}`}
                  onClick={() => setCard(index, { weapons: card.weapons.filter((_, i) => i !== at) })}
                >
                  Remove Weapon
                </button>
              </fieldset>
            ))}

            <button type="button" onClick={() => setCard(index, { weapons: [...card.weapons, newWeaponCard()] })}>
              Add weapon
            </button>
            {cards.length > 1 ? (
              <button
                type="button"
                aria-label={`Remove ${label}`}
                onClick={() => onChange(cards.filter((_, at) => at !== index))}
              >
                Remove Card
              </button>
            ) : null}
          </fieldset>
        );
      })}
      <button type="button" onClick={() => onChange([...cards, newElementCard()])}>
        Add another kind of element
      </button>
    </>
  );
}

/** One row off the weapon's validity card: what it may count at one band. */
function ValidityRow({ band, row, onChange }: {
  band: string;
  row: DirtsideValidityInput;
  onChange: (row: DirtsideValidityInput) => void;
}) {
  return (
    <div className="table-fields" aria-label={`${band} validity`} role="group">
      <label title={`Which chit colours this weapon's hits may count at ${band.toLowerCase()} range, off your own card.`}>
        {band} colours
        <select value={row.colours} disabled={row.isIneffective} onChange={(e) => onChange({ ...row, colours: e.target.value })}>
          {chitColourSets.map((colour) => <option key={colour} value={colour}>{colour}</option>)}
        </select>
      </label>
      <label title="How the numbers on the chits read at this band, off your own card.">
        {band} value scale
        <select value={row.valueScale} disabled={row.isIneffective} onChange={(e) => onChange({ ...row, valueScale: e.target.value })}>
          {valueScales.map((scale) => <option key={scale} value={scale}>{scale}</option>)}
        </select>
      </label>
      <label title="Whether the special chits do anything at this band.">
        {band} specials count
        <input type="checkbox" checked={row.specialsCount} disabled={row.isIneffective} onChange={(e) => onChange({ ...row, specialsCount: e.target.checked })} />
      </label>
      <label title="This weapon cannot harm the target at this band at all, and no chit leaves the pot. Not the same as no valid colours: specials are not colour-gated.">
        {band} ineffective
        <input type="checkbox" checked={row.isIneffective} onChange={(e) => onChange({ ...row, isIneffective: e.target.checked })} />
      </label>
    </div>
  );
}

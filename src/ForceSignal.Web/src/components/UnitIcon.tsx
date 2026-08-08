import { useEffect } from 'react';
import spriteMarkup from '../assets/unit-icons.svg?raw';

/**
 * A ground unit type that has an icon.
 *
 * These cover both ground games: the first seven are StarGrunt's infantry-scale teams, the rest are
 * Dirtside's vehicles and the battlefield markers both share.
 */
export type UnitIconKey =
  | 'rifle-team'
  | 'powered-armour'
  | 'support-weapon'
  | 'anti-armour'
  | 'command'
  | 'sniper'
  | 'medic'
  | 'tank'
  | 'apc'
  | 'recon'
  | 'walker'
  | 'artillery'
  | 'air-defence'
  | 'vtol'
  | 'aerospace'
  | 'comms'
  | 'drone'
  | 'minefield';

/** Every unit icon, with the label a picker should show. */
export const unitIconOptions: { key: UnitIconKey; label: string }[] = [
  { key: 'rifle-team', label: 'Rifle team' },
  { key: 'powered-armour', label: 'Powered armour' },
  { key: 'support-weapon', label: 'Support weapon team' },
  { key: 'anti-armour', label: 'Anti-armour team' },
  { key: 'command', label: 'Command unit' },
  { key: 'sniper', label: 'Sniper' },
  { key: 'medic', label: 'Medic' },
  { key: 'tank', label: 'Tank' },
  { key: 'apc', label: 'APC or MICV' },
  { key: 'recon', label: 'Recon vehicle' },
  { key: 'walker', label: 'Combat walker' },
  { key: 'artillery', label: 'Artillery' },
  { key: 'air-defence', label: 'Air defence' },
  { key: 'vtol', label: 'VTOL' },
  { key: 'aerospace', label: 'Aerospace fighter' },
  { key: 'comms', label: 'Comms or HQ vehicle' },
  { key: 'drone', label: 'Drone' },
  { key: 'minefield', label: 'Minefield' },
];

const SPRITE_ELEMENT_ID = 'forcesignal-unit-icons';

/**
 * Puts the icon sheet into the document once, so every icon is a same-document reference.
 *
 * Pointing `use` at a file in `public/` would also work, but content referenced across documents
 * does not inherit `currentColor` in every browser - the icons would come out a fixed colour and
 * stop taking their fleet's accent. Inlining the sheet keeps them tintable like everything else on
 * the map, at the cost of a few kilobytes in the bundle.
 */
function ensureSprite() {
  if (typeof document === 'undefined' || document.getElementById(SPRITE_ELEMENT_ID)) {
    return;
  }

  const holder = document.createElement('div');
  holder.id = SPRITE_ELEMENT_ID;
  holder.setAttribute('aria-hidden', 'true');
  holder.style.display = 'none';
  holder.innerHTML = spriteMarkup;
  document.body.prepend(holder);
}

/**
 * One ground unit icon.
 *
 * The artwork is from game-icons.net under Creative Commons Attribution 3.0 - see ATTRIBUTION.md,
 * and the credit shown in the app's notice panel. It is not ForceSignal's own, unlike the ship
 * silhouettes drawn inline in ShipCard.
 */
export function UnitIcon({ iconKey, label }: { iconKey: UnitIconKey; label?: string }) {
  useEffect(ensureSprite, []);

  // Runs on the first render too, so the symbol exists before the browser resolves the reference.
  ensureSprite();

  return (
    <svg
      className="unit-icon-svg"
      viewBox="0 0 512 512"
      role={label ? 'img' : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
    >
      <use href={`#gi-${iconKey}`} />
    </svg>
  );
}

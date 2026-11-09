/**
 * How to play: the whole match, one step at a time, with a picture of each screen.
 *
 * Opened from the top bar at any point, and it opens on the step the match is actually at - a new
 * player who is lost mid-game wants "what do I do now", not page one. The pictures are screenshots
 * of this app (public/help/), taken by playing a match through on two devices.
 *
 * Like every other piece of prose here, this says where a number goes and never what it is: the app
 * ships no rules numbers, and HelpGuide.test.tsx accounts for every digit the guide puts on screen.
 */

import { useEffect, useRef, useState } from 'react';

export type HelpImage = { src: string; alt: string; phone?: boolean };
export type HelpStep = { id: string; title: string; body: string[]; images: HelpImage[] };

export const helpSteps: HelpStep[] = [
  {
    id: 'join',
    title: 'Start or join a match',
    body: [
      'One player taps Create Match and becomes the host. Their screen shows a room code: read it out, or tap Copy code.',
      'Everyone else types a display name and the code, then taps Join Match. Any phone with a browser will do.',
    ],
    images: [
      { src: 'help/01-create-or-join.png', alt: 'The start panel with Display name, Create Match, Room code and Join Match' },
      { src: 'help/01-create-or-join-phone.png', alt: 'The same start panel on a phone', phone: true },
      { src: 'help/02-room-code.png', alt: 'The room code with its Copy code button' },
    ],
  },
  {
    id: 'rules',
    title: 'Set the rules profile (host)',
    body: [
      'ForceSignal comes with no rules numbers. The host enters the ones from your own rulebook once, under Rules profile → Edit numbers, or loads a saved file with Import JSON.',
      'The amber list says exactly what is still missing. When it is empty, tap Save & Play Against This. This browser remembers the profile for next time. Nobody can start until one is set.',
    ],
    images: [{ src: 'help/03-rules-profile.png', alt: 'The rules profile panel with the list of missing entries' }],
  },
  {
    id: 'fleet',
    title: 'Build your fleet',
    body: [
      'Each player adds their own ships under Fleet setup. A preset such as Cruiser or Fighters only picks the shape and icon. Thrust, hull and armour boxes, screens and fire controls come off your own record card.',
      'Add each weapon with Add under Weapons: its type, dice, range and the arcs it fires through. Then tap Add Ship.',
      'Course is a clock face: 12 points up the table and 3 points to the right.',
    ],
    images: [{ src: 'help/04-add-ship.png', alt: 'The Add ship form with presets, stats and a weapon mount' }],
  },
  {
    id: 'ready',
    title: 'Ready up',
    body: [
      'When your fleet is complete, tap Ready under Match commands.',
      'Once every player is ready, the match moves to Order entry. The top bar always shows the phase and turn.',
    ],
    images: [{ src: 'help/05-ready.png', alt: 'The Ready button under Match commands' }],
  },
  {
    id: 'plot',
    title: 'Plot your orders',
    body: [
      'Open Play Map, tap one of your ships, then its Helm tab. Tap, right-click or long-press open space to plot where it goes. The arrow is the path it will really fly, and the panel says whether that is within its thrust.',
      'On the ship card you can plot with the compass, Max Accel / Max Decel, or Port +1 / Starboard +1 instead.',
      'A ship you leave alone keeps its speed and heading.',
    ],
    images: [
      { src: 'help/06-plot-on-map.png', alt: 'A ship selected on the play map with its plotted course arrow and the Helm panel' },
      { src: 'help/06-plot-on-card-phone.png', alt: 'Plotting from the ship card on a phone', phone: true },
    ],
  },
  {
    id: 'lock',
    title: 'Lock your orders',
    body: [
      'Happy with every ship? Tap Lock Fleet Orders. Your orders are sealed so the other side cannot see them, and the table knows you have finished.',
      'Changed your mind before everyone has locked? Tap Resume Plotting. The Pre-turn checklist shows anything still missing.',
    ],
    images: [{ src: 'help/07-checklist-locked.png', alt: 'The pre-turn checklist with orders locked' }],
  },
  {
    id: 'move',
    title: 'Reveal and move',
    body: [
      'Once everyone has locked, each player taps Reveal Fleet Orders.',
      'When every order is revealed, the host taps Advance Turn and all ships move at once. Look at the map to see where everyone ended up.',
    ],
    images: [{ src: 'help/08-after-movement-map.png', alt: 'The play map after movement, with each ship at its new position' }],
  },
  {
    id: 'fire',
    title: 'Fire',
    body: [
      'The app rolls for who fires first, then the sides take turns one ship at a time.',
      'On Play Map, tap your ship and open Fire. Pick a Target and a Weapon, tap Use Map Solution to measure the range, and when the panel says Ready, tap Fire.',
      'When that ship is finished, tap Done Firing (or Hold Fire if it will not shoot) to hand the turn over. If Fire is greyed out, the line at the top of the panel says why: not your turn, out of arc, out of range, or already fired.',
    ],
    images: [
      { src: 'help/09-fire-console.png', alt: 'The map firing panel with target, weapon, bearing, range and the Fire button' },
      { src: 'help/09-fire-console-phone.png', alt: 'The firing panel on a phone', phone: true },
      { src: 'help/09-fire-refused.png', alt: 'A refused shot, with the reason shown above the Fire button' },
    ],
  },
  {
    id: 'results',
    title: 'Read the results',
    body: [
      'Every roll the app makes is written to the Log tab, newest first: what fired at what, the dice, and the armour and hull damage done.',
      'Damage to your ships also shows on their cards. Export the log as MD, CSV or JSON for an after-action report.',
    ],
    images: [{ src: 'help/10-battle-log.png', alt: 'The battle log listing each shot with its dice and damage' }],
  },
  {
    id: 'next',
    title: 'Next turn',
    body: [
      'When everyone has finished firing, the host taps Advance Turn. The top bar moves on to the next turn in Order entry, and you plot again.',
      'Damage, ammunition and fighter states carry over from turn to turn.',
    ],
    images: [{ src: 'help/11-next-turn.png', alt: 'The top bar showing order entry for the next turn' }],
  },
  {
    id: 'words',
    title: 'Words the app uses',
    body: [
      'mu: measurement units, whatever your table measures in (inches, centimetres, squares).',
      'Course: a clock face. Ships turn a clock point at a time to port (left) or starboard (right).',
      'Thrust: what a ship can spend each turn on changing speed and turning. Speed change is how much faster or slower it will go.',
      'Arcs: where a weapon can point. F is fore, FS and FP are fore starboard and fore port, AS and AP are aft starboard and aft port.',
      'Fire control (firecon): how many targets a ship can engage in a turn.',
      'Screens: shields that make beam hits count for less.',
      'Threshold check: when a ship loses a row of hull boxes it rolls to see which systems fail. The results are in the Log.',
      'Initiative: who fires next. The sides take turns, one ship at a time.',
    ],
    images: [],
  },
];

/**
 * The step a player at this point in a match needs next.
 *
 * Read off the match's own state rather than guessed: no match yet is joining, a match without a
 * playable profile is the rules step, and each phase after that is the step that acts in it.
 */
export function helpStepFor(match: { phase: string; rulesPlayable: boolean; shipCount: number } | null): string {
  if (!match) {
    return 'join';
  }

  switch (match.phase) {
    case 'FleetSetup':
      return !match.rulesPlayable ? 'rules' : match.shipCount === 0 ? 'fleet' : 'ready';
    case 'OrderEntry':
      return 'plot';
    case 'OrdersLocked':
      return 'lock';
    case 'Reveal':
    case 'Movement':
      return 'move';
    case 'Firing':
      return 'fire';
    default:
      return 'join';
  }
}

export function HelpGuide({ currentStepId, onClose }: { currentStepId: string; onClose: () => void }) {
  const startIndex = Math.max(0, helpSteps.findIndex((step) => step.id === currentStepId));
  const [index, setIndex] = useState(startIndex);
  const headingRef = useRef<HTMLHeadingElement | null>(null);
  const step = helpSteps[index];

  useEffect(() => {
    headingRef.current?.focus();
    // On a phone the step list is one scrolling row; keep the open step in it visible.
    document.querySelector('.help-steps [aria-current="step"]')?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
  }, [index]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose();
      }
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div className="help-backdrop" onClick={onClose}>
      <div
        className="help-guide"
        role="dialog"
        aria-modal="true"
        aria-labelledby="help-guide-title"
        onClick={(event) => event.stopPropagation()}
      >
        <div className="help-head">
          <span className="label" id="help-guide-title">How to play</span>
          <button className="ghost" type="button" onClick={onClose} aria-label="Close how to play">Close</button>
        </div>
        <ol className="help-steps" aria-label="Steps of a match">
          {helpSteps.map((item, itemIndex) => (
            <li key={item.id}>
              <button
                type="button"
                className={['ghost', itemIndex === index ? 'active' : '', item.id === currentStepId ? 'current' : ''].filter(Boolean).join(' ')}
                aria-current={itemIndex === index ? 'step' : undefined}
                onClick={() => setIndex(itemIndex)}
              >
                {item.title}
                {item.id === currentStepId ? <em>You are here</em> : null}
              </button>
            </li>
          ))}
        </ol>
        <section className="help-body" aria-live="polite">
          <h2 ref={headingRef} tabIndex={-1}>
            {step.title}
            {step.id === currentStepId ? <em className="help-here">You are here</em> : null}
          </h2>
          {step.body.map((paragraph) => <p key={paragraph}>{paragraph}</p>)}
          <div className="help-images">
            {step.images.map((image) => (
              <figure key={image.src} className={image.phone ? 'phone' : ''}>
                <img src={`${import.meta.env.BASE_URL}${image.src}`} alt={image.alt} loading="lazy" />
                <figcaption>{image.alt}</figcaption>
              </figure>
            ))}
          </div>
          <div className="help-nav">
            <button className="ghost" type="button" disabled={index === 0} onClick={() => setIndex(index - 1)}>Back</button>
            <button type="button" disabled={index === helpSteps.length - 1} onClick={() => setIndex(index + 1)}>Next</button>
          </div>
        </section>
      </div>
    </div>
  );
}

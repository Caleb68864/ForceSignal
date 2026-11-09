// @vitest-environment jsdom
/**
 * The how-to-play guide: it opens where the player is, every picture it names is there, and it
 * states no number nobody entered.
 */

import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { HelpGuide, helpStepFor, helpSteps } from './HelpGuide.tsx';

afterEach(cleanup);

const pictures = import.meta.glob('../../public/help/*.png', { query: '?url', import: 'default', eager: true }) as Record<string, string>;
const pictureNames = Object.keys(pictures).map((path) => `help/${path.slice(path.lastIndexOf('/') + 1)}`).sort();

describe('which step a player needs', () => {
  it('follows the match from joining to firing', () => {
    expect(helpStepFor(null)).toBe('join');
    expect(helpStepFor({ phase: 'FleetSetup', rulesPlayable: false, shipCount: 0 })).toBe('rules');
    expect(helpStepFor({ phase: 'FleetSetup', rulesPlayable: true, shipCount: 0 })).toBe('fleet');
    expect(helpStepFor({ phase: 'FleetSetup', rulesPlayable: true, shipCount: 3 })).toBe('ready');
    expect(helpStepFor({ phase: 'OrderEntry', rulesPlayable: true, shipCount: 3 })).toBe('plot');
    expect(helpStepFor({ phase: 'OrdersLocked', rulesPlayable: true, shipCount: 3 })).toBe('lock');
    expect(helpStepFor({ phase: 'Reveal', rulesPlayable: true, shipCount: 3 })).toBe('move');
    expect(helpStepFor({ phase: 'Movement', rulesPlayable: true, shipCount: 3 })).toBe('move');
    expect(helpStepFor({ phase: 'Firing', rulesPlayable: true, shipCount: 3 })).toBe('fire');
  });

  it('only ever names a step that exists', () => {
    const ids = new Set(helpSteps.map((step) => step.id));
    for (const phase of ['FleetSetup', 'OrderEntry', 'OrdersLocked', 'Reveal', 'Movement', 'Firing', 'Something new']) {
      expect(ids.has(helpStepFor({ phase, rulesPlayable: true, shipCount: 1 })), phase).toBe(true);
    }
  });
});

describe('the pictures', () => {
  it('are all in public/help, and every one there is used', () => {
    // Reached-the-subject: the glob really found the folder.
    expect(pictureNames.length).toBeGreaterThan(5);
    const named = helpSteps.flatMap((step) => step.images.map((image) => image.src)).sort();
    expect(named.filter((src) => !pictureNames.includes(src))).toEqual([]);
    expect(pictureNames.filter((src) => !named.includes(src))).toEqual([]);
  });

  it('each say in words what they show', () => {
    for (const image of helpSteps.flatMap((step) => step.images)) {
      expect(image.alt.length, image.src).toBeGreaterThan(15);
    }
  });
});

describe('the guide', () => {
  it('opens on the step the match is at, marked as such, with focus on its title', () => {
    render(<HelpGuide currentStepId="fire" onClose={() => undefined} />);
    const heading = screen.getByRole('heading', { level: 2 });
    expect(heading.textContent).toContain('Fire');
    expect(heading.textContent).toContain('You are here');
    expect(document.activeElement).toBe(heading);
    expect(screen.getByRole('dialog', { name: 'How to play' })).toBeTruthy();
  });

  it('walks forward and back, and jumps from the list', () => {
    render(<HelpGuide currentStepId="join" onClose={() => undefined} />);
    expect(screen.getByRole('button', { name: 'Back' })).toHaveProperty('disabled', true);
    act(() => {
      fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    });
    expect(screen.getByRole('heading', { level: 2 }).textContent).toBe(helpSteps[1].title);
    act(() => {
      fireEvent.click(screen.getByRole('button', { name: helpSteps.at(-1)?.title }));
    });
    expect(screen.getByRole('button', { name: 'Next' })).toHaveProperty('disabled', true);
  });

  it('closes on Escape, on Close, and on the backdrop but not on its own content', () => {
    const onClose = vi.fn();
    const { container } = render(<HelpGuide currentStepId="plot" onClose={onClose} />);
    fireEvent.click(screen.getByRole('dialog'));
    expect(onClose).not.toHaveBeenCalled();
    fireEvent.keyDown(document, { key: 'Escape' });
    fireEvent.click(screen.getByRole('button', { name: 'Close how to play' }));
    const backdrop = container.querySelector('.help-backdrop');
    if (backdrop) {
      fireEvent.click(backdrop);
    }
    expect(onClose).toHaveBeenCalledTimes(3);
  });
});

/**
 * Every digit the guide says, with why it is not a rules number - the same audit
 * contentPolicy.render.test.tsx runs over the panels. Anything new fails here until classified.
 */
const accountedFor: Record<string, string> = {
  '12': 'the course clock face this app uses: 12 is up the table. A convention of the screen, not a rule',
  '3': 'the same clock face: 3 is to the right',
  '1': 'the Port +1 / Starboard +1 button labels, which turn by one clock point',
};

describe('the guide states no rules numbers', () => {
  it('has every digit in its text accounted for', () => {
    const text = helpSteps.flatMap((step) => [step.title, ...step.body, ...step.images.map((image) => image.alt)]).join(' ');
    const digits = new Set(text.match(/\d+/g) ?? []);
    expect([...digits].filter((digit) => !(digit in accountedFor))).toEqual([]);
  });
});

// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { blankRulesProfile, type RulesProfile } from '../types.ts';
import { RulesProfileEditor } from './RulesProfileEditor.tsx';

/**
 * Numbers invented for this test. ForceSignal ships none, and neither does its test suite - these
 * exist only so there is something recognisable to watch move from the table into the form.
 */
const tableProfile = (): RulesProfile => ({
  ...blankRulesProfile,
  name: 'Invented Layer',
  dieFaces: 8,
  beamDamage: [{ dieFace: 8, screenLevel: 0, damage: 2 }],
  beamRangeBandWidth: 10,
  thresholdRowCount: 3,
});

function openTheNumbers() {
  fireEvent.click(screen.getByRole('button', { name: 'Edit numbers' }));
  return screen.getByLabelText('Name') as HTMLInputElement;
}

describe('RulesProfileEditor', () => {
  afterEach(() => {
    cleanup();
    localStorage.clear();
  });

  it('picks up the profile that arrives after it is already on screen', () => {
    const onApply = vi.fn();

    // The order every real session takes: the setup panel renders, and the first snapshot lands a
    // moment later. Seeding the form once on mount meant it was seeded blank and stayed that way,
    // so a match that already had a profile showed zeros for every number in it.
    const view = render(<RulesProfileEditor value={blankRulesProfile} editable onApply={onApply} />);
    expect(openTheNumbers().value).toBe('');

    view.rerender(<RulesProfileEditor value={tableProfile()} editable onApply={onApply} />);

    expect((screen.getByLabelText('Name') as HTMLInputElement).value).toBe('Invented Layer');
    expect((screen.getByLabelText('Die faces') as HTMLInputElement).value).toBe('8');
  });

  it('leaves half-typed numbers alone when a snapshot brings the same profile back', () => {
    const onApply = vi.fn();
    const view = render(<RulesProfileEditor value={tableProfile()} editable onApply={onApply} />);

    const name = openTheNumbers();
    fireEvent.change(name, { target: { value: 'Half Typed' } });

    // A snapshot arrives on every mutation anyone at the table makes, each carrying a freshly
    // parsed profile object. Following the table by identity rather than by content would wipe the
    // form every time the opponent nudged a ship.
    view.rerender(<RulesProfileEditor value={tableProfile()} editable onApply={onApply} />);

    expect((screen.getByLabelText('Name') as HTMLInputElement).value).toBe('Half Typed');
  });
});

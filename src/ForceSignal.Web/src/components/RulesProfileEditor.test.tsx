// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
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

  /**
   * The rules profile is the one thing this app makes the player type by hand, from their own
   * rulebook - thirty fields including the beam damage table - and by this project's own policy it
   * is their data. An import that could not be read used to replace the whole draft with zeros:
   * no message, no confirmation, no undo, behind a panel that is collapsed by default. "I could
   * not read your file" is never "you meant to start again".
   */
  function pickFile(view: ReturnType<typeof render>, contents: string | Error) {
    const input = view.container.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File([typeof contents === 'string' ? contents : ''], 'picked.json', { type: 'application/json' });
    // jsdom's File implements no Blob.text(), so the one method the component calls is supplied
    // here - including the case where reading the file itself fails, which is what an unreadable
    // card or a file that went away between the picker and the read looks like.
    Object.defineProperty(file, 'text', {
      value: () => (typeof contents === 'string' ? Promise.resolve(contents) : Promise.reject(contents)),
    });
    fireEvent.change(input, { target: { files: [file] } });
  }

  it('keeps the numbers on screen when the file it was given cannot be read', async () => {
    const view = render(<RulesProfileEditor value={tableProfile()} editable onApply={vi.fn()} />);
    openTheNumbers();

    pickFile(view, new Error('the file could not be read'));

    // The numbers first, because losing them is the harm; the message second, because saying
    // nothing about it is the other half.
    await waitFor(() => expect((screen.getByLabelText('Name') as HTMLInputElement).value).toBe('Invented Layer'));
    expect((screen.getByLabelText('Die faces') as HTMLInputElement).value).toBe('8');
    expect(screen.getByRole('alert').textContent).toMatch(/could not be read/i);
  });

  it('keeps them when the file parses but is not a rules profile', async () => {
    // The likelier mistake at a table: the right sort of file, the wrong one. A fleet export parses
    // perfectly well and carries none of these fields, and reading it as a profile would blank
    // every number in the same silence.
    const view = render(<RulesProfileEditor value={tableProfile()} editable onApply={vi.fn()} />);
    openTheNumbers();

    pickFile(view, JSON.stringify({ formatVersion: 1, side: 'blue', units: [] }));

    await waitFor(() => expect(screen.getByRole('alert')).toBeTruthy());
    expect((screen.getByLabelText('Name') as HTMLInputElement).value).toBe('Invented Layer');
    expect((screen.getByLabelText('Die faces') as HTMLInputElement).value).toBe('8');
    expect((screen.getByLabelText('Range band (mu)') as HTMLInputElement).value).toBe('10');
  });

  it('still loads a profile it can read, and clears the complaint', async () => {
    const view = render(<RulesProfileEditor value={tableProfile()} editable onApply={vi.fn()} />);
    openTheNumbers();

    pickFile(view, 'not JSON');
    await waitFor(() => expect(screen.getByRole('alert')).toBeTruthy());

    pickFile(view, JSON.stringify({ ...tableProfile(), name: 'From A File', dieFaces: 10 }));

    await waitFor(() => expect((screen.getByLabelText('Name') as HTMLInputElement).value).toBe('From A File'));
    expect((screen.getByLabelText('Die faces') as HTMLInputElement).value).toBe('10');
    expect(screen.queryByRole('alert')).toBeNull();
  });
});

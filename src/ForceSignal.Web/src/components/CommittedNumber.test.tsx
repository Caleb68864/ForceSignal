// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CommittedNumber } from './CommittedNumber.tsx';

function renderField(value: number, onCommit = vi.fn()) {
  const view = render(<CommittedNumber value={value} min={0} max={24} onCommit={onCommit} />);
  const field = screen.getByRole('spinbutton') as HTMLInputElement;
  return { ...view, field, onCommit };
}

describe('CommittedNumber', () => {
  afterEach(cleanup);

  it('does not report while the player is still typing', () => {
    const { field, onCommit } = renderField(6);

    act(() => field.focus());
    fireEvent.change(field, { target: { value: '9' } });

    expect(onCommit).not.toHaveBeenCalled();
    expect(field.value).toBe('9');
  });

  it('commits on blur, clamped into range', () => {
    const { field, onCommit, rerender } = renderField(6);

    act(() => field.focus());
    fireEvent.change(field, { target: { value: '99' } });
    act(() => field.blur());

    expect(onCommit).toHaveBeenCalledWith(24);
    // The field is a view of the ship once editing ends, so the clamped value shows only when the
    // owner hands it back.
    expect(field.value).toBe('6');
    rerender(<CommittedNumber value={24} min={0} max={24} onCommit={onCommit} />);
    expect(field.value).toBe('24');
  });

  it('commits on Enter', () => {
    const { field, onCommit } = renderField(6);

    act(() => field.focus());
    fireEvent.change(field, { target: { value: '12' } });
    fireEvent.keyDown(field, { key: 'Enter' });

    expect(onCommit).toHaveBeenCalledWith(12);
  });

  it('does not commit a value that did not change', () => {
    const { field, onCommit } = renderField(6);

    act(() => field.focus());
    fireEvent.change(field, { target: { value: '6' } });
    act(() => field.blur());

    expect(onCommit).not.toHaveBeenCalled();
  });

  // Between edits the field is a view of the ship, and the ship changes under it - a snapshot
  // from the other player, an undo - so it must follow.
  it('follows the value it is given between edits', () => {
    const { field, rerender, onCommit } = renderField(6);

    rerender(<CommittedNumber value={3} min={0} max={24} onCommit={onCommit} />);

    expect(field.value).toBe('3');
  });
});

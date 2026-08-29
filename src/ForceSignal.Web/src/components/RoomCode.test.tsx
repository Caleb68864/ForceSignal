// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { RoomCode } from './RoomCode.tsx';

function setClipboard(clipboard: { writeText: (text: string) => Promise<void> } | undefined) {
  Object.defineProperty(navigator, 'clipboard', { value: clipboard, configurable: true });
}

describe('RoomCode', () => {
  afterEach(() => {
    cleanup();
    setClipboard(undefined);
  });

  it('copies the code and says so', async () => {
    const writeText = vi.fn(() => Promise.resolve());
    setClipboard({ writeText });
    render(<RoomCode code="ABCD" />);

    fireEvent.click(screen.getByRole('button', { name: 'Copy code' }));

    expect(await screen.findByText('Copied')).toBeTruthy();
    expect(writeText).toHaveBeenCalledWith('ABCD');
  });

  // Plain HTTP over the LAN has no clipboard API at all; the code is selected for the platform's
  // own copy gesture instead.
  it('selects the code on screen when there is no clipboard', async () => {
    setClipboard(undefined);
    render(<RoomCode code="ABCD" />);

    fireEvent.click(screen.getByRole('button', { name: 'Copy code' }));

    expect(await screen.findByText('Selected - press copy')).toBeTruthy();
    expect(window.getSelection()?.toString()).toBe('ABCD');
  });

  it('falls back the same way when the clipboard refuses', async () => {
    setClipboard({ writeText: () => Promise.reject(new Error('Denied')) });
    render(<RoomCode code="ABCD" />);

    fireEvent.click(screen.getByRole('button', { name: 'Copy code' }));

    expect(await screen.findByText('Selected - press copy')).toBeTruthy();
  });
});

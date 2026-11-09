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

describe('the invite link', () => {
  afterEach(() => {
    cleanup();
    setClipboard(undefined);
    Object.defineProperty(navigator, 'share', { value: undefined, configurable: true });
  });

  it('opens the app with the room code filled in', async () => {
    const { inviteLink } = await import('./RoomCode.tsx');
    expect(inviteLink('PIVOT-VIOLET', { origin: 'http://table.local:6297', pathname: '/' })).toBe('http://table.local:6297/?join=PIVOT-VIOLET');
  });

  it('goes through the phone\'s share sheet when there is one', async () => {
    const share = vi.fn(() => Promise.resolve());
    Object.defineProperty(navigator, 'share', { value: share, configurable: true });
    render(<RoomCode code="ABCD" />);

    fireEvent.click(screen.getByRole('button', { name: 'Share invite link' }));

    await vi.waitFor(() => expect(share).toHaveBeenCalledWith(expect.objectContaining({ url: expect.stringContaining('?join=ABCD') })));
  });

  it('copies the link where there is no share sheet', async () => {
    const writeText = vi.fn(() => Promise.resolve());
    setClipboard({ writeText });
    render(<RoomCode code="ABCD" />);

    fireEvent.click(screen.getByRole('button', { name: 'Share invite link' }));

    expect(await screen.findByText('Invite link copied')).toBeTruthy();
    expect(writeText).toHaveBeenCalledWith(expect.stringContaining('?join=ABCD'));
  });
});

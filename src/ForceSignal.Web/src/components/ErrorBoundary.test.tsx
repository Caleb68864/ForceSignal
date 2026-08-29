// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ErrorBoundary } from './ErrorBoundary.tsx';

const exportLastDeviceBackup = vi.fn(() => 'Backup written.');
vi.mock('../lib/backup.ts', () => ({ exportLastDeviceBackup: () => exportLastDeviceBackup() }));

function Faulty(): never {
  throw new Error('The map lost its bearings.');
}

describe('ErrorBoundary', () => {
  beforeEach(() => {
    // React reports a caught render error on the console as well; that is the boundary's own
    // record, not a test failure.
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    exportLastDeviceBackup.mockClear();
  });

  it('shows what went wrong instead of a blank page', () => {
    render(<ErrorBoundary><Faulty /></ErrorBoundary>);

    expect(screen.getByRole('alert').textContent).toContain('The map lost its bearings.');
    expect(screen.getByRole('button', { name: 'Export Last Device Backup' })).toBeTruthy();
  });

  it('offers the device backup from the fallback', () => {
    render(<ErrorBoundary><Faulty /></ErrorBoundary>);

    fireEvent.click(screen.getByRole('button', { name: 'Export Last Device Backup' }));

    expect(exportLastDeviceBackup).toHaveBeenCalledTimes(1);
    expect(screen.getByText('Backup written.')).toBeTruthy();
  });

  it('renders its children while nothing throws', () => {
    render(<ErrorBoundary><p>All quiet.</p></ErrorBoundary>);

    expect(screen.getByText('All quiet.')).toBeTruthy();
    expect(screen.queryByRole('alert')).toBeNull();
  });
});

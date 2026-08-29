/**
 * The last line between a render that throws and a blank page.
 *
 * Nothing persisted is lost when a render fails, but a player mid-turn had no path back except a
 * reload and no way to get at the last known state of the match first. This shows the error and
 * offers the device backup, which is the same file the auth screen offers.
 */

import { Component, type ErrorInfo, type ReactNode } from 'react';
import { exportLastDeviceBackup } from '../lib/backup.ts';

type State = { error: Error | null; note: string };

export class ErrorBoundary extends Component<{ children: ReactNode }, State> {
  state: State = { error: null, note: '' };

  static getDerivedStateFromError(error: unknown): Partial<State> {
    return { error: error instanceof Error ? error : new Error(String(error)) };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // The console is the only record of this on a device at a table.
    console.error('ForceSignal stopped rendering.', error, info.componentStack);
  }

  render() {
    if (!this.state.error) {
      return this.props.children;
    }

    return (
      <main className="shell">
        <section className="panel" role="alert" aria-label="ForceSignal stopped">
          <span className="label">Something went wrong</span>
          <h2>ForceSignal stopped rendering</h2>
          <p>{this.state.error.message || 'An unexpected error stopped the screen.'}</p>
          <p className="privacy">
            Your match is still on the server and your order keys are still on this device. Export
            the device backup, then reload the page.
          </p>
          <div className="quick-actions">
            <button type="button" onClick={() => this.setState({ note: exportLastDeviceBackup() })}>
              Export Last Device Backup
            </button>
            <button className="ghost" type="button" onClick={() => window.location.reload()}>Reload</button>
          </div>
          {this.state.note ? <p className="constraint-line" aria-live="polite">{this.state.note}</p> : null}
        </section>
      </main>
    );
  }
}

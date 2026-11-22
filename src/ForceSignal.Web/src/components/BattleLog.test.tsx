// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import type { MatchLogEntry } from '../types.ts';
import { BattleLog } from './BattleLog.tsx';

let sequence = 0;
const entry = (category: string, message: string): MatchLogEntry => ({
  sequence: (sequence += 1), timestamp: '2026-11-22T10:00:00Z', turnNumber: 3, phase: 'Firing', category, message,
});

describe('the battle record (playtest m15)', () => {
  afterEach(cleanup);

  it('folds a run of ship-state snapshots into one row that opens on a tap', () => {
    const entries = [
      entry('Fire', 'Valiant fired on Mark.'),
      ...Array.from({ length: 8 }, (_, index) => entry('Snapshot', `Ship ${index} pos 1,1`)),
      entry('Phase', 'Firing opened.'),
    ];
    const { container } = render(<BattleLog entries={entries} />);

    expect(container.querySelectorAll('.log-list > li')).toHaveLength(3);
    expect(screen.getByText(/8 ship states/)).toBeTruthy();
  });

  it('narrows to one kind of entry', () => {
    render(<BattleLog entries={[entry('Fire', 'Valiant fired on Mark.'), entry('Movement', 'Mark moved.'), entry('Setup', 'Blue joined.')]} />);

    fireEvent.change(screen.getByLabelText('Show'), { target: { value: 'fire' } });

    expect(screen.getByText('Valiant fired on Mark.')).toBeTruthy();
    expect(screen.queryByText('Mark moved.')).toBeNull();
    expect(screen.queryByText('Blue joined.')).toBeNull();
  });

  it('draws a page of rows at a time, with more on request', () => {
    const entries = Array.from({ length: 150 }, (_, index) => entry('Fire', `Shot ${index}`));
    const { container } = render(<BattleLog entries={entries} />);

    expect(container.querySelectorAll('.log-list > li')).toHaveLength(60);
    fireEvent.click(screen.getByText('Show 60 older'));
    expect(container.querySelectorAll('.log-list > li')).toHaveLength(120);
  });
});

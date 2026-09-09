// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import type { OrderPreview } from '../types.ts';
import { OrderPreviewNotice } from './OrderPreviewNotice.tsx';

function preview(extra: Partial<OrderPreview> = {}): OrderPreview {
  return {
    shipId: 'ship-1',
    isValid: true,
    errors: [],
    usableThrust: 4,
    thrustSpent: 2,
    maxTurnSteps: 2,
    startingVelocity: 6,
    startingCourse: 12,
    endingVelocity: 6,
    endingCourse: 1,
    segments: [],
    path: [],
    runsOffTable: false,
    ...extra,
  };
}

describe('OrderPreviewNotice', () => {
  afterEach(cleanup);

  it('says nothing about a plot that will lock', () => {
    const { container } = render(<OrderPreviewNotice preview={preview()} />);

    expect(container.textContent).toBe('');
  });

  it('says nothing when there is no preview yet', () => {
    const { container } = render(<OrderPreviewNotice preview={null} />);

    expect(container.textContent).toBe('');
  });

  // The whole point of the server computing these: a player finds out before pressing Lock, not by
  // pressing it. Every reason is shown, because the first one fixed is rarely the only one.
  it('gives every reason the server refused the plot for', () => {
    render(<OrderPreviewNotice preview={preview({
      isValid: false,
      errors: ['Thrust spent exceeds usable thrust.', 'Turn exceeds the ship thrust rating.'],
    })} />);

    const notice = screen.getByRole('status');
    expect(notice.textContent).toContain('will not lock');
    expect(notice.textContent).toContain('Thrust spent exceeds usable thrust.');
    expect(notice.textContent).toContain('Turn exceeds the ship thrust rating.');
    expect(screen.getAllByRole('listitem')).toHaveLength(2);
  });

  // Leaving the table is a decision a player may mean, so it is said without calling the plot broken.
  it('warns that a legal plot leaves the table without calling it invalid', () => {
    render(<OrderPreviewNotice preview={preview({ runsOffTable: true })} />);

    const notice = screen.getByRole('status');
    expect(notice.textContent).toContain('off the table');
    expect(notice.textContent).not.toContain('will not lock');
  });

  it('reports the refusal rather than the table edge when the plot is both', () => {
    render(<OrderPreviewNotice preview={preview({
      isValid: false,
      errors: ['Thrust spent exceeds usable thrust.'],
      runsOffTable: true,
    })} />);

    expect(screen.getByRole('status').textContent).toContain('will not lock');
  });
});

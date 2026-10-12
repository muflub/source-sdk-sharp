/*
 * The verdict line.
 *
 * Five situations produce a page with nothing or almost nothing on it, and they
 * mean opposite things: never fetched, no client in the process, nothing has
 * ever differed, everything differed but was forgiven, and things are genuinely
 * breaking. Three of those are FINE and two are not, and the comfortable
 * rendering — an empty list under a heading — is identical for all five.
 *
 * What this file defends is that a reader is never left to guess which one they
 * are in, and in particular that the best of the five ("nothing has ever
 * differed") does not read as the worst ("this view is not wired up").
 */

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { VerdictPane } from '../VerdictPane';
import type { Prediction } from '../../types';
import { drifting, prediction, record } from './fixtures';

const pane = (data: Prediction | null, records = data?.records ?? []) =>
  render(<VerdictPane data={data} records={records} />);

const state = (): string | null =>
  document.querySelector('.pred-verdict')?.getAttribute('data-state') ?? null;

describe('the verdict', () => {
  it('does not report a healthy game before anything has been read', () => {
    pane(null);

    expect(state()).toBe('unread');
    expect(screen.getByText(/not read yet/i)).toBeTruthy();
  });

  /*
   * A dedicated server has no client and prediction is client-side, so there is
   * nothing to compare. Rendered as an empty chart that would read as a server
   * whose every prediction is perfect.
   */
  it('says a dedicated server has no client, in its own words', () => {
    pane({
      available: false,
      note: 'no client in this process — prediction is client-side, so a dedicated server '
        + 'has nothing to report here',
      records: [],
    });

    expect(state()).toBe('unavailable');
    expect(document.querySelector('.pred-headline')!.textContent).toMatch(/no client in this process/i);
    expect(screen.getByText(/prediction is client-side/i)).toBeTruthy();
  });

  // The one that matters. An empty ring is prediction WORKING: a compare that
  // matched byte for byte is never recorded, so there is nothing to list.
  it('reads an empty ring as good news rather than as missing data', () => {
    pane(prediction({ observed: 0, mispredicted: 0, toleratedOnly: 0, records: [] }), []);

    expect(state()).toBe('clean');
    expect(screen.getByText(/prediction is holding/i)).toBeTruthy();
    expect(screen.getByText(/never recorded/i)).toBeTruthy();
  });

  it('does not call a clean game empty, unavailable or unread', () => {
    pane(prediction({ observed: 0, mispredicted: 0, records: [] }), []);

    expect(screen.queryByText(/no client in this process/i)).toBeNull();
    expect(screen.queryByText(/not read yet/i)).toBeNull();
  });

  it('separates differences that were all forgiven from ones that broke through', () => {
    pane(prediction({ observed: 400, mispredicted: 0, toleratedOnly: 400, records: [] }), []);

    expect(state()).toBe('tolerated');
    expect(screen.getByText(/inside the tolerance/i)).toBeTruthy();
  });

  it('calls a game with real mispredictions broken', () => {
    pane(prediction());

    expect(state()).toBe('breaking');
    expect(screen.getByText(/prediction is breaking/i)).toBeTruthy();
  });

  /*
   * The counters are of DIFFERING compares only. Labelled as a misprediction
   * rate, 3900 of 4021 would read as a client that gets nine commands in ten
   * wrong — when it may be getting 999 in 1000 right and simply never being
   * recorded for them.
   */
  it('labels the counters as differing compares, never as the game\'s rate', () => {
    pane(prediction());

    expect(screen.getByText(/3,900 broke through/)).toBeTruthy();
    expect(screen.getByText(/4,021 differing/)).toBeTruthy();
    expect(screen.getByText(/never recorded|not in the denominator|not of all/i)).toBeTruthy();
  });

  it('says how much of the ring is held, so a short chart is not read as a quiet game', () => {
    pane(prediction({ held: 137, capacity: 256 }));

    expect(screen.getByText(/holding 137 of 256/)).toBeTruthy();
  });

  it('draws the rate once there is enough to draw, and not before', () => {
    pane(prediction({ records: [record()] }), [record()]);
    expect(document.querySelector('.pred-rate')).toBeNull();

    pane(prediction(), drifting(8));
    expect(document.querySelector('.pred-rate')).toBeTruthy();
  });

  // A share auto-scaled to its own maximum draws a 2% rate as a chart that
  // touches the ceiling, so the axis is pinned at 0 and 100%.
  it('pins the rate axis at 0 and 100% rather than scaling it to what happened', () => {
    pane(prediction(), drifting(8));

    expect(screen.getByText('100%')).toBeTruthy();
    expect(screen.getByText('0%')).toBeTruthy();
  });

  /*
   * And the axis labels have to be true of the line under them. Auto-scaled to
   * its own maximum, a run in which one compare in eight broke through draws a
   * line that touches the top of a chart labelled 100% — a game breaking one
   * command in eight rendered as a game breaking every one of them.
   */
  it('draws a low rate low, under an axis that says 100%', () => {
    const mostly = Array.from({ length: 8 }, (_, index) => record({
      sequence: index, command: 100 + index * 4, mispredicted: index === 7,
    }));

    pane(prediction(), mostly);

    const path = document.querySelector('.pred-rate .pred-line')!.getAttribute('d')!;
    const ys = [...path.matchAll(/[ML] [\d.]+ ([\d.]+)/g)].map(match => Number(match[1]));
    const axis = document.querySelectorAll('.pred-rate .pred-axis');
    const top = Number(axis[1]!.getAttribute('y1'));
    const bottom = Number(axis[0]!.getAttribute('y1'));

    // Nothing on the line reaches the upper half of a chart whose ceiling is
    // every compare breaking. y grows downwards.
    expect(Math.min(...ys)).toBeGreaterThan((top + bottom) / 2);
  });
});

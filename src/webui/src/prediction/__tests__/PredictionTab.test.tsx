/*
 * The prediction tab: what it asks the game, and what it does with the answer.
 *
 * The polling is where this view can quietly lie about the thing it exists to
 * report. It follows along with `since`/`nextSequence` rather than refetching,
 * and a poll that asked from zero every second would append the same records
 * again on every tick — drawing one real misprediction sixty times and reporting
 * a game failing sixty times as often as it is. A game that restarts sends
 * sequences that begin again below where this side had got to, and appending
 * those to what is held interleaves two runs into a chart of something that
 * never happened.
 *
 * The rest is the five states that all look like an empty page and are not.
 */

import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { PredictionTab } from '../PredictionTab';
import { drifting, prediction, record } from './fixtures';

vi.mock('../../api', () => ({ getPrediction: vi.fn() }));

const api = await import('../../api') as unknown as {
  getPrediction: ReturnType<typeof vi.fn>;
};

beforeEach(() => {
  vi.clearAllMocks();
  api.getPrediction.mockResolvedValue(prediction());
});

/*
 * Opened with the live poll off.
 *
 * The tab polls every second by default, which is right in a game and wrong in
 * a test: a call count that depends on how long the suite took to get here is a
 * test that passes on a fast machine and fails on a busy one. The poll itself is
 * exercised deliberately, on fake timers, at the bottom of this file.
 */
const show = () => {
  history.replaceState(null, '', '/?predlive=0');
  return render(<PredictionTab visible />);
};

describe('the prediction tab', () => {
  it('asks the game nothing until somebody is looking at it', async () => {
    history.replaceState(null, '', '/?predlive=0');
    render(<PredictionTab visible={false} />);

    await Promise.resolve();
    expect(api.getPrediction).not.toHaveBeenCalled();
  });

  it('reads once the tab is opened', async () => {
    show();

    await waitFor(() => expect(api.getPrediction).toHaveBeenCalled());
  });

  /*
   * The follow-along. Asking from zero every time would append the same records
   * on every poll, and a chart of one misprediction drawn sixty times reads as
   * sixty mispredictions.
   */
  it('follows along from where it got to rather than refetching everything', async () => {
    show();

    await waitFor(() => expect(api.getPrediction).toHaveBeenCalledTimes(1));
    expect(api.getPrediction.mock.calls[0]![0]).toBe(0);

    api.getPrediction.mockResolvedValue(prediction({ nextSequence: 4100, records: [] }));
    await userEvent.click(screen.getByRole('button', { name: /refresh/i }));

    await waitFor(() => expect(api.getPrediction).toHaveBeenCalledTimes(2));
    expect(api.getPrediction.mock.calls[1]![0]).toBe(4021);
  });

  it('does not hold the same record twice when a poll returns it again', async () => {
    api.getPrediction.mockResolvedValue(prediction({ nextSequence: 4021, records: drifting(4) }));
    show();

    await waitFor(() => expect(screen.getByText(/4 record\(s\) held here/)).toBeTruthy());

    // The same reply again, because nothing new happened. The count must not
    // double: `since` moved past those records the first time.
    api.getPrediction.mockResolvedValue(prediction({ nextSequence: 4021, records: [] }));
    await userEvent.click(screen.getByRole('button', { name: /refresh/i }));

    await waitFor(() => expect(api.getPrediction).toHaveBeenCalledTimes(2));
    expect(screen.getByText(/4 record\(s\) held here/)).toBeTruthy();
  });

  /*
   * A restarted game numbers from the beginning again. Appended to what is held,
   * the two runs interleave into a drift chart of something that never happened.
   */
  it('starts over when the game does, rather than splicing two runs together', async () => {
    api.getPrediction.mockResolvedValue(prediction({ nextSequence: 4021, records: drifting(6) }));
    show();

    await waitFor(() => expect(screen.getByText(/6 record\(s\) held here/)).toBeTruthy());

    api.getPrediction.mockResolvedValue(prediction({ nextSequence: 2, records: [record({ sequence: 1 })] }));
    await userEvent.click(screen.getByRole('button', { name: /refresh/i }));

    await waitFor(() => expect(screen.getByText(/1 record\(s\) held here/)).toBeTruthy());
  });

  it('clears what it holds when the counters are reset', async () => {
    api.getPrediction.mockResolvedValue(prediction({ records: drifting(6) }));
    show();

    await waitFor(() => expect(screen.getByText(/6 record\(s\) held here/)).toBeTruthy());

    await userEvent.click(screen.getByRole('button', { name: /reset counters/i }));

    await waitFor(() => expect(screen.getByText(/0 record\(s\) held here/)).toBeTruthy());
    expect(api.getPrediction.mock.calls[1]![2]).toBe(true);
  });

  /*
   * A failed poll is not an empty game. Blanking the chart over one dropped
   * request throws away the drift somebody came here to look at, and an empty
   * chart reads as a game that has stopped mispredicting.
   */
  it('keeps what it has when a poll fails, and says the reading is old', async () => {
    api.getPrediction.mockResolvedValue(prediction({ records: drifting(4) }));
    show();

    await waitFor(() => expect(screen.getByText(/4 record\(s\) held here/)).toBeTruthy());

    api.getPrediction.mockRejectedValue(new Error('gone'));
    await userEvent.click(screen.getByRole('button', { name: /refresh/i }));

    await waitFor(() => expect(screen.getByText(/last read of/i)).toBeTruthy());
    expect(screen.getByText(/4 record\(s\) held here/)).toBeTruthy();
  });

  it('shows no panes for a process with no client, only the reason', async () => {
    api.getPrediction.mockResolvedValue({
      available: false,
      note: 'no client in this process — prediction is client-side, so a dedicated server '
        + 'has nothing to report here',
      records: [],
    });

    show();

    await waitFor(() => expect(screen.getByText(/prediction is client-side/i)).toBeTruthy());
    expect(document.querySelector('.pred-drift')).toBeNull();
    expect(document.querySelector('.pred-fields-table')).toBeNull();
  });

  it('reads a game that has never mispredicted as holding, not as unwired', async () => {
    api.getPrediction.mockResolvedValue(
      prediction({ observed: 0, mispredicted: 0, toleratedOnly: 0, records: [] }));

    show();

    await waitFor(() => expect(screen.getByText(/prediction is holding/i)).toBeTruthy());
    expect(screen.queryByText(/prediction is breaking/i)).toBeNull();
  });

  it('opens on the drift chart, which is the question somebody came with', async () => {
    api.getPrediction.mockResolvedValue(prediction({ records: drifting(6) }));
    show();

    await waitFor(() => expect(document.querySelector('.pred-drift')).toBeTruthy());
  });

  it('moves between its panes', async () => {
    api.getPrediction.mockResolvedValue(prediction({ records: drifting(6) }));
    show();

    await waitFor(() => expect(document.querySelector('.pred-drift')).toBeTruthy());

    await userEvent.click(screen.getByRole('button', { name: 'Fields' }));
    expect(document.querySelector('.pred-fields-table')).toBeTruthy();

    await userEvent.click(screen.getByRole('button', { name: 'One compare' }));
    expect(document.querySelector('.pred-record')).toBeTruthy();
  });

  // A view somebody set up is a link they can send. Which pane, which field and
  // which compare are all part of "it happens here".
  it('keeps the pane and the chosen field in the address', async () => {
    api.getPrediction.mockResolvedValue(prediction({ records: drifting(6) }));
    show();

    await waitFor(() => expect(document.querySelector('.pred-drift')).toBeTruthy());
    await userEvent.click(screen.getByRole('button', { name: 'Fields' }));

    expect(new URLSearchParams(location.search).get('predpane')).toBe('fields');
  });

  it('opens the compare behind a point clicked on the drift chart', async () => {
    api.getPrediction.mockResolvedValue(prediction({ records: drifting(6) }));
    show();

    await waitFor(() => expect(document.querySelector('.pred-drift')).toBeTruthy());

    await userEvent.click(document.querySelectorAll('.pred-point')[3]!);

    expect(document.querySelector('.pred-record')).toBeTruthy();
    expect(new URLSearchParams(location.search).get('predrec')).toBe('4003');
  });

  /*
   * A poll that kept running after the tab was left would go on costing the
   * game a request a second for a view nobody is looking at — and every other
   * tab in this page pays that bill too.
   */
  it('polls while it is being looked at, and stops when it is left', async () => {
    vi.useFakeTimers();
    history.replaceState(null, '', '/?predlive=1');

    try {
      const view = render(<PredictionTab visible />);
      await act(async () => { await vi.advanceTimersByTimeAsync(2500); });

      const whileOpen = api.getPrediction.mock.calls.length;
      expect(whileOpen).toBeGreaterThan(1);

      view.rerender(<PredictionTab visible={false} />);
      await act(async () => { await vi.advanceTimersByTimeAsync(5000); });

      expect(api.getPrediction.mock.calls.length).toBe(whileOpen);
    } finally {
      vi.useRealTimers();
    }
  });
});

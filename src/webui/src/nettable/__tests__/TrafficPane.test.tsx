/*
 * The traffic pane — Part 13's third panel, landed at Step 9.
 *
 * What matters here is the same thing that matters everywhere else in this tab:
 * what it REFUSES to say. Three different situations produce an empty table —
 * a host that never wired the view, a view that is not sampling, and a world in
 * which nothing moved — and they mean opposite things. A reader who cannot tell
 * them apart concludes the payload is free, which is exactly the wrong lesson
 * and the one this panel exists to prevent.
 */

import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { TrafficPane } from '../TrafficPane';
import type { NetTraffic } from '../../types';

const noop = (): void => {};

function pane(traffic: NetTraffic | null) {
  return render(
    <TrafficPane
      traffic={traffic}
      live={false}
      onLive={noop}
      onRefresh={noop}
      onReset={noop}
    />,
  );
}

const measured: NetTraffic = {
  available: true,
  sampling: true,
  elapsedSeconds: 14,
  trackedEntities: 336,
  bitsPerChangedWord: 39,
  windowSeconds: 10,
  unit: 'a changed payload dword costs 39 bits on the wire',
  fieldsNote: 'a word holding two changed fields is charged to both',
  classes: [
    {
      className: 'func_breakable_surf',
      entityFramesSeen: 5000,
      entityFramesChanged: 13,
      wordsTouched: 225,
      bytesChanged: 708,
      bytesPerSecond: 78.21,
      recentBytesPerSecond: 12,
      wordFill: 0.79,
      fields: [
        { name: 'Corner', words: 39, bytesPerSecond: 13.5 },
        { name: 'Normal', words: 30, bytesPerSecond: 10.4 },
      ],
    },
    {
      className: 'env_sprite',
      entityFramesSeen: 5000,
      entityFramesChanged: 0,
      wordsTouched: 0,
      bytesChanged: 0,
      bytesPerSecond: 0,
      recentBytesPerSecond: 0,
      wordFill: 0,
      fields: [],
    },
  ],
};

describe('the traffic pane', () => {
  it('says when the host does not report traffic at all', () => {
    pane({ available: false, classes: [] });

    expect(screen.getByText(/does not report payload traffic/i)).toBeTruthy();
  });

  it('says when it is wired but not sampling, and names the convar', () => {
    pane({ ...measured, sampling: false, classes: [] });

    // The distinction that matters: not sampling is not the same as quiet, and
    // a reader must be told which one they are looking at.
    expect(screen.getByText(/not sampling/i)).toBeTruthy();
    expect(screen.getByText(/cs_sv_nettable_profile 1/)).toBeTruthy();
  });

  it('distinguishes a quiet world from an unsampled one', () => {
    pane({ ...measured, sampling: true, classes: [] });

    expect(screen.getByText(/nothing has changed yet/i)).toBeTruthy();
    expect(screen.queryByText(/not sampling/i)).toBeNull();
  });

  it('reports words, bytes and fill for a class that moved', () => {
    pane(measured);

    expect(screen.getByText('func_breakable_surf')).toBeTruthy();
    expect(screen.getByText('225')).toBeTruthy();
    expect(screen.getByText('708')).toBeTruthy();
    expect(screen.getByText('79%')).toBeTruthy();
  });

  it('names the fields that cost the words', () => {
    pane(measured);

    expect(screen.getByText('Corner')).toBeTruthy();
    expect(screen.getByText('39w')).toBeTruthy();
    expect(screen.getByText('Normal')).toBeTruthy();
  });

  it('leaves out classes that cost nothing, so the table is what moved', () => {
    pane(measured);

    // env_sprite was sampled and never changed. Listing it would bury the two
    // rows that matter under two hundred that do not.
    expect(screen.queryByText('env_sprite')).toBeNull();
  });

  it('states the unit, because bytes is the intuitive and wrong reading', () => {
    pane(measured);

    expect(screen.getByText(/changed payload dword costs 39 bits/i)).toBeTruthy();
    expect(screen.getByText(/charged to both/i)).toBeTruthy();
  });

  it('offers a reset, so a reader can get a clean window', async () => {
    const onReset = vi.fn();

    render(
      <TrafficPane
        traffic={measured}
        live={false}
        onLive={noop}
        onRefresh={noop}
        onReset={onReset}
      />,
    );

    screen.getByRole('button', { name: /reset totals/i }).click();
    expect(onReset).toHaveBeenCalled();
  });
});

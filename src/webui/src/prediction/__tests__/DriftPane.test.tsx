/*
 * The drift chart — the pane the tab exists for.
 *
 * The console prints "11 byte(s) differ" and then "14 byte(s) differ" and a
 * reader cannot tell from those whether the player is wobbling harmlessly
 * around the server's position or walking away from it. Those are opposite
 * diagnoses and the byte count is blind to both, because it counts bytes that
 * differ rather than distance.
 *
 * So what this file defends is the answer being GIVEN rather than left to be
 * eyeballed off a line: the word "growing", the tolerance drawn across the chart
 * as the thing the line is being judged against, and — the failure that would be
 * invisible — a tolerance that is off the top of the plot, which leaves a rising
 * curve with nothing to measure it by and reads as catastrophe on a field that
 * is behaving itself.
 */

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { DriftPane } from '../DriftPane';
import type { PredictionRecord } from '../../types';
import { drifting, field, record, withinField } from './fixtures';

const noop = (): void => {};

function pane(records: PredictionRecord[], selected: number | null = null, onSelect = noop) {
  return render(
    <DriftPane
      records={records}
      field="Origin"
      onField={noop}
      selected={selected}
      onSelect={onSelect}
    />,
  );
}

const trendLine = (): HTMLElement => document.querySelector('.pred-trend') as HTMLElement;

describe('the drift chart', () => {
  it('says an empty ring is the good answer rather than drawing an empty chart', () => {
    pane([]);

    expect(screen.getByText(/never recorded|good answer/i)).toBeTruthy();
    expect(document.querySelector('.pred-drift')).toBeNull();
  });

  /*
   * The question the log cannot answer, answered in a word. A reader should not
   * have to judge a slope by eye — that is exactly the judgement that goes
   * wrong, and the one this pane was built to make for them.
   */
  it('says in words whether the difference is growing', () => {
    pane(drifting(8));

    expect(trendLine().getAttribute('data-direction')).toBe('growing');
    expect(trendLine().textContent).toMatch(/growing/);
  });

  it('calls a difference that is not moving steady, not growing', () => {
    const flat = Array.from({ length: 8 }, (_, index) => record({
      sequence: index,
      command: 23800 + index * 4,
      fields: [withinField({ maxAbsDelta: 0.2, toleranceRatio: 0.4 })],
    }));

    render(
      <DriftPane records={flat} field="Velocity" onField={noop} selected={null} onSelect={noop} />,
    );

    expect(trendLine().getAttribute('data-direction')).toBe('steady');
  });

  it('measures the rise against the tolerance, which is what makes it actionable', () => {
    pane(drifting(8));

    expect(screen.getByText(/x the tolerance/)).toBeTruthy();
  });

  // Without the reference line the chart is a rising curve and nothing else,
  // and every rising curve reads as a bug.
  it('draws the declared tolerance across the chart, labelled', () => {
    pane(drifting(4));

    expect(document.querySelector('.pred-tolerance line')).toBeTruthy();
    expect(document.querySelector('.pred-tolerance text')!.textContent).toMatch(/tolerance 0\.0625/);
  });

  /*
   * The silent failure. A tolerance well above every sample, scaled out of the
   * plot, leaves the reader with a line and no yardstick — and the field in
   * question is the one that is FINE.
   */
  it('keeps the tolerance on the chart even when every sample is far below it', () => {
    const tiny = Array.from({ length: 6 }, (_, index) => record({
      sequence: index,
      command: 23800 + index * 4,
      fields: [field({ tolerance: 100, maxAbsDelta: 0.001, toleranceRatio: 0.00001 })],
    }));

    pane(tiny);

    // The ceiling of the axis is above the tolerance, which is what puts the
    // line on the chart. Scaled to the samples alone it would read 1.2e-3 and
    // the tolerance would be somewhere off the top with the reader left
    // looking at a rising line and nothing to judge it by.
    expect(screen.getByText('115.000')).toBeTruthy();

    const y = Number(document.querySelector('.pred-tolerance line')!.getAttribute('y1'));
    const points = [...document.querySelectorAll('.pred-point')]
      .map(point => Number(point.getAttribute('cy')));

    // Above every sample, and y grows downwards.
    expect(points.every(cy => cy > y)).toBe(true);
  });

  /*
   * A field with no tolerance has no line to be under, and drawing one at zero
   * would say the opposite of the truth — that everything is over. It gets said
   * in words instead.
   */
  it('says when there is no tolerance rather than drawing one at zero', () => {
    const undeclared = drifting(4).map(item => record({
      ...item,
      fields: [field({ tolerance: 0, tolerable: true, toleranceRatio: null })],
    }));

    pane(undeclared);

    expect(screen.getByText(/tolerance is declared for/)).toBeTruthy();
    expect(document.querySelector('.pred-tolerance')).toBeNull();
  });

  it('marks the samples that are over the tolerance apart from the ones under it', () => {
    pane(drifting(8));

    const over = document.querySelectorAll('.pred-point[data-over="true"]');
    const under = document.querySelectorAll('.pred-point[data-over="false"]');

    expect(over.length).toBeGreaterThan(0);
    expect(under.length).toBeGreaterThan(0);
  });

  it('opens the compare behind a point when it is clicked', async () => {
    const onSelect = vi.fn();
    pane(drifting(4), null, onSelect);

    const point = document.querySelectorAll('.pred-point')[2] as SVGCircleElement;
    await userEvent.click(point);

    expect(onSelect).toHaveBeenCalledWith(4002);
  });

  it('marks the compare that is open, so the chart and the detail agree', () => {
    pane(drifting(4), 4002);

    expect(document.querySelectorAll('.pred-point[data-selected="true"]')).toHaveLength(1);
  });

  it('says how far out the worst sample was, in the field\'s own units', () => {
    pane(drifting(8));

    expect(screen.getByText(/worst 0\.400/)).toBeTruthy();
  });

  it('offers every field that has ever differed, not only the last one', async () => {
    const mixed = [record({ fields: [field(), withinField()] })];
    pane(mixed);

    const options = [...document.querySelectorAll('option')].map(option => option.textContent);

    expect(options).toContain('Origin');
    expect(options).toContain('Velocity');
  });
});

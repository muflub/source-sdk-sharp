/*
 * One compare, opened up.
 *
 * This is where "10 byte(s) differ, first at offset 56" turns into numbers
 * somebody can act on: what the client predicted, what the server said, and the
 * difference — with each component drawn against the tolerance its field
 * declared rather than against its neighbours. Scaled against each other the
 * longest bar is full width whether it is a millimetre or a mile out, which is
 * the wrong answer rendered beautifully.
 *
 * Two shapes have to survive intact and both look like a bug from the outside: a
 * field with no components (a string, or padding — the bytes differ and a
 * distance is not a thing they have) and a record that was kept even though
 * nothing in it broke through, because a tolerated difference that is growing is
 * the only early warning there is.
 */

import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { RecordPane } from '../RecordPane';
import type { PredictionRecord } from '../../types';
import { drifting, field, paddingField, record, stringField, withinField } from './fixtures';

const noop = (): void => {};

const pane = (records: PredictionRecord[], selected: number | null = null, onSelect = noop) =>
  render(<RecordPane records={records} selected={selected} onSelect={onSelect} />);

const bars = (): HTMLElement[] => [...document.querySelectorAll('.pred-bar-fill')] as HTMLElement[];

describe('one compare', () => {
  it('says an empty ring is the good answer rather than nothing to show', () => {
    pane([]);

    expect(screen.getByText(/good answer/i)).toBeTruthy();
  });

  it('shows what was predicted, what the server said, and the difference', () => {
    pane([record()]);

    expect(screen.getByText('123.450')).toBeTruthy();
    expect(screen.getByText('123.510')).toBeTruthy();
    expect(screen.getByText('-0.0600')).toBeTruthy();
  });

  // Counting columns to find which of three numbers is the yaw is exactly the
  // work this pane exists to remove.
  it('names the axes rather than numbering them', () => {
    pane([record()]);

    expect(screen.getByText('x')).toBeTruthy();
    expect(screen.getByText('y')).toBeTruthy();
    expect(screen.getByText('z')).toBeTruthy();
  });

  /*
   * The reading the bars are for. Two of these three components are inside a
   * tolerance of 0.0625 and one is five times past it; "10 byte(s) differ" says
   * one field is wrong and nothing about which part of it.
   */
  it('marks only the component that is past the tolerance', () => {
    pane([record()]);

    const over = bars().map(bar => bar.getAttribute('data-over'));

    expect(over).toEqual(['false', 'false', 'true']);
  });

  it('draws the tolerance mark on every bar, not only on the ones that crossed it', () => {
    pane([record()]);

    const marks = [...document.querySelectorAll('.pred-bar-mark')]
      .filter(mark => !(mark as HTMLElement).hidden);

    expect(marks).toHaveLength(3);
  });

  /*
   * Scaled against the tolerance, not against the other components. Against
   * each other, the worst bar is full width whether it is 0.31 out of an
   * allowed 0.0625 or 0.31 out of an allowed 10 — and those are opposite
   * diagnoses.
   */
  it('scales the bars against the tolerance rather than against each other', () => {
    pane([record()]);

    const widths = bars().map(bar => bar.style.width);

    // 0.06 of an allowed 0.0625 is just under the halfway mark; 0.31 is five
    // times over and runs off the end.
    expect(Number.parseFloat(widths[0]!)).toBeGreaterThan(40);
    expect(Number.parseFloat(widths[0]!)).toBeLessThan(50);
    expect(widths[2]).toBe('100%');
  });

  it('says the ratio next to every component, so the bar is never the only source', () => {
    pane([record()]);

    expect(screen.getByText('4.96x over')).toBeTruthy();
  });

  it('has no numbers to show for a string, and says so instead of showing zeroes', () => {
    pane([record({ fields: [stringField()] })]);

    expect(bars()).toHaveLength(0);
    expect(screen.getByText(/not a thing this field has/i)).toBeTruthy();
  });

  it('explains padding rather than blaming a field for a byte nobody claims', () => {
    pane([record({ fields: [paddingField()] })]);

    expect(screen.getByText(/no field claims these bytes/i)).toBeTruthy();
  });

  /*
   * A record with nothing broken in it is not noise. It is a tolerated
   * difference, and a tolerated difference that is GROWING is the early
   * warning — so it has to read as kept on purpose rather than as a
   * misprediction that failed to explain itself.
   */
  it('reads a tolerated compare as prediction working, and says why it was kept', () => {
    pane([record({ mispredicted: false, fields: [withinField()] })]);

    expect(screen.getByText(/every difference in this compare was inside/i)).toBeTruthy();
    expect(screen.getByText(/early warning/i)).toBeTruthy();
  });

  it('says plainly when a compare genuinely broke through', () => {
    pane([record()]);

    expect(screen.getByText(/genuinely apart/i)).toBeTruthy();
  });

  it('says where the first difference was, and how big the blob is', () => {
    pane([record()]);

    expect(screen.getByText('56')).toBeTruthy();
    expect(screen.getByText(/of 120 byte\(s\)/)).toBeTruthy();
  });

  // The log line that sent somebody here is the LAST one, so an unselected pane
  // opening on the oldest record in the ring would show the wrong compare.
  it('opens on the newest compare when none was chosen', () => {
    pane(drifting(5));

    expect(screen.getByText('23816')).toBeTruthy();
  });

  it('opens the compare that was chosen instead', () => {
    pane(drifting(5), 4001);

    expect(screen.getByText('23804')).toBeTruthy();
  });

  it('lists the compares, marking which of them broke through', () => {
    pane(drifting(5));

    const items = [...document.querySelectorAll('.pred-record-item')];

    expect(items).toHaveLength(5);
    expect(items.some(item => item.getAttribute('data-broke') === 'true')).toBe(true);
    expect(items.some(item => item.getAttribute('data-broke') === 'false')).toBe(true);
  });

  it('lists the newest compare first', () => {
    pane(drifting(5));

    const first = document.querySelector('.pred-record-item')!;

    expect(within(first as HTMLElement).getByText('cmd 23816')).toBeTruthy();
  });

  it('opens a compare picked from the list', async () => {
    const onSelect = vi.fn();
    pane(drifting(5), null, onSelect);

    await userEvent.click(document.querySelectorAll('.pred-record-item')[1]!);

    expect(onSelect).toHaveBeenCalledWith(4003);
  });

  it('shows every field of the compare, not only the first', () => {
    pane([record({ fields: [field(), withinField(), paddingField()] })]);

    expect(document.querySelectorAll('.pred-field')).toHaveLength(3);
  });
});

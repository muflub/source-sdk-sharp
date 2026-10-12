/*
 * The field table.
 *
 * The console names the fields — "Origin, Velocity" — and stops. What it never
 * says is which of them is the problem, and the obvious table gets that
 * backwards: sorted by how often a field appears, a field comfortably inside its
 * tolerance five hundred times sits above a field a hundred times past its
 * tolerance once, and the reader starts by investigating the one that is
 * working.
 *
 * The other thing defended here is what a missing ratio is allowed to look
 * like. A field that declared no tolerance and a field whose KIND can never
 * carry one both have no ratio, and both are different again from a field with a
 * ratio of nearly zero. Printed as "0.00x" all three read as fine, and one of
 * them means nothing was ever forgiving this.
 */

import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { FieldsPane } from '../FieldsPane';
import type { PredictionRecord } from '../../types';
import { field, paddingField, record, withinField } from './fixtures';

const noop = (): void => {};

const pane = (records: PredictionRecord[], onField = noop, selected = '') =>
  render(<FieldsPane records={records} field={selected} onField={onField} />);

const rows = (): HTMLElement[] => [...document.querySelectorAll('.pred-field-row')] as HTMLElement[];

describe('the field table', () => {
  // An empty table is prediction WORKING, and the ordinary rendering of an
  // empty table ("no data") says the opposite.
  it('reads an empty table as the good answer rather than as nothing to show', () => {
    pane([]);

    expect(screen.getByText(/good answer|Nothing is wrong/i)).toBeTruthy();
    expect(document.querySelector('table')).toBeNull();
  });

  it('shows what each field declared and how far past it the worst compare went', () => {
    pane([record()]);

    const row = within(rows()[0]!);

    expect(row.getByText('Origin')).toBeTruthy();
    expect(row.getByText('0.0625')).toBeTruthy();
    expect(row.getByText('4.96x over')).toBeTruthy();
  });

  /*
   * The ordering rule. A field far past its tolerance once is a bug; a field
   * inside its tolerance five hundred times is prediction working.
   */
  it('puts the field that broke above the field that merely appears often', () => {
    const held = Array.from({ length: 50 }, (_, index) => record({
      sequence: index, fields: [withinField()],
    }));

    pane([...held, record({ sequence: 999, fields: [field({ toleranceRatio: 100 })] })]);

    expect(within(rows()[0]!).getByText('Origin')).toBeTruthy();
  });

  it('marks a row that broke through apart from one that held', () => {
    pane([record({ fields: [field(), withinField()] })]);

    const states = rows().map(row => row.getAttribute('data-state'));

    expect(states).toContain('over');
    expect(states).toContain('within');
  });

  // The distinction the whole tab turns on. Both fields differed; only one of
  // them is a problem, and a reader taking only the colour away must take the
  // right thing away.
  it('says inside tolerance and over tolerance in words, not only in colour', () => {
    pane([record({ fields: [field(), withinField()] })]);

    expect(screen.getByText('over tolerance')).toBeTruthy();
    expect(screen.getByText('inside tolerance')).toBeTruthy();
  });

  it('never prints a field with no tolerance as a comfortable ratio', () => {
    pane([record({ fields: [field({ tolerance: 0, tolerable: true, toleranceRatio: null })] })]);

    expect(screen.getByText('no tolerance')).toBeTruthy();
    expect(screen.getByText('no tolerance declared')).toBeTruthy();
    expect(screen.queryByText('0.00x')).toBeNull();
  });

  /*
   * A kind that can never be tolerated is not a field that declared nothing.
   * One is a tolerance somebody could add; the other is a tolerance that could
   * never exist, and sending a reader off to widen it wastes their afternoon.
   */
  it('separates a kind that can never be tolerated from one that declared nothing', () => {
    pane([record({
      fields: [
        field({ name: 'Flags', kindName: 'int', tolerance: 0, tolerable: false, toleranceRatio: null }),
        field({ name: 'Speed', kindName: 'float', tolerance: 0, tolerable: true, toleranceRatio: null }),
      ],
    })]);

    expect(screen.getByText('kind cannot be tolerated')).toBeTruthy();
    expect(screen.getByText('no tolerance declared')).toBeTruthy();
  });

  it('names padding as a byte no field claims rather than blaming a field for it', () => {
    pane([record({ fields: [paddingField()] })]);

    expect(screen.getByText('(padding@57)')).toBeTruthy();
    expect(screen.getByText(/claimed by no field/i)).toBeTruthy();
  });

  it('counts how often a field differed and how often that was a break', () => {
    pane([
      record({ sequence: 1, fields: [field({ toleranceRatio: 0.2 })] }),
      record({ sequence: 2, fields: [field({ toleranceRatio: 9 })] }),
    ]);

    const cells = [...rows()[0]!.querySelectorAll('td')].map(cell => cell.textContent);

    expect(cells).toContain('2');   // seen
    expect(cells).toContain('1');   // broke
  });

  it('picks a field to chart when its row is clicked', async () => {
    const onField = vi.fn();
    pane([record()], onField);

    await userEvent.click(rows()[0]!);

    expect(onField).toHaveBeenCalledWith('Origin');
  });

  it('says so plainly when every difference was inside its tolerance', () => {
    pane([record({ fields: [withinField()] })]);

    expect(screen.getByText(/every difference was inside the/i)).toBeTruthy();
  });
});

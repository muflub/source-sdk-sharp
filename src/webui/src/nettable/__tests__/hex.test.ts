/*
 * The hex grid's attribution.
 *
 * Tested apart from the component that draws it because this is the part that
 * can be wrong invisibly. A grid that says byte 12 belongs to the wrong field is
 * a picture telling you an offset bug is not there, and the whole reason the
 * inspector exists is to make an offset bug obvious rather than deducible.
 */

import { describe, expect, it } from 'vitest';

import { changedAt, fieldAt, fieldColour, fillLabel, hexCells, hexRows, offsetLabel } from '../hex';
import type { NetDecodedField } from '../../types';

const field = (name: string, offset: number, bytes: number): NetDecodedField => ({
  name, csType: 'int', kind: 'int', offset, bytes, bits: bytes * 8,
  present: true, value: '0', hex: '', number: 0, changed: false,
});

// Two ints with a two-byte hole between them, which is what alignment padding
// looks like from here.
const fields = [field('First', 0, 4), field('Second', 6, 2)];

describe('attributing a byte to a field', () => {
  it('claims a field its own bytes', () => {
    expect(fieldAt(fields, 0)?.name).toBe('First');
    expect(fieldAt(fields, 3)?.name).toBe('First');
    expect(fieldAt(fields, 6)?.name).toBe('Second');
    expect(fieldAt(fields, 7)?.name).toBe('Second');
  });

  // The two boundaries. An off-by-one here would make the grid agree with a
  // decoder that was off by one, which is the one failure it must not have.
  it('does not claim the byte after a field', () => {
    expect(fieldAt(fields, 4)).toBeNull();
  });

  it('does not claim the byte before a field', () => {
    expect(fieldAt(fields, 5)).toBeNull();
  });

  it('claims nothing past the last field', () => {
    expect(fieldAt(fields, 8)).toBeNull();
  });
});

describe('the changed runs', () => {
  it('covers the run and stops at its end', () => {
    expect(changedAt([[4, 2]], 3)).toBe(false);
    expect(changedAt([[4, 2]], 4)).toBe(true);
    expect(changedAt([[4, 2]], 5)).toBe(true);
    expect(changedAt([[4, 2]], 6)).toBe(false);
  });

  it('handles several runs', () => {
    expect(changedAt([[0, 1], [7, 1]], 7)).toBe(true);
    expect(changedAt([[0, 1], [7, 1]], 4)).toBe(false);
  });

  it('marks nothing when nothing moved', () => {
    expect(changedAt([], 0)).toBe(false);
  });
});

describe('the grid', () => {
  const cells = hexCells('0102030400006162', fields, [[6, 2]]);

  it('has one cell per byte', () => {
    expect(cells).toHaveLength(8);
    expect(cells.map(c => c.hex)).toEqual(
      ['01', '02', '03', '04', '00', '00', '61', '62']);
  });

  it('marks the first and last byte of each field, and only those', () => {
    expect(cells.filter(c => c.fieldStart).map(c => c.offset)).toEqual([0, 6]);
    expect(cells.filter(c => c.fieldEnd).map(c => c.offset)).toEqual([3, 7]);
  });

  // Padding is shown rather than hidden: a value landing in a byte no field
  // claims is a layout that does not agree with itself.
  it('leaves padding unclaimed rather than folding it into a neighbour', () => {
    expect(cells.filter(c => c.field === null).map(c => c.offset)).toEqual([4, 5]);
  });

  it('tints exactly the bytes that moved', () => {
    expect(cells.filter(c => c.changed).map(c => c.offset)).toEqual([6, 7]);
  });

  it('renders printable bytes as themselves and the rest as dots', () => {
    expect(cells.map(c => c.ascii).join('')).toBe('.....' + '.ab');
  });

  it('stops at the end of the hex rather than inventing bytes', () => {
    expect(hexCells('', fields, [])).toHaveLength(0);
    expect(hexCells('ff', fields, [])).toHaveLength(1);
  });

  it('splits into rows of the width asked for', () => {
    const rows = hexRows(hexCells('00'.repeat(20), [], []), 16);

    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveLength(16);
    expect(rows[1]).toHaveLength(4);
    expect(rows[1]![0]!.offset).toBe(16);
  });
});

describe('the field colours', () => {
  // By position, not by a hash of the name: a hash can collide two adjacent
  // fields onto one colour, which defeats the only thing colouring is for.
  it('gives adjacent fields different colours', () => {
    expect(fieldColour(fields, fields[0]!)).not.toBe(fieldColour(fields, fields[1]!));
  });

  it('gives an unclaimed byte no colour', () => {
    expect(fieldColour(fields, null)).toBe(-1);
  });
});

describe('the labels', () => {
  it('pads an offset to four hex digits', () => {
    expect(offsetLabel(0)).toBe('0x0000');
    expect(offsetLabel(16)).toBe('0x0010');
    expect(offsetLabel(4096)).toBe('0x1000');
  });

  it('reads a tier fill as a percentage', () => {
    expect(fillLabel(32, 0.75)).toBe('75%');
    expect(fillLabel(32, 1)).toBe('100%');
  });

  // A type too big for every tier is not "over 100%", it is unnetworkable by the
  // design at all, and a number there would invite arithmetic about it.
  it('says n/a rather than a number for a type that fits no tier', () => {
    expect(fillLabel(-1, 1)).toBe('n/a');
  });
});

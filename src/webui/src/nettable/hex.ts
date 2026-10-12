/*
 * The hex grid, with the field boundaries drawn onto it.
 *
 * Pure, and separate from the component that renders it, because this is the
 * part that can be wrong in a way nobody would notice. A grid that attributes
 * byte 12 to the wrong field is a picture that tells you an offset bug is not
 * there — which is worse than not having the picture, since the whole reason the
 * inspector exists is to make an offset bug obvious rather than deducible.
 *
 * Part 13's own description of the panel: "a field reading the wrong bytes shows
 * up as a value that moves when its NEIGHBOUR is written". That only works if
 * the grid says truthfully which bytes belong to whom.
 */

import type { NetChangedRun, NetDecodedField } from '../types';

export interface HexCell {
  offset: number;

  /** Two lowercase hex digits, as the server sent them. */
  hex: string;

  /** The printable character, or a dot. */
  ascii: string;

  /**
   * Which field owns this byte, or null for one no field claims — alignment
   * padding, or the tail native reserves for the greediest class on the
   * hierarchy. Unclaimed bytes are worth SEEING: a value landing in one is a
   * layout that does not agree with itself.
   */
  field: NetDecodedField | null;

  /** First and last byte of that field, so a boundary can be drawn. */
  fieldStart: boolean;
  fieldEnd: boolean;

  /** Differs from the previous sample. */
  changed: boolean;
}

/** Two hex digits at a byte offset, or null past the end. */
function byteAt(hex: string, offset: number): string | null {
  const at = offset * 2;
  return at + 2 <= hex.length ? hex.slice(at, at + 2) : null;
}

/**
 * Which field owns a byte.
 *
 * Linear rather than an index, because the field list is tens of entries and
 * building a per-byte map would cost more than the scan for every blob anyone
 * actually looks at. The FIRST match wins, and overlapping fields are a real
 * possibility worth not hiding: a generator bug that placed two fields on the
 * same bytes would otherwise be invisible here.
 */
export function fieldAt(fields: NetDecodedField[], offset: number): NetDecodedField | null {
  for (const field of fields) {
    if (offset >= field.offset && offset < field.offset + field.bytes) return field;
  }

  return null;
}

/** Whether a byte falls inside any changed run. */
export function changedAt(runs: NetChangedRun[], offset: number): boolean {
  for (const [start, length] of runs) {
    if (offset >= start && offset < start + length) return true;
  }

  return false;
}

/**
 * Every byte of a blob, with what it belongs to and whether it moved.
 *
 * The flat hex is the input rather than the row list, because the rows are a
 * presentation choice — sixteen to a line — and the attribution is not.
 */
export function hexCells(
  hex: string, fields: NetDecodedField[], runs: NetChangedRun[],
): HexCell[] {
  const cells: HexCell[] = [];

  for (let offset = 0; ; offset++) {
    const byte = byteAt(hex, offset);
    if (byte === null) break;

    const field = fieldAt(fields, offset);
    const code = parseInt(byte, 16);

    cells.push({
      offset,
      hex: byte,
      ascii: code >= 0x20 && code < 0x7f ? String.fromCharCode(code) : '.',
      field,
      fieldStart: field !== null && offset === field.offset,
      fieldEnd: field !== null && offset === field.offset + field.bytes - 1,
      changed: changedAt(runs, offset),
    });
  }

  return cells;
}

/** The cells split into rows of `width`, for a grid with an offset column. */
export function hexRows(cells: HexCell[], width = 16): HexCell[][] {
  const rows: HexCell[][] = [];

  for (let i = 0; i < cells.length; i += width) rows.push(cells.slice(i, i + width));

  return rows;
}

/**
 * A stable colour index per field, so the same field is the same colour in the
 * grid and in the table beside it.
 *
 * By POSITION in the field list rather than by a hash of the name: a hash keeps
 * a colour across a rename, which nobody needs, and can collide two adjacent
 * fields onto one colour, which defeats the only thing the colouring is for.
 */
export function fieldColour(fields: NetDecodedField[], field: NetDecodedField | null): number {
  if (field === null) return -1;

  const index = fields.indexOf(field);
  return index < 0 ? -1 : index % 6;
}

/** "0x0010", the width the offset column is laid out for. */
export function offsetLabel(offset: number): string {
  return `0x${offset.toString(16).padStart(4, '0')}`;
}

/**
 * How much of a tier a layout uses, as a percentage a person reads.
 *
 * A type too big for every tier is not "over 100%", it is unnetworkable by the
 * design at all, and saying "n/a" beats a number that invites arithmetic.
 */
export function fillLabel(ceiling: number, fill: number): string {
  return ceiling <= 0 ? 'n/a' : `${Math.round(fill * 100)}%`;
}

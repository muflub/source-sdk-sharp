/*
 * The lump table: what a map file is made of, and how much of it each part is.
 *
 * This is the navigation for the whole tab, which is the point. A BSP's own
 * table of contents is a better index of a BSP than anything invented for the
 * occasion, and it answers a question nothing else does on the way past: where
 * the eleven megabytes went.
 */

import type { ReactNode } from 'react';

import type { MapLump } from '../types';
import { bytes, count } from './sections';

export type Order = 'file' | 'size';

export function LumpTable({
  lumps, selected, order, showEmpty, decodedOnly, decoded,
  onSelect, onOrder, onShowEmpty, onDecodedOnly,
}: {
  lumps: readonly MapLump[];
  selected: number | null;

  /** Lump indices with something decoded behind them, marked so they invite a click. */
  decoded: ReadonlySet<number>;
  order: Order;
  showEmpty: boolean;

  /** Hide the lumps nothing here can read, leaving only the ones worth opening. */
  decodedOnly: boolean;
  onSelect: (index: number | null) => void;
  onOrder: (order: Order) => void;
  onShowEmpty: (show: boolean) => void;
  onDecodedOnly: (only: boolean) => void;
}): ReactNode {
  const used = lumps.filter(lump => lump.length > 0);

  // Against the biggest lump rather than the file: the lighting lump is most of
  // a map, and bars drawn against the total leave every other row at a width of
  // nothing. What the bars are for is comparing the rows to each other.
  const largest = used.reduce((most, lump) => Math.max(most, lump.length), 1);

  /*
   * Two filters, deliberately separate.
   *
   * An empty lump and one nothing decodes are different facts about a map, and
   * somebody looking for either is looking for a different thing: the first says
   * what this map does not have, the second says what this tool cannot show. So
   * hiding one does not hide the other, and the row count says how many are left
   * rather than leaving a short list to be taken for the whole file.
   */
  const shown = lumps.filter(lump =>
    (showEmpty || lump.length > 0) && (!decodedOnly || decoded.has(lump.index)));

  if (order === 'size') shown.sort((a, b) => b.length - a.length);

  return (
    <aside className="lumps">
      <header>
        <h3>Lumps</h3>
        <span className="count">
          {shown.length === lumps.length
            ? `${used.length} of ${lumps.length} used`
            : `showing ${shown.length} of ${lumps.length}`}
        </span>
      </header>

      <div className="toolbar thin">
        <label>
          order
          <select value={order} onChange={event => onOrder(event.target.value as Order)}>
            <option value="file">by number</option>
            <option value="size">by size</option>
          </select>
        </label>
        <label title="lumps this map has nothing in">
          <input
            type="checkbox"
            checked={showEmpty}
            onChange={event => onShowEmpty(event.target.checked)}
          /> empty ones
        </label>
        <label title="hide the lumps nothing here can read — mostly data handed straight to the engine">
          <input
            type="checkbox"
            checked={decodedOnly}
            onChange={event => onDecodedOnly(event.target.checked)}
          /> readable only
        </label>
      </div>

      <div className="table-wrap">
        <table className="lump-list">
          <thead>
            <tr>
              <th className="numeric">#</th>
              <th>Lump</th>
              <th className="numeric">Size</th>
              <th className="numeric">Records</th>
            </tr>
          </thead>
          <tbody>
            {shown.map(lump => (
              <tr
                key={lump.index}
                className={[
                  lump.index === selected ? 'selected' : '',
                  lump.length === 0 ? 'dim' : '',
                  decoded.has(lump.index) ? 'decoded' : '',
                ].join(' ').trim()}
                onClick={() => onSelect(lump.index === selected ? null : lump.index)}
                title={lump.note || undefined}
              >
                <td className="numeric mono">{lump.index}</td>
                <td className="name">
                  {lump.name.toLowerCase()}
                  {lump.version > 0 && <span className="pill">v{lump.version}</span>}
                </td>
                <td className="numeric mono size">
                  {/* Behind the number rather than beside it: a column of bars
                      and a column of sizes say the same thing twice, and the
                      table is already wide. */}
                  <span
                    className="bar"
                    style={{ width: `${Math.round((lump.length / largest) * 100)}%` }}
                  />
                  <span className="value">{lump.length === 0 ? '—' : bytes(lump.length)}</span>
                </td>
                <td className="numeric mono">
                  {lump.records < 0 ? '—' : count(lump.records)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </aside>
  );
}

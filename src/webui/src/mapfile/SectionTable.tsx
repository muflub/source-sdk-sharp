/*
 * One decoded lump, as a table you can take away with you.
 *
 * Generic on purpose: every section has the same shape, so there is one table,
 * one sort, one filter and one export rather than a component per lump. What
 * differs between a list of props and a list of cubemaps is the columns, and
 * that is data.
 */

import { useMemo, useState, type ReactNode } from 'react';

import { save as download } from '../map/download';
import {
  count, filterRows, sortRows, toCsv, toJson, type Cell, type Section,
} from './sections';

/**
 * How many rows are drawn before the rest are held back.
 *
 * A map's entity list runs to hundreds and its pakfile to thousands, and a
 * thousand rows of table is a page that scrolls for a second every time it is
 * touched. The export is never cut short — what is held back is what is drawn,
 * and the button says so rather than the list simply ending.
 */
const PAGE = 300;

export function SectionTable({ section, filter }: {
  section: Section;
  filter: string;
}): ReactNode {
  const [sort, setSort] = useState<{ key: string; descending: boolean } | null>(null);
  const [limit, setLimit] = useState(PAGE);

  const rows = useMemo(() => {
    const matching = filterRows(section.rows, filter);
    const column = section.columns.find(c => c.key === sort?.key);

    return sortRows(matching, column, sort?.descending ?? false);
  }, [section, filter, sort]);

  const shown = rows.slice(0, limit);

  const toggle = (key: string): void =>
    setSort(previous => previous?.key === key
      ? { key, descending: !previous.descending }
      : { key, descending: false });

  /*
   * Exported after the filter and before the paging.
   *
   * Which is the only reading of it that is not a trap: what somebody has on
   * screen is what they mean by "this", and a file that stopped at three hundred
   * rows because the page did would be wrong in a way nothing on screen shows.
   */
  const exported = (kind: 'csv' | 'json'): void => {
    const name = `${section.id}${filter ? '-filtered' : ''}`;

    if (kind === 'csv') download(toCsv(section.columns, rows), `${name}.csv`, 'text/csv');
    else download(toJson(rows), `${name}.json`, 'application/json');
  };

  return (
    <section className="lump-section">
      <header>
        <h3>{section.title}</h3>
        <span className="count">
          {rows.length === section.rows.length
            ? `${count(section.rows.length)} rows`
            : `${count(rows.length)} of ${count(section.rows.length)} rows`}
        </span>
        <span className="spacer" />
        <button onClick={() => exported('csv')} title="the rows as shown, for a spreadsheet">CSV</button>
        <button onClick={() => exported('json')} title="the rows as shown, as JSON">JSON</button>
      </header>

      {section.note && <p className="note">{section.note}</p>}

      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              {section.columns.map(column => (
                <th
                  key={column.key}
                  className={`sortable${column.numeric ? ' numeric' : ''}`}
                  aria-sort={sort?.key === column.key
                    ? (sort.descending ? 'descending' : 'ascending')
                    : 'none'}
                >
                  <button onClick={() => toggle(column.key)}>
                    {column.label}
                    {sort?.key === column.key && (
                      <span className="arrow"> {sort.descending ? '▾' : '▴'}</span>
                    )}
                  </button>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {shown.map((row, index) => (
              <tr key={index}>
                {section.columns.map(column => (
                  <td key={column.key} className={column.numeric ? 'numeric mono' : ''}>
                    {show(row[column.key])}
                  </td>
                ))}
              </tr>
            ))}

            {shown.length === 0 && (
              <tr>
                <td className="empty" colSpan={section.columns.length}>nothing matches</td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {rows.length > shown.length && (
        <p className="note">
          Showing {count(shown.length)} of {count(rows.length)}.{' '}
          <button className="link" onClick={() => setLimit(limit + PAGE * 4)}>Show more</button>
          {' '}&mdash; an export takes all of them either way.
        </p>
      )}
    </section>
  );
}

/**
 * One cell.
 *
 * Floats are rounded here rather than at the source: the export carries the
 * value the map holds, and a column of coordinates reading -1023.9999998 is
 * unreadable for the sake of a precision nobody is looking at on screen.
 */
function show(value: Cell | undefined): string {
  if (value === undefined) return '';
  if (typeof value !== 'number') return value;
  if (Number.isInteger(value)) return String(value);

  return value.toFixed(2);
}

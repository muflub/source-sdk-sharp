/*
 * Sorting a table by one of its columns.
 *
 * Shared by the listings, which are all the same shape: a few short columns of
 * names, values and kinds, and one column of prose that nobody wants ordered.
 *
 * Which column and which way live in the address, like every other thing you set
 * up to look at something — a table sorted by what changed is worth sending to
 * somebody.
 */

import { useMemo, type ReactNode } from 'react';

import { useUrlFlag, useUrlState } from '../hooks/useUrlState';

export interface Sort<Key extends string> {
  key: Key;
  descending: boolean;

  /** Clicking a heading: a new column sorts ascending, the same one turns around. */
  toggle: (key: Key) => void;
}

export function useSort<Key extends string>(fallback: Key): Sort<Key> {
  const [key, setKey] = useUrlState('sort', fallback);
  const [descending, setDescending] = useUrlFlag('desc');

  return {
    key: key as Key,
    descending,
    toggle: next => {
      if (next === key) setDescending(!descending);
      else { setKey(next); setDescending(false); }
    },
  };
}

/**
 * The rows in order.
 *
 * Numbers compare as numbers when both sides are numbers — "10" after "9" rather
 * than before it, which is what a column of ints is for. Ties fall back to
 * whatever the caller says identifies a row, so the order is stable rather than
 * whatever the game happened to register first.
 */
export function sortRows<T>(
  rows: readonly T[],
  value: (row: T) => string,
  identity: (row: T) => string,
  { descending }: { descending: boolean },
): T[] {
  const compare = (a: T, b: T): number => {
    const left = value(a);
    const right = value(b);

    const numbers = left !== '' && right !== ''
      && !Number.isNaN(Number(left)) && !Number.isNaN(Number(right));

    const result = numbers ? Number(left) - Number(right) : left.localeCompare(right);

    return result !== 0 ? result : identity(a).localeCompare(identity(b));
  };

  const order = [...rows].sort(compare);
  return descending ? order.reverse() : order;
}

/** The same as sortRows, memoised on what it depends on. */
export function useSorted<T, Key extends string>(
  rows: readonly T[],
  sort: Sort<Key>,
  value: (row: T, key: Key) => string,
  identity: (row: T) => string,
): T[] {
  return useMemo(
    () => sortRows(rows, row => value(row, sort.key), identity, { descending: sort.descending }),
    // The two functions are written inline at every call site and would change
    // on every render; what the order actually depends on is these three.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [rows, sort.key, sort.descending]);
}

export function SortHeader<Key extends string>({ column, label, sort }: {
  column: Key;
  label: string;
  sort: Sort<Key>;
}): ReactNode {
  const active = sort.key === column;

  return (
    <th className="sortable" aria-sort={active ? (sort.descending ? 'descending' : 'ascending') : 'none'}>
      <button onClick={() => sort.toggle(column)}>
        {label}
        {active && <span className="arrow"> {sort.descending ? '▾' : '▴'}</span>}
      </button>
    </th>
  );
}

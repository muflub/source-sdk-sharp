/*
 * Every entity in the current mode, filtered.
 *
 * Two gestures, because there are two ideas: a plain click selects the one
 * entity the graph, the viewports and the inspector all follow, and ctrl or
 * shift collects any number of them to be saved as a group.
 */

import { useMemo, type ReactNode } from 'react';
import { useSyncExternalStore } from 'react';

import { keyOf, type Entity } from './model';
import type { SelectionStore } from './selection';

/*
 * Capped rather than paged.
 *
 * Six hundred rows is fine and six thousand is not, and a debugger that locks up
 * while rendering a list is worse than one that says "narrow it down".
 */
const LIMIT = 400;

const coord = (n: number): number => Math.round(n * 10) / 10;

export function EntityList({ entities, filter, selectedKey, selection, onSelect }: {
  entities: Entity[];
  filter: string;
  selectedKey: string | null;
  selection: SelectionStore;
  onSelect: (entity: Entity) => void;
}): ReactNode {
  const shown = useMemo(() => {
    const text = filter.trim().toLowerCase();
    if (text === '') return entities;

    return entities.filter(entity =>
      entity.classname.toLowerCase().includes(text)
      || entity.targetname.toLowerCase().includes(text));
  }, [entities, filter]);

  // The store is not React state, so this is how a change to it re-renders.
  const collected = useSyncExternalStore(
    listener => selection.subscribe(listener),
    () => selection.size());

  const order = useMemo(() => shown.slice(0, LIMIT).map(entity => keyOf(entity)!), [shown]);

  return (
    <>
      <div className="table-wrap map-list">
        <table>
          <thead>
            <tr><th>Name</th><th>Class</th><th>I/O</th></tr>
          </thead>
          <tbody>
            {shown.slice(0, LIMIT).map(entity => {
              const key = keyOf(entity)!;
              const out = entity.connections.length;
              const into = entity.incoming.length;

              return (
                <tr
                  key={key}
                  className={[
                    key === selectedKey ? 'selected' : '',
                    selection.has(key) ? 'marked' : '',
                  ].join(' ').trim() || undefined}
                  title={`${entity.classname}  ${entity.origin.map(coord).join(', ')}`}
                  onClick={event => {
                    if (event.ctrlKey || event.metaKey || event.shiftKey) {
                      selection.toggle(key, { range: event.shiftKey, order });
                      return;
                    }

                    onSelect(entity);
                  }}
                >
                  <td className="name">{entity.targetname}</td>
                  <td className="mono">{entity.classname}</td>
                  {/* The position is in the row's title rather than a column:
                      this pane is narrow, and a fourth column of wrapping
                      numbers turns every row into a paragraph. */}
                  <td className="mono" title={`${into} connection(s) in, ${out} out`}>
                    {out || into ? `${into}/${out}` : ''}
                  </td>
                </tr>
              );
            })}
            {shown.length === 0 && (
              <tr><td className="empty" colSpan={3}>nothing matches</td></tr>
            )}
          </tbody>
        </table>
      </div>

      <div className="count">
        {shown.length > LIMIT ? `showing ${LIMIT} of ${shown.length}` : `${shown.length} shown`}
        {collected > 0 && ` · ${collected} collected`}
      </div>
    </>
  );
}

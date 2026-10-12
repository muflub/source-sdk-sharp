/*
 * Which entities are collected, as opposed to which one is being looked at.
 *
 * Two different ideas, deliberately kept apart: the selection is one entity — the
 * thing the graph centres on and the inspector fills in — and this is any number
 * of them, gathered to be saved as a group.
 *
 * An observable object rather than React state, because it is shared by two
 * panes that do not contain each other — the list marks entities, the groups
 * pane captures them — and each subscribes to what it needs. Holding it above
 * both would re-render the whole tab, list included, on every ctrl-click.
 */

export interface SelectionStore {
  has(key: string | null): boolean;
  size(): number;
  keys(): readonly string[];
  toggle(key: string, options?: { range?: boolean; order?: readonly string[] }): void;
  add(keys: readonly string[]): void;
  clear(): void;
  subscribe(listener: () => void): () => void;
}

export function createSelection(): SelectionStore {
  let keys = new Set<string>();
  let last: string | null = null;

  const listeners = new Set<() => void>();
  const changed = (): void => { for (const listener of listeners) listener(); };

  return {
    has: key => key !== null && keys.has(key),
    size: () => keys.size,
    keys: () => [...keys],

    toggle(key, { range = false, order = [] } = {}) {
      // Shift takes everything between the last click and this one, in the order
      // the list is showing — which is what a list selection means everywhere
      // else, and so what anyone will expect here.
      if (range && last !== null) {
        const from = order.indexOf(last);
        const to = order.indexOf(key);

        if (from >= 0 && to >= 0) {
          const [start, end] = from < to ? [from, to] : [to, from];
          for (let i = start; i <= end; i++) keys.add(order[i]!);

          last = key;
          changed();
          return;
        }
      }

      keys.has(key) ? keys.delete(key) : keys.add(key);
      last = key;
      changed();
    },

    add(more) {
      for (const key of more) keys.add(key);
      changed();
    },

    clear() {
      keys = new Set();
      last = null;
      changed();
    },

    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
  };
}

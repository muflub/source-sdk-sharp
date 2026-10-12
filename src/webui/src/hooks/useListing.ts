/*
 * A list read from the game once, and then left alone.
 *
 * Commands, convars and entity classes are declared at startup and do not change
 * while the game runs, so they are read rather than polled. What that missed is
 * that the page is usually open *before* there is anything to read from: the
 * launcher opens a browser as soon as the API answers, which is during module
 * init, well before a map has loaded — and behind the proxy, before the page has
 * even worked out which game it is addressing.
 *
 * One attempt at mount therefore came back empty, and stayed empty until
 * somebody pressed Refresh. So this keeps asking until it has an answer, and
 * then stops: an empty list is a question that has not been answered yet, not an
 * answer of "none".
 */

import { useEffect, useRef, useState } from 'react';

/** How long between attempts while there is still nothing. */
const RETRY_MS = 1500;

export interface Listing<T> {
  items: T[];

  /** Read it again now — what the Refresh button does. */
  reload: () => Promise<void>;

  /** Nothing yet, and still trying. Lets a view say so rather than look empty. */
  waiting: boolean;

  /**
   * When the game last answered, or null if it never has.
   *
   * Worth showing, because a list that is read once and then left alone looks
   * exactly the same whether it was read a second ago or before the map that is
   * running now was loaded.
   */
  loadedAt: Date | null;
}

/**
 * @param load     Reads the list. Anything thrown is a game that is not ready.
 * @param visible  Whether the tab is showing; a hidden one asks once and waits.
 */
export function useListing<T>(load: () => Promise<T[]>, visible: boolean): Listing<T> {
  const [items, setItems] = useState<T[]>([]);
  const [waiting, setWaiting] = useState(true);
  const [loadedAt, setLoadedAt] = useState<Date | null>(null);

  // The loader closes over props that change on most renders, and restarting the
  // retry loop every time one did would ask far more often than asked for.
  const latest = useRef(load);
  latest.current = load;

  const reload = useRef(async (): Promise<void> => {
    try {
      const next = await latest.current();

      setItems(next);
      setWaiting(next.length === 0);
      setLoadedAt(new Date());
    } catch {
      // A game that is still starting, or one that has gone. Either way the
      // list on screen is left alone: blanking it would lose a good answer to
      // one bad moment.
      setWaiting(true);
    }
  }).current;

  const attempted = useRef(false);

  useEffect(() => {
    if (!waiting) return;

    // One attempt from a hidden tab, so that switching to it usually finds the
    // list already there; after that it waits to be looked at. A page sitting on
    // the logs should not keep asking a game that is not there for a list nobody
    // is reading.
    if (!visible && attempted.current) return;

    let live = true;
    let timer = 0;

    const attempt = async (): Promise<void> => {
      if (!live) return;

      attempted.current = true;
      await reload();

      // Checked again after the await: the tab may have been left, or an answer
      // may have arrived, while the request was out.
      if (live) timer = window.setTimeout(() => void attempt(), RETRY_MS);
    };

    void attempt();

    return () => {
      live = false;
      window.clearTimeout(timer);
    };
  }, [waiting, visible, reload]);

  return { items, reload, waiting, loadedAt };
}

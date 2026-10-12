/*
 * State that lives in the address bar.
 *
 * Everything you set up to look at something — which game, which tab, which pane,
 * what the list is filtered to — belongs in the URL rather than only in the page.
 * A debugging session is something you send to somebody: "it happens on this
 * entity" is a link, not a list of instructions for reproducing the view.
 *
 * Written with replaceState rather than pushState. Typing five characters into a
 * filter box is one thought, and it should not take five presses of Back to
 * leave — the tab and the entity, which are the things worth stepping back
 * through, are in the hash and the browser handles those itself.
 */

import { useCallback, useEffect, useState } from 'react';

/** Reads one query parameter. */
export function readParam(name: string): string {
  return new URLSearchParams(location.search).get(name) ?? '';
}

/** Writes one, dropping it when it is back to its default. */
export function writeParam(name: string, value: string, fallback = ''): void {
  const params = new URLSearchParams(location.search);

  // An address carrying "&filter=" for every box nobody typed in is unreadable,
  // and the point of putting this in the URL is that it can be read.
  if (value === fallback) params.delete(name);
  else params.set(name, value);

  const query = params.toString();

  history.replaceState(null, '', `${location.pathname}${query ? `?${query}` : ''}${location.hash}`);
}

/**
 * A piece of state kept in the query string.
 *
 * Same shape as useState, so a component adopts it by changing one line.
 */
export function useUrlState(name: string, fallback = ''): [string, (value: string) => void] {
  const [value, setValue] = useState(() => readParam(name) || fallback);

  const set = useCallback((next: string) => {
    setValue(next);
    writeParam(name, next, fallback);
  }, [name, fallback]);

  // Back and forward move through the hash, and the query goes with it — so what
  // the boxes show has to follow, or the address and the page disagree.
  useEffect(() => {
    const onPopState = (): void => setValue(readParam(name) || fallback);

    window.addEventListener('popstate', onPopState);
    window.addEventListener('hashchange', onPopState);

    return () => {
      window.removeEventListener('popstate', onPopState);
      window.removeEventListener('hashchange', onPopState);
    };
  }, [name, fallback]);

  return [value, set];
}

/** The same, for a checkbox. */
export function useUrlFlag(name: string, fallback = false): [boolean, (value: boolean) => void] {
  const [text, setText] = useUrlState(name, fallback ? '1' : '0');

  return [text === '1', (value: boolean) => setText(value ? '1' : '0')];
}

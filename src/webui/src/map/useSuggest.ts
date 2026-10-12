/*
 * Attaching a suggestion list to an input.
 *
 * The lists themselves are built from the map and the listings — see suggest —
 * and the two keyed kinds are built on focus, because there is one per classname
 * and one per key and building all of them for a map would be hundreds nobody
 * opens.
 */

import { useCallback } from 'react';

import * as suggest from '../suggest';
import type { SuggestKind } from '../suggest';

export type { SuggestKind };

/** A ref callback that points an input at the right list. */
export function useSuggest(kind: SuggestKind, context?: string) {
  return useCallback((input: HTMLInputElement | null) => {
    if (input) suggest.attach(input, kind, context);
  }, [kind, context]);
}

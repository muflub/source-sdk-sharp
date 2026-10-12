/*
 * Noticing that the interface has been rebuilt.
 *
 * A debug build serves this page out of the working tree, so `make run` can
 * leave the bundler watching and an edit is on disk a moment after it is saved.
 * What was missing was the last step: the page had to be reloaded by hand, which
 * is exactly the kind of thing that is forgotten for ten minutes at a time while
 * you wonder why a change did nothing.
 *
 * Polled rather than pushed, like everything else here, and from the shell's one
 * loop rather than a timer of its own. The server answers with a stamp of the
 * files it is serving; two different stamps mean a new build.
 *
 * A release build answers with a constant, so this does nothing at all in one —
 * which is right. An embedded copy of the page cannot change under a running
 * game, and a shipped tool that reloads itself would be a bug.
 */

import { useCallback, useRef, useState } from 'react';

import { getUiStamp } from '../api';

/**
 * Watches for a rebuild, and answers whether one is waiting to be applied.
 *
 * `safeToReload` is asked before anything happens: the map tab holds edits that
 * exist nowhere but this page until they are saved, and throwing those away
 * because a stylesheet changed would be a poor trade. When it says no, the
 * caller is told instead, and can offer the reload rather than perform it.
 */
export function useLiveReload(safeToReload: () => boolean): [boolean, () => Promise<void>] {
  const [waiting, setWaiting] = useState(false);
  const known = useRef<string | null>(null);
  const safe = useRef(safeToReload);
  safe.current = safeToReload;

  const check = useCallback(async (): Promise<void> => {
    const result = await getUiStamp();
    if (!result?.live) return;

    // The first answer is what "unchanged" means; there is nothing to compare it
    // against, and reloading on sight of it would be a loop.
    if (known.current === null) {
      known.current = result.stamp;
      return;
    }

    if (known.current === result.stamp) return;

    known.current = result.stamp;

    if (safe.current()) location.reload();
    else setWaiting(true);
  }, []);

  return [waiting, check];
}

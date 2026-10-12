/*
 * Polling, as the page has always done it.
 *
 * One loop rather than a timer per feed. Every poll is a fresh connection the
 * game answers on its listener thread, and several timers drifting into each
 * other would have it accepting connections in bursts instead of a trickle.
 * Awaiting each round also means a slow answer delays the next poll rather than
 * stacking another on top of it.
 */

import { useEffect, useRef } from 'react';

/** The base rate. Everything slower is a multiple of it. */
export const TICK_MS = 250;

export type Tick = (counter: number) => Promise<void> | void;

/**
 * Runs a function forever, one call at a time.
 *
 * The callback is held in a ref so that changing it — which happens on most
 * renders, since it closes over state — does not restart the loop. A loop that
 * restarted on every render would poll far faster than it was asked to.
 */
export function usePoll(tick: Tick, intervalMs = TICK_MS): void {
  const latest = useRef(tick);
  latest.current = tick;

  useEffect(() => {
    let running = true;
    let counter = 0;

    const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));

    void (async () => {
      while (running) {
        try {
          await latest.current(counter);
        } catch {
          // Reported by whatever is watching the connection; a failed poll must
          // not stop the loop, or one dropped request ends the session.
        }

        counter++;
        await sleep(intervalMs);
      }
    })();

    return () => { running = false; };
  }, [intervalMs]);
}

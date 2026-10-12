/*
 * When the list on screen was read.
 *
 * These listings are read once and then left alone, because what they describe
 * does not change while the game runs — which means a stale one looks exactly
 * like a fresh one. The clock time rather than "2 minutes ago": the question
 * being answered is usually "is this from before or after I restarted the
 * game", and a wall clock answers it without arithmetic.
 */

import type { ReactNode } from 'react';

export function LoadedAt({ at }: { at: Date | null }): ReactNode {
  if (at === null) return null;

  return (
    <span className="loaded-at" title={at.toString()}>
      read at {at.toLocaleTimeString()}
    </span>
  );
}

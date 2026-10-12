/*
 * Reading a build stamp.
 *
 * The rendering is in BuildStamp.tsx; the questions it asks of the stamp are
 * here, because they are the part that can be wrong in a way nobody would see:
 * a page that says nothing when the game is running old code looks exactly like
 * a page that says nothing because it is not.
 */

import type { Build, BuildHalf, Freshness } from '../types';

/** Which half. Named rather than boolean, because the advice differs per half. */
export type Which = 'native' | 'managed';

/**
 * What to do about a half that has been rebuilt since it was loaded.
 *
 * The two are NOT interchangeable. The managed half can be replaced in a running
 * process and the native half cannot, so offering a reload for native would be
 * advice that quietly does nothing — which is worse than no advice, because it
 * looks like it worked.
 */
export function advice(which: Which, freshness: Freshness): string {
  if (freshness !== 'stale') return '';

  return which === 'native'
    ? 'rebuilt since this game started — restart it'
    : 'rebuilt since this game loaded — run cs_sv_reload';
}

/**
 * Whether anything at all has been rebuilt under this game.
 *
 * Drives the marker in the bar, which is the only part of this anybody sees
 * without going looking.
 */
export function outOfDate(build: Build | undefined): boolean {
  if (!build) return false;

  return build.native.freshness === 'stale' || build.managed.freshness === 'stale';
}

/**
 * Whether the two halves came from different source.
 *
 * Worth saying out loud: it means the managed half was reloaded from a tree
 * that is not the one the running native half was built from, which is a state
 * that produces symptoms nobody would connect to it. Two halves that are simply
 * unstamped are not a divergence — nothing is known about either.
 */
export function sourceDiverged(build: Build | undefined): boolean {
  if (!build) return false;
  if (!build.native.known || !build.managed.known) return false;
  if (build.native.source === 'unknown' || build.managed.source === 'unknown') return false;

  return build.native.source !== build.managed.source;
}

/**
 * The stamp as one line, for the bar. Short: it competes with the map, the tick
 * count and the connection state for the same strip.
 */
export function short(half: BuildHalf): string {
  if (!half.known) return 'unknown';

  return `${clock(half.at)} ${half.hash}`;
}

/** Clock time, in the reader's own zone — they are comparing it to when they built. */
export function clock(at: number): string {
  if (!at) return '';

  return new Date(at).toLocaleTimeString([], {
    hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false,
  });
}

/** The full time, for the detail pane and the hover title. */
export function full(at: number): string {
  return at ? new Date(at).toLocaleString() : 'unknown';
}

/**
 * How the dirty flag reads.
 *
 * Three answers for three states. "clean" is only ever said when the build
 * actually said so; a build whose git could not be asked says that instead,
 * because presenting it as clean is the one thing this must never do.
 */
export function dirtiness(half: BuildHalf): string {
  if (!half.known || half.source === 'unknown') return 'not known';

  return half.dirty === true ? 'uncommitted work'
       : half.dirty === false ? 'clean'
       : 'could not be asked';
}

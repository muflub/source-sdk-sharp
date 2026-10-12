/*
 * Hiding the lines you have already read a thousand times.
 *
 * A Source console logs per-tick traces by the hundred, and one of them repeating
 * is enough to bury everything else in the feed. Filtering by text goes the wrong
 * way round — it says what to keep, when what you know is what to throw away —
 * so this is the other side of it: point at a line, and lines like it stop
 * arriving.
 *
 * "Like it" cannot be "identical to it", because the noisiest lines are the ones
 * carrying a different number every time: an entity index, a position, a tick.
 * So a line is reduced to its shape — every run of digits becomes a #, and the
 * shape is what is matched. "player#1 hit worldspawn#0 for 25" and the same line
 * a second later with different numbers are one pattern, which is what anybody
 * pointing at either of them meant.
 */

/** The key hidden patterns are stored under. Per browser, like the groups. */
const STORAGE_KEY = 'debug-api.muted-lines';

/** What a line looks like with its numbers taken out. */
export function signature(text: string): string {
  return text
    .replace(/-?\d+(\.\d+)?/g, '#')
    .replace(/\s+/g, ' ')
    .trim();
}

export function isMuted(text: string, muted: readonly string[]): boolean {
  if (muted.length === 0) return false;

  return muted.includes(signature(text));
}

export function load(): string[] {
  try {
    const stored: unknown = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '[]');

    // Anything else in there is somebody else's data or a half-written value;
    // an empty list is a better answer than a crash on every render.
    return Array.isArray(stored) ? stored.filter(one => typeof one === 'string') : [];
  } catch {
    return [];
  }
}

export function save(muted: readonly string[]): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(muted));
  } catch {
    // A full or disabled store. The muting still works for this session, which
    // is the part that matters while something is being read.
  }
}

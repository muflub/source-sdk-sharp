/*
 * The live mock-ups' arithmetic (the Mockups tab's Live view).
 *
 * POST {base}live answers UiLive's requests: the game's dev API loads the UI
 * assemblies itself, and `make ui-preview WATCH=1` runs the preview tool's
 * --live process. `list` names every segment of the UI (screens, HUD
 * elements, gallery parts, panels), its data presets and the preset sizes;
 * `render` draws one at a size with edits laid over its data and answers the
 * values it read. Everything here is pure or wraps storage, so it is tested
 * without a browser.
 */

export interface LivePreset { slug: string; title: string }

export interface LiveSegment {
  id: string;
  name: string;
  /** screen, hud, part or panel. */
  kind: string;
  assembly: string;
  presets: LivePreset[];
}

export interface LiveSize { name: string; w: number; h: number; scale: number }

export interface LiveList {
  segments: LiveSegment[];
  sizes: LiveSize[];
  fonts: { family: string; weight: number; data: string }[];
}

export type LeafValue = string | number | boolean | null;

/** One value the screen read: IScoreboardSource.Player(2).Name, its type and what it was answered. */
export interface LiveLeaf {
  key: string;
  source: string;
  type: 'bool' | 'int' | 'float' | 'string' | 'enum';
  value: LeafValue;
  changed: boolean;
  options: string[] | null;
}

export interface LiveFrame {
  name?: string;
  layer?: string;
  preset?: string;
  w?: number;
  h?: number;
  ms?: number;
  svg?: string;
  elements?: import('./manifest').Element[];
  values?: LiveLeaf[];
  error?: string;
}

export type Edits = Record<string, LeafValue>;

/** What the view was showing: restored on the next visit. */
export interface LiveView {
  segment: string;
  preset: string;
  w: number;
  h: number;
  scale: number;
  backdrop: string;
}

export const DEFAULT_VIEW: LiveView = { segment: '', preset: '', w: 1920, h: 1080, scale: 1, backdrop: 'checker' };

/** Asks the live mock-ups; a refusal or a dead server answers { error }. */
export async function askLive<T>(base: string, request: object): Promise<T | { error: string }> {
  try {
    const r = await fetch(`${base}live`, { method: 'POST', body: JSON.stringify(request), headers: { 'Content-Type': 'application/json' } });
    const text = await r.text();
    try {
      return JSON.parse(text) as T | { error: string };
    } catch {
      return { error: `${r.status}: ${text.slice(0, 200) || 'no answer'}` };
    }
  } catch (e) {
    return { error: String(e) };
  }
}

//---------------------------------------------------------------------------
// Browser storage. Every access is guarded: a private window or blocked site
// data throws, and the view must work without it.

const PREFIX = 'ss-mockups:';

export function editsKey(segment: string, preset: string): string {
  return `${PREFIX}edits:${segment}:${preset}`;
}

function read(key: string): string | null {
  try { return localStorage.getItem(key); } catch { return null; }
}

function write(key: string, value: string | null): void {
  try {
    if (value === null) localStorage.removeItem(key);
    else localStorage.setItem(key, value);
  } catch { /* storage refused: the edits live until the page closes */ }
}

export function loadEdits(segment: string, preset: string): Edits {
  const text = read(editsKey(segment, preset));
  if (!text) return {};
  try {
    const parsed: unknown = JSON.parse(text);
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed as Edits : {};
  } catch {
    return {};
  }
}

export function saveEdits(segment: string, preset: string, edits: Edits): void {
  write(editsKey(segment, preset), Object.keys(edits).length === 0 ? null : JSON.stringify(edits));
}

export function loadView(): LiveView {
  const text = read(`${PREFIX}view`);
  if (!text) return DEFAULT_VIEW;
  try {
    return { ...DEFAULT_VIEW, ...JSON.parse(text) as Partial<LiveView> };
  } catch {
    return DEFAULT_VIEW;
  }
}

export function saveView(view: LiveView): void {
  write(`${PREFIX}view`, JSON.stringify(view));
}

//---------------------------------------------------------------------------
// Edits.

/** An edit set to the value the screen was answered without it is no edit. */
export function setEdit(edits: Edits, key: string, value: LeafValue, original: LeafValue | undefined): Edits {
  const next = { ...edits };
  if (original !== undefined && value === original) delete next[key];
  else next[key] = value;
  return next;
}

export function clearEdit(edits: Edits, key: string): Edits {
  const next = { ...edits };
  delete next[key];
  return next;
}

/** What each leaf was answered without edits: the last answer's unedited values, kept across renders. */
export function originals(previous: Record<string, LeafValue>, leaves: LiveLeaf[]): Record<string, LeafValue> {
  const next = { ...previous };
  for (const l of leaves) if (!l.changed) next[l.key] = l.value;
  return next;
}

//---------------------------------------------------------------------------
// Random values: plausible for the field, so a random screen still reads as
// the screen.

/** A seeded generator (mulberry32): the same seed, the same values. */
export function seeded(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6D2B79F5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const NAMES = ['Alyx', 'Barney', 'Kleiner', 'Mossman', 'Grigori', 'Odessa', 'Vortigaunt', 'Breen', 'Magnusson', 'Lamarr',
  'Scout', 'Soldier', 'Pyro', 'Demoman', 'Heavy', 'Engineer', 'Medic', 'Sniper', 'Spy', 'xX_Headshot_Xx', 'a very long player name indeed'];
const WORDS = ['ready', 'capture', 'the', 'point', 'defend', 'round', 'over', 'reload', 'objective', 'secured', 'overtime',
  'team', 'blue', 'red', 'server', 'lockdown', 'friendly', 'fire', 'enabled', 'waiting', 'for', 'players', 'sudden', 'death'];
const MAPS = ['dm_lockdown', 'dm_overwatch', 'ctf_2fort', 'cp_dustbowl', 'pl_badwater', 'koth_harvest'];

function pick<T>(list: readonly T[], rng: () => number): T {
  return list[Math.floor(rng() * list.length)]!;
}

function between(lo: number, hi: number, rng: () => number): number {
  return Math.floor(lo + rng() * (hi - lo + 1));
}

/** The member a key ends in: "IScoreboardSource.Player(2).Name" → "Name", "X.Tags.Count" → "Count". */
export function memberOf(key: string): string {
  const last = key.split('.').pop() ?? key;
  return last.replace(/[([].*$/, '');
}

export function randomValue(leaf: LiveLeaf, rng: () => number): LeafValue {
  const member = memberOf(leaf.key);
  switch (leaf.type) {
    case 'bool':
      return rng() < 0.5;
    case 'enum':
      return leaf.options && leaf.options.length > 0 ? pick(leaf.options, rng) : leaf.value;
    case 'int': {
      // A count makes rows: kept small enough to read.
      if (/Count$/.test(member)) return between(0, 8, rng);
      if (/Max/.test(member)) return between(50, 200, rng);
      if (/(Health|Armou?r|Percent|Progress)/.test(member)) return between(0, 100, rng);
      const v = typeof leaf.value === 'number' ? Math.abs(leaf.value) : 0;
      return between(0, v > 0 ? Math.max(10, v * 2) : 100, rng);
    }
    case 'float': {
      const v = typeof leaf.value === 'number' ? Math.abs(leaf.value) : 1;
      const hi = v <= 1 ? 1 : v * 2;
      return Math.round(rng() * hi * 100) / 100;
    }
    case 'string': {
      if (/Map|Level/.test(member)) return pick(MAPS, rng);
      if (/(Name|Player|Nick|Author|Speaker)/.test(member)) return pick(NAMES, rng);
      const n = between(1, 4, rng);
      const words = Array.from({ length: n }, () => pick(WORDS, rng));
      return words.join(' ').replace(/^./, c => c.toUpperCase());
    }
    default:
      return leaf.value;
  }
}

/** A random value for every leaf (only those not in `keep`, when given). */
export function randomEdits(leaves: LiveLeaf[], rng: () => number, keep?: Edits): Edits {
  const out: Edits = { ...(keep ?? {}) };
  for (const l of leaves) {
    if (keep && l.key in keep) continue;
    out[l.key] = randomValue(l, rng);
  }
  return out;
}

//---------------------------------------------------------------------------
// Layout of the view.

/** The zoom that fits a w×h screen in the box, never above 1 (a small screen is shown at its size). */
export function fitZoom(w: number, h: number, boxW: number, boxH: number): number {
  if (w <= 0 || h <= 0 || boxW <= 0 || boxH <= 0) return 1;
  return Math.min(1, boxW / w, boxH / h);
}

/** A screen size from a drag: the start size plus the pointer's travel at the zoom, held to what the renderer draws. */
export function dragSize(start: { w: number; h: number }, dx: number, dy: number, zoom: number): { w: number; h: number } {
  const clamp = (v: number, lo: number, hi: number) => Math.round(Math.min(hi, Math.max(lo, v)));
  return { w: clamp(start.w + dx / zoom, 160, 8192), h: clamp(start.h + dy / zoom, 120, 8192) };
}

/** The leaf's key without its interface: "Player(2).Name". */
export function shortKey(leaf: LiveLeaf): string {
  return leaf.key.startsWith(`${leaf.source}.`) ? leaf.key.slice(leaf.source.length + 1) : leaf.key;
}

/** The call a leaf belongs to: "Player(2).Name" → "Player(2)", "Title" → "Title". */
export function callOf(short: string): string {
  let depth = 0;
  for (let i = 0; i < short.length; i++) {
    const c = short[i];
    if (c === '(' || c === '[') depth++;
    else if (c === ')' || c === ']') depth--;
    else if (c === '.' && depth === 0) return short.slice(0, i);
  }
  return short;
}

/** Leaves by interface, then by call, in the order read. */
export function groupLeaves(leaves: LiveLeaf[]): Map<string, Map<string, LiveLeaf[]>> {
  const out = new Map<string, Map<string, LiveLeaf[]>>();
  for (const l of leaves) {
    const calls = out.get(l.source) ?? new Map<string, LiveLeaf[]>();
    const call = callOf(shortKey(l));
    const list = calls.get(call) ?? [];
    list.push(l);
    calls.set(call, list);
    out.set(l.source, calls);
  }
  return out;
}

export const KINDS: { kind: string; title: string }[] = [
  { kind: 'screen', title: 'Screens' },
  { kind: 'hud', title: 'HUD' },
  { kind: 'part', title: 'Gallery parts' },
  { kind: 'panel', title: 'Panels' },
];

/** The segments a filter keeps: every word in the name, id or assembly. */
export function filterSegments(segments: LiveSegment[], filter: string): LiveSegment[] {
  const words = filter.trim().toLowerCase().split(/\s+/).filter(Boolean);
  if (words.length === 0) return segments;
  return segments.filter(s => {
    const hay = `${s.name} ${s.id} ${s.assembly}`.toLowerCase();
    return words.every(w => hay.includes(w));
  });
}

/** CSS that loads the list's fonts from their bytes, so the inline SVG's text is drawn in the faces it was measured with. */
export function liveFontFaces(list: LiveList): string {
  return list.fonts
    .map(f => `@font-face { font-family: "${f.family}"; font-weight: ${f.weight}; src: url("data:font/ttf;base64,${f.data}") format("truetype"); }`)
    .join('\n');
}

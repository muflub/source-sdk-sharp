/*
 * What `make ui-preview` wrote, and the arithmetic the Mockups tab does on it.
 *
 * The preview tool (src/tools/UiPreview) renders every screen sample at every
 * mock-up size, and every gallery part, into bin/<config>/ui-preview, with a
 * manifest.json listing it all. In watch mode tools/ui-preview serves that
 * directory under /preview/ beside this page. Everything here is pure, so it is
 * tested without a browser.
 */

/** The files of one sample at one size: always svg and json, png with PNG=1. */
export interface SizeFiles {
  svg: string;
  json: string;
  png?: string;
  'png-dark'?: string;
  'png-bright'?: string;
}

export interface Sample {
  group: string;
  title: string;
  slug: string;
  gallery: boolean;
  layer: string;
  files: Record<string, SizeFiles>;
}

export interface Manifest {
  generated: string;
  sizes: string[];
  backdrops: string[];
  fonts: { family: string; weight: number; file: string }[];
  samples: Sample[];
}

/** One laid-out element, as the preview's element JSON lists it. */
export interface Element {
  i: number;
  id: number;
  parent: number;
  kind: string;
  name: string;
  x: number;
  y: number;
  w: number;
  h: number;
  flags: string;
}

/** Where the preview's files are served: /preview/ beside the page, or ?preview=<url>. */
export function previewBase(search: string = location.search): string {
  const given = new URLSearchParams(search).get('preview');
  if (!given) return '/preview/';
  return given.endsWith('/') ? given : `${given}/`;
}

/** Screens and gallery parts, each in manifest order: group name → its samples. */
export function groups(manifest: Manifest, gallery: boolean): Map<string, Sample[]> {
  const out = new Map<string, Sample[]>();
  for (const s of manifest.samples) {
    if (s.gallery !== gallery) continue;
    const list = out.get(s.group) ?? [];
    list.push(s);
    out.set(s.group, list);
  }
  return out;
}

/** "1920x1080" or "1080x1920@1.75" (drawn at ui_scale 1.75) → [1920, 1080]; anything else → null. */
export function parseSize(size: string): [number, number] | null {
  const m = /^(\d+)x(\d+)(?:@\d+(?:\.\d+)?)?$/.exec(size);
  return m ? [Number(m[1]), Number(m[2])] : null;
}

/** The sizes a sample has, in manifest order; a gallery part has one. */
export function sizesOf(sample: Sample, all: string[]): string[] {
  const own = Object.keys(sample.files);
  return all.filter(s => own.includes(s)).concat(own.filter(s => !all.includes(s)));
}

/** The picture to show: the PNG over the chosen still when there is one, else null (the SVG is shown). */
export function pngFor(files: SizeFiles, backdrop: string): string | null {
  if (backdrop === 'busy') return files.png ?? null;
  if (backdrop === 'dark') return files['png-dark'] ?? null;
  if (backdrop === 'bright') return files['png-bright'] ?? null;
  return null;
}

/**
 * The element under a point: the smallest by area that contains it, so the
 * inspector names the label rather than the panel the label sits on. Ties go
 * to the later element (drawn on top).
 */
export function elementAt(elements: Element[], x: number, y: number): Element | null {
  let best: Element | null = null;
  for (const e of elements) {
    if (e.w <= 0 || e.h <= 0) continue;
    if (x < e.x || y < e.y || x >= e.x + e.w || y >= e.y + e.h) continue;
    if (!best || e.w * e.h <= best.w * best.h) best = e;
  }
  return best;
}

/** The element's ancestry as names: "TeamTable[team0] › row 3 › Alyx". */
export function pathOf(elements: Element[], e: Element): string {
  const names: string[] = [];
  for (let k: number = e.i; k >= 0 && names.length < 6; k = elements[k]?.parent ?? -1) {
    const name = elements[k]?.name;
    if (name && !/^(Box|Stack|Grid|Spacer)$/.test(name)) names.push(name);
  }
  return names.reverse().join(' › ');
}

/** CSS that loads the preview's fonts, so inline SVG text is drawn in the same faces the layout measured. */
export function fontFaces(manifest: Manifest, base: string): string {
  return manifest.fonts
    .map(f => `@font-face { font-family: "${f.family}"; font-weight: ${f.weight}; src: url("${base}${f.file}") format("truetype"); }`)
    .join('\n');
}

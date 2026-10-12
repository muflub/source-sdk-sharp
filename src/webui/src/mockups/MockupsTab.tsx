/*
 * Mock-ups of the game's UI, without the game (plan_client_ui §7).
 *
 * `make ui-preview WATCH=1` renders every screen sample at every mock-up size
 * and serves the pictures here under /preview/; each save re-renders, and the
 * tool says so over /preview/events, so this tab reloads what it shows. Two
 * views: Screens (pick a screen, a sample, a size, a world backdrop; hover to
 * see what each element is and where) and Gallery (every part in every state,
 * to pick from by looking).
 *
 * Served by the game instead, there is no /preview/: the tab says how to start
 * the preview rather than showing an empty page.
 */

import { useCallback, useEffect, useMemo, useRef, useState, type MouseEvent, type ReactNode } from 'react';

import {
  elementAt, fontFaces, groups, parseSize, pathOf, pngFor, previewBase, sizesOf,
  type Element, type Manifest, type Sample,
} from './manifest';

type View = 'screens' | 'gallery';
const BACKDROPS = ['none', 'busy', 'dark', 'bright'] as const;
type Backdrop = (typeof BACKDROPS)[number];

/** Loads the manifest, and again on every render event from the watcher. */
function useManifest(base: string, visible: boolean): { manifest: Manifest | null; error: string | null; version: number } {
  const [manifest, setManifest] = useState<Manifest | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [version, setVersion] = useState(0);

  useEffect(() => {
    if (!visible) return;
    let live = true;
    fetch(`${base}manifest.json?v=${version}`)
      .then(async r => {
        if (!r.ok) throw new Error(`${r.status}`);
        return await r.json() as Manifest;
      })
      .then(m => { if (live) { setManifest(m); setError(null); } })
      .catch((e: unknown) => { if (live) setError(String(e)); });
    return () => { live = false; };
  }, [base, visible, version]);

  useEffect(() => {
    if (!visible || typeof EventSource === 'undefined') return;
    const events = new EventSource(`${base}events`);
    events.addEventListener('render', () => setVersion(v => v + 1));
    return () => events.close();
  }, [base, visible]);

  return { manifest, error, version };
}

export function MockupsTab({ visible }: { visible: boolean }): ReactNode {
  const base = useMemo(() => previewBase(), []);
  const { manifest, error, version } = useManifest(base, visible);
  const [view, setView] = useState<View>('screens');

  if (!manifest) {
    return (
      <div className="mockups-empty">
        <h3>No mock-ups here</h3>
        <p>
          The pictures come from the preview tool, not the game. Run{' '}
          <code>make ui-preview WATCH=1</code> and open{' '}
          <a href="http://127.0.0.1:7191/#mockups">http://127.0.0.1:7191/#mockups</a>.
        </p>
        {error && <p className="dim">({base}manifest.json: {error})</p>}
      </div>
    );
  }

  return (
    <div className="mockups">
      <style>{fontFaces(manifest, base)}</style>
      <div className="mockups-bar">
        <div className="segmented" role="tablist">
          {(['screens', 'gallery'] as const).map(v => (
            <button key={v} role="tab" aria-selected={view === v} onClick={() => setView(v)}>
              {v === 'screens' ? 'Screens' : 'Gallery'}
            </button>
          ))}
        </div>
        <span className="dim">rendered {new Date(manifest.generated).toLocaleTimeString()}</span>
      </div>
      {view === 'screens'
        ? <ScreensView manifest={manifest} base={base} version={version} />
        : <GalleryView manifest={manifest} base={base} version={version} />}
    </div>
  );
}

function ScreensView({ manifest, base, version }: { manifest: Manifest; base: string; version: number }): ReactNode {
  const screens = useMemo(() => groups(manifest, false), [manifest]);
  const names = [...screens.keys()];
  const [group, setGroup] = useState(names[0] ?? '');
  const samples = screens.get(group) ?? [];
  const [slug, setSlug] = useState(samples[0]?.slug ?? '');
  const sample = samples.find(s => s.slug === slug) ?? samples[0];
  const sizes = sample ? sizesOf(sample, manifest.sizes) : [];
  const [size, setSize] = useState('1920x1080');
  const shownSize = sizes.includes(size) ? size : sizes[0] ?? '';
  const [backdrop, setBackdrop] = useState<Backdrop>('busy');
  const [picture, setPicture] = useState<'svg' | 'png'>('svg');

  if (!sample) return <p className="dim">No screen samples: add a [UiSample] method to a screen.</p>;

  return (
    <>
      <div className="mockups-pickers">
        <Picker label="Screen" items={names} value={group} onChange={g => { setGroup(g); setSlug(screens.get(g)?.[0]?.slug ?? ''); }} />
        <Picker label="Sample" items={samples.map(s => s.slug)} labels={samples.map(s => s.title)} value={sample.slug} onChange={setSlug} />
        <Picker label="Size" items={sizes} value={shownSize} onChange={setSize} />
        <Picker label="Backdrop" items={[...BACKDROPS]} value={backdrop} onChange={b => setBackdrop(b as Backdrop)} />
        <Picker label="Picture" items={['svg', 'png']} value={picture} onChange={p => setPicture(p as 'svg' | 'png')} />
      </div>
      <Frame sample={sample} size={shownSize} base={base} backdrop={backdrop} picture={picture} version={version} />
    </>
  );
}

function GalleryView({ manifest, base, version }: { manifest: Manifest; base: string; version: number }): ReactNode {
  const parts = useMemo(() => groups(manifest, true), [manifest]);
  return (
    <div className="gallery">
      {[...parts.entries()].map(([part, states]) => (
        <section key={part} className="gallery-part">
          <h3>{part}</h3>
          <div className="gallery-states">
            {states.map(s => (
              <figure key={s.slug}>
                <Frame sample={s} size={Object.keys(s.files)[0] ?? ''} base={base} backdrop="busy" picture="svg" version={version} compact />
                <figcaption>{s.title}</figcaption>
              </figure>
            ))}
          </div>
        </section>
      ))}
    </div>
  );
}

function Picker({ label, items, labels, value, onChange }: {
  label: string;
  items: string[];
  labels?: string[];
  value: string;
  onChange: (value: string) => void;
}): ReactNode {
  return (
    <div className="picker" role="group" aria-label={label}>
      <span className="dim">{label}</span>
      {items.map((item, i) => (
        <button key={item} aria-pressed={item === value} onClick={() => onChange(item)}>
          {labels?.[i] ?? item}
        </button>
      ))}
    </div>
  );
}

/**
 * One picture of one sample at one size, over a backdrop. The SVG is inlined
 * (so its text uses the preview's fonts) and hovering names the element under
 * the pointer from the sample's element JSON.
 */
function Frame({ sample, size, base, backdrop, picture, version, compact = false }: {
  sample: Sample;
  size: string;
  base: string;
  backdrop: Backdrop;
  picture: 'svg' | 'png';
  version: number;
  compact?: boolean;
}): ReactNode {
  const files = sample.files[size];
  const [svg, setSvg] = useState('');
  const [elements, setElements] = useState<Element[]>([]);
  const [hover, setHover] = useState<Element | null>(null);
  const box = useRef<HTMLDivElement>(null);
  const dims = parseSize(size) ?? [1920, 1080];
  const png = files ? pngFor(files, backdrop) : null;
  const showPng = picture === 'png' && png !== null;

  useEffect(() => {
    if (!files) return;
    let live = true;
    // A picture the watcher is rewriting can be missing for a moment: show
    // nothing for it rather than an unhandled rejection; the next render event
    // fetches again.
    fetch(`${base}${files.svg}?v=${version}`).then(r => r.text())
      .then(t => { if (live) setSvg(t); }, () => { if (live) setSvg(''); });
    fetch(`${base}${files.json}?v=${version}`).then(r => r.json() as Promise<Element[]>)
      .then(e => { if (live) setElements(e); }, () => { if (live) setElements([]); });
    return () => { live = false; };
  }, [files, base, version]);

  const onMove = useCallback((event: MouseEvent<HTMLDivElement>) => {
    const r = box.current?.getBoundingClientRect();
    if (!r || r.width === 0) return;
    const x = (event.clientX - r.left) * dims[0] / r.width;
    const y = (event.clientY - r.top) * dims[1] / r.height;
    setHover(elementAt(elements, x, y));
  }, [elements, dims]);

  if (!files) return <p className="dim">{sample.title} has no {size} picture.</p>;

  const background = backdrop !== 'none' && !showPng ? `url("${base}backdrops/${backdrop}.jpg")` : undefined;

  return (
    <div className={compact ? 'mockup compact' : 'mockup'}>
      <div
        ref={box}
        className="mockup-frame"
        data-testid="mockup-frame"
        style={{ aspectRatio: `${dims[0]} / ${dims[1]}`, backgroundImage: background }}
        onMouseMove={compact ? undefined : onMove}
        onMouseLeave={() => setHover(null)}
      >
        {showPng
          ? <img src={`${base}${png}?v=${version}`} alt={`${sample.title} at ${size}`} />
          : <div className="mockup-svg" dangerouslySetInnerHTML={{ __html: svg }} />}
        {hover && (
          <div
            className="mockup-highlight"
            style={{
              left: `${hover.x / dims[0] * 100}%`,
              top: `${hover.y / dims[1] * 100}%`,
              width: `${hover.w / dims[0] * 100}%`,
              height: `${hover.h / dims[1] * 100}%`,
            }}
          />
        )}
      </div>
      {!compact && (
        <div className="mockup-inspector" data-testid="inspector">
          {hover
            ? <><b>{pathOf(elements, hover) || hover.kind}</b> <span className="dim">{hover.kind} · id {hover.id.toString(16)} · {hover.x},{hover.y} {hover.w}×{hover.h} px{hover.flags !== 'None' ? ` · ${hover.flags}` : ''}</span></>
            : <span className="dim">Hover the picture to name an element. {size}, {sample.layer} layer.</span>}
        </div>
      )}
    </div>
  );
}

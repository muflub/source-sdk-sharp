/*
 * The Mockups tab's Live view: every segment of the game's UI (screens, HUD
 * elements, gallery parts, panels), drawn on request at any size with the
 * data it reads listed and editable.
 *
 * The list on the left is everything UiLive found in the UI assemblies. The
 * frame in the middle is the screen at the size chosen, from the preset sizes
 * or by dragging its edges; it is zoomed to fit, and hovering names the
 * element under the pointer. The panel on the right is every value the screen
 * read in its last render, grouped by data interface and call: an edit
 * re-renders, and the edits are kept in this browser per segment and preset.
 * Random fills every value with something plausible; Reset drops the edits.
 */

import { useCallback, useEffect, useMemo, useRef, useState, type MouseEvent, type PointerEvent, type ReactNode } from 'react';

import { elementAt, pathOf, type Element } from './manifest';
import {
  askLive, clearEdit, DEFAULT_VIEW, dragSize, editsKey, filterSegments, fitZoom, groupLeaves, KINDS, liveFontFaces,
  loadEdits, loadView, originals, randomEdits, saveEdits, saveView, seeded, setEdit, shortKey,
  type Edits, type LeafValue, type LiveFrame, type LiveLeaf, type LiveList, type LiveSegment, type LiveView as View,
} from './live';

const SCALES = [1, 1.25, 1.5, 1.75, 2];
const BACKDROPS = ['checker', 'dark', 'light'] as const;
const DEBOUNCE_MS = 120;

export function LiveView({ base, version }: { base: string; version: number }): ReactNode {
  const [list, setList] = useState<LiveList | null>(null);
  const [listError, setListError] = useState<string | null>(null);
  const [view, setViewState] = useState<View>(loadView);
  const [filter, setFilter] = useState('');
  // The fonts' CSS, made once per list (the faces are a few hundred kilobytes of base64).
  const fonts = useMemo(() => (list ? liveFontFaces(list) : ''), [list]);

  const setView = useCallback((patch: Partial<View>) => {
    setViewState(v => {
      const next = { ...v, ...patch };
      saveView(next);
      return next;
    });
  }, []);

  useEffect(() => {
    let live = true;
    void askLive<LiveList>(base, { op: 'list' }).then(r => {
      if (!live) return;
      if ('error' in r) setListError(r.error);
      else { setList(r); setListError(null); }
    });
    return () => { live = false; };
  }, [base, version]);

  const segment = list?.segments.find(s => s.id === view.segment) ?? list?.segments[0];
  const preset = segment?.presets.find(p => p.slug === view.preset)?.slug ?? segment?.presets[0]?.slug ?? '';

  if (listError && !list) {
    return (
      <div className="mockups-empty">
        <h3>The live mock-ups did not answer</h3>
        <p className="dim">{listError}</p>
        <p>
          The game answers them from its own client UI assemblies (a listen server or a client, not a dedicated
          server). Without the game, run <code>make ui-preview WATCH=1</code> and open{' '}
          <a href="http://127.0.0.1:7191/#mockups">http://127.0.0.1:7191/#mockups</a>.
        </p>
      </div>
    );
  }
  if (!list || !segment) return <p className="dim">Loading the UI's segments…</p>;

  return (
    <div className="live">
      <style>{fonts}</style>
      <SegmentList
        segments={list.segments}
        filter={filter}
        onFilter={setFilter}
        selected={segment.id}
        onSelect={id => setView({ segment: id, preset: '' })}
      />
      <Workbench key={`${segment.id}:${preset}`} base={base} version={version} list={list} segment={segment} preset={preset}
                 view={view} setView={setView} />
    </div>
  );
}

function SegmentList({ segments, filter, onFilter, selected, onSelect }: {
  segments: LiveSegment[];
  filter: string;
  onFilter: (f: string) => void;
  selected: string;
  onSelect: (id: string) => void;
}): ReactNode {
  const shown = filterSegments(segments, filter);
  return (
    <nav className="live-segments" aria-label="UI segments">
      <input
        type="search"
        placeholder={`Filter ${segments.length} segments`}
        value={filter}
        onChange={e => onFilter(e.target.value)}
      />
      {KINDS.map(({ kind, title }) => {
        const items = shown.filter(s => s.kind === kind);
        if (items.length === 0) return null;
        return (
          <section key={kind}>
            <h4>{title} <span className="dim">{items.length}</span></h4>
            <ul>
              {items.map(s => (
                <li key={s.id}>
                  <button aria-pressed={s.id === selected} onClick={() => onSelect(s.id)} title={s.id}>
                    <span>{s.name}</span>
                    <span className="dim">{s.assembly.replace(/^SourceSharp\./, '')}{s.presets.every(p => p.slug === 'auto') ? ' · made up' : ''}</span>
                  </button>
                </li>
              ))}
            </ul>
          </section>
        );
      })}
      {shown.length === 0 && <p className="dim">Nothing matches.</p>}
    </nav>
  );
}

/**
 * One segment and preset: the toolbar, the frame and the values. Keyed by the
 * pair, so a switch starts from that pair's saved edits with nothing of the
 * last one's state.
 */
function Workbench({ base, version, list, segment, preset, view, setView }: {
  base: string;
  version: number;
  list: LiveList;
  segment: LiveSegment;
  preset: string;
  view: View;
  setView: (patch: Partial<View>) => void;
}): ReactNode {
  const [edits, setEditsState] = useState<Edits>(() => loadEdits(segment.id, preset));
  const [frame, setFrame] = useState<LiveFrame | null>(null);
  const [orig, setOrig] = useState<Record<string, LeafValue>>({});
  const [busy, setBusy] = useState(false);
  const editsRef = useRef(edits);
  const randomPending = useRef<(() => number) | null>(null);
  const seq = useRef(0);

  const setEdits = useCallback((next: Edits) => {
    editsRef.current = next;
    setEditsState(next);
    saveEdits(segment.id, preset, next);
  }, [segment.id, preset]);

  useEffect(() => {
    const id = ++seq.current;
    const timer = setTimeout(() => {
      setBusy(true);
      void askLive<LiveFrame>(base, {
        op: 'render', segment: segment.id, preset, w: view.w, h: view.h, scale: view.scale, values: edits,
      }).then((r: LiveFrame) => {
        if (id !== seq.current) return;
        setBusy(false);
        setFrame(r);
        const values = r.values ?? [];
        setOrig(o => originals(o, values));
        // Random once more over what the random values made the screen read
        // (a raised count's new rows), so they are random too.
        const rng = randomPending.current;
        randomPending.current = null;
        if (rng && values.some(l => !(l.key in editsRef.current)))
          setEdits(randomEdits(values, rng, editsRef.current));
      });
    }, DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [base, version, segment.id, preset, view.w, view.h, view.scale, edits, setEdits]);

  const leaves = frame?.values ?? [];
  const edited = Object.keys(edits).length;

  const random = (): void => {
    const rng = seeded(Math.floor(Math.random() * 2 ** 31));
    randomPending.current = rng;
    setEdits(randomEdits(leaves, rng));
  };

  return (
    <>
      <div className="live-main">
        <Toolbar list={list} segment={segment} preset={preset} view={view} setView={setView}
                 frame={frame} busy={busy} edited={edited}
                 onRandom={random} onReset={() => setEdits({})} canRandom={leaves.length > 0} />
        <Stage frame={frame} view={view} setView={setView} />
      </div>
      <Values
        leaves={leaves}
        edits={edits}
        storageKey={editsKey(segment.id, preset)}
        onEdit={(key, value) => setEdits(setEdit(edits, key, value, orig[key]))}
        onClear={key => setEdits(clearEdit(edits, key))}
      />
    </>
  );
}

function Toolbar({ list, segment, preset, view, setView, frame, busy, edited, onRandom, onReset, canRandom }: {
  list: LiveList;
  segment: LiveSegment;
  preset: string;
  view: View;
  setView: (patch: Partial<View>) => void;
  frame: LiveFrame | null;
  busy: boolean;
  edited: number;
  onRandom: () => void;
  onReset: () => void;
  canRandom: boolean;
}): ReactNode {
  const size = list.sizes.find(s => s.w === view.w && s.h === view.h && s.scale === view.scale)?.name ?? 'custom';
  return (
    <div className="live-toolbar">
      <b>{segment.name}</b>
      <label>
        <span className="dim">Data</span>
        <select value={preset} onChange={e => setView({ preset: e.target.value })} aria-label="Data preset">
          {segment.presets.map(p => <option key={p.slug} value={p.slug}>{p.title}</option>)}
        </select>
      </label>
      <label>
        <span className="dim">Size</span>
        <select
          value={size}
          aria-label="Size preset"
          onChange={e => {
            const s = list.sizes.find(x => x.name === e.target.value);
            if (s) setView({ w: s.w, h: s.h, scale: s.scale });
          }}
        >
          {size === 'custom' && <option value="custom">custom</option>}
          {list.sizes.map(s => <option key={s.name} value={s.name}>{s.name}</option>)}
        </select>
      </label>
      <NumberBox label="Width" value={view.w} min={160} max={8192} onCommit={w => setView({ w })} />
      <span className="dim">×</span>
      <NumberBox label="Height" value={view.h} min={120} max={8192} onCommit={h => setView({ h })} />
      <label>
        <span className="dim">ui_scale</span>
        <select value={view.scale} onChange={e => setView({ scale: Number(e.target.value) })} aria-label="ui_scale">
          {[...new Set([...SCALES, view.scale])].sort((a, b) => a - b).map(s => <option key={s} value={s}>{s}</option>)}
        </select>
      </label>
      <button
        onClick={() => setView({ w: DEFAULT_VIEW.w, h: DEFAULT_VIEW.h, scale: DEFAULT_VIEW.scale })}
        title="1920×1080 at ui_scale 1"
      >
        Default size
      </button>
      <div className="segmented" role="group" aria-label="Backdrop">
        {BACKDROPS.map(b => (
          <button key={b} aria-pressed={view.backdrop === b} onClick={() => setView({ backdrop: b })}>{b}</button>
        ))}
      </div>
      <span className="live-spacer" />
      <span className="dim" aria-live="polite">
        {busy ? 'drawing…' : frame?.ms !== undefined ? `${frame.ms} ms` : ''}
      </span>
      <button onClick={onRandom} disabled={!canRandom} title="A plausible random value for every value the screen reads">Random</button>
      <button onClick={onReset} disabled={edited === 0} title="Drop every edit: back to the preset's data">Reset</button>
    </div>
  );
}

/** A number input that commits on Enter or blur, so a half-typed value does not re-render the screen. */
function NumberBox({ label, value, min, max, onCommit }: {
  label: string;
  value: number;
  min: number;
  max: number;
  onCommit: (v: number) => void;
}): ReactNode {
  const [text, setText] = useState(String(value));
  useEffect(() => setText(String(value)), [value]);
  const commit = (): void => {
    const v = Math.round(Number(text));
    if (Number.isFinite(v)) onCommit(Math.min(max, Math.max(min, v)));
    else setText(String(value));
  };
  return (
    <input
      className="live-number"
      aria-label={label}
      inputMode="numeric"
      value={text}
      onChange={e => setText(e.target.value)}
      onBlur={commit}
      onKeyDown={e => { if (e.key === 'Enter') commit(); }}
    />
  );
}

/**
 * The screen at its size, zoomed to fit the space, with handles on its right
 * edge, bottom edge and corner that resize the screen (not the zoom: the zoom
 * holds while a handle is dragged, so the frame follows the pointer).
 */
function Stage({ frame, view, setView }: { frame: LiveFrame | null; view: View; setView: (patch: Partial<View>) => void }): ReactNode {
  const host = useRef<HTMLDivElement>(null);
  const box = useRef<HTMLDivElement>(null);
  const [space, setSpace] = useState({ w: 960, h: 540 });
  const [drag, setDrag] = useState<{ x: number; y: number; w: number; h: number; zoom: number; axis: 'x' | 'y' | 'xy' } | null>(null);
  const [hover, setHover] = useState<Element | null>(null);

  useEffect(() => {
    const el = host.current;
    if (!el || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(([entry]) => {
      if (entry) setSpace({ w: entry.contentRect.width, h: entry.contentRect.height });
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  const zoom = drag?.zoom ?? fitZoom(view.w, view.h, space.w - 32, space.h - 32);
  const elements = frame?.elements ?? [];
  // The picture is the last answer's size until the next answer comes.
  const drawnW = frame?.w ?? view.w, drawnH = frame?.h ?? view.h;

  const onDown = (axis: 'x' | 'y' | 'xy') => (e: PointerEvent<HTMLDivElement>): void => {
    e.preventDefault();
    e.currentTarget.setPointerCapture?.(e.pointerId);
    setDrag({ x: e.clientX, y: e.clientY, w: view.w, h: view.h, zoom, axis });
  };
  const onMove = (e: PointerEvent<HTMLDivElement>): void => {
    if (!drag) return;
    const next = dragSize(drag, drag.axis === 'y' ? 0 : e.clientX - drag.x, drag.axis === 'x' ? 0 : e.clientY - drag.y, drag.zoom);
    if (next.w !== view.w || next.h !== view.h) setView(next);
  };
  const onUp = (): void => setDrag(null);

  const onHover = (e: MouseEvent<HTMLDivElement>): void => {
    const r = box.current?.getBoundingClientRect();
    if (!r || r.width === 0) return;
    setHover(elementAt(elements, (e.clientX - r.left) * drawnW / r.width, (e.clientY - r.top) * drawnH / r.height));
  };

  return (
    <div className="live-stage" ref={host}>
      <div className="live-frame-wrap" style={{ width: view.w * zoom, height: view.h * zoom }}>
        <div
          ref={box}
          className={`live-frame backdrop-${view.backdrop}`}
          data-testid="live-frame"
          onMouseMove={onHover}
          onMouseLeave={() => setHover(null)}
        >
          {frame?.svg && <div className="mockup-svg" dangerouslySetInnerHTML={{ __html: frame.svg }} />}
          {hover && (
            <div
              className="mockup-highlight"
              style={{
                left: `${hover.x / drawnW * 100}%`,
                top: `${hover.y / drawnH * 100}%`,
                width: `${hover.w / drawnW * 100}%`,
                height: `${hover.h / drawnH * 100}%`,
              }}
            />
          )}
          {frame?.error && <div className="live-error" role="alert">{frame.error}</div>}
        </div>
        {(['x', 'y', 'xy'] as const).map(axis => (
          <div
            key={axis}
            className={`live-handle live-handle-${axis}`}
            role="separator"
            aria-label={axis === 'x' ? 'Drag to change the width' : axis === 'y' ? 'Drag to change the height' : 'Drag to change the size'}
            onPointerDown={onDown(axis)}
            onPointerMove={onMove}
            onPointerUp={onUp}
            onPointerCancel={onUp}
          />
        ))}
      </div>
      <div className="mockup-inspector live-inspector" data-testid="inspector">
        {hover
          ? <><b>{pathOf(elements, hover) || hover.kind}</b> <span className="dim">{hover.kind} · {hover.x},{hover.y} {hover.w}×{hover.h} px{hover.flags !== 'None' ? ` · ${hover.flags}` : ''}</span></>
          : <span className="dim">{view.w}×{view.h}{view.scale !== 1 ? ` at ui_scale ${view.scale}` : ''}, shown at {Math.round(zoom * 100)}%{frame?.layer ? `, ${frame.layer} layer` : ''}. Drag an edge to resize; hover to name an element.</span>}
      </div>
    </div>
  );
}

function Values({ leaves, edits, storageKey, onEdit, onClear }: {
  leaves: LiveLeaf[];
  edits: Edits;
  storageKey: string;
  onEdit: (key: string, value: LeafValue) => void;
  onClear: (key: string) => void;
}): ReactNode {
  const [filter, setFilter] = useState('');
  const text = filter.trim().toLowerCase();
  const shown = text ? leaves.filter(l => l.key.toLowerCase().includes(text)) : leaves;
  const groups = groupLeaves(shown);
  const edited = Object.keys(edits).length;

  return (
    <aside className="live-values" aria-label="Mock data">
      <div className="live-values-head">
        <b>Data</b>
        <span className="dim">{leaves.length} values read · {edited} edited</span>
      </div>
      <input type="search" placeholder="Filter values" value={filter} onChange={e => setFilter(e.target.value)} />
      {leaves.length === 0 && <p className="dim">The screen read no data.</p>}
      {[...groups.entries()].map(([source, calls]) => (
        <details key={source} open>
          <summary>{source} <span className="dim">{[...calls.values()].reduce((n, l) => n + l.length, 0)}</span></summary>
          {[...calls.entries()].map(([call, items]) => {
            const single = items.length === 1 && shortKey(items[0]!) === call;
            return (
              <div key={call} className="live-call">
                {!single && <div className="live-call-name">{call}</div>}
                {items.map(l => (
                  <LeafRow
                    key={l.key}
                    leaf={l}
                    label={single ? call : shortKey(l).slice(call.length + 1) || 'returns'}
                    edited={l.key in edits}
                    value={l.key in edits ? edits[l.key]! : l.value}
                    onEdit={v => onEdit(l.key, v)}
                    onClear={() => onClear(l.key)}
                  />
                ))}
              </div>
            );
          })}
        </details>
      ))}
      <p className="dim live-storage">Edits are kept in this browser ({storageKey}).</p>
    </aside>
  );
}

function LeafRow({ leaf, label, edited, value, onEdit, onClear }: {
  leaf: LiveLeaf;
  label: string;
  edited: boolean;
  value: LeafValue;
  onEdit: (v: LeafValue) => void;
  onClear: () => void;
}): ReactNode {
  return (
    <div className={edited ? 'live-leaf edited' : 'live-leaf'}>
      <label title={leaf.key}>
        <span className="live-leaf-name">{label}</span>
        <LeafInput leaf={leaf} value={value} onEdit={onEdit} label={leaf.key} />
      </label>
      {edited && <button className="live-leaf-reset" onClick={onClear} title="Back to the preset's value" aria-label={`Reset ${leaf.key}`}>↺</button>}
    </div>
  );
}

/** The input for a leaf's type. Text and numbers commit as typed when they parse. */
function LeafInput({ leaf, value, onEdit, label }: { leaf: LiveLeaf; value: LeafValue; onEdit: (v: LeafValue) => void; label: string }): ReactNode {
  const [text, setText] = useState(value === null ? '' : String(value));
  useEffect(() => setText(value === null ? '' : String(value)), [value]);

  if (leaf.type === 'bool')
    return <input type="checkbox" aria-label={label} checked={value === true} onChange={e => onEdit(e.target.checked)} />;
  if (leaf.type === 'enum')
    return (
      <select aria-label={label} value={String(value)} onChange={e => onEdit(e.target.value)}>
        {(leaf.options ?? [String(value)]).map(o => <option key={o} value={o}>{o}</option>)}
      </select>
    );
  if (leaf.type === 'int' || leaf.type === 'float')
    return (
      <input
        aria-label={label}
        inputMode={leaf.type === 'int' ? 'numeric' : 'decimal'}
        value={text}
        onChange={e => {
          setText(e.target.value);
          const raw = e.target.value.trim();
          const v = Number(raw);
          if (raw !== '' && Number.isFinite(v)) onEdit(leaf.type === 'int' ? Math.round(v) : v);
          else if (/^-?Infinity$|^NaN$/.test(raw)) onEdit(raw);
        }}
      />
    );
  return <input aria-label={label} value={text} onChange={e => { setText(e.target.value); onEdit(e.target.value); }} />;
}

/*
 * The AI tab's live 3D map (plan_hl2mp_ai §7.1): every player over the map's
 * wireframe, and for each bot where it aims, what it sees and refuses, where it
 * is going, what it heard and what it remembers. The selected bot is the tab's
 * own (its buttons and a click here are one choice), drawn bright over the
 * others.
 *
 * It asks /ai/world about five times a second through the page's one way of
 * polling, and only while the tab shows. The renderer holds the geometry on the
 * GPU; the layers are built by ai/aiWorld.ts, which is where their tests are.
 */

import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';

import { getAiNav, getAiWorld, getMapGeometry } from '../api';
import { usePoll } from '../hooks/usePoll';
import { buildNavLayers } from '../map/aiNav';
import { attachCamera } from '../map/viewport/cameraControls';
import { BOT_LINE_LAYERS, View, buildWorld } from '../map/viewport/renderer';
import type { MapGeometry } from '../types';
import { STAGE_COLOURS } from './aiInspect';
import {
  ALL_LAYERS, HEARD_COLOURS, WORLD_COLOURS, WORLD_LAYERS, WORLD_POLL_MS, buildWorldLayers, emptyState, fitPlayers, followPoint, mergeWorld,
  pickables, placeLabels, staleness, type AiWorld, type WorldLabel, type WorldToggles,
} from './aiWorld';

/** What a layer shows is what is so, what the bot means to do, or what it believes, which can be wrong. */
const GROUP_NAMES = { truth: 'Truth:', plan: 'Plan:', belief: 'Beliefs:' } as const;

/** How long a wireframe that could not be read waits to be asked for again. */
const GEOMETRY_RETRY_MS = 2000;

const css = (c: readonly number[]): string => `rgb(${c.map(v => Math.round(v * 255)).join(' ')})`;

function Key({ colour, label }: { colour: readonly number[]; label: string }): ReactNode {
  return <span className="ai3d-key"><i style={{ background: css(colour) }} />{label}</span>;
}

/** The colours, by layer, so that a line can be read without the source open. */
function Legend(): ReactNode {
  return (
    <div className="ai3d-legend">
      <span><b>truth</b> <Key colour={WORLD_COLOURS.bot} label="bot" /><Key colour={WORLD_COLOURS.human} label="human" /><Key colour={WORLD_COLOURS.aim} label="aim" /><Key colour={WORLD_COLOURS.cone} label="view cone" /></span>
      <span>
        <b>sight</b> <Key colour={WORLD_COLOURS.seen} label="seen" /><Key colour={WORLD_COLOURS.partly} label="partly" /><Key colour={WORLD_COLOURS.recognising} label="recognising" />
        {(['range', 'cone', 'pvs', 'trace', 'unserved'] as const).map(s => <Key key={s} colour={STAGE_COLOURS[s]!} label={`refused: ${s}`} />)}
      </span>
      <span><b>plan</b> <Key colour={WORLD_COLOURS.route} label="route, goal" /><Key colour={WORLD_COLOURS.target} label="target (dashed)" /></span>
      <span>
        <b>belief</b> <Key colour={WORLD_COLOURS.remembered} label="last seen" />
        {(['gunfire', 'explosion', 'reload', 'footstep', 'pain', 'door', 'physics'] as const).map(k => <Key key={k} colour={HEARD_COLOURS[k]!} label={k} />)}
      </span>
    </div>
  );
}

export function AiWorldPanel({ visible, selected, onSelect }: {
  visible: boolean;
  selected: number | null;
  onSelect: (slot: number) => void;
}): ReactNode {
  const host = useRef<HTMLDivElement>(null);
  const view = useRef<View | null>(null);
  const [broken, setBroken] = useState<string | null>(null);

  const [world, setWorld] = useState<AiWorld | null>(null);
  const [failed, setFailed] = useState<string | null>(null);
  const [geometry, setGeometry] = useState<MapGeometry | null>(null);
  const [toggles, setToggles] = useState<WorldToggles>(ALL_LAYERS);
  const [others, setOthers] = useState(true);
  const [showNav, setShowNav] = useState(false);
  const [follow, setFollow] = useState(false);
  const [labels, setLabels] = useState<WorldLabel[]>([]);
  const [clock, setClock] = useState({ now: 0, changed: 0, drawMs: 0 });

  // What the draw path and the handlers installed once see of the newest render.
  const latest = useRef({ world, selected, onSelect, follow });
  latest.current = { world, selected, onSelect, follow };

  const [retry, setRetry] = useState(0);
  const handledRetry = useRef(0);
  const retryAfter = useRef(0);
  const lastTick = useRef<{ tick: number; at: number } | null>(null);
  const drawMs = useRef(0);
  const pending = useRef(false);

  const draw = useCallback(() => {
    if (pending.current) return;
    pending.current = true;
    requestAnimationFrame(() => {
      pending.current = false;
      const v = view.current;
      if (!v) return;
      const started = performance.now();
      const { world: w, selected: s, follow: f } = latest.current;
      const at = f ? followPoint(w, s) : null;
      if (at) v.orbit.target = at;
      v.draw();
      setLabels(placeLabels(w?.actors ?? [], s, p => v.project(p), v.canvas.clientWidth, v.canvas.clientHeight));
      drawMs.current = performance.now() - started;
    });
  }, []);

  useEffect(() => {
    const element = host.current;
    const canvas = element?.querySelector('canvas');
    if (!element || !canvas) return;
    try {
      view.current = new View(canvas, 'persp');
    } catch (error) {
      setBroken(error instanceof Error ? error.message : String(error));
      return;
    }
    const detach = attachCamera(element, () => view.current, draw, (v, x, y) => {
      const hit = v.pick(x, y, pickables(latest.current.world));
      if (hit) latest.current.onSelect(hit.slot);
    });
    window.addEventListener('resize', draw);
    return () => {
      detach();
      window.removeEventListener('resize', draw);
      view.current = null;
    };
  }, [draw]);

  usePoll(async () => {
    if (!visible || document.hidden) return;
    if (retryAfter.current > 0 && Date.now() >= retryAfter.current) {
      retryAfter.current = 0;
      setRetry(r => r + 1);
    }
    try {
      const answer = await getAiWorld();
      const now = Date.now();
      const seen = lastTick.current?.tick === answer.tick && lastTick.current ? lastTick.current : { tick: answer.tick, at: now };
      lastTick.current = seen;
      setWorld(last => mergeWorld(last, answer));
      setFailed(null);
      setClock({ now, changed: seen.at, drawMs: drawMs.current });
    } catch (error) {
      setFailed(error instanceof Error ? error.message : String(error));
      setClock(c => ({ ...c, now: Date.now() }));
    }
  }, WORLD_POLL_MS);

  // The map's wireframe, read once per map: the largest answer the API has.
  // The last map's goes at once. A read that failed is asked again by the poll.
  const hasAnswer = world !== null && !world.error;
  const mapKey = hasAnswer ? world.map : null;
  const loadedMap = useRef<string | null>(null);
  const [geometryNote, setGeometryNote] = useState<string | null>(null);
  useEffect(() => {
    if (!visible || mapKey === null || loadedMap.current === mapKey && retry === handledRetry.current) return;
    let cancelled = false;
    if (loadedMap.current !== mapKey) setGeometry(null);
    loadedMap.current = mapKey;
    handledRetry.current = retry;
    setGeometryNote(null);
    void getMapGeometry(mapKey || undefined).then(answer => {
      if (cancelled) return;
      setGeometry(answer.error ? null : answer);
      setGeometryNote(answer.error ?? null);
    }, () => {
      if (!cancelled) retryAfter.current = Date.now() + GEOMETRY_RETRY_MS;
    });
    return () => { cancelled = true; };
  }, [visible, mapKey, retry]);

  // The camera on the players, or on the whole map with none alive.
  const fit = useCallback(() => {
    const v = view.current;
    if (!v) return;
    const players = fitPlayers(latest.current.world);
    if (players) {
      v.orbit.target = players.target;
      v.orbit.distance = players.distance;
    } else if (geometry) {
      v.frame(geometry.mins, geometry.maxs);
    }
    draw();
  }, [geometry, draw]);

  // A wireframe frames itself, which sets the clip planes.
  useEffect(() => {
    const v = view.current;
    if (!v) return;
    const built = geometry ? buildWorld(geometry) : { positions: new Float32Array(0), colours: new Float32Array(0) };
    v.upload('world', built.positions, built.colours);
    if (geometry) v.frame(geometry.mins, geometry.maxs);
    draw();
  }, [geometry, draw]);

  // The first players on a map fit once, and once more on its wireframe's framing.
  const frameKey = geometry ?? mapKey;
  const fitted = useRef<MapGeometry | string | null>(null);
  useEffect(() => {
    if (frameKey === null || fitted.current === frameKey || !fitPlayers(world)) return;
    fitted.current = frameKey;
    fit();
  }, [world, frameKey, fit]);

  // The nav's cells are 35,000 points on dm_lockdown: off until asked for, and read once per map.
  const navFor = useRef<string | null>(null);
  useEffect(() => {
    const v = view.current;
    if (!v) return;
    const empty = new Float32Array(0);
    if (!showNav || mapKey === null) {
      v.upload('navCells', empty, empty);
      navFor.current = null;
      draw();
      return;
    }
    if (navFor.current === mapKey) return;
    let cancelled = false;
    navFor.current = mapKey;
    void getAiNav().then(answer => {
      if (cancelled || answer.error || !answer.cell) return;
      // The cells alone: dm_lockdown's thousands of drop links would hide the bots' lines (the map tab shows those).
      const { cells } = buildNavLayers(answer);
      v.upload('navCells', cells.positions, cells.colours);
      draw();
    });
    return () => { cancelled = true; };
  }, [showNav, mapKey, draw]);

  useEffect(() => {
    const v = view.current;
    if (!v) return;
    if (world && !world.error) {
      const layers = buildWorldLayers(world, selected, toggles, others);
      for (const name of BOT_LINE_LAYERS) v.upload(name, layers[name].positions, layers[name].colours);
      v.upload('botMarkers', layers.botMarkers.positions, layers.botMarkers.colours);
    }
    draw();
  }, [world, selected, toggles, others, follow, draw]);

  // A canvas that has never been shown has no size.
  useEffect(() => {
    if (visible) draw();
  }, [visible, draw]);

  const empty = broken ? `The 3D view could not start: ${broken}` : emptyState(world, failed, geometry !== null);
  const age = staleness(clock.changed, clock.now);
  const chosen = world?.bots.find(b => b.slot === selected);

  return (
    <section className="ai3d">
      <div className="ai3d-toolbar">
        {WORLD_LAYERS.map((layer, i) => (
          <label key={layer.key} className="ai3d-toggle">
            {layer.group !== WORLD_LAYERS[i - 1]?.group && <span className="ai3d-group">{GROUP_NAMES[layer.group]}</span>}
            <input
              type="checkbox" checked={toggles[layer.key]} aria-label={layer.label}
              onChange={e => setToggles({ ...toggles, [layer.key]: e.target.checked })}
            />
            {layer.label}
          </label>
        ))}
        <span className="ai3d-sep" />
        <label className="ai3d-toggle"><input type="checkbox" checked={others} aria-label="Other bots" onChange={e => setOthers(e.target.checked)} />Other bots</label>
        <label className="ai3d-toggle"><input type="checkbox" checked={showNav} aria-label="Nav cells" onChange={e => setShowNav(e.target.checked)} />Nav cells</label>
        <label className="ai3d-toggle"><input type="checkbox" checked={follow} aria-label="Follow selected" onChange={e => setFollow(e.target.checked)} />Follow selected</label>
        <button type="button" onClick={fit}>Fit players</button>
        <span className={`ai3d-readout${age.stale || failed ? ' ai3d-stale' : ''}`} data-testid="world-readout">
          {world && !world.error
            ? <>tick {world.tick} · {age.stale || failed ? `STALE ${age.seconds.toFixed(1)} s` : 'live'} · route {world.costUs.toFixed(0)} µs · draw {clock.drawMs.toFixed(1)} ms</>
            : 'no data'}
        </span>
      </div>
      <div className="ai3d-view" ref={host}>
        <canvas />
        <div className="ai3d-labels">
          {labels.map(l => (
            <button
              key={l.slot} type="button" data-testid={`world-label-${l.slot}`}
              className={`ai3d-label${l.isBot ? '' : ' ai3d-human'}${l.selected ? ' ai3d-selected' : ''}`}
              style={{ left: l.x, top: l.y }} disabled={!l.isBot} onClick={() => onSelect(l.slot)}
            >{l.name}</button>
          ))}
        </div>
        {empty && <p className="ai3d-empty" data-testid="world-empty">{empty}</p>}
      </div>
      <p className="ai3d-doing muted" data-testid="world-doing">
        {chosen
          ? <>{chosen.name}: {chosen.truth ? `${chosen.going?.branch ?? '-'}/${chosen.going?.leaf ?? '-'}` : 'dead'}
            {chosen.route ? ` · route ${chosen.route.source}, ${chosen.route.points.length} points` : ' · no route'}
            {(chosen.going?.targetSlot ?? -1) >= 0 ? ` · target slot ${chosen.going!.targetSlot}` : ''}
            {` · heard ${chosen.beliefs.heard.length} in 5 s`}</>
          : 'No bot selected.'}
        {world?.navRefusal ? ` · no navigation: ${world.navRefusal}` : ''}
        {geometryNote ? ` · no wireframe: ${geometryNote}` : ''}
      </p>
      <Legend />
    </section>
  );
}

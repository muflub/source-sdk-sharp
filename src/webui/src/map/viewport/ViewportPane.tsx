/*
 * Four views of one scene.
 *
 * A perspective camera you can orbit, and the three axis-aligned views a level
 * editor has. They exist for different questions — "where is this thing" is a
 * top-down question, "what can see it" is a perspective one — and answering both
 * without leaving the debugger is the point.
 *
 * The renderer is not React. It is told what to draw and when, and it keeps its
 * geometry on the GPU between frames; describing that as markup would mean
 * re-uploading a map's worth of vertices every time the selection changed. What
 * React owns here is the canvases, the events, and the one thing that is really
 * state: whether there is any geometry to draw yet.
 */

import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';

import type { MapGeometry } from '../../types';
import { keyOf, type Entity } from '../model';
import { View, buildEntities, buildWorld, type Drag, type ViewMode } from './renderer';
import { getAiBot, getAiBots, getAiFlow, getAiNav } from '../../api';
import { buildConeLayer, buildHeatLayer } from '../../ai/aiInspect';
import { buildFlowLayer, buildNavLayers, type AiNav } from '../aiNav';

const VIEWS: [ViewMode, string][] = [
  ['persp', '3D'],
  ['top', 'Top (x/y)'],
  ['front', 'Front (x/z)'],
  ['side', 'Side (y/z)'],
];

export function ViewportPane({ visible, entities, map, selectedKey, onSelect, loadGeometry }: {
  visible: boolean;
  entities: Entity[];
  map: string;
  selectedKey: string | null;
  onSelect: (entity: Entity) => void;

  /** Reads the map's geometry, or answers null if it has none. */
  loadGeometry: () => Promise<MapGeometry | null>;
}): ReactNode {
  const host = useRef<HTMLDivElement>(null);
  const views = useRef<View[]>([]);
  const [note, setNote] = useState('');

  const [geometry, setGeometry] = useState<MapGeometry | null>(null);

  // The AI's nav and flow layers (plan_hl2mp_ai §7.1), off until asked for.
  const [showNav, setShowNav] = useState(false);
  const [aiNav, setAiNav] = useState<AiNav | null>(null);
  const loadedFor = useRef('');

  /*
   * The entity list, as the draw path sees it.
   *
   * A ref as well as a prop because picking happens inside a DOM handler
   * installed once, and a handler that closed over the first render's list would
   * be picking from the map as it was when the tab opened.
   */
  const latest = useRef({ entities, onSelect });
  latest.current = { entities, onSelect };

  const pendingDraw = useRef(false);

  const draw = useCallback(() => {
    if (pendingDraw.current) return;

    pendingDraw.current = true;
    requestAnimationFrame(() => {
      pendingDraw.current = false;
      for (const view of views.current) view.draw();
    });
  }, []);

  //-------------------------------------------------------------------------
  // The views themselves.
  //-------------------------------------------------------------------------

  useEffect(() => {
    const element = host.current;
    if (!element) return;

    const made: View[] = [];

    for (const [mode] of VIEWS) {
      const canvas = element.querySelector<HTMLCanvasElement>(`canvas[data-view="${mode}"]`);
      if (!canvas) continue;

      try {
        made.push(new View(canvas, mode));
      } catch (error) {
        // One view failing should not take the other three with it, and a page
        // with no viewport at all is still a working debugger.
        setNote(`a view could not start: ${error instanceof Error ? error.message : String(error)}`);
      }
    }

    views.current = made;

    const onResize = (): void => draw();
    window.addEventListener('resize', onResize);

    return () => {
      window.removeEventListener('resize', onResize);
      views.current = [];
    };
  }, [draw]);

  //-------------------------------------------------------------------------
  // Geometry, read once per map.
  //
  // Only when the pane is actually up: it is the largest response the API has,
  // and a tab nobody has opened should not be asking for one.
  //-------------------------------------------------------------------------

  useEffect(() => {
    if (!visible || !map || loadedFor.current === map) return;

    let cancelled = false;
    loadedFor.current = map;
    setNote('reading map geometry…');

    void loadGeometry().then(result => {
      if (cancelled) return;

      setGeometry(result);
      setNote(result ? '' : 'this map has no geometry to draw');
    });

    return () => { cancelled = true; };
  }, [visible, map, loadGeometry]);

  // A map with no geometry read yet is still worth framing, so that entities
  // are somewhere rather than at the far end of a default camera.
  useEffect(() => {
    if (!visible) return;

    const world = geometry ? buildWorld(geometry) : null;

    for (const view of views.current) {
      if (world) view.upload('world', world.positions, world.colours);
      if (geometry) view.frame(geometry.mins, geometry.maxs);
      else view.frame(...bounds(entities));
    }

    draw();
    // Framing follows the geometry, not the entity list: re-framing every time
    // a live poll moved something would fight whoever is looking at it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [visible, geometry, draw]);

  useEffect(() => {
    if (!visible) return;

    const radius = views.current[0]?.radius ?? 1024;
    const built = buildEntities(entities, geometry, selectedKey, radius);

    for (const view of views.current) {
      view.upload('entities', built.entities.positions, built.entities.colours);
      view.upload('brushes', built.brushes.positions, built.brushes.colours);
      view.upload('selection', built.selection.positions, built.selection.colours);
    }

    draw();
  }, [visible, entities, geometry, selectedKey, draw]);

  useEffect(() => {
    if (!visible || !showNav) return;
    let cancelled = false;
    void getAiNav().then(answer => {
      if (cancelled) return;
      if (answer.error || !answer.cell) {
        setNote(`AI nav: ${answer.error ?? 'not served'}`);
        setAiNav(null);
        return;
      }
      setAiNav(answer);
    });
    return () => { cancelled = true; };
  }, [visible, showNav, map]);

  useEffect(() => {
    if (!visible) return;
    const empty = new Float32Array(0);
    const layers = showNav && aiNav ? buildNavLayers(aiNav) : null;
    for (const view of views.current) {
      view.upload('navCells', layers?.cells.positions ?? empty, layers?.cells.colours ?? empty);
      view.upload('navLinks', layers?.links.positions ?? empty, layers?.links.colours ?? empty);
    }
    draw();
  }, [visible, showNav, aiNav, draw]);

  // The bots' eyes and beliefs (plan_hl2mp_ai §7.1): each bot's cone and its
  // lines to every candidate by stage, and every unseen opponent's belief as
  // a heatmap, polled while the nav layers are on.
  const [aiTick, setAiTick] = useState(0);
  useEffect(() => {
    if (!visible || !showNav) return;
    const id = setInterval(() => setAiTick(t => t + 1), 1000);
    return () => clearInterval(id);
  }, [visible, showNav]);

  useEffect(() => {
    if (!visible) return;
    const empty = new Float32Array(0);
    if (!showNav || !aiNav) {
      for (const view of views.current) {
        view.upload('aiCone', empty, empty);
        view.upload('aiHeat', empty, empty);
      }
      draw();
      return;
    }
    let cancelled = false;
    void (async () => {
      const list = await getAiBots();
      if (cancelled || !Array.isArray(list)) return;
      const bots = await Promise.all(list.map(b => getAiBot(b.slot)));
      if (cancelled) return;
      const cones = bots.filter(b => b.perception).map(b => buildConeLayer(b.perception));
      const heats = bots.flatMap(b => b.record?.memory?.actors ?? []).filter(a => a.belief && !a.inSight).map(a => buildHeatLayer(aiNav, a.belief!));
      const join = (layers: { positions: Float32Array; colours: Float32Array }[]) => {
        const n = layers.reduce((s, l) => s + l.positions.length, 0);
        const out = { positions: new Float32Array(n), colours: new Float32Array(n) };
        let at = 0;
        for (const l of layers) {
          out.positions.set(l.positions, at);
          out.colours.set(l.colours, at);
          at += l.positions.length;
        }
        return out;
      };
      const cone = join(cones), heat = join(heats);
      for (const view of views.current) {
        view.upload('aiCone', cone.positions, cone.colours);
        view.upload('aiHeat', heat.positions, heat.colours);
      }
      draw();
    })();
    return () => { cancelled = true; };
  }, [visible, showNav, aiNav, aiTick, draw]);

  // The flow field to the selected entity: where a bot sent there would go.
  useEffect(() => {
    if (!visible) return;
    const empty = new Float32Array(0);
    const target = entities.find(e => keyOf(e) === selectedKey);
    if (!showNav || !aiNav || !target) {
      for (const view of views.current) view.upload('navFlow', empty, empty);
      draw();
      return;
    }
    let cancelled = false;
    void getAiFlow(target.origin[0], target.origin[1], target.origin[2]).then(flow => {
      if (cancelled || flow.error || !flow.next) return;
      const layer = buildFlowLayer(aiNav, flow);
      for (const view of views.current) view.upload('navFlow', layer.positions, layer.colours);
      draw();
    });
    return () => { cancelled = true; };
  }, [visible, showNav, aiNav, selectedKey, entities, draw]);

  //-------------------------------------------------------------------------
  // Orbiting, panning, zooming, picking.
  //
  // On the container rather than per canvas, and the move and release are on the
  // window: a drag that leaves the canvas it started in should keep going, which
  // is exactly what happens when someone spins a camera quickly.
  //-------------------------------------------------------------------------

  useEffect(() => {
    const element = host.current;
    if (!element) return;

    let dragging: { view: View; drag: Drag } | null = null;

    const viewFor = (target: EventTarget | null): View | null => {
      const canvas = (target as HTMLElement | null)?.closest?.('canvas');
      return views.current.find(view => view.canvas === canvas) ?? null;
    };

    const onMouseDown = (event: MouseEvent): void => {
      const view = viewFor(event.target);
      if (!view) return;

      dragging = { view, drag: view.beginDrag(event) };
      event.preventDefault();
    };

    const onMouseMove = (event: MouseEvent): void => {
      if (!dragging) return;

      dragging.view.applyDrag(dragging.drag,
        event.clientX - dragging.drag.x, event.clientY - dragging.drag.y);

      draw();
    };

    const onMouseUp = (event: MouseEvent): void => {
      if (dragging && !dragging.drag.moved && event.button === 0) {
        const rect = dragging.view.canvas.getBoundingClientRect();
        const hit = dragging.view.pick(
          event.clientX - rect.left, event.clientY - rect.top, latest.current.entities);

        if (hit) latest.current.onSelect(hit);
      }

      dragging = null;
    };

    const onWheel = (event: WheelEvent): void => {
      const view = viewFor(event.target);
      if (!view) return;

      event.preventDefault();
      view.zoomBy(event.deltaY);
      draw();
    };

    const noMenu = (event: Event): void => event.preventDefault();

    element.addEventListener('mousedown', onMouseDown);
    element.addEventListener('contextmenu', noMenu);

    // Not passive, because zooming the map must not also scroll the page.
    element.addEventListener('wheel', onWheel, { passive: false });
    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup', onMouseUp);

    return () => {
      element.removeEventListener('mousedown', onMouseDown);
      element.removeEventListener('contextmenu', noMenu);
      element.removeEventListener('wheel', onWheel);
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onMouseUp);
    };
  }, [draw]);

  // A canvas that has never been shown has no size, so the first frame after the
  // pane appears is drawn once it does.
  useEffect(() => {
    if (visible) draw();
  }, [visible, draw]);

  return (
    <>
      <p className="note" hidden={!note}>{note}</p>

      <label className="note">
        <input type="checkbox" checked={showNav} onChange={e => setShowNav(e.target.checked)} />
        {' '}AI nav: cells by who stands there, links by how they are crossed, and the flow field to the selected entity
      </label>

      <div className="viewports" ref={host}>
        {VIEWS.map(([mode, label]) => (
          <div className="viewport" key={mode}>
            <span className="viewport-label">{label}</span>
            <canvas data-view={mode} />
          </div>
        ))}
      </div>

      <p className="note">
        Drag to orbit or pan, wheel to zoom, click an entity to select it.
        The world is drawn as wireframe so that entities inside it stay visible.
      </p>
    </>
  );
}

/** Where the entities are, for a map whose geometry could not be read. */
function bounds(entities: Entity[]): [[number, number, number], [number, number, number]] {
  if (entities.length === 0) return [[-512, -512, -512], [512, 512, 512]];

  const mins: [number, number, number] = [Infinity, Infinity, Infinity];
  const maxs: [number, number, number] = [-Infinity, -Infinity, -Infinity];

  for (const entity of entities) {
    for (let axis = 0; axis < 3; axis++) {
      mins[axis] = Math.min(mins[axis]!, entity.origin[axis]!);
      maxs[axis] = Math.max(maxs[axis]!, entity.origin[axis]!);
    }
  }

  return [mins, maxs];
}

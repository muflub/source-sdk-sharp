/*
 * The checks a real browser is required for.
 *
 * Everything that is logic — YAML, graph layout, suggestions, the group store,
 * the camera maths — is in Vitest, which runs in jsdom and needs no browser.
 * Three things could not come with it:
 *
 *   - Whether a hidden panel is actually hidden. That is a CSS cascade question,
 *     and jsdom does not have one. It is worth a test because [hidden] loses to
 *     display:grid, which once had every tab rendering at once down the page.
 *
 *   - The drag that makes a connection. It ends by asking the document what is
 *     under the pointer, and jsdom has no layout, so elementFromPoint answers
 *     nothing there.
 *
 *   - Clicking an entity in a viewport. Picking is projection maths against a
 *     canvas, and jsdom has neither a WebGL context nor a canvas with a size.
 *
 * Open /ui/selftest.html after changing any of them.
 */

import { createRoot } from 'react-dom/client';

import { GraphPane } from './map/graph/GraphPane';
import { ViewportPane } from './map/viewport/ViewportPane';
import { fromMap, relink, type Entity } from './map/model';

const log: string[] = [];
let failures = 0;

function check(name: string, ok: boolean, extra?: string): void {
  if (!ok) failures++;
  log.push(`${ok ? 'ok   ' : 'FAIL '}${name}${extra ? '  ' + extra : ''}`);
}

/** Lets React finish rendering what the last event caused. */
const settle = (ms = 0): Promise<void> =>
  new Promise(resolve => setTimeout(resolve, ms));

const entity = (id: number, targetname: string, connections: Entity['connections']): Entity =>
  fromMap({
    id, classname: 'logic_relay', targetname, model: '', brushModel: -1,
    origin: [0, 0, 0], angles: [0, 0, 0], keyvalues: [], connections,
  });

const wire = (target: string, input: string) =>
  ({ output: 'OnTrigger', target, input, param: '', delay: 0, times: -1 });

async function run(): Promise<void> {
  //-------------------------------------------------------------------------
  // The hidden attribute, which is a question about the cascade.
  //-------------------------------------------------------------------------

  for (const className of ['panel', 'map-pane']) {
    const probe = document.createElement('div');
    probe.className = className;
    probe.hidden = true;
    document.body.appendChild(probe);

    check(`a hidden .${className} is actually hidden`,
      getComputedStyle(probe).display === 'none', getComputedStyle(probe).display);

    probe.remove();
  }

  //-------------------------------------------------------------------------
  // Dragging a wire, which ends in elementFromPoint.
  //-------------------------------------------------------------------------

  const source = entity(0, 'source', [wire('sink', 'Kill')]);
  const sink = entity(1, 'sink', [wire('source', 'Trigger')]);
  const entities = [source, sink];

  relink(entities);

  let told = 0;
  let changed = 0;

  const root = createRoot(document.getElementById('graph-host')!);

  root.render(
    <GraphPane
      entities={entities}
      mode="static"
      selectedKey={null}
      components={[]}
      group=""
      onGroup={() => {}}
      onSelect={() => {}}
      onChanged={() => { changed++; }}
      onResult={() => { told++; }}
    />);

  // A turn of the loop rather than act(): act is a development-only export, and
  // this runs against the production bundle the game actually serves. React
  // renders concurrently, so the first paint needs more than a microtask.
  await settle(100);

  const out = document.querySelector<SVGGElement>('.port-out');
  const into = document.querySelector<SVGGElement>('.port-in');

  if (!out || !into) {
    check('the graph drew ports to drag between', false,
      `${document.querySelectorAll('.node').length} nodes, `
      + `${document.querySelectorAll('.graph-svg').length} svg`);
    return;
  }

  /* Measured from the dot, not the group: a <g> bounding box takes in the label
     and the title, and does not land on what anyone would aim at. */
  const at = (port: SVGGElement) => {
    const box = port.querySelector('.port-dot')!.getBoundingClientRect();
    return { clientX: box.left + box.width / 2, clientY: box.top + box.height / 2 };
  };

  const fire = async (target: EventTarget, type: string, point: { clientX: number; clientY: number }) => {
    target.dispatchEvent(new MouseEvent(type, { bubbles: true, cancelable: true, ...point }));
    await settle();
  };

  const from = at(out);
  const to = at(into);

  await fire(out, 'mousedown', from);
  check('a wire starts from an output port', document.querySelector('.edge.wire') !== null);

  await fire(document.querySelector('.graph')!, 'mousemove',
    { clientX: (from.clientX + to.clientX) / 2, clientY: to.clientY });
  check('the wire follows the pointer', document.querySelector('.edge.wire') !== null);

  const under = document.elementFromPoint(to.clientX, to.clientY);
  check('the input port is somewhere a pointer can reach',
    under !== null && under.closest('.port-in') !== null,
    under ? `${under.tagName}.${under.getAttribute('class') ?? ''}` : 'nothing there');

  await fire(into, 'mouseup', to);

  const editor = document.querySelector('.wire-editor');
  check('dropping on an input opens the editor', editor !== null);

  const input = editor?.querySelector('input');
  check('the editor knows which input was dropped on', !!input?.value, input?.value ?? '(none)');

  const before = source.connections.length;
  const connect = Array.from(editor?.querySelectorAll('button') ?? [])
    .find(button => button.textContent === 'Connect');

  connect?.click();
  await settle(100);

  check('the connection is added to the map', source.connections.length === before + 1);

  // In static mode the model IS the map, so this is an edit to be saved rather
  // than something the running game is told about.
  check('wiring in static mode edits the map rather than the game', told === 0, String(told));
  check('and marks the map dirty', changed > 0);

  // An aborted drag is the commonest outcome of picking a port up by accident.
  const count = source.connections.length;
  await fire(document.querySelector('.port-out')!, 'mousedown', from);
  await fire(document.querySelector('.graph')!, 'mouseup', { clientX: 5, clientY: 5 });

  check('a wire dropped on nothing changes nothing', source.connections.length === count);

  await viewports();
}

/*
 * Clicking an entity in a viewport.
 *
 * The interesting part is not the maths — that is in Vitest — but everything
 * around it: that the canvas has a size by the time it is drawn, that the click
 * is measured against the view it landed in, and that a click is told from a
 * drag. All three are the sort of thing that only breaks in a browser.
 */
async function viewports(): Promise<void> {
  const host = document.getElementById('viewport-host')!;

  if (!document.createElement('canvas').getContext('webgl')) {
    log.push('skip  the viewports need WebGL, which this browser did not give us');
    return;
  }

  const at = (id: number, origin: [number, number, number]): Entity => fromMap({
    id, classname: 'prop_physics', targetname: `prop${id}`, model: '', brushModel: -1,
    origin, angles: [0, 0, 0], keyvalues: [], connections: [],
  });

  /*
   * Three, symmetrically: with no geometry to read the views frame themselves
   * around the entities, so the middle one lands in the middle of the canvas
   * and the other two are far enough away that picking cannot reach them.
   *
   * Where exactly the corners fall is the projection's business, and that is
   * tested against the maths rather than guessed at here.
   */
  const entities = [at(0, [-1000, -1000, 0]), at(1, [1000, 1000, 0]), at(2, [0, 0, 0])];
  let picked: Entity | null = null;

  createRoot(host).render(
    <ViewportPane
      visible
      entities={entities}
      map="selftest"
      selectedKey={null}
      onSelect={entity => { picked = entity; }}
      loadGeometry={async () => null}
    />);

  await settle(200);

  const canvas = host.querySelector<HTMLCanvasElement>('canvas[data-view="top"]');
  check('the viewports drew four canvases',
    host.querySelectorAll('canvas').length === 4, String(host.querySelectorAll('canvas').length));

  if (!canvas) return;

  const box = canvas.getBoundingClientRect();
  check('a viewport canvas has a size to draw against', box.width > 0 && box.height > 0,
    `${Math.round(box.width)}x${Math.round(box.height)}`);

  const target = entities[2]!;
  const view = { x: box.left + box.width / 2, y: box.top + box.height / 2 };

  canvas.dispatchEvent(new MouseEvent('mousedown', {
    bubbles: true, cancelable: true, button: 0, clientX: view.x, clientY: view.y }));
  window.dispatchEvent(new MouseEvent('mouseup', {
    bubbles: true, button: 0, clientX: view.x, clientY: view.y }));

  await settle();
  check('clicking an entity in a viewport selects it',
    picked === target, picked ? (picked as Entity).targetname : 'nothing was picked');

  // Empty space is empty space: a viewport that snapped to the nearest entity
  // anywhere could never deselect.
  picked = null;

  canvas.dispatchEvent(new MouseEvent('mousedown', {
    bubbles: true, cancelable: true, button: 0, clientX: box.left + 4, clientY: box.top + 4 }));
  window.dispatchEvent(new MouseEvent('mouseup', {
    bubbles: true, button: 0, clientX: box.left + 4, clientY: box.top + 4 }));

  await settle();
  check('clicking empty space selects nothing', picked === null);

  // A drag is a camera move, not a selection: releasing after one would select
  // whatever happened to be under the pointer at the end of the spin.
  picked = null;

  canvas.dispatchEvent(new MouseEvent('mousedown', {
    bubbles: true, cancelable: true, button: 0, clientX: view.x, clientY: view.y }));
  window.dispatchEvent(new MouseEvent('mousemove', {
    bubbles: true, clientX: view.x + 60, clientY: view.y }));
  window.dispatchEvent(new MouseEvent('mouseup', {
    bubbles: true, button: 0, clientX: view.x + 60, clientY: view.y }));

  await settle();
  check('a drag moves the camera rather than selecting', picked === null);
}

run()
  .catch(error => {
    failures++;
    log.push(`THREW ${error instanceof Error ? error.stack : String(error)}`);
  })
  .finally(() => {
    log.push('', failures === 0
      ? `${log.filter(line => line.startsWith('ok')).length} passed, 0 failed`
      : `${failures} FAILED`);

    document.getElementById('out')!.textContent = log.join('\n');
  });

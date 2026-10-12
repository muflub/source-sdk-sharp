/*
 * The viewports, minus the GPU.
 *
 * Everything here is decided before a single WebGL call: where the axes point,
 * what a drag means, which entity a click is nearest, and what the map turns
 * into. A transposed axis is the failure worth guarding against — it looks
 * entirely plausible and puts every position in the map somewhere it is not.
 *
 * A View needs a canvas with a WebGL context, which jsdom does not have, so the
 * methods that only read the camera are called against a stand-in holding the
 * same fields. That is a real test of the maths and an honest one: the parts it
 * cannot reach are the ones that do nothing but hand buffers to the driver.
 */

import { describe, expect, it } from 'vitest';

import type { MapGeometry, Vec3 } from '../../../types';
import { fromMap } from '../../model';
import {
  AXES, View, boxEdges, buildEntities, buildWorld, lookAt, multiply, ortho, type ViewMode,
} from '../renderer';

/** Where a world point lands in clip space, which is all these views decide. */
function project(matrix: Float32Array, point: Vec3): [number, number] {
  const w = matrix[3]! * point[0] + matrix[7]! * point[1] + matrix[11]! * point[2] + matrix[15]!;

  return [
    (matrix[0]! * point[0] + matrix[4]! * point[1] + matrix[8]! * point[2] + matrix[12]!) / w,
    (matrix[1]! * point[0] + matrix[5]! * point[1] + matrix[9]! * point[2] + matrix[13]!) / w,
  ];
}

// Source is Z-up, so these are the three a level editor shows.
describe.each([
  ['top', 0, 1],
  ['front', 0, 2],
  ['side', 1, 2],
] as const)('the %s view', (mode, across, up) => {
  const axes = AXES[mode];
  const eye: Vec3 = [axes.eye[0] * 1000, axes.eye[1] * 1000, axes.eye[2] * 1000];

  const matrix = multiply(
    ortho(-500, 500, -500, 500, 1, 5000),
    lookAt(eye, [0, 0, 0], axes.upVector));

  const along = (axis: number): Vec3 => {
    const point: Vec3 = [0, 0, 0];
    point[axis] = 100;
    return point;
  };

  it(`puts axis ${across} across the screen`, () => {
    const [x, y] = project(matrix, along(across));

    expect(Math.abs(x)).toBeGreaterThan(0.1);
    expect(Math.abs(y)).toBeLessThan(0.001);
  });

  it(`puts axis ${up} up the screen`, () => {
    const [x, y] = project(matrix, along(up));

    expect(y).toBeGreaterThan(0.1);
    expect(Math.abs(x)).toBeLessThan(0.001);
  });
});

describe('a box', () => {
  it('is drawn as its twelve edges', () => {
    const points: number[] = [];
    const colours: number[] = [];

    boxEdges([-1, -1, -1], [1, 1, 1], points, [1, 1, 1], colours);

    // Twelve edges, two ends each, three components per end.
    expect(points).toHaveLength(12 * 2 * 3);
    expect(colours).toHaveLength(points.length);
  });
});

//---------------------------------------------------------------------------
// The camera, driven.
//---------------------------------------------------------------------------

const camera = (mode: ViewMode, size = { width: 400, height: 300 }): View => Object.assign(
  Object.create(View.prototype) as object,
  {
    mode,
    canvas: { clientWidth: size.width, clientHeight: size.height },
    mins: [-1000, -1000, -1000],
    maxs: [1000, 1000, 1000],
    centre: [0, 0, 0],
    radius: 1000,
    matrix: null,
    orbit: { yaw: 0.7, pitch: 0.6, distance: 1000, target: [0, 0, 0] },
    pan: { x: 0, y: 0, zoom: 1 },
  }) as unknown as View;

describe('dragging', () => {
  it('orbits the perspective view', () => {
    const view = camera('persp');
    const drag = view.beginDrag({ clientX: 0, clientY: 0, button: 0 });

    view.applyDrag(drag, 100, 0);

    expect(view.orbit.yaw).toBeCloseTo(0.7 - 100 * 0.008);
    expect(drag.moved).toBe(true);
  });

  it('will not orbit past vertical', () => {
    const view = camera('persp');
    const drag = view.beginDrag({ clientX: 0, clientY: 0, button: 0 });

    // Far enough to go over the top, where the up vector and the view direction
    // become parallel and the camera basis collapses.
    view.applyDrag(drag, 0, 10_000);

    expect(view.orbit.pitch).toBeLessThanOrEqual(1.5);
  });

  it('treats a click as a click, not a drag', () => {
    const view = camera('top');
    const drag = view.beginDrag({ clientX: 0, clientY: 0, button: 0 });

    // Two pixels: a careful click, not a movement. Told apart by distance rather
    // than timing, so that a slow one still selects.
    view.applyDrag(drag, 1, 1);

    expect(drag.moved).toBe(false);
  });

  it('pans an orthographic view in world units', () => {
    const view = camera('top');
    const drag = view.beginDrag({ clientX: 0, clientY: 0, button: 2 });

    // The view covers 2200 units across its height, so a drag of half the
    // canvas moves the map by a quarter of that.
    const perPixel = (view.orthoExtent(400 / 300) * 2) / 300;

    view.applyDrag(drag, 0, -150);

    expect(view.pan.y).toBeCloseTo(150 * perPixel);
  });

  it('measures an orthographic view across the axes it shows', () => {
    const view = camera('front');

    // A map that is wide and flat, as they are. Framing the front view against
    // the largest of the three dimensions would show a thin band of geometry in
    // the middle of an empty frame.
    Object.assign(view, { mins: [-4000, -4000, -200], maxs: [4000, 4000, 200] });

    expect(view.orthoExtent(400 / 300)).toBeLessThan(4000);
  });

  it('zooms towards and away', () => {
    const view = camera('top');

    view.zoomBy(-1);
    expect(view.pan.zoom).toBeGreaterThan(1);

    view.zoomBy(1);
    expect(view.pan.zoom).toBeCloseTo(1);
  });
});

describe('picking', () => {
  const entity = (id: number, origin: Vec3) => fromMap({
    id, classname: 'logic_relay', targetname: `relay${id}`, model: '', brushModel: -1,
    origin, angles: [0, 0, 0], keyvalues: [], connections: [],
  });

  const view = camera('top');
  view.matrix = multiply(ortho(-200, 200, -150, 150, 1, 5000),
    lookAt([0, 0, 1000], [0, 0, 0], [0, 1, 0]));

  const near = entity(0, [0, 0, 0]);
  const far = entity(1, [180, 140, 0]);

  it('selects the entity under the pointer', () => {
    // The middle of a 400x300 canvas, which is where the origin projects.
    expect(view.pick(200, 150, [near, far])).toBe(near);
  });

  it('selects nothing when nothing is near enough', () => {
    // A click in empty space is a click in empty space; a viewport that snapped
    // to the nearest entity anywhere could never deselect.
    expect(view.pick(100, 30, [near, far])).toBeNull();
  });
});

//---------------------------------------------------------------------------
// The map, turned into something to draw.
//---------------------------------------------------------------------------

describe('the world wireframe', () => {
  it('draws each face as a closed loop of edges', () => {
    const geometry = {
      map: 'test', mins: [0, 0, 0], maxs: [1, 1, 0],
      vertices: [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0],
      faceStarts: [0], faceLengths: [4], faceIndices: [0, 1, 2, 3],
      models: [],
    } as unknown as MapGeometry;

    const world = buildWorld(geometry);

    // Four edges, including the one back to the start: a face drawn as an open
    // path leaves a gap in every surface in the map.
    expect(world.positions).toHaveLength(4 * 2 * 3);
    expect(world.colours).toHaveLength(world.positions.length);

    const last = world.positions.slice(-6);
    expect([...last]).toEqual([0, 1, 0, 0, 0, 0]);
  });
});

describe('the entity markers', () => {
  const point = fromMap({
    id: 0, classname: 'logic_relay', targetname: 'relay', model: '', brushModel: -1,
    origin: [10, 20, 30], angles: [0, 0, 0], keyvalues: [], connections: [],
  });

  const brush = fromMap({
    id: 1, classname: 'trigger_multiple', targetname: 'trap', model: '*1', brushModel: 1,
    origin: [100, 0, 0], angles: [0, 0, 0], keyvalues: [], connections: [],
  });

  const geometry = {
    map: 'test', mins: [0, 0, 0], maxs: [1, 1, 1], vertices: [],
    faceStarts: [], faceLengths: [], faceIndices: [],
    models: [
      { mins: [0, 0, 0], maxs: [0, 0, 0], origin: [0, 0, 0] },
      { mins: [-32, -32, 0], maxs: [32, 32, 64], origin: [0, 0, 0] },
    ],
  } as unknown as MapGeometry;

  it('puts a point at every entity', () => {
    const built = buildEntities([point, brush], geometry, null, 1000);

    expect([...built.entities.positions.slice(0, 3)]).toEqual([10, 20, 30]);
    expect(built.entities.positions).toHaveLength(2 * 3);
  });

  it('draws a brush entity as its volume, moved to where it is', () => {
    const built = buildEntities([point, brush], geometry, null, 1000);

    // Only the brush entity: a trigger drawn as a dot loses the only thing
    // anyone wants to know about it, which is where its edges are.
    expect(built.brushes.positions).toHaveLength(12 * 2 * 3);
    expect(Math.min(...built.brushes.positions)).toBe(-32);
    expect(Math.max(...built.brushes.positions)).toBe(132);
  });

  it('boxes the selection, and only when there is one', () => {
    expect(buildEntities([point], geometry, null, 1000).selection.positions).toHaveLength(0);
    expect(buildEntities([point], geometry, 'static:0', 1000).selection.positions)
      .toHaveLength(12 * 2 * 3);
  });

  it('sizes the selection box against the map', () => {
    const small = buildEntities([point], geometry, 'static:0', 100).selection.positions;
    const large = buildEntities([point], geometry, 'static:0', 100_000).selection.positions;

    // Fixed would be either invisible in a large map or bigger than a small one.
    expect(Math.max(...large)).toBeGreaterThan(Math.max(...small));
  });
});

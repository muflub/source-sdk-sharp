/*
 * The map, drawn.
 *
 * WebGL, written out by hand. A library would be less code, but this page is
 * served by the game over a loopback socket with no network behind it: anything
 * fetched from elsewhere simply would not load, and vendoring a renderer to draw
 * six thousand lines and six hundred points is a poor trade.
 *
 * Imperative on purpose, and kept out of React entirely. A canvas is a mutable
 * thing with buffers on a GPU behind it; describing that declaratively would
 * mean uploading geometry on every render. The component owns one of these and
 * tells it when something changed.
 *
 * The world is drawn as wireframe rather than shaded: solid surfaces hide the
 * entities inside them, which are the only reason this view exists.
 */

import type { MapGeometry, Vec3 } from '../../types';
import { KIND_COLOURS, type Entity } from '../model';

export type ViewMode = 'persp' | 'top' | 'front' | 'side';

/**
 * Where the axes go on screen, per view.
 *
 * Source is Z-up, so the editor-standard views are top (x right, y up), front
 * (x right, z up) and side (y right, z up). Getting one of these transposed
 * makes every position in the map look subtly wrong and is very hard to spot.
 */
export const AXES: Record<Exclude<ViewMode, 'persp'>, {
  right: number; up: number; eye: Vec3; upVector: Vec3;
}> = {
  top: { right: 0, up: 1, eye: [0, 0, 1], upVector: [0, 1, 0] },
  front: { right: 0, up: 2, eye: [0, -1, 0], upVector: [0, 0, 1] },
  side: { right: 1, up: 2, eye: [1, 0, 0], upVector: [0, 0, 1] },
};

//---------------------------------------------------------------------------
// Just enough matrix maths.
//---------------------------------------------------------------------------

export const identity = (): Float32Array =>
  new Float32Array([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);

export function multiply(a: Float32Array, b: Float32Array): Float32Array {
  const out = new Float32Array(16);

  for (let row = 0; row < 4; row++) {
    for (let col = 0; col < 4; col++) {
      out[col * 4 + row] =
        a[0 * 4 + row]! * b[col * 4 + 0]! +
        a[1 * 4 + row]! * b[col * 4 + 1]! +
        a[2 * 4 + row]! * b[col * 4 + 2]! +
        a[3 * 4 + row]! * b[col * 4 + 3]!;
    }
  }

  return out;
}

export function perspective(fovY: number, aspect: number, near: number, far: number): Float32Array {
  const f = 1 / Math.tan(fovY / 2);
  const out = new Float32Array(16);

  out[0] = f / aspect;
  out[5] = f;
  out[10] = (far + near) / (near - far);
  out[11] = -1;
  out[14] = (2 * far * near) / (near - far);

  return out;
}

export function ortho(
  left: number, right: number, bottom: number, top: number, near: number, far: number,
): Float32Array {
  const out = identity();

  out[0] = 2 / (right - left);
  out[5] = 2 / (top - bottom);
  out[10] = -2 / (far - near);
  out[12] = -(right + left) / (right - left);
  out[13] = -(top + bottom) / (top - bottom);
  out[14] = -(far + near) / (far - near);

  return out;
}

export function lookAt(eye: Vec3, target: Vec3, up: Vec3): Float32Array {
  const z = normalise([eye[0] - target[0], eye[1] - target[1], eye[2] - target[2]]);
  const x = normalise(cross(up, z));
  const y = cross(z, x);

  return new Float32Array([
    x[0], y[0], z[0], 0,
    x[1], y[1], z[1], 0,
    x[2], y[2], z[2], 0,
    -dot(x, eye), -dot(y, eye), -dot(z, eye), 1,
  ]);
}

const cross = (a: Vec3, b: Vec3): Vec3 =>
  [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

const dot = (a: Vec3, b: Vec3): number => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

function normalise(v: Vec3): Vec3 {
  const length = Math.hypot(v[0], v[1], v[2]) || 1;
  return [v[0] / length, v[1] / length, v[2] / length];
}

/** The twelve edges of a box, as line segments. */
export function boxEdges(
  mins: Vec3, maxs: Vec3, into: number[], colour: Vec3, colours: number[],
): void {
  const corners: Vec3[] = [];

  for (let i = 0; i < 8; i++) {
    corners.push([
      (i & 1) ? maxs[0] : mins[0],
      (i & 2) ? maxs[1] : mins[1],
      (i & 4) ? maxs[2] : mins[2],
    ]);
  }

  const pairs = [[0, 1], [2, 3], [4, 5], [6, 7], [0, 2], [1, 3], [4, 6], [5, 7],
    [0, 4], [1, 5], [2, 6], [3, 7]];

  for (const [a, b] of pairs) {
    into.push(...corners[a!]!, ...corners[b!]!);
    colours.push(...colour, ...colour);
  }
}

//---------------------------------------------------------------------------
// Shaders.
//---------------------------------------------------------------------------

const VERTEX_SHADER = `
attribute vec3 aPosition;
attribute vec3 aColour;
uniform mat4 uViewProjection;
uniform float uPointSize;
varying vec3 vColour;

void main() {
  gl_Position = uViewProjection * vec4(aPosition, 1.0);
  gl_PointSize = uPointSize;
  vColour = aColour;
}`;

const FRAGMENT_SHADER = `
precision mediump float;
uniform float uAlpha;
uniform float uRound;
varying vec3 vColour;

void main() {
  // Points are drawn as discs rather than squares when asked: a square marker
  // at this size is indistinguishable from a fragment of wireframe.
  if (uRound > 0.5) {
    vec2 offset = gl_PointCoord - vec2(0.5);
    if (dot(offset, offset) > 0.25) discard;
  }

  gl_FragColor = vec4(vColour, uAlpha);
}`;

const hex = (value: string): Vec3 => [
  parseInt(value.slice(1, 3), 16) / 255,
  parseInt(value.slice(3, 5), 16) / 255,
  parseInt(value.slice(5, 7), 16) / 255,
];

interface Buffer { position: WebGLBuffer; colour: WebGLBuffer; count: number }

/** One canvas, one camera, and the buffers behind them. */
export class View {
  private readonly gl: WebGLRenderingContext;
  private readonly program: WebGLProgram;
  private readonly attributes: { position: number; colour: number };
  private readonly uniforms: Record<'viewProjection' | 'pointSize' | 'alpha' | 'round', WebGLUniformLocation | null>;
  private readonly buffers: Record<string, Buffer> = {};

  private mins: Vec3 = [-512, -512, -512];
  private maxs: Vec3 = [512, 512, 512];
  private centre: Vec3 = [0, 0, 0];

  /** Half the largest dimension. Sets the camera distance and the far plane. */
  radius = 1024;

  matrix: Float32Array | null = null;

  // Perspective starts looking slightly down from behind, which is the angle a
  // map is usually drawn at and needs no adjusting to make sense of.
  orbit = { yaw: 0.7, pitch: 0.6, distance: 4000, target: [0, 0, 0] as Vec3 };
  pan = { x: 0, y: 0, zoom: 1 };

  constructor(readonly canvas: HTMLCanvasElement, readonly mode: ViewMode) {
    const gl = canvas.getContext('webgl', { antialias: true, alpha: false });
    if (!gl) throw new Error('WebGL is unavailable');

    this.gl = gl;

    const program = gl.createProgram()!;
    gl.attachShader(program, compile(gl, gl.VERTEX_SHADER, VERTEX_SHADER));
    gl.attachShader(program, compile(gl, gl.FRAGMENT_SHADER, FRAGMENT_SHADER));
    gl.linkProgram(program);

    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
      throw new Error(`link: ${gl.getProgramInfoLog(program) ?? ''}`);
    }

    this.program = program;
    this.attributes = {
      position: gl.getAttribLocation(program, 'aPosition'),
      colour: gl.getAttribLocation(program, 'aColour'),
    };
    this.uniforms = {
      viewProjection: gl.getUniformLocation(program, 'uViewProjection'),
      pointSize: gl.getUniformLocation(program, 'uPointSize'),
      alpha: gl.getUniformLocation(program, 'uAlpha'),
      round: gl.getUniformLocation(program, 'uRound'),
    };
  }

  frame(mins: Vec3, maxs: Vec3): void {
    this.mins = mins;
    this.maxs = maxs;
    this.centre = [(mins[0] + maxs[0]) / 2, (mins[1] + maxs[1]) / 2, (mins[2] + maxs[2]) / 2];
    this.radius = Math.max(maxs[0] - mins[0], maxs[1] - mins[1], maxs[2] - mins[2], 64) / 2;

    this.orbit.target = [...this.centre];

    // Close enough that the map fills the frame. Three radii away sounds safe
    // and puts a Source map in the middle distance, where nothing can be read.
    this.orbit.distance = this.radius * 2;
    this.pan = { x: 0, y: 0, zoom: 1 };
  }

  /**
   * Half the height an orthographic view must cover to hold the map.
   *
   * Measured across the two axes this view shows, not all three. A Source map is
   * wide and flat, so framing every view against the largest dimension leaves
   * the front and side views showing a thin band in the middle of an empty
   * frame.
   */
  orthoExtent(aspect: number): number {
    if (this.mode === 'persp') return this.radius;

    const axes = AXES[this.mode];
    const halfAcross = Math.max(this.maxs[axes.right]! - this.mins[axes.right]!, 64) / 2;
    const halfUp = Math.max(this.maxs[axes.up]! - this.mins[axes.up]!, 64) / 2;

    return Math.max(halfUp, halfAcross / aspect) * 1.1;
  }

  upload(name: string, positions: Float32Array, colours: Float32Array): void {
    const gl = this.gl;
    const buffer = this.buffers[name] ??= {
      position: gl.createBuffer()!, colour: gl.createBuffer()!, count: 0,
    };

    gl.bindBuffer(gl.ARRAY_BUFFER, buffer.position);
    gl.bufferData(gl.ARRAY_BUFFER, positions, gl.STATIC_DRAW);

    gl.bindBuffer(gl.ARRAY_BUFFER, buffer.colour);
    gl.bufferData(gl.ARRAY_BUFFER, colours, gl.STATIC_DRAW);

    buffer.count = positions.length / 3;
  }

  viewProjection(): Float32Array {
    const aspect = (this.canvas.width || 1) / (this.canvas.height || 1);

    if (this.mode === 'persp') {
      const { yaw, pitch, distance, target } = this.orbit;
      const eye: Vec3 = [
        target[0] + distance * Math.cos(pitch) * Math.cos(yaw),
        target[1] + distance * Math.cos(pitch) * Math.sin(yaw),
        target[2] + distance * Math.sin(pitch),
      ];

      // Far plane generous: a Source map is thousands of units across, and
      // clipping the far half of it looks like missing geometry.
      return multiply(perspective(1.1, aspect, 8, this.radius * 20), lookAt(eye, target, [0, 0, 1]));
    }

    const axes = AXES[this.mode];
    const extent = this.orthoExtent(aspect) / this.pan.zoom;

    const eye: number[] = [
      this.centre[0] + axes.eye[0] * this.radius * 8,
      this.centre[1] + axes.eye[1] * this.radius * 8,
      this.centre[2] + axes.eye[2] * this.radius * 8,
    ];

    const target: number[] = [...this.centre];

    // Panning moves the camera along the two axes that are on screen, which is
    // the only way a pan in an orthographic view can mean anything.
    eye[axes.right]! -= this.pan.x;
    target[axes.right]! -= this.pan.x;
    eye[axes.up]! -= this.pan.y;
    target[axes.up]! -= this.pan.y;

    return multiply(
      ortho(-extent * aspect, extent * aspect, -extent, extent, 1, this.radius * 20),
      lookAt(eye as Vec3, target as Vec3, axes.upVector));
  }

  draw(): void {
    const gl = this.gl;
    const width = this.canvas.clientWidth;
    const height = this.canvas.clientHeight;

    if (width === 0 || height === 0) return;

    const ratio = Math.min(window.devicePixelRatio || 1, 2);

    if (this.canvas.width !== width * ratio || this.canvas.height !== height * ratio) {
      this.canvas.width = width * ratio;
      this.canvas.height = height * ratio;
    }

    gl.viewport(0, 0, this.canvas.width, this.canvas.height);
    gl.clearColor(0.06, 0.07, 0.09, 1);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    gl.enable(gl.DEPTH_TEST);
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);

    gl.useProgram(this.program);

    this.matrix = this.viewProjection();
    gl.uniformMatrix4fv(this.uniforms.viewProjection, false, this.matrix);

    this.drawBuffer('world', gl.LINES, 1, 0.25, 0);
    this.drawBuffer('brushes', gl.LINES, 1, 0.7, 0);
    this.drawBuffer('entities', gl.POINTS, this.mode === 'persp' ? 7 : 6, 1, 1);
    this.drawBuffer('selection', gl.LINES, 1, 1, 0);
  }

  private drawBuffer(name: string, primitive: number, pointSize: number, alpha: number, round: number): void {
    const buffer = this.buffers[name];
    if (!buffer || buffer.count === 0) return;

    const gl = this.gl;

    gl.bindBuffer(gl.ARRAY_BUFFER, buffer.position);
    gl.enableVertexAttribArray(this.attributes.position);
    gl.vertexAttribPointer(this.attributes.position, 3, gl.FLOAT, false, 0, 0);

    gl.bindBuffer(gl.ARRAY_BUFFER, buffer.colour);
    gl.enableVertexAttribArray(this.attributes.colour);
    gl.vertexAttribPointer(this.attributes.colour, 3, gl.FLOAT, false, 0, 0);

    gl.uniform1f(this.uniforms.pointSize, pointSize);
    gl.uniform1f(this.uniforms.alpha, alpha);
    gl.uniform1f(this.uniforms.round, round);

    gl.drawArrays(primitive, 0, buffer.count);
  }

  /** Screen position of a world point, or null if it is behind the camera. */
  project(point: Vec3): [number, number] | null {
    const m = this.matrix;
    if (!m) return null;

    const w = m[3]! * point[0] + m[7]! * point[1] + m[11]! * point[2] + m[15]!;
    if (w <= 0) return null;

    const x = (m[0]! * point[0] + m[4]! * point[1] + m[8]! * point[2] + m[12]!) / w;
    const y = (m[1]! * point[0] + m[5]! * point[1] + m[9]! * point[2] + m[13]!) / w;

    return [
      (x * 0.5 + 0.5) * this.canvas.clientWidth,
      (0.5 - y * 0.5) * this.canvas.clientHeight,
    ];
  }

  //-------------------------------------------------------------------------
  // Moving the camera.
  //
  // The events are the component's; what a drag means is the view's, because
  // it depends on which projection is in front of you.
  //-------------------------------------------------------------------------

  beginDrag(event: { clientX: number; clientY: number; button: number }): Drag {
    return {
      x: event.clientX, y: event.clientY, button: event.button,
      orbit: { ...this.orbit, target: [...this.orbit.target] },
      pan: { ...this.pan },
      moved: false,
    };
  }

  applyDrag(drag: Drag, dx: number, dy: number): void {
    // Told apart by distance rather than by timing, so that a slow, careful
    // click still counts as a click and selects what is under it.
    if (Math.abs(dx) + Math.abs(dy) > 3) drag.moved = true;

    if (this.mode === 'persp' && drag.button === 0) {
      this.orbit.yaw = drag.orbit.yaw - dx * 0.008;

      // Clamped just short of straight up: at exactly vertical the up vector and
      // the view direction are parallel and the camera basis collapses.
      this.orbit.pitch = Math.max(-1.5, Math.min(1.5, drag.orbit.pitch + dy * 0.008));
      return;
    }

    const width = this.canvas.clientWidth || 1;
    const height = this.canvas.clientHeight || 1;

    if (this.mode === 'persp') {
      // Panning a perspective view moves what it is looking at, along the
      // camera's own right and up — not along world axes, which would send the
      // view sideways the moment it was rotated.
      const scale = this.orbit.distance / 600;
      const right = [-Math.sin(this.orbit.yaw), Math.cos(this.orbit.yaw), 0];

      this.orbit.target = [
        drag.orbit.target[0] - right[0]! * dx * scale,
        drag.orbit.target[1] - right[1]! * dx * scale,
        drag.orbit.target[2] + dy * scale,
      ];

      return;
    }

    // In an orthographic view a pan is measured in world units per pixel, which
    // is exactly what the extent divided by the height is.
    const scale = (this.orthoExtent(width / height) * 2 / this.pan.zoom) / height;

    this.pan.x = drag.pan.x + dx * scale;
    this.pan.y = drag.pan.y - dy * scale;
  }

  zoomBy(deltaY: number): void {
    const factor = deltaY < 0 ? 1 / 1.15 : 1.15;

    if (this.mode === 'persp') {
      this.orbit.distance = Math.max(32, Math.min(this.radius * 20, this.orbit.distance * factor));
    } else {
      this.pan.zoom = Math.max(0.05, Math.min(40, this.pan.zoom / factor));
    }
  }

  /** The entity nearest a click, if one is near enough to have been meant. */
  pick(px: number, py: number, entities: Entity[]): Entity | null {
    let best: Entity | null = null;
    let bestDistance = 14 * 14;

    for (const entity of entities) {
      const at = this.project(entity.origin);
      if (!at) continue;

      const distance = (at[0] - px) ** 2 + (at[1] - py) ** 2;

      if (distance < bestDistance) {
        bestDistance = distance;
        best = entity;
      }
    }

    return best;
  }
}

/** A drag in progress: where it started and what the camera was then. */
export interface Drag {
  x: number;
  y: number;
  button: number;
  orbit: View['orbit'];
  pan: View['pan'];
  moved: boolean;
}

function compile(gl: WebGLRenderingContext, type: number, source: string): WebGLShader {
  const shader = gl.createShader(type)!;

  gl.shaderSource(shader, source);
  gl.compileShader(shader);

  if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
    throw new Error(`shader: ${gl.getShaderInfoLog(shader) ?? ''}`);
  }

  return shader;
}

//---------------------------------------------------------------------------
// Turning the map into something to draw.
//---------------------------------------------------------------------------

/** World wireframe: every face outline, as line segments. */
export function buildWorld(geometry: MapGeometry): { positions: Float32Array; colours: Float32Array } {
  const segments: number[] = [];

  for (let face = 0; face < geometry.faceStarts.length; face++) {
    const start = geometry.faceStarts[face]!;
    const length = geometry.faceLengths[face]!;

    for (let i = 0; i < length; i++) {
      const a = geometry.faceIndices[start + i]!;
      const b = geometry.faceIndices[start + ((i + 1) % length)]!;

      segments.push(
        geometry.vertices[a * 3]!, geometry.vertices[a * 3 + 1]!, geometry.vertices[a * 3 + 2]!,
        geometry.vertices[b * 3]!, geometry.vertices[b * 3 + 1]!, geometry.vertices[b * 3 + 2]!);
    }
  }

  const positions = new Float32Array(segments);
  const colours = new Float32Array(positions.length);

  for (let i = 0; i < colours.length; i += 3) {
    colours[i] = 0.42;
    colours[i + 1] = 0.47;
    colours[i + 2] = 0.55;
  }

  return { positions, colours };
}

/** Entity markers, brush entity boxes, and a box around the selection. */
export function buildEntities(entities: Entity[], geometry: MapGeometry | null, selectedKey: string | null, radius: number) {
  const positions = new Float32Array(entities.length * 3);
  const colours = new Float32Array(entities.length * 3);

  entities.forEach((entity, i) => {
    positions[i * 3] = entity.origin[0];
    positions[i * 3 + 1] = entity.origin[1];
    positions[i * 3 + 2] = entity.origin[2];

    const colour = hex(KIND_COLOURS[entity.kind]);
    colours[i * 3] = colour[0];
    colours[i * 3 + 1] = colour[1];
    colours[i * 3 + 2] = colour[2];
  });

  // Brush entities are volumes, not points. A trigger drawn as a dot loses the
  // only thing anyone wants to know about it, which is how big it is and where
  // its edges are.
  const brushPositions: number[] = [];
  const brushColours: number[] = [];
  const models = geometry?.models ?? [];

  for (const entity of entities) {
    const index = entity.brushModel;
    if (index < 1 || index >= models.length) continue;

    const model = models[index]!;
    const colour = hex(KIND_COLOURS[entity.kind]);

    boxEdges(
      [model.mins[0] + entity.origin[0], model.mins[1] + entity.origin[1], model.mins[2] + entity.origin[2]],
      [model.maxs[0] + entity.origin[0], model.maxs[1] + entity.origin[1], model.maxs[2] + entity.origin[2]],
      brushPositions, colour, brushColours);
  }

  const selectionPositions: number[] = [];
  const selectionColours: number[] = [];
  const selected = entities.find(entity =>
    (entity.handle !== undefined ? `live:${entity.handle}` : `static:${entity.id}`) === selectedKey);

  if (selected) {
    // Sized against the map rather than fixed, so the marker is visible in a
    // large map and does not swallow a small one.
    const size = Math.max(16, radius / 60);
    const [x, y, z] = selected.origin;

    boxEdges([x - size, y - size, z - size], [x + size, y + size, z + size],
      selectionPositions, [1, 1, 1], selectionColours);
  }

  return {
    entities: { positions, colours },
    brushes: { positions: new Float32Array(brushPositions), colours: new Float32Array(brushColours) },
    selection: { positions: new Float32Array(selectionPositions), colours: new Float32Array(selectionColours) },
  };
}

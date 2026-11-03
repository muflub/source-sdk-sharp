/*
 * The AI's navigation, as map-tab layers (plan_hl2mp_ai §7.1).
 *
 * /ai/nav is the level's derived graph: each cell as seven numbers (minX, minY,
 * maxX, maxY, floorZ, classes, flags) and each link that is not a walk as three
 * (from, to, type). /ai/flow is one flow field: each cell's next cell. Both are
 * turned here into what the viewport draws: a point per cell coloured by who
 * may stand there, a line per link coloured by how it is crossed, and an arrow
 * per cell toward its next.
 */

/** /ai/nav's answer. */
export interface AiNav {
  map: string;
  cells: number;
  edges: number;
  cell: number[];
  link: number[];
  obstacles: { name: string; kind: string; blocking: boolean }[];
  /** The places most routes must pass, busiest first (plan_hl2mp_ai §5.5); empty until worked out. */
  chokes?: { x: number; y: number; z: number; score: number }[];
  error?: string;
}

/** /ai/flow's answer. */
export interface AiFlow {
  objective: number;
  class: 'stand' | 'crouch';
  reachable: number;
  next: number[];
  cost: number[];
  error?: string;
}

/** The classes and flags bits, as the server writes them. */
export const STAND = 1;
export const CROUCH = 2;
export const WATER = 1;
export const LADDER = 2;

/** How many points draw one choke point's column. */
export const CHOKE_POINTS = 24;

/** Link types, in NavEdgeType's order. */
export const LINK_TYPES = ['walk', 'jump', 'crouchjump', 'drop', 'ladder', 'door'] as const;

/** Colours, as 0..1 RGB: a cell by who stands there, a link by how it is crossed. */
export const NAV_COLOURS = {
  stand: [0.30, 0.78, 0.42],
  crouch: [0.95, 0.78, 0.25],
  water: [0.30, 0.55, 0.95],
  ladder: [0.30, 0.90, 0.90],
  choke: [1.0, 0.2, 0.8],
  walk: [0.5, 0.5, 0.5],
  jump: [0.95, 0.55, 0.20],
  crouchjump: [0.95, 0.30, 0.20],
  drop: [0.65, 0.40, 0.90],
  door: [0.95, 0.95, 0.95],
  flow: [0.95, 0.35, 0.65],
} as const;

/** A cell's centre on its floor, raised a little so it sits above the floor's wireframe. */
export function cellCentre(nav: AiNav, cell: number): [number, number, number] {
  const c = nav.cell;
  const at = cell * 7;
  return [(c[at]! + c[at + 2]!) / 2, (c[at + 1]! + c[at + 3]!) / 2, c[at + 4]! + 2];
}

export function cellColour(nav: AiNav, cell: number): readonly number[] {
  const classes = nav.cell[cell * 7 + 5]!;
  const flags = nav.cell[cell * 7 + 6]!;
  if (flags & LADDER) return NAV_COLOURS.ladder;
  if (flags & WATER) return NAV_COLOURS.water;
  return classes & STAND ? NAV_COLOURS.stand : NAV_COLOURS.crouch;
}

export interface Layer { positions: Float32Array; colours: Float32Array }

/** A point per cell, and a line per non-walk link. */
export function buildNavLayers(nav: AiNav): { cells: Layer; links: Layer } {
  const n = Math.floor(nav.cell.length / 7);
  const chokes = nav.chokes ?? [];
  const extra = chokes.length * CHOKE_POINTS;
  const cells = { positions: new Float32Array((n + extra) * 3), colours: new Float32Array((n + extra) * 3) };
  for (let i = 0; i < n; i++) {
    cells.positions.set(cellCentre(nav, i), i * 3);
    cells.colours.set(cellColour(nav, i), i * 3);
  }

  // A choke point is a column of bright points standing on its cell, taller as it is busier.
  chokes.forEach((c, k) => {
    for (let p = 0; p < CHOKE_POINTS; p++) {
      const at = (n + k * CHOKE_POINTS + p) * 3;
      cells.positions.set([c.x, c.y, c.z + (p / CHOKE_POINTS) * 32 * (1 + 3 * c.score)], at);
      cells.colours.set(NAV_COLOURS.choke, at);
    }
  });

  const m = Math.floor(nav.link.length / 3);
  const links = { positions: new Float32Array(m * 6), colours: new Float32Array(m * 6) };
  for (let i = 0; i < m; i++) {
    const from = nav.link[i * 3]!;
    const to = nav.link[i * 3 + 1]!;
    const type = LINK_TYPES[nav.link[i * 3 + 2]!] ?? 'walk';
    links.positions.set(cellCentre(nav, from), i * 6);
    links.positions.set(cellCentre(nav, to), i * 6 + 3);
    links.colours.set(NAV_COLOURS[type], i * 6);
    links.colours.set(NAV_COLOURS[type], i * 6 + 3);
  }

  return { cells, links };
}

/**
 * The flow field as arrows: from each cell that has a next toward it, going
 * two thirds of the way so a run of arrows reads as a direction, not a line.
 */
export function buildFlowLayer(nav: AiNav, flow: AiFlow): Layer {
  const starts: number[] = [];
  for (let cell = 0; cell < flow.next.length; cell++) if (flow.next[cell]! >= 0) starts.push(cell);

  const layer = { positions: new Float32Array(starts.length * 6), colours: new Float32Array(starts.length * 6) };
  starts.forEach((cell, i) => {
    const a = cellCentre(nav, cell);
    const b = cellCentre(nav, flow.next[cell]!);
    const tip: [number, number, number] = [a[0] + (b[0] - a[0]) * 0.66, a[1] + (b[1] - a[1]) * 0.66, a[2] + (b[2] - a[2]) * 0.66];
    layer.positions.set(a, i * 6);
    layer.positions.set(tip, i * 6 + 3);
    layer.colours.set(NAV_COLOURS.flow, i * 6);
    layer.colours.set(NAV_COLOURS.flow, i * 6 + 3);
  });

  return layer;
}

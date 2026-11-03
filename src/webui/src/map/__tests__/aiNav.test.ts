/*
 * The AI's nav layers (plan_hl2mp_ai §7.1), from a small sample of /ai/nav and
 * /ai/flow: a point per cell at its centre and colour, a line per link in its
 * type's colour, an arrow per cell with a next and none for the objective.
 */

import { describe, expect, it } from 'vitest';

import { CHOKE_POINTS,
  NAV_COLOURS, buildFlowLayer, buildNavLayers, cellCentre, type AiFlow, type AiNav,
} from '../aiNav';

// Three cells in a row, 16 wide: a standing one, a crouch-only one, a ladder.
const nav: AiNav = {
  map: 'sample',
  cells: 3,
  edges: 4,
  cell: [
    0, 0, 16, 16, 0, 3, 0,
    16, 0, 32, 16, 0, 2, 0,
    32, 0, 48, 16, 30, 3, 2,
  ],
  link: [1, 2, 1, 2, 1, 3],
  obstacles: [],
};

const flow: AiFlow = { objective: 2, class: 'crouch', reachable: 3, next: [1, 2, -1], cost: [32, 16, 0] };

describe('the nav layers', () => {
  const { cells, links } = buildNavLayers(nav);

  it('puts a point at each cell centre, on its floor', () => {
    expect(Array.from(cells.positions)).toEqual([8, 8, 2, 24, 8, 2, 40, 8, 32]);
  });

  it('stands a column of choke-coloured points on each choke point, after the cells', () => {
    const before = buildNavLayers(nav).cells.positions.length / 3;
    const { cells } = buildNavLayers({ ...nav, chokes: [{ x: 5, y: 6, z: 7, score: 1 }] });
    expect(cells.positions.length / 3).toBe(before + CHOKE_POINTS);
    expect([...cells.positions.slice(before * 3, before * 3 + 3)]).toEqual([5, 6, 7]);
    const colour = [...cells.colours.slice(before * 3, before * 3 + 3)];
    NAV_COLOURS.choke.forEach((v, i) => expect(colour[i]).toBeCloseTo(v));
  });

  it('colours a cell by who may stand there', () => {
    expect(Array.from(cells.colours.slice(0, 3))).toEqual(NAV_COLOURS.stand.map(Math.fround));
    expect(Array.from(cells.colours.slice(3, 6))).toEqual(NAV_COLOURS.crouch.map(Math.fround));
    expect(Array.from(cells.colours.slice(6, 9))).toEqual(NAV_COLOURS.ladder.map(Math.fround));
  });

  it('draws each link between its cells in its type colour', () => {
    expect(links.positions.length).toBe(12);
    expect(Array.from(links.positions.slice(0, 6))).toEqual([...cellCentre(nav, 1), ...cellCentre(nav, 2)]);
    expect(Array.from(links.colours.slice(0, 3))).toEqual(NAV_COLOURS.jump.map(Math.fround));
    expect(Array.from(links.colours.slice(6, 9))).toEqual(NAV_COLOURS.drop.map(Math.fround));
  });
});

describe('the flow layer', () => {
  const layer = buildFlowLayer(nav, flow);

  it('draws an arrow from each cell with a next, and none from the objective', () => {
    expect(layer.positions.length).toBe(2 * 6);
  });

  it('points each arrow toward the next cell', () => {
    const [ax, , , bx] = Array.from(layer.positions.slice(0, 4));
    expect(bx!).toBeGreaterThan(ax!);
  });
});

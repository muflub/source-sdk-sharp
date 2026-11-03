import { describe, expect, it } from 'vitest';

import { ammoBar, buildConeLayer, buildHeatLayer, CONE_COLOUR, STAGE_COLOURS, type AiAmmo, type AiPerception } from '../aiInspect';
import type { AiNav } from '../../map/aiNav';

const ammo = (min: number, max: number, clip = 45): AiAmmo => ({
  weapon: 3, clip, min, max, histogram: [], reloading: false, reloadEndTick: 0, pEmpty: 0, pReloading: 0, push: false, windowSeconds: 0,
  barWidth: (max - min) / clip,
});

describe('the ammo bar', () => {
  it('is as wide as the range and starts at its low end', () => {
    const bar = ammoBar(ammo(10, 28));
    expect(bar.width).toBeCloseTo(((28 - 10) / 45) * 100);
    expect(bar.left).toBeCloseTo((10 / 45) * 100);
  });

  it('is a sliver when the clip is known, the whole track when nothing is', () => {
    expect(ammoBar(ammo(45, 45)).width).toBe(0);
    expect(ammoBar(ammo(0, 45)).width).toBeCloseTo(100);
  });
});

const perception: AiPerception = {
  slot: 2, eye: [0, 0, 64], yaw: 0, pitch: 0, fov: 90, range: 6000,
  candidates: [
    { slot: 1, at: [500, 0, 36], stage: 'clear', fraction: 1, sees: true },
    { slot: 3, at: [-10, 0, 36], stage: 'cone', fraction: 0, sees: false },
    { slot: 4, at: [500, 400, 36], stage: 'trace', fraction: 0, sees: false },
  ],
  heard: [],
};

describe('the cone layer', () => {
  it('draws the field edges at yaw ± fov/2 out to the drawn range', () => {
    const layer = buildConeLayer(perception, 1000);
    const end = Array.from(layer.positions.slice(3, 6));
    expect(end[0]).toBeCloseTo(Math.cos(-Math.PI / 4) * 1000);
    expect(end[1]).toBeCloseTo(Math.sin(-Math.PI / 4) * 1000);
    expect(Array.from(layer.colours.slice(0, 3))).toEqual(Array.from(new Float32Array(CONE_COLOUR)));
  });

  it('colours each candidate by the stage it reached', () => {
    const layer = buildConeLayer(perception);
    expect(layer.positions.length).toBe((2 + 3) * 6);
    const colourOf = (i: number) => Array.from(layer.colours.slice((2 + i) * 6, (2 + i) * 6 + 3));
    expect(colourOf(0)).toEqual(Array.from(new Float32Array(STAGE_COLOURS.clear!)));
    expect(colourOf(1)).toEqual(Array.from(new Float32Array(STAGE_COLOURS.cone!)));
    expect(colourOf(2)).toEqual(Array.from(new Float32Array(STAGE_COLOURS.trace!)));
  });
});

describe('the heat layer', () => {
  const nav = { map: 't', cells: 3, edges: 0, cell: [0, 0, 16, 16, 0, 3, 0, 16, 0, 32, 16, 0, 3, 0, 32, 0, 48, 16, 0, 3, 0], link: [], obstacles: [] } as AiNav;

  it('puts a point on each cell with mass, the hottest reddest', () => {
    const layer = buildHeatLayer(nav, { argMaxCell: 2, argMax: [40, 8, 0], entropy: 1, cells: [0, 2], mass: [0.2, 0.8] });
    expect(layer.positions.length).toBe(6);
    expect(layer.positions[3]).toBeCloseTo(40);
    expect(layer.colours[3]!).toBeGreaterThan(layer.colours[0]!);
  });
});

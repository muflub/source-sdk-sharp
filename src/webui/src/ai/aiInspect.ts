/*
 * The bot inspector's data (plan_hl2mp_ai §7.2), web UI only: /ai/bots lists
 * the bots, /ai/bot/<slot> answers one bot's record (memory, skills) and its
 * perception. Turned here into what the Ai tab shows and what the map tab
 * draws: the eye's cone and the lines to every candidate coloured by the
 * stage it reached, and each unseen opponent's belief as a heatmap.
 */

import { cellCentre, type AiNav, type Layer } from '../map/aiNav';

export type Vec = [number, number, number];

export interface AiBotListing { slot: number; serial: number; name: string; profile: string; spent: number }

export interface AiAmmo {
  weapon: number; clip: number; min: number; max: number; histogram: number[];
  reloading: boolean; reloadEndTick: number; pEmpty: number; pReloading: number; push: boolean; windowSeconds: number; barWidth: number;
}

export interface AiBelief { argMaxCell: number; argMax: Vec; entropy: number; cells: number[]; mass: number[] }

export interface AiHabits {
  retreatTendency: number; aggression: number; reloadStyle: number | string; engagementRange: number[];
  campCells: number[]; campCounts: number[]; pickupItems: number[]; reloadBehaviours: number[]; routeCells: number[]; routeCounts: number[];
}

export interface AiActor {
  slot: number; serial: number; inSight: boolean; confidence: number; lastSeen: Vec; lastSeenTick: number; lastHeard: Vec;
  lastHeardTick: number; lastHeardKind: string; healthEstimate: number; weaponSeen: number; rank: number; frags: number; deaths: number;
  ammo: AiAmmo; belief: AiBelief | null; intent: string; intentScore: number; habits: AiHabits | null;
}

export interface AiMemory {
  actors: AiActor[];
  investigations: { position: Vec; loudness: number; tick: number; kind: string; worth: number }[];
  items: { index: number; className: string; position: Vec; present: string; seenTick: number; expectedBack: number; respawnSeconds: number }[];
}

export interface AiSkills {
  profile: string; skills: { key: string; name: string; level: number }[]; spent: number; pool: number; cap: number;
  derived: Record<string, number>;
}

export interface AiTraceNode { id: number; name: string; status: string; value: number | null }

export interface AiDecide { tick: number; chosen: string; utility: number; scoreboardTerm: number; ammoTerm: number; nodes: AiTraceNode[] }

/** The doing-and-planning part (§7.2): the branch, the verdict and its window arithmetic, the reason terms, the last decides' traces. */
export interface AiDoing {
  branch: string; leaf: string; goal: Vec | null; targetSlot: number; verdict: string; pEmpty: number; pReloading: number;
  windowSeconds: number; arriveSeconds: number; scoreboardTerm: number; ammoTerm: number; expires: number; decides: AiDecide[];
}

export interface AiRecord { slot: number; serial: number; tick: number; memory: AiMemory | null; skills: AiSkills; doing: AiDoing | null }

export interface AiCandidate { slot: number; at: Vec; stage: string; fraction: number; sees: boolean }

export interface AiPerception {
  slot: number; eye: Vec; yaw: number; pitch: number; fov: number; range: number; candidates: AiCandidate[];
  heard: { slot: number; at: Vec; tick: number; kind: string }[];
}

export interface AiBot { record: AiRecord; perception: AiPerception; error?: string }

/** The scrubber's recording (plan_hl2mp_ai §7.1): GET /ai/replay, and the answer to POST /ai/replay/load. */
export interface AiReplayStatus {
  path: string; map: string; seed: number; world: boolean; first: number; last: number; ticks: number;
  bots: { slot: number; levels: number[] }[]; at: number; seeking: number; fault: string | null; error?: string;
}

/** One bot in the replay at a tick: its record, and the intent the recording holds beside the one the replay made. */
export interface AiReplayFrame {
  tick: number; decide: number; same: boolean; recorded: unknown; replayed: unknown; record: AiRecord;
  seeking?: number; at?: number; error?: string;
}

/** The inspector's colours: §7.1's green sighted, grey range, yellow cone, orange PVS, red trace. */
export const STAGE_COLOURS: Record<string, readonly number[]> = {
  clear: [0.30, 0.85, 0.35],
  range: [0.55, 0.55, 0.55],
  cone: [0.95, 0.85, 0.25],
  pvs: [0.95, 0.55, 0.20],
  trace: [0.90, 0.25, 0.20],
  unserved: [0.45, 0.45, 0.75],
  none: [0.3, 0.3, 0.3],
};

export const CONE_COLOUR = [0.85, 0.85, 0.95] as const;

/** The ammo estimate's uncertainty bar, in percent of the clip: from the range's low end, as wide as the range. */
export function ammoBar(ammo: AiAmmo): { left: number; width: number } {
  if (ammo.clip <= 0) return { left: 0, width: 0 };
  return { left: (ammo.min / ammo.clip) * 100, width: ammo.barWidth * 100 };
}

/**
 * The eye's field: two edges from the eye at yaw ± fov/2 out to the range
 * (and a short tick along the view), and a line to each candidate coloured
 * by its stage.
 */
export function buildConeLayer(p: AiPerception, drawRange = 1024): Layer {
  const lines: [Vec, Vec, readonly number[]][] = [];
  const reach = Math.min(p.range, drawRange);
  const edge = (deg: number): Vec => {
    const r = (deg * Math.PI) / 180;
    return [p.eye[0] + Math.cos(r) * reach, p.eye[1] + Math.sin(r) * reach, p.eye[2]];
  };
  lines.push([p.eye, edge(p.yaw - p.fov / 2), CONE_COLOUR]);
  lines.push([p.eye, edge(p.yaw + p.fov / 2), CONE_COLOUR]);
  for (const c of p.candidates) lines.push([p.eye, c.at, STAGE_COLOURS[c.stage] ?? STAGE_COLOURS.none!]);

  const layer = { positions: new Float32Array(lines.length * 6), colours: new Float32Array(lines.length * 6) };
  lines.forEach(([a, b, colour], i) => {
    layer.positions.set(a, i * 6);
    layer.positions.set(b, i * 6 + 3);
    layer.colours.set(colour, i * 6);
    layer.colours.set(colour, i * 6 + 3);
  });
  return layer;
}

/** A belief's heat for its mass: dark blue at nothing to bright red at the arg-max's share. */
export function heatColour(mass: number, top: number): number[] {
  const t = top > 0 ? Math.min(1, mass / top) : 0;
  return [0.2 + 0.8 * t, 0.15 + 0.35 * (1 - Math.abs(0.5 - t) * 2), 0.6 * (1 - t)];
}

/** The belief as points on the cells that hold mass, raised above the nav's own points. */
export function buildHeatLayer(nav: AiNav, belief: AiBelief): Layer {
  const top = Math.max(0, ...belief.mass);
  const layer = { positions: new Float32Array(belief.cells.length * 3), colours: new Float32Array(belief.cells.length * 3) };
  belief.cells.forEach((cell, i) => {
    const c = cellCentre(nav, cell);
    layer.positions.set([c[0], c[1], c[2] + 4], i * 3);
    layer.colours.set(heatColour(belief.mass[i]!, top), i * 3);
  });
  return layer;
}

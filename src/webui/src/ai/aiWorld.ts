/*
 * The AI tab's 3D view (plan_hl2mp_ai §7.1), as data: GET /ai/world's answer
 * turned into what the renderer draws. Every bot's body, aim, field of view,
 * what it saw and refused, where it is going, what it heard and what it
 * remembers, the selected bot bright and the others dim, each layer one that
 * can be off.
 *
 * The answer keeps truth and belief apart, and so does this: bodies, aim and
 * sight lines are where things are; heard sounds and remembered places are
 * where a bot believes they are, and can be wrong.
 */

import { BACKGROUND } from '../map/viewport/renderer';
import type { Layer } from '../map/aiNav';
import { linesLayer, STAGE_COLOURS, type Line, type Vec } from './aiInspect';

export interface WorldActor { slot: number; name: string; isBot: boolean; alive: boolean; at: Vec | null }
export interface WorldCandidate { slot: number; at: Vec; stage: string; fraction: number; sees: boolean }
export interface WorldTruth {
  eye: Vec; yaw: number; pitch: number; fov: number; range: number;
  /** How far along the view the first thing a bullet would hit is; -1 nothing within reach. */
  aimDistance: number;
  candidates: WorldCandidate[];
}
export interface WorldGoing {
  branch: string | null; leaf: string | null; goal: Vec | null; targetSlot: number;
  /** The bot's prefire: "" none, "aiming" or "firing", at the place it thinks someone is. */
  prefire?: string; prefireAt?: Vec | null;
}
export interface WorldRoute { source: string; target: Vec; points: Vec[] }
export interface WorldHeard { source: number; at: Vec; tick: number; kind: string; loudness: number }
export interface WorldRemembered { slot: number; at: Vec; tick: number; confidence: number; inSight: boolean }
export interface WorldBot {
  slot: number; name: string; stepping: boolean; truth: WorldTruth | null; going: WorldGoing | null; route: WorldRoute | null;
  beliefs: { heard: WorldHeard[]; remembered: WorldRemembered[] | null };
}
export interface AiWorld {
  tick: number; tickSeconds: number; map: string; navRefusal: string | null; costUs: number;
  actors: WorldActor[]; bots: WorldBot[]; error?: string;
}

export type WorldLayerKey = 'bodies' | 'aim' | 'sight' | 'going' | 'heard' | 'memory';
export type WorldToggles = Record<WorldLayerKey, boolean>;

/** The layers, by what they are: truth, what it means to do, or what it believes. */
export const WORLD_LAYERS: { key: WorldLayerKey; label: string; group: 'truth' | 'plan' | 'belief' }[] = [
  { key: 'bodies', label: 'Bots', group: 'truth' },
  { key: 'aim', label: 'Aiming', group: 'truth' },
  { key: 'sight', label: 'Sees', group: 'truth' },
  { key: 'going', label: 'Going', group: 'plan' },
  { key: 'heard', label: 'Heard', group: 'belief' },
  { key: 'memory', label: 'Remembers', group: 'belief' },
];

export const ALL_LAYERS: WorldToggles = { bodies: true, aim: true, sight: true, going: true, heard: true, memory: true };

/** How often the panel asks: about 5 Hz. */
export const WORLD_POLL_MS = 200;

/** Longer than this since the tick last moved and the readout says the data is stale. */
export const STALE_SECONDS = 1;

/** How far an aim that hits nothing is drawn. */
export const AIM_LENGTH = 2048;

/** How far the view cone is drawn, whatever the bot's range: a 6,000 u cone hides the map. */
export const CONE_LENGTH = 400;

/** Heard sounds fade out over this (the route answers the same window). */
export const HEARD_SECONDS = 5;

/** A player's box, from its feet, and the height of its middle. */
const HALF_WIDTH = 16;
const HEIGHT = 72;
export const BODY_MIDDLE = 36;

/** How much of a bot's colour shows when it is not the selected one. */
export const DIM = 0.4;

export const WORLD_COLOURS = {
  bot: [0.35, 0.75, 1.0],
  human: [1.0, 0.6, 0.85],
  aim: [0.15, 1.0, 1.0],
  cone: [0.55, 0.55, 0.72],
  seen: STAGE_COLOURS.clear!,
  partly: [0.70, 0.95, 0.20],
  recognising: [0.25, 0.75, 0.70],
  route: [1.0, 0.35, 0.9],
  target: [1.0, 1.0, 1.0],
  remembered: [1.0, 0.75, 0.3],
  prefire: [1.0, 0.25, 0.1],
} as const;

/** A heard sound's colour, by its kind (SoundKind's names, lower case). */
export const HEARD_COLOURS: Record<string, readonly number[]> = {
  gunfire: [1.0, 0.3, 0.2],
  explosion: [1.0, 0.55, 0.1],
  reload: [1.0, 0.85, 0.3],
  dryfire: [0.9, 0.65, 0.45],
  footstep: [0.55, 0.8, 1.0],
  pain: [1.0, 0.4, 0.6],
  death: [0.8, 0.35, 0.7],
  pickup: [0.5, 1.0, 0.6],
  door: [0.9, 0.9, 0.6],
  physics: [0.7, 0.7, 0.7],
  unknown: [0.6, 0.6, 0.8],
};

/** `share` of a colour over the background: how a line fades on an opaque canvas. */
export function fade(colour: readonly number[], share: number): number[] {
  const s = Math.max(0, Math.min(1, share));
  return [0, 1, 2].map(i => colour[i]! * s + BACKGROUND[i]! * (1 - s));
}

/**
 * Where a view looks, from Source's angles: yaw turns about +z from +x, and a
 * positive pitch looks DOWN. (AngleVectors in mathlib; VisionMath.Forward on
 * the server.) The sign is the thing a live comparison has to confirm.
 */
export function aimForward(yaw: number, pitch: number): Vec {
  const y = (yaw * Math.PI) / 180;
  const p = (pitch * Math.PI) / 180;
  return [Math.cos(p) * Math.cos(y), Math.cos(p) * Math.sin(y), -Math.sin(p)];
}

const along = (from: Vec, dir: Vec, distance: number): Vec =>
  [from[0] + dir[0] * distance, from[1] + dir[1] * distance, from[2] + dir[2] * distance];

/** Where a bot's aim line ends: what it hits, or the fixed length. */
export function aimEnd(t: WorldTruth): Vec {
  return along(t.eye, aimForward(t.yaw, t.pitch), t.aimDistance >= 0 ? t.aimDistance : AIM_LENGTH);
}

/** The colour of a line to an opponent: seen, partly seen, being recognised, or the stage that refused it. */
export function sightColour(c: WorldCandidate): readonly number[] {
  if (c.sees) return c.fraction < 1 ? WORLD_COLOURS.partly : WORLD_COLOURS.seen;
  if (c.stage === 'clear') return WORLD_COLOURS.recognising;
  return STAGE_COLOURS[c.stage] ?? STAGE_COLOURS.none!;
}

const cross = (a: Vec, b: Vec): Vec => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

function unit(v: Vec): Vec {
  const n = Math.hypot(v[0], v[1], v[2]) || 1;
  return [v[0] / n, v[1] / n, v[2] / n];
}

/**
 * The field of view as the vision pass judges it: a round cone about the view,
 * angle from the forward at most fov/2 (§4.1), so it tilts with the pitch. Four
 * edges out to a ring.
 */
export function coneLines(t: WorldTruth, colour: readonly number[], length = CONE_LENGTH): Line[] {
  const forward = aimForward(t.yaw, t.pitch);
  const reach = Math.min(t.range, length);
  const half = (t.fov / 2 * Math.PI) / 180;
  const right = unit(Math.abs(forward[2]) > 0.999 ? [1, 0, 0] : cross(forward, [0, 0, 1]));
  const up = cross(right, forward);
  const centre = along(t.eye, forward, reach * Math.cos(half));
  const radius = reach * Math.sin(half);
  const rim = (angle: number): Vec => along(along(centre, right, Math.cos(angle) * radius), up, Math.sin(angle) * radius);
  const lines: Line[] = [];
  const segments = 24;
  for (let i = 0; i < segments; i++) lines.push([rim((i / segments) * 2 * Math.PI), rim(((i + 1) / segments) * 2 * Math.PI), colour]);
  for (let i = 0; i < 4; i++) lines.push([t.eye, rim((i / 4) * 2 * Math.PI), colour]);
  return lines;
}

/** A flat ring, for a heard sound. */
function ring(at: Vec, radius: number, colour: readonly number[]): Line[] {
  const lines: Line[] = [];
  const point = (i: number): Vec => {
    const a = (i / 12) * 2 * Math.PI;
    return [at[0] + Math.cos(a) * radius, at[1] + Math.sin(a) * radius, at[2]];
  };
  for (let i = 0; i < 12; i++) lines.push([point(i), point(i + 1), colour]);
  return lines;
}

/** Three short bars through a point: the end of an aim, a remembered place. */
function star(at: Vec, size: number, colour: readonly number[]): Line[] {
  return [0, 1, 2].map(axis => {
    const a: Vec = [...at];
    const b: Vec = [...at];
    a[axis]! -= size;
    b[axis]! += size;
    return [a, b, colour] as Line;
  });
}

/** A diamond standing on a point: where a bot is going. */
function diamond(at: Vec, size: number, colour: readonly number[]): Line[] {
  const top: Vec = [at[0], at[1], at[2] + size * 2];
  const bottom: Vec = [at[0], at[1], at[2]];
  const mid = at[2] + size;
  const corners: Vec[] = [[at[0] + size, at[1], mid], [at[0], at[1] + size, mid], [at[0] - size, at[1], mid], [at[0], at[1] - size, mid]];
  return corners.flatMap((c, i) => [[top, c, colour], [bottom, c, colour], [c, corners[(i + 1) % 4]!, colour]] as Line[]);
}

/** A straight line as dashes, so a target line cannot be mistaken for a route or a sight line. */
function dashed(a: Vec, b: Vec, colour: readonly number[], dash = 24): Line[] {
  const length = Math.hypot(b[0] - a[0], b[1] - a[1], b[2] - a[2]);
  const count = Math.min(64, Math.max(1, Math.floor(length / (dash * 2))));
  const point = (t: number): Vec => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
  const lines: Line[] = [];
  for (let i = 0; i < count; i++) lines.push([point(i / count), point((i + 0.5) / count), colour]);
  return lines;
}

function boxLines(at: Vec, grow: number, colour: readonly number[]): Line[] {
  const x0 = at[0] - HALF_WIDTH - grow, x1 = at[0] + HALF_WIDTH + grow;
  const y0 = at[1] - HALF_WIDTH - grow, y1 = at[1] + HALF_WIDTH + grow;
  const z0 = at[2] - grow, z1 = at[2] + HEIGHT + grow;
  const c: Vec[] = [[x0, y0, z0], [x1, y0, z0], [x1, y1, z0], [x0, y1, z0], [x0, y0, z1], [x1, y0, z1], [x1, y1, z1], [x0, y1, z1]];
  const pairs = [[0, 1], [1, 2], [2, 3], [3, 0], [4, 5], [5, 6], [6, 7], [7, 4], [0, 4], [1, 5], [2, 6], [3, 7]];
  return pairs.map(([a, b]) => [c[a!]!, c[b!]!, colour] as Line);
}

export const bodyMiddle = (at: Vec): Vec => [at[0], at[1], at[2] + BODY_MIDDLE];

const LIFT = 8;
const lifted = (p: Vec): Vec => [p[0], p[1], p[2] + LIFT];

/** A bot's aim: the line along its view, and a star where it hits something. */
export function aimLines(t: WorldTruth, share: number): Line[] {
  const colour = fade(WORLD_COLOURS.aim, share);
  const end = aimEnd(t);
  return [[t.eye, end, colour], ...(t.aimDistance >= 0 ? star(end, 8, colour) : [])];
}

/** A bot's field, and a line to each opponent coloured by what became of it. */
export function sightLines(t: WorldTruth, share: number): Line[] {
  return [
    ...coneLines(t, fade(WORLD_COLOURS.cone, share)),
    ...t.candidates.map(c => [t.eye, c.at, fade(sightColour(c), share)] as Line),
  ];
}

/** A bot's follower's path, its goal, and a dashed line to its target where that target truly is. */
export function goingLines(bot: WorldBot, actors: WorldActor[], share: number): Line[] {
  const colour = fade(WORLD_COLOURS.route, share);
  const lines: Line[] = [];
  const points = bot.route?.points ?? [];
  for (let i = 1; i < points.length; i++) lines.push([lifted(points[i - 1]!), lifted(points[i]!), colour]);
  const goal = bot.route?.target ?? bot.going?.goal ?? null;
  if (goal) lines.push(...diamond(lifted(goal), 12, colour));
  const target = bot.going && bot.going.targetSlot >= 0 ? actors.find(a => a.slot === bot.going!.targetSlot) : undefined;
  if (bot.truth && target?.at) lines.push(...dashed(bot.truth.eye, bodyMiddle(target.at), fade(WORLD_COLOURS.target, share)));
  return lines;
}

/** How much of a sound heard at `tick` is left at `now`: all of it new, none past the window. */
export function heardShare(tick: number, now: number, tickSeconds: number): number {
  return Math.max(0, Math.min(1, 1 - ((now - tick) * tickSeconds) / HEARD_SECONDS));
}

/**
 * What a bot heard: a ring at the place it believes, as wide as the sound is
 * loud, a line back to the listener, coloured by kind and fading with age.
 */
export function heardLines(bot: WorldBot, world: Pick<AiWorld, 'tick' | 'tickSeconds'>, share: number): Line[] {
  const lines: Line[] = [];
  for (const h of bot.beliefs.heard) {
    const left = heardShare(h.tick, world.tick, world.tickSeconds);
    if (left <= 0) continue;
    const colour = fade(HEARD_COLOURS[h.kind] ?? HEARD_COLOURS.unknown!, left * share);
    lines.push(...ring(lifted(h.at), 10 + 30 * h.loudness, colour));
    if (bot.truth) lines.push([bot.truth.eye, lifted(h.at), colour]);
  }
  return lines;
}

/** Where a bot last saw each opponent out of sight now, faint as its confidence is low. */
export function memoryLines(bot: WorldBot, share: number): Line[] {
  const lines: Line[] = [];
  for (const r of bot.beliefs.remembered ?? []) {
    if (r.inSight) continue;
    lines.push(...star(bodyMiddle(r.at), 14, fade(WORLD_COLOURS.remembered, share * Math.max(0.25, r.confidence))));
  }
  // The place it is firing at, or about to, on what it believes: a line from its eye.
  const at = bot.going?.prefireAt;
  if (at) {
    const colour = fade(WORLD_COLOURS.prefire, bot.going?.prefire === 'firing' ? share : share * 0.5);
    lines.push(...star(at, 18, colour));
    if (bot.truth) lines.push([bot.truth.eye, at, colour]);
  }
  return lines;
}

export interface WorldLayers {
  botBodies: Layer; botMarkers: Layer; botAim: Layer; botSight: Layer; botGoing: Layer; botHeard: Layer; botMemory: Layer;
}

/**
 * Every layer for one answer. Each bot's lines at full strength when it is the
 * selected one (or none is), dim when not, and absent when `others` is off.
 */
export function buildWorldLayers(world: AiWorld, selected: number | null, toggles: WorldToggles, others = true): WorldLayers {
  const shareOf = (slot: number): number => (selected === null || slot === selected ? 1 : DIM);
  const shown = world.bots.filter(b => others || selected === null || b.slot === selected);
  const of = (key: WorldLayerKey, lines: (bot: WorldBot, share: number) => Line[]): Layer =>
    linesLayer(toggles[key] ? shown.flatMap(b => lines(b, shareOf(b.slot))) : []);

  const living = toggles.bodies ? world.actors.filter(a => a.alive && a.at) : [];
  const bodies: Line[] = [];
  const markers = { positions: new Float32Array(living.length * 3), colours: new Float32Array(living.length * 3) };
  living.forEach((a, i) => {
    const colour = fade(a.isBot ? WORLD_COLOURS.bot : WORLD_COLOURS.human, a.isBot ? shareOf(a.slot) : 1);
    bodies.push(...boxLines(a.at!, 0, colour));
    if (a.isBot && a.slot === selected) bodies.push(...boxLines(a.at!, 6, colour));
    markers.positions.set(bodyMiddle(a.at!), i * 3);
    markers.colours.set(colour, i * 3);
  });

  return {
    botBodies: linesLayer(bodies),
    botMarkers: markers,
    botAim: of('aim', (b, s) => (b.truth ? aimLines(b.truth, s) : [])),
    botSight: of('sight', (b, s) => (b.truth ? sightLines(b.truth, s) : [])),
    botGoing: of('going', (b, s) => goingLines(b, world.actors, s)),
    botHeard: of('heard', (b, s) => heardLines(b, world, s)),
    botMemory: of('memory', memoryLines),
  };
}

/**
 * A new answer over the last. A bot whose brain was stepping on a worker
 * answered without its mind; what it last said still stands, so nothing
 * flickers off for a poll.
 */
export function mergeWorld(last: AiWorld | null, next: AiWorld): AiWorld {
  // A 404's body says only why: nobody is there to draw.
  if (next.error) return { ...next, actors: [], bots: [] };
  if (!last) return next;
  return {
    ...next,
    bots: next.bots.map(bot => {
      if (!bot.stepping) return bot;
      const before = last.bots.find(b => b.slot === bot.slot);
      if (!before) return bot;
      return { ...bot, going: before.going, beliefs: { ...bot.beliefs, remembered: before.beliefs.remembered } };
    }),
  };
}

/** How old the data is, and whether the readout should say it has stopped. */
export function staleness(changedMs: number, nowMs: number): { seconds: number; stale: boolean } {
  const seconds = Math.max(0, nowMs - changedMs) / 1000;
  return { seconds, stale: seconds > STALE_SECONDS };
}

/** What the panel says instead of a scene: nothing when there is something to see. */
export function emptyState(world: AiWorld | null, failed: string | null, hasGeometry: boolean): string | null {
  if (failed) return `No answer from the game: ${failed}`;
  if (!world) return 'Asking the game…';
  if (world.error) return `The game does not answer /ai/world (${world.error}): the AI binding needs a running map.`;
  if (!hasGeometry && !world.map) return world.navRefusal ? `No map to draw: ${world.navRefusal}` : 'No map is loaded.';
  if (world.bots.length === 0) return 'No bots on this map. Add one with ai_bot_add, or from the create-server screen.';
  return null;
}

export interface WorldLabel { slot: number; name: string; isBot: boolean; selected: boolean; x: number; y: number }

/** The height of a label on screen, and how wide a name is taken to be for telling two apart. */
const LABEL_HEIGHT = 15;
const LABEL_WIDTH = 90;

/**
 * Each living player's name above its head, through the view's projection:
 * none behind the camera or off the canvas, the selected bot first, and a label
 * that would land on another moved up clear of it.
 */
export function placeLabels(
  actors: WorldActor[], selected: number | null, project: (at: Vec) => [number, number] | null, width: number, height: number,
): WorldLabel[] {
  const placed: WorldLabel[] = [];
  const order = [...actors].sort((a, b) => Number(b.slot === selected) - Number(a.slot === selected) || a.slot - b.slot);
  for (const actor of order) {
    if (!actor.alive || !actor.at) continue;
    const at = project([actor.at[0], actor.at[1], actor.at[2] + HEIGHT + 12]);
    if (!at || at[0] < 0 || at[1] < 0 || at[0] > width || at[1] > height) continue;
    let y = at[1];
    while (placed.some(p => Math.abs(p.x - at[0]) < LABEL_WIDTH && Math.abs(p.y - y) < LABEL_HEIGHT)) y -= LABEL_HEIGHT;
    placed.push({ slot: actor.slot, name: actor.name, isBot: actor.isBot, selected: actor.slot === selected, x: at[0], y });
  }
  return placed;
}

/** What a click can select: the living bots, by the middle of their bodies. */
export function pickables(world: AiWorld | null): { slot: number; origin: Vec }[] {
  return (world?.actors ?? []).filter(a => a.isBot && a.alive && a.at).map(a => ({ slot: a.slot, origin: bodyMiddle(a.at!) }));
}

/** The nearest a fit stands: close enough to read one bot's lines, far enough to see the room. */
const FIT_DISTANCE_MIN = 900;

/**
 * A camera that holds the living players. A map's bounds are the wrong frame
 * for this view: a 3D skybox thousands of units from the playable map (as on
 * dm_lockdown) leaves the bots a few pixels across, and the bots are the point.
 */
export function fitPlayers(world: AiWorld | null): { target: Vec; distance: number } | null {
  const at = (world?.actors ?? []).flatMap(a => (a.alive && a.at ? [a.at] : []));
  if (at.length === 0) return null;
  const lo = (axis: number) => Math.min(...at.map(p => p[axis]!));
  const hi = (axis: number) => Math.max(...at.map(p => p[axis]!));
  const target: Vec = [(lo(0) + hi(0)) / 2, (lo(1) + hi(1)) / 2, (lo(2) + hi(2)) / 2 + BODY_MIDDLE];
  const across = Math.hypot(hi(0) - lo(0), hi(1) - lo(1), hi(2) - lo(2));
  return { target, distance: Math.max(FIT_DISTANCE_MIN, across * 1.1) };
}

/** Where the camera looks while following a bot, or null when it is dead or gone. */
export function followPoint(world: AiWorld | null, slot: number | null): Vec | null {
  const at = world?.actors.find(a => a.slot === slot)?.at;
  return at ? bodyMiddle(at) : null;
}

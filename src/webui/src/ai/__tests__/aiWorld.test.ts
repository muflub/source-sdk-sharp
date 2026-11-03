/*
 * The 3D view's builders: where an aim points, what a route draws, how a heard
 * sound fades, what colour a refused opponent gets, what a toggle turns off.
 * Positions and colours, decided before any WebGL call.
 */

import { describe, expect, it } from 'vitest';

import {
  aimEnd, aimForward, AIM_LENGTH, ALL_LAYERS, buildWorldLayers, coneLines, CONE_LENGTH, DIM, emptyState, fade, fitPlayers, followPoint, goingLines,
  heardLines, heardShare, HEARD_COLOURS, HEARD_SECONDS, memoryLines, mergeWorld, pickables, placeLabels, sightColour, sightLines, staleness,
  WORLD_COLOURS, type AiWorld, type WorldBot, type WorldTruth,
} from '../aiWorld';
import { STAGE_COLOURS, type Vec } from '../aiInspect';
import { BACKGROUND } from '../../map/viewport/renderer';

const truth = (over: Partial<WorldTruth> = {}): WorldTruth => ({
  eye: [0, 0, 64], yaw: 0, pitch: 0, fov: 90, range: 6000, aimDistance: -1, candidates: [], ...over,
});

const bot = (slot: number, over: Partial<WorldBot> = {}): WorldBot => ({
  slot, name: `bot${slot}`, stepping: false, truth: truth(), going: null, route: null, beliefs: { heard: [], remembered: [] }, ...over,
});

const world = (over: Partial<AiWorld> = {}): AiWorld => ({
  tick: 1000, tickSeconds: 0.015, map: 'dm_x', navRefusal: null, costUs: 10,
  actors: [
    { slot: 1, name: 'Gordon', isBot: false, alive: true, at: [100, 0, 0] },
    { slot: 2, name: 'bot2', isBot: true, alive: true, at: [0, 0, 0] },
    { slot: 3, name: 'bot3', isBot: true, alive: true, at: [0, 300, 0] },
  ],
  bots: [bot(2), bot(3, { truth: truth({ eye: [0, 300, 64] }) })],
  ...over,
});

const close = (a: readonly number[], b: readonly number[]) => a.forEach((v, i) => expect(v).toBeCloseTo(b[i]!, 4));
const colourOf = (colours: Float32Array, line: number) => Array.from(colours.slice(line * 6, line * 6 + 3));
const lineOf = (positions: Float32Array, line: number): [number[], number[]] =>
  [Array.from(positions.slice(line * 6, line * 6 + 3)), Array.from(positions.slice(line * 6 + 3, line * 6 + 6))];

describe('where an aim points', () => {
  it('looks along +x at yaw 0', () => close(aimForward(0, 0), [1, 0, 0]));
  it('looks along +y at yaw 90', () => close(aimForward(90, 0), [0, 1, 0]));
  it('looks along -x at yaw 180', () => close(aimForward(180, 0), [-1, 0, 0]));
  it('looks along -y at yaw -90', () => close(aimForward(-90, 0), [0, -1, 0]));
  it('looks straight DOWN at pitch +90: Source pitch is positive down', () => close(aimForward(0, 90), [0, 0, -1]));
  it('looks straight UP at pitch -90', () => close(aimForward(0, -90), [0, 0, 1]));
  it('leans down by a positive pitch without turning', () => {
    const f = aimForward(90, 30);
    close(f, [0, Math.cos(Math.PI / 6), -0.5]);
  });

  it('ends at what it hits', () => close(aimEnd(truth({ aimDistance: 250 })), [250, 0, 64]));
  it('ends at the fixed length when it hits nothing', () => close(aimEnd(truth({ aimDistance: -1, yaw: 90 })), [0, AIM_LENGTH, 64]));
});

describe('the view cone', () => {
  it('stands apart from the aim: its colour is not the aim line\'s', () => {
    const layers = buildWorldLayers(world({ bots: [bot(2)] }), 2, ALL_LAYERS);
    expect(colourOf(layers.botAim.colours, 0)).toEqual(Array.from(new Float32Array(WORLD_COLOURS.aim.map((_, i) => fade(WORLD_COLOURS.aim, 1)[i]!))));
    expect(colourOf(layers.botSight.colours, 0)).not.toEqual(colourOf(layers.botAim.colours, 0));
  });

  it('reaches its length at half the field from the view, tilted with the pitch', () => {
    const t = truth({ pitch: 45 });
    const forward = aimForward(0, 45);
    const edges = coneLines(t, [1, 1, 1]).slice(-4);
    expect(edges).toHaveLength(4);
    for (const [a, b] of edges) {
      expect(a).toEqual(t.eye);
      const d: Vec = [b[0] - a[0], b[1] - a[1], b[2] - a[2]];
      const length = Math.hypot(...d);
      expect(length).toBeCloseTo(CONE_LENGTH, 2);
      expect(Math.acos((d[0] * forward[0] + d[1] * forward[1] + d[2] * forward[2]) / length)).toBeCloseTo(Math.PI / 4, 4);
    }
  });

  it('stays inside a short range', () => {
    const [, far] = coneLines(truth({ range: 100 }), [1, 1, 1]).at(-1)!;
    expect(Math.hypot(far[0], far[1], far[2] - 64)).toBeCloseTo(100, 2);
  });

  it('holds together looking straight down', () => {
    for (const [a, b] of coneLines(truth({ pitch: 90 }), [1, 1, 1])) {
      for (const v of [...a, ...b]) expect(Number.isFinite(v)).toBe(true);
    }
  });
});

describe('what a bot saw', () => {
  const c = (stage: string, sees: boolean, fraction: number) => ({ slot: 1, at: [1, 2, 3] as Vec, stage, fraction, sees });

  it('is the seen colour when wholly seen', () => expect(sightColour(c('clear', true, 1))).toEqual(WORLD_COLOURS.seen));
  it('is the partly-seen colour under a whole', () => expect(sightColour(c('clear', true, 0.4))).toEqual(WORLD_COLOURS.partly));
  it('is the recognising colour when clear but not yet recognised', () => expect(sightColour(c('clear', false, 1))).toEqual(WORLD_COLOURS.recognising));
  it.each(['range', 'cone', 'pvs', 'trace', 'unserved'])('is the %s stage\'s colour when refused there', stage =>
    expect(sightColour(c(stage, false, 0))).toEqual(STAGE_COLOURS[stage]));
  it('has a colour for a stage it has not heard of', () => expect(sightColour(c('new', false, 0))).toEqual(STAGE_COLOURS.none));

  it('draws a line from the eye to where each opponent truly is', () => {
    const lines = sightLines(truth({ candidates: [c('trace', false, 0)] }), 1);
    const [a, b, colour] = lines.at(-1)!;
    expect(a).toEqual([0, 0, 64]);
    expect(b).toEqual([1, 2, 3]);
    close(colour, STAGE_COLOURS.trace!);
  });
});

describe('where a bot is going', () => {
  const route = { source: 'brain', target: [500, 0, 0] as Vec, points: [[0, 0, 0], [100, 200, 0], [500, 0, 0]] as Vec[] };
  const colour = fade(WORLD_COLOURS.route, 1);

  it('draws the follower\'s path, leg by leg, not a straight line to the goal', () => {
    const lines = goingLines(bot(2, { route }), [], 1);
    expect(lines[0]!.slice(0, 2)).toEqual([[0, 0, 8], [100, 200, 8]]);
    expect(lines[1]!.slice(0, 2)).toEqual([[100, 200, 8], [500, 0, 8]]);
  });

  it('marks the goal the route ends at, over a goal the brain still names', () => {
    const lines = goingLines(bot(2, { route, going: { branch: 'Fight', leaf: 'x', goal: [9, 9, 9], targetSlot: -1 } }), [], 1);
    expect(lines.length).toBe(2 + 12);
    expect(lines.slice(2).every(([a]) => Math.abs(a[0] - 500) <= 12)).toBe(true);
  });

  it('marks the brain\'s goal when no follower has it yet', () => {
    const lines = goingLines(bot(2, { going: { branch: 'Fight', leaf: 'x', goal: [9, 9, 0], targetSlot: -1 } }), [], 1);
    expect(lines.length).toBe(12);
    close(lines[0]![2], colour);
  });

  it('draws nothing with no route, no goal and no target', () => expect(goingLines(bot(2), [], 1)).toEqual([]));

  it('dashes a line to the target where it truly stands', () => {
    const actors = world().actors;
    const lines = goingLines(bot(2, { going: { branch: 'Fight', leaf: 'x', goal: null, targetSlot: 1 } }), actors, 1);
    expect(lines.length).toBeGreaterThan(0);
    expect(lines[0]![0]).toEqual([0, 0, 64]);
    const last = lines.at(-1)!;
    expect(last[1][0]).toBeLessThan(100);
    expect(last[1][0]).toBeGreaterThan(50);
    close(last[2], fade(WORLD_COLOURS.target, 1));
  });

  it('draws no target line to a target that is dead', () => {
    const actors = world().actors.map(a => (a.slot === 1 ? { ...a, alive: false, at: null } : a));
    expect(goingLines(bot(2, { going: { branch: 'F', leaf: 'x', goal: null, targetSlot: 1 } }), actors, 1)).toEqual([]);
  });
});

describe('what a bot heard', () => {
  const heard = (tick: number, kind = 'gunfire', at: Vec = [40, 0, 0]) => ({ source: 1, at, tick, kind, loudness: 0.5 });

  it('is all there when new and gone past the window', () => {
    expect(heardShare(1000, 1000, 0.015)).toBe(1);
    expect(heardShare(1000, 1000 + HEARD_SECONDS / 0.015 / 2, 0.015)).toBeCloseTo(0.5, 5);
    expect(heardShare(1000, 1000 + HEARD_SECONDS / 0.015 + 1, 0.015)).toBe(0);
  });

  it('fades toward the background with age', () => {
    const w = { tick: 1000 + HEARD_SECONDS / 0.015 / 2, tickSeconds: 0.015 };
    const [, , colour] = heardLines(bot(2, { beliefs: { heard: [heard(1000)], remembered: [] } }), w, 1)[0]!;
    close(colour, fade(HEARD_COLOURS.gunfire!, 0.5));
    expect(colour[0]).toBeLessThan(HEARD_COLOURS.gunfire![0]!);
    expect(colour[0]).toBeGreaterThan(BACKGROUND[0]);
  });

  it('draws nothing of a sound past the window', () => {
    const w = { tick: 2000, tickSeconds: 0.015 };
    expect(heardLines(bot(2, { beliefs: { heard: [heard(1000)], remembered: [] } }), w, 1)).toEqual([]);
  });

  it('colours by kind, and a kind it has not heard of plainly', () => {
    const w = { tick: 1000, tickSeconds: 0.015 };
    const of = (kind: string) => heardLines(bot(2, { beliefs: { heard: [heard(1000, kind)], remembered: [] } }), w, 1)[0]![2];
    close(of('footstep'), HEARD_COLOURS.footstep!);
    close(of('gunfire'), HEARD_COLOURS.gunfire!);
    close(of('mystery'), HEARD_COLOURS.unknown!);
  });

  it('rings the place it believes and draws a line back to the listener', () => {
    const lines = heardLines(bot(2, { beliefs: { heard: [heard(1000)], remembered: [] } }), { tick: 1000, tickSeconds: 0.015 }, 1);
    expect(lines).toHaveLength(13);
    const [a, b] = lines.at(-1)!;
    expect(a).toEqual([0, 0, 64]);
    expect(b).toEqual([40, 0, 8]);
    // Loudness 0.5 widens the ring to 25 u about the place.
    expect(lines[0]![0][0]).toBeCloseTo(65, 4);
  });

  it('rings a dead listener\'s sounds with no line back', () => {
    const lines = heardLines(bot(2, { truth: null, beliefs: { heard: [heard(1000)], remembered: [] } }), { tick: 1000, tickSeconds: 0.015 }, 1);
    expect(lines).toHaveLength(12);
  });
});

describe('what a bot remembers', () => {
  const remembered = (inSight: boolean, confidence = 1) => ({ slot: 1, at: [10, 20, 30] as Vec, tick: 5, confidence, inSight });

  it('marks where it last saw an opponent out of sight now', () => {
    const lines = memoryLines(bot(2, { beliefs: { heard: [], remembered: [remembered(false)] } }), 1);
    expect(lines).toHaveLength(3);
    close(lines[0]![2], fade(WORLD_COLOURS.remembered, 1));
    expect(lines[0]![0]).toEqual([-4, 20, 66]);
  });

  it('marks nothing of one in sight: the sight line says so', () =>
    expect(memoryLines(bot(2, { beliefs: { heard: [], remembered: [remembered(true)] } }), 1)).toEqual([]));

  it('fades as its confidence does', () => {
    const lines = memoryLines(bot(2, { beliefs: { heard: [], remembered: [remembered(false, 0.5)] } }), 1);
    close(lines[0]![2], fade(WORLD_COLOURS.remembered, 0.5));
  });

  it('takes no memory at all while a brain steps', () =>
    expect(memoryLines(bot(2, { beliefs: { heard: [], remembered: null } }), 1)).toEqual([]));
});

describe('the layers', () => {
  const full = world({
    bots: [
      bot(2, {
        truth: truth({ aimDistance: 100, candidates: [{ slot: 1, at: [100, 0, 36], stage: 'clear', fraction: 1, sees: true }] }),
        route: { source: 'brain', target: [50, 0, 0], points: [[0, 0, 0], [50, 0, 0]] },
        beliefs: { heard: [{ source: 1, at: [5, 5, 0], tick: 1000, kind: 'footstep', loudness: 1 }], remembered: [{ slot: 3, at: [1, 1, 1], tick: 1, confidence: 1, inSight: false }] },
      }),
      bot(3),
    ],
  });
  const LINE_OF = { aim: 'botAim', sight: 'botSight', going: 'botGoing', heard: 'botHeard', memory: 'botMemory' } as const;

  it('has something in every layer when all are on', () => {
    const all = buildWorldLayers(full, 2, ALL_LAYERS);
    for (const layer of [all.botBodies, all.botMarkers, all.botAim, all.botSight, all.botGoing, all.botHeard, all.botMemory]) {
      expect(layer.positions.length).toBeGreaterThan(0);
    }
  });

  it.each(Object.entries(LINE_OF))('turning %s off empties that layer and no other', (key, name) => {
    const all = buildWorldLayers(full, 2, ALL_LAYERS);
    const off = buildWorldLayers(full, 2, { ...ALL_LAYERS, [key]: false });
    for (const layer of Object.values(LINE_OF)) {
      if (layer === name) expect(off[layer].positions.length).toBe(0);
      else expect(off[layer].positions).toEqual(all[layer].positions);
    }
    expect(off.botBodies.positions).toEqual(all.botBodies.positions);
  });

  it('turning the bots off empties their boxes and markers', () => {
    const off = buildWorldLayers(full, 2, { ...ALL_LAYERS, bodies: false });
    expect([off.botBodies.positions.length, off.botMarkers.positions.length]).toEqual([0, 0]);
  });

  it('draws the selected bot bright and the others dim', () => {
    const all = buildWorldLayers(world(), 3, ALL_LAYERS);
    close(colourOf(all.botAim.colours, 0), fade(WORLD_COLOURS.aim, DIM));
    close(colourOf(all.botAim.colours, 1), fade(WORLD_COLOURS.aim, 1));
    expect(colourOf(all.botAim.colours, 1)[1]!).toBeGreaterThan(colourOf(all.botAim.colours, 0)[1]!);
  });

  it('draws every bot bright when none is selected', () => {
    const all = buildWorldLayers(world(), null, ALL_LAYERS);
    close(colourOf(all.botAim.colours, 0), fade(WORLD_COLOURS.aim, 1));
    close(colourOf(all.botAim.colours, 1), fade(WORLD_COLOURS.aim, 1));
  });

  it('leaves the other bots\' lines out on asking, their bodies stay', () => {
    const only = buildWorldLayers(world(), 3, ALL_LAYERS, false);
    expect(only.botAim.positions.length).toBe(6);
    expect(lineOf(only.botAim.positions, 0)[0]).toEqual([0, 300, 64]);
    expect(only.botMarkers.positions.length).toBe(9);
  });

  it('marks a body at its middle, a human in another colour from a bot', () => {
    const all = buildWorldLayers(world(), null, ALL_LAYERS);
    expect(Array.from(all.botMarkers.positions.slice(0, 3))).toEqual([100, 0, 36]);
    const human = Array.from(all.botMarkers.colours.slice(0, 3));
    const robot = Array.from(all.botMarkers.colours.slice(3, 6));
    close(human, WORLD_COLOURS.human);
    close(robot, WORLD_COLOURS.bot);
  });

  it('boxes the selected bot twice', () => {
    const none = buildWorldLayers(world(), null, ALL_LAYERS).botBodies.positions.length;
    expect(buildWorldLayers(world(), 2, ALL_LAYERS).botBodies.positions.length).toBe(none + 12 * 6);
    expect(buildWorldLayers(world(), 1, ALL_LAYERS).botBodies.positions.length).toBe(none);
  });

  it('draws no body for the dead', () => {
    const actors = world().actors.map(a => (a.slot === 2 ? { ...a, alive: false, at: null } : a));
    expect(buildWorldLayers(world({ actors }), null, ALL_LAYERS).botMarkers.positions.length).toBe(6);
  });

  it('draws no aim or field for a dead bot', () => {
    const all = buildWorldLayers(world({ bots: [bot(2, { truth: null })] }), null, ALL_LAYERS);
    expect([all.botAim.positions.length, all.botSight.positions.length]).toEqual([0, 0]);
  });
});

describe('an answer over the last', () => {
  const going = { branch: 'Fight', leaf: 'push', goal: null, targetSlot: 1 };
  const remembered = [{ slot: 1, at: [1, 2, 3] as Vec, tick: 1, confidence: 1, inSight: false }];
  const before = world({ bots: [bot(2, { going, beliefs: { heard: [], remembered } })] });

  it('keeps what a stepping bot last said', () => {
    const next = world({ tick: 1001, bots: [bot(2, { stepping: true, going: null, beliefs: { heard: [], remembered: null } })] });
    const merged = mergeWorld(before, next);
    expect(merged.tick).toBe(1001);
    expect(merged.bots[0]!.going).toEqual(going);
    expect(merged.bots[0]!.beliefs.remembered).toEqual(remembered);
  });

  it('takes what a bot that is not stepping says, a null too', () => {
    const next = world({ bots: [bot(2, { going: null, beliefs: { heard: [], remembered: [] } })] });
    expect(mergeWorld(before, next).bots[0]!.going).toBeNull();
    expect(mergeWorld(before, next).bots[0]!.beliefs.remembered).toEqual([]);
  });

  it('makes an answer that is only an error an empty scene', () => {
    const merged = mergeWorld(before, { error: 'no such endpoint' } as AiWorld);
    expect(merged).toMatchObject({ error: 'no such endpoint', actors: [], bots: [] });
  });

  it('takes a first answer as it is', () => {
    const next = world();
    expect(mergeWorld(null, next)).toBe(next);
  });
});

describe('the readout', () => {
  it('calls fresh data fresh, and data past a second stale', () => {
    expect(staleness(1000, 1500)).toEqual({ seconds: 0.5, stale: false });
    expect(staleness(1000, 2500)).toEqual({ seconds: 1.5, stale: true });
  });
});

describe('the empty states', () => {
  it('says when the game does not answer', () => expect(emptyState(null, 'HTTP 504', false)).toContain('HTTP 504'));
  it('says it is asking before any answer', () => expect(emptyState(null, null, false)).toContain('Asking'));
  it('says when the route is not served', () => expect(emptyState(world({ error: 'no such endpoint' }), null, false)).toContain('/ai/world'));
  it('says when there is no map', () => expect(emptyState(world({ map: '', bots: [] }), null, false)).toBe('No map is loaded.'));
  it('names why the navigation was refused', () =>
    expect(emptyState(world({ map: '', navRefusal: 'no nav for x' }), null, false)).toContain('no nav for x'));
  it('says when there are no bots', () => expect(emptyState(world({ bots: [] }), null, true)).toContain('No bots'));
  it('says nothing when there is a scene', () => expect(emptyState(world(), null, true)).toBeNull());
});

describe('the labels', () => {
  const project = (at: Vec): [number, number] | null => (at[0] < 0 ? null : [at[0], at[1]]);

  it('names each living player above its head, through the projection', () => {
    const asked: Vec[] = [];
    const labels = placeLabels(world().actors, null, at => { asked.push(at); return project(at); }, 1000, 1000);
    expect(labels.map(l => l.name)).toEqual(['Gordon', 'bot2', 'bot3']);
    expect(asked[0]).toEqual([100, 0, 84]);
  });

  it('leaves out one behind the camera, off the canvas, or dead', () => {
    const actors = [
      { slot: 1, name: 'behind', isBot: true, alive: true, at: [-5, 5, 0] as Vec },
      { slot: 2, name: 'off', isBot: true, alive: true, at: [5000, 5, 0] as Vec },
      { slot: 3, name: 'dead', isBot: true, alive: false, at: null },
      { slot: 4, name: 'here', isBot: true, alive: true, at: [5, 5, 0] as Vec },
    ];
    expect(placeLabels(actors, null, project, 1000, 1000).map(l => l.name)).toEqual(['here']);
  });

  it('moves a label up clear of another, keeping the selected one in place', () => {
    const actors = [
      { slot: 1, name: 'a', isBot: true, alive: true, at: [100, 100, 0] as Vec },
      { slot: 2, name: 'b', isBot: true, alive: true, at: [110, 100, 0] as Vec },
    ];
    const labels = placeLabels(actors, 2, project, 1000, 1000);
    expect(labels[0]).toMatchObject({ name: 'b', selected: true, y: 100 });
    expect(labels[1]).toMatchObject({ name: 'a', selected: false, y: 85 });
  });
});

describe('fitting the camera', () => {
  it('centres on the living players, at their middle height', () => {
    const fit = fitPlayers(world())!;
    expect(fit.target).toEqual([50, 150, 36]);
  });

  it('stands far enough off to hold players far apart', () => {
    const far = world({ actors: [
      { slot: 1, name: 'a', isBot: true, alive: true, at: [0, 0, 0] },
      { slot: 2, name: 'b', isBot: true, alive: true, at: [3000, 4000, 0] },
    ] });
    expect(fitPlayers(far)!.distance).toBeCloseTo(5500);
  });

  it('stands no nearer than a room away from one player', () => {
    const one = world({ actors: [{ slot: 1, name: 'a', isBot: true, alive: true, at: [5, 6, 7] }] });
    expect(fitPlayers(one)).toEqual({ target: [5, 6, 43], distance: 900 });
  });

  it('ignores the dead, and has no answer with nobody alive', () => {
    const dead = { slot: 2, name: 'b', isBot: true, alive: false, at: null };
    expect(fitPlayers(world({ actors: [dead] }))).toBeNull();
    expect(fitPlayers(null)).toBeNull();
  });
});

describe('selecting and following', () => {
  it('lets a click pick only living bots, by the middle of their bodies', () => {
    const actors = world().actors.map(a => (a.slot === 3 ? { ...a, alive: false, at: null } : a));
    expect(pickables(world({ actors }))).toEqual([{ slot: 2, origin: [0, 0, 36] }]);
    expect(pickables(null)).toEqual([]);
  });

  it('follows the selected bot\'s body, and nothing once it is dead', () => {
    expect(followPoint(world(), 3)).toEqual([0, 300, 36]);
    expect(followPoint(world({ actors: [] }), 3)).toBeNull();
    expect(followPoint(world(), null)).toBeNull();
  });
});

/*
 * The 3D panel around its builders: it asks about five times a second while
 * shown and not at all while hidden, feeds the view, shares the tab's selection
 * and says when there is nothing to see. The view is a stand-in that records
 * what it was told: the GPU is what jsdom has not got, and the maths it would
 * do is the renderer's own tests.
 */

import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';

import { AiWorldPanel } from '../AiWorldPanel';
import type { AiWorld } from '../aiWorld';
import { WORLD_POLL_MS } from '../aiWorld';

const stand = vi.hoisted(() => ({ views: [] as { uploads: Map<string, Float32Array>; orbit: { target: number[]; distance: number }; framed: number; draws: number; canvas: HTMLCanvasElement }[], broken: false }));

vi.mock('../../api', () => ({ getAiWorld: vi.fn(), getMapGeometry: vi.fn(), getAiNav: vi.fn() }));

vi.mock('../../map/viewport/renderer', async original => {
  const real = await original<typeof import('../../map/viewport/renderer')>();
  class FakeView {
    uploads = new Map<string, Float32Array>();
    orbit = { target: [0, 0, 0], distance: 1 };
    framed = 0;
    draws = 0;
    constructor(readonly canvas: HTMLCanvasElement) {
      if (stand.broken) throw new Error('WebGL is unavailable');
      Object.defineProperty(canvas, 'clientWidth', { value: 1000 });
      Object.defineProperty(canvas, 'clientHeight', { value: 800 });
      stand.views.push(this);
    }
    upload(name: string, positions: Float32Array) { this.uploads.set(name, positions); }
    frame() { this.framed++; }
    draw() { this.draws++; }
    project(p: [number, number, number]): [number, number] { return [p[0], p[1]]; }
    beginDrag(event: MouseEvent) { return { x: event.clientX, y: event.clientY, button: event.button, moved: false }; }
    applyDrag() {}
    zoomBy() {}
    pick<T extends { origin: [number, number, number] }>(x: number, y: number, things: T[]): T | null {
      return things.find(t => Math.hypot(t.origin[0] - x, t.origin[1] - y) < 14) ?? null;
    }
  }
  return { ...real, View: FakeView };
});

const api = await import('../../api') as unknown as {
  getAiWorld: ReturnType<typeof vi.fn>; getMapGeometry: ReturnType<typeof vi.fn>; getAiNav: ReturnType<typeof vi.fn>;
};

const truth = { eye: [0, 0, 64], yaw: 0, pitch: 0, fov: 90, range: 6000, aimDistance: -1, candidates: [] };

const answer = (over: Partial<AiWorld> = {}): AiWorld => ({
  tick: 500, tickSeconds: 0.015, map: 'dm_x', navRefusal: null, costUs: 42,
  actors: [
    { slot: 1, name: 'Gordon', isBot: false, alive: true, at: [100, 100, 0] },
    { slot: 2, name: 'Alyx', isBot: true, alive: true, at: [200, 100, 0] },
    { slot: 3, name: 'Barney', isBot: true, alive: true, at: [400, 300, 0] },
  ],
  bots: [
    { slot: 2, name: 'Alyx', stepping: false, truth: truth as never, going: { branch: 'Fight', leaf: 'push', goal: null, targetSlot: 1 }, route: { source: 'brain', target: [9, 9, 9], points: [[0, 0, 0], [9, 9, 9]] }, beliefs: { heard: [], remembered: [] } },
    { slot: 3, name: 'Barney', stepping: false, truth: truth as never, going: null, route: null, beliefs: { heard: [], remembered: [] } },
  ],
  ...over,
});

const geometry = { map: 'dm_x', mins: [0, 0, 0], maxs: [10, 10, 10], vertices: [0, 0, 0, 1, 0, 0], faceStarts: [0], faceLengths: [2], faceIndices: [0, 1], models: [] };

const view = () => stand.views.at(-1)!;

beforeEach(() => {
  vi.clearAllMocks();
  stand.views.length = 0;
  stand.broken = false;
  api.getAiWorld.mockResolvedValue(answer());
  api.getMapGeometry.mockResolvedValue(geometry);
});

afterEach(() => { vi.useRealTimers(); });

async function shown(selected: number | null = 2, onSelect = vi.fn()) {
  const utils = render(<AiWorldPanel visible selected={selected} onSelect={onSelect} />);
  await screen.findByText(/tick 500/);
  return { ...utils, onSelect };
}

it('asks at about 5 Hz while it shows', async () => {
  vi.useFakeTimers();
  render(<AiWorldPanel visible selected={null} onSelect={vi.fn()} />);
  await act(async () => { await vi.advanceTimersByTimeAsync(0); });
  const first = api.getAiWorld.mock.calls.length;
  expect(first).toBe(1);
  await act(async () => { await vi.advanceTimersByTimeAsync(WORLD_POLL_MS * 5); });
  expect(api.getAiWorld.mock.calls.length).toBe(first + 5);
  expect(1000 / WORLD_POLL_MS).toBe(5);
});

it('stops asking when hidden, and starts again when shown', async () => {
  vi.useFakeTimers();
  const { rerender } = render(<AiWorldPanel visible selected={null} onSelect={vi.fn()} />);
  await act(async () => { await vi.advanceTimersByTimeAsync(WORLD_POLL_MS * 2); });
  const before = api.getAiWorld.mock.calls.length;
  expect(before).toBeGreaterThan(0);

  rerender(<AiWorldPanel visible={false} selected={null} onSelect={vi.fn()} />);
  await act(async () => { await vi.advanceTimersByTimeAsync(WORLD_POLL_MS * 20); });
  expect(api.getAiWorld.mock.calls.length).toBe(before);

  rerender(<AiWorldPanel visible selected={null} onSelect={vi.fn()} />);
  await act(async () => { await vi.advanceTimersByTimeAsync(WORLD_POLL_MS * 2); });
  expect(api.getAiWorld.mock.calls.length).toBeGreaterThan(before);
});

it('stops asking while the browser tab is in the background', async () => {
  vi.useFakeTimers();
  const hidden = vi.spyOn(document, 'hidden', 'get').mockReturnValue(true);
  render(<AiWorldPanel visible selected={null} onSelect={vi.fn()} />);
  await act(async () => { await vi.advanceTimersByTimeAsync(WORLD_POLL_MS * 5); });
  expect(api.getAiWorld).not.toHaveBeenCalled();
  hidden.mockRestore();
});

it('never asks for a second answer while one is outstanding', async () => {
  vi.useFakeTimers();
  let answerNow: (w: AiWorld) => void = () => {};
  api.getAiWorld.mockImplementation(() => new Promise<AiWorld>(resolve => { answerNow = resolve; }));
  render(<AiWorldPanel visible selected={null} onSelect={vi.fn()} />);
  await act(async () => { await vi.advanceTimersByTimeAsync(WORLD_POLL_MS * 10); });
  expect(api.getAiWorld).toHaveBeenCalledTimes(1);
  await act(async () => { answerNow(answer()); await vi.advanceTimersByTimeAsync(WORLD_POLL_MS + 1); });
  expect(api.getAiWorld).toHaveBeenCalledTimes(2);
});

it('reads the map once, feeds the wireframe and frames it', async () => {
  await shown();
  await waitFor(() => expect(view().uploads.get('world')?.length).toBe(12));
  expect(view().framed).toBeGreaterThan(0);
  expect(api.getMapGeometry).toHaveBeenCalledTimes(1);
  expect(api.getMapGeometry).toHaveBeenCalledWith('dm_x');
});

it('reads the map again when the level changes', async () => {
  vi.useFakeTimers();
  render(<AiWorldPanel visible selected={null} onSelect={vi.fn()} />);
  await act(async () => { await vi.advanceTimersByTimeAsync(WORLD_POLL_MS * 3); });
  expect(api.getMapGeometry).toHaveBeenCalledTimes(1);
  api.getAiWorld.mockResolvedValue(answer({ map: 'dm_y' }));
  await act(async () => { await vi.advanceTimersByTimeAsync(WORLD_POLL_MS * 3); });
  expect(api.getMapGeometry).toHaveBeenCalledTimes(2);
  expect(api.getMapGeometry).toHaveBeenLastCalledWith('dm_y');
});

it('fits the camera to the players once a map, not on every poll', async () => {
  vi.useFakeTimers();
  render(<AiWorldPanel visible selected={null} onSelect={vi.fn()} />);
  await act(async () => { await vi.advanceTimersByTimeAsync(WORLD_POLL_MS); });
  const fitted = view().orbit.target;
  expect(fitted).toEqual([250, 200, 36]);
  view().orbit.target = [1, 2, 3];
  await act(async () => { await vi.advanceTimersByTimeAsync(WORLD_POLL_MS * 5); });
  expect(view().orbit.target).toEqual([1, 2, 3]);
  fireEvent.click(screen.getByText('Fit players'));
  expect(view().orbit.target).toEqual([250, 200, 36]);
});

it('shows beliefs as beliefs: the toggles are grouped by what they are', async () => {
  await shown();
  const toolbar = document.querySelector('.ai3d-toolbar')!;
  expect(Array.from(toolbar.querySelectorAll('.ai3d-group')).map(g => g.textContent)).toEqual(['Truth:', 'Plan:', 'Beliefs:']);
  const heard = screen.getByLabelText('Heard').closest('label')!;
  expect(heard.textContent).toContain('Beliefs:');
});

it('drops the last map\'s wireframe at once on a level change, and fits the new map\'s players', async () => {
  vi.useFakeTimers();
  let answerMap: (g: unknown) => void = () => {};
  render(<AiWorldPanel visible selected={null} onSelect={vi.fn()} />);
  await act(async () => { await vi.advanceTimersByTimeAsync(WORLD_POLL_MS); });
  expect(view().uploads.get('world')?.length).toBe(12);
  expect(view().orbit.target).toEqual([250, 200, 36]);

  api.getMapGeometry.mockImplementation(() => new Promise(resolve => { answerMap = resolve; }));
  api.getAiWorld.mockResolvedValue(answer({ map: 'dm_y', actors: [{ slot: 1, name: 'Gordon', isBot: false, alive: true, at: [1000, 1000, 0] }], bots: [] }));
  await act(async () => { await vi.advanceTimersByTimeAsync(WORLD_POLL_MS * 2); });
  expect(view().uploads.get('world')?.length).toBe(0);
  expect(view().orbit.target).toEqual([1000, 1000, 36]);

  view().orbit.target = [0, 0, 0];
  await act(async () => { answerMap({ ...geometry, map: 'dm_y' }); await vi.advanceTimersByTimeAsync(WORLD_POLL_MS); });
  expect(view().uploads.get('world')?.length).toBe(12);
  expect(view().orbit.target).toEqual([1000, 1000, 36]);
});

it('does not fit until a player is there, then fits them', async () => {
  vi.useFakeTimers();
  api.getAiWorld.mockResolvedValue(answer({ actors: [], bots: [] }));
  render(<AiWorldPanel visible selected={null} onSelect={vi.fn()} />);
  await act(async () => { await vi.advanceTimersByTimeAsync(WORLD_POLL_MS * 2); });
  expect(view().orbit.target).toEqual([0, 0, 0]);
  api.getAiWorld.mockResolvedValue(answer({ actors: [{ slot: 1, name: 'Gordon', isBot: false, alive: true, at: [50, 60, 0] }], bots: [] }));
  await act(async () => { await vi.advanceTimersByTimeAsync(WORLD_POLL_MS * 2); });
  expect(view().orbit.target).toEqual([50, 60, 36]);
});

it('asks again for a wireframe that could not be read', async () => {
  vi.useFakeTimers();
  api.getMapGeometry.mockRejectedValueOnce(new Error('HTTP 504'));
  render(<AiWorldPanel visible selected={null} onSelect={vi.fn()} />);
  await act(async () => { await vi.advanceTimersByTimeAsync(WORLD_POLL_MS * 2); });
  expect(api.getMapGeometry).toHaveBeenCalledTimes(1);
  expect(view().uploads.get('world')?.length ?? 0).toBe(0);
  await act(async () => { await vi.advanceTimersByTimeAsync(3000); });
  expect(api.getMapGeometry).toHaveBeenCalledTimes(2);
  expect(view().uploads.get('world')?.length).toBe(12);
});

it('says when a map has no wireframe, and does not keep asking', async () => {
  vi.useFakeTimers();
  api.getMapGeometry.mockResolvedValue({ error: 'this map has no geometry' });
  render(<AiWorldPanel visible selected={2} onSelect={vi.fn()} />);
  await act(async () => { await vi.advanceTimersByTimeAsync(4000); });
  expect(screen.getByTestId('world-doing').textContent).toContain('no wireframe: this map has no geometry');
  expect(api.getMapGeometry).toHaveBeenCalledTimes(1);
});

it('labels the players over the view', async () => {
  await shown();
  expect((await screen.findByTestId('world-label-2')).textContent).toBe('Alyx');
  expect(screen.getByTestId('world-label-1').textContent).toBe('Gordon');
});

it('marks the selected bot\'s label, and only its', async () => {
  await shown(3);
  expect((await screen.findByTestId('world-label-3')).className).toContain('ai3d-selected');
  expect(screen.getByTestId('world-label-2').className).not.toContain('ai3d-selected');
});

it('selects a bot by clicking its label, never a human by its', async () => {
  const { onSelect } = await shown();
  fireEvent.click(await screen.findByTestId('world-label-3'));
  expect(onSelect).toHaveBeenCalledWith(3);
  fireEvent.click(screen.getByTestId('world-label-1'));
  expect(onSelect).toHaveBeenCalledTimes(1);
});

it('selects a bot by clicking its marker', async () => {
  const { onSelect } = await shown();
  await screen.findByTestId('world-label-3');
  const canvas = view().canvas;
  fireEvent.mouseDown(canvas, { clientX: 400, clientY: 300, button: 0 });
  fireEvent.mouseUp(window, { clientX: 400, clientY: 300, button: 0 });
  expect(onSelect).toHaveBeenCalledWith(3);
});

it('selects nothing clicking a human\'s marker or empty space', async () => {
  const { onSelect } = await shown();
  await screen.findByTestId('world-label-3');
  for (const [x, y] of [[100, 100], [700, 700]]) {
    fireEvent.mouseDown(view().canvas, { clientX: x, clientY: y, button: 0 });
    fireEvent.mouseUp(window, { clientX: x, clientY: y, button: 0 });
  }
  expect(onSelect).not.toHaveBeenCalled();
});

it('dims on another selection: the layers follow the prop', async () => {
  const { rerender } = await shown(2);
  await waitFor(() => expect(view().uploads.get('botAim')?.length).toBe(12));
  const markers = view().uploads.get('botMarkers')!;
  rerender(<AiWorldPanel visible selected={3} onSelect={vi.fn()} />);
  expect(view().uploads.get('botMarkers')!.length).toBe(markers.length);
  expect(screen.getByTestId('world-doing').textContent).toContain('Barney');
});

it('turns a layer off from its toggle', async () => {
  await shown();
  await waitFor(() => expect(view().uploads.get('botAim')?.length).toBeGreaterThan(0));
  fireEvent.click(screen.getByLabelText('Aiming'));
  expect(view().uploads.get('botAim')?.length).toBe(0);
  expect(view().uploads.get('botSight')?.length).toBeGreaterThan(0);
  fireEvent.click(screen.getByLabelText('Aiming'));
  expect(view().uploads.get('botAim')?.length).toBeGreaterThan(0);
});

it('leaves the other bots out from its toggle', async () => {
  await shown(2);
  await waitFor(() => expect(view().uploads.get('botAim')?.length).toBe(12));
  fireEvent.click(screen.getByLabelText('Other bots'));
  expect(view().uploads.get('botAim')?.length).toBe(6);
});

it('does not read the nav until asked, then draws its cells', async () => {
  api.getAiNav.mockResolvedValue({ map: 'dm_x', cells: 1, edges: 0, cell: [0, 0, 8, 8, 0, 1, 0], link: [], obstacles: [] });
  await shown();
  expect(api.getAiNav).not.toHaveBeenCalled();
  fireEvent.click(screen.getByLabelText('Nav cells'));
  await waitFor(() => expect(view().uploads.get('navCells')?.length).toBe(3));
  expect(api.getAiNav).toHaveBeenCalledTimes(1);
  fireEvent.click(screen.getByLabelText('Nav cells'));
  await waitFor(() => expect(view().uploads.get('navCells')?.length).toBe(0));
});

it('keeps the camera on the selected bot while following', async () => {
  await shown(3);
  await screen.findByTestId('world-label-3');
  expect(view().orbit.target).not.toEqual([400, 300, 36]);
  fireEvent.click(screen.getByLabelText('Follow selected'));
  await waitFor(() => expect(view().orbit.target).toEqual([400, 300, 36]));
});

it('says live while the tick moves, and STALE once it stops', async () => {
  vi.useFakeTimers();
  let tick = 500;
  api.getAiWorld.mockImplementation(async () => answer({ tick: tick++ }));
  render(<AiWorldPanel visible selected={2} onSelect={vi.fn()} />);
  await act(async () => { await vi.advanceTimersByTimeAsync(WORLD_POLL_MS * 2); });
  expect(screen.getByTestId('world-readout').textContent).toContain('live');
  expect(screen.getByTestId('world-readout').textContent).toContain('route 42 µs');

  api.getAiWorld.mockImplementation(async () => answer({ tick: 900 }));
  await act(async () => { await vi.advanceTimersByTimeAsync(WORLD_POLL_MS * 10); });
  const readout = screen.getByTestId('world-readout');
  expect(readout.textContent).toContain('STALE');
  expect(readout.className).toContain('ai3d-stale');
  expect(readout.textContent).toContain('tick 900');
});

it('says stale when the game stops answering, and why', async () => {
  vi.useFakeTimers();
  render(<AiWorldPanel visible selected={2} onSelect={vi.fn()} />);
  await act(async () => { await vi.advanceTimersByTimeAsync(WORLD_POLL_MS); });
  api.getAiWorld.mockRejectedValue(new Error('HTTP 504'));
  await act(async () => { await vi.advanceTimersByTimeAsync(WORLD_POLL_MS * 2); });
  expect(screen.getByTestId('world-readout').textContent).toContain('STALE');
  expect(screen.getByTestId('world-empty').textContent).toContain('HTTP 504');
});

it('says so when there are no bots', async () => {
  api.getAiWorld.mockResolvedValue(answer({ bots: [], actors: [] }));
  await shown(null);
  expect((await screen.findByTestId('world-empty')).textContent).toContain('No bots');
});

it('says so when the route is not there', async () => {
  api.getAiWorld.mockResolvedValue({ error: 'no such endpoint' });
  render(<AiWorldPanel visible selected={null} onSelect={vi.fn()} />);
  expect((await screen.findByTestId('world-empty')).textContent).toContain('/ai/world');
  expect(api.getMapGeometry).not.toHaveBeenCalled();
});

it('says when WebGL will not start, and keeps polling', async () => {
  stand.broken = true;
  render(<AiWorldPanel visible selected={null} onSelect={vi.fn()} />);
  expect((await screen.findByTestId('world-empty')).textContent).toContain('WebGL is unavailable');
  await waitFor(() => expect(api.getAiWorld).toHaveBeenCalled());
});

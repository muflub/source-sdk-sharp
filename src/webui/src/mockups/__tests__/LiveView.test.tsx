/*
 * The Live view: it lists every segment, draws the chosen one, lists the
 * values it read, and sends edits, sizes, Random and Reset to the live
 * mock-ups, keeping the edits in this browser.
 */

import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';

import { LiveView } from '../LiveView';
import { editsKey, type LiveFrame, type LiveList } from '../live';

const list: LiveList = {
  segments: [
    { id: 'screen:scoreboard', name: 'Scoreboard', kind: 'screen', assembly: 'SourceSharp.Client.Ui',
      presets: [{ slug: 'twelve', title: '12 players' }, { slug: 'auto', title: 'made-up data' }] },
    { id: 'screen:hud', name: 'Hud', kind: 'hud', assembly: 'SourceSharp.Client.Ui', presets: [{ slug: 'alive', title: 'alive' }] },
    { id: 'panel:TF2.Client.Ui.HealthPanel', name: 'HealthPanel', kind: 'panel', assembly: 'TF2.Client.Ui',
      presets: [{ slug: 'auto', title: 'made-up data' }] },
  ],
  sizes: [{ name: '1920x1080', w: 1920, h: 1080, scale: 1 }, { name: '1024x768', w: 1024, h: 768, scale: 1 }],
  fonts: [],
};

interface RenderRequest { op: 'render'; segment: string; preset: string; w: number; h: number; scale: number; values: Record<string, unknown> }

let renders: RenderRequest[] = [];
let answer: (r: RenderRequest) => LiveFrame = () => ({});

function frameFor(r: RenderRequest): LiveFrame {
  const count = typeof r.values['IScoreboardSource.PlayerCount'] === 'number' ? r.values['IScoreboardSource.PlayerCount'] as number : 2;
  const values = [
    { key: 'IScoreboardSource.ServerName', source: 'IScoreboardSource', type: 'string' as const, value: 'Lambda', changed: false, options: null },
    { key: 'IScoreboardSource.PlayerCount', source: 'IScoreboardSource', type: 'int' as const, value: 2, changed: false, options: null },
    { key: 'IScoreboardSource.TryTeam(0)', source: 'IScoreboardSource', type: 'bool' as const, value: true, changed: false, options: null },
    { key: 'IScoreboardSource.TryTeam(0).team.Name', source: 'IScoreboardSource', type: 'string' as const, value: 'Rebels', changed: false, options: null },
    ...Array.from({ length: Math.min(count, 8) }, (_, i) => (
      { key: `IScoreboardSource.Player(${i}).Name`, source: 'IScoreboardSource', type: 'string' as const, value: `P${i}`, changed: false, options: null })),
  ].map(v => (v.key in r.values ? { ...v, value: r.values[v.key] as string, changed: true } : v));
  return {
    name: 'scoreboard', layer: 'Menu', w: r.w, h: r.h, ms: 5,
    svg: `<svg data-segment="${r.segment}" data-preset="${r.preset}" data-w="${r.w}"></svg>`,
    elements: [{ i: 0, id: 1, parent: -1, kind: 'Box', name: 'scoreboard', x: 0, y: 0, w: r.w, h: r.h, flags: 'None' }],
    values,
  };
}

beforeEach(() => {
  localStorage.clear();
  renders = [];
  answer = frameFor;
  vi.stubGlobal('fetch', vi.fn(async (_url: string, init?: RequestInit) => {
    const body = JSON.parse(String(init?.body ?? '{}')) as { op: string };
    if (body.op === 'list') return new Response(JSON.stringify(list));
    renders.push(body as RenderRequest);
    return new Response(JSON.stringify(answer(body as RenderRequest)));
  }));
});

afterEach(() => vi.unstubAllGlobals());

const lastRender = (): RenderRequest => renders.at(-1)!;

async function drawn(segment: string, preset: string): Promise<void> {
  await waitFor(() => expect(document.querySelector(`svg[data-segment="${segment}"][data-preset="${preset}"]`)).toBeTruthy());
}

it('lists every segment under its kind and draws the first with its first preset', async () => {
  render(<LiveView base="/preview/" version={0} />);
  const nav = await screen.findByRole('navigation', { name: 'UI segments' });
  for (const h of ['Screens', 'HUD', 'Panels']) expect(within(nav).getByText(h)).toBeTruthy();
  expect(within(nav).getByText('HealthPanel')).toBeTruthy();
  await drawn('screen:scoreboard', 'twelve');
  expect(lastRender()).toMatchObject({ w: 1920, h: 1080, scale: 1, values: {} });
});

it('lists the values the screen read, grouped by its interface and call', async () => {
  render(<LiveView base="/preview/" version={0} />);
  const data = await screen.findByRole('complementary', { name: 'Mock data' });
  await waitFor(() => expect(within(data).getByText('IScoreboardSource')).toBeTruthy());
  expect(within(data).getByText('Player(1)')).toBeTruthy();
  expect(within(data).getByText('returns')).toBeTruthy();
  expect((within(data).getByLabelText('IScoreboardSource.ServerName') as HTMLInputElement).value).toBe('Lambda');
});

it('an edit is drawn, marked, and kept in this browser for the next visit', async () => {
  const first = render(<LiveView base="/preview/" version={0} />);
  const box = await screen.findByLabelText('IScoreboardSource.ServerName');
  fireEvent.change(box, { target: { value: 'Black Mesa' } });
  await waitFor(() => expect(lastRender().values).toEqual({ 'IScoreboardSource.ServerName': 'Black Mesa' }));
  expect(JSON.parse(localStorage.getItem(editsKey('screen:scoreboard', 'twelve'))!)).toEqual({ 'IScoreboardSource.ServerName': 'Black Mesa' });
  await waitFor(() => expect(screen.getByText('1 edited', { exact: false })).toBeTruthy());
  first.unmount();

  renders = [];
  render(<LiveView base="/preview/" version={0} />);
  await waitFor(() => expect(renders.length).toBeGreaterThan(0));
  expect(lastRender().values).toEqual({ 'IScoreboardSource.ServerName': 'Black Mesa' });
});

it('Reset drops every edit, from the request and from storage', async () => {
  localStorage.setItem(editsKey('screen:scoreboard', 'twelve'), JSON.stringify({ 'IScoreboardSource.ServerName': 'X' }));
  render(<LiveView base="/preview/" version={0} />);
  await waitFor(() => expect(lastRender()?.values).toEqual({ 'IScoreboardSource.ServerName': 'X' }));
  await userEvent.click(screen.getByRole('button', { name: 'Reset' }));
  await waitFor(() => expect(lastRender().values).toEqual({}));
  expect(localStorage.getItem(editsKey('screen:scoreboard', 'twelve'))).toBeNull();
});

it('Random gives every value read a value, and the rows a raised count adds', async () => {
  render(<LiveView base="/preview/" version={0} />);
  await drawn('screen:scoreboard', 'twelve');
  await userEvent.click(screen.getByRole('button', { name: 'Random' }));
  await waitFor(() => {
    const values = lastRender().values;
    const count = values['IScoreboardSource.PlayerCount'] as number;
    expect(typeof values['IScoreboardSource.ServerName']).toBe('string');
    for (let i = 0; i < count; i++) expect(values).toHaveProperty([`IScoreboardSource.Player(${i}).Name`]);
  });
});

it('a size preset, the width box and the default size each re-render at that size', async () => {
  render(<LiveView base="/preview/" version={0} />);
  await drawn('screen:scoreboard', 'twelve');
  await userEvent.selectOptions(screen.getByLabelText('Size preset'), '1024x768');
  await waitFor(() => expect(lastRender()).toMatchObject({ w: 1024, h: 768 }));
  const width = screen.getByLabelText('Width');
  await userEvent.clear(width);
  await userEvent.type(width, '1500{Enter}');
  await waitFor(() => expect(lastRender()).toMatchObject({ w: 1500, h: 768 }));
  await userEvent.click(screen.getByRole('button', { name: 'Default size' }));
  await waitFor(() => expect(lastRender()).toMatchObject({ w: 1920, h: 1080 }));
});

it('dragging the corner handle resizes the screen, and the size is kept', async () => {
  render(<LiveView base="/preview/" version={0} />);
  await drawn('screen:scoreboard', 'twelve');
  const corner = screen.getByRole('separator', { name: 'Drag to change the size' });
  fireEvent.pointerDown(corner, { clientX: 100, clientY: 100, pointerId: 1 });
  fireEvent.pointerMove(corner, { clientX: 200, clientY: 100, pointerId: 1 });
  fireEvent.pointerUp(corner, { pointerId: 1 });
  await waitFor(() => expect(lastRender().w).toBeGreaterThan(1920));
  expect(lastRender().h).toBe(1080);
  expect(JSON.parse(localStorage.getItem('ss-mockups:view')!).w).toBe(lastRender().w);
});

it('choosing a segment draws it, and the filter narrows the list', async () => {
  render(<LiveView base="/preview/" version={0} />);
  const nav = await screen.findByRole('navigation', { name: 'UI segments' });
  await userEvent.click(within(nav).getByText('HealthPanel'));
  await drawn('panel:TF2.Client.Ui.HealthPanel', 'auto');
  await userEvent.type(within(nav).getByRole('searchbox'), 'hud');
  expect(within(nav).queryByText('HealthPanel')).toBeNull();
  expect(within(nav).getByText('Hud')).toBeTruthy();
});

it('a segment that fails to draw says why and still lists its values', async () => {
  answer = r => ({ error: 'scoreboard threw while drawing: boom', values: frameFor(r).values ?? [] });
  render(<LiveView base="/preview/" version={0} />);
  expect((await screen.findByRole('alert')).textContent).toContain('boom');
  expect(screen.getByLabelText('IScoreboardSource.ServerName')).toBeTruthy();
});

it('says what to run when nothing answers', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 404 })));
  render(<LiveView base="/preview/" version={0} />);
  expect(await screen.findByText('The live mock-ups did not answer')).toBeTruthy();
  expect(screen.getByText('make ui-preview WATCH=1')).toBeTruthy();
});

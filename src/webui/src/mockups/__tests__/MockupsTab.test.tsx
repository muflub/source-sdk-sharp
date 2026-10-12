/*
 * The Mockups tab: what it says when there is no preview, and that it shows
 * the chosen screen, sample and size, names what the pointer is over, and
 * reloads when the watcher says it rendered.
 */

import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';

import { MockupsTab } from '../MockupsTab';
import type { Element, Manifest } from '../manifest';

const manifest: Manifest = {
  generated: '2026-10-04T23:00:00Z',
  sizes: ['1920x1080', '2560x1080'],
  backdrops: [],
  fonts: [],
  samples: [
    { group: 'scoreboard', title: '12 players, two teams', slug: '12-players-two-teams', gallery: false, layer: 'Menu',
      files: {
        '1920x1080': { svg: 'screens/scoreboard/12-1920x1080.svg', json: 'screens/scoreboard/12-1920x1080.json' },
        '2560x1080': { svg: 'screens/scoreboard/12-2560x1080.svg', json: 'screens/scoreboard/12-2560x1080.json' },
      } },
    { group: 'scoreboard', title: 'empty server', slug: 'empty-server', gallery: false, layer: 'Menu',
      files: { '1920x1080': { svg: 'screens/scoreboard/empty-1920x1080.svg', json: 'screens/scoreboard/empty-1920x1080.json' } } },
    { group: 'Button', title: 'default, primary', slug: 'default-primary', gallery: true, layer: 'Menu',
      files: { '1280x720': { svg: 'gallery/Button/d.svg', json: 'gallery/Button/d.json' } } },
  ],
};

const elements: Element[] = [
  { i: 0, id: 1, parent: -1, kind: 'Box', name: 'scoreboard', x: 0, y: 0, w: 1920, h: 1080, flags: 'None' },
  { i: 1, id: 0xab, parent: 0, kind: 'Text', name: '"Alyx"', x: 0, y: 0, w: 960, h: 540, flags: 'Elided' },
];

let requested: string[] = [];
let listeners: Record<string, () => void> = {};

function respond(url: string): Response {
  const path = url.replace(/^\/preview\//, '').replace(/\?.*$/, '');
  if (path === 'manifest.json') return new Response(JSON.stringify(manifest));
  if (path.endsWith('.svg')) return new Response(`<svg data-file="${path}"></svg>`);
  if (path.endsWith('.json')) return new Response(JSON.stringify(elements));
  return new Response('', { status: 404 });
}

beforeEach(() => {
  requested = [];
  listeners = {};
  vi.stubGlobal('fetch', vi.fn(async (url: string) => { requested.push(url); return respond(url); }));
  vi.stubGlobal('EventSource', class {
    constructor(public url: string) {}
    addEventListener(name: string, fn: () => void) { listeners[name] = fn; }
    close() {}
  });
});

afterEach(() => vi.unstubAllGlobals());

it('says how to start the preview when there is none', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 404 })));
  render(<MockupsTab visible />);
  expect(await screen.findByText('No mock-ups here')).toBeTruthy();
  expect(screen.getByText('make ui-preview WATCH=1')).toBeTruthy();
});

it('shows the first sample of the first screen at 1920x1080, then the size and sample chosen', async () => {
  const { container } = render(<MockupsTab visible />);
  await waitFor(() => expect(container.querySelector('svg[data-file="screens/scoreboard/12-1920x1080.svg"]')).toBeTruthy());

  await userEvent.click(screen.getByRole('button', { name: '2560x1080' }));
  await waitFor(() => expect(container.querySelector('svg[data-file="screens/scoreboard/12-2560x1080.svg"]')).toBeTruthy());

  await userEvent.click(screen.getByRole('button', { name: 'empty server' }));
  await waitFor(() => expect(container.querySelector('svg[data-file="screens/scoreboard/empty-1920x1080.svg"]')).toBeTruthy());
});

it('names the element under the pointer', async () => {
  render(<MockupsTab visible />);
  const frame = await screen.findByTestId('mockup-frame');
  await waitFor(() => expect(requested.some(u => u.includes('12-1920x1080.json'))).toBe(true));
  frame.getBoundingClientRect = () => ({ left: 0, top: 0, width: 960, height: 540, right: 960, bottom: 540, x: 0, y: 0, toJSON: () => ({}) });

  // 100,100 on a 960-wide frame is 200,200 on the 1920 screen: inside "Alyx".
  await waitFor(() => {
    fireEvent.mouseMove(frame, { clientX: 100, clientY: 100 });
    expect(screen.getByTestId('inspector').textContent).toContain('scoreboard › "Alyx"');
  });
  expect(screen.getByTestId('inspector').textContent).toContain('id ab');
  expect(screen.getByTestId('inspector').textContent).toContain('Elided');
});

it('reloads when the watcher says it rendered', async () => {
  render(<MockupsTab visible />);
  await waitFor(() => expect(listeners.render).toBeTruthy());
  const before = requested.filter(u => u.includes('manifest.json')).length;
  listeners.render!();
  await waitFor(() => expect(requested.filter(u => u.includes('manifest.json')).length).toBe(before + 1));
  expect(requested.at(-1)).toContain('?v=1');
});

it('lists every gallery part in the gallery view', async () => {
  const { container } = render(<MockupsTab visible />);
  await userEvent.click(await screen.findByRole('tab', { name: 'Gallery' }));
  expect(await screen.findByText('Button')).toBeTruthy();
  await waitFor(() => expect(container.querySelector('svg[data-file="gallery/Button/d.svg"]')).toBeTruthy());
});

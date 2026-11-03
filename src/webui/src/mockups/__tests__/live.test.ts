/*
 * The live mock-ups' arithmetic: edits, storage, random values, grouping and
 * the frame's size.
 */

import { afterEach, beforeEach, expect, it, vi } from 'vitest';

import {
  callOf, dragSize, editsKey, filterSegments, fitZoom, groupLeaves, liveFontFaces, loadEdits, loadView, memberOf,
  originals, randomEdits, randomValue, saveEdits, seeded, setEdit, type LiveLeaf, type LiveSegment,
} from '../live';

const leaf = (key: string, type: LiveLeaf['type'], value: LiveLeaf['value'], options: string[] | null = null): LiveLeaf =>
  ({ key, source: key.split('.')[0]!, type, value, changed: false, options });

beforeEach(() => localStorage.clear());
afterEach(() => vi.restoreAllMocks());

it('an edit set back to the original value is dropped', () => {
  const edits = setEdit({}, 'S.Title', 'New', 'Old');
  expect(edits).toEqual({ 'S.Title': 'New' });
  expect(setEdit(edits, 'S.Title', 'Old', 'Old')).toEqual({});
});

it('the originals keep the unedited answers across renders', () => {
  const first = originals({}, [leaf('S.A', 'int', 1), { ...leaf('S.B', 'int', 9), changed: true }]);
  expect(first).toEqual({ 'S.A': 1 });
  expect(originals(first, [{ ...leaf('S.A', 'int', 5), changed: true }])).toEqual({ 'S.A': 1 });
});

it('edits round-trip through storage per segment and preset, and an empty set is removed', () => {
  saveEdits('screen:a', 'p', { 'S.A': 3 });
  expect(loadEdits('screen:a', 'p')).toEqual({ 'S.A': 3 });
  expect(loadEdits('screen:a', 'other')).toEqual({});
  saveEdits('screen:a', 'p', {});
  expect(localStorage.getItem(editsKey('screen:a', 'p'))).toBeNull();
});

it('corrupt or refused storage reads as nothing saved', () => {
  localStorage.setItem(editsKey('s', 'p'), '{not json');
  expect(loadEdits('s', 'p')).toEqual({});
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('refused'); });
  expect(loadEdits('s', 'p')).toEqual({});
  expect(loadView().w).toBe(1920);
});

it('the same seed gives the same random values', () => {
  const leaves = [leaf('S.Name', 'string', 'x'), leaf('S.Score', 'int', 4), leaf('S.On', 'bool', true)];
  expect(randomEdits(leaves, seeded(7))).toEqual(randomEdits(leaves, seeded(7)));
  expect(randomEdits(leaves, seeded(7))).not.toEqual(randomEdits(leaves, seeded(8)));
});

it('a random count stays small enough to read', () => {
  const rng = seeded(1);
  for (let i = 0; i < 200; i++) {
    const v = randomValue(leaf('S.PlayerCount', 'int', 1000), rng) as number;
    expect(v).toBeGreaterThanOrEqual(0);
    expect(v).toBeLessThanOrEqual(8);
  }
  expect(memberOf('S.Tags.Count')).toBe('Count');
});

it('a random enum is one of its names, and a random fraction stays a fraction', () => {
  const rng = seeded(3);
  for (let i = 0; i < 50; i++) {
    expect(['A', 'B']).toContain(randomValue(leaf('S.Mode', 'enum', 'A', ['A', 'B']), rng));
    const f = randomValue(leaf('S.Opacity', 'float', 0.5), rng) as number;
    expect(f >= 0 && f <= 1).toBe(true);
  }
});

it('random fills only the leaves not kept', () => {
  const out = randomEdits([leaf('S.A', 'int', 1), leaf('S.B', 'int', 2)], seeded(2), { 'S.A': 42 });
  expect(out['S.A']).toBe(42);
  expect(typeof out['S.B']).toBe('number');
});

it('the zoom fits the screen in the space and never enlarges it', () => {
  expect(fitZoom(1920, 1080, 960, 1080)).toBe(0.5);
  expect(fitZoom(1920, 1080, 1920, 540)).toBe(0.5);
  expect(fitZoom(640, 480, 1920, 1080)).toBe(1);
});

it('a drag resizes the screen by the travel at the zoom, held to the renderer\'s range', () => {
  expect(dragSize({ w: 1920, h: 1080 }, 50, -25, 0.5)).toEqual({ w: 2020, h: 1030 });
  expect(dragSize({ w: 200, h: 200 }, -1000, -1000, 1)).toEqual({ w: 160, h: 120 });
});

it('a leaf\'s call is its key up to the first dot outside brackets', () => {
  expect(callOf('Player(2).Name')).toBe('Player(2)');
  expect(callOf('Weapon(1,"a.b").box.Name')).toBe('Weapon(1,"a.b")');
  expect(callOf('Tags[0]')).toBe('Tags[0]');
  expect(callOf('Title')).toBe('Title');
});

it('leaves group by interface, then by call, in the order read', () => {
  const groups = groupLeaves([leaf('S.Player(0).Name', 'string', 'a'), leaf('S.Title', 'string', 't'),
    leaf('S.Player(0).Frags', 'int', 1), leaf('T.On', 'bool', true)]);
  expect([...groups.keys()]).toEqual(['S', 'T']);
  expect([...groups.get('S')!.keys()]).toEqual(['Player(0)', 'Title']);
  expect(groups.get('S')!.get('Player(0)')!.map(l => l.key)).toEqual(['S.Player(0).Name', 'S.Player(0).Frags']);
});

it('the segment filter matches every word in the name, id or assembly', () => {
  const segs: LiveSegment[] = [
    { id: 'screen:scoreboard', name: 'Scoreboard', kind: 'screen', assembly: 'SourceSharp.Client.Ui', presets: [] },
    { id: 'panel:TF2.Client.Ui.HealthPanel', name: 'HealthPanel', kind: 'panel', assembly: 'TF2.Client.Ui', presets: [] },
  ];
  expect(filterSegments(segs, '').length).toBe(2);
  expect(filterSegments(segs, 'tf2 health').map(s => s.name)).toEqual(['HealthPanel']);
  expect(filterSegments(segs, 'tf2 score')).toEqual([]);
});

it('the fonts load from their bytes', () => {
  expect(liveFontFaces({ segments: [], sizes: [], fonts: [{ family: 'Lato', weight: 700, data: 'AAEC' }] }))
    .toBe('@font-face { font-family: "Lato"; font-weight: 700; src: url("data:font/ttf;base64,AAEC") format("truetype"); }');
});

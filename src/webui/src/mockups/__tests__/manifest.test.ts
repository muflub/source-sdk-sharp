/*
 * The Mockups tab's arithmetic: grouping, sizes, which picture, and which
 * element is under the pointer.
 */

import { describe, expect, it } from 'vitest';

import { elementAt, fontFaces, groups, parseSize, pathOf, pngFor, previewBase, sizesOf, type Element, type Manifest } from '../manifest';

const manifest: Manifest = {
  generated: '2026-10-04T23:00:00Z',
  sizes: ['1024x768', '1920x1080', '2560x1080'],
  backdrops: ['backdrops/busy.jpg'],
  fonts: [{ family: 'IBM Plex Sans Condensed', weight: 700, file: 'fonts/IBMPlexSansCondensed-700.ttf' }],
  samples: [
    { group: 'scoreboard', title: '12 players', slug: '12-players', gallery: false, layer: 'Menu',
      files: { '2560x1080': { svg: 'a.svg', json: 'a.json' }, '1920x1080': { svg: 'b.svg', json: 'b.json', png: 'b.png', 'png-dark': 'b-dark.png' } } },
    { group: 'hud', title: 'full health', slug: 'full-health', gallery: false, layer: 'Hud', files: {} },
    { group: 'scoreboard', title: 'empty', slug: 'empty', gallery: false, layer: 'Menu', files: {} },
    { group: 'Button', title: 'default', slug: 'default', gallery: true, layer: 'Menu', files: { '1280x720': { svg: 'g.svg', json: 'g.json' } } },
  ],
};

describe('groups', () => {
  it('splits screens from gallery parts and keeps manifest order within a group', () => {
    const screens = groups(manifest, false);
    expect([...screens.keys()]).toEqual(['scoreboard', 'hud']);
    expect(screens.get('scoreboard')?.map(s => s.slug)).toEqual(['12-players', 'empty']);
    expect([...groups(manifest, true).keys()]).toEqual(['Button']);
  });
});

describe('sizes and pictures', () => {
  it('orders a sample\'s sizes as the manifest does, keeping sizes it alone has', () => {
    expect(sizesOf(manifest.samples[0]!, manifest.sizes)).toEqual(['1920x1080', '2560x1080']);
    expect(sizesOf(manifest.samples[3]!, manifest.sizes)).toEqual(['1280x720']);
  });

  it('parses a size and refuses anything else', () => {
    expect(parseSize('3440x1440')).toEqual([3440, 1440]);
    expect(parseSize('1080x1920@1.75')).toEqual([1080, 1920]);
    expect(parseSize('wide')).toBeNull();
  });

  it('picks the PNG drawn over the chosen still, or none', () => {
    const files = manifest.samples[0]!.files['1920x1080']!;
    expect(pngFor(files, 'busy')).toBe('b.png');
    expect(pngFor(files, 'dark')).toBe('b-dark.png');
    expect(pngFor(files, 'bright')).toBeNull();
    expect(pngFor(files, 'none')).toBeNull();
  });

  it('serves from /preview/ unless the address names another', () => {
    expect(previewBase('')).toBe('/preview/');
    expect(previewBase('?preview=http://127.0.0.1:7191/preview')).toBe('http://127.0.0.1:7191/preview/');
  });

  it('loads every font the layout measured with', () => {
    expect(fontFaces(manifest, '/preview/')).toBe(
      '@font-face { font-family: "IBM Plex Sans Condensed"; font-weight: 700; src: url("/preview/fonts/IBMPlexSansCondensed-700.ttf") format("truetype"); }');
  });
});

describe('elementAt', () => {
  const els: Element[] = [
    { i: 0, id: 1, parent: -1, kind: 'Box', name: 'scoreboard', x: 0, y: 0, w: 1920, h: 1080, flags: 'None' },
    { i: 1, id: 2, parent: 0, kind: 'Stack', name: 'TeamTable[team0]', x: 100, y: 100, w: 600, h: 400, flags: 'None' },
    { i: 2, id: 3, parent: 1, kind: 'Text', name: '"Alyx"', x: 120, y: 140, w: 80, h: 24, flags: 'None' },
    { i: 3, id: 4, parent: 1, kind: 'Spacer', name: 'Spacer', x: 0, y: 0, w: 0, h: 0, flags: 'None' },
  ];

  it('names the smallest element under the point, not the panel it sits on', () => {
    expect(elementAt(els, 130, 150)?.id).toBe(3);
    expect(elementAt(els, 400, 400)?.id).toBe(2);
    expect(elementAt(els, 1800, 1000)?.id).toBe(1);
  });

  it('finds nothing off every element, and never an empty one', () => {
    expect(elementAt(els, 5000, 5000)).toBeNull();
    expect(elementAt(els, 0, 0)?.id).toBe(1);
  });

  it('spells the ancestry without the anonymous containers', () => {
    expect(pathOf(els, els[2]!)).toBe('scoreboard › TeamTable[team0] › "Alyx"');
  });
});

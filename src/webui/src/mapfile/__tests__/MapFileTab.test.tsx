/*
 * The map file tab.
 *
 * What is worth pinning down here is the navigation, because it is the unusual
 * part: the file's own lump table is the index, so choosing a lump is how you
 * read anything at all, and a lump with nothing decoded behind it has to say so
 * rather than look broken.
 *
 * The export is the other half. It is the reason the tab exists rather than a
 * convenience on top of it, and the property that matters is that what comes out
 * is what is on screen.
 */

import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { MapFileTab } from '../MapFileTab';
import type { MapFile, MapLump } from '../../types';

/*
 * The lump list's row for one lump: the one body row of the list whose text
 * matches, and exactly one, as a getBy query insists.
 *
 * Not getByRole('row', { name }), which these tests used. A role query computes
 * the accessible name of every row on the page (a row's whole text, through
 * dom-accessibility-api), and once a lump is open its own table's rows are on
 * the page too: 0.1-0.3 s a query here, and the two tests that make three of
 * them went past vitest's 5 s budget on a loaded CI runner. A row's name IS
 * its text, so matching the text picks the same row; scoping it to the list
 * keeps an opened lump's rows out of it.
 */
function lumpRow(name: RegExp): HTMLElement {
  const list = document.querySelector<HTMLElement>('table.lump-list');

  if (!list) throw new Error('no lump list on the page');

  const rows = Array.from(list.querySelectorAll<HTMLElement>('tbody tr'))
    .filter(row => name.test(row.textContent ?? ''));

  if (rows.length !== 1) throw new Error(`${rows.length} lump rows match ${name}`);

  return rows[0]!;
}

vi.mock('../../api', () => ({
  getMap: vi.fn(),
  getMapFile: vi.fn(),
  getMapEntities: vi.fn(),
  getMapLump: vi.fn(),
}));

vi.mock('../../map/download', () => ({ save: vi.fn() }));

const api = await import('../../api') as unknown as {
  getMap: ReturnType<typeof vi.fn>;
  getMapFile: ReturnType<typeof vi.fn>;
  getMapEntities: ReturnType<typeof vi.fn>;
  getMapLump: ReturnType<typeof vi.fn>;
};

const download = (await import('../../map/download')).save as unknown as ReturnType<typeof vi.fn>;

const lump = (index: number, name: string, over: Partial<MapLump> = {}): MapLump => ({
  index,
  name,
  offset: 1036,
  length: 0,
  version: 0,
  uncompressedSize: 0,
  stride: 0,
  records: -1,
  note: '',
  ...over,
});

/** A lump answer with nothing in it, which is what most of them are. */
const emptyLump = {
  map: 'ss_sandbox', lump: 0, name: '', known: false,
  faces: [], texinfos: [], leaves: [], brushes: [], brushsides: [],
};

const file: MapFile = {
  map: 'ss_sandbox',
  path: '/mod/maps/ss_sandbox.bsp',
  bytes: 6408692,
  ident: 'VBSP',
  version: 20,
  revision: 1,
  unaccounted: 21,
  visClusters: 227,
  visAverage: 225,
  visMax: 227,
  occluders: 0,
  levelFlags: '',
  entityCount: 383,
  connectionCount: 91,
  warnings: [],

  lumps: [
    lump(0, 'ENTITIES', { length: 4000 }),
    lump(1, 'PLANES', { length: 200, stride: 20, records: 10 }),
    lump(2, 'TEXDATA', { length: 64, stride: 32, records: 2 }),
    lump(3, 'VERTEXES'),
    lump(4, 'VISIBILITY', { length: 500, note: 'compressed visibility.' }),
    lump(5, 'NODES'),
    lump(6, 'TEXINFO'),
    lump(7, 'FACES', { length: 112, stride: 56, records: 2 }),
  ],

  gameLumps: [],
  classes: [{ classname: 'light', count: 12 }, { classname: 'worldspawn', count: 1 }],

  materials: [
    {
      index: 0, name: 'dev/dev_measurewall01a', width: 512, height: 512,
      viewWidth: 512, viewHeight: 512, faces: 350, area: 9892256, luxels: 181105,
      reflectivity: [0.5, 0.5, 0.5],
    },
    {
      index: 1, name: 'tools/toolstrigger', width: 64, height: 64,
      viewWidth: 64, viewHeight: 64, faces: 0, area: 0, luxels: 0,
      reflectivity: [0, 0, 0],
    },
  ],

  materialNames: ['dev/dev_measurewall01a', 'tools/toolstrigger'],
  propModels: [],
  props: [],
  cubemaps: [],
  worldlights: [],
  overlays: [],
  displacements: [],
  pak: [],
  models: [],
  areas: [],
  areaportals: [],
  water: [],
  detailModels: [],
};

beforeEach(() => {
  vi.clearAllMocks();
  history.replaceState(null, '', '/');

  api.getMap.mockResolvedValue({ current: 'ss_sandbox', available: ['ss_sandbox', 'dm_lockdown'] });
  api.getMapFile.mockResolvedValue(file);
  api.getMapEntities.mockResolvedValue({ entities: [] });
  api.getMapLump.mockResolvedValue(emptyLump);
});

/** Renders the tab and waits for the first read to land. */
async function show(): Promise<void> {
  render(<MapFileTab visible />);
  await screen.findByText('ss_sandbox.bsp');
}

/*
 * Everything before the file is read, and the read itself.
 */
describe('reading a map', () => {
  it('shows what the file is', async () => {
    await show();

    expect(screen.getByText('ss_sandbox.bsp')).toBeTruthy();
    expect(screen.getByText('VBSP 20')).toBeTruthy();
    expect(screen.getByText('6.1 MB')).toBeTruthy();
  });

  /*
   * Zero clusters means the map was compiled without vis and is drawing every
   * surface at once, which is the commonest reason a small map runs badly — so
   * it is said in words rather than shown as a zero.
   */
  it('says so in words when a map has no visibility', async () => {
    api.getMapFile.mockResolvedValue({ ...file, visClusters: 0 });
    await show();

    expect(screen.getByText(/compiled without vis/)).toBeTruthy();
  });

  it('reads the file once rather than on every showing', async () => {
    await show();

    expect(api.getMapFile).toHaveBeenCalledTimes(1);
  });

  it('says what went wrong rather than showing an empty page', async () => {
    api.getMapFile.mockResolvedValue({ error: 'no such map' });
    render(<MapFileTab visible />);

    expect((await screen.findByRole('alert')).textContent).toContain('no such map');
  });

  /* The decoders report what they could not read; dropping it would leave rows
     silently missing from a view whose whole job is completeness. */
  it('shows what the file could not read', async () => {
    api.getMapFile.mockResolvedValue({
      ...file,
      warnings: ['lump 7 (FACES) claims 1048576 bytes at 1048576'],
    });

    render(<MapFileTab visible />);

    expect((await screen.findByRole('alert')).textContent).toContain('FACES');
  });
});

describe('the lump table', () => {
  it('lists the lumps with their sizes and record counts', async () => {
    await show();

    const planes = lumpRow(/planes/);

    expect(within(planes).getByText('200 B')).toBeTruthy();
    expect(within(planes).getByText('10')).toBeTruthy();
  });

  /* A lump with no fixed record cannot report a count, and reporting zero would
     read as "this map has no entities". */
  it('shows no count for a lump that is not an array of records', async () => {
    await show();

    const entities = lumpRow(/entities/);

    expect(within(entities).getAllByText('—').length).toBeGreaterThan(0);
  });

  /*
   * Two filters that mean different things: what this map does not have, and
   * what this tool cannot show. Hiding one must not hide the other.
   */
  it('hides the lumps nothing here can read', async () => {
    await show();

    expect(lumpRow(/visibility/)).toBeTruthy();

    await userEvent.click(screen.getByLabelText(/readable only/));

    expect(screen.queryByRole('row', { name: /visibility/ })).toBeNull();
    expect(lumpRow(/texdata/)).toBeTruthy();

    // The big lumps count as readable even before anybody has opened one.
    expect(lumpRow(/faces/)).toBeTruthy();
  });

  it('says how many are left rather than letting a short list pass for the file', async () => {
    await show();
    await userEvent.click(screen.getByLabelText(/readable only/));

    expect(screen.getByText(/showing \d+ of 8/)).toBeTruthy();
  });

  it('keeps the two filters apart', async () => {
    await show();

    await userEvent.click(screen.getByLabelText(/readable only/));
    await userEvent.click(screen.getByLabelText(/empty ones/));

    // Empty and readable: shown, because only the undecodable ones were hidden.
    expect(lumpRow(/texinfo/)).toBeTruthy();
    expect(screen.queryByRole('row', { name: /visibility/ })).toBeNull();
  });

  it('hides the empty lumps until asked for them', async () => {
    await show();

    expect(screen.queryByRole('row', { name: /vertexes/ })).toBeNull();

    await userEvent.click(screen.getByLabelText(/empty ones/));

    expect(lumpRow(/vertexes/)).toBeTruthy();
  });
});

describe('reading a lump', () => {
  it('starts on what the map has in it, rather than on nothing', async () => {
    await show();

    expect(screen.getByText('What this map has in it')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Materials' })).toBeTruthy();
  });

  it('opens a lump from the contents', async () => {
    await show();
    await userEvent.click(screen.getByRole('button', { name: 'Materials' }));

    expect(screen.getByRole('columnheader', { name: /Material/ })).toBeTruthy();
    expect(screen.getByText('dev/dev_measurewall01a')).toBeTruthy();
  });

  it('opens a lump by clicking its row', async () => {
    await show();
    await userEvent.click(lumpRow(/texdata/));

    expect(screen.getByRole('heading', { name: /2\. texdata/ })).toBeTruthy();
  });

  /*
   * Most of a BSP is data the engine is handed and nothing else reads. Saying
   * that is the honest answer, and it is a different answer from "this lump is
   * empty" — which the same pane also has to give.
   */
  it('says why a lump has no table rather than showing an empty one', async () => {
    await show();
    await userEvent.click(lumpRow(/visibility/));

    expect(screen.getByText(/Nothing here decodes this lump/)).toBeTruthy();
  });

  it('closes a lump by clicking it again', async () => {
    await show();

    await userEvent.click(lumpRow(/texdata/));
    await userEvent.click(lumpRow(/texdata/));

    expect(screen.getByText('What this map has in it')).toBeTruthy();
  });
});

describe('exporting', () => {
  it('hands over the whole report as JSON', async () => {
    await show();
    await userEvent.click(screen.getByRole('button', { name: 'Export all' }));

    const [text, name, type] = download.mock.calls[0]!;

    expect(name).toBe('ss_sandbox.bsp.json');
    expect(type).toBe('application/json');
    expect(JSON.parse(text as string).map).toBe('ss_sandbox');
  });

  it('hands over one section as a spreadsheet', async () => {
    await show();
    await userEvent.click(screen.getByRole('button', { name: 'Materials' }));
    await userEvent.click(screen.getByRole('button', { name: 'CSV' }));

    const [text, name, type] = download.mock.calls[0]!;

    expect(name).toBe('materials.csv');
    expect(type).toBe('text/csv');
    expect(text).toContain('dev/dev_measurewall01a');
    expect(text).toContain('tools/toolstrigger');
  });

  /*
   * The one that would be wrong invisibly: somebody filters a table down to what
   * they care about and exports it, and gets a file of everything. What is on
   * screen is what "this" means.
   */
  it('exports what the filter left, not what was read', async () => {
    await show();
    await userEvent.click(screen.getByRole('button', { name: 'Materials' }));
    await userEvent.type(screen.getByPlaceholderText('filter rows'), 'toolstrigger');

    expect(screen.queryByText('dev/dev_measurewall01a')).toBeNull();

    await userEvent.click(screen.getByRole('button', { name: 'CSV' }));

    const [text, name] = download.mock.calls[0]!;

    expect(text).toContain('tools/toolstrigger');
    expect(text).not.toContain('dev_measurewall01a');

    // Named so that a directory of exports does not turn into a directory of
    // files that all claim to be the whole thing.
    expect(name).toBe('materials-filtered.csv');
  });
});

/*
 * The lumps that are read on their own.
 *
 * The behaviour worth pinning down is when the request happens: sending faces
 * and brush sides with the report would be megabytes paid by everybody who
 * wanted to look at something small, and the whole design rests on that request
 * being made only when somebody opens the lump.
 */
describe('reading a big lump', () => {
  const faces = {
    ...emptyLump,
    lump: 7,
    name: 'FACES',
    known: true,
    faces: [{
      index: 0, material: 'metal/wall062a', surface: 'bumplight', area: 512,
      lightmapWidth: 4, lightmapHeight: 5, luxels: 80, styles: 2, texinfo: 1,
      plane: 12, side: true, onNode: true, firstEdge: 300, edges: 4,
      dispInfo: -1, origFace: 77, primitives: 2,
    }],
  };

  it('does not ask for one until it is opened', async () => {
    await show();

    expect(api.getMapLump).not.toHaveBeenCalled();
  });

  it('asks for it when it is opened, and shows what came back', async () => {
    api.getMapLump.mockResolvedValue(faces);

    await show();
    await userEvent.click(lumpRow(/faces/));

    expect(await screen.findByText('metal/wall062a')).toBeTruthy();
    expect(api.getMapLump).toHaveBeenCalledWith('ss_sandbox', 7);
  });

  it('asks once, however often the lump is opened', async () => {
    api.getMapLump.mockResolvedValue(faces);

    await show();
    await userEvent.click(lumpRow(/faces/));
    await screen.findByText('metal/wall062a');

    await userEvent.click(lumpRow(/texdata/));
    await userEvent.click(lumpRow(/faces/));
    await screen.findByText('metal/wall062a');

    expect(api.getMapLump).toHaveBeenCalledTimes(1);
  });

  it('exports what it read, like every other table', async () => {
    api.getMapLump.mockResolvedValue(faces);

    await show();
    await userEvent.click(lumpRow(/faces/));
    await screen.findByText('metal/wall062a');

    await userEvent.click(screen.getByRole('button', { name: 'CSV' }));

    const [text, name] = download.mock.calls[0]!;

    expect(name).toBe('faces-7.csv');
    expect(text).toContain('metal/wall062a');

    // The lightmap columns are the reason to read this lump at all.
    expect(text).toContain('Luxels');
  });

  /* A failed read leaves the lump unread rather than caching an empty answer,
     so leaving it and coming back tries again. */
  it('does not remember a read that failed', async () => {
    api.getMapLump.mockRejectedValue(new Error('gone'));

    await show();
    await userEvent.click(lumpRow(/faces/));
    await userEvent.click(lumpRow(/texdata/));
    await userEvent.click(lumpRow(/faces/));

    expect(api.getMapLump.mock.calls.length).toBeGreaterThan(1);
  });
});

describe('what the map costs', () => {
  /*
   * The number that decides whether a map runs, and the one important fact about
   * a compiled map that is nowhere in the file — the visibility lump holds the
   * visible sets rather than their sizes.
   */
  it('shows how much of the map an average place in it can see', async () => {
    await show();

    expect(screen.getByText(/225 of 227/)).toBeTruthy();
    expect(screen.getByText(/worst 227/)).toBeTruthy();
  });

  it('says nothing about visibility when there is none to measure', async () => {
    api.getMapFile.mockResolvedValue({ ...file, visClusters: 0, visAverage: -1, visMax: -1 });
    await show();

    expect(screen.queryByText(/worst/)).toBeNull();
  });

  it('shows what the level was compiled with, when it says', async () => {
    api.getMapFile.mockResolvedValue({
      ...file,
      levelFlags: 'static prop lighting baked (HDR)',
    });

    await show();

    expect(screen.getByText(/static prop lighting baked/)).toBeTruthy();
  });
});

/*
 * Turning a map file into tables.
 *
 * The properties worth pinning down are the ones a reader cannot check for
 * themselves: that a section belongs to the lump it was decoded from, that a
 * vector arrives as three sortable columns rather than one unusable one, and
 * above all that what comes out of an export is what was on screen — a CSV that
 * quietly dropped the filter, or stopped where the page did, would be wrong in a
 * way nothing visible would show.
 */

import { describe, expect, it } from 'vitest';

import {
  bytes, csvCell, detailSections, filterRows, sectionsForLump, sectionsOf, sortRows, toCsv,
} from '../sections';
import type { MapEntity, MapFile, MapLumpDetail } from '../../types';

const noLump: MapLumpDetail = {
  map: 'ss_sandbox', lump: 0, name: '', known: true,
  faces: [], texinfos: [], leaves: [], brushes: [], brushsides: [],
};

const empty: MapFile = {
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
  entityCount: 0,
  connectionCount: 0,
  warnings: [],
  lumps: [],
  gameLumps: [],
  classes: [],
  materials: [],
  materialNames: [],
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

const file = (over: Partial<MapFile>): MapFile => ({ ...empty, ...over });

describe('a map with nothing in it', () => {
  it('has no sections at all', () => {
    expect(sectionsOf(empty)).toEqual([]);
  });

  /*
   * The lump table beside this view lists every lump including the empty ones,
   * so a page of empty tables would be the same information twice — and the
   * second time as noise.
   */
  it('leaves out a section rather than showing it empty', () => {
    const one = sectionsOf(file({ cubemaps: [{ origin: [0, 0, 0], size: 1 }] }));

    expect(one.map(section => section.id)).toEqual(['cubemaps']);
  });
});

describe('sections', () => {
  const props = file({
    propModels: ['models/crate.mdl', 'models/drum.mdl'],
    props: [
      {
        index: 0, model: 'models/crate.mdl', origin: [128, -64, 32.5], angles: [0, 270, 0],
        skin: 3, fadeMin: 400, fadeMax: 900, solid: 6, flags: 0x11, firstLeaf: 7, leafCount: 4,
      },
      {
        index: 1, model: 'models/crate.mdl', origin: [0, 0, 0], angles: [0, 0, 0],
        skin: 0, fadeMin: 0, fadeMax: 0, solid: 0, flags: 0, firstLeaf: 0, leafCount: 1,
      },
    ],
  });

  const sections = sectionsOf(props);

  it('knows which lump each came out of', () => {
    expect(sections.every(section => section.lump === 35)).toBe(true);
    expect(sectionsForLump(sections, 35).map(s => s.id)).toEqual(['propModels', 'props']);
    expect(sectionsForLump(sections, 0)).toEqual([]);
  });

  /*
   * One cell reading "[128, -64, 32.5]" cannot be sorted, cannot be filtered on
   * a single axis, and lands in a spreadsheet as one string. Three columns is
   * what it costs to have it be data.
   */
  it('spreads a vector into three columns', () => {
    const section = sections.find(s => s.id === 'props')!;

    expect(section.columns.map(column => column.key)).toContain('origin0');
    expect(section.rows[0]).toMatchObject({ origin0: 128, origin1: -64, origin2: 32.5 });
    expect(section.rows[0]).toMatchObject({ angles1: 270 });
  });

  it('counts how many of each model the map places', () => {
    const models = sections.find(s => s.id === 'propModels')!;

    expect(models.rows).toEqual([
      { index: 0, model: 'models/crate.mdl', count: 2 },
      { index: 1, model: 'models/drum.mdl', count: 0 },
    ]);
  });

  /* Flags are a bit field, and the engine's own headers write them in hex. */
  it('writes flags the way they are declared', () => {
    expect(sections.find(s => s.id === 'props')!.rows[0]!['flags']).toBe('0x11');
  });

  it('names a cubemap after the texture vrad bakes for it', () => {
    const [cubemaps] = sectionsOf(file({ cubemaps: [{ origin: [-2437, 4024, 182], size: 0 }] }));

    expect(cubemaps!.rows[0]!['texture']).toBe('c-2437_4024_182.vtf');
  });

  it('works out how big each brush model is', () => {
    const [models] = sectionsOf(file({
      models: [{ mins: [-512, -256, 0], maxs: [512, 256, 128], origin: [0, 0, 0] }],
    }));

    expect(models!.rows[0]).toMatchObject({ model: 'worldspawn', size0: 1024, size2: 128 });
  });

  it('takes the entity list from the caller, since the report has only a count', () => {
    const entities: MapEntity[] = [{
      id: 0, classname: 'func_door', targetname: 'gate', model: '*1', brushModel: 1,
      origin: [0, 0, 0], angles: [0, 0, 0],
      keyvalues: [{ key: 'classname', value: 'func_door' }],
      connections: [{ output: 'OnOpen', target: 'x', input: 'Kill', param: '', delay: 0, times: -1 }],
    }];

    const [list] = sectionsOf(empty, entities);

    expect(list!.id).toBe('entities');
    expect(list!.lump).toBe(0);
    expect(list!.rows[0]).toMatchObject({ classname: 'func_door', keys: 1, connections: 1 });
  });
});

describe('filtering', () => {
  const rows = [
    { name: 'concrete/floor01', faces: 12 },
    { name: 'tools/toolsnodraw', faces: 3 },
    { name: 'metal/wall062a', faces: 120 },
  ];

  /*
   * Every cell rather than a named column: what somebody types here is a name or
   * a number they saw somewhere else, and asking which column it was in first
   * would be asking them to already know the answer.
   */
  it('matches any cell', () => {
    expect(filterRows(rows, 'tools')).toHaveLength(1);
    expect(filterRows(rows, '12')).toHaveLength(2);
  });

  it('ignores case and surrounding space', () => {
    expect(filterRows(rows, '  METAL ')).toHaveLength(1);
  });

  it('an empty filter is not a filter', () => {
    expect(filterRows(rows, '   ')).toHaveLength(3);
  });
});

describe('ordering', () => {
  const rows = [{ n: 9, s: 'b' }, { n: 100, s: 'a' }, { n: 10, s: 'c' }];

  /* Offsets and sizes sorted as text put 100 before 9, every time. */
  it('sorts a numeric column as numbers', () => {
    const sorted = sortRows(rows, { key: 'n', label: 'N', numeric: true }, false);

    expect(sorted.map(row => row['n'])).toEqual([9, 10, 100]);
  });

  it('sorts a text column as text', () => {
    const sorted = sortRows(rows, { key: 's', label: 'S' }, false);

    expect(sorted.map(row => row['s'])).toEqual(['a', 'b', 'c']);
  });

  it('turns around', () => {
    const sorted = sortRows(rows, { key: 'n', label: 'N', numeric: true }, true);

    expect(sorted.map(row => row['n'])).toEqual([100, 10, 9]);
  });

  it('leaves the rows alone when no column is chosen', () => {
    expect(sortRows(rows, undefined, false).map(row => row['n'])).toEqual([9, 100, 10]);
  });

  it('does not reorder the rows it was given', () => {
    const original = [...rows];
    sortRows(rows, { key: 'n', label: 'N', numeric: true }, false);

    expect(rows).toEqual(original);
  });
});

describe('exporting', () => {
  const columns = [
    { key: 'name', label: 'Material' },
    { key: 'faces', label: 'Faces', numeric: true },
  ];

  it('writes a header and a row per line', () => {
    const csv = toCsv(columns, [{ name: 'metal/wall', faces: 3 }]);

    expect(csv).toBe('Material,Faces\nmetal/wall,3\n');
  });

  it('takes the columns in the order the table shows them', () => {
    expect(toCsv([...columns].reverse(), [{ name: 'a', faces: 1 }]))
      .toBe('Faces,Material\n1,a\n');
  });

  /*
   * A material name has no commas in it until the one that does, and a CSV that
   * is wrong in a single row imports silently misaligned.
   */
  it('quotes a value that would otherwise break the row', () => {
    expect(csvCell('plain')).toBe('plain');
    expect(csvCell('a,b')).toBe('"a,b"');
    expect(csvCell('say "hi"')).toBe('"say ""hi"""');
    expect(csvCell('two\nlines')).toBe('"two\nlines"');
  });

  it('writes an empty cell for a column a row does not have', () => {
    expect(toCsv(columns, [{ name: 'only' }])).toBe('Material,Faces\nonly,\n');
  });
});

describe('reading sizes', () => {
  it('says bytes, kilobytes and megabytes', () => {
    expect(bytes(512)).toBe('512 B');
    expect(bytes(2048)).toBe('2.0 kB');
    expect(bytes(6408692)).toBe('6.1 MB');
  });
});

describe('what a material costs', () => {
  const lit = file({
    materials: [
      {
        index: 0, name: 'plaster/wall034a', width: 512, height: 512,
        viewWidth: 512, viewHeight: 512, faces: 472, area: 4391185, luxels: 75,
        reflectivity: [0.5, 0.5, 0.5],
      },
      {
        index: 1, name: 'tools/toolsskybox', width: 512, height: 512,
        viewWidth: 512, viewHeight: 512, faces: 182, area: 12086974, luxels: 25,
        reflectivity: [0, 0, 0],
      },
    ],
  });

  const [materials] = sectionsOf(lit);

  /*
   * Neither of these is in the lump: both are counted by walking the faces, and
   * together they are the answer to "what is this map made of, and what did it
   * cost" — which is the question that gets a map file opened.
   */
  it('carries the area built out of each material', () => {
    expect(materials!.rows[0]).toMatchObject({ area: 4391185, luxels: 75 });
  });

  it('shows the lightmap as a share of the map, not as a raw count', () => {
    expect(materials!.rows[0]!['share']).toBe(75);
    expect(materials!.rows[1]!['share']).toBe(25);
  });

  /* A map with no lighting at all must not divide by its own zero. */
  it('survives a map with no lightmap', () => {
    const [dark] = sectionsOf(file({
      materials: [{
        index: 0, name: 'x', width: 1, height: 1, viewWidth: 1, viewHeight: 1,
        faces: 1, area: 0, luxels: 0, reflectivity: [0, 0, 0],
      }],
    }));

    expect(dark!.rows[0]!['share']).toBe(0);
  });
});

describe('the small lumps', () => {
  it('reads the areas a map is cut into', () => {
    const [areas] = sectionsOf(file({ areas: [{ index: 0, firstPortal: 0, portals: 3 }] }));

    expect(areas!.lump).toBe(20);
    expect(areas!.rows[0]).toMatchObject({ portals: 3 });
  });

  it('reads the areaportals, by the number an entity binds to', () => {
    const [portals] = sectionsOf(file({
      areaportals: [{ index: 0, key: 7, otherArea: 2, plane: 91, firstVertex: 0, vertices: 4 }],
    }));

    expect(portals!.rows[0]).toMatchObject({ key: 7, otherArea: 2 });
  });

  it('reads the water, with the material on it', () => {
    const [water] = sectionsOf(file({
      water: [{ index: 0, surfaceZ: 128, minZ: -64, texinfo: 3, material: 'water/canal' }],
    }));

    expect(water!.rows[0]).toMatchObject({ material: 'water/canal', surfaceZ: 128 });
  });

  it('reads the detail prop models', () => {
    const [detail] = sectionsOf(file({ detailModels: [{ index: 0, name: 'models/grass.mdl' }] }));

    expect(detail!.lump).toBe(35);
    expect(detail!.rows[0]).toMatchObject({ name: 'models/grass.mdl' });
  });
});

describe('a lump read on its own', () => {
  it('turns faces into a table with the lightmap in it', () => {
    const [faces] = detailSections({
      ...noLump,
      lump: 7,
      name: 'FACES',
      faces: [{
        index: 0, material: 'metal/wall062a', surface: 'bumplight', area: 512,
        lightmapWidth: 4, lightmapHeight: 5, luxels: 80, styles: 2, texinfo: 1,
        plane: 12, side: true, onNode: true, firstEdge: 300, edges: 4,
        dispInfo: -1, origFace: 77, primitives: 2,
      }],
    });

    expect(faces!.lump).toBe(7);
    expect(faces!.title).toBe('Faces');

    // The two lightmap axes go in one cell: they only mean anything together,
    // and nobody sorts a map by the height of its lightmaps.
    expect(faces!.rows[0]).toMatchObject({ lightmap: '4×5', luxels: 80, surface: 'bumplight' });
  });

  /* Three lumps are arrays of the same record, so the title has to come from
     the lump rather than from the shape of the rows. */
  it('names itself after the lump it came from', () => {
    const [original] = detailSections({
      ...noLump, lump: 27, name: 'ORIGINALFACES',
      faces: [{
        index: 0, material: '', surface: '', area: 0, lightmapWidth: 1, lightmapHeight: 1,
        luxels: 0, styles: 0, texinfo: 0, plane: 0, side: false, onNode: false,
        firstEdge: 0, edges: 0, dispInfo: -1, origFace: -1, primitives: 0,
      }],
    });

    expect(original!.title).toBe('Originalfaces');
    expect(original!.id).toBe('faces-27');
  });

  it('turns surfaces into a table with the lightmap scale in it', () => {
    const [infos] = detailSections({
      ...noLump, lump: 6, name: 'TEXINFO',
      texinfos: [{
        index: 0, material: 'tools/toolsnodraw', surface: 'nodraw skip',
        lightmapScale: 16, texdata: 3,
      }],
    });

    expect(infos!.rows[0]).toMatchObject({ lightmapScale: 16, surface: 'nodraw skip' });
  });

  it('turns leaves into a table, bounds and all', () => {
    const [leaves] = detailSections({
      ...noLump, lump: 10, name: 'LEAFS',
      leaves: [{
        index: 0, cluster: -1, area: 3, contents: 'solid playerclip',
        firstFace: 11, faces: 4, firstBrush: 7, brushes: 2, water: -1,
        mins: [-128, -64, 0], maxs: [128, 64, 96],
      }],
    });

    expect(leaves!.rows[0]).toMatchObject({ cluster: -1, contents: 'solid playerclip', mins0: -128 });
  });

  it('turns brushes and their sides into tables', () => {
    const [brushes] = detailSections({
      ...noLump, lump: 18, name: 'BRUSHES',
      brushes: [{ index: 0, contents: 'playerclip detail', firstSide: 6, sides: 4 }],
    });

    expect(brushes!.rows[0]).toMatchObject({ contents: 'playerclip detail', sides: 4 });

    const [sides] = detailSections({
      ...noLump, lump: 19, name: 'BRUSHSIDES',
      brushsides: [
        { index: 0, material: 'tools/toolsclip', plane: 31, texinfo: 0, dispInfo: -1, bevel: false },
        { index: 1, material: 'tools/toolsclip', plane: 5, texinfo: 0, dispInfo: -1, bevel: true },
      ],
    });

    // A word rather than "true": the column is scanned, not computed with.
    expect(sides!.rows[0]!['bevel']).toBe('');
    expect(sides!.rows[1]!['bevel']).toBe('yes');
  });

  it('has nothing to say about a lump that came back empty', () => {
    expect(detailSections(noLump)).toEqual([]);
  });
});

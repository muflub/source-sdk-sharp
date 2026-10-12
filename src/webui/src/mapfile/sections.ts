/*
 * The map file, turned into tables.
 *
 * Every decoded lump becomes a section: a title, a set of columns, and rows of
 * plain values. One shape for all of them, which is what makes the rest of this
 * tab small — one table component, one filter, one sort, and an export that
 * works the same everywhere because there is only one thing to export.
 *
 * Rows are flat records of strings and numbers rather than the shapes the API
 * sends. A vector becomes an x, a y and a z, because a column of "[-64, 128,
 * 32]" cannot be sorted, cannot be filtered usefully, and lands in a spreadsheet
 * as one useless cell. The point of this view is that the file can be read and
 * taken somewhere else, and three columns is what that costs.
 *
 * Sections are keyed to the lump they came out of. The lump table is the
 * navigation for this tab, so a section that did not know where it came from
 * would have to be listed somewhere else — and the file's own structure is a
 * better index than any we would invent.
 */

import type { MapEntity, MapFile, MapLumpDetail } from '../types';

export type Cell = string | number;

export interface Column {
  key: string;
  label: string;

  /** Right-aligned, and sorted as a number. */
  numeric?: boolean;
}

export interface Section {
  /** Stable across reloads: it is what the address bar remembers. */
  id: string;
  title: string;

  /** Which lump this decodes. Several sections can share one. */
  lump: number;

  /** One line about what the rows are, where the rows alone do not say. */
  note: string;
  columns: Column[];
  rows: Record<string, Cell>[];
}

/** The three components of a vector, as three columns. */
const vector = (prefix: string, labels = ['x', 'y', 'z']): Column[] =>
  labels.map((label, i) => ({ key: `${prefix}${i}`, label: `${prefix} ${label}`, numeric: true }));

const spread = (prefix: string, values: readonly number[]): Record<string, number> =>
  Object.fromEntries(values.map((value, i) => [`${prefix}${i}`, value]));

/**
 * Every section the file has something to say about.
 *
 * A section with no rows is left out rather than shown empty. A map with no
 * displacements has nothing to say about displacements, and a page of empty
 * tables is how a tool teaches people to stop reading it — the lump table
 * beside this still lists every lump, including the ones that are not there.
 *
 * @param entities Read separately, from the endpoint the entity debugger uses.
 *                 The file report carries a count and a histogram rather than
 *                 the whole list, because every other view of it polls.
 */
export function sectionsOf(file: MapFile, entities: readonly MapEntity[] = []): Section[] {
  const sections: Section[] = [];

  const add = (section: Section): void => {
    if (section.rows.length > 0) sections.push(section);
  };

  /** Every lightmap sample in the map, so a material can be shown as a share of it. */
  const luxels = file.materials.reduce((total, material) => total + material.luxels, 0);

  add({
    id: 'entities',
    title: 'Entities',
    lump: 0,
    note: 'Everything the map declares, as text in the entity lump.',
    columns: [
      { key: 'id', label: '#', numeric: true },
      { key: 'classname', label: 'Class' },
      { key: 'targetname', label: 'Name' },
      { key: 'model', label: 'Model' },
      ...vector('origin'),
      ...vector('angles', ['pitch', 'yaw', 'roll']),
      { key: 'keys', label: 'Keys', numeric: true },
      { key: 'connections', label: 'Outputs', numeric: true },
    ],
    rows: entities.map(entity => ({
      id: entity.id,
      classname: entity.classname,
      targetname: entity.targetname,
      model: entity.model,
      ...spread('origin', entity.origin),
      ...spread('angles', entity.angles),
      keys: entity.keyvalues.length,
      connections: entity.connections.length,
    })),
  });

  add({
    id: 'classes',
    title: 'Entity classes',
    lump: 0,
    note: 'How many of each, most first.',
    columns: [
      { key: 'classname', label: 'Class' },
      { key: 'count', label: 'Count', numeric: true },
    ],
    rows: file.classes.map(entry => ({ classname: entry.classname, count: entry.count })),
  });

  add({
    id: 'models',
    title: 'Brush models',
    lump: 14,
    note: 'Model 0 is the world; the rest are what brush entities are made of, referred to as "*1", "*2" and so on.',
    columns: [
      { key: 'index', label: '#', numeric: true },
      { key: 'model', label: 'Referred to as' },
      ...vector('mins'),
      ...vector('maxs'),
      ...vector('origin'),
      { key: 'size0', label: 'width', numeric: true },
      { key: 'size1', label: 'depth', numeric: true },
      { key: 'size2', label: 'height', numeric: true },
    ],
    rows: file.models.map((model, index) => ({
      index,
      model: index === 0 ? 'worldspawn' : `*${index}`,
      ...spread('mins', model.mins),
      ...spread('maxs', model.maxs),
      ...spread('origin', model.origin),
      ...spread('size', model.maxs.map((max, axis) => max - (model.mins[axis] ?? 0))),
    })),
  });

  add({
    id: 'materials',
    title: 'Materials',
    lump: 2,
    note: 'Counted by walking the faces: how much of the world is built out of each material, and what it cost to light. A material with no faces is one nothing was built out of.',
    columns: [
      { key: 'index', label: '#', numeric: true },
      { key: 'name', label: 'Material' },
      { key: 'faces', label: 'Faces', numeric: true },
      { key: 'area', label: 'Area', numeric: true },
      { key: 'luxels', label: 'Luxels', numeric: true },
      { key: 'share', label: 'Lightmap %', numeric: true },
      { key: 'width', label: 'Width', numeric: true },
      { key: 'height', label: 'Height', numeric: true },
      { key: 'viewWidth', label: 'View width', numeric: true },
      { key: 'viewHeight', label: 'View height', numeric: true },
      ...vector('reflectivity', ['r', 'g', 'b']),
    ],
    rows: file.materials.map(material => ({
      index: material.index,
      name: material.name,
      faces: material.faces,
      area: Math.round(material.area),
      luxels: material.luxels,

      // Against the map's own total rather than a byte count: the question is
      // which materials are eating the lightmap, and that is a share.
      share: luxels === 0 ? 0 : Math.round((material.luxels / luxels) * 1000) / 10,

      width: material.width,
      height: material.height,
      viewWidth: material.viewWidth,
      viewHeight: material.viewHeight,
      ...spread('reflectivity', material.reflectivity),
    })),
  });

  add({
    id: 'materialNames',
    title: 'Material names',
    lump: 43,
    note: 'The raw name table. Longer than the material list above when a name is left in that nothing points at.',
    columns: [
      { key: 'index', label: '#', numeric: true },
      { key: 'name', label: 'Name' },
    ],
    rows: file.materialNames.map((name, index) => ({ index, name })),
  });

  add({
    id: 'gameLumps',
    title: 'Game lumps',
    lump: 35,
    note: 'The offsets here are into the file, not into the game lump — which is why moving it means rewriting all of them.',
    columns: [
      { key: 'id', label: 'Id' },
      { key: 'version', label: 'Version', numeric: true },
      { key: 'flags', label: 'Flags', numeric: true },
      { key: 'offset', label: 'Offset', numeric: true },
      { key: 'length', label: 'Bytes', numeric: true },
    ],
    rows: file.gameLumps.map(lump => ({
      id: lump.id,
      version: lump.version,
      flags: lump.flags,
      offset: lump.offset,
      length: lump.length,
    })),
  });

  add({
    id: 'propModels',
    title: 'Prop models',
    lump: 35,
    note: 'The dictionary the props index into, with how many of each the map places.',
    columns: [
      { key: 'index', label: '#', numeric: true },
      { key: 'model', label: 'Model' },
      { key: 'count', label: 'Placed', numeric: true },
    ],
    rows: file.propModels.map((model, index) => ({
      index,
      model,
      count: file.props.filter(prop => prop.model === model).length,
    })),
  });

  add({
    id: 'props',
    title: 'Static props',
    lump: 35,
    note: 'Not entities: these exist only here, and nothing else in this tool can see them.',
    columns: [
      { key: 'index', label: '#', numeric: true },
      { key: 'model', label: 'Model' },
      ...vector('origin'),
      ...vector('angles', ['pitch', 'yaw', 'roll']),
      { key: 'skin', label: 'Skin', numeric: true },
      { key: 'fadeMin', label: 'Fade from', numeric: true },
      { key: 'fadeMax', label: 'Fade to', numeric: true },
      { key: 'solid', label: 'Solid', numeric: true },
      { key: 'flags', label: 'Flags' },
      { key: 'firstLeaf', label: 'First leaf', numeric: true },
      { key: 'leafCount', label: 'Leaves', numeric: true },
    ],
    rows: file.props.map(prop => ({
      index: prop.index,
      model: prop.model,
      ...spread('origin', prop.origin),
      ...spread('angles', prop.angles),
      skin: prop.skin,
      fadeMin: prop.fadeMin,
      fadeMax: prop.fadeMax,
      solid: prop.solid,
      flags: hex(prop.flags),
      firstLeaf: prop.firstLeaf,
      leafCount: prop.leafCount,
    })),
  });

  add({
    id: 'worldlights',
    title: 'World lights',
    lump: 15,
    note: 'What the engine reads at runtime, for models and props. The surfaces themselves are lit by the baked lightmaps, which these do not change.',
    columns: [
      { key: 'index', label: '#', numeric: true },
      { key: 'type', label: 'Kind' },
      { key: 'set', label: 'Set' },
      ...vector('origin'),
      ...vector('intensity', ['r', 'g', 'b']),
      ...vector('normal'),
      { key: 'style', label: 'Style', numeric: true },
      { key: 'radius', label: 'Radius', numeric: true },
      { key: 'owner', label: 'Owner', numeric: true },
    ],
    rows: file.worldlights.map(light => ({
      index: light.index,
      type: light.type,
      set: light.hdr ? 'HDR' : 'LDR',
      ...spread('origin', light.origin),
      ...spread('intensity', light.intensity),
      ...spread('normal', light.normal),
      style: light.style,
      radius: light.radius,
      owner: light.owner,
    })),
  });

  add({
    id: 'cubemaps',
    title: 'Cubemaps',
    lump: 42,
    note: 'The sample positions. The baked .vtf for each is in the pakfile, named after its position.',
    columns: [
      { key: 'index', label: '#', numeric: true },
      ...vector('origin'),
      { key: 'size', label: 'Size', numeric: true },
      { key: 'texture', label: 'Baked as' },
    ],
    rows: file.cubemaps.map((cubemap, index) => ({
      index,
      ...spread('origin', cubemap.origin),
      size: cubemap.size,

      // vrad names each face after the sample's position, so this is the name to
      // look for in the pakfile listing when a reflection is wrong.
      texture: `c${cubemap.origin.join('_')}.vtf`,
    })),
  });

  add({
    id: 'overlays',
    title: 'Overlays',
    lump: 45,
    note: 'Decals projected onto world surfaces: signs, stains and scorches.',
    columns: [
      { key: 'id', label: '#', numeric: true },
      { key: 'material', label: 'Material' },
      ...vector('origin'),
      { key: 'faces', label: 'Faces', numeric: true },
      { key: 'texinfo', label: 'Texinfo', numeric: true },
    ],
    rows: file.overlays.map(overlay => ({
      id: overlay.id,
      material: overlay.material,
      ...spread('origin', overlay.origin),
      faces: overlay.faces,
      texinfo: overlay.texinfo,
    })),
  });

  add({
    id: 'displacements',
    title: 'Displacements',
    lump: 26,
    note: 'Sculpted terrain. Each is a grid of (2^power + 1) vertices square, grown from one face.',
    columns: [
      { key: 'index', label: '#', numeric: true },
      { key: 'power', label: 'Power', numeric: true },
      { key: 'vertices', label: 'Vertices', numeric: true },
      ...vector('start'),
      { key: 'face', label: 'From face', numeric: true },
      { key: 'contents', label: 'Contents', numeric: true },
    ],
    rows: file.displacements.map(disp => ({
      index: disp.index,
      power: disp.power,
      vertices: disp.vertices,
      ...spread('start', disp.start),
      face: disp.face,
      contents: disp.contents,
    })),
  });

  add({
    id: 'areas',
    title: 'Areas',
    lump: 20,
    note: 'Regions the map is cut into by areaportals. Two or more mean the map can shut parts of itself off from view.',
    columns: [
      { key: 'index', label: '#', numeric: true },
      { key: 'portals', label: 'Portals', numeric: true },
      { key: 'firstPortal', label: 'First portal', numeric: true },
    ],
    rows: file.areas.map(area => ({
      index: area.index,
      portals: area.portals,
      firstPortal: area.firstPortal,
    })),
  });

  add({
    id: 'areaportals',
    title: 'Areaportals',
    lump: 21,
    note: 'The doorways between areas. The key is what a func_areaportal binds to with its portalnumber.',
    columns: [
      { key: 'index', label: '#', numeric: true },
      { key: 'key', label: 'Portal number', numeric: true },
      { key: 'otherArea', label: 'Looks into', numeric: true },
      { key: 'plane', label: 'Plane', numeric: true },
      { key: 'vertices', label: 'Vertices', numeric: true },
    ],
    rows: file.areaportals.map(portal => ({
      index: portal.index,
      key: portal.key,
      otherArea: portal.otherArea,
      plane: portal.plane,
      vertices: portal.vertices,
    })),
  });

  add({
    id: 'water',
    title: 'Water',
    lump: 36,
    note: 'Each body of water, with the height of its surface and the material on it.',
    columns: [
      { key: 'index', label: '#', numeric: true },
      { key: 'material', label: 'Material' },
      { key: 'surfaceZ', label: 'Surface z', numeric: true },
      { key: 'minZ', label: 'Bottom z', numeric: true },
    ],
    rows: file.water.map(water => ({
      index: water.index,
      material: water.material,
      surfaceZ: water.surfaceZ,
      minZ: water.minZ,
    })),
  });

  add({
    id: 'detailModels',
    title: 'Detail prop models',
    lump: 35,
    note: 'Grass and rubble, scattered by a material rather than placed. Only the models are listed; the objects themselves are thousands of sprites.',
    columns: [
      { key: 'index', label: '#', numeric: true },
      { key: 'name', label: 'Model' },
    ],
    rows: file.detailModels.map(model => ({ index: model.index, name: model.name })),
  });

  add({
    id: 'pak',
    title: 'Embedded files',
    lump: 40,
    note: 'The zip the map carries with it. What is in here overrides the mod, and what is missing from here is what a player without the content will not see.',
    columns: [
      { key: 'name', label: 'Path' },
      { key: 'kind', label: 'Kind' },
      { key: 'bytes', label: 'Bytes', numeric: true },
      { key: 'compressed', label: 'Stored', numeric: true },
    ],
    rows: file.pak.map(entry => ({
      name: entry.name,
      kind: extension(entry.name),
      bytes: entry.bytes,
      compressed: entry.compressed,
    })),
  });

  return sections;
}

/**
 * The sections for a lump that is read on its own.
 *
 * Same shape as the rest, so the same table draws them and the same export takes
 * them away. They are built separately only because they arrive separately.
 */
export function detailSections(detail: MapLumpDetail): Section[] {
  if (detail.faces.length > 0) {
    return [{
      id: `faces-${detail.lump}`,
      title: detail.name === 'FACES' ? 'Faces' : titleCase(detail.name),
      lump: detail.lump,
      note: 'The surfaces the compiler cut the world into. Sort by luxels to find what is filling the lightmap.',
      columns: [
        { key: 'index', label: '#', numeric: true },
        { key: 'material', label: 'Material' },
        { key: 'surface', label: 'Flags' },
        { key: 'area', label: 'Area', numeric: true },
        { key: 'lightmap', label: 'Lightmap' },
        { key: 'luxels', label: 'Luxels', numeric: true },
        { key: 'styles', label: 'Styles', numeric: true },
        { key: 'edges', label: 'Edges', numeric: true },
        { key: 'dispInfo', label: 'Displacement', numeric: true },
        { key: 'plane', label: 'Plane', numeric: true },
        { key: 'texinfo', label: 'Texinfo', numeric: true },
      ],
      rows: detail.faces.map(face => ({
        index: face.index,
        material: face.material,
        surface: face.surface,
        area: Math.round(face.area),

        // One cell, because the two numbers only mean anything together and
        // nobody sorts a map by the height of its lightmaps.
        lightmap: `${face.lightmapWidth}×${face.lightmapHeight}`,

        luxels: face.luxels,
        styles: face.styles,
        edges: face.edges,
        dispInfo: face.dispInfo,
        plane: face.plane,
        texinfo: face.texinfo,
      })),
    }];
  }

  if (detail.texinfos.length > 0) {
    return [{
      id: 'texinfos',
      title: 'Surfaces',
      lump: detail.lump,
      note: 'How each material is applied. Sixteen units per luxel is the default; anything smaller was asked for, and costs four times the lightmap for every halving.',
      columns: [
        { key: 'index', label: '#', numeric: true },
        { key: 'material', label: 'Material' },
        { key: 'surface', label: 'Flags' },
        { key: 'lightmapScale', label: 'Units per luxel', numeric: true },
        { key: 'texdata', label: 'Texdata', numeric: true },
      ],
      rows: detail.texinfos.map(info => ({
        index: info.index,
        material: info.material,
        surface: info.surface,
        lightmapScale: info.lightmapScale,
        texdata: info.texdata,
      })),
    }];
  }

  if (detail.leaves.length > 0) {
    return [{
      id: 'leaves',
      title: 'Leaves',
      lump: detail.lump,
      note: 'One convex region of space each. A cluster of -1 is solid, which is nowhere the player can stand.',
      columns: [
        { key: 'index', label: '#', numeric: true },
        { key: 'cluster', label: 'Cluster', numeric: true },
        { key: 'area', label: 'Area', numeric: true },
        { key: 'contents', label: 'Contents' },
        { key: 'faces', label: 'Faces', numeric: true },
        { key: 'brushes', label: 'Brushes', numeric: true },
        { key: 'water', label: 'Water', numeric: true },
        ...vector('mins'),
        ...vector('maxs'),
      ],
      rows: detail.leaves.map(leaf => ({
        index: leaf.index,
        cluster: leaf.cluster,
        area: leaf.area,
        contents: leaf.contents,
        faces: leaf.faces,
        brushes: leaf.brushes,
        water: leaf.water,
        ...spread('mins', leaf.mins),
        ...spread('maxs', leaf.maxs),
      })),
    }];
  }

  if (detail.brushes.length > 0) {
    return [{
      id: 'brushes',
      title: 'Brushes',
      lump: detail.lump,
      note: 'What the world was built out of, and what each one is made of. Filter on "playerclip" or "detail" to find them.',
      columns: [
        { key: 'index', label: '#', numeric: true },
        { key: 'contents', label: 'Contents' },
        { key: 'sides', label: 'Sides', numeric: true },
        { key: 'firstSide', label: 'First side', numeric: true },
      ],
      rows: detail.brushes.map(brush => ({
        index: brush.index,
        contents: brush.contents,
        sides: brush.sides,
        firstSide: brush.firstSide,
      })),
    }];
  }

  if (detail.brushsides.length > 0) {
    return [{
      id: 'brushsides',
      title: 'Brush sides',
      lump: detail.lump,
      note: 'One face of one brush. A bevel is a side the compiler added for collision rather than one anybody drew.',
      columns: [
        { key: 'index', label: '#', numeric: true },
        { key: 'material', label: 'Material' },
        { key: 'plane', label: 'Plane', numeric: true },
        { key: 'bevel', label: 'Bevel' },
        { key: 'dispInfo', label: 'Displacement', numeric: true },
        { key: 'texinfo', label: 'Texinfo', numeric: true },
      ],
      rows: detail.brushsides.map(side => ({
        index: side.index,
        material: side.material,
        plane: side.plane,
        bevel: side.bevel ? 'yes' : '',
        dispInfo: side.dispInfo,
        texinfo: side.texinfo,
      })),
    }];
  }

  return [];
}

/** A lump's own name, made readable: ORIGINALFACES becomes Originalfaces. */
const titleCase = (name: string): string =>
  name.charAt(0) + name.slice(1).toLowerCase().replaceAll('_', ' ');

/** The sections decoded out of one lump, in the order they were built. */
export const sectionsForLump = (sections: readonly Section[], lump: number): Section[] =>
  sections.filter(section => section.lump === lump);

const extension = (name: string): string => {
  const dot = name.lastIndexOf('.');
  return dot > name.lastIndexOf('/') ? name.slice(dot + 1).toLowerCase() : '';
};

/** Flags read as a bit field, and hexadecimal is how the engine's headers write them. */
const hex = (value: number): string => `0x${(value >>> 0).toString(16)}`;

//---------------------------------------------------------------------------
// Filtering, ordering and export. Everything below works on any section,
// because there is only one shape of section.
//---------------------------------------------------------------------------

/**
 * Rows matching a filter.
 *
 * Every cell, not a chosen column: what somebody types here is a model name, a
 * material, a classname or a number they saw somewhere else, and asking them
 * which column it was in first would be asking them to already know.
 */
export function filterRows(
  rows: readonly Record<string, Cell>[],
  text: string,
): Record<string, Cell>[] {
  const wanted = text.trim().toLowerCase();

  if (wanted === '') return [...rows];

  return rows.filter(row =>
    Object.values(row).some(cell => String(cell).toLowerCase().includes(wanted)));
}

/**
 * Rows in order.
 *
 * Numbers compare as numbers when the column says it holds them. A column of
 * offsets sorted as text puts 1000000 before 999, and every column in this view
 * is either a name or a number.
 */
export function sortRows(
  rows: readonly Record<string, Cell>[],
  column: Column | undefined,
  descending: boolean,
): Record<string, Cell>[] {
  if (!column) return [...rows];

  const compare = (a: Record<string, Cell>, b: Record<string, Cell>): number => {
    const left = a[column.key];
    const right = b[column.key];

    if (column.numeric) return Number(left ?? 0) - Number(right ?? 0);

    return String(left ?? '').localeCompare(String(right ?? ''));
  };

  const ordered = [...rows].sort(compare);
  return descending ? ordered.reverse() : ordered;
}

/**
 * One section as comma-separated values.
 *
 * The format a spreadsheet opens, which is the whole reason it is offered: the
 * questions this data answers next — how much of the map is one material, which
 * props are past the fade distance — are pivot table questions, and this tool is
 * not going to grow a pivot table.
 */
export function toCsv(columns: readonly Column[], rows: readonly Record<string, Cell>[]): string {
  const lines = [columns.map(column => csvCell(column.label)).join(',')];

  for (const row of rows) {
    lines.push(columns.map(column => csvCell(row[column.key] ?? '')).join(','));
  }

  return `${lines.join('\n')}\n`;
}

/**
 * One value, quoted when it has to be.
 *
 * A material name has no commas in it and a path has no quotes, right up until
 * the one that does — and a CSV that is wrong in one row is a CSV that imports
 * silently misaligned.
 */
export function csvCell(value: Cell): string {
  const text = String(value);

  return /[",\n\r]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

/** One section as JSON, for anything that is not a spreadsheet. */
export const toJson = (rows: readonly Record<string, Cell>[]): string =>
  `${JSON.stringify(rows, null, 2)}\n`;

//---------------------------------------------------------------------------

/** A byte count as somebody would say it. */
export function bytes(count: number): string {
  if (count < 1024) return `${count} B`;
  if (count < 1024 * 1024) return `${(count / 1024).toFixed(1)} kB`;

  return `${(count / (1024 * 1024)).toFixed(1)} MB`;
}

/** A number with thousands separated, which is the difference between 6142 and 61420. */
export const count = (value: number): string => value.toLocaleString('en-GB');

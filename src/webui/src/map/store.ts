/*
 * What the entity debugger is looking at.
 *
 * One store, shared by every pane, holding the two lists the tab is about: the
 * entities the map declares and the entities the engine currently has. Two
 * sources of truth for what a map contains is exactly the bug this tool exists
 * to find, so there is one of these and every view reads it.
 *
 * Outside React on purpose. It is loaded by requests, mutated by edits, and read
 * by four panes — one of which draws to a canvas and does not re-render when a
 * prop changes. A subscription and a mutable object model that honestly; holding
 * it as component state would mean rebuilding a map's worth of entities on every
 * poll to say that one of them moved.
 *
 * The shape of an entity and the rules for joining the two lists are in model —
 * this is the part with the requests and the bookkeeping.
 */

import { getEntities, getMap, getMapEntities, getMapGeometry, saveMap as postMap } from '../api';
import type { MapGeometry, MapInfo, SaveResult } from '../types';
import {
  findStatic, fromMap, join, keyOf, relink as linkIncoming, type Entity, type Mode,
} from './model';

export interface MapState {
  mode: Mode;
  map: string;
  available: string[];
  info: MapInfo | null;
  statics: Entity[];
  lives: Entity[];
  geometry: MapGeometry | null;
  selectedKey: string | null;
  error: string;

  /*
   * Whether the static list has been edited since it was read.
   *
   * Tracked on the whole list rather than per entity because that is the
   * question that matters: the map is written whole, so anything unsaved means
   * the file on disk is not what is on screen.
   */
  dirty: boolean;
}

export const state: MapState = {
  mode: 'static',
  map: '',
  available: [],
  info: null,
  statics: [],
  lives: [],
  geometry: null,
  selectedKey: null,
  error: '',
  dirty: false,
};

const listeners = new Set<() => void>();

/** Subscribes to changes, and answers with how to stop. */
export function onChange(fn: () => void): () => void {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

function changed(): void {
  for (const fn of [...listeners]) fn();
}

/*
 * Which static entity a live one is.
 *
 * Established once, by classname and position, and then remembered by handle.
 * Re-matching every poll would fail the moment anything moved — and the entities
 * that move are exactly the ones worth watching. A handle is stable for the life
 * of an entity, so pairing to it once is both cheaper and correct.
 */
const pairings = new Map<number, number | null>();

/** Everything belonging to the map, thrown away — a new map, or a new game. */
export function reset(): void {
  state.statics = [];
  state.lives = [];
  state.geometry = null;
  state.map = '';
  state.selectedKey = null;
  state.dirty = false;
  pairings.clear();
}

export function select(entity: Entity | null): void {
  state.selectedKey = keyOf(entity);
  changed();
}

export function selected(): Entity | null {
  return entities().find(entity => keyOf(entity) === state.selectedKey) ?? null;
}

/** The list the current mode shows, already joined. */
export function entities(): Entity[] {
  return state.mode === 'static' ? state.statics : state.lives;
}

export function setMode(mode: Mode): void {
  state.mode = mode;
  changed();
}

/** Entities matching a filter, which every view shares. */
export function filtered(filterText: string): Entity[] {
  const filter = filterText.trim().toLowerCase();
  if (filter === '') return entities();

  return entities().filter(entity =>
    entity.classname.toLowerCase().includes(filter)
    || entity.targetname.toLowerCase().includes(filter));
}

//---------------------------------------------------------------------------
// Loading.
//---------------------------------------------------------------------------

export async function loadMap(mapName?: string): Promise<void> {
  state.dirty = false;

  try {
    const info = await getMap(mapName);

    state.info = info;
    state.available = info.available ?? [];
    state.error = info.error ?? '';

    if (!info.loaded) {
      state.statics = [];
      state.map = '';
      changed();
      return;
    }

    // Loaded and named are the same answer from the API, but only one of them
    // is in the type: an info that says loaded without a name is a bug in the
    // server, and reading nothing is better than reading "undefined.bsp".
    state.map = info.map ?? '';

    const data = await getMapEntities(state.map);

    if (data.error) {
      state.error = data.error;
      state.statics = [];
    } else {
      state.statics = data.entities.map(fromMap);
      state.error = '';

      // Reverse edges have to exist before any view asks what points at what.
      relink();
    }

    // A new map invalidates every pairing: the handles belong to the old one.
    pairings.clear();
  } catch {
    state.error = 'could not read the map';
  }

  changed();
}

export async function loadGeometry(): Promise<MapGeometry | null> {
  if (state.geometry && state.geometry.map === state.map) return state.geometry;
  if (!state.map) return null;

  const data = await getMapGeometry(state.map);

  state.geometry = data.error ? null : data;
  changed();

  return state.geometry;
}

export async function pollLive(): Promise<void> {
  const data = await getEntities('*');

  if (data.error || !data.entities) return;

  state.lives = data.entities.map(live => {
    let match = pairings.get(live.handle);

    if (match === undefined) {
      match = findStatic(live, state.statics);
      pairings.set(live.handle, match);
    }

    return join(live, match === null ? null : state.statics[match] ?? null);
  });

  changed();
}

//---------------------------------------------------------------------------
// Editing.
//---------------------------------------------------------------------------

/** Recomputes what the derived views need, after the static list changed. */
export function relink(): void {
  byName = linkIncoming(state.statics);
}

/** Every static entity by lowercased name. Names are not unique in a map. */
export let byName = new Map<string, Entity[]>();

/** Something in the static list changed, so the map on disk is now behind. */
export function markDirty(): void {
  state.dirty = true;
  changed();
}

/**
 * Removes an entity from the edited list.
 *
 * From the model only. Nothing is written until the map is saved, which is what
 * makes an accidental delete a Revert away rather than a restore from backup.
 */
export function removeEntity(entity: Entity): void {
  state.statics = state.statics.filter(other => other !== entity);

  // Ids are the selection's identity, so they have to stay dense and stable
  // across a delete, or the row below the deleted one becomes selected.
  state.statics.forEach((other, index) => { other.id = index; });

  if (state.selectedKey === keyOf(entity)) state.selectedKey = null;

  relink();
  markDirty();
}

/** Adds an entity to the edited list and selects it. */
export function addEntity(partial: Partial<Entity> & { classname?: string }): Entity {
  const entity = fromMap({
    id: state.statics.length,
    classname: partial.classname ?? 'info_target',
    targetname: partial.targetname ?? '',
    origin: partial.origin ?? [0, 0, 0],
    angles: partial.angles ?? [0, 0, 0],
    model: partial.model ?? '',
    brushModel: -1,
    keyvalues: partial.keyvalues ?? [],
    connections: partial.connections ?? [],
  });

  state.statics.push(entity);
  relink();

  state.selectedKey = keyOf(entity);
  markDirty();

  return entity;
}

/**
 * Writes the edited list back to a map file.
 *
 * The whole list every time, because that is what the file holds — a map is
 * rewritten, not patched, and sending only what changed would mean the server
 * guessing at what to keep.
 */
export async function save(asName?: string): Promise<SaveResult> {
  const result = await postMap(state.map, asName, state.statics.map(entity => ({
    classname: entity.classname,
    targetname: entity.targetname,
    origin: entity.origin,
    angles: entity.angles,
    model: entity.model,

    // A list rather than an object: a map entity may carry the same key twice,
    // and the order it writes them in is worth keeping.
    keyvalues: entity.keyvalues,
    connections: entity.connections,
  })));

  if (!result.error) {
    // Saving to a different name leaves this list unsaved against its own map,
    // so only an overwrite clears the flag.
    if (result.overwrote) state.dirty = false;
    changed();
  }

  return result;
}

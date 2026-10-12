/*
 * The entity model behind the debugger.
 *
 * Two sources, deliberately kept apart until asked to join:
 *
 *   Static  -- what the compiled map declares. Read straight out of the BSP, so
 *              it exists before the game starts, survives it, and carries the
 *              one thing the running engine cannot tell us here: the targetname
 *              of every entity and every I/O connection between them.
 *
 *   Live    -- what the engine holds right now. Positions, health and angles for
 *              everything in the world, including entities the map never
 *              declared.
 *
 * Neither is the whole picture. Live entities have no names or connections;
 * static entities have no idea what has happened since. Joining them is what
 * makes a debugger rather than two lists, and the join is the interesting part.
 */

import type { Connection, KeyValue, LiveEntity, MapEntity, Vec3 } from '../types';

export type Mode = 'static' | 'live';

/** What kind of thing an entity is. Only ever used for colour and shape. */
export type Kind =
  | 'logic' | 'trigger' | 'brush' | 'npc' | 'item'
  | 'light' | 'sound' | 'prop' | 'point' | 'other' | 'missing';

/** A connection, with the entity that fires it — the edge the map file does not record. */
export interface IncomingConnection extends Connection {
  from: Entity;
}

/**
 * One entity, from either source, with everything the views need.
 *
 * The two sources produce the same shape on purpose: every view except the
 * viewports treats them identically, and a union type would have each of them
 * asking which kind it was holding on every access.
 */
export interface Entity {
  /** Position in the static list. The selection's identity, so it stays dense. */
  id: number;
  classname: string;
  targetname: string;
  model: string;
  brushModel: number;
  origin: Vec3;
  angles: Vec3;
  keyvalues: KeyValue[];
  connections: Connection[];
  incoming: IncomingConnection[];
  kind: Kind;
  label: string;

  /** Live only: the engine's handle and index, and its health. */
  handle?: number;
  index?: number;
  health?: number;

  /**
   * Live only. False for an entity created at runtime, which has no counterpart
   * in the map at all — an empty keyvalue list there means "we do not know its
   * keys", not "it has none", and the difference matters.
   */
  fromMap?: boolean;
}

const KINDS: [Kind, RegExp][] = [
  ['logic', /^(logic_|math_|filter_|point_template|env_global)/],
  ['trigger', /^trigger_/],
  ['brush', /^func_/],
  ['npc', /^(npc_|monster_)/],
  ['item', /^(item_|weapon_|ammo_)/],
  ['light', /^(light|env_sprite|env_lightglow|point_spotlight)/],
  ['sound', /^(ambient_|env_soundscape|env_sound)/],
  ['prop', /^prop_/],
  ['point', /^(info_|path_|env_|point_)/],
];

export function classify(classname: string): Kind {
  for (const [kind, pattern] of KINDS) {
    if (pattern.test(classname)) return kind;
  }

  return 'other';
}

export const KIND_COLOURS: Record<Kind, string> = {
  logic: '#c792ea',
  trigger: '#ffb454',
  brush: '#5aa9e6',
  npc: '#e06c75',
  item: '#6cc47f',
  light: '#e5c07b',
  sound: '#56b6c2',
  prop: '#8b93a3',
  point: '#61afef',
  other: '#a0a6b0',
  missing: '#e06c75',
};

/** A stable identity for the selection, across both modes and across polls. */
export const keyOf = (entity: Entity | null): string | null =>
  entity === null ? null
    : entity.handle !== undefined ? `live:${entity.handle}`
      : `static:${entity.id}`;

/** A declared entity, with the fields the file does not spell out. */
export function fromMap(entity: MapEntity): Entity {
  return {
    ...entity,
    incoming: [],
    kind: classify(entity.classname),
    label: entity.targetname || entity.classname,
  };
}

/**
 * Fills in the reverse edges.
 *
 * A connection is stored on the entity that fires it, so an entity has no idea
 * what targets it — and "what triggers this door" is at least as common a
 * question as "what does this button do".
 *
 * Names are not unique in a map, and a whole pattern of level design leans on
 * that: one name, many doors, fired together. So this is a list per name.
 */
export function relink(entities: Entity[]): Map<string, Entity[]> {
  const byName = new Map<string, Entity[]>();

  for (const entity of entities) {
    entity.incoming = [];
    if (!entity.targetname) continue;

    const name = entity.targetname.toLowerCase();
    const existing = byName.get(name);

    if (existing) existing.push(entity);
    else byName.set(name, [entity]);
  }

  for (const source of entities) {
    for (const connection of source.connections) {
      for (const target of byName.get(connection.target.toLowerCase()) ?? []) {
        target.incoming.push({ ...connection, from: source });
      }
    }
  }

  return byName;
}

/**
 * Which declared entity a live one is.
 *
 * By classname and position, because that is all the two sides share: the engine
 * will not tell us a targetname here, and the map does not know a handle. The
 * caller remembers the answer by handle — re-matching every poll would fail the
 * moment anything moved, and the entities that move are the ones worth watching.
 */
export function findStatic(live: LiveEntity, statics: Entity[]): number | null {
  let best: number | null = null;

  // Squared. An entity has not moved far by the first sample that sees it.
  let bestDistance = 64 * 64;

  for (let i = 0; i < statics.length; i++) {
    const candidate = statics[i]!;
    if (candidate.classname !== live.classname) continue;

    const dx = candidate.origin[0] - live.origin[0];
    const dy = candidate.origin[1] - live.origin[1];
    const dz = candidate.origin[2] - live.origin[2];
    const distance = dx * dx + dy * dy + dz * dz;

    if (distance < bestDistance) {
      bestDistance = distance;
      best = i;
    }
  }

  return best;
}

/** A live entity, given whatever the map knows about it. */
export function join(live: LiveEntity, source: Entity | null): Entity {
  return {
    id: source?.id ?? -1,
    classname: live.classname,
    targetname: source?.targetname ?? '',
    model: source?.model ?? '',
    brushModel: source?.brushModel ?? -1,
    origin: live.origin,
    angles: live.angles,
    keyvalues: source?.keyvalues ?? [],
    connections: source?.connections ?? [],
    incoming: source?.incoming ?? [],
    kind: classify(live.classname),
    label: source?.targetname || live.classname,
    handle: live.handle,
    index: live.index,
    health: live.health,
    fromMap: source !== null,
  };
}

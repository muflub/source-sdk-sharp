/*
 * Where saved groups live.
 *
 * localStorage rather than the game. A group outlives the map it came from and
 * the process it was captured in, which is exactly what the game cannot offer:
 * the server has no writable state that survives a restart, and giving a
 * debugging tool a filesystem it has no business having would be the wrong
 * trade.
 *
 * It also means groups are per browser and per machine, which is what the YAML
 * export is for.
 */

import { TEMPLATES, type Group, type GroupEntity } from './templates';
import type { Entity } from './model';

const KEY = 'debug-api.groups';
const VERSION = 1;

/**
 * Reads the saved groups, seeding the templates into an empty store.
 *
 * Anything unreadable is treated as nothing rather than thrown: this is a
 * browser store a person may well have edited by hand, and losing the groups is
 * bad but taking the page down with them is worse.
 *
 * Templates are seeded only when there is no store at all. Re-adding them on
 * every load would resurrect the ones someone deliberately deleted.
 */
export function load(): Group[] {
  let stored: { groups?: unknown } | null = null;

  try {
    stored = JSON.parse(localStorage.getItem(KEY) ?? 'null') as { groups?: unknown } | null;
  } catch {
    stored = null;
  }

  if (stored === null) {
    const seeded = TEMPLATES.map(template => ({ ...template, template: true }));
    save(seeded);
    return seeded;
  }

  return Array.isArray(stored.groups) ? stored.groups as Group[] : [];
}

/** Returns an error to show, or null. The quota is the realistic failure. */
export function save(groups: Group[]): string | null {
  try {
    localStorage.setItem(KEY, JSON.stringify({ version: VERSION, groups }));
    return null;
  } catch (error) {
    // A save that silently did not happen would be discovered at the worst
    // possible moment, which is when the group is wanted back.
    return `could not save: ${error instanceof Error ? error.message : String(error)} — this group is not stored`;
  }
}

/** The templates that are missing, so restoring does not revert an edited one. */
export function missingTemplates(groups: Group[]): Group[] {
  const have = new Set(groups.map(group => group.name));
  return TEMPLATES.filter(template => !have.has(template.name)).map(t => ({ ...t, template: true }));
}

/** An entity as a group stores it: what it is, where it was, and what it does. */
export function toGroupEntity(entity: Entity): GroupEntity {
  const keyvalues: Record<string, string> = {};

  for (const kv of entity.keyvalues) {
    // The four that are also fields would otherwise appear twice, and an edit to
    // one copy and not the other has no defined meaning.
    if (['classname', 'targetname', 'origin', 'angles'].includes(kv.key.toLowerCase())) continue;
    keyvalues[kv.key] = kv.value;
  }

  return {
    classname: entity.classname,
    targetname: entity.targetname,
    origin: entity.origin,
    angles: entity.angles,
    keyvalues,
    connections: entity.connections,
  };
}

let counter = 0;

/** Ids only have to be unique here, and a counter makes them so without a clock. */
export const newId = (): string => `g${++counter}_${Date.now().toString(36)}`;

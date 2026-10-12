/*
 * Named groups of entities, kept in the browser and put back into the game.
 *
 * The point is the loop this closes. The debugger can show what a map contains
 * and poke at it; a group is how you take a piece of one away, keep it past a
 * restart, and drop it back somewhere else — a working button and door, a trap
 * that took an afternoon to wire, saved once and placed wherever it is wanted.
 */

import { useCallback, useEffect, useState, type ReactNode } from 'react';

import { spawnGroup } from '../api';
import { save as download } from './download';
import { load, missingTemplates, newId, save, toGroupEntity } from './groupStore';
import type { Group, GroupEntity } from './templates';
import type { Entity } from './model';
import type { SelectionStore } from './selection';
import { toYaml, fromYaml } from './yaml';

export function GroupsPane({ selection, entities, map, mode, onResult }: {
  selection: SelectionStore;
  entities: Entity[];
  map: string;
  mode: string;
  onResult: (result: unknown) => void;
}): ReactNode {
  const [groups, setGroups] = useState<Group[]>([]);
  const [note, setNote] = useState('');
  const [player, setPlayer] = useState(1);
  const [selected, setSelected] = useState(0);

  useEffect(() => setGroups(load()), []);
  useEffect(() => selection.subscribe(() => setSelected(selection.size())), [selection]);

  const write = useCallback((next: Group[]): void => {
    setGroups(next);
    setNote(save(next) ?? '');
  }, []);

  const capture = (): void => {
    const chosen = entities.filter(entity => selection.has(keyFor(entity)));

    if (chosen.length === 0) {
      setNote('nothing is selected — ctrl-click entities in the list first');
      return;
    }

    const group: Group = {
      id: newId(),
      name: `${map || 'group'} ${groups.length + 1}`,
      map,
      source: mode,
      entities: chosen.map(toGroupEntity),
    };

    write([group, ...groups]);
    setNote(`saved ${chosen.length} entit${chosen.length === 1 ? 'y' : 'ies'} as "${group.name}"`);
  };

  const place = async (group: Group, placement: 'origin' | 'look'): Promise<void> => {
    setNote(`placing "${group.name}"…`);

    let result;

    try {
      result = await spawnGroup({ name: group.name, placement, player, entities: group.entities });
    } catch {
      setNote('the game did not answer');
      return;
    }

    onResult(result);

    if (result.error) {
      setNote(result.error);
      return;
    }

    const failed = (result.entities ?? []).filter(entity => !entity.created);

    if (failed.length === 0) {
      setNote(`placed ${result.created} entit${result.created === 1 ? 'y' : 'ies'} `
        + (placement === 'look' ? 'where you are looking' : 'at their saved positions'));
      return;
    }

    // Named individually. "3 of 7 failed" says something is wrong; which three
    // says what.
    setNote(`placed ${result.created} of ${result.requested}. `
      + `${failed.map(e => e.classname).join(', ')} — ${failed[0]!.error}`);
  };

  const importFile = async (file: File): Promise<void> => {
    try {
      const imported = fromYaml(await file.text()) as GroupYaml[];

      write([{
        id: newId(),
        name: file.name.replace(/\.ya?ml$/i, ''),
        map: '',
        source: 'imported',
        entities: imported.map(normalise),
      }, ...groups]);

      setNote(`imported ${imported.length} entit${imported.length === 1 ? 'y' : 'ies'} from ${file.name}`);
    } catch (error) {
      setNote(`could not read that file: ${error instanceof Error ? error.message : String(error)}`);
    }
  };

  return (
    <>
      <div className="toolbar">
        <button onClick={capture}>Save selection as a group</button>
        <button onClick={() => selection.clear()}>Clear selection</button>
        <label className="file-button">
          Import YAML
          <input
            type="file"
            accept=".yaml,.yml,text/yaml"
            hidden
            onChange={event => {
              const file = event.target.files?.[0];
              if (file) void importFile(file);

              // Cleared so choosing the same file twice fires the event twice.
              event.target.value = '';
            }}
          />
        </label>
        <button
          title="add back any template that has been deleted"
          onClick={() => {
            const added = missingTemplates(groups);
            write([...groups, ...added]);
            setNote(added.length ? `added ${added.length} template(s)` : 'every template is already here');
          }}
        >Restore templates</button>
        <label>
          player
          <input
            type="number"
            min={1}
            value={player}
            style={{ width: '6ch' }}
            onChange={event => setPlayer(Number(event.target.value) || 1)}
          />
        </label>
        <span className="count">
          {selected ? `${selected} selected` : 'ctrl-click the list to select, shift-click for a range'}
        </span>
      </div>

      <p className="note">
        Groups live in this browser, not in the game, so they outlast the map they
        came from and the game they were captured in. Placing one creates the
        entities for real &mdash; keyvalues first, then position, then the
        connections between them.
      </p>

      <p className="note" hidden={!note}>{note}</p>

      <div className="cards">
        {groups.map(group => (
          <GroupCard
            key={group.id}
            group={group}
            onRename={name => write(groups.map(g => g === group ? { ...g, name } : g))}
            onDelete={() => write(groups.filter(g => g !== group))}
            onPlace={placement => void place(group, placement)}
          />
        ))}
        {groups.length === 0 && <p className="empty">no saved groups yet</p>}
      </div>
    </>
  );
}

function GroupCard({ group, onRename, onDelete, onPlace }: {
  group: Group;
  onRename: (name: string) => void;
  onDelete: () => void;
  onPlace: (placement: 'origin' | 'look') => void;
}): ReactNode {
  const classes = [...new Set(group.entities.map(entity => entity.classname))];

  return (
    <div className={`group-card${group.template ? ' template' : ''}`}>
      <div className="group-head">
        <input
          className="group-name"
          defaultValue={group.name}
          onBlur={event => onRename(event.target.value.trim() || group.name)}
        />
        {group.template && <span className="pill">template</span>}
      </div>

      <p className="sub">
        {group.entities.length} entit{group.entities.length === 1 ? 'y' : 'ies'}
        {group.map && ` · from ${group.map}`}
        {group.note && ` · ${group.note}`}
      </p>

      <div>
        {classes.slice(0, 6).map(classname => <span className="pill" key={classname}>{classname}</span>)}
        {classes.length > 6 && <span className="pill none">+{classes.length - 6} more</span>}
      </div>

      <div className="toolbar">
        <button onClick={() => onPlace('look')}>Place where I look</button>
        <button
          title="puts every entity back exactly where it was captured"
          onClick={() => onPlace('origin')}
        >Place at saved origin</button>
        <button onClick={() => download(
          toYaml(group.entities.map(toEntityLike), group.map, 'group'),
          `${group.name.replace(/[^\w.-]+/g, '_')}.yaml`)}
        >YAML</button>
        <button onClick={onDelete}>{group.template ? 'Forget' : 'Delete'}</button>
      </div>
    </div>
  );
}

/** A stored entry, shaped as the YAML writer expects a live entity to be. */
function toEntityLike(entry: GroupEntity) {
  return {
    classname: entry.classname,
    targetname: entry.targetname,
    origin: entry.origin,
    angles: entry.angles,
    keyvalues: Object.entries(entry.keyvalues).map(([key, value]) => ({ key, value: String(value) })),
    connections: entry.connections,
  };
}

interface GroupYaml {
  classname?: string;
  targetname?: string;
  origin?: number[] | string;
  angles?: number[] | string;
  keyvalues?: Record<string, unknown>;
  connections?: Record<string, unknown>[];
}

/** A hand-written YAML entity, in the shape the store keeps. */
function normalise(entry: GroupYaml): GroupEntity {
  const keyvalues: Record<string, string> = {};
  for (const [key, value] of Object.entries(entry.keyvalues ?? {})) keyvalues[key] = String(value);

  return {
    classname: entry.classname ?? '',
    targetname: entry.targetname ?? '',
    origin: asVector(entry.origin),
    angles: asVector(entry.angles),
    keyvalues,
    connections: (entry.connections ?? []).map(c => ({
      output: String(c.output ?? ''), target: String(c.target ?? ''), input: String(c.input ?? ''),
      param: c.param === undefined ? '' : String(c.param),
      delay: Number(c.delay) || 0,
      times: c.times === undefined ? -1 : Number(c.times),
    })),
  };
}

function asVector(value: number[] | string | undefined): [number, number, number] {
  const parts = Array.isArray(value) ? value.map(Number)
    : typeof value === 'string' ? value.split(/\s+/).map(Number)
      : [];

  return [parts[0] ?? 0, parts[1] ?? 0, parts[2] ?? 0];
}

/** The selection's key for an entity, matching what the entity list uses. */
const keyFor = (entity: Entity): string =>
  entity.handle !== undefined ? `live:${entity.handle}` : `static:${entity.id}`;


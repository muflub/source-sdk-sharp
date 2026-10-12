/*
 * The selected entity: what it is, what it does, and how to make it do it now.
 *
 * The trigger buttons are what make this a debugger rather than a viewer. They
 * go through ent_fire, which is the same path a mapper's own logic takes — so
 * what happens when you press one is what would have happened anyway, rather
 * than an approximation of it.
 *
 * In static mode the list is the map, so everything here is editable and the
 * edits are what get written. In live mode it is the engine's, and the only way
 * to change a value is to tell the running game.
 */

import { useState, type ReactNode } from 'react';

import { fireInput, fireOutput } from '../api';
import type { CommandResult, Connection, KeyValue } from '../types';
import { save as download } from './download';
import { useSuggest } from './useSuggest';
import type { Entity, IncomingConnection, Mode } from './model';

import { dump, parse, toYaml } from './yaml';

/*
 * Inputs almost every entity understands.
 *
 * There is no FGD here to ask, so this is the set defined on CBaseEntity itself
 * plus the two or three so common that leaving them out would mean typing them
 * every time.
 */
const COMMON_INPUTS = [
  'Kill', 'KillHierarchy', 'AddOutput', 'FireUser1', 'FireUser2', 'FireUser3', 'FireUser4',
  'Enable', 'Disable', 'Toggle', 'Use', 'SetParent', 'ClearParent', 'Alpha', 'Color',
];

const coord = (n: number): number => Math.round(n * 10) / 10;

export function Inspector({ entity, mode, map, onChanged, onRemove, onSelectByName, onResult }: {
  entity: Entity | null;
  mode: Mode;
  map: string;
  onChanged: () => void;
  onRemove: (entity: Entity) => void;
  onSelectByName: (name: string) => void;
  onResult: (result: CommandResult) => void;
}): ReactNode {
  if (!entity) {
    return <p className="empty">nothing selected — pick an entity in any view</p>;
  }

  const editable = mode === 'static';

  const fire = async (target: string, input: string, param?: string, delay?: number | string) =>
    onResult(await fireInput(target, input, param, delay));

  return (
    <>
      <div className="inspect-header">
        <h3>{entity.targetname || '(unnamed)'}</h3>
        <p className="sub">{entity.classname}</p>

        {/* Created at runtime. Saying so matters: an empty keyvalue list here
            means "not in the map file", not "this entity has no keys". */}
        {entity.fromMap === false && (
          <p className="warn-note">created at runtime — the map file has nothing to say about this one</p>
        )}
      </div>

      <Where
        entity={entity}
        editable={editable}
        onChanged={onChanged}
        onRemove={() => onRemove(entity)}
      />

      <FireForm entity={entity} onFire={fire} />

      <Outputs
        entity={entity}
        editable={editable}
        map={map}
        onChanged={onChanged}
        onSelectByName={onSelectByName}
        onFire={fire}
        onResult={onResult}
      />

      <Incoming entity={entity} onSelect={connection => onSelectByName(connection.from.targetname)} />

      <Keyvalues
        entity={entity}
        editable={editable}
        onChanged={onChanged}
        onFire={fire}
      />

      <Yaml entity={entity} map={map} mode={mode} editable={editable} onFire={fire} />
    </>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }): ReactNode {
  return <section className="inspect-section"><h4>{title}</h4>{children}</section>;
}

function Where({ entity, editable, onChanged, onRemove }: {
  entity: Entity;
  editable: boolean;
  onChanged: () => void;
  onRemove: () => void;
}): ReactNode {
  const name = useSuggest('targetname');
  const classname = useSuggest('classname');

  return (
    <Section title="Where">
      <div className="kv">
        {/* The static list is the map's own, so editing it is editing the map.
            The live list is the engine's, and it will not be told what an
            entity's origin was declared as — it has one, and it is where the
            entity is. */}
        {editable ? (
          <>
            <span className="k">name</span>
            <span>
              <input
                ref={name}
                type="text"
                defaultValue={entity.targetname}
                onChange={event => {
                  entity.targetname = event.target.value;
                  entity.label = event.target.value || entity.classname;
                  onChanged();
                }}
              />
            </span>

            <span className="k">class</span>
            <span>
              <input
                ref={classname}
                type="text"
                defaultValue={entity.classname}
                onChange={event => {
                  entity.classname = event.target.value || entity.classname;
                  onChanged();
                }}
              />
            </span>

            <span className="k">origin</span>
            <Vector values={entity.origin} onChanged={onChanged} />

            <span className="k">angles</span>
            <Vector values={entity.angles} onChanged={onChanged} />
          </>
        ) : (
          <>
            <span className="k">origin</span><span>{entity.origin.map(coord).join(', ')}</span>
            <span className="k">angles</span><span>{entity.angles.map(coord).join(', ')}</span>
          </>
        )}

        {entity.index !== undefined && <><span className="k">index</span><span>{entity.index}</span></>}
        {entity.handle !== undefined && <><span className="k">handle</span><span>{entity.handle}</span></>}
        {entity.health !== undefined && <><span className="k">health</span><span>{entity.health}</span></>}
        {entity.brushModel > 0 && <><span className="k">brush model</span><span>*{entity.brushModel}</span></>}
      </div>

      {editable && (
        <button className="tiny danger" onClick={onRemove}>Delete this entity</button>
      )}
    </Section>
  );
}

/** Three number boxes over one vector, edited in place. */
function Vector({ values, onChanged }: { values: number[]; onChanged: () => void }): ReactNode {
  return (
    <span className="vector-field">
      {[0, 1, 2].map(axis => (
        <input
          key={axis}
          type="number"
          step="any"
          defaultValue={coord(values[axis] ?? 0)}
          onChange={event => { values[axis] = Number(event.target.value) || 0; onChanged(); }}
        />
      ))}
    </span>
  );
}

function FireForm({ entity, onFire }: {
  entity: Entity;
  onFire: (target: string, input: string, param?: string, delay?: string) => void;
}): ReactNode {
  const [input, setInput] = useState('');
  const [param, setParam] = useState('');
  const [delay, setDelay] = useState('0');

  // ent_fire addresses by name. Without one there is nothing to aim at, and
  // offering the form anyway would just fail on every press.
  if (!entity.targetname) {
    return (
      <Section title="Fire an input">
        <p className="empty">this entity has no targetname, so ent_fire has no way to address it</p>
      </Section>
    );
  }

  // What the map sends this entity first: those are the inputs it demonstrably
  // understands, which is better evidence than any generic list.
  const known = [...new Set([...entity.incoming.map(c => c.input), ...COMMON_INPUTS])];

  return (
    <Section title="Fire an input">
      <form
        className="fire-form"
        onSubmit={event => {
          event.preventDefault();
          if (input.trim()) onFire(entity.targetname, input.trim(), param, delay);
        }}
      >
        <input
          list="inspector-inputs"
          placeholder="input, e.g. Trigger"
          required
          value={input}
          onChange={event => setInput(event.target.value)}
        />
        <input placeholder="parameter" value={param} onChange={event => setParam(event.target.value)} />
        <input
          type="number"
          step="any"
          min="0"
          title="delay in seconds"
          value={delay}
          onChange={event => setDelay(event.target.value)}
        />
        <button type="submit">Fire</button>

        <datalist id="inspector-inputs">
          {known.map(name => <option key={name} value={name} />)}
        </datalist>
      </form>
    </Section>
  );
}

function Outputs({ entity, editable, map, onChanged, onSelectByName, onFire, onResult }: {
  entity: Entity;
  editable: boolean;
  map: string;
  onChanged: () => void;
  onSelectByName: (name: string) => void;
  onFire: (target: string, input: string, param?: string, delay?: number) => void;
  onResult: (result: CommandResult) => void;
}): ReactNode {
  if (entity.connections.length === 0) {
    return <Section title="Outputs"><p className="empty">this entity fires nothing</p></Section>;
  }

  // Grouped by output, because that is the unit a person thinks in: "what does
  // OnTrigger do" rather than "what is the fourth connection".
  const byOutput = new Map<string, Connection[]>();

  for (const connection of entity.connections) {
    const existing = byOutput.get(connection.output);
    if (existing) existing.push(connection);
    else byOutput.set(connection.output, [connection]);
  }

  return (
    <Section title="Outputs">
      {[...byOutput].map(([name, group]) => (
        <div className="output-group" key={name}>
          <div className="output-head">
            <span className="output-name">{name}</span>
            <button
              title="fires everything this output is connected to, now"
              disabled={!entity.targetname}
              onClick={async () => onResult(await fireOutput(entity.targetname, name, map))}
            >Fire {group.length}</button>
          </div>

          {group.map((connection, index) => (
            <div className="connection" key={`${connection.target}-${connection.input}-${index}`}>
              <button className="link" onClick={() => onSelectByName(connection.target)}>
                {connection.target || '(nothing)'}
              </button>
              <span className="arrow"> ▸ </span>
              <span className="input-name">{connection.input}</span>
              {connection.param && <span className="param"> "{connection.param}"</span>}
              {connection.delay > 0 && <span className="delay"> after {connection.delay}s</span>}
              {connection.times >= 0 && <span className="delay"> ×{connection.times}</span>}

              {editable ? (
                <button
                  className="tiny"
                  title="remove this connection from the map"
                  onClick={() => {
                    entity.connections = entity.connections.filter(other => other !== connection);
                    onChanged();
                  }}
                >×</button>
              ) : (
                <button
                  className="tiny"
                  title="fires just this one connection"
                  onClick={() => onFire(connection.target, connection.input, connection.param, connection.delay)}
                >send</button>
              )}
            </div>
          ))}
        </div>
      ))}
    </Section>
  );
}

function Incoming({ entity, onSelect }: {
  entity: Entity;
  onSelect: (connection: IncomingConnection) => void;
}): ReactNode {
  if (entity.incoming.length === 0) {
    return <Section title="Fired by"><p className="empty">nothing in the map points at this</p></Section>;
  }

  return (
    <Section title="Fired by">
      {entity.incoming.map((connection, index) => (
        <div className="connection" key={index}>
          <button className="link" onClick={() => onSelect(connection)}>
            {connection.from.targetname || connection.from.classname}
          </button>
          <span className="arrow"> . </span>
          <span className="output-name">{connection.output}</span>
          <span className="arrow"> ▸ </span>
          <span className="input-name">{connection.input}</span>
        </div>
      ))}
    </Section>
  );
}

/**
 * The keys, editable in static mode.
 *
 * Connection keys are left out. They are the same data as the Outputs section
 * above, and showing them twice invites an edit to one copy that the other
 * silently overwrites when the map is written.
 */
function Keyvalues({ entity, editable, onChanged, onFire }: {
  entity: Entity;
  editable: boolean;
  onChanged: () => void;
  onFire: (target: string, input: string, param?: string, delay?: number) => void;
}): ReactNode {
  const [key, setKey] = useState('');
  const [value, setValue] = useState('');
  const keyRef = useSuggest('key', entity.classname);

  const shown = entity.keyvalues.filter(kv => !isConnectionKey(kv));

  if (shown.length === 0 && !editable) {
    return (
      <Section title="Keyvalues">
        <p className="empty">{entity.fromMap === false ? 'not declared in the map file' : 'no keys'}</p>
      </Section>
    );
  }

  return (
    <Section title="Keyvalues">
      <table className="kv-table">
        <tbody>
          {shown.map((kv, index) => (
            <tr key={`${kv.key}-${index}`}>
              <td className="name">{kv.key}</td>
              <td>
                <input
                  type="text"
                  defaultValue={kv.value}
                  onChange={event => {
                    // Edited in place. Appending a second copy of the key would
                    // leave the map with both, and the engine applying whichever
                    // came last.
                    if (editable) { kv.value = event.target.value; onChanged(); }
                  }}
                />
                {editable ? (
                  <button
                    className="tiny"
                    title="remove this key"
                    onClick={() => {
                      entity.keyvalues = entity.keyvalues.filter(other => other !== kv);
                      onChanged();
                    }}
                  >×</button>
                ) : (
                  <button
                    className="tiny"
                    // AddOutput is how a keyvalue is changed on a live entity —
                    // there is no other input that takes an arbitrary key.
                    title="sets this key on the live entity, via AddOutput"
                    disabled={!entity.targetname}
                    onClick={() => onFire(entity.targetname, 'AddOutput', `${kv.key} ${kv.value}`, 0)}
                  >set</button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {editable && (
        <form
          className="fire-form"
          onSubmit={event => {
            event.preventDefault();

            const name = key.trim();
            if (!name) return;

            const existing = entity.keyvalues.find(kv => kv.key.toLowerCase() === name.toLowerCase());
            if (existing) existing.value = value;
            else entity.keyvalues.push({ key: name, value });

            setKey('');
            setValue('');
            onChanged();
          }}
        >
          <input ref={keyRef} placeholder="key" required value={key} onChange={e => setKey(e.target.value)} />
          <input placeholder="value" value={value} onChange={e => setValue(e.target.value)} />
          <button type="submit">Add key</button>
        </form>
      )}
    </Section>
  );
}

/** Whether a key is really one of the entity's outputs, shown elsewhere. */
function isConnectionKey(kv: KeyValue): boolean {
  const parts = String(kv.value).split(/[,\x1b]/);
  return parts.length >= 5 && /^[A-Za-z_]\w*$/.test(parts[1] ?? '');
}

function Yaml({ entity, map, mode, editable, onFire }: {
  entity: Entity;
  map: string;
  mode: Mode;
  editable: boolean;
  onFire: (target: string, input: string, param?: string, delay?: number) => void;
}): ReactNode {
  const [note, setNote] = useState('');
  const text = dump(toObject(entity)) as string;

  return (
    <Section title="YAML">
      <textarea className="yaml" rows={10} key={text} defaultValue={text} id="inspector-yaml" />

      <div className="toolbar">
        <button onClick={() => void navigator.clipboard?.writeText(text)}>Copy</button>
        <button onClick={() => download(
          toYaml([entity], map, mode),
          `${map || 'map'}-${entity.targetname || entity.classname}.yaml`)}
        >Download</button>
        <button
          title="sends each keyvalue to the running entity with AddOutput"
          disabled={editable || !entity.targetname}
          onClick={() => {
            const area = document.getElementById('inspector-yaml') as HTMLTextAreaElement | null;
            if (!area) return;

            try {
              // A document that is not a mapping has no keys to send, which the
              // count says plainly rather than throwing at whoever pressed this.
              const edited = parse(area.value);
              const sent = edited && typeof edited === 'object' && !Array.isArray(edited)
                ? applyToLive(entity, edited, onFire)
                : 0;

              setNote(`sent ${sent} key(s)`);
            } catch (error) {
              setNote(`could not read that YAML: ${error instanceof Error ? error.message : String(error)}`);
            }
          }}
        >Apply to live entity</button>
        <span className="count">{note}</span>
      </div>
    </Section>
  );
}

/** One entity in the shape the YAML reader reads back. */
function toObject(entity: Entity): Record<string, unknown> {
  const keyvalues: Record<string, string> = {};

  for (const kv of entity.keyvalues) {
    if (['classname', 'targetname', 'origin', 'angles'].includes(kv.key.toLowerCase())) continue;
    keyvalues[kv.key] = kv.value;
  }

  const object: Record<string, unknown> = {
    classname: entity.classname,
    targetname: entity.targetname,
    origin: entity.origin.map(n => Math.round(n * 100) / 100),
    angles: entity.angles.map(n => Math.round(n * 100) / 100),
  };

  if (Object.keys(keyvalues).length) object.keyvalues = keyvalues;
  if (entity.connections.length) object.connections = entity.connections;

  return object;
}

/**
 * Pushes edited keys at a running entity.
 *
 * Only the keys that differ: AddOutput on every key would rewrite values the
 * game may have changed for its own reasons since the map loaded.
 */
function applyToLive(
  entity: Entity,
  edited: { keyvalues?: Record<string, unknown>; origin?: unknown; angles?: unknown },
  onFire: (target: string, input: string, param?: string, delay?: number) => void,
): number {
  const original = toObject(entity) as { keyvalues?: Record<string, string> };
  const changes: [string, string][] = [];

  for (const [key, value] of Object.entries(edited.keyvalues ?? {})) {
    if (String(original.keyvalues?.[key]) === String(value)) continue;
    changes.push([key, String(value)]);
  }

  for (const axis of ['origin', 'angles'] as const) {
    const before = entity[axis].map(n => Math.round(n * 100) / 100).join(' ');
    const after = Array.isArray(edited[axis]) ? (edited[axis] as number[]).join(' ') : edited[axis];

    if (after !== undefined && String(after) !== before) changes.push([axis, String(after)]);
  }

  for (const [key, value] of changes) {
    onFire(entity.targetname, 'AddOutput', `${key} ${value}`, 0);
  }

  return changes.length;
}

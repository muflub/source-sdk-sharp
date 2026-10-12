/*
 * The entity debugger.
 *
 * The tab itself: the toolbar, the list, and the actions that write. Every pane
 * on it reads one store — see store — rather than a copy of its own, because
 * two sources of truth for what a map contains is exactly the bug this tool
 * exists to find.
 */

import { useCallback, useEffect, useMemo, useState, type ReactNode, type RefObject } from 'react';

import { runCommand } from '../api';
import type { CommandResult, Vec3 } from '../types';
import { componentsOf, inComponent, type Component } from './components';
import { EntityList } from './EntityList';
import { EventsPane, type EventsHandle } from './EventsPane';
import { GroupsPane } from './GroupsPane';
import { GraphPane } from './graph/GraphPane';
import { Inspector } from './Inspector';
import { MapOptions, PromptBar, type Prompt } from './MapToolbar';
import { save as download } from './download';
import { fromMap, type Entity, type Mode } from './model';
import type { SelectionStore } from './selection';
import { useUrlState } from '../hooks/useUrlState';
import { ViewportPane } from './viewport/ViewportPane';

import * as data from './store';
import * as suggest from '../suggest';
import { toYaml, fromYaml } from './yaml';

/** What the shell drives from its poll loop, while this tab is up. */
export interface MapHandle {
  poll: () => Promise<void>;
}

/*
 * Everything on the page belongs to one game, so switching games throws all of
 * it away. Keeping any of it would be worse than an empty page: an entity handle
 * from the previous instance is indistinguishable from one from this one, and
 * acting on it would be acting on the wrong game.
 */
export const resetMap = data.reset;

/** The panes this tab shows, in the order their tabs appear. */
const PANES: [Pane, string][] = [
  ['map', 'Map'],
  ['graph', 'Connections'],
  ['views', 'Viewports'],
  ['events', 'Event log'],
  ['groups', 'Groups'],
];

export type Pane = 'map' | 'graph' | 'views' | 'events' | 'groups';

export function MapTab({ visible, select, selection, handle, eventsHandle, onResult }: {
  visible: boolean;

  /** An entity named in the address, so a link can point at one. */
  select: string;
  selection: SelectionStore;
  handle: RefObject<MapHandle | null>;

  /** The event feed's, held by the shell because the shell drives its poll. */
  eventsHandle: RefObject<EventsHandle | null>;
  onResult: (result: CommandResult) => void;
}): ReactNode {
  const [, bump] = useState(0);
  const [filter, setFilter] = useUrlState('find');
  const [follow, setFollow] = useState(true);
  const [status, setStatus] = useState('');
  const [prompt, setPrompt] = useState<Prompt | null>(null);
  const [pane, setPane] = useUrlState('pane', 'graph') as [Pane, (pane: Pane) => void];

  /*
   * Which piece of the map's logic is being looked at.
   *
   * Shared by the graph and the list on purpose: picking a trap in the graph and
   * then reading a list of every entity in the map is two views disagreeing
   * about what the subject is.
   */
  const [group, setGroup] = useUrlState('group');

  // The store is not React state, so this is what re-renders when it changes.
  useEffect(() => data.onChange(() => bump(n => n + 1)), []);

  const state = data.state;
  const entities = data.entities();

  const components = useMemo(() => componentsOf(entities), [entities]);
  const chosen = components.find(component => component.id === group);

  // Everything in the piece being looked at, or everything, and then whatever
  // the filter box says.
  const listed = useMemo(
    () => entities.filter(entity => inComponent(entity, chosen)),
    [entities, chosen]);

  const reload = useCallback(async (mapName?: string): Promise<void> => {
    await data.loadMap(mapName);

    // What can be suggested is whatever the map turned out to contain, so the
    // suggestions are rebuilt from it rather than kept up to date piecemeal.
    suggest.refreshFromMap(data.state.statics, data.state.lives, data.state.available);
  }, []);

  // Only while the tab is up: live entities are the largest thing the API
  // returns, and polling for a list nobody is looking at is the game's time.
  handle.current = {
    poll: async () => {
      if (!visible || state.mode !== 'live' || !follow) return;
      await data.pollLive();
    },
  };

  useEffect(() => {
    if (!visible) return;

    // Nothing to select until the map has been read, so the name in the address
    // is applied after loading rather than before.
    const selectByName = (): void => {
      if (!select) return;

      const wanted = select.toLowerCase();
      const match = data.entities().find(entity => (entity.targetname || '').toLowerCase() === wanted);

      if (match) data.select(match);
    };

    if (!state.map && state.statics.length === 0) void reload().then(selectByName);
    else selectByName();
  }, [visible, select, reload, state.map, state.statics.length]);

  const save = async (asName?: string): Promise<void> => {
    setStatus('saving…');

    let result;

    try {
      result = await data.save(asName);
    } catch {
      setStatus('the game did not answer');
      return;
    }

    if (result.error) {
      setStatus(`could not save: ${String(result.error)}`);
      return;
    }

    // Named outright: a save that quietly went somewhere other than where it was
    // expected is the failure worth guarding against here.
    setStatus(`saved ${result.entityCount} entities to ${result.path}`
      + (result.backup ? ` — the previous file is at ${result.backup}` : '')
      + (result.overwrote ? ' · Reload to see it in game' : ''));

    await reload();
  };

  /**
   * Loads a map in the running game.
   *
   * "map" rather than "changelevel": this exists to see an edited map, and only
   * a fresh load re-reads the file.
   */
  const loadInGame = async (mapName: string): Promise<void> => {
    if (!mapName) return;

    if (state.dirty
      && !confirm('The entity list has unsaved edits. Load the map anyway?')) return;

    setStatus(`asking the game to load ${mapName}…`);

    const result = await runCommand(`map ${mapName}`);
    onResult(result);

    setStatus(result.error
      ? `could not ask the game: ${result.error}`
      : `told the game to load ${mapName} — it takes a moment`);
  };

  const options = (
    <MapOptions
      info={state.info}
      map={state.map}
      mode={state.mode}
      dirty={state.dirty}
      canSave={state.statics.length > 0}
      follow={follow}
      onMapChange={name => void reload(name)}
      onModeChange={mode => { data.setMode(mode); if (mode === 'live') void data.pollLive(); }}
      onFollowChange={setFollow}
      onReload={() => void reload()}
      onLoadInGame={name => void loadInGame(name)}
      onReloadInGame={() => void loadInGame(state.info?.current ?? state.map)}
      onImport={file => void importFile(file, setStatus)}

      onAdd={() => setPrompt({
        label: 'New entity of class',
        value: 'logic_relay',
        kind: 'classnames',
        action: 'Add',
        onAnswer: classname => {
          // Next to whatever is selected, because a new entity is almost always
          // wanted beside the thing being worked on.
          const near = data.selected();
          const origin: Vec3 = near
            ? [near.origin[0] + 64, near.origin[1], near.origin[2]]
            : [0, 0, 0];

          data.addEntity({ classname, origin });
          setStatus(`added a ${classname} — give it a name and keys in the inspector`);
        },
      })}

      onSave={() => void save()}

      onSaveAs={() => setPrompt({
        label: 'Save the entity list as',
        value: `${state.map || 'map'}_edit`,
        kind: 'maps',
        action: 'Save',
        onAnswer: name => {
          // The server checks this too, but saying so here costs a round trip
          // less and explains the rule rather than reporting a rejection.
          if (/[\\/]|\.\./.test(name)) {
            setStatus('a map name is a name, not a path');
            return;
          }

          void save(name.replace(/\.bsp$/i, ''));
        },
      })}

      onRevert={() => {
        if (state.dirty && !confirm('Throw away the edits and read the map again?')) return;

        void reload().then(() => setStatus('reverted to the map on disk'));
      }}
    />
  );

  return (
    <div className="map-layout">
      {/*
        * The list on the left, beside the views rather than above them. It is
        * the thing every other pane is about — what is selected here is what the
        * graph centres on, what the viewports box, and what the inspector fills
        * in — so it is read down the side of them rather than in a strip that
        * pushed them off the screen.
        */}
      <div className="map-side">
        <div className="toolbar">
          <input
            type="search"
            list="sg-classnames"
            placeholder="filter by name or class"
            value={filter}
            onChange={event => setFilter(event.target.value)}
          />
          <button
            title="add every entity the filter is showing to the selection"
            onClick={() => selection.add(shownKeys(listed, filter))}
          >Select shown</button>
          <button
            title="every entity currently listed, as YAML"
            onClick={() => download(
              toYaml(filtered(listed, filter), state.map, state.mode),
              `${state.map || 'map'}-entities.yaml`)}
          >Export</button>
        </div>

        <EntityList
          entities={listed}
          filter={filter}
          selectedKey={state.selectedKey}
          selection={selection}
          onSelect={entity => data.select(entity)}
        />

        <p className="count">
          {chosen ? `${chosen.label} · ${listed.length} entities` : summarise(state, entities)}
        </p>
      </div>

      <div className="map-main">
        <nav className="tabs sub">
          {PANES.map(([name, label]) => (
            <button
              key={name}
              className="map-pane-tab"
              data-pane={name}
              aria-selected={pane === name}
              onClick={() => setPane(name)}
            >{label}</button>
          ))}
        </nav>

        <section className="map-pane" data-pane="map" hidden={pane !== 'map'}>
          {options}
          <PromptBar prompt={prompt} onCancel={() => setPrompt(null)} />

          <p className="note" hidden={!state.error}>{state.error}</p>
          <p className="note">{status}</p>
        </section>

        <section className="map-pane" data-pane="graph" hidden={pane !== 'graph'}>
          <MapGraph
            components={components}
            group={group}
            onGroup={setGroup}
            onResult={onResult}
          />
        </section>

        {/* Kept mounted while hidden, because its geometry lives on the GPU and
            re-reading a map's worth of it on every pane switch is the slowest
            thing this page can do. */}
        <section className="map-pane" data-pane="views" hidden={pane !== 'views'}>
          <MapViewports visible={visible && pane === 'views'} />
        </section>

        <section className="map-pane" data-pane="events" hidden={pane !== 'events'}>
          <EventsPane handle={eventsHandle} />
        </section>

        <section className="map-pane" data-pane="groups" hidden={pane !== 'groups'}>
          <MapGroups selection={selection} onResult={onResult} />
        </section>
      </div>

      <aside className="inspector">
        <MapInspector onResult={onResult} />
      </aside>
    </div>
  );
}

/** The inspector, which needs the same store and the same actions. */
export function MapInspector({ onResult }: { onResult: (result: CommandResult) => void }): ReactNode {
  const [, bump] = useState(0);
  useEffect(() => data.onChange(() => bump(n => n + 1)), []);

  const entity = data.selected();

  return (
    <Inspector
      entity={entity}
      mode={data.state.mode}
      map={data.state.map}
      onChanged={() => data.markDirty()}
      onRemove={target => data.removeEntity(target)}
      onSelectByName={name => {
        const wanted = name.toLowerCase();
        const match = data.entities().find(other => (other.targetname || '').toLowerCase() === wanted);
        if (match) data.select(match);
      }}
      onResult={onResult}
    />
  );
}

const filtered = (entities: Entity[], filter: string): Entity[] => {
  const text = filter.trim().toLowerCase();
  if (text === '') return entities;

  return entities.filter(entity => entity.classname.toLowerCase().includes(text)
    || entity.targetname.toLowerCase().includes(text));
};

const shownKeys = (entities: Entity[], filter: string): string[] =>
  filtered(entities, filter).slice(0, 400)
    .map(entity => entity.handle !== undefined ? `live:${entity.handle}` : `static:${entity.id}`);

function summarise(state: typeof data.state, entities: Entity[]): string {
  const connections = state.statics.reduce((total, entity) => total + entity.connections.length, 0);

  if (state.mode === 'static') {
    return `${state.map || 'no map'} · ${entities.length} declared · ${connections} connections`
      + (state.dirty ? ' · edited, not saved' : '');
  }

  const known = entities.filter(entity => entity.fromMap).length;
  return `${state.map || 'no map'} · ${entities.length} live · ${known} matched to the map`;
}

/**
 * Loads entities from a file into the model.
 *
 * Shown, not written. The BSP is a compiled artefact and nothing here rewrites
 * one from an import — what this is for is comparing a saved state against the
 * map, and pushing individual values at the running game from the inspector.
 */
async function importFile(file: File, setStatus: (text: string) => void): Promise<void> {
  try {
    const entities = fromYaml(await file.text()) as Record<string, unknown>[];

    data.state.statics = entities.map((entity, id) => fromMap({
      id,
      classname: String(entity.classname ?? '(none)'),
      targetname: String(entity.targetname ?? ''),
      origin: asVector(entity.origin),
      angles: asVector(entity.angles),
      model: String(entity.model ?? ''),
      brushModel: -1,
      keyvalues: Object.entries((entity.keyvalues ?? {}) as Record<string, unknown>)
        .map(([key, value]) => ({ key, value: String(value) })),
      connections: ((entity.connections ?? []) as Record<string, unknown>[]).map(c => ({
        output: String(c.output ?? ''), target: String(c.target ?? ''), input: String(c.input ?? ''),
        param: c.param === undefined ? '' : String(c.param),
        delay: Number(c.delay) || 0,
        times: c.times === undefined ? -1 : Number(c.times),
      })),
    }));

    data.relink();
    data.setMode('static');
    setStatus(`loaded ${entities.length} entit${entities.length === 1 ? 'y' : 'ies'} from ${file.name}`);
  } catch (error) {
    setStatus(`could not read that file: ${error instanceof Error ? error.message : String(error)}`);
  }
}

function asVector(value: unknown): Vec3 {
  const numbers = Array.isArray(value) ? value.map(Number)
    : typeof value === 'string' ? value.split(/\s+/).map(Number)
      : [];

  return [numbers[0] || 0, numbers[1] || 0, numbers[2] || 0];
}

/** The connections graph, on the same store as everything else. */
export function MapGraph({ components, group, onGroup, onResult }: {
  components: Component[];
  group: string;
  onGroup: (group: string) => void;
  onResult: (result: CommandResult) => void;
}): ReactNode {
  const [, bump] = useState(0);
  useEffect(() => data.onChange(() => bump(n => n + 1)), []);

  return (
    <GraphPane
      entities={data.entities()}
      mode={data.state.mode}
      selectedKey={data.state.selectedKey}
      components={components}
      group={group}
      onGroup={onGroup}
      onSelect={entity => data.select(entity)}
      onChanged={() => data.markDirty()}
      onResult={onResult}
    />
  );
}

/** The groups pane, which captures from the list the tab is showing. */
export function MapGroups({ selection, onResult }: {
  selection: SelectionStore;
  onResult: (result: CommandResult) => void;
}): ReactNode {
  const [, bump] = useState(0);
  useEffect(() => data.onChange(() => bump(n => n + 1)), []);

  return (
    <GroupsPane
      selection={selection}
      entities={data.entities()}
      map={data.state.map}
      mode={data.state.mode}
      onResult={result => onResult(result as CommandResult)}
    />
  );
}

/** The viewports, on the same store as everything else. */
export function MapViewports({ visible }: { visible: boolean }): ReactNode {
  const [, bump] = useState(0);
  useEffect(() => data.onChange(() => bump(n => n + 1)), []);

  return (
    <ViewportPane
      visible={visible}
      entities={data.entities()}
      map={data.state.map}
      selectedKey={data.state.selectedKey}
      onSelect={entity => data.select(entity)}
      loadGeometry={data.loadGeometry}
    />
  );
}

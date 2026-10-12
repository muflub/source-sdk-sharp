/*
 * Talking to the game.
 *
 * One module, so a view's dependencies are visible from its imports rather than
 * from whatever happened to be loaded first — and so the instance selection has
 * exactly one place to live.
 */

import type {
  CommandResult, ConCommand, ConVar, EntityEventPage, Instance, LiveEntity, Service,
  LogPage, MapEntity, MapFile, MapGeometry, MapInfo, MapLumpDetail, NetPayload,
  NetSchema, NetTraffic, NetTypeDetail, PlayerAim, Prediction,
  SaveResult, ScriptedClass,
  ScriptedInstance, SpawnResult, Status,
} from './types';

/*
 * Which instance to address.
 *
 * Empty when the game serves this page itself: there is one game and no choice
 * to make. A proxy in front of several answers /_instances, and then every
 * request carries the pid so two browser tabs can watch two games without
 * fighting over a shared selection.
 */
let instancePid = '';

export const getInstancePid = (): string => instancePid;
export const setInstancePid = (pid: string): void => { instancePid = pid; };

export type Query = Record<string, string | number | undefined | null>;

/** A query string, skipping what was not given. */
export function query(params: Query): string {
  const parts: string[] = [];

  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue;
    parts.push(`${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`);
  }

  return parts.length ? `?${parts.join('&')}` : '';
}

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const url = instancePid
    ? `${path}${path.includes('?') ? '&' : '?'}_pid=${encodeURIComponent(instancePid)}`
    : path;

  const response = await fetch(url, options);

  // A 404 still carries a JSON body saying which endpoint was missing, and that
  // is more useful to a caller than a thrown status.
  if (!response.ok && response.status !== 404) {
    throw new Error(`HTTP ${response.status}`);
  }

  return await response.json() as T;
}

const get = <T>(path: string): Promise<T> => request<T>(path);

const post = <T>(path: string, body: string): Promise<T> =>
  request<T>(path, { method: 'POST', body });

//---------------------------------------------------------------------------
// Reading.
//---------------------------------------------------------------------------

export const getStatus = (): Promise<Status> => get('/status');

/**
 * A stamp of the interface files the game is serving, and whether they can
 * change under it — true only for a debug build reading them off disk.
 *
 * Null rather than a throw when the game does not answer: this drives a
 * convenience, and a failed poll of it should be invisible.
 */
export const getUiStamp = (): Promise<{ stamp: string; live: boolean } | null> =>
  get<{ stamp: string; live: boolean }>('/ui/stamp').catch(() => null);

export const getLog = (since: number, max = 500): Promise<LogPage> =>
  get(`/log${query({ since, max })}`);

export const getConsole = (since: number, max = 500): Promise<LogPage> =>
  get(`/console${query({ since, max })}`);

/**
 * Which registry to list: this mod's, the engine's, or both.
 *
 * The default stays this mod's. The engine's registry is thousands of entries of
 * live state and costs the game a frame to walk, so a page that polls asks for
 * it when somebody wants to see it rather than every few seconds regardless.
 */
export type ConSourceFilter = 'managed' | 'native' | 'all';

export const getConVars = (source?: ConSourceFilter):
  Promise<{ convars?: ConVar[]; count?: number; error?: string }> =>
  get(`/convars${query({ source })}`);

export const getCommands = (source?: ConSourceFilter):
  Promise<{ commands?: ConCommand[]; count?: number; error?: string }> =>
  get(`/commands${query({ source })}`);

export const getEntityClasses = (): Promise<{ classes: ScriptedClass[]; count: number }> =>
  get('/entityclasses');

/**
 * Which of the container's two keys to list.
 *
 * The default stays `type`, because that is the question somebody writing an
 * entity is asking: what may I put in a constructor. The console names are the
 * same convars and commands the other two tabs already show, so they are opt-in
 * rather than mixed into the common view.
 */
export type ServiceLookupFilter = 'type' | 'name' | 'all';

export const getServices = (by?: ServiceLookupFilter):
  Promise<{ services?: Service[]; count?: number; error?: string }> =>
  get(`/services${query({ by })}`);

export const getInstances = (): Promise<{ instances: ScriptedInstance[]; count: number; error?: string }> =>
  get('/instances');

/** Live entities of a class. "*" is every entity the engine holds. */
export const getEntities = (className: string): Promise<{ entities: LiveEntity[]; count: number; error?: string }> =>
  get(`/entities${query({ class: className })}`);

export const getMap = (map?: string): Promise<MapInfo> => get(`/map${query({ map })}`);

export const getMapEntities = (map?: string): Promise<{ entities: MapEntity[]; count: number; error?: string }> =>
  get(`/map/entities${query({ map })}`);

/**
 * Everything in the map file: the header, every lump, and the contents of the
 * ones whose format is known.
 *
 * Read on demand rather than polled. It walks lumps nothing else looks at --
 * the pakfile and the static props among them -- and a map does not change
 * while the game holds it open.
 */
export const getMapFile = (map?: string): Promise<MapFile> =>
  get(`/map/file${query({ map })}`);

/**
 * One lump of a map, decoded.
 *
 * Separate from the report because these are the lumps with tens of thousands of
 * records in them. Asked for when somebody opens one, and not before.
 */
export const getMapLump = (map: string, lump: number): Promise<MapLumpDetail> =>
  get(`/map/lump${query({ map, lump })}`);

export const getMapGeometry = (map?: string): Promise<MapGeometry> =>
  get(`/map/geometry${query({ map })}`);

export const getEvents = (since: number, max = 500): Promise<EntityEventPage> =>
  get(`/events${query({ since, max })}`);

export const getAim = (player = 1): Promise<PlayerAim> => get(`/player/aim${query({ player })}`);

/*
 * The networked schema, and one entity's bytes read back through it.
 *
 * The first two read metadata fixed at startup, so they never wait for a frame.
 * The payload does — it lifts the blob off the entity on the game thread — which
 * is why it is asked for when somebody is looking rather than polled alongside
 * everything else.
 */

export const getNetTables = (): Promise<NetSchema> => get('/nettables');

/**
 * What the payload costs. Needs cs_sv_nettable_profile 1 in the game; the reply
 * says so itself rather than coming back empty, which would read as free.
 */
export const getNetTraffic = (reset = false): Promise<NetTraffic> =>
  get(reset ? '/nettraffic?reset=1' : '/nettraffic');

/** One type by classname or by type id; the server accepts either spelling. */
export const getNetTable = (key: string): Promise<NetTypeDetail> =>
  get(`/nettable/${encodeURIComponent(key)}`);

/**
 * One entity's payload.
 *
 * Each call also becomes the baseline for the next one — the server keeps the
 * last blob it was shown per entity — so what comes back says what moved since
 * the PREVIOUS read, not since the game started. That is what makes an offset
 * bug visible: a field reading the wrong bytes moves when its neighbour is
 * written, and only a diff shows it.
 */
export const getPayload = (index: number): Promise<NetPayload> =>
  get(`/entity/${index}/payload`);

/**
 * What the client's prediction got wrong, newest sequences after `since`.
 *
 * Followed incrementally rather than refetched: the ring holds a few hundred
 * records and asking for all of them every second would redraw a chart out of
 * the same points. `reset` clears the counters, which is the only way to get a
 * window that starts now — the totals are process-wide otherwise.
 */
export const getPrediction = (since?: number, max?: number, reset?: boolean): Promise<Prediction> =>
  get(`/prediction${query({ since, max, reset: reset ? 1 : undefined })}`);

//---------------------------------------------------------------------------
// Writing.
//---------------------------------------------------------------------------

export const runCommand = (command: string): Promise<CommandResult> => post('/command', command);

export const readConVar = (name: string): Promise<{ name: string; value: string; error?: string }> =>
  get(`/convar${query({ name })}`);

export const writeConVar = (name: string, value: string): Promise<{ name: string; value: string; error?: string }> =>
  get(`/convar${query({ name, value })}`);

export const fireInput = (
  target: string, input: string, param?: string, delay?: number | string,
): Promise<CommandResult> => get(`/entity/input${query({ target, input, param, delay })}`);

export const fireOutput = (entity: string, output: string, map?: string): Promise<CommandResult> =>
  get(`/entity/output${query({ entity, output, map })}`);

export const connectEntities = (connection: {
  source: string; output: string; target: string; input: string;
  param?: string; delay?: number; times?: number;
}): Promise<CommandResult> => get(`/entity/connect${query(connection)}`);

export const spawnGroup = (group: unknown): Promise<SpawnResult> =>
  post('/entities/spawn', JSON.stringify(group));

export const saveMap = (map: string, as: string | undefined, entities: unknown): Promise<SaveResult> =>
  post(`/map/save${query({ map, as })}`, JSON.stringify({ entities }));

//---------------------------------------------------------------------------
// Instances, which only exist behind the proxy.
//---------------------------------------------------------------------------

/**
 * The games that are up, or null when this page is served by one directly.
 *
 * Null rather than an empty list, because "there is no proxy" and "the proxy
 * knows of no games" mean different things to the picker.
 */
export async function listInstances(): Promise<Instance[] | null> {
  try {
    const response = await fetch('/_instances');
    if (!response.ok) return null;

    const data = await response.json() as { instances?: Instance[] };
    return data.instances ?? [];
  } catch {
    return null;
  }
}

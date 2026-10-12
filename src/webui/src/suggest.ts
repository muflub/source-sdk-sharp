/*
 * Suggestions for the text boxes, drawn from what the page already knows.
 *
 * Everything typed into this tool is a name that exists somewhere else: a
 * classname the map uses, an input some entity already answers to, a key that
 * every other entity of the same class carries. None of it needs to be
 * remembered or guessed at, because the answer is in data already loaded — so
 * these lists are derived from that data rather than kept as a table of things
 * someone thought of once.
 *
 * Native <datalist> rather than a custom dropdown. It gets keyboard handling,
 * filtering and placement right on every platform for free, and a debugging tool
 * is a poor place to spend that effort. The one thing it cannot do is match on
 * part of a value, so the console box — where what is typed is a whole command
 * line — rebuilds its options as you type, with each option a complete line.
 */

/*
 * What this reads, and no more.
 *
 * Structural rather than the API types: a listing carries help text, flags and
 * live counts, and a suggestion list is built from a name. Asking for the whole
 * shape would tie these lists to endpoints they do not use.
 */
interface Named { name: string }

interface DeclaredClass { className: string; inputs?: string[]; outputs?: string[] }

/** Likewise the entities: a classname, a name, its keys and what it fires. */
interface Described {
  classname: string;
  targetname: string;
  keyvalues: { key: string; value: string }[];
  connections: { output: string; target: string; input: string }[];
}

/** Inputs every entity understands, for when the map offers no evidence. */
const COMMON_INPUTS = [
  'Kill', 'KillHierarchy', 'AddOutput', 'FireUser1', 'FireUser2', 'FireUser3', 'FireUser4',
  'Enable', 'Disable', 'Toggle', 'Use', 'SetParent', 'ClearParent', 'Alpha', 'Color',
];

/** Keys almost every entity accepts, so a new one is not a blank page. */
const COMMON_KEYS = [
  'targetname', 'origin', 'angles', 'spawnflags', 'parentname', 'globalname',
  'rendercolor', 'renderamt', 'rendermode', 'disableshadows', 'StartDisabled',
];

const index = {
  classnames: [] as string[],
  targetnames: [] as string[],
  inputs: [] as string[],
  outputs: [] as string[],
  commands: [] as string[],
  convars: [] as string[],
  maps: [] as string[],

  // Keyed by classname, then by key: what other entities of the same class
  // carry, which is by far the best evidence for what this one should.
  keysByClass: new Map<string, Set<string>>(),
  valuesByKey: new Map<string, Set<string>>(),
};

/** Where the datalists live. One host, so nothing has to tidy up after itself. */
function host(): HTMLElement {
  const existing = document.getElementById('suggestions');
  if (existing) return existing;

  const node = document.createElement('div');
  node.id = 'suggestions';
  node.hidden = true;
  document.body.appendChild(node);

  return node;
}

function listFor(id: string, values: Iterable<string>): HTMLDataListElement {
  let list = document.getElementById(id) as HTMLDataListElement | null;

  if (!list) {
    list = document.createElement('datalist');
    list.id = id;
    host().appendChild(list);
  }

  list.textContent = '';

  // Capped. A datalist of six hundred entries is slower to open than it is to
  // type the thing you were looking for.
  for (const value of [...values].slice(0, 200)) {
    const option = document.createElement('option');
    option.value = value;
    list.appendChild(option);
  }

  return list;
}

const sorted = (values: Iterable<string>): string[] =>
  [...values].filter(Boolean).sort((a, b) => a.localeCompare(b));

//---------------------------------------------------------------------------
// Building the indexes.
//---------------------------------------------------------------------------

/**
 * Rebuilds everything derived from the entity lists.
 *
 * Given both lists rather than reading the store, so that what suggests and what
 * holds the map stay separable — and cheap enough to run on any change.
 */
export function refreshFromMap(statics: Described[], lives: Described[], maps: string[]): void {
  const classnames = new Set<string>();
  const targetnames = new Set<string>();
  const inputs = new Set(COMMON_INPUTS);
  const outputs = new Set<string>();

  index.keysByClass = new Map();
  index.valuesByKey = new Map();

  // Both lists, not just the one on screen: a classname that exists live is
  // worth suggesting while editing the map, and the other way round.
  for (const entity of [...statics, ...lives]) {
    classnames.add(entity.classname);
    if (entity.targetname) targetnames.add(entity.targetname);

    for (const connection of entity.connections || []) {
      outputs.add(connection.output);
      inputs.add(connection.input);
      if (connection.target) targetnames.add(connection.target);
    }

    let keys = index.keysByClass.get(entity.classname);

    if (!keys) {
      keys = new Set();
      index.keysByClass.set(entity.classname, keys);
    }

    for (const kv of entity.keyvalues || []) {
      keys.add(kv.key);

      let values = index.valuesByKey.get(kv.key.toLowerCase());

      if (!values) {
        values = new Set();
        index.valuesByKey.set(kv.key.toLowerCase(), values);
      }

      // A key with hundreds of distinct values — an origin, a model path — is
      // not one anybody completes from a list.
      if (values.size < 60) values.add(kv.value);
    }
  }

  index.classnames = sorted(classnames);
  index.targetnames = sorted(targetnames);
  index.inputs = sorted(inputs);
  index.outputs = sorted(outputs);
  index.maps = maps;

  listFor('sg-classnames', index.classnames);
  listFor('sg-targetnames', index.targetnames);
  listFor('sg-inputs', index.inputs);
  listFor('sg-outputs', index.outputs);
  listFor('sg-maps', index.maps);
}

/** The console command names, which come from their own endpoints. */
export function refreshFromListings({ commands, convars, entityClasses }: {
  commands?: Named[];
  convars?: Named[];
  entityClasses?: DeclaredClass[];
}): void {
  if (commands) index.commands = sorted(new Set(commands.map(c => c.name)));
  if (convars) index.convars = sorted(new Set(convars.map(v => v.name)));

  if (entityClasses) {
    // The classes this mod implements are placeable even when the current map
    // contains none of them, so they belong in the classname list.
    const declared = new Set([...index.classnames, ...entityClasses.map(c => c.className)]);
    index.classnames = sorted(declared);
    listFor('sg-classnames', index.classnames);

    const inputs = new Set(index.inputs);
    const outputs = new Set(index.outputs);

    for (const info of entityClasses) {
      for (const name of info.inputs || []) inputs.add(name);
      for (const name of info.outputs || []) outputs.add(name);
    }

    index.inputs = sorted(inputs);
    index.outputs = sorted(outputs);
    listFor('sg-inputs', index.inputs);
    listFor('sg-outputs', index.outputs);
  }

  listFor('sg-commands', [...index.commands, ...index.convars].sort((a, b) => a.localeCompare(b)));
}

//---------------------------------------------------------------------------
// Attaching.
//---------------------------------------------------------------------------

/**
 * Points an input at a suggestion list.
 *
 * The two keyed kinds build their list on focus rather than up front: there is
 * one per classname and one per key, and building all of them for a map would
 * be hundreds of lists nobody opens.
 */
export type SuggestKind =
  | 'classname' | 'targetname' | 'input' | 'output' | 'command' | 'map' | 'key' | 'value';

export function attach(input: HTMLInputElement, kind: SuggestKind, context?: string): HTMLInputElement {
  switch (kind) {
    case 'classname':  input.setAttribute('list', 'sg-classnames'); break;
    case 'targetname': input.setAttribute('list', 'sg-targetnames'); break;
    case 'input':      input.setAttribute('list', 'sg-inputs'); break;
    case 'output':     input.setAttribute('list', 'sg-outputs'); break;
    case 'command':    input.setAttribute('list', 'sg-commands'); break;
    case 'map':        input.setAttribute('list', 'sg-maps'); break;

    case 'key': {
      const id = 'sg-key-' + slug(context || '');
      input.setAttribute('list', id);
      input.addEventListener('focus', () => {
        const own = index.keysByClass.get(context ?? '') ?? new Set<string>();
        listFor(id, sorted(new Set([...own, ...COMMON_KEYS])));
      });
      break;
    }

    case 'value': {
      const id = 'sg-value-' + slug(context || '');
      input.setAttribute('list', id);
      input.addEventListener('focus', () => {
        listFor(id, sorted(index.valuesByKey.get((context || '').toLowerCase()) ?? new Set<string>()));
      });
      break;
    }
  }

  return input;
}

const slug = (text: string): string => text.replace(/[^\w-]+/g, '_') || 'none';

/** Every input name known, for a caller building its own list. */
export const allInputs = (): string[] => index.inputs;

/** Every classname known, likewise. */
export const allClassnames = (): string[] => index.classnames;

//---------------------------------------------------------------------------
// The console, which completes whole command lines.
//---------------------------------------------------------------------------

/**
 * Rebuilds the console's options for what has been typed so far.
 *
 * A datalist matches against the whole value, so completing the second word of
 * a command means offering options that already contain the first. Rebuilding
 * as you type is what makes "ent_fire pl" offer "ent_fire plug_pull".
 */
export function refreshConsole(typed: string): void {
  const text = typed || '';
  const space = text.indexOf(' ');

  if (space < 0) {
    // Filtered by what has been typed before the cap is applied, not after. The
    // list used to be this mod's sixty and the cap never bit; it can now be the
    // engine's several thousand, and capping first would offer two hundred names
    // beginning with "a" to somebody who has typed "sv_g".
    listFor('sg-console', [...index.commands, ...index.convars]
      .filter(name => name.toLowerCase().startsWith(text.toLowerCase()))
      .sort((a, b) => a.localeCompare(b)));
    return;
  }

  const verb = text.slice(0, space).toLowerCase();
  const rest = text.slice(space + 1);

  const complete = (values: Iterable<string>): void => void listFor('sg-console',
    [...values].filter(value => value.toLowerCase().startsWith(rest.toLowerCase()))
      .map(value => `${text.slice(0, space)} ${value}`));

  if (verb === 'map' || verb === 'changelevel') return complete(index.maps);
  if (verb === 'ent_fire' || verb === 'ent_remove' || verb === 'ent_dump') return complete(index.targetnames);
  if (verb === 'ent_create' || verb === 'ent_info') return complete(index.classnames);

  // A convar being given a value: its current one is the most useful thing to
  // offer, since the usual edit is a small change to it.
  const convar = index.convars.find(name => name.toLowerCase() === verb);
  if (convar) return complete(sorted(index.valuesByKey.get(verb) ?? new Set<string>()));

  listFor('sg-console', []);
}

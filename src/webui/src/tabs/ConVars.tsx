/*
 * The convars, with an editor typed to each.
 *
 * A bool as a checkbox and a float as a number field is not decoration: the
 * declared type is the only thing that says whether "2" is a valid value, and
 * offering the wrong control is how a debugging tool teaches someone the wrong
 * thing about their own game.
 *
 * Which is exactly why the engine's own convars get a text box and nothing more.
 * They can be listed now — the source picker asks the game to walk its registry
 * — but nobody ever declared a type for one, so the honest control is the one
 * the console offers: a line of text. A 0/1 convar shown as a checkbox would be
 * this tool inventing a rule the game does not have.
 */

import { useEffect, useMemo, useState, type ReactNode } from 'react';

import { getConVars, writeConVar, type ConSourceFilter } from '../api';
import { FilterBox } from '../components/FilterBox';
import { FlagFilter, flagsIn, matchesFlag } from '../components/FlagFilter';
import { LoadedAt } from '../components/LoadedAt';
import { SortHeader, useSort, useSorted } from '../components/Sortable';
import { useListing } from '../hooks/useListing';
import type { ConVar } from '../types';
import { useUrlFlag, useUrlState } from '../hooks/useUrlState';

type Saved = 'idle' | 'saved' | 'failed';

export function ConVarsTab({ visible, onLoaded }: {
  visible: boolean;
  onLoaded?: (convars: ConVar[]) => void;
}): ReactNode {
  const [filter, setFilter] = useUrlState('find');
  const [changedOnly, setChangedOnly] = useUrlFlag('changed');
  const [type, setType] = useUrlState('type');
  const [flag, setFlag] = useUrlState('flag');
  const [source, setSource] = useUrlState('source', 'managed');
  const sort = useSort<SortKey>('name');

  const { items: loaded, reload: load, waiting, loadedAt } = useListing(async () => {
    const data = await getConVars(source as ConSourceFilter);

    // Absent rather than empty when the game could not answer in time: walking
    // the engine's registry needs a frame, and a game sitting at a menu is not
    // running any. An empty list keeps the retry going, which is what should
    // happen; a crash on an undefined list is not.
    const convars = data.convars ?? [];

    onLoaded?.(convars);
    return convars;
  }, visible);

  // The listing is read once and then left alone, so changing which registry it
  // is reading has to go and ask again — nothing else would.
  useEffect(() => { void load(); }, [source, load]);

  /*
   * The values, with anything written from here applied over what was read.
   *
   * Kept beside the listing rather than inside it: the listing is re-read
   * whenever the game is asked again, and an edit made a moment ago should
   * survive that rather than flicker back to the old value.
   */
  const [edits, setEdits] = useState<Record<string, string>>({});

  const convars = useMemo(
    () => loaded.map(convar => convar.name in edits
      ? { ...convar, value: edits[convar.name]! }
      : convar),
    [loaded, edits]);

  /** What the filter box suggests: the names in front of you. */
  const names = useMemo(() => convars.map(convar => convar.name), [convars]);

  /** The types actually present, so the filter offers what there is. */
  const types = useMemo(
    () => [...new Set(convars.map(convar => convar.type))].sort((a, b) => a.localeCompare(b)),
    [convars]);

  /** Likewise the flags: what is in front of you, not all thirty-two. */
  const flags = useMemo(() => flagsIn(convars), [convars]);

  const shown = useMemo(() => {
    const text = filter.trim().toLowerCase();

    const matching = convars.filter(convar => {
      if (changedOnly && convar.value === convar.default) return false;
      if (type !== '' && convar.type !== type) return false;
      if (!matchesFlag(convar.flags, flag)) return false;

      return text === ''
        || convar.name.toLowerCase().includes(text)
        || convar.help.toLowerCase().includes(text);
    });

    return matching;
  }, [convars, filter, changedOnly, type, flag]);

  const ordered = useSorted(shown, sort, valueOf, convar => convar.name);

  /** The value came back from the engine, so the row shows what it really holds. */
  const applied = (name: string, value: string): void =>
    setEdits(previous => ({ ...previous, [name]: value }));

  return (
    <>
      <div className="toolbar">
        {/* Suggested from the convars actually listed, rather than from every
            command the game knows: this box filters what is on screen. */}
        <FilterBox
          value={filter}
          options={names}
          placeholder="filter convars"
          onChange={setFilter}
        />
        <label>
          <input
            type="checkbox"
            checked={changedOnly}
            onChange={event => setChangedOnly(event.target.checked)}
          /> changed from default
        </label>
        <label>
          type
          <select value={type} onChange={event => setType(event.target.value)}>
            <option value="">any</option>
            {types.map(name => <option key={name} value={name}>{name}</option>)}
          </select>
        </label>
        <FlagFilter value={flag} options={flags} onChange={setFlag} />
        {/* Not a checkbox, because these are three different questions and only
            one of them is free: this mod's convars are managed objects and are
            listed without asking the game for anything. */}
        <label>
          source
          <select value={source} onChange={event => setSource(event.target.value)}>
            <option value="managed">managed</option>
            <option value="native">native</option>
            <option value="all">both</option>
          </select>
        </label>
        <button onClick={() => void load()}>Refresh</button>
        <LoadedAt at={loadedAt} />
        <span className="count">
          {waiting ? 'waiting for the game…' : `${shown.length} of ${convars.length}`}
        </span>
      </div>

      <p className="note">
        Values are read back from the engine after every write, so what is shown is
        what the convar actually holds &mdash; not what it was asked for.
        {source !== 'managed' && <>
          {' '}<b>native</b> convars are listed as the engine holds them: no declared
          type, so a text box, and a value from the moment the list was read rather
          than a live one.
          {source === 'native' && <>
            {' '}The <b>managed</b> ones are not in this list at all &mdash; they are in
            the engine&rsquo;s table too, but the typed listing shows them properly.
          </>}
        </>}
      </p>

      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              {COLUMNS.map(([key, label]) => (
                <SortHeader key={key} column={key} label={label} sort={sort} />
              ))}
              <th>Help</th>
              <th className="actions-head"><span className="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            {ordered.map(convar => <Row key={convar.name} convar={convar} onApplied={applied} />)}
            {ordered.length === 0 && (
              <tr><td className="empty" colSpan={8}>no convars match</td></tr>
            )}
          </tbody>
        </table>
      </div>
    </>
  );
}

function Row({ convar, onApplied }: {
  convar: ConVar;
  onApplied: (name: string, value: string) => void;
}): ReactNode {
  const [saved, setSaved] = useState<Saved>('idle');

  const modified = convar.value !== convar.default;

  const commit = async (value: string): Promise<void> => {
    setSaved('idle');

    try {
      const result = await writeConVar(convar.name, value);
      if (result.error) throw new Error(result.error);

      // The read-back, not what was sent: a convar can clamp or reject, and
      // showing the requested value would hide exactly that.
      onApplied(convar.name, result.value);
      setSaved('saved');
    } catch {
      setSaved('failed');
    }
  };

  return (
    <tr className={[
      modified ? 'changed' : '',
      convar.registered ? '' : 'unregistered',
      convar.source === 'native' ? 'native' : '',
    ].join(' ').trim()}>
      <td className="name">{convar.name}</td>
      <td>
        <div className={`value-cell ${saved === 'idle' ? '' : saved}`}>
          <Editor convar={convar} onCommit={commit} />

          {/* Beside the value, because "this one has been changed" is a fact
              about the value and belongs where the eye already is. The Default
              column says what it would be; this says that it is not that. */}
          <span
            className="modified-dot"
            hidden={!modified}
            title={`changed from its default of ${convar.default === '' ? '(empty)' : convar.default}`}
          >●</span>
        </div>
      </td>
      <td className="mono">{convar.default === '' ? '""' : convar.default}</td>
      <td className="mono">{convar.type}</td>
      <td className="flags">
        {convar.flags && convar.flags !== 'None'
          ? convar.flags.split(/[,|]\s*/).map(flag => (
            <span className="pill" key={flag}>{flag}</span>
          ))
          : <span className="dim">—</span>}
      </td>

      {/* Where the row came from, in a column of its own: a C# class for a
          managed convar, the DLL that registered it for a native one. In a
          pill for the native ones, because the difference between a class and
          a DLL is the difference between an editor that knows the type and one
          that can only offer text — and it should be readable down the column
          rather than only in the row you happen to be on. */}
      <td className="mono">
        {convar.source === 'native'
          ? <span className="pill" title="native: registered by the engine, with no declared type and no C# class behind it">
              {convar.declaredBy || 'engine'}
            </span>
          : convar.declaredBy || '—'}
      </td>

      <td className="help">
        {convar.help || '—'}
        {!convar.registered && <> <span className="pill">the engine refused this convar; writes go nowhere</span></>}
      </td>

      <td className="actions">
        <RowMenu
          disabled={!convar.registered}
          onDefault={() => void commit(convar.default)}
          defaultValue={convar.default}
          canReset={modified}
        />
      </td>
    </tr>
  );
}

/**
 * The control for one convar, chosen by its declared type.
 *
 * Uncontrolled, keyed on the value the engine last reported: typing is local
 * until it is committed, and a poll that brought back the same value should not
 * reach in and move the cursor.
 */
/** What a convar clamps to, in words, or null when it clamps to nothing. */
function describeBounds(convar: ConVar): string | null {
  if (convar.min === null && convar.max === null) return null;

  if (convar.min !== null && convar.max !== null) return `the engine clamps this to ${convar.min}–${convar.max}`;

  return convar.min !== null
    ? `the engine clamps this to ${convar.min} and above`
    : `the engine clamps this to ${convar.max} and below`;
}

/** The columns worth ordering by. Help is prose, and sorting prose is noise. */
const COLUMNS: [SortKey, string][] = [
  ['name', 'ConVar'],
  ['value', 'Value'],
  ['default', 'Default'],
  ['type', 'Type'],
  ['flags', 'Flags'],
  ['declaredBy', 'Declared by'],
];

type SortKey = 'name' | 'value' | 'default' | 'type' | 'flags' | 'declaredBy';

/** What a column holds, for ordering by it. */
const valueOf = (convar: ConVar, key: SortKey): string =>
  key === 'value' ? convar.value
    : key === 'default' ? convar.default
      : key === 'type' ? convar.type
        : key === 'flags' ? convar.flags
          : key === 'declaredBy' ? convar.declaredBy
            : convar.name;

/**
 * A row's actions.
 *
 * Out of the value cell and behind a mark on the right: resetting a convar is
 * something you do occasionally, and a button per row for it put a permanent
 * column of Defaults down the middle of the values you were trying to read.
 *
 * Opened by hovering, and by focus for anyone on a keyboard — both in CSS, so
 * there is no open state to hold, nothing to close when the pointer leaves, and
 * nothing to get stuck open when a row is filtered away underneath it.
 */
function RowMenu({ disabled, canReset, defaultValue, onDefault }: {
  disabled: boolean;
  canReset: boolean;
  defaultValue: string;
  onDefault: () => void;
}): ReactNode {
  return (
    <div className="row-menu">
      <button className="row-menu-trigger" aria-haspopup="menu" title="Actions">⋯</button>

      <div className="row-menu-items" role="menu">
        <button
          role="menuitem"
          disabled={disabled || !canReset}
          title={canReset
            ? `set back to ${defaultValue === '' ? '(empty)' : defaultValue}`
            : 'already at its default'}
          onClick={onDefault}
        >Reset to default</button>
      </div>
    </div>
  );
}

function Editor({ convar, onCommit }: {
  convar: ConVar;
  onCommit: (value: string) => void;
}): ReactNode {
  const disabled = !convar.registered;

  if (convar.type === 'bool') {
    return (
      <input
        type="checkbox"
        disabled={disabled}
        checked={convar.value !== '0' && convar.value !== ''}
        onChange={event => onCommit(event.target.checked ? '1' : '0')}
      />
    );
  }

  const numeric = convar.type === 'int' || convar.type === 'float';

  // The bounds go on the box because the engine clamps to them on the way in,
  // from every source at once. This is not a check of our own — a write that
  // broke them would come back clamped anyway — it is the box saying what the
  // value is allowed to be before somebody sends one that will not stick.
  return (
    <input
      key={convar.value}
      title={describeBounds(convar) ?? (convar.type === 'unknown'
        ? 'the engine states no type for its own convars, so this is what the console offers: text'
        : undefined)}
      type={numeric ? 'number' : 'text'}
      {...(convar.type === 'float' ? { step: 'any' } : {})}
      {...(convar.min !== null ? { min: convar.min } : {})}
      {...(convar.max !== null ? { max: convar.max } : {})}
      disabled={disabled}
      defaultValue={convar.value}
      onChange={event => { if (event.target.type === 'checkbox') onCommit(event.target.value); }}
      onBlur={event => { if (event.target.value !== convar.value) onCommit(event.target.value); }}
      onKeyDown={event => { if (event.key === 'Enter') event.currentTarget.blur(); }}
    />
  );
}

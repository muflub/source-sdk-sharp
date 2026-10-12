/*
 * The console commands, this mod's and the engine's.
 *
 * Read from the registries that registered them, so this is what the engine was
 * actually told rather than what the source appears to say. The engine's own
 * thousands are listed too, when asked for: walking its table costs the game a
 * frame, so it is opt-in rather than something a polling page does anyway.
 *
 * Flags earn a column here more than they do among the convars. A command
 * marked Cheat does nothing at all without sv_cheats — it does not complain, it
 * does not fail, it simply does not happen — and a debugging tool that offers a
 * Run button for one without saying so has set a trap.
 */

import { useEffect, useMemo, type ReactNode } from 'react';

import { getCommands, type ConSourceFilter } from '../api';
import type { ConCommand } from '../types';
import { FilterBox } from '../components/FilterBox';
import { FlagFilter, flagsIn, matchesFlag } from '../components/FlagFilter';
import { LoadedAt } from '../components/LoadedAt';
import { SortHeader, useSort, useSorted } from '../components/Sortable';
import { useListing } from '../hooks/useListing';
import { useUrlState } from '../hooks/useUrlState';

export function CommandsTab({ visible, onRun, onPrime, onLoaded }: {
  visible: boolean;
  onRun: (command: string) => void;
  onPrime: (command: string) => void;
  onLoaded?: (commands: ConCommand[]) => void;
}): ReactNode {
  const [filter, setFilter] = useUrlState('find');
  const [flag, setFlag] = useUrlState('flag');
  const [source, setSource] = useUrlState('source', 'managed');

  const sort = useSort<SortKey>('name');

  const { items: commands, reload: load, waiting, loadedAt } = useListing(async () => {
    const data = await getCommands(source as ConSourceFilter);

    // Absent rather than empty when the game could not answer in time: the
    // engine's table is walked on the game thread, and a game running no frames
    // answers nothing. An empty list retries; an undefined one would throw.
    const list = data.commands ?? [];

    onLoaded?.(list);
    return list;
  }, visible);

  // Nothing else would ask again: a listing is read once and then left alone.
  useEffect(() => { void load(); }, [source, load]);

  /** What the filter box suggests. */
  const names = useMemo(() => commands.map(command => command.name), [commands]);

  /** The flags present, so the picker offers what there is rather than all 32. */
  const flags = useMemo(() => flagsIn(commands), [commands]);

  const shown = useMemo(() => {
    const text = filter.trim().toLowerCase();

    return commands.filter(command => {
      if (!matchesFlag(command.flags, flag)) return false;

      return text === ''
        || command.name.toLowerCase().includes(text)
        || command.help.toLowerCase().includes(text);
    });
  }, [commands, filter, flag]);

  const ordered = useSorted(shown, sort, valueOf, command => command.name);

  return (
    <>
      <div className="toolbar">
        {/* Suggested from the commands in front of you, and only once three
            characters have been typed — the same box the convars use. */}
        <FilterBox
          value={filter}
          options={names}
          placeholder="filter commands"
          onChange={setFilter}
        />
        {/* The most useful question a four-thousand-row listing can be asked:
            which of these will not run without sv_cheats. */}
        <FlagFilter value={flag} options={flags} onChange={setFlag} />
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
          {waiting ? 'waiting for the game…' : `${shown.length} of ${commands.length}`}
        </span>
      </div>

      <p className="note">
        {source === 'managed'
          ? <><b>managed</b> commands are the ones this mod registers. Ask for
              <b> native</b> to see the engine&rsquo;s, or run anything at all from the
              box at the bottom &mdash; the console takes whatever the game does.</>
          : <><b>native</b> commands are everything the engine and the game DLLs
              registered. One marked <span className="pill">Cheat</span> does nothing
              without <code>sv_cheats 1</code>, and says nothing when it does
              nothing.{source === 'native' && ' The managed ones are not in this list.'}</>}
      </p>

      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <SortHeader column="name" label="Command" sort={sort} />
              <th>Help</th>
              <SortHeader column="declaredBy" label="Declared by" sort={sort} />
              <SortHeader column="flags" label="Flags" sort={sort} />
              <th />
            </tr>
          </thead>
          <tbody>
            {ordered.map(command => (
              <tr key={command.name} className={command.source === 'native' ? 'native' : ''}>
                <td className="name">{command.name}</td>
                <td className="help">
                  {command.help || '—'}
                  {command.registered === false && <> <span className="pill">the engine refused this command; nothing runs it</span></>}
                </td>

                {/* Where it came from, and nothing appended to the name: a C#
                    class for one of ours, the DLL that registered it for one of
                    the engine's, in a pill so the two read apart down the
                    column. */}
                <td className="mono">
                  {command.source === 'native'
                    ? <span className="pill" title="native: registered by the engine or a game DLL">
                        {command.declaredBy || 'engine'}
                      </span>
                    : command.declaredBy || '—'}
                </td>
                <td className="flags">
                  {command.flags
                    ? command.flags.split(/[,|]\s*/).map(flag => (
                      <span className="pill" key={flag}>{flag}</span>
                    ))
                    : <span className="dim">—</span>}
                </td>
                <td>
                  {/* Two buttons rather than one: a command that takes arguments
                      and one that does not are used completely differently. */}
                  <button disabled={command.registered === false}
                          onClick={() => onRun(command.name)}>Run</button>
                  <button disabled={command.registered === false}
                          onClick={() => onPrime(`${command.name} `)}>Edit</button>
                </td>
              </tr>
            ))}
            {ordered.length === 0 && (
              <tr><td className="empty" colSpan={5}>no commands match</td></tr>
            )}
          </tbody>
        </table>
      </div>
    </>
  );
}

/** Help is prose, and ordering prose is noise; the others are names. */
type SortKey = 'name' | 'declaredBy' | 'flags';

const valueOf = (command: ConCommand, key: SortKey): string =>
  key === 'declaredBy' ? command.declaredBy
    : key === 'flags' ? command.flags
      : command.name;

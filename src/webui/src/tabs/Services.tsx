/*
 * The server's service container: what an entity may ask for.
 *
 * This is the view the container otherwise makes harder rather than easier.
 * Before it, what an entity could reach was whatever statics happened to be in
 * scope — at least a compiler would list those. Now it is whatever the module
 * registered, and without this the only way to find out is to read
 * ServerModule's Initialize.
 *
 * Two keys, and they are not interchangeable, which is why the picker exists
 * rather than one merged list. A CONTRACT is asked for by type and checked by
 * the compiler. A convar or a command is asked for by the NAME a player types,
 * which nothing checks. Somebody writing an entity is nearly always asking the
 * first question, so that is the default.
 *
 * The SERVER's container, and only the server's. The client has one too, with
 * entities resolving from it, but no socket of its own — as with every other
 * managed listing here. Said out loud on the page, because "services" sounds
 * like it would cover both.
 */

import { useEffect, useMemo, type ReactNode } from 'react';

import { getServices, type ServiceLookupFilter } from '../api';
import type { Service } from '../types';
import { FilterBox } from '../components/FilterBox';
import { LoadedAt } from '../components/LoadedAt';
import { SortHeader, useSort, useSorted } from '../components/Sortable';
import { useListing } from '../hooks/useListing';
import { useUrlState } from '../hooks/useUrlState';

export function ServicesTab({ visible, onLoaded }: {
  visible: boolean;
  onLoaded?: (services: Service[]) => void;
}): ReactNode {
  const [filter, setFilter] = useUrlState('find');
  const [by, setBy] = useUrlState('by', 'type');
  const [kind, setKind] = useUrlState('kind');

  const sort = useSort<SortKey>('key');

  const { items: services, reload: load, waiting, loadedAt } = useListing(async () => {
    const data = await getServices(by as ServiceLookupFilter);

    // Absent rather than empty when the server did not supply the listing: an
    // empty list would read as "the container is empty", which is a different
    // and much more alarming thing than "this build does not answer".
    const list = data.services ?? [];

    onLoaded?.(list);
    return list;
  }, visible);

  useEffect(() => { void load(); }, [by, load]);

  /** What the filter box suggests. */
  const keys = useMemo(() => services.map(service => service.key), [services]);

  /**
   * The kinds present, for the picker to offer.
   *
   * Only the ones actually there, the same way the flag picker works: a filter
   * that can be set to something no row satisfies spends its life showing
   * nothing and looking broken.
   */
  const kinds = useMemo(
    () => [...new Set(services.map(service => service.kind))].sort((a, b) => a.localeCompare(b)),
    [services]);

  const shown = useMemo(() => {
    const text = filter.trim().toLowerCase();

    return services.filter(service => {
      if (kind !== '' && service.kind !== kind) return false;

      return text === ''
        || service.key.toLowerCase().includes(text)
        || service.implementation.toLowerCase().includes(text);
    });
  }, [services, filter, kind]);

  const ordered = useSorted(shown, sort, valueOf, service => service.key);

  return (
    <>
      <div className="toolbar">
        <FilterBox
          value={filter}
          options={keys}
          placeholder="filter services"
          onChange={setFilter}
        />
        {/* Every convar this mod declares is askable by its own class, so the
            type view is mostly convars — fifteen of them against seven actual
            systems today, and the gap only widens. Narrowing by kind is what
            makes "what systems are there" answerable at a glance. */}
        <label>
          kind
          <select value={kind} onChange={event => setKind(event.target.value)}>
            <option value="">any</option>
            {kinds.map(name => <option key={name} value={name}>{name}</option>)}
          </select>
        </label>
        <label>
          asked for by
          <select value={by} onChange={event => setBy(event.target.value)}>
            <option value="type">type</option>
            <option value="name">console name</option>
            <option value="all">both</option>
          </select>
        </label>
        <button onClick={() => void load()}>Refresh</button>
        <LoadedAt at={loadedAt} />
        <span className="count">
          {waiting ? 'waiting for the game…' : `${shown.length} of ${services.length}`}
        </span>
      </div>

      <p className="note">
        {by === 'name'
          ? <><b>console names</b> are what a <code>[Named(&quot;sv_gravity&quot;)]</code> parameter
              resolves. Only this mod&rsquo;s convars and commands are in the container &mdash;
              the engine&rsquo;s thousands are <i>not</i>, so a name in the ConVars tab is not
              necessarily one an entity can ask for.</>
          : <><b>contracts</b> are what a constructor parameter names, and the compiler checks
              them. This is the <b>server&rsquo;s</b> container; the client has its own, which
              has no socket to list it through.</>}
      </p>

      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <SortHeader column="key" label="Asked for as" sort={sort} />
              <SortHeader column="kind" label="Kind" sort={sort} />
              <SortHeader column="implementation" label="Answered by" sort={sort} />
              <th>State</th>
            </tr>
          </thead>
          <tbody>
            {ordered.map(service => (
              <tr key={`${service.lookedUpBy}:${service.key}`}>
                <td className="name">{service.key}</td>
                <td className="flags">
                  <span className="pill">{service.kind}</span>
                  {/* Which key this row is, so the merged view is readable. Only
                      there when both are shown — in a filtered view every row
                      has the same answer and the column would be noise. */}
                  {by === 'all' && <> <span className="pill">{service.lookedUpBy}</span></>}
                </td>
                <td className="mono">{service.implementation}</td>
                <td>
                  {/* Unbuilt is not a fault and must not read as one: laziness is
                      the point, and a service nothing has needed is one nothing
                      has paid for. */}
                  {service.built
                    ? <span className="dim">built</span>
                    : <span className="pill" title="a lazy registration nothing has asked for yet; it is built on the first ask">
                        not built yet
                      </span>}
                </td>
              </tr>
            ))}
            {ordered.length === 0 && (
              <tr><td className="empty" colSpan={4}>no services match</td></tr>
            )}
          </tbody>
        </table>
      </div>
    </>
  );
}

/** Every column here is a name; none of them is prose. */
type SortKey = 'key' | 'kind' | 'implementation';

const valueOf = (service: Service, key: SortKey): string =>
  key === 'kind' ? service.kind
    : key === 'implementation' ? service.implementation
      : service.key;

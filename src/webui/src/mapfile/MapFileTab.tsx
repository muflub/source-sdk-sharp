/*
 * The map file, whole.
 *
 * The entity debugger reads a map for what it declares. This reads it for what
 * it is: a header, sixty-four lumps, and the contents of the ones whose format
 * is known. Two different questions, so two tabs — "what does this map do" and
 * "what is in this file" send you looking in different directions, and a view
 * that answered both would answer neither well.
 *
 * Nothing here is polled. A map on disk does not change while the game holds it
 * open, and the file being looked at is very often not the one that is loaded —
 * which is the other reason this is worth having: it needs no running game at
 * all.
 */

import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';

import { getMap, getMapEntities, getMapFile, getMapLump } from '../api';
import { FilterBox } from '../components/FilterBox';
import { LoadedAt } from '../components/LoadedAt';
import { useUrlFlag, useUrlState } from '../hooks/useUrlState';
import { save as download } from '../map/download';
import type { MapEntity, MapFile, MapLumpDetail } from '../types';
import { LumpTable, type Order } from './LumpTable';
import { SectionTable } from './SectionTable';
import { bytes, count, detailSections, sectionsForLump, sectionsOf } from './sections';

/**
 * The lumps that are read on their own, when opened.
 *
 * Everything else in the file arrives with the report because it is small enough
 * to. These are not: a medium map has six thousand faces and sixteen thousand
 * brush sides, and three lumps here are arrays of the same face record.
 */
const READ_ON_DEMAND = new Set([6, 7, 10, 18, 19, 27, 58]);

export function MapFileTab({ visible }: { visible: boolean }): ReactNode {
  /*
   * Which map, in the address bar under a name of its own.
   *
   * Not shared with the entity debugger's map: the two tabs are very often
   * pointed at different files on purpose — the one that is loaded, and the one
   * that was just compiled — and having them move together would make that
   * impossible to hold.
   */
  const [map, setMap] = useUrlState('file');
  const [filter, setFilter] = useUrlState('lumpfind');
  const [lumpText, setLump] = useUrlState('lump');
  const [order, setOrder] = useUrlState('lumporder', 'file');
  const [showEmpty, setShowEmpty] = useUrlFlag('empty');
  const [decodedOnly, setDecodedOnly] = useUrlFlag('readable');

  const [available, setAvailable] = useState<string[]>([]);
  const [file, setFile] = useState<MapFile | null>(null);
  const [entities, setEntities] = useState<MapEntity[]>([]);
  const [error, setError] = useState('');
  const [reading, setReading] = useState(false);
  const [loadedAt, setLoadedAt] = useState<Date | null>(null);

  /*
   * The big lumps, once each.
   *
   * Kept by lump rather than replaced, so stepping between faces and brushes and
   * back does not re-read either. Thrown away whole when the map changes, which
   * is the only time any of it can be wrong.
   */
  const [details, setDetails] = useState<Record<number, MapLumpDetail>>({});
  const [readingLump, setReadingLump] = useState<number | null>(null);

  const selected = lumpText === '' ? null : Number(lumpText);

  const load = useCallback(async (name: string): Promise<void> => {
    setReading(true);

    try {
      // The listing comes back with the map the game has loaded, which is what
      // an unnamed request resolves to. Asked for every read so that a game
      // changing level is noticed by the next one.
      const info = await getMap(name);
      setAvailable(info.available);

      const wanted = name || info.current;
      const [report, declared] = await Promise.all([
        getMapFile(wanted),
        // The report carries a histogram rather than the entity list, because
        // every other view of that polls for it. This is the one place that
        // wants both, so it asks for both.
        getMapEntities(wanted).catch(() => ({ entities: [] as MapEntity[] })),
      ]);

      if (report.error) {
        setError(report.error);
        setFile(null);
        setEntities([]);
        return;
      }

      setError('');
      setFile(report);
      setEntities(declared.entities ?? []);
      setDetails({});
      setLoadedAt(new Date());
    } catch {
      setError('could not reach the game');
    } finally {
      setReading(false);
    }
  }, []);

  /*
   * Read when the tab is first looked at, and whenever the map is changed.
   *
   * Deliberately not on every showing: this walks the whole file, and a tab that
   * re-read eleven megabytes every time somebody clicked back to it would be the
   * slowest thing in the interface.
   */
  // Null rather than an empty string, because an empty string is a real answer
  // here: it means "whichever map the game has loaded", and starting out equal
  // to it would mean the first showing decided it had already read one.
  const asked = useRef<string | null>(null);

  useEffect(() => {
    if (!visible || asked.current === map) return;

    asked.current = map;
    void load(map);
  }, [visible, map, load]);

  /*
   * Read a big lump when somebody opens it, and not before.
   *
   * The alternative was sending faces and brush sides with the report, which on
   * a medium map is three megabytes paid by everybody who wanted to look at the
   * pakfile. What is opened is read; what is not is never asked for.
   */
  useEffect(() => {
    if (!file || selected === null || details[selected]) return;

    const lump = file.lumps[selected];
    if (!lump || lump.length === 0 || !READ_ON_DEMAND.has(selected)) return;

    let live = true;
    setReadingLump(selected);

    void (async () => {
      try {
        const detail = await getMapLump(file.map, selected);

        // Checked after the await: the lump may have been left, or the map
        // changed, while the request was out.
        if (live) setDetails(previous => ({ ...previous, [selected]: detail }));
      } catch {
        // Left absent rather than cached as empty, so leaving the lump and
        // coming back tries again.
      } finally {
        if (live) setReadingLump(null);
      }
    })();

    return () => { live = false; };
  }, [file, selected, details]);

  const sections = useMemo(
    () => (file ? sectionsOf(file, entities) : []),
    [file, entities]);

  const decoded = useMemo(
    () => new Set([...sections.map(section => section.lump), ...READ_ON_DEMAND]),
    [sections]);

  const detail = selected === null ? undefined : details[selected];

  const showing = selected === null
    ? []
    : [...sectionsForLump(sections, selected), ...(detail ? detailSections(detail) : [])];

  const lump = selected === null || !file ? null : file.lumps[selected] ?? null;

  /** The whole report, as it came off the wire. What "export everything" means. */
  const exportAll = (): void => {
    if (!file) return;

    download(
      `${JSON.stringify({ ...file, entities }, null, 2)}\n`,
      `${file.map}.bsp.json`,
      'application/json');
  };

  return (
    <>
      <div className="toolbar">
        <label>
          map
          <select value={map} onChange={event => setMap(event.target.value)}>
            <option value="">the loaded map</option>
            {available.map(name => <option key={name} value={name}>{name}</option>)}
          </select>
        </label>

        <button onClick={() => void load(map)}>Re-read</button>

        <FilterBox
          value={filter}
          options={[]}
          placeholder="filter rows"
          onChange={setFilter}
        />

        <button
          disabled={!file}
          title="every decoded lump, as one JSON file"
          onClick={exportAll}
        >Export all</button>

        <LoadedAt at={loadedAt} />
        <span className="count">{reading ? 'reading the file…' : ''}</span>
      </div>

      {error !== '' && <p className="banner error" role="alert">{error}</p>}

      {file && (
        <>
          <Facts file={file} />

          {file.warnings.length > 0 && (
            <div className="banner warn" role="alert">
              <b>This file did not read cleanly.</b>
              <ul>
                {file.warnings.map(warning => <li key={warning}>{warning}</li>)}
              </ul>
            </div>
          )}

          <div className="mapfile">
            <LumpTable
              lumps={file.lumps}
              selected={selected}
              decoded={decoded}
              order={order as Order}
              showEmpty={showEmpty}
              decodedOnly={decodedOnly}
              onSelect={index => setLump(index === null ? '' : String(index))}
              onOrder={next => setOrder(next)}
              onShowEmpty={setShowEmpty}
              onDecodedOnly={setDecodedOnly}
            />

            <div className="lump-detail">
              {lump === null && <Contents sections={sections} onSelect={setLump} />}

              {lump !== null && (
                <>
                  <header className="lump-head">
                    <h3>{lump.index}. {lump.name.toLowerCase()}</h3>
                    <span className="count">
                      {lump.length === 0 ? 'empty' : bytes(lump.length)}
                      {lump.records >= 0 && ` · ${count(lump.records)} records of ${lump.stride} bytes`}
                      {lump.length > 0 && ` · at ${count(lump.offset)}`}
                    </span>
                  </header>

                  {lump.note && <p className="note">{lump.note}</p>}

                  {showing.map(section => (
                    <SectionTable key={section.id} section={section} filter={filter} />
                  ))}

                  {showing.length === 0 && (
                    <p className="empty-pane">
                      {readingLump === selected
                        ? `Reading ${count(lump.records)} records…`
                        : lump.length === 0
                          ? 'This map has nothing in this lump.'
                          : 'Nothing here decodes this lump. Its size and record count are above; '
                            + 'what is in it is either handed straight to the engine or is somebody '
                            + 'else’s format.'}
                    </p>
                  )}
                </>
              )}
            </div>
          </div>
        </>
      )}

      {!file && !reading && error === '' && (
        <p className="empty-pane">Choose a map, or load one in the game.</p>
      )}
    </>
  );
}

/**
 * The header facts.
 *
 * Chosen for being the ones that change what you do next: a version says which
 * engine wrote it, no vis clusters says the map draws everything at once, and a
 * large unaccounted count says something appended a lump and left the old copy
 * where it was — which is what this tool's own map writer does every save.
 */
function Facts({ file }: { file: MapFile }): ReactNode {
  const chips: [string, string, string?][] = [
    ['file', `${file.map}.bsp`, file.path],
    ['size', bytes(file.bytes)],
    ['version', `${file.ident} ${file.version}`],
    ['revision', count(file.revision)],
    ['entities', count(file.entityCount)],
    ['connections', count(file.connectionCount)],
    ['props', count(file.props.length)],
    ['materials', count(file.materials.length)],
    ['embedded', count(file.pak.length)],
    ['vis clusters', file.visClusters === 0 ? 'none — compiled without vis' : count(file.visClusters)],
    ['slack', bytes(file.unaccounted)],
  ];

  /*
   * How much of the map an average place in it can see.
   *
   * The number that decides whether a map runs, and the one important fact about
   * a compiled map that is nowhere in the file — the visibility lump holds the
   * visible sets rather than their sizes, so this is measured rather than read.
   */
  if (file.visAverage >= 0 && file.visClusters > 0) {
    chips.push([
      'sees',
      `${count(file.visAverage)} of ${count(file.visClusters)} · worst ${count(file.visMax)}`,
      'clusters an average cluster can see, and the worst one',
    ]);
  }

  if (file.levelFlags) chips.push(['compiled with', file.levelFlags]);

  return (
    <div className="facts">
      {chips.map(([label, value, hint]) => (
        <span className="chip" key={label} title={hint}>{label} <b>{value}</b></span>
      ))}
    </div>
  );
}

/**
 * What there is to look at, when no lump has been chosen.
 *
 * The lump table beside this is complete but it is a table of sixty-four rows
 * where twenty are empty and most of the rest are opaque. This says which of
 * them have something readable behind them, which is the thing somebody opening
 * the tab actually wants to know.
 */
function Contents({ sections, onSelect }: {
  sections: readonly { id: string; title: string; lump: number; rows: unknown[] }[];
  onSelect: (lump: string) => void;
}): ReactNode {
  return (
    <div className="contents">
      <h3>What this map has in it</h3>

      <ul>
        {sections.map(section => (
          <li key={section.id}>
            <button className="link" onClick={() => onSelect(String(section.lump))}>
              {section.title}
            </button>
            <span className="count">{count(section.rows.length)}</span>
            <span className="dim">lump {section.lump}</span>
          </li>
        ))}
      </ul>

      <p className="note">
        Pick a lump on the left to read it, or Export all for the whole file as JSON.
        Every table exports what it is showing, filter and all.
      </p>
    </div>
  );
}

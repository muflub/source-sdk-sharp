/*
 * What the two sides have agreed to send, what one entity's bytes say, and what
 * it all costs.
 *
 * Its own tab rather than a pane of the entity debugger next door, because the
 * two answer different questions. That one is about what the map declares and
 * what the engine holds; this is about the WIRE — which C# type a class
 * networks, where each field sits in its blob, whether the bytes on the server
 * right now decode to what somebody thinks they wrote, and which field is
 * costing bandwidth.
 *
 * All three panels of Part 13 are here since Step 9. Traffic came last because
 * the numbers only mean something once payloads are the only path.
 */

import { useCallback, useEffect, useState, type ReactNode } from 'react';

import { getNetTable, getNetTables, getNetTraffic, getPayload } from '../api';
import { useUrlState } from '../hooks/useUrlState';
import type { NetPayload, NetSchema, NetTraffic, NetTypeDetail } from '../types';
import { InspectorPane } from './InspectorPane';
import { SchemaPane } from './SchemaPane';
import { TrafficPane } from './TrafficPane';

const PANES = ['schema', 'inspector', 'traffic'] as const;
type Pane = (typeof PANES)[number];

const PANE_LABELS: Record<Pane, string> = {
  schema: 'Schema',
  inspector: 'Entity inspector',
  traffic: 'Traffic',
};

export function NetTablesTab({ visible }: { visible: boolean }): ReactNode {
  const [pane, setPane] = useUrlState('netpane');
  const [filter, setFilter] = useUrlState('nettype');
  const [index, setIndex] = useUrlState('ent');

  const [schema, setSchema] = useState<NetSchema | null>(null);
  const [details, setDetails] = useState<Record<string, NetTypeDetail>>({});
  const [payload, setPayload] = useState<NetPayload | null>(null);
  const [live, setLive] = useState(false);
  const [traffic, setTraffic] = useState<NetTraffic | null>(null);
  const [trafficLive, setTrafficLive] = useState(false);

  const showing: Pane = (PANES as readonly string[]).includes(pane) ? pane as Pane : 'schema';

  const loadSchema = useCallback(async (): Promise<void> => {
    try {
      setSchema(await getNetTables());
    } catch {
      // Left as it was. The shell already reports whether the game is answering,
      // and blanking a good schema over one bad poll loses more than it says.
    }
  }, []);

  /*
   * A type's fields, read when a row is opened rather than with the listing.
   *
   * The fields are the bulk of the schema — a page that polled the listing with
   * them included would be sending every field of every type to draw a table of
   * counts. Cached once read, because a layout is fixed at DLL init and cannot
   * change while the game runs.
   */
  const loadType = useCallback(async (className: string): Promise<void> => {
    try {
      const detail = await getNetTable(className);
      setDetails(previous => ({ ...previous, [className]: detail }));
    } catch { /* as above */ }
  }, []);

  const sample = useCallback(async (): Promise<void> => {
    const n = Number.parseInt(index, 10);

    if (!Number.isFinite(n) || n < 0) {
      setPayload(null);
      return;
    }

    try {
      setPayload(await getPayload(n));
    } catch { /* as above */ }
  }, [index]);

  const sampleTraffic = useCallback(async (reset = false): Promise<void> => {
    try {
      setTraffic(await getNetTraffic(reset));
    } catch { /* as above */ }
  }, []);

  /*
   * The traffic totals accumulate on the game thread whether anybody is looking
   * or not, so this poll only READS them — unlike the inspector's, it costs the
   * game nothing and the rate is about how fast a reader wants the number to
   * move rather than about the sampling.
   */
  useEffect(() => {
    if (!visible || showing !== 'traffic') return;

    void sampleTraffic();
    if (!trafficLive) return;

    const timer = setInterval(() => void sampleTraffic(), 1000);
    return () => clearInterval(timer);
  }, [visible, showing, trafficLive, sampleTraffic]);

  // Once, when the tab is first looked at. The schema cannot change while the
  // game runs — a ServerClass may only be made before the engine collects the
  // class list — so polling it would be asking a fixed question repeatedly.
  useEffect(() => {
    if (visible && schema === null) void loadSchema();
  }, [visible, schema, loadSchema]);

  /*
   * The payload is the one thing here that costs a frame, so it is sampled only
   * while somebody is looking at it and only while "live" is on. And each sample
   * is the baseline for the next, which is why a slower rate is the right one:
   * at four a second the diff would be four ticks of noise rather than a second
   * of movement.
   */
  useEffect(() => {
    if (!visible || showing !== 'inspector') return;

    void sample();
    if (!live) return;

    const timer = setInterval(() => void sample(), 1000);
    return () => clearInterval(timer);
  }, [visible, showing, live, sample]);

  return (
    <>
      <div className="toolbar">
        <span className="modes">
          {PANES.map(name => (
            <button
              key={name}
              className="map-pane-tab"
              aria-pressed={showing === name}
              onClick={() => setPane(name)}
            >
              {PANE_LABELS[name]}
            </button>
          ))}
        </span>

        <input
          type="search"
          placeholder="filter types"
          value={filter}
          onChange={event => setFilter(event.target.value)}
          hidden={showing !== 'schema'}
        />

        <button onClick={() => void loadSchema()} hidden={showing === 'traffic'}>
          Refresh schema
        </button>

        {schema !== null && (
          <span className="count">
            {schema.count} type(s) · {schema.tiersLive ? 'tiers live' : 'tiers unknown'}
          </span>
        )}
      </div>

      {showing === 'schema' && (
        <SchemaPane
          schema={schema}
          details={details}
          filter={filter}
          onOpen={className => void loadType(className)}
        />
      )}

      {showing === 'inspector' && (
        <InspectorPane
          payload={payload}
          index={index}
          live={live}
          onIndex={setIndex}
          onLive={setLive}
          onRefresh={() => void sample()}
        />
      )}

      {showing === 'traffic' && (
        <TrafficPane
          traffic={traffic}
          live={trafficLive}
          onLive={setTrafficLive}
          onRefresh={() => void sampleTraffic()}
          onReset={() => void sampleTraffic(true)}
        />
      )}
    </>
  );
}

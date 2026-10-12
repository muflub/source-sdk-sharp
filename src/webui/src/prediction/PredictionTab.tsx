/*
 * Whether the client's guess at the next frame matched what the server said.
 *
 * The tab exists because of a console line that cannot be acted on:
 *
 *     MISPREDICTION at command 23824 — 14 byte(s) differ, first at offset 56
 *
 * A byte count is not a distance. Fourteen bytes differ whether the player is a
 * hundredth of a unit out or a metre out, and those are opposite diagnoses with
 * opposite fixes. So the four panes answer the four questions that line leaves:
 * whether it is working at all (the verdict, above everything), which field and
 * how far past its own tolerance (fields), how far apart on each axis of one
 * compare (record), and — the one that settles it — whether the gap is growing
 * (drift).
 *
 * Everything with state in it is here. The panes take props and nothing else,
 * so each of them can be tested by rendering it with a fixture and no mock at
 * all; the API and the polling live in one place where the follow-along can be
 * reasoned about once.
 */

import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';

import { getPrediction } from '../api';
import { useUrlFlag, useUrlState } from '../hooks/useUrlState';
import type { Prediction, PredictionRecord } from '../types';
import { DriftPane } from './DriftPane';
import { FieldsPane } from './FieldsPane';
import { RecordPane } from './RecordPane';
import { VerdictPane } from './VerdictPane';

const PANES = ['drift', 'fields', 'record'] as const;
type Pane = (typeof PANES)[number];

const PANE_LABELS: Record<Pane, string> = {
  drift: 'Drift',
  fields: 'Fields',
  record: 'One compare',
};

/**
 * How many records to keep on this side.
 *
 * More than the game's ring, on purpose: the ring is what the game can hand over
 * in one reply and this is the window a trend is fitted through. A chart of the
 * last thirty compares cannot show a drift that took a thousand commands to
 * build.
 */
const BUFFER = 600;

export function PredictionTab({ visible }: { visible: boolean }): ReactNode {
  const [pane, setPane] = useUrlState('predpane');
  const [field, setField] = useUrlState('predfield');
  const [record, setRecord] = useUrlState('predrec');
  const [live, setLive] = useUrlFlag('predlive', true);

  const [data, setData] = useState<Prediction | null>(null);
  const [records, setRecords] = useState<PredictionRecord[]>([]);
  const [failed, setFailed] = useState(false);

  /*
   * Where the follow-along is up to, in a ref rather than in state.
   *
   * The poll closes over it, and a poll that closed over a stale `since` would
   * ask for the same records every second and append each of them again — a
   * chart of one real misprediction drawn sixty times, which reads as a game
   * failing sixty times as often as it is.
   */
  const since = useRef(0);

  const showing: Pane = (PANES as readonly string[]).includes(pane) ? pane as Pane : 'drift';
  const selected = record === '' ? null : Number.parseInt(record, 10);

  const sample = useCallback(async (reset = false): Promise<void> => {
    try {
      const next = await getPrediction(reset ? 0 : since.current, 256, reset);
      setData(next);
      setFailed(false);

      if (reset) {
        since.current = next.nextSequence ?? 0;
        setRecords([]);
        return;
      }

      /*
       * The game restarted, or the counters were reset from somewhere else. Its
       * sequences start again from below where this side had got to, and
       * appending them to what is held would interleave two runs into one chart
       * that never happened.
       */
      if ((next.nextSequence ?? 0) < since.current) {
        since.current = next.nextSequence ?? 0;
        setRecords(next.records);
        return;
      }

      since.current = next.nextSequence ?? since.current;

      if (next.records.length > 0) {
        setRecords(held => [...held, ...next.records].slice(-BUFFER));
      }
    } catch {
      // Held rather than blanked. The shell says whether the game is answering,
      // and throwing away a chart of a real drift over one dropped poll loses
      // the thing somebody came here to look at.
      setFailed(true);
    }
  }, []);

  useEffect(() => {
    if (!visible) return;

    void sample();
    if (!live) return;

    // A second: the records are already the compares that found something, and
    // the question they answer — is the gap growing — moves over hundreds of
    // commands rather than between two frames.
    const timer = setInterval(() => void sample(), 1000);
    return () => clearInterval(timer);
  }, [visible, live, sample]);

  const pick = (sequence: number): void => {
    setRecord(String(sequence));
    setPane('record');
  };

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

        <button onClick={() => void sample()}>Refresh</button>
        <button onClick={() => void sample(true)}>Reset counters</button>

        <label className="pred-live">
          <input type="checkbox" checked={live} onChange={event => setLive(event.target.checked)} />
          live
        </label>

        <span className="count">{records.length} record(s) held here</span>
      </div>

      {failed && (
        <p className="banner error">
          The last read of <code>/prediction</code> failed. Everything below is what the game said
          before that, not what is happening now.
        </p>
      )}

      <VerdictPane data={data} records={records} />

      {/*
        The panes are hidden rather than dropped only where it would cost a
        refetch to come back; here every one of them is drawn from the same held
        records, so the cheap thing and the simple thing agree.
      */}
      {data !== null && data.available && (
        <>
          {showing === 'drift' && (
            <DriftPane
              records={records}
              field={field}
              onField={setField}
              selected={selected}
              onSelect={pick}
            />
          )}

          {showing === 'fields' && (
            <FieldsPane records={records} field={field} onField={setField} />
          )}

          {showing === 'record' && (
            <RecordPane records={records} selected={selected} onSelect={pick} />
          )}
        </>
      )}
    </>
  );
}

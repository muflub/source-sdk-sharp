/*
 * What entities have been doing.
 *
 * The live view is a snapshot, and a snapshot cannot show an entity that spawned
 * and was gone again before the next one — which is exactly the entity worth
 * knowing about. This is the record that fills that in.
 *
 * Two kinds of entry, and the difference matters enough to say out loud. Outputs
 * and inputs are reported as they happen, so they cannot be missed. Spawns and
 * removals are found by comparing samples, so anything shorter-lived than the
 * sampling period slips through.
 */

import { useMemo, useRef, useState, type ReactNode } from 'react';

import { getEvents } from '../api';
import type { EntityEvent, EntityEventKind } from '../types';
import { useUrlState } from '../hooks/useUrlState';

const KEEP = 2000;

const KINDS: [EntityEventKind, string][] = [
  ['spawned', 'spawns'],
  ['removed', 'removals'],
  ['input', 'inputs'],
  ['output', 'outputs'],
  ['level', 'level'],
];

const LABELS: Record<EntityEventKind, string> = {
  spawned: 'spawn', removed: 'remove', input: 'input', output: 'output', level: 'level',
};

export interface EventsHandle {
  poll: () => Promise<void>;
  reset: () => void;
}

export function EventsPane({ handle }: { handle: { current: EventsHandle | null } }): ReactNode {
  const [events, setEvents] = useState<EntityEvent[]>([]);
  const [sampling, setSampling] = useState({ on: false, period: 0 });
  const [paused, setPaused] = useState(false);
  const [filter, setFilter] = useUrlState('events');
  const [kinds, setKinds] = useState<ReadonlySet<EntityEventKind>>(new Set(KINDS.map(([kind]) => kind)));

  /*
   * Where the feed has been read to.
   *
   * A ref rather than state, and moved on the moment the answer arrives. State
   * only changes at the next render, and the poll runs from the shell's loop
   * rather than from a render — so a second poll starting before React had
   * committed the first asked for the same events again, and appended a second
   * copy of every one of them. Which React noticed before anybody else did:
   * "two children with the same key".
   */
  const since = useRef(0);
  // Likewise read from the loop rather than from a render.
  const stopped = useRef(paused);
  stopped.current = paused;

  handle.current = {
    poll: async () => {
      if (stopped.current) return;

      const page = await getEvents(since.current);
      if (!page.events) return;

      since.current = page.next;
      setSampling({ on: page.sampling, period: page.samplePeriod });

      if (page.events.length) {
        setEvents(previous => {
          // Only what is genuinely newer — see the same guard in the log feeds.
          // An event listed twice reads as the entity having fired twice.
          const last = previous.length ? previous[previous.length - 1]!.seq : -1;
          const fresh = page.events.filter(one => one.seq > last);

          return fresh.length ? [...previous, ...fresh].slice(-KEEP) : previous;
        });
      }
    },

    reset: () => { setEvents([]); since.current = 0; },
  };

  const shown = useMemo(() => {
    const text = filter.trim().toLowerCase();

    return events.filter(event => kinds.has(event.kind) && (text === ''
      || event.classname.toLowerCase().includes(text)
      || event.name.toLowerCase().includes(text)
      || event.detail.toLowerCase().includes(text)));
  }, [events, kinds, filter]);

  const toggleKind = (kind: EntityEventKind): void => setKinds(previous => {
    const next = new Set(previous);
    next.has(kind) ? next.delete(kind) : next.add(kind);
    return next;
  });

  return (
    <>
      <div className="toolbar">
        <input
          type="search"
          placeholder="filter events"
          value={filter}
          onChange={event => setFilter(event.target.value)}
        />
        {KINDS.map(([kind, label]) => (
          <label key={kind}>
            <input type="checkbox" checked={kinds.has(kind)} onChange={() => toggleKind(kind)} /> {label}
          </label>
        ))}
        <button onClick={() => setPaused(!paused)}>{paused ? 'Resume' : 'Pause'}</button>
        {/* The list, not the position: clearing is about the screen, and
            rewinding the feed would fetch everything still in the ring again. */}
        <button onClick={() => setEvents([])}>Clear</button>
        {/* Said rather than assumed: the first poll after a pause has a gap in
            it, and a view that does not know that shows it as a quiet period. */}
        <span className="count">
          {sampling.on ? `sampling every ${sampling.period}s` : 'not sampling'} · {shown.length} of {events.length}
        </span>
      </div>

      <p className="note">
        Outputs and inputs are recorded as they happen. Spawns and removals are
        found by comparing samples, so an entity shorter-lived than the sample
        period will not appear.
      </p>

      <div className="table-wrap">
        <table>
          <thead>
            <tr><th>Time</th><th>What</th><th>Entity</th><th>Index</th><th>Detail</th></tr>
          </thead>
          <tbody>
            {/* Newest first: a history read for "what just happened" is read
                from the top. */}
            {shown.slice(-400).reverse().map(event => (
              <tr key={event.seq} className={`event-${event.kind}`}>
                <td className="mono">{event.time}</td>
                <td>{LABELS[event.kind] ?? event.kind}</td>
                <td className="name">{event.name || event.classname || '—'}</td>
                <td className="mono">{event.index >= 0 ? event.index : ''}</td>
                <td className="help">{event.detail}</td>
              </tr>
            ))}
            {shown.length === 0 && (
              <tr><td className="empty" colSpan={5}>nothing recorded yet</td></tr>
            )}
          </tbody>
        </table>
      </div>
    </>
  );
}

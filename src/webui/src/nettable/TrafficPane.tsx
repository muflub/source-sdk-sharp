/*
 * The third panel of Part 13: what the payload costs, by class and by field.
 *
 * "Which field is costing bandwidth" has never been a question with an answer in
 * this tree, and the generic payload is what makes it answerable — the delta is
 * determined by which payload words differ between encodes, and that is
 * observable without a packet capture.
 *
 * THE UNIT IS THE DWORD. One changed byte costs the whole word it sits in, so
 * the words column is the wire cost and the bytes column is how much of it was
 * real. The ratio between them is what Part 10's packing is meant to improve, so
 * it is shown rather than left to be divided out by hand.
 */

import type { ReactNode } from 'react';

import type { NetTraffic, NetTrafficClass } from '../types';

/** Bytes per second, at the precision a number that moves every second earns. */
function rate(bytesPerSecond: number): string {
  if (bytesPerSecond >= 1024) return `${(bytesPerSecond / 1024).toFixed(1)} KiB/s`;
  if (bytesPerSecond >= 1) return `${bytesPerSecond.toFixed(0)} B/s`;
  return bytesPerSecond > 0 ? '<1 B/s' : '0';
}

function percent(fraction: number): string {
  return `${Math.round(fraction * 100)}%`;
}

function ClassRow({ row, widest }: { row: NetTrafficClass; widest: number }): ReactNode {
  // Against the busiest class rather than a fixed ceiling: what a reader wants
  // from a bar here is the ordering and the relative size, and an absolute
  // scale would make every row invisible on a quiet map.
  const share = widest > 0 ? row.wordsTouched / widest : 0;

  return (
    <>
      <tr>
        <td className="traffic-class">{row.className}</td>
        <td className="numeric">{row.wordsTouched.toLocaleString()}</td>
        <td className="numeric">{row.bytesChanged.toLocaleString()}</td>
        <td className="numeric">{percent(row.wordFill)}</td>
        <td className="numeric">{rate(row.bytesPerSecond)}</td>
        <td className="numeric">{rate(row.recentBytesPerSecond)}</td>
        <td className="traffic-bar-cell">
          <span className="traffic-bar" style={{ width: `${Math.max(share * 100, 1)}%` }} />
        </td>
      </tr>

      {row.fields.length > 0 && (
        <tr className="traffic-fields-row">
          <td colSpan={7}>
            <span className="traffic-fields">
              {row.fields.map(field => (
                <span key={field.name} className="traffic-field">
                  <code>{field.name}</code>
                  <span className="traffic-field-words">{field.words.toLocaleString()}w</span>
                </span>
              ))}
            </span>
          </td>
        </tr>
      )}
    </>
  );
}

export function TrafficPane({
  traffic,
  onRefresh,
  onReset,
  live,
  onLive,
}: {
  traffic: NetTraffic | null;
  onRefresh: () => void;
  onReset: () => void;
  live: boolean;
  onLive: (live: boolean) => void;
}): ReactNode {
  if (traffic === null) {
    return <p className="empty-pane">No traffic report yet.</p>;
  }

  /*
   * THREE STATES THAT LOOK THE SAME AND ARE NOT. A host that never wired the
   * view, a view that is not sampling, and a world in which nothing moved all
   * produce an empty table — and a reader who cannot tell them apart will
   * conclude the payload is free. Each says which it is.
   */
  if (!traffic.available) {
    return (
      <p className="empty-pane">
        This build&rsquo;s server does not report payload traffic.
      </p>
    );
  }

  const widest = traffic.classes.reduce((most, row) => Math.max(most, row.wordsTouched), 0);
  const busy = traffic.classes.filter(row => row.wordsTouched > 0);

  return (
    <>
      <div className="toolbar">
        <button onClick={onRefresh}>Refresh</button>
        <button onClick={onReset}>Reset totals</button>

        <label className="traffic-live">
          <input type="checkbox" checked={live} onChange={e => onLive(e.target.checked)} />
          live
        </label>

        <span className="count">
          {traffic.trackedEntities ?? 0} entity(ies) tracked
          {traffic.elapsedSeconds !== undefined && ` · ${traffic.elapsedSeconds.toFixed(0)}s`}
        </span>
      </div>

      {traffic.sampling === false && (
        <p className="banner warn">
          Not sampling — set <code>cs_sv_nettable_profile 1</code> in the game.
          Anything below is whatever was measured last.
        </p>
      )}

      {traffic.unit !== undefined && <p className="note">{traffic.unit}</p>}

      {busy.length === 0
        ? (
          <p className="empty-pane">
            {traffic.sampling === true
              ? 'Sampling, and nothing has changed yet.'
              : 'Nothing measured.'}
          </p>
        )
        : (
          <>
            <div className="table-wrap">
            <table className="schema-table traffic-table">
              <thead>
                <tr>
                  <th>class</th>
                  <th className="numeric">words</th>
                  <th className="numeric">bytes</th>
                  <th className="numeric">fill</th>
                  <th className="numeric">B/s</th>
                  <th className="numeric">recent</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {busy.map(row => (
                  <ClassRow key={row.className} row={row} widest={widest} />
                ))}
              </tbody>
            </table>
            </div>

            {traffic.fieldsNote !== undefined && (
              <p className="note">{traffic.fieldsNote}</p>
            )}
          </>
        )}
    </>
  );
}

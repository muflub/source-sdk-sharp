/*
 * One compare, field by field and component by component.
 *
 * "10 byte(s) differ, first at offset 56" is where the console stops. This is
 * what was actually on either side: what the client predicted, what the server
 * said, and the difference — with each component's bar drawn against the
 * field's DECLARED TOLERANCE rather than against its neighbours. The tolerance
 * mark sits at the middle of every bar, so a bar past the middle is a field out
 * of tolerance and no arithmetic is required of the reader.
 *
 * Scaling the bars against each other instead would be prettier and useless: the
 * longest bar would be full width whether it was a millimetre or a mile out, and
 * "which axis moved most" is not a question anybody has.
 */

import type { ReactNode } from 'react';

import type { PredictionField, PredictionRecord } from '../types';
import {
  componentBars, magnitude, ratioText, TOLERANCE_MARK, toleranceState,
} from './drift';

function Bar({ bar, tolerated }: {
  bar: ReturnType<typeof componentBars>[number];
  tolerated: boolean;
}): ReactNode {
  return (
    <tr className="pred-component" data-over={bar.over}>
      <td className="name">{bar.axis}</td>
      <td className="numeric mono">{magnitude(bar.predicted)}</td>
      <td className="numeric mono">{magnitude(bar.server)}</td>
      <td className="numeric mono pred-delta" data-over={bar.over}>{magnitude(bar.delta)}</td>
      <td className="numeric mono dim">{ratioText(bar.ratio)}</td>
      <td className="pred-bar-cell">
        <span className="pred-bar-track">
          {/* The tolerance, at the middle, on every bar including the ones well
              under it — a mark that only appears when it is crossed teaches a
              reader nothing about the bars that did not cross it. */}
          <span className="pred-bar-mark" style={{ left: `${TOLERANCE_MARK * 100}%` }} hidden={bar.ratio === null} />
          <span
            className="pred-bar-fill"
            data-over={bar.over}
            data-tolerated={tolerated}
            data-clamped={bar.clamped}
            style={{ width: `${Math.max(bar.fraction * 100, 0.5)}%` }}
          />
        </span>
      </td>
    </tr>
  );
}

function Field({ field }: { field: PredictionField }): ReactNode {
  const state = toleranceState(field);
  const bars = componentBars(field);

  return (
    <div className="pred-field" data-state={state}>
      <p className="pred-field-head">
        <b className="name">{field.name}</b>
        <span className="dim">{field.kindName} · offset {field.offset} · {field.bytes} byte(s)</span>
        <span className="pill" data-state={state}>
          {state === 'within' && `inside tolerance — ${ratioText(field.toleranceRatio)}`}
          {state === 'over' && `over tolerance — ${ratioText(field.toleranceRatio)}`}
          {state === 'none-declared' && 'no tolerance declared — any difference is a break'}
          {state === 'not-tolerable' && `a ${field.kindName} can never be tolerated`}
        </span>
        <span className="dim">{field.differingBytes} of {field.bytes} byte(s) differ</span>
      </p>

      {/*
        A string has bytes that differ and no numbers to subtract, and so does a
        byte no field claims. Showing an empty table under them would read as a
        field whose components all came out equal, which is the opposite of what
        happened.
      */}
      {bars.length === 0
        ? (
          <p className="note">
            {field.kindName === 'padding'
              ? 'Padding — no field claims these bytes, so there is nothing to subtract. A value '
                + 'landing here is a layout that does not agree with itself.'
              : `No numeric components for a ${field.kindName}: the bytes differ, but a distance `
                + 'is not a thing this field has.'}
          </p>
        )
        : (
          <table className="schema-table pred-components">
            <thead>
              <tr>
                <th></th>
                <th className="numeric">predicted</th>
                <th className="numeric">server</th>
                <th className="numeric">delta</th>
                <th className="numeric">vs tolerance</th>
                <th>{field.tolerance > 0 ? `0 — ${magnitude(field.tolerance)} — ${magnitude(field.tolerance * 2)}` : 'relative'}</th>
              </tr>
            </thead>
            <tbody>
              {bars.map(bar => <Bar key={bar.index} bar={bar} tolerated={field.tolerated} />)}
            </tbody>
          </table>
        )}
    </div>
  );
}

export function RecordPane({ records, selected, onSelect }: {
  records: PredictionRecord[];
  selected: number | null;
  onSelect: (sequence: number) => void;
}): ReactNode {
  if (records.length === 0) {
    return (
      <p className="empty-pane">
        No compare has found a difference, so there is nothing to open. That is the good
        answer, not a missing one.
      </p>
    );
  }

  // The newest is the one somebody just saw in the console, so it is what an
  // unselected pane opens on rather than the oldest thing still in the ring.
  const record = records.find(item => item.sequence === selected) ?? records[records.length - 1]!;

  return (
    <div className="pred-record">
      <div className="pred-record-list">
        {/* Newest first: the log line that sent somebody here is the last one. */}
        {[...records].reverse().map(item => (
          <button
            key={item.sequence}
            className="pred-record-item"
            aria-pressed={item.sequence === record.sequence}
            data-broke={item.mispredicted}
            onClick={() => onSelect(item.sequence)}
          >
            <span className="mono">cmd {item.command}</span>
            <span className="dim">
              {item.fields.length} field(s) · {item.mispredicted ? 'broke through' : 'inside tolerance'}
            </span>
          </button>
        ))}
      </div>

      <div className="pred-record-detail">
        <p className="count">
          command <b className="mono">{record.command}</b> · sequence {record.sequence} ·{' '}
          <span className="mono">{record.class}</span> · first difference at offset{' '}
          <b className="mono">{record.firstDiffOffset}</b> of {record.blobBytes} byte(s)
        </p>

        <p className="note" data-broke={record.mispredicted}>
          {record.mispredicted
            ? 'This compare broke through. At least one field differed by more than it was '
              + 'allowed to, and the two sides are genuinely apart.'
            : 'Every difference in this compare was inside the tolerance its field declared. '
              + 'Kept anyway — a tolerated difference that is growing is the only early warning '
              + 'there is, and the drift chart is where to see that.'}
        </p>

        {record.fields.map(field => <Field key={`${field.name}@${field.offset}`} field={field} />)}
      </div>
    </div>
  );
}

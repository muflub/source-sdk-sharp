/*
 * Which fields are involved, and how badly.
 *
 * The console line names the fields — "Origin, Velocity" — and stops there, so
 * the two questions it leaves are which of them is actually the problem and
 * whether either is anywhere near what it was allowed to be out by. This table
 * puts the declared tolerance and the worst ratio next to each other, ordered by
 * standing before frequency: a field a hundred times its tolerance once is a bug
 * and a field inside its tolerance five hundred times is prediction working, and
 * a table sorted by frequency alone puts the second one on top.
 */

import type { ReactNode } from 'react';

import type { PredictionRecord } from '../types';
import { fieldSummaries, magnitude, ratioText, type FieldSummary, type ToleranceState } from './drift';

/**
 * What each standing means, spelled out, because three of the four render as
 * an absent number and mean entirely different things.
 */
const STATE_WORDS: Record<ToleranceState, string> = {
  within: 'inside tolerance',
  over: 'over tolerance',
  'none-declared': 'no tolerance declared',
  'not-tolerable': 'kind cannot be tolerated',
};

function Row({ summary, selected, onPick }: {
  summary: FieldSummary;
  selected: boolean;
  onPick: () => void;
}): ReactNode {
  return (
    <tr
      className="pred-field-row"
      data-state={summary.state}
      data-selected={selected}
      onClick={onPick}
    >
      <td className="name">
        {summary.name}
        {summary.padding && <span className="dim"> claimed by no field</span>}
      </td>
      <td className="dim">{summary.kindName}</td>
      <td className="numeric mono">{summary.offset}</td>

      {/* Zero is a real declaration — "none" — and printing it as 0 would read
          as a tolerance of nothing, which is the same words for a different
          thing than no tolerance at all. */}
      <td className="numeric mono">
        {summary.tolerable && summary.tolerance > 0 ? magnitude(summary.tolerance) : <span className="dim">none</span>}
      </td>

      <td className="numeric mono pred-ratio" data-state={summary.state}>
        {ratioText(summary.worstRatio)}
      </td>
      <td className="numeric mono">{magnitude(summary.worstDelta)}</td>
      <td className="numeric">{summary.appearances}</td>
      <td className="numeric">{summary.diverging}</td>
      <td><span className="pill" data-state={summary.state}>{STATE_WORDS[summary.state]}</span></td>
    </tr>
  );
}

export function FieldsPane({ records, field, onField }: {
  records: PredictionRecord[];
  field: string;
  onField: (field: string) => void;
}): ReactNode {
  if (records.length === 0) {
    return (
      <p className="empty-pane">
        No field has differed. Nothing is wrong — a prediction that matched byte for byte
        is never recorded, so an empty table here is the good answer.
      </p>
    );
  }

  const summaries = fieldSummaries(records);
  const breaking = summaries.filter(summary => summary.diverging > 0);

  return (
    <>
      <p className="note">
        {breaking.length === 0
          ? `${summaries.length} field(s) have differed and every difference was inside the `
            + 'tolerance the field declared. Sorted by how close each came to breaking it.'
          : `${breaking.length} of ${summaries.length} field(s) have broken through their `
            + 'tolerances. Worst first — a field far past its tolerance once matters more than '
            + 'one comfortably inside it a thousand times. Pick a row to chart its drift.'}
      </p>

      <div className="table-wrap">
        <table className="schema-table pred-fields-table">
          <thead>
            <tr>
              <th>field</th>
              <th>kind</th>
              <th className="numeric">offset</th>
              <th className="numeric">tolerance</th>
              <th className="numeric">worst ratio</th>
              <th className="numeric">worst delta</th>
              <th className="numeric">seen</th>
              <th className="numeric">broke</th>
              <th>standing</th>
            </tr>
          </thead>
          <tbody>
            {summaries.map(summary => (
              <Row
                key={summary.name}
                summary={summary}
                selected={summary.name === field}
                onPick={() => onField(summary.name)}
              />
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}

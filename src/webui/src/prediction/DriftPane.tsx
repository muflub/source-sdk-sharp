/*
 * Is it growing?
 *
 * The centrepiece, and the reason the tab exists. The console prints
 *
 *     MISPREDICTION at command 23820 — 11 byte(s) differ
 *     MISPREDICTION at command 23824 — 14 byte(s) differ
 *
 * and every one of those lines is compatible with two opposite worlds: a player
 * jittering harmlessly a hundredth of a unit either side of the server, and a
 * player walking steadily away from it. The byte count cannot tell them apart —
 * it counts bytes that differ, not distance — so this chart plots the DISTANCE
 * over the command number with the field's declared tolerance drawn straight
 * across it. Below the line is prediction working. Above it, and rising, is the
 * bug.
 *
 * Inline SVG, hand-rolled, from geometry in drift.ts. No chart library: the page
 * travels down a loopback Unix socket with no CDN behind it, and the maths worth
 * getting right is worth unit-testing on its own rather than through a canvas.
 */

import type { ReactNode } from 'react';

import type { PredictionRecord } from '../types';
import {
  driftSeries, linePlot, magnitude, PLOT_BOX, toleranceOf, trend,
} from './drift';

const TREND_WORDS: Record<'growing' | 'steady' | 'shrinking', string> = {
  growing: 'growing',
  steady: 'steady',
  shrinking: 'shrinking',
};

export function DriftPane({ records, field, onField, selected, onSelect }: {
  records: PredictionRecord[];

  /** Which field is being followed. Empty until one is chosen. */
  field: string;
  onField: (field: string) => void;

  /** The record whose detail is open, so the chart can mark it. */
  selected: number | null;
  onSelect: (sequence: number) => void;
}): ReactNode {
  // Every field that has ever differed, so the picker offers the one somebody
  // came here about even if it has not differed in the last few records.
  const names = [...new Set(records.flatMap(record => record.fields.map(f => f.name)))].sort();
  const showing = names.includes(field) ? field : (names[0] ?? '');

  if (records.length === 0) {
    return (
      <p className="empty-pane">
        Nothing has differed yet, so there is no drift to draw. That is the good answer:
        a prediction that matched byte for byte is never recorded.
      </p>
    );
  }

  const tolerance = toleranceOf(records, showing);
  const samples = driftSeries(records, showing);
  const plot = linePlot(samples, { reference: tolerance });
  const movement = trend(samples, tolerance);

  const worst = samples.reduce((most, sample) => Math.max(most, sample.value), 0);

  return (
    <>
      <div className="toolbar">
        <label className="pred-field-picker">
          field
          <select value={showing} onChange={event => onField(event.target.value)}>
            {names.map(name => <option key={name} value={name}>{name}</option>)}
          </select>
        </label>

        <span className="count">
          {samples.length} sample(s) · worst {magnitude(worst)}
          {tolerance === null
            ? ' · no tolerance declared'
            : ` · tolerance ${magnitude(tolerance)}`}
        </span>
      </div>

      {/*
        The answer to the question the log cannot answer, in words, above the
        chart that shows the working. A slope is the only thing that separates a
        wobble from a divergence, and a reader should not have to eyeball one.
      */}
      <p className="pred-trend" data-direction={movement.direction}>
        <b>
          {tolerance === null
            ? `This field has no declared tolerance, and its difference is ${TREND_WORDS[movement.direction]}.`
            : `Against its tolerance of ${magnitude(tolerance)}, the difference is ${TREND_WORDS[movement.direction]}.`}
        </b>{' '}
        {movement.direction === 'steady'
          ? 'It moves about but is not walking away — over the samples held here it ends up where it started.'
          : `Over the ${plot.xMax - plot.xMin} command(s) held here it ${
            movement.change > 0 ? 'rose' : 'fell'} by ${magnitude(Math.abs(movement.change))}${
            movement.changeRatio === null ? '' : ` — ${Math.abs(movement.changeRatio).toFixed(2)}x the tolerance`}.`}
      </p>

      {tolerance === null && (
        <p className="banner warn">
          No tolerance is declared for <code>{showing}</code>, so there is no line to be under
          and any difference at all is a misprediction. The chart below is scaled to the
          field&rsquo;s own worst value, which says how it is MOVING and nothing about whether
          it is acceptable.
        </p>
      )}

      <svg
        className="pred-drift"
        viewBox={`0 0 ${PLOT_BOX.width} ${PLOT_BOX.height}`}
        role="img"
        aria-label={`distance between predicted and server ${showing} over command number`}
      >
        <line className="pred-axis" x1={plot.left} y1={plot.bottom} x2={plot.right} y2={plot.bottom} />
        <line className="pred-axis" x1={plot.left} y1={plot.top} x2={plot.left} y2={plot.bottom} />

        <text className="pred-tick" x={plot.left - 6} y={plot.top + 4} textAnchor="end">
          {magnitude(plot.yTop)}
        </text>
        <text className="pred-tick" x={plot.left - 6} y={plot.bottom} textAnchor="end">0</text>

        {/*
          The tolerance, across the whole chart. It is part of the scale rather
          than an overlay — a tolerance above every sample still has to be
          visible, or the chart shows a rising line with nothing to judge it
          against and reads as a disaster when it is a field behaving itself.
        */}
        {plot.referenceY !== null && (
          <g className="pred-tolerance">
            <line x1={plot.left} y1={plot.referenceY} x2={plot.right} y2={plot.referenceY} />
            <text x={plot.right} y={plot.referenceY - 4} textAnchor="end">
              tolerance {magnitude(plot.reference ?? 0)}
            </text>
          </g>
        )}

        <path className="pred-line pred-line-drift" d={plot.path} />

        {plot.points.map(point => (
          <circle
            key={point.sequence ?? `${point.command}-${point.x}`}
            className="pred-point"
            data-over={point.over}
            data-selected={point.sequence !== undefined && point.sequence === selected}
            cx={point.x}
            cy={point.y}
            r={point.sequence !== undefined && point.sequence === selected ? 5 : 3}
            onClick={() => { if (point.sequence !== undefined) onSelect(point.sequence); }}
          >
            <title>
              command {point.command}: {magnitude(point.value)}
              {tolerance === null ? '' : ` (${(point.value / tolerance).toFixed(2)}x tolerance)`}
            </title>
          </circle>
        ))}

        <text className="pred-tick" x={plot.left} y={PLOT_BOX.height - 8}>cmd {plot.xMin}</text>
        <text className="pred-tick" x={plot.right} y={PLOT_BOX.height - 8} textAnchor="end">
          cmd {plot.xMax}
        </text>
      </svg>

      <p className="note">
        Distance between the predicted value and the server&rsquo;s, by command number. Click a
        point to open that compare field by field.
      </p>
    </>
  );
}

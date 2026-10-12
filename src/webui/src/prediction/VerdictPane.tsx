/*
 * The answer, before any of the charts.
 *
 * Somebody opens this tab with one question — "is prediction working?" — and
 * every number that could answer it is a trap. The counters are of DIFFERING
 * compares only, so a game predicting almost perfectly shows a share near 100%
 * and a game predicting nothing at all shows the same. The record list is empty
 * both when the client is flawless and when there is no client. So the words go
 * first and the numbers go under them, rather than leaving a reader to work out
 * which of five situations they are in from a table.
 */

import type { ReactNode } from 'react';

import type { Prediction, PredictionRecord } from '../types';
import { linePlot, rateSeries, verdict, type Sample } from './drift';

/** The rolling window the rate is measured over, in records. */
const RATE_WINDOW = 20;

const RATE_BOX = {
  width: 720, height: 96, padLeft: 62, padRight: 14, padTop: 10, padBottom: 20,
};

/**
 * The share of recent DIFFERING compares that broke through, along the commands
 * that produced them.
 *
 * Deliberately not called a misprediction rate. It is the share of the compares
 * that found something, and the ones that found nothing were never recorded —
 * so this line sitting at 100% is entirely compatible with a client that
 * predicts correctly 999 commands in every 1000.
 */
function RateChart({ samples }: { samples: Sample[] }): ReactNode {
  // Fixed at 1: this is a share, and a share auto-scaled to its own maximum
  // would draw a 2% rate as a chart that touches the ceiling.
  const plot = linePlot(samples, { yTop: 1, box: RATE_BOX });

  return (
    <svg
      className="pred-rate"
      viewBox={`0 0 ${RATE_BOX.width} ${RATE_BOX.height}`}
      preserveAspectRatio="none"
      role="img"
      aria-label="share of differing compares that broke through, over command number"
    >
      <line className="pred-axis" x1={plot.left} y1={plot.bottom} x2={plot.right} y2={plot.bottom} />
      <line className="pred-axis" x1={plot.left} y1={plot.top} x2={plot.left} y2={plot.bottom} />

      <text className="pred-tick" x={plot.left - 6} y={plot.top + 4} textAnchor="end">100%</text>
      <text className="pred-tick" x={plot.left - 6} y={plot.bottom} textAnchor="end">0%</text>

      <path className="pred-line pred-line-rate" d={plot.path} />

      <text className="pred-tick" x={plot.left} y={RATE_BOX.height - 6}>cmd {plot.xMin}</text>
      <text className="pred-tick" x={plot.right} y={RATE_BOX.height - 6} textAnchor="end">cmd {plot.xMax}</text>
    </svg>
  );
}

export function VerdictPane({ data, records }: {
  data: Prediction | null;
  records: PredictionRecord[];
}): ReactNode {
  const answer = verdict(data);
  const samples = rateSeries(records, RATE_WINDOW);

  return (
    <div className="pred-verdict" data-state={answer.state}>
      <p className="pred-headline">{answer.headline}</p>
      <p className="note">{answer.detail}</p>

      {data !== null && data.available && (
        <p className="count pred-counters">
          <span className="pill">{(data.observed ?? 0).toLocaleString()} differing</span>
          <span className="pill">{(data.mispredicted ?? 0).toLocaleString()} broke through</span>
          <span className="pill">{(data.toleratedOnly ?? 0).toLocaleString()} inside tolerance</span>
          <span className="dim">
            holding {(data.held ?? 0).toLocaleString()} of {(data.capacity ?? 0).toLocaleString()}
          </span>
        </p>
      )}

      {/* Two records is not a trend, and drawing a line through them would
          suggest one. Below that the counters above are the whole story. */}
      {samples.length > 2 && (
        <>
          <RateChart samples={samples} />
          <p className="note">
            Share of the last {RATE_WINDOW} DIFFERING compares that broke through, by command
            number. Not the game&rsquo;s misprediction rate: a prediction that held byte for byte
            was never recorded, so it is not in the denominator.
          </p>
        </>
      )}
    </div>
  );
}

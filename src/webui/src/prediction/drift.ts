/*
 * Reading a misprediction, and placing it on a chart.
 *
 * Everything with a rule in it lives here, apart from the drawing, for the same
 * reason the graph's layering does: a mistake in this file is a WRONG ANSWER
 * rather than an ugly one, and a wrong answer here reads as a working
 * prediction. The console line that started all this —
 *
 *     MISPREDICTION at command 23824 — 14 byte(s) differ, first at offset 56
 *
 * says how MUCH memory disagreed and nothing about how FAR apart the two sides
 * ended up. Fourteen bytes is fourteen bytes whether the player is a hair out
 * or a metre out, and those are opposite diagnoses. What matters is the delta
 * measured against the tolerance that field DECLARED, and whether that quantity
 * is growing — so those two are what this module computes.
 *
 * The scaling functions are separate from the components because the degenerate
 * cases are where a chart lies: one point, every value identical, every value
 * zero, no tolerance declared at all. A naive min/max scale divides by zero on
 * three of those and draws a flat line at the top of the plot on the fourth,
 * which reads as "at the ceiling" when it means "nothing moved".
 */

import type { Prediction, PredictionField, PredictionRecord } from '../types';

//---------------------------------------------------------------------------
// Reading one field.
//---------------------------------------------------------------------------

/**
 * Where one differing field stands against what it declared.
 *
 * Four states rather than a boolean, because three different things produce a
 * field with no usable ratio and they mean different things to whoever is
 * reading:
 *
 *  - `within`        — it differed, and the difference is inside its tolerance.
 *                      This is prediction WORKING, not prediction failing.
 *  - `over`          — it differed by more than it is allowed to. The real one.
 *  - `none-declared` — the kind could carry a tolerance and this field declared
 *                      none, so any difference at all is a break. `toleranceRatio`
 *                      is null here, and null is not zero and is not "fine".
 *  - `not-tolerable` — the KIND can never be tolerated (only float, vector and
 *                      qangle can). Nothing was ever going to forgive this one.
 */
export type ToleranceState = 'within' | 'over' | 'none-declared' | 'not-tolerable';

export function toleranceState(field: PredictionField): ToleranceState {
  if (!field.tolerable) return 'not-tolerable';

  // Null and zero are the same declaration — nothing — and the server sends
  // both spellings. Neither is a ratio, and neither means the field is fine.
  if (field.tolerance <= 0 || field.toleranceRatio === null) return 'none-declared';

  return field.toleranceRatio > 1 ? 'over' : 'within';
}

/**
 * Whether this field is a genuine divergence rather than an accepted wobble.
 *
 * Everything that is not `within` is: a field only appears in a record because
 * it differed, so "no tolerance was engaged" and "it broke the one it had" are
 * both breaks.
 */
export const isDiverging = (field: PredictionField): boolean => toleranceState(field) !== 'within';

/** Padding is a byte no field claims. It has no kind, no tolerance and no components. */
export const isPadding = (field: PredictionField): boolean =>
  field.kind < 0 || field.kindName === 'padding';

/**
 * What to call component `index` of a field.
 *
 * A vector's three numbers are x, y and z and an angle's are pitch, yaw and
 * roll, and a reader chasing a misprediction in the yaw should not have to
 * count columns to find it. Anything else falls back to an index, which is
 * honest about not knowing.
 */
export function componentAxis(kindName: string, index: number, count: number): string {
  if (count === 3 && kindName === 'vector') return ['x', 'y', 'z'][index] ?? String(index);
  if (count === 3 && kindName === 'qangle') return ['pitch', 'yaw', 'roll'][index] ?? String(index);
  if (count === 1) return 'value';

  return `[${index}]`;
}

//---------------------------------------------------------------------------
// One record's components, drawn against the tolerance.
//---------------------------------------------------------------------------

/**
 * Where the tolerance mark sits across a component bar, as a fraction of its
 * width.
 *
 * Halfway, so a bar reaching past the middle is a field out of tolerance and
 * that reading needs no arithmetic from the reader. It also means the visible
 * range runs to twice the tolerance, which is where the clamp comes from.
 */
export const TOLERANCE_MARK = 0.5;

export interface ComponentBar {
  index: number;
  axis: string;
  predicted: number;
  server: number;
  delta: number;
  magnitude: number;

  /** magnitude / tolerance, or null when no tolerance was declared. */
  ratio: number | null;

  /** 0..1 of the bar's width. The tolerance is at TOLERANCE_MARK. */
  fraction: number;

  /** The bar ran off the end — it is at least twice the tolerance. */
  clamped: boolean;

  /** Not covered by a tolerance: this is a break rather than a wobble. */
  over: boolean;
}

/**
 * One bar per component of a field.
 *
 * Scaled against the TOLERANCE rather than against the other components,
 * because the question is never "which axis moved most" — it is "did any axis
 * move further than it was allowed to". Scaling against the worst component
 * would put a bar at full width whether it was a millimetre or a mile out.
 *
 * A field with no tolerance has nothing to scale against, so those bars fall
 * back to the worst magnitude among the components and say so with a null
 * ratio. Their `over` is still true for any non-zero delta: nothing was
 * forgiving them.
 */
export function componentBars(field: PredictionField): ComponentBar[] {
  const tolerance = field.tolerable ? field.tolerance : 0;
  const worst = field.components.reduce((most, c) => Math.max(most, Math.abs(c.delta)), 0);

  return field.components.map((component, index) => {
    const magnitude = Math.abs(component.delta);
    const ratio = tolerance > 0 ? magnitude / tolerance : null;

    // Twice the tolerance is full width, so the mark lands at the middle. With
    // nothing declared there is no such scale, and the worst component is the
    // only honest ceiling left.
    const raw = tolerance > 0
      ? magnitude / (tolerance * 2)
      : (worst > 0 ? magnitude / worst : 0);

    return {
      index,
      axis: componentAxis(field.kindName, index, field.components.length),
      predicted: component.predicted,
      server: component.server,
      delta: component.delta,
      magnitude,
      ratio,
      fraction: Math.min(raw, 1),
      clamped: raw > 1,
      over: tolerance > 0 ? magnitude > tolerance : magnitude > 0,
    };
  });
}

//---------------------------------------------------------------------------
// Which fields are involved, and how badly.
//---------------------------------------------------------------------------

export interface FieldSummary {
  name: string;
  kindName: string;
  offset: number;
  padding: boolean;

  /** What the field declared. Zero means it declared none. */
  tolerance: number;
  tolerable: boolean;

  /** Records this field differed in. */
  appearances: number;

  /** Of those, the ones where it broke through. */
  diverging: number;
  within: number;

  /** The worst ratio ever seen, or null when it never had a tolerance to beat. */
  worstRatio: number | null;
  worstDelta: number;
  lastCommand: number;
  state: ToleranceState;
}

/**
 * Every field that has differed, worst first.
 *
 * Ordered by whether it is breaking before how far it is out, and by how far
 * out before how often: a field a hundred times its tolerance once is a bug and
 * a field inside its tolerance five hundred times is prediction working. Sorting
 * by frequency alone puts the second above the first, which is exactly backwards
 * for somebody trying to find out what is wrong.
 */
export function fieldSummaries(records: PredictionRecord[]): FieldSummary[] {
  const byName = new Map<string, FieldSummary>();

  for (const record of records) {
    for (const field of record.fields) {
      const state = toleranceState(field);
      const existing = byName.get(field.name);

      if (!existing) {
        byName.set(field.name, {
          name: field.name,
          kindName: field.kindName,
          offset: field.offset,
          padding: isPadding(field),
          tolerance: field.tolerance,
          tolerable: field.tolerable,
          appearances: 1,
          diverging: isDiverging(field) ? 1 : 0,
          within: isDiverging(field) ? 0 : 1,
          worstRatio: field.toleranceRatio,
          worstDelta: field.maxAbsDelta,
          lastCommand: record.command,
          state,
        });

        continue;
      }

      existing.appearances += 1;
      if (isDiverging(field)) existing.diverging += 1;
      else existing.within += 1;

      if (field.toleranceRatio !== null) {
        existing.worstRatio = existing.worstRatio === null
          ? field.toleranceRatio
          : Math.max(existing.worstRatio, field.toleranceRatio);
      }

      existing.worstDelta = Math.max(existing.worstDelta, field.maxAbsDelta);
      existing.lastCommand = Math.max(existing.lastCommand, record.command);

      // The worst standing wins: a field that broke once and held a hundred
      // times is a field that breaks.
      if (rank(state) > rank(existing.state)) existing.state = state;
    }
  }

  return [...byName.values()].sort(compareFields);
}

/** How bad a standing is. Only used for ordering. */
function rank(state: ToleranceState): number {
  if (state === 'within') return 0;
  if (state === 'none-declared') return 1;
  if (state === 'not-tolerable') return 2;

  return 3;
}

export function compareFields(a: FieldSummary, b: FieldSummary): number {
  /*
   * Breaking before comfortable, ahead of everything else.
   *
   * This is what keeps a field with no declared tolerance — which has no ratio
   * to sort by and is nevertheless a break, because nothing was forgiving it —
   * above a field sitting comfortably inside one. Ranked by ratio alone the
   * field that is FINE would go first, and the reader would start by
   * investigating the thing that works.
   */
  const breaking = (b.diverging > 0 ? 1 : 0) - (a.diverging > 0 ? 1 : 0);
  if (breaking !== 0) return breaking;

  // Then by how far past. A null ratio has nothing to compare and sorts last
  // within its group rather than as a zero.
  const ratio = (b.worstRatio ?? -1) - (a.worstRatio ?? -1);
  if (ratio !== 0) return ratio;

  const often = b.appearances - a.appearances;
  if (often !== 0) return often;

  return a.name.localeCompare(b.name);
}

//---------------------------------------------------------------------------
// The verdict.
//---------------------------------------------------------------------------

/**
 * Five states that all show an empty or near-empty page and mean entirely
 * different things.
 *
 * `clean` is the one worth being careful about. A prediction that held byte for
 * byte is NEVER recorded, so an empty record list is the GOOD answer — and the
 * obvious rendering of an empty list ("no data") reads as broken. Somebody
 * looking at this tab to find out whether prediction works must not come away
 * thinking it is unwired when it is in fact perfect.
 */
export type VerdictState = 'unread' | 'unavailable' | 'clean' | 'tolerated' | 'breaking';

export interface Verdict {
  state: VerdictState;

  /** The answer in words. */
  headline: string;

  /** Why that is the answer, and what the numbers behind it do and do not mean. */
  detail: string;

  /** mispredicted / observed, or null when nothing has been observed. */
  share: number | null;
}

export function verdict(data: Prediction | null): Verdict {
  if (data === null) {
    return {
      state: 'unread',
      headline: 'Not read yet.',
      detail: 'Nothing has been asked of the game yet, so nothing here is a statement about it.',
      share: null,
    };
  }

  if (!data.available) {
    return {
      state: 'unavailable',
      headline: 'Nothing to report — there is no client in this process.',
      detail: data.note
        ?? 'Prediction is client-side, so a dedicated server has nothing to compare and this is not a fault.',
      share: null,
    };
  }

  const observed = data.observed ?? 0;
  const mispredicted = data.mispredicted ?? 0;
  const share = observed > 0 ? mispredicted / observed : null;

  if (observed === 0) {
    return {
      state: 'clean',
      headline: 'Prediction is holding.',
      detail: 'Not one compare has found a difference. A prediction that matched byte for byte '
        + 'is never recorded, so an empty list here is the good answer rather than a missing one.',
      share: null,
    };
  }

  if (mispredicted === 0) {
    return {
      state: 'tolerated',
      headline: 'Prediction is holding, inside tolerance.',
      detail: `${observed.toLocaleString()} compare(s) found a difference and every one of them was `
        + 'inside the tolerance its field declared. Worth watching rather than fixing — a '
        + 'tolerated difference that is growing is the only early warning there is.',
      share: 0,
    };
  }

  return {
    state: 'breaking',
    headline: 'Prediction is breaking.',
    detail: `${mispredicted.toLocaleString()} of ${observed.toLocaleString()} compare(s) that found a `
      + 'difference broke through their tolerances. That share is of DIFFERING compares, not of all '
      + 'the commands the game ran — a prediction that held exactly is never counted here at all.',
    share,
  };
}

//---------------------------------------------------------------------------
// Charts.
//---------------------------------------------------------------------------

export interface PlotBox {
  width: number;
  height: number;
  padLeft: number;
  padRight: number;
  padTop: number;
  padBottom: number;
}

export const PLOT_BOX: PlotBox = {
  width: 720, height: 220, padLeft: 62, padRight: 14, padTop: 14, padBottom: 28,
};

/** Room above the tallest thing on the chart, so a peak is not clipped by the frame. */
export const HEADROOM = 1.15;

export interface Sample {
  /** The x axis is the command number: these arrive in command order and gaps are real. */
  command: number;
  value: number;

  /** Whatever the caller wants back on the point it clicked. */
  sequence?: number;
}

export interface PlotPoint extends Sample {
  x: number;
  y: number;

  /** Above the reference line, when there is one. */
  over: boolean;
}

export interface LinePlot {
  box: PlotBox;
  left: number;
  right: number;
  top: number;
  bottom: number;
  xMin: number;
  xMax: number;

  /** The value at the top of the plot. Never zero, or every y divides by it. */
  yTop: number;
  points: PlotPoint[];
  path: string;

  /** The declared tolerance, drawn across the chart. Null when none was declared. */
  reference: number | null;
  referenceY: number | null;

  /** The reference is on the chart rather than off the top of it. */
  referenceInView: boolean;
}

/**
 * Places samples in a box.
 *
 * The y axis starts at zero rather than at the smallest value. These are
 * magnitudes, and a min/max scale over values that barely move fills the chart
 * with a wandering line that reads as chaos — when what it means is "nothing
 * happened". Zero-based, that same series is a flat line near the bottom, which
 * is the truth.
 *
 * The reference line is part of the scale, not drawn on top of it: a tolerance
 * above every sample must still be visible, or the chart shows a rising line
 * with nothing to judge it against and every reader concludes the worst.
 */
export function linePlot(samples: Sample[], options: {
  reference?: number | null;
  yTop?: number;
  box?: PlotBox;
} = {}): LinePlot {
  const box = options.box ?? PLOT_BOX;
  const left = box.padLeft;
  const right = box.width - box.padRight;
  const top = box.padTop;
  const bottom = box.height - box.padBottom;

  const reference = options.reference !== undefined && options.reference !== null && options.reference > 0
    ? options.reference
    : null;

  const commands = samples.map(sample => sample.command);
  const xMin = commands.length ? Math.min(...commands) : 0;
  const xMax = commands.length ? Math.max(...commands) : 0;
  const span = xMax - xMin;

  const tallest = samples.reduce((most, sample) => Math.max(most, sample.value), 0);

  /*
   * Never zero. Every degenerate series — no samples, one sample, every value
   * identical, every value zero — lands here, and each of them divides by this.
   * Falling back to 1 puts a flat zero line on the floor of the chart, which is
   * what "nothing moved" should look like.
   */
  const scaled = Math.max(tallest, reference ?? 0) * HEADROOM;
  const yTop = options.yTop !== undefined && options.yTop > 0
    ? options.yTop
    : (scaled > 0 ? scaled : 1);

  const place = (value: number): number => bottom - (Math.min(value, yTop) / yTop) * (bottom - top);

  const points: PlotPoint[] = samples.map((sample, index) => ({
    ...sample,
    // A single sample, or a run of them all from the same command, has no span
    // to spread across. Centred, because pinning it to the left edge reads as
    // the beginning of a series that then stopped.
    x: span > 0
      ? left + ((sample.command - xMin) / span) * (right - left)
      : (samples.length > 1
        ? left + (index / (samples.length - 1)) * (right - left)
        : (left + right) / 2),
    y: place(sample.value),
    over: reference !== null && sample.value > reference,
  }));

  const referenceY = reference !== null ? place(reference) : null;

  return {
    box, left, right, top, bottom, xMin, xMax, yTop, points,
    path: points.map((point, index) => `${index === 0 ? 'M' : 'L'} ${point.x.toFixed(2)} ${point.y.toFixed(2)}`).join(' '),
    reference,
    referenceY,
    referenceInView: reference !== null && reference <= yTop,
  };
}

/**
 * How far one field was out, over time.
 *
 * `maxAbsDelta` rather than a per-component figure: the question the drift chart
 * answers is whether the gap is growing, and a vector that is drifting in z
 * while steady in x and y is drifting.
 */
export function driftSeries(records: PredictionRecord[], fieldName: string): Sample[] {
  const samples: Sample[] = [];

  for (const record of records) {
    for (const field of record.fields) {
      if (field.name !== fieldName) continue;

      samples.push({ command: record.command, value: field.maxAbsDelta, sequence: record.sequence });
    }
  }

  return samples;
}

/** The tolerance the named field declared, or null if it never declared one. */
export function toleranceOf(records: PredictionRecord[], fieldName: string): number | null {
  for (const record of records) {
    for (const field of record.fields) {
      if (field.name === fieldName) return field.tolerable && field.tolerance > 0 ? field.tolerance : null;
    }
  }

  return null;
}

/**
 * The share of the last `window` records that broke through, at each record.
 *
 * A rolling window rather than a running total, because a running total of a
 * process that has been up for an hour cannot move: a burst of mispredictions
 * starting now would raise it by a fraction of a percent, and the one thing
 * somebody watching this wants to see is that it started.
 */
export function rateSeries(records: PredictionRecord[], window = 20): Sample[] {
  const samples: Sample[] = [];

  for (let index = 0; index < records.length; index++) {
    const from = Math.max(0, index - window + 1);
    const slice = records.slice(from, index + 1);
    const broke = slice.filter(record => record.mispredicted).length;
    const record = records[index]!;

    samples.push({ command: record.command, value: broke / slice.length, sequence: record.sequence });
  }

  return samples;
}

//---------------------------------------------------------------------------
// Is it growing?
//---------------------------------------------------------------------------

export interface Trend {
  /** Units of delta per command. */
  slope: number;

  /** What that slope adds up to across the samples on screen. */
  change: number;
  direction: 'growing' | 'steady' | 'shrinking';

  /** The change as a share of the tolerance, when there is one. */
  changeRatio: number | null;
}

/**
 * A least-squares slope through the samples.
 *
 * This is the whole point of the tab. "Ten bytes differ" repeated five times in
 * the console is either a player wobbling harmlessly around the server's
 * position or a player walking away from it, and nothing in that line separates
 * the two. A slope does.
 *
 * "Steady" is a band rather than an exact zero: a fitted slope through real
 * samples is never exactly zero, and a chart that says "growing" because the
 * last hundred commands drifted a nanometre is a chart nobody will believe the
 * second time.
 */
export function trend(samples: Sample[], tolerance: number | null = null): Trend {
  const flat: Trend = { slope: 0, change: 0, direction: 'steady', changeRatio: null };
  if (samples.length < 2) return flat;

  const n = samples.length;
  const meanX = samples.reduce((sum, s) => sum + s.command, 0) / n;
  const meanY = samples.reduce((sum, s) => sum + s.value, 0) / n;

  let covariance = 0;
  let variance = 0;

  for (const sample of samples) {
    covariance += (sample.command - meanX) * (sample.value - meanY);
    variance += (sample.command - meanX) ** 2;
  }

  // Every sample from the same command: a vertical scatter with no run to rise
  // over. There is no slope, and inventing one by dividing by zero is worse
  // than saying so.
  if (variance === 0) return flat;

  const slope = covariance / variance;
  const span = samples[n - 1]!.command - samples[0]!.command;
  const change = slope * span;

  // Against the tolerance where there is one, and against the typical magnitude
  // where there is not — a tenth of what the field is allowed to be out by is a
  // real move; a tenth of a nanometre is not.
  const yardstick = tolerance !== null && tolerance > 0 ? tolerance : meanY;
  const threshold = yardstick > 0 ? yardstick * 0.1 : Number.EPSILON;

  return {
    slope,
    change,
    direction: Math.abs(change) < threshold ? 'steady' : (change > 0 ? 'growing' : 'shrinking'),
    changeRatio: tolerance !== null && tolerance > 0 ? change / tolerance : null,
  };
}

//---------------------------------------------------------------------------
// Formatting, so every pane spells the same number the same way.
//---------------------------------------------------------------------------

/** A delta, at a precision that does not round a real divergence away to zero. */
export function magnitude(value: number): string {
  const size = Math.abs(value);

  if (size === 0) return '0';
  if (size < 0.001) return value.toExponential(1);
  if (size < 1) return value.toFixed(4);
  if (size < 1000) return value.toFixed(3);

  return value.toFixed(1);
}

/**
 * A ratio against a tolerance, or what to say when there is no tolerance.
 *
 * Never a number when the ratio is null. "0.00x" would be read as comfortably
 * inside a tolerance by every reader, and what it means is that no tolerance
 * was ever engaged.
 */
export function ratioText(ratio: number | null): string {
  if (ratio === null) return 'no tolerance';
  if (ratio >= 100) return `${ratio.toFixed(0)}x over`;
  if (ratio > 1) return `${ratio.toFixed(2)}x over`;

  return `${ratio.toFixed(2)}x`;
}

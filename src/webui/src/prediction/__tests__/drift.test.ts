/*
 * The prediction tab's arithmetic.
 *
 * Everything here is a case where the WRONG answer looks like a working chart.
 * A tolerance line drawn off the top of the plot leaves a rising curve with
 * nothing to judge it against, and every reader concludes disaster. A null ratio
 * printed as 0.00x reads as a field comfortably inside a tolerance it never had.
 * A min/max scale over a field that barely moves fills the chart with a wandering
 * line that reads as chaos and means "nothing happened". And four of the
 * degenerate inputs — no samples, one sample, every value identical, every value
 * zero — divide by zero in the obvious implementation and produce a chart of
 * NaN, which renders as an empty box that looks exactly like a quiet game.
 *
 * So these test the maths directly rather than through a rendered pane: a
 * mistake in this file is a WRONG ANSWER about whether prediction works, not an
 * ugly picture.
 */

import { describe, expect, it } from 'vitest';

import {
  componentAxis, componentBars, compareFields, driftSeries, fieldSummaries, HEADROOM,
  isDiverging, isPadding, linePlot, magnitude, PLOT_BOX, rateSeries, ratioText,
  TOLERANCE_MARK, toleranceOf, toleranceState, trend, verdict,
  type FieldSummary, type Sample,
} from '../drift';
import { drifting, field, paddingField, prediction, record, stringField, withinField } from './fixtures';

describe('where a field stands against what it declared', () => {
  it('calls a field past its ratio over, which is the only reading that matters', () => {
    expect(toleranceState(field({ toleranceRatio: 4.96 }))).toBe('over');
  });

  it('calls a field under its ratio within — this is prediction working', () => {
    expect(toleranceState(field({ toleranceRatio: 0.4 }))).toBe('within');
  });

  // Exactly at the tolerance is inside it. A field allowed to be 0.0625 out and
  // 0.0625 out has not broken anything, and reporting it as a misprediction
  // would send somebody hunting a bug that is a boundary.
  it('counts exactly at the tolerance as inside it', () => {
    expect(toleranceState(field({ toleranceRatio: 1 }))).toBe('within');
  });

  /*
   * The three states below all render as an absent ratio and mean entirely
   * different things. Collapsing them to one "unknown" would have a reader
   * looking for a tolerance to widen on a field whose kind can never have one.
   */
  it('separates a float that declared no tolerance from one that has none to declare', () => {
    const undeclared = field({ tolerance: 0, toleranceRatio: null, tolerable: true });
    const impossible = field({ kindName: 'int', tolerance: 0, toleranceRatio: null, tolerable: false });

    expect(toleranceState(undeclared)).toBe('none-declared');
    expect(toleranceState(impossible)).toBe('not-tolerable');
  });

  it('never reads a null ratio as a comfortable one', () => {
    expect(toleranceState(field({ tolerance: 0, toleranceRatio: null }))).not.toBe('within');
  });

  it('treats everything that is not within as a genuine divergence', () => {
    expect(isDiverging(field({ toleranceRatio: 4.96 }))).toBe(true);
    expect(isDiverging(field({ tolerance: 0, toleranceRatio: null }))).toBe(true);
    expect(isDiverging(withinField())).toBe(false);
  });

  it('knows padding from a field, so a byte nobody claims is not blamed on one', () => {
    expect(isPadding(paddingField())).toBe(true);
    expect(isPadding(field())).toBe(false);
  });
});

describe('naming a component', () => {
  // Somebody chasing a misprediction in the yaw should not have to count
  // columns to find which of three numbers the yaw is.
  it('names a vector x, y, z and an angle pitch, yaw, roll', () => {
    expect(componentAxis('vector', 1, 3)).toBe('y');
    expect(componentAxis('qangle', 1, 3)).toBe('yaw');
  });

  it('falls back to an index rather than guessing at four components', () => {
    expect(componentAxis('vector', 2, 4)).toBe('[2]');
    expect(componentAxis('float', 0, 1)).toBe('value');
  });
});

describe('a component bar, drawn against its tolerance', () => {
  it('puts a component exactly at its tolerance on the tolerance mark', () => {
    const [bar] = componentBars(field({
      tolerance: 0.5,
      components: [{ predicted: 1, server: 1.5, delta: 0.5 }],
    }));

    expect(bar!.fraction).toBeCloseTo(TOLERANCE_MARK, 6);
  });

  // The mark is at the middle, so the visible range runs to twice the
  // tolerance. Scaling to anything else would move the mark and the whole
  // "past the middle is a break" reading with it.
  it('fills the bar at twice the tolerance, without calling it clamped', () => {
    const [bar] = componentBars(field({
      tolerance: 0.5,
      components: [{ predicted: 1, server: 2, delta: 1 }],
    }));

    expect(bar!.fraction).toBe(1);
    expect(bar!.clamped).toBe(false);
  });

  it('says when a bar ran off the end, so full width is not read as exactly twice', () => {
    const [bar] = componentBars(field({
      tolerance: 0.5,
      components: [{ predicted: 1, server: 11, delta: 10 }],
    }));

    expect(bar!.fraction).toBe(1);
    expect(bar!.clamped).toBe(true);
  });

  it('does not call a component exactly on its tolerance over', () => {
    const [bar] = componentBars(field({
      tolerance: 0.5,
      components: [{ predicted: 1, server: 1.5, delta: -0.5 }],
    }));

    expect(bar!.over).toBe(false);
  });

  /*
   * The case from the console log, component by component. Two of these three
   * numbers are inside a tolerance of 0.0625 and one is five times past it, and
   * "10 byte(s) differ" says none of that: it is one field, one break, in z.
   */
  it('marks only the component past the tolerance, not the whole field', () => {
    const [x, y, z] = componentBars(field());

    expect(x!.over).toBe(false);   // 0.06 out, allowed 0.0625
    expect(y!.over).toBe(false);   // identical
    expect(z!.over).toBe(true);    // 0.31 out, allowed 0.0625
  });

  /*
   * With nothing declared there is no scale to draw against, so the bars fall
   * back to the worst component and say so with a null ratio. What must NOT
   * happen is a bar being called safe: nothing was forgiving these.
   */
  it('scales an undeclared field against itself and still calls the difference a break', () => {
    const loose = componentBars(field({
      tolerance: 0,
      tolerable: true,
      toleranceRatio: null,
      components: [
        { predicted: 1, server: 3, delta: -2 },
        { predicted: 1, server: 2, delta: 1 },
      ],
    }));

    expect(loose[0]!.ratio).toBeNull();
    expect(loose[0]!.fraction).toBe(1);
    expect(loose[1]!.fraction).toBe(0.5);
    expect(loose[0]!.over).toBe(true);
  });

  it('survives an undeclared field whose components all came out equal', () => {
    const equal = componentBars(field({
      tolerance: 0,
      tolerable: true,
      toleranceRatio: null,
      components: [{ predicted: 1, server: 1, delta: 0 }],
    }));

    expect(equal[0]!.fraction).toBe(0);
    expect(Number.isNaN(equal[0]!.fraction)).toBe(false);
    expect(equal[0]!.over).toBe(false);
  });

  it('has nothing to draw for a string or for padding, rather than a row of zeroes', () => {
    expect(componentBars(stringField())).toHaveLength(0);
    expect(componentBars(paddingField())).toHaveLength(0);
  });
});

const milderName = (summary: FieldSummary): FieldSummary => ({ ...summary, name: 'Milder' });

describe('the field table', () => {
  it('merges a field across the records it differed in', () => {
    const summaries = fieldSummaries([
      record({ sequence: 1, command: 10 }),
      record({ sequence: 2, command: 20 }),
    ]);

    expect(summaries).toHaveLength(1);
    expect(summaries[0]!.appearances).toBe(2);
    expect(summaries[0]!.lastCommand).toBe(20);
  });

  it('keeps the WORST ratio, not the latest — one break is a bug', () => {
    const summaries = fieldSummaries([
      record({ sequence: 1, fields: [field({ toleranceRatio: 12 })] }),
      record({ sequence: 2, fields: [field({ toleranceRatio: 0.2 })] }),
    ]);

    expect(summaries[0]!.worstRatio).toBe(12);
  });

  it('keeps the worst standing, so a field that broke once is not reported as holding', () => {
    const summaries = fieldSummaries([
      record({ sequence: 1, fields: [field({ toleranceRatio: 0.2 })] }),
      record({ sequence: 2, fields: [field({ toleranceRatio: 9 })] }),
    ]);

    expect(summaries[0]!.state).toBe('over');
  });

  it('counts the records it broke in separately from the ones it held in', () => {
    const summaries = fieldSummaries([
      record({ sequence: 1, fields: [field({ toleranceRatio: 0.2 })] }),
      record({ sequence: 2, fields: [field({ toleranceRatio: 9 })] }),
      record({ sequence: 3, fields: [field({ toleranceRatio: 4 })] }),
    ]);

    expect(summaries[0]!.diverging).toBe(2);
    expect(summaries[0]!.within).toBe(1);
  });

  /*
   * The ordering rule the whole table exists for. A field a hundred times its
   * tolerance once is a bug; a field inside its tolerance five hundred times is
   * prediction working. Sorted by frequency the second one goes on top, which
   * is exactly backwards for somebody trying to find out what is broken.
   */
  it('puts a field that broke once above one that held five hundred times', () => {
    const held = Array.from({ length: 500 }, (_, index) => record({
      sequence: index, fields: [withinField()],
    }));

    const summaries = fieldSummaries([
      ...held,
      record({ sequence: 999, fields: [field({ name: 'Origin', toleranceRatio: 100 })] }),
    ]);

    expect(summaries[0]!.name).toBe('Origin');
  });

  /*
   * A field with no declared tolerance is BREAKING — nothing was forgiving it —
   * so it belongs above a field that is comfortably inside one, however often
   * that one appears. Ranked by ratio alone it would sort below, because it has
   * no ratio at all and the field that is fine has a small one.
   */
  it('sorts a field with no tolerance above one comfortably inside its own', () => {
    const held = Array.from({ length: 20 }, (_, index) => record({
      sequence: index, fields: [withinField()],
    }));

    const summaries = fieldSummaries([...held, record({
      sequence: 99,
      fields: [field({ name: 'Undeclared', tolerance: 0, tolerable: true, toleranceRatio: null })],
    })]);

    expect(summaries[0]!.name).toBe('Undeclared');
  });

  // Among fields that ARE breaking, the one furthest past its tolerance goes
  // first: that is the one somebody can act on.
  it('ranks two breaking fields by how far past their tolerances they went', () => {
    const worse: FieldSummary = {
      name: 'Worse', kindName: 'vector', offset: 0, padding: false,
      tolerance: 0.0625, tolerable: true, appearances: 1, diverging: 1, within: 0,
      worstRatio: 40, worstDelta: 2.5, lastCommand: 1, state: 'over',
    };

    const milder: FieldSummary = { ...milderName(worse), worstRatio: 2 };

    expect(compareFields(worse, milder)).toBeLessThan(0);
    expect(compareFields(milder, worse)).toBeGreaterThan(0);
  });
});

describe('the verdict', () => {
  it('says nothing has been read rather than reporting a healthy game', () => {
    expect(verdict(null).state).toBe('unread');
  });

  it('says a dedicated server has no client, and does not call that a failure', () => {
    const answer = verdict({
      available: false,
      note: 'no client in this process — prediction is client-side',
      records: [],
    });

    expect(answer.state).toBe('unavailable');
    expect(answer.detail).toMatch(/client-side/);
    expect(answer.share).toBeNull();
  });

  /*
   * The reading this whole tab is built to prevent. A prediction that held byte
   * for byte is NEVER recorded, so an empty list is the GOOD answer — and the
   * obvious rendering of an empty list reads as a broken or unwired view.
   */
  it('reads an empty ring as prediction HOLDING, not as no data', () => {
    const answer = verdict(prediction({ observed: 0, mispredicted: 0, records: [] }));

    expect(answer.state).toBe('clean');
    expect(answer.headline).toMatch(/holding/i);
    expect(answer.detail).toMatch(/never recorded|good answer/i);
  });

  it('separates differences that were all forgiven from differences that broke through', () => {
    const forgiven = verdict(prediction({ observed: 400, mispredicted: 0, toleratedOnly: 400 }));

    expect(forgiven.state).toBe('tolerated');
    expect(forgiven.share).toBe(0);
    expect(forgiven.headline).toMatch(/holding/i);
  });

  it('calls a game with real mispredictions broken, and shares the fraction', () => {
    const answer = verdict(prediction({ observed: 200, mispredicted: 50 }));

    expect(answer.state).toBe('breaking');
    expect(answer.share).toBeCloseTo(0.25, 6);
  });

  // The share is of DIFFERING compares. Presented as the game's misprediction
  // rate it would report a client predicting 999 commands in 1000 correctly as
  // one failing nearly always, so the detail has to say which it is.
  it('says the share is of differing compares rather than of all commands', () => {
    expect(verdict(prediction()).detail).toMatch(/DIFFERING|not of all/);
  });
});

describe('placing samples on a chart', () => {
  const samples = (values: number[], from = 100, step = 4): Sample[] =>
    values.map((value, index) => ({ command: from + index * step, value, sequence: index }));

  it('draws nothing and divides by nothing when there are no samples', () => {
    const plot = linePlot([]);

    expect(plot.points).toHaveLength(0);
    expect(plot.path).toBe('');
    expect(plot.yTop).toBeGreaterThan(0);
  });

  // One sample has no span to spread across. Pinned to the left edge it reads as
  // the beginning of a series that then stopped.
  it('centres a single sample rather than pinning it to an edge', () => {
    const plot = linePlot(samples([1]));

    expect(plot.points[0]!.x).toBeCloseTo((plot.left + plot.right) / 2, 6);
    expect(Number.isNaN(plot.points[0]!.x)).toBe(false);
  });

  it('spreads samples that all share one command evenly instead of stacking them', () => {
    const plot = linePlot([
      { command: 7, value: 1 }, { command: 7, value: 2 }, { command: 7, value: 3 },
    ]);

    expect(plot.points[0]!.x).toBeCloseTo(plot.left, 6);
    expect(plot.points[2]!.x).toBeCloseTo(plot.right, 6);
    expect(plot.points.every(point => Number.isFinite(point.x))).toBe(true);
  });

  /*
   * Zero-based rather than min/max. A field that barely moves has a min and a
   * max a hair apart, and a scale fitted to them fills the chart with a
   * wandering line that reads as chaos and means "nothing happened".
   */
  it('draws a series that barely moves as a nearly flat line, not as chaos', () => {
    const plot = linePlot(samples([0.5, 0.501, 0.5, 0.502]));
    const ys = plot.points.map(point => point.y);
    const spread = Math.max(...ys) - Math.min(...ys);

    // Fitted to its own min and max, that same series fills the chart with a
    // wandering line -- which reads as chaos and means "nothing happened".
    expect(spread).toBeLessThan((plot.bottom - plot.top) * 0.05);
  });

  it('gives identical values one identical y', () => {
    const ys = linePlot(samples([0.5, 0.5, 0.5])).points.map(point => point.y);

    expect(new Set(ys).size).toBe(1);
    expect(ys.every(y => Number.isFinite(y))).toBe(true);
  });

  it('handles a series that is all zeroes without producing NaN', () => {
    const plot = linePlot(samples([0, 0, 0]));

    expect(plot.yTop).toBe(1);
    expect(plot.points.every(point => point.y === plot.bottom)).toBe(true);
  });

  it('leaves headroom above the tallest sample so a peak is not clipped', () => {
    const plot = linePlot(samples([1, 2, 4]));

    expect(plot.yTop).toBeCloseTo(4 * HEADROOM, 6);
    expect(plot.points[2]!.y).toBeGreaterThan(plot.top);
  });

  /*
   * The one that matters most. A tolerance above every sample must still be on
   * the chart: drawn off the top, the reader sees a rising line with nothing to
   * judge it against and concludes the worst about a field behaving itself.
   */
  it('keeps a tolerance above every sample on the chart', () => {
    const plot = linePlot(samples([0.001, 0.002, 0.003]), { reference: 5 });

    expect(plot.referenceInView).toBe(true);
    expect(plot.referenceY!).toBeGreaterThanOrEqual(plot.top);
    expect(plot.referenceY!).toBeLessThanOrEqual(plot.bottom);
  });

  it('keeps a tolerance far below the samples on the chart too', () => {
    const plot = linePlot(samples([100, 200, 300]), { reference: 0.0625 });

    expect(plot.referenceY!).toBeLessThanOrEqual(plot.bottom);
    expect(plot.referenceY!).toBeGreaterThan(plot.bottom - (plot.bottom - plot.top) * 0.1);
  });

  it('draws no tolerance line when the field declared none', () => {
    expect(linePlot(samples([1, 2]), { reference: null }).referenceY).toBeNull();
    expect(linePlot(samples([1, 2]), { reference: 0 }).referenceY).toBeNull();
    expect(linePlot(samples([1, 2])).referenceY).toBeNull();
  });

  it('marks the samples that are over the tolerance and not the ones under it', () => {
    const plot = linePlot(samples([0.5, 1.5]), { reference: 1 });

    expect(plot.points[0]!.over).toBe(false);
    expect(plot.points[1]!.over).toBe(true);
  });

  it('takes a fixed ceiling, so a share is never auto-scaled to its own maximum', () => {
    const plot = linePlot(samples([0.02, 0.04]), { yTop: 1 });

    expect(plot.yTop).toBe(1);
    expect(plot.points[1]!.y).toBeGreaterThan((plot.top + plot.bottom) / 2);
  });

  it('starts the path with a move and continues with lines', () => {
    expect(linePlot(samples([1, 2, 3])).path).toMatch(/^M [\d.]+ [\d.]+ L .* L /);
  });

  it('places every point inside the box it was given', () => {
    const plot = linePlot(samples([0, 1, 2, 3]), { box: PLOT_BOX });

    for (const point of plot.points) {
      expect(point.x).toBeGreaterThanOrEqual(plot.left);
      expect(point.x).toBeLessThanOrEqual(plot.right);
      expect(point.y).toBeGreaterThanOrEqual(plot.top);
      expect(point.y).toBeLessThanOrEqual(plot.bottom);
    }
  });
});

describe('the series the charts are drawn from', () => {
  it('follows one field across the records, keeping its command as the x axis', () => {
    const series = driftSeries(drifting(3), 'Origin');

    expect(series.map(sample => sample.command)).toEqual([23800, 23804, 23808]);
    expect(series[0]!.value).toBeCloseTo(0.05, 6);
  });

  it('ignores the fields that are not the one being followed', () => {
    const mixed = [record({ fields: [field(), withinField()] })];

    expect(driftSeries(mixed, 'Velocity')).toHaveLength(1);
  });

  it('carries the sequence back, so a point on the chart can be clicked open', () => {
    expect(driftSeries(drifting(2), 'Origin')[0]!.sequence).toBe(4000);
  });

  it('reports no tolerance as null rather than as a tolerance of zero', () => {
    expect(toleranceOf([record({ fields: [field({ tolerance: 0 })] })], 'Origin')).toBeNull();
    expect(toleranceOf([record()], 'Origin')).toBeCloseTo(0.0625, 6);
    expect(toleranceOf([], 'Origin')).toBeNull();
  });

  /*
   * A rolling window, not a running total. A running total over a process that
   * has been up an hour cannot move: a burst starting now would raise it by a
   * fraction of a percent, and that it started is the one thing worth seeing.
   */
  it('measures the rate over a window, so a burst starting now is visible', () => {
    const quiet = Array.from({ length: 40 }, (_, index) =>
      record({ sequence: index, command: index, mispredicted: false }));
    const burst = Array.from({ length: 5 }, (_, index) =>
      record({ sequence: 40 + index, command: 40 + index, mispredicted: true }));

    const series = rateSeries([...quiet, ...burst], 5);

    expect(series[39]!.value).toBe(0);
    expect(series[44]!.value).toBe(1);
  });

  it('divides by what it actually has before the window is full', () => {
    const series = rateSeries([record({ mispredicted: true })], 20);

    expect(series[0]!.value).toBe(1);
  });
});

describe('whether it is growing — the question the console log cannot answer', () => {
  const rising = (from: number, step: number, count = 10): Sample[] =>
    Array.from({ length: count }, (_, index) => ({ command: 100 + index * 4, value: from + index * step }));

  it('calls a series that walks away from the server growing', () => {
    const movement = trend(rising(0.05, 0.05), 0.0625);

    expect(movement.direction).toBe('growing');
    expect(movement.change).toBeGreaterThan(0);
  });

  it('calls a series that is closing shrinking', () => {
    expect(trend(rising(1, -0.05), 0.0625).direction).toBe('shrinking');
  });

  // A wobble is not a drift. This is the whole distinction the tab exists for,
  // and a fitted slope through real samples is never exactly zero — so "steady"
  // has to be a band around it rather than an equality.
  it('calls a wobble that ends where it started steady rather than growing', () => {
    const wobble: Sample[] = [0.01, 0.03, 0.01, 0.03, 0.01]
      .map((value, index) => ({ command: 100 + index * 4, value }));

    expect(trend(wobble, 0.0625).direction).toBe('steady');
  });

  // The band, rather than the exact zero. A rise of a hundredth of what the
  // field is allowed to be out by, reported as "growing", is a chart nobody
  // believes the second time.
  it('keeps a rise far below the tolerance out of the growing bucket', () => {
    expect(trend(rising(0.01, 0.0001), 0.0625).direction).toBe('steady');
  });

  it('will not fit a line through one sample', () => {
    expect(trend([{ command: 1, value: 5 }]).direction).toBe('steady');
    expect(trend([]).slope).toBe(0);
  });

  it('does not divide by zero when every sample came from one command', () => {
    const stacked: Sample[] = [1, 2, 3].map(value => ({ command: 7, value }));
    const movement = trend(stacked, 1);

    expect(Number.isNaN(movement.slope)).toBe(false);
    expect(movement.direction).toBe('steady');
  });

  it('reports the rise as a multiple of the tolerance, which is what makes it actionable', () => {
    const movement = trend(rising(0, 0.0625 / 4, 5), 0.0625);

    expect(movement.changeRatio).toBeCloseTo(1, 6);
  });

  it('has no ratio to report when the field declared no tolerance', () => {
    expect(trend(rising(0.05, 0.05), null).changeRatio).toBeNull();
  });
});

describe('spelling the numbers', () => {
  // The trap: printing a null ratio as a number. "0.00x" is read by every
  // reader as a field comfortably inside a tolerance, and it means there was
  // never a tolerance at all.
  it('never prints a missing ratio as a number', () => {
    expect(ratioText(null)).toBe('no tolerance');
    expect(ratioText(null)).not.toMatch(/0/);
  });

  it('says over for a ratio past one and leaves one under it plain', () => {
    expect(ratioText(4.96)).toBe('4.96x over');
    expect(ratioText(0.4)).toBe('0.40x');
    expect(ratioText(1)).toBe('1.00x');
  });

  // A real divergence rounded to "0" reads as a field that matched, which is
  // the exact opposite of what happened.
  it('does not round a small real difference away to zero', () => {
    expect(magnitude(0.0004)).not.toBe('0');
    expect(magnitude(0)).toBe('0');
  });
});

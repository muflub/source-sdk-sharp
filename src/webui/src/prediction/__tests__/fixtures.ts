/*
 * Records shaped exactly like the ones /prediction sends.
 *
 * Not a test file — vitest collects `*.test.ts` — but it is where the contract
 * is written down, so a change to the endpoint breaks these builders rather than
 * quietly leaving every pane test asserting against a shape the game no longer
 * produces.
 */

import type { Prediction, PredictionField, PredictionRecord } from '../../types';

/** A vector that is well past its tolerance — the case from the console log. */
export function field(over: Partial<PredictionField> = {}): PredictionField {
  return {
    name: 'Origin',
    kind: 2,
    kindName: 'vector',
    offset: 56,
    bytes: 12,
    tolerance: 0.0625,
    tolerable: true,
    tolerated: false,
    differingBytes: 8,
    maxAbsDelta: 0.31,
    toleranceRatio: 4.96,
    components: [
      { predicted: 123.45, server: 123.51, delta: -0.06 },
      { predicted: 10, server: 10, delta: 0 },
      { predicted: 64, server: 63.69, delta: 0.31 },
    ],
    ...over,
  };
}

/** A float inside the tolerance it declared: prediction working, not failing. */
export const withinField = (over: Partial<PredictionField> = {}): PredictionField => field({
  name: 'Velocity',
  tolerance: 0.5,
  tolerated: true,
  maxAbsDelta: 0.2,
  toleranceRatio: 0.4,
  ...over,
});

/** A byte no field claims: no kind, no components, nothing to subtract. */
export const paddingField = (over: Partial<PredictionField> = {}): PredictionField => field({
  name: '(padding@57)',
  kind: -1,
  kindName: 'padding',
  offset: 57,
  bytes: 1,
  tolerance: 0,
  tolerable: false,
  tolerated: false,
  differingBytes: 1,
  maxAbsDelta: 0,
  toleranceRatio: null,
  components: [],
  ...over,
});

/** A string: bytes differ, and a distance is not a thing it has. */
export const stringField = (over: Partial<PredictionField> = {}): PredictionField => field({
  name: 'ModelName',
  kind: 5,
  kindName: 'string',
  offset: 96,
  bytes: 32,
  tolerance: 0,
  tolerable: false,
  toleranceRatio: null,
  maxAbsDelta: 0,
  components: [],
  ...over,
});

export function record(over: Partial<PredictionRecord> = {}): PredictionRecord {
  return {
    sequence: 4020,
    command: 23824,
    typeId: 12,
    class: 'player',
    blobBytes: 120,
    standingBytes: 14,
    firstDiffOffset: 56,
    toleratedFields: 1,
    mispredicted: true,
    atUtc: '2026-09-01T10:00:00.0000000Z',
    fields: [field()],
    ...over,
  };
}

export function prediction(over: Partial<Prediction> = {}): Prediction {
  return {
    available: true,
    capacity: 256,
    held: 137,
    nextSequence: 4021,
    observed: 4021,
    mispredicted: 3900,
    toleratedOnly: 121,
    unit: 'observed counts compares that found any difference; a prediction that held '
      + 'byte for byte is never recorded',
    records: [record()],
    ...over,
  };
}

/**
 * A run of compares in which Origin walks steadily away from the server.
 *
 * The shape the tab has to be able to tell from a wobble: the byte count in the
 * console barely moves across these and the distance triples.
 */
export function drifting(count = 8): PredictionRecord[] {
  return Array.from({ length: count }, (_, index) => record({
    sequence: 4000 + index,
    command: 23800 + index * 4,
    fields: [field({
      maxAbsDelta: 0.05 + index * 0.05,
      toleranceRatio: (0.05 + index * 0.05) / 0.0625,
      tolerated: 0.05 + index * 0.05 <= 0.0625,
      components: [
        { predicted: 123.45, server: 123.45 + 0.05 + index * 0.05, delta: -(0.05 + index * 0.05) },
        { predicted: 10, server: 10, delta: 0 },
        { predicted: 64, server: 64, delta: 0 },
      ],
    })],
    mispredicted: 0.05 + index * 0.05 > 0.0625,
  }));
}

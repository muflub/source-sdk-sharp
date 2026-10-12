/*
 * Editing the static entity list.
 *
 * The static list is the map, so an edit to it is an edit to the file that gets
 * written. What matters is the bookkeeping: what is dirty, and that ids stay
 * dense so the selection does not slide onto a neighbour when something is
 * deleted.
 */

import { beforeEach, describe, expect, it } from 'vitest';

import * as data from '../store';
import { fromMap, keyOf } from '../model';

beforeEach(() => {
  data.state.mode = 'static';
  data.state.map = 'test';
  data.state.lives = [];
  data.state.selectedKey = null;
  data.state.statics = ['first', 'second', 'third'].map((targetname, id) => fromMap({
    id, classname: 'logic_relay', targetname,
    origin: [0, 0, 0], angles: [0, 0, 0], model: '', brushModel: -1,
    keyvalues: [],
    connections: id === 0
      ? [{ output: 'OnTrigger', target: 'second', input: 'Trigger', param: '', delay: 0, times: -1 }]
      : [],
  }));
  data.relink();
  data.state.dirty = false;
});

it('starts clean', () => {
  expect(data.state.dirty).toBe(false);
});

describe('adding', () => {
  it('appends, selects and marks the map dirty', () => {
    const added = data.addEntity({ classname: 'logic_timer', origin: [1, 2, 3] });

    expect(data.state.statics).toHaveLength(4);
    expect(data.state.dirty).toBe(true);
    expect(data.state.selectedKey).toBe(keyOf(added));
  });

  it('gives the new entity the fields the writer needs', () => {
    const added = data.addEntity({ classname: 'logic_timer' });

    expect(Array.isArray(added.keyvalues)).toBe(true);
    expect(Array.isArray(added.connections)).toBe(true);
  });
});

describe('deleting', () => {
  beforeEach(() => data.removeEntity(data.state.statics[1]!));

  it('takes it out and marks the map dirty', () => {
    expect(data.state.statics).toHaveLength(2);
    expect(data.state.dirty).toBe(true);
  });

  // Ids are the selection's identity. A gap would leave a deleted entity's key
  // pointing at whichever one moved into its place.
  it('keeps the ids dense', () => {
    expect(data.state.statics.map((e: { id: number }) => e.id)).toEqual([0, 1]);
  });

  // Otherwise "fired by" would still list the entity that was just deleted.
  it('rebuilds the reverse edges', () => {
    for (const entity of data.state.statics) {
      for (const connection of entity.incoming ?? []) {
        expect(data.state.statics).toContain(connection.from);
      }
    }
  });
});

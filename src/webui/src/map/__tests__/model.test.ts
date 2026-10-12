/*
 * The entity model.
 *
 * The join is what makes this a debugger rather than two lists, and its rules
 * are where the subtle mistakes live: which live entity is which declared one,
 * and what a reverse edge means when several entities share a name.
 */

import { describe, expect, it } from 'vitest';

import { classify, findStatic, fromMap, join, keyOf, relink, type Entity } from '../model';
import type { LiveEntity, MapEntity, Vec3 } from '../../types';

const declared = (over: Partial<MapEntity>): Entity => fromMap({
  id: 0, classname: 'logic_relay', targetname: '', model: '', brushModel: -1,
  origin: [0, 0, 0], angles: [0, 0, 0], keyvalues: [], connections: [], ...over,
});

const live = (over: Partial<LiveEntity>): LiveEntity => ({
  handle: 1, index: 1, classname: 'logic_relay', health: 100,
  origin: [0, 0, 0], angles: [0, 0, 0], ...over,
});

const wire = (target: string) =>
  ({ output: 'OnTrigger', target, input: 'Trigger', param: '', delay: 0, times: -1 });

describe('classifying', () => {
  it('reads the kind from the classname', () => {
    expect(classify('logic_relay')).toBe('logic');
    expect(classify('trigger_multiple')).toBe('trigger');
    expect(classify('func_door')).toBe('brush');
    expect(classify('item_healthkit')).toBe('item');
  });

  it('falls back rather than guessing', () => {
    expect(classify('some_mod_thing')).toBe('other');
  });
});

describe('identity', () => {
  // A live entity and a declared one can describe the same thing, and the two
  // lists are shown separately — so their keys must not collide.
  it('is by handle when live and by id when declared', () => {
    expect(keyOf(declared({ id: 3 }))).toBe('static:3');
    expect(keyOf(join(live({ handle: 9 }), null))).toBe('live:9');
  });
});

describe('reverse edges', () => {
  it('point back at whatever fires them', () => {
    const button = declared({ id: 0, targetname: 'button', connections: [wire('door')] });
    const door = declared({ id: 1, targetname: 'door' });

    relink([button, door]);

    expect(door.incoming).toHaveLength(1);
    expect(door.incoming[0]!.from).toBe(button);
  });

  // One name, many doors, fired together: a whole pattern of level design leans
  // on names not being unique.
  it('reach every entity sharing the name', () => {
    const button = declared({ id: 0, targetname: 'button', connections: [wire('door')] });
    const first = declared({ id: 1, targetname: 'door' });
    const second = declared({ id: 2, targetname: 'door' });

    relink([button, first, second]);

    expect(first.incoming).toHaveLength(1);
    expect(second.incoming).toHaveLength(1);
  });

  it('are rebuilt rather than added to, so relinking twice does not double them', () => {
    const button = declared({ id: 0, targetname: 'button', connections: [wire('door')] });
    const door = declared({ id: 1, targetname: 'door' });

    relink([button, door]);
    relink([button, door]);

    expect(door.incoming).toHaveLength(1);
  });

  it('ignore a target no entity has', () => {
    const button = declared({ id: 0, targetname: 'button', connections: [wire('typo')] });
    expect(() => relink([button])).not.toThrow();
  });
});

describe('matching a live entity to a declared one', () => {
  const statics = [
    declared({ id: 0, classname: 'logic_relay', targetname: 'near', origin: [0, 0, 0] }),
    declared({ id: 1, classname: 'logic_relay', targetname: 'far', origin: [1000, 0, 0] }),
  ];

  it('takes the closest of the same class', () => {
    expect(findStatic(live({ origin: [8, 0, 0] }), statics)).toBe(0);
  });

  it('will not match across classes', () => {
    expect(findStatic(live({ classname: 'func_door', origin: [0, 0, 0] }), statics)).toBeNull();
  });

  // An entity created at runtime has no counterpart at all, and saying so is the
  // difference between "we do not know its keys" and "it has none".
  it('gives up rather than reaching for something far away', () => {
    expect(findStatic(live({ origin: [500, 0, 0] }), statics)).toBeNull();
  });
});

describe('joining', () => {
  it('takes the name and connections from the map, and the position from the engine', () => {
    const source = declared({ id: 4, targetname: 'plug_pull', connections: [wire('door')] });
    const joined = join(live({ origin: [1, 2, 3] as Vec3, health: 50 }), source);

    expect(joined.targetname).toBe('plug_pull');
    expect(joined.connections).toHaveLength(1);
    expect(joined.origin).toEqual([1, 2, 3]);
    expect(joined.health).toBe(50);
    expect(joined.fromMap).toBe(true);
  });

  it('marks an entity the map never declared', () => {
    const joined = join(live({}), null);

    expect(joined.fromMap).toBe(false);
    expect(joined.keyvalues).toEqual([]);
    expect(joined.label).toBe('logic_relay');
  });
});

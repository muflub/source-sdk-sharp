/*
 * Saved groups, the templates that ship with them, and the selection they are
 * captured from.
 *
 * The store is the browser's, so the risk is persistence rather than
 * arithmetic: what survives a reload, and what a hand-edited store does.
 */

import { beforeEach, describe, expect, it } from 'vitest';

import { load, missingTemplates, save, toGroupEntity } from '../groupStore';
import { createSelection } from '../selection';
import { TEMPLATES } from '../templates';
import { fromMap } from '../model';

const KEY = 'debug-api.groups';

beforeEach(() => localStorage.removeItem(KEY));

describe('templates', () => {
  it('are seeded into an empty store', () => {
    expect(load()).toHaveLength(TEMPLATES.length);
  });

  // Reseeding on every load would resurrect the ones deleted on purpose.
  it('are not reseeded once the store exists', () => {
    save([]);
    expect(load()).toHaveLength(0);
  });

  it('come back one at a time when asked for', () => {
    const kept = TEMPLATES.slice(0, 2).map(t => ({ ...t }));
    expect(missingTemplates(kept)).toHaveLength(TEMPLATES.length - 2);
  });

  // A brush entity keeps its volume in the compiled map, so one created at
  // runtime arrives with no brush and never fires. A template that cannot work
  // when placed is worse than no template.
  it('use no brush entities', () => {
    const brushy = TEMPLATES
      .flatMap(template => template.entities.map(entity => entity.classname))
      .filter(classname => /^(func_|trigger_)/.test(classname));

    expect(brushy).toEqual([]);
  });

  it('only connect to entities they contain', () => {
    for (const template of TEMPLATES) {
      const names = new Set(template.entities.map(entity => entity.targetname).filter(Boolean));

      for (const entity of template.entities) {
        for (const connection of entity.connections) {
          expect(names, `${template.name} -> ${connection.target}`).toContain(connection.target);
        }
      }
    }
  });

  // A connection is added by name, so an entity that fires one needs a name.
  it('name every entity that fires something', () => {
    for (const template of TEMPLATES) {
      for (const entity of template.entities) {
        if (entity.connections.length) expect(entity.targetname).toBeTruthy();
      }
    }
  });
});

describe('the store', () => {
  it('is versioned', () => {
    save(TEMPLATES);
    expect(JSON.parse(localStorage.getItem(KEY)!).version).toBe(1);
  });

  // A store someone has edited by hand, or one written by an older version.
  // Losing the groups is bad; taking the page down with them is worse, and
  // coming back with the templates beats coming back with nothing at all.
  it('falls back to the templates rather than throwing on an unreadable store', () => {
    localStorage.setItem(KEY, '{ this is not json');

    expect(() => load()).not.toThrow();
    expect(load()).toHaveLength(TEMPLATES.length);
  });
});

describe('capturing an entity', () => {
  const entity = fromMap({
    id: 0, classname: 'logic_relay', targetname: 'relay', model: '', brushModel: -1,
    origin: [1, 2, 3], angles: [0, 90, 0],
    keyvalues: [
      { key: 'classname', value: 'logic_relay' },
      { key: 'origin', value: '1 2 3' },
      { key: 'StartDisabled', value: '0' },
    ],
    connections: [{ output: 'OnTrigger', target: 'door', input: 'Open', param: '', delay: 0, times: -1 }],
  });

  it('keeps what makes it work', () => {
    const stored = toGroupEntity(entity);

    expect(stored.classname).toBe('logic_relay');
    expect(stored.origin).toEqual([1, 2, 3]);
    expect(stored.connections).toHaveLength(1);
    expect(stored.keyvalues.StartDisabled).toBe('0');
  });

  // The four that are also fields would otherwise appear twice, and an edit to
  // one copy and not the other has no defined meaning.
  it('does not keep the keys that are also fields', () => {
    const stored = toGroupEntity(entity);

    expect(stored.keyvalues.classname).toBeUndefined();
    expect(stored.keyvalues.origin).toBeUndefined();
  });
});

describe('the selection', () => {
  it('toggles one at a time', () => {
    const selection = createSelection();

    selection.toggle('static:0');
    expect(selection.size()).toBe(1);

    selection.toggle('static:0');
    expect(selection.size()).toBe(0);
  });

  it('takes the span between two, in the order the list shows', () => {
    const selection = createSelection();
    const order = ['static:0', 'static:1', 'static:2', 'static:3'];

    selection.toggle('static:0', { order });
    selection.toggle('static:2', { range: true, order });

    expect(selection.size()).toBe(3);
    expect(selection.has('static:3')).toBe(false);
  });

  it('adds a whole filtered list at once', () => {
    const selection = createSelection();
    selection.add(['static:0', 'static:1']);

    expect(selection.size()).toBe(2);
  });

  it('tells whoever is watching', () => {
    const selection = createSelection();
    let told = 0;

    selection.subscribe(() => told++);
    selection.toggle('static:0');
    selection.clear();

    expect(told).toBe(2);
  });
});

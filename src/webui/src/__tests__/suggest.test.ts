/*
 * The suggestion lists.
 *
 * Everything typed into this tool is a name that exists somewhere else, so what
 * is worth checking is that the lists are derived from the right place — and
 * that the console, which completes whole command lines rather than single
 * words, offers lines that can actually be run.
 */

import { beforeEach, describe, expect, it } from 'vitest';

import * as suggest from '../suggest';
import * as data from '../map/store';
import { fromMap } from '../map/model';
import type { MapEntity } from '../types';

const options = (id: string) =>
  Array.from(document.querySelectorAll(`#${id} option`)).map(o => (o as HTMLOptionElement).value);

beforeEach(() => {
  document.body.innerHTML = '';

  data.state.mode = 'static';
  data.state.map = 'test';
  data.state.lives = [];
  data.state.available = ['dm_lockdown', 'dm_powerhouse'];
  data.state.statics = ([
    {
      id: 0, classname: 'func_button', targetname: 'button_one',
      origin: [0, 0, 0], angles: [0, 0, 0], model: '', brushModel: -1,
      keyvalues: [{ key: 'speed', value: '250' }],
      connections: [{ output: 'OnPressed', target: 'door_one', input: 'Open', param: '', delay: 0, times: -1 }],
    },
    {
      id: 1, classname: 'func_door', targetname: 'door_one',
      origin: [0, 0, 0], angles: [0, 0, 0], model: '', brushModel: -1,
      keyvalues: [{ key: 'speed', value: '100' }, { key: 'wait', value: '4' }],
      connections: [],
    },
  ] as MapEntity[]).map(fromMap);

  data.relink();
  suggest.refreshFromMap(data.state.statics, data.state.lives, data.state.available);
});

describe('the lists', () => {
  it('take classnames and targetnames from the map', () => {
    expect(options('sg-classnames')).toContain('func_button');
    expect(options('sg-targetnames')).toContain('door_one');
  });

  // An input the map demonstrably sends is better evidence than any fixed list.
  it('take inputs and outputs from the connections the map makes', () => {
    expect(options('sg-inputs')).toContain('Open');
    expect(options('sg-outputs')).toContain('OnPressed');
  });

  it('still offer the inputs every entity understands', () => {
    expect(options('sg-inputs')).toContain('AddOutput');
  });

  it('take maps from what is on disk', () => {
    expect(options('sg-maps')).toContain('dm_powerhouse');
  });

  it('take commands and convars from their own endpoints', () => {
    suggest.refreshFromListings({
      commands: [{ name: 'cs_spawn_pickup' }],
      convars: [{ name: 'cs_sv_damage_scale' }],
    });

    expect(options('sg-commands')).toEqual(
      expect.arrayContaining(['cs_spawn_pickup', 'cs_sv_damage_scale']));
  });

  // A class this mod implements is placeable even in a map containing none.
  it('add the entity classes this mod declares', () => {
    suggest.refreshFromListings({
      entityClasses: [{ className: 'cs_timer', inputs: ['Start'], outputs: ['OnTimer'] }],
    });

    expect(options('sg-classnames')).toContain('cs_timer');
    expect(options('sg-inputs')).toContain('Start');
  });
});

describe('keys and values', () => {
  // The best evidence for what an entity should carry is what the others of its
  // class already do.
  it('are suggested from other entities of the same class', () => {
    const box = document.createElement('input');
    document.body.appendChild(box);

    suggest.attach(box, 'key', 'func_door');
    box.dispatchEvent(new Event('focus'));

    const list = options(box.getAttribute('list')!);
    expect(list).toContain('wait');
    expect(list).toContain('targetname');
  });

  // Values cross classes: "what do other things set this to" is the question.
  it('draw values from every entity using the key', () => {
    const box = document.createElement('input');
    document.body.appendChild(box);

    suggest.attach(box, 'value', 'speed');
    box.dispatchEvent(new Event('focus'));

    expect(options(box.getAttribute('list')!)).toEqual(expect.arrayContaining(['100', '250']));
  });
});

describe('the console', () => {
  beforeEach(() => {
    suggest.refreshFromListings({
      commands: [{ name: 'cs_spawn_pickup' }],
      convars: [{ name: 'cs_sv_damage_scale' }],
    });
  });

  it('offers commands when nothing is typed', () => {
    suggest.refreshConsole('');
    expect(options('sg-console')).toContain('cs_spawn_pickup');
  });

  // A datalist matches the whole value, so the second word can only be completed
  // by offering options that already contain the first.
  it('completes whole command lines', () => {
    suggest.refreshConsole('map ');
    expect(options('sg-console')).toContain('map dm_lockdown');
  });

  it('narrows as the line is typed', () => {
    suggest.refreshConsole('map dm_p');
    expect(options('sg-console')).toEqual(['map dm_powerhouse']);
  });

  it('completes targetnames after ent_fire', () => {
    suggest.refreshConsole('ent_fire door');
    expect(options('sg-console')).toContain('ent_fire door_one');
  });

  it('completes classnames after ent_create', () => {
    suggest.refreshConsole('ent_create func');
    expect(options('sg-console').some(o => o.startsWith('ent_create func_'))).toBe(true);
  });

  it('offers nothing for a verb it does not know, rather than everything', () => {
    suggest.refreshConsole('something_unknown ');
    expect(options('sg-console')).toHaveLength(0);
  });
});

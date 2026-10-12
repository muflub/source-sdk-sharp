/*
 * The YAML a group or an entity round-trips through.
 *
 * Map values are always strings, and the ones that look like other types are the
 * whole risk here: a round trip that inferred a type would silently rewrite the
 * map it was supposed to preserve.
 */

import { describe, expect, it } from 'vitest';

import { dump, parse, toYaml, fromYaml, entityToObject, type YamlMap } from '../yaml';

/* Every document below is a mapping, and every test reads it as one. */
const read = (text: string): Record<string, any> => parse(text) as YamlMap;

const entity = {
  classname: 'logic_relay',
  targetname: 'plug pull',
  origin: [-4098.2, 4539.7, 119],
  angles: [0, 0, 0],
  keyvalues: [
    { key: 'origin', value: '-4098.2 4539.7 119' },
    { key: 'targetname', value: 'plug pull' },
    { key: 'StartDisabled', value: '0' },
    { key: 'message', value: 'he said "hi", then left' },
    { key: 'empty', value: '' },
    { key: 'numberish', value: '007' },
  ],
  connections: [
    { output: 'OnTrigger', target: 'a b', input: 'Kill', param: '', delay: 0.5, times: -1 },
  ],
};

describe('a round trip', () => {
  const back = read(dump(entityToObject(entity)));

  it('keeps the classname and a name with a space in it', () => {
    expect(back.classname).toBe('logic_relay');
    expect(back.targetname).toBe('plug pull');
  });

  it('keeps the origin as a vector', () => {
    expect(back.origin).toEqual([-4098.2, 4539.7, 119]);
  });

  it('keeps a value containing quotes', () => {
    expect(back.keyvalues.message).toBe('he said "hi", then left');
  });

  it('keeps an empty value empty', () => {
    expect(back.keyvalues.empty).toBe('');
  });

  // The traps: both of these are strings in a map, and both look like something
  // else. Turning "007" into 7 would rewrite the map on the way through.
  it('does not renumber a leading-zero value', () => {
    expect(back.keyvalues.numberish).toBe('007');
  });

  it('keeps "0" a string', () => {
    expect(back.keyvalues.StartDisabled).toBe('0');
  });

  it('keeps a connection whole', () => {
    expect(back.connections[0]).toMatchObject({ target: 'a b', input: 'Kill', delay: 0.5 });
  });

  // These four are written as fields, so carrying them in keyvalues as well
  // would let an edit to one copy be overwritten by the other.
  it('does not duplicate the keys that are also fields', () => {
    expect(back.keyvalues.classname).toBeUndefined();
    expect(back.keyvalues.origin).toBeUndefined();
  });
});

describe('documents', () => {
  it('round-trip whole', () => {
    expect(fromYaml(toYaml([entity], 'dm_lockdown', 'static'))).toHaveLength(1);
  });

  it('accept a bare list, which is what someone would write by hand', () => {
    expect(fromYaml('- classname: foo\n  targetname: bar\n')).toHaveLength(1);
  });

  it('ignore comments', () => {
    expect(read('# hello\nkey: value  # trailing\n').key).toBe('value');
  });

  it('refuse nonsense rather than guessing at it', () => {
    expect(() => fromYaml('just a string')).toThrow();
  });
});

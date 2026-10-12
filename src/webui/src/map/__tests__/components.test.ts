/*
 * The separate pieces of logic in a map.
 *
 * A map is dozens of these — a door and its button, a trap and its trigger —
 * sharing no edges at all, and the whole reason for finding them is being able
 * to look at one.
 */

import { describe, expect, it } from 'vitest';

import { componentsOf, inComponent } from '../components';
import { fromMap, relink, type Entity } from '../model';
import type { Connection } from '../../types';

const wire = (target: string, input = 'Trigger'): Connection =>
  ({ output: 'OnTrigger', target, input, param: '', delay: 0, times: -1 });

function map(...declared: [string, string, Connection[]][]): Entity[] {
  const entities = declared.map(([targetname, classname, connections], id) => fromMap({
    id, classname, targetname, model: '', brushModel: -1,
    origin: [0, 0, 0], angles: [0, 0, 0], keyvalues: [], connections,
  }));

  relink(entities);
  return entities;
}

describe('a map with two unrelated pieces of logic', () => {
  const entities = map(
    ['button', 'func_button', [wire('door', 'Open')]],
    ['door', 'func_door', []],
    ['timer', 'logic_timer', [wire('siren'), wire('lights')]],
    ['siren', 'ambient_generic', []],
    ['lights', 'light', []],
    ['a_prop', 'prop_physics', []],
  );

  const components = componentsOf(entities);

  it('finds both, biggest first', () => {
    expect(components).toHaveLength(2);
    expect(components[0]!.size).toBe(3);
    expect(components[1]!.size).toBe(2);
  });

  // The busiest node is the relay at the middle of a trap or the button on a
  // door — what somebody would call the piece.
  it('names each after the entity doing the most in it', () => {
    expect(components[0]!.label).toBe('timer +2');
    expect(components[1]!.label).toBe('button +1');
  });

  // Five hundred props that connect to nothing are not five hundred pieces of
  // logic; they are the rest of the map.
  it('leaves out what connects to nothing', () => {
    expect(components.flatMap(one => [...one.keys])).not.toContain('static:5');
  });

  it('says which entities belong to which', () => {
    const [timer, , lights, prop] = [entities[2]!, entities[3]!, entities[4]!, entities[5]!];

    expect(inComponent(timer, components[0])).toBe(true);
    expect(inComponent(lights, components[0])).toBe(true);
    expect(inComponent(prop, components[0])).toBe(false);
  });

  // Nothing chosen means everything, which is what the list shows by default.
  it('lets everything through when no piece is chosen', () => {
    expect(inComponent(entities[5]!, undefined)).toBe(true);
  });
});

describe('a name for a piece', () => {
  // An index would move the moment an entity was added, and an address carrying
  // one would then point at a different piece of logic than it did.
  it('is the entity at its middle rather than its position in the list', () => {
    const before = componentsOf(map(
      ['button', 'func_button', [wire('door', 'Open')]],
      ['door', 'func_door', []],
    ));

    const after = componentsOf(map(
      ['unrelated', 'logic_relay', [wire('other')]],
      ['other', 'logic_relay', []],
      ['button', 'func_button', [wire('door', 'Open')]],
      ['door', 'func_door', []],
    ));

    expect(after.some(one => one.id === before[0]!.id)).toBe(true);
  });
});

// An entity with no targetname is known by its classname, so a map with two
// unnamed triggers has two pieces claiming the same name — dm_lockdown has
// exactly that, and an ambiguous name in the address picks the wrong one.
describe('two pieces that would be called the same thing', () => {
  const entities = map(
    ['', 'trigger_multiple', [wire('door_a', 'Open')]],
    ['door_a', 'func_door', []],
    ['', 'trigger_multiple', [wire('door_b', 'Open')]],
    ['door_b', 'func_door', []],
  );

  const components = componentsOf(entities);

  it('gives them names that can be told apart', () => {
    expect(components).toHaveLength(2);
    expect(components[0]!.id).not.toBe(components[1]!.id);
  });

  it('says so in the label as well, since a list of two identical rows is no choice', () => {
    expect(components[0]!.label).toContain('door_');
  });
});

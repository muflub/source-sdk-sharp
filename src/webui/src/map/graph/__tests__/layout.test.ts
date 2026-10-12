/*
 * The graph's layering.
 *
 * Map logic is full of cycles — a relay that re-triggers itself is an ordinary
 * thing to write — and a longest-path layering meets them head on. The first
 * version of this both hung on one and, once it stopped hanging, flung its
 * members four columns apart and off the side of the view.
 */

import { describe, expect, it } from 'vitest';

import { build, place, NODE_WIDTH } from '../layout';
import { fromMap, type Entity } from '../../model';
import type { Connection } from '../../../types';

const wire = (target: string, output = 'OnTrigger', input = 'Trigger'): Connection =>
  ({ output, target, input, param: '', delay: 0, times: -1 });

const entity = (id: number, targetname: string, connections: Connection[] = []): Entity =>
  fromMap({
    id, classname: 'logic_relay', targetname, model: '', brushModel: -1,
    origin: [0, 0, 0], angles: [0, 0, 0], keyvalues: [], connections,
  });

describe('a cycle', () => {
  const cycle = () => [entity(0, 'a', [wire('b')]), entity(1, 'b', [wire('a')])];

  it('lays out rather than hanging', () => {
    const started = performance.now();
    place(build(cycle()));

    expect(performance.now() - started).toBeLessThan(1000);
  });

  // Relaxing until nothing moves never finishes on a cycle, and stopping after
  // a fixed number of passes strews the nodes across a column for every pass.
  it('does not inflate the layering', () => {
    const layout = place(build(cycle()));
    const rightmost = Math.max(...layout.nodes.map(node => node.x));

    expect(rightmost).toBeLessThanOrEqual(NODE_WIDTH + 130);
  });

  it('keeps both nodes and both edges', () => {
    const layout = place(build(cycle()));

    expect(layout.nodes).toHaveLength(2);
    expect(layout.edges).toHaveLength(2);
  });
});

describe('ports', () => {
  it('are the outputs it fires and the inputs it is sent', () => {
    const layout = build([entity(0, 'a', [wire('b', 'OnTrigger', 'Kill')]), entity(1, 'b')]);

    const a = layout.nodes.find(node => node.entity.targetname === 'a')!;
    const b = layout.nodes.find(node => node.entity.targetname === 'b')!;

    expect(a.outputs).toEqual(['OnTrigger']);
    expect(a.inputs).toEqual([]);
    expect(b.inputs).toEqual(['Kill']);
  });

  it('are listed once however many connections use them', () => {
    const layout = build([
      entity(0, 'a', [wire('b', 'OnTrigger', 'Kill'), wire('c', 'OnTrigger', 'Kill')]),
      entity(1, 'b'), entity(2, 'c'),
    ]);

    expect(layout.nodes.find(node => node.entity.targetname === 'a')!.outputs).toEqual(['OnTrigger']);
  });
});

describe('a target no entity has', () => {
  // A typo in a target is invisible in a map file and instantly obvious here.
  it('is drawn as a node of its own, marked missing', () => {
    const layout = build([entity(0, 'a', [wire('typo')])]);
    const missing = layout.nodes.filter(node => node.missing);

    expect(missing).toHaveLength(1);
    expect(missing[0]!.entity.targetname).toBe('typo');
  });
});

// One name, many doors, fired together: a whole pattern of level design leans
// on names not being unique.
describe('a name several entities share', () => {
  it('fans one connection out to every one of them', () => {
    const layout = build([
      entity(0, 'button', [wire('door', 'OnPressed', 'Open')]),
      entity(1, 'door'), entity(2, 'door'),
    ]);

    expect(layout.edges).toHaveLength(2);
  });
});

describe('entities that connect to nothing', () => {
  // A map is six hundred entities and five hundred of them are props and lights.
  // Drawing them would bury the hundred that matter.
  it('are left out entirely', () => {
    const layout = build([entity(0, 'a', [wire('b')]), entity(1, 'b'), entity(2, 'lonely')]);

    expect(layout.nodes.map(node => node.entity.targetname)).not.toContain('lonely');
  });
});

describe('layering', () => {
  it('puts a chain in the order it fires', () => {
    const layout = place(build([
      entity(0, 'first', [wire('second')]),
      entity(1, 'second', [wire('third')]),
      entity(2, 'third'),
    ]));

    const x = (name: string) => layout.nodes.find(node => node.entity.targetname === name)!.x;

    expect(x('first')).toBeLessThan(x('second'));
    expect(x('second')).toBeLessThan(x('third'));
  });

  it('keeps unrelated logic from overlapping', () => {
    const layout = place(build([
      entity(0, 'a1', [wire('a2')]), entity(1, 'a2'),
      entity(2, 'b1', [wire('b2')]), entity(3, 'b2'),
    ]));

    const y = (name: string) => layout.nodes.find(node => node.entity.targetname === name)!.y;

    expect(y('a1')).not.toBe(y('b1'));
  });
});

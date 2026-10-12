/*
 * Turning a map's entity list into a graph, and placing it.
 *
 * A map's logic is a directed graph and a map file is a flat list, so this is
 * most of the work. Kept apart from the drawing because it is the part with the
 * rules in it — and the part where a mistake is a wrong answer rather than an
 * ugly one.
 */

import type { Connection } from '../../types';
import { keyOf, type Entity, type Kind } from '../model';

export const NODE_WIDTH = 190;
export const HEADER_HEIGHT = 32;
export const PORT_HEIGHT = 14;
export const PORT_RADIUS = 4;

const NODE_PADDING = 6;
const COLUMN_GAP = 130;
const ROW_GAP = 20;
const COMPONENT_GAP = 60;

export interface GraphNode {
  key: string;
  entity: { classname: string; targetname: string; kind: Kind };

  /** Set for a target no entity has — a typo, drawn rather than dropped. */
  missing: boolean;
  outputs: string[];
  inputs: string[];
  height: number;
  x: number;
  y: number;
  out: GraphEdge[];
  in: GraphEdge[];

  /** The real entity, absent on a missing node. */
  source: Entity | null;
}

export interface GraphEdge {
  from: GraphNode;
  to: GraphNode;
  connection: Connection;
}

export interface Graph {
  nodes: GraphNode[];
  edges: GraphEdge[];
}

export const label = (node: GraphNode): string =>
  node.entity.targetname || node.entity.classname;

/**
 * The entities worth drawing, and the edges between them.
 *
 * Only the ones taking part in the logic. A map is six hundred entities and five
 * hundred of them are props and lights that connect to nothing — drawing them
 * would bury the hundred that matter, which is the opposite of what this is for.
 */
export function build(entities: Entity[]): Graph {
  const nodes = new Map<string, GraphNode>();
  const edges: GraphEdge[] = [];

  const make = (key: string, entity: GraphNode['entity'], source: Entity | null, missing = false): GraphNode => {
    const existing = nodes.get(key);
    if (existing) return existing;

    const node: GraphNode = {
      key, entity, missing, source,
      outputs: [], inputs: [], height: 0, x: 0, y: 0, out: [], in: [],
    };

    nodes.set(key, node);
    return node;
  };

  // Indexed by name, because a connection names its target and several entities
  // can share a name — firing all of them is a real and common map pattern.
  const byName = new Map<string, Entity[]>();

  for (const entity of entities) {
    if (!entity.targetname) continue;

    const name = entity.targetname.toLowerCase();
    const existing = byName.get(name);

    if (existing) existing.push(entity);
    else byName.set(name, [entity]);
  }

  const connect = (from: GraphNode, to: GraphNode, connection: Connection): void => {
    const edge = { from, to, connection };
    edges.push(edge);
    from.out.push(edge);
    to.in.push(edge);
  };

  for (const entity of entities) {
    for (const connection of entity.connections) {
      const from = make(keyOf(entity)!, entity, entity);
      const targets = byName.get(connection.target.toLowerCase()) ?? [];

      if (targets.length === 0) {
        // A typo in a target is invisible in a map file and instantly obvious
        // as an edge going nowhere.
        connect(from, make(`missing:${connection.target}`, {
          classname: '(no such entity)', targetname: connection.target, kind: 'missing',
        }, null, true), connection);

        continue;
      }

      for (const target of targets) connect(from, make(keyOf(target)!, target, target), connection);
    }
  }

  for (const node of nodes.values()) assignPorts(node);

  return { nodes: [...nodes.values()], edges };
}

/**
 * The ports a node shows.
 *
 * The outputs it fires and the inputs something sends it. There is no FGD here
 * to ask what else an entity would accept, and listing a guess would be worse
 * than listing what is demonstrably true.
 */
function assignPorts(node: GraphNode): void {
  node.outputs = [...new Set(node.out.map(edge => edge.connection.output))].sort();
  node.inputs = [...new Set(node.in.map(edge => edge.connection.input))].sort();

  node.height = HEADER_HEIGHT + NODE_PADDING
    + Math.max(node.outputs.length, node.inputs.length, 1) * PORT_HEIGHT + NODE_PADDING;
}

export const portY = (index: number): number =>
  HEADER_HEIGHT + NODE_PADDING + index * PORT_HEIGHT + PORT_HEIGHT / 2;

export const outputPortY = (node: GraphNode, name: string): number =>
  portY(Math.max(0, node.outputs.indexOf(name)));

export const inputPortY = (node: GraphNode, name: string): number =>
  portY(Math.max(0, node.inputs.indexOf(name)));

/**
 * Places the graph.
 *
 * Layered by longest path from a source, so a chain of relays reads left to
 * right in the order it actually fires. A force layout would look more organic
 * and be useless for this: the same map would come out differently every time,
 * and the one question this view answers is "what happens next".
 */
export function place(graph: Graph): Graph & { width: number; height: number } {
  let offsetY = 0;
  let width = 0;

  for (const component of findComponents(graph)) {
    const depth = assignDepths(component);
    const columns = new Map<number, GraphNode[]>();

    for (const node of component) {
      const column = depth.get(node.key)!;
      const existing = columns.get(column);

      if (existing) existing.push(node);
      else columns.set(column, [node]);
    }

    let componentHeight = 0;

    for (const [column, members] of columns) {
      members.sort((a, b) => label(a).localeCompare(label(b)));

      let y = offsetY;

      for (const node of members) {
        node.x = column * (NODE_WIDTH + COLUMN_GAP);
        node.y = y;
        y += node.height + ROW_GAP;
      }

      componentHeight = Math.max(componentHeight, y - offsetY);
      width = Math.max(width, (column + 1) * (NODE_WIDTH + COLUMN_GAP));
    }

    offsetY += componentHeight + COMPONENT_GAP;
  }

  return { ...graph, width, height: offsetY };
}

/** Weakly connected components, so unrelated bits of logic do not overlap. */
export function findComponents(graph: Graph): GraphNode[][] {
  const seen = new Set<string>();
  const components: GraphNode[][] = [];

  for (const node of graph.nodes) {
    if (seen.has(node.key)) continue;

    const component: GraphNode[] = [];
    const queue = [node];
    seen.add(node.key);

    while (queue.length) {
      const at = queue.pop()!;
      component.push(at);

      for (const edge of [...at.out, ...at.in]) {
        for (const next of [edge.from, edge.to]) {
          if (seen.has(next.key)) continue;

          seen.add(next.key);
          queue.push(next);
        }
      }
    }

    components.push(component);
  }

  // Biggest first: the largest piece of logic is almost always the one being
  // looked at, and it should not be below three orphan pairs.
  components.sort((a, b) => b.length - a.length);
  return components;
}

/**
 * A column for each node: one past the deepest thing that feeds it.
 *
 * Map logic contains cycles constantly — a relay that re-triggers itself is an
 * ordinary thing to write — so the graph is ordered first and relaxed second.
 * Only edges pointing forwards in that order count; an edge pointing backwards
 * is what closes a cycle, and following it would push its target one column
 * further out on every pass. Relaxing until nothing moved would then never
 * finish, and stopping after a fixed number of passes leaves the nodes strewn
 * across a column for every pass that ran.
 */
export function assignDepths(component: GraphNode[]): Map<string, number> {
  const order = depthFirstOrder(component);
  const position = new Map(order.map((node, index) => [node.key, index]));
  const depth = new Map(order.map(node => [node.key, 0]));

  for (const node of order) {
    for (const edge of node.out) {
      if (!depth.has(edge.to.key)) continue;
      if (position.get(edge.to.key)! <= position.get(node.key)!) continue;   // a back edge

      depth.set(edge.to.key, Math.max(depth.get(edge.to.key)!, depth.get(node.key)! + 1));
    }
  }

  return depth;
}

/**
 * Reverse post-order: an ordering in which every edge except the ones closing a
 * cycle points forwards.
 *
 * Iterative rather than recursive. A component can be hundreds of nodes deep,
 * and a recursive walk of one would be a stack overflow in a debugging tool —
 * which is a poor place to meet one.
 */
export function depthFirstOrder(component: GraphNode[]): GraphNode[] {
  // Entry points first, so a chain of logic starts at the thing that starts it
  // rather than wherever the entity list happened to begin.
  const roots = component.filter(node => node.in.length === 0);
  const starts = roots.length ? [...roots, ...component] : component;

  const seen = new Set<string>();
  const finished: GraphNode[] = [];

  for (const start of starts) {
    if (seen.has(start.key)) continue;

    const stack = [{ node: start, at: 0 }];
    seen.add(start.key);

    while (stack.length) {
      const frame = stack[stack.length - 1]!;

      if (frame.at >= frame.node.out.length) {
        finished.push(frame.node);
        stack.pop();
        continue;
      }

      const next = frame.node.out[frame.at++]!.to;
      if (seen.has(next.key)) continue;

      seen.add(next.key);
      stack.push({ node: next, at: 0 });
    }
  }

  return finished.reverse();
}

/**
 * A curve from an output port to an input port.
 *
 * Bezier rather than a straight line: edges between adjacent columns would
 * otherwise lie exactly on top of each other whenever two nodes share a row, and
 * a bundle of overlapping straight lines reads as one.
 */
export function bezier(x1: number, y1: number, x2: number, y2: number): string {
  const bend = Math.max(30, Math.abs(x2 - x1) / 2);
  return `M ${x1} ${y1} C ${x1 + bend} ${y1}, ${x2 - bend} ${y2}, ${x2} ${y2}`;
}

export const curveFor = (edge: GraphEdge): string => bezier(
  edge.from.x + NODE_WIDTH,
  edge.from.y + outputPortY(edge.from, edge.connection.output),
  edge.to.x,
  edge.to.y + inputPortY(edge.to, edge.connection.input));

/*
 * The separate pieces of logic a map contains.
 *
 * A map's connections are not one graph but dozens: a door and its button, a
 * trap and its trigger, the round timer and everything it ends. They share no
 * edges at all, and drawing them together is what makes the connections view a
 * wall — two hundred nodes of which the twenty you care about are somewhere in
 * the middle.
 *
 * So they can be taken one at a time, here and in the entity list, which is the
 * difference between "this map's logic" and "this trap".
 */

import { build, findComponents, label } from './graph/layout';
import { keyOf, type Entity } from './model';

export interface Component {
  /**
   * The name of the entity at the middle of it.
   *
   * A name rather than a number, because this ends up in the address: an index
   * — or an entity's position in the list, which is what its key is — moves the
   * moment anything is added, and a link would then point at a different piece
   * of logic than the one it was sent about.
   */
  id: string;
  label: string;

  /** The entities in it, by the key the selection uses. */
  keys: ReadonlySet<string>;

  /** Their names, which is what tells two same-named pieces apart. */
  members: readonly string[];
  size: number;
}

/**
 * Every piece of connected logic, biggest first.
 *
 * Entities that connect to nothing are left out entirely rather than becoming
 * five hundred components of one: they are not a piece of logic, they are the
 * rest of the map.
 */
export function componentsOf(entities: Entity[]): Component[] {
  const graph = build(entities);

  const pieces = findComponents(graph)
    .filter(nodes => nodes.length > 1)
    .map(nodes => {
      // The node with the most connections names the piece. It is the relay at
      // the middle of a trap or the button on a door — the thing somebody would
      // call it — and it beats the alphabet, which would name a trap after
      // whichever of its sounds began with "a".
      const busiest = [...nodes]
        .sort((a, b) => (b.out.length + b.in.length) - (a.out.length + a.in.length)
          || label(a).localeCompare(label(b)))[0]!;

      const keys = new Set(nodes.filter(node => !node.missing).map(node => node.key));

      return {
        id: label(busiest),
        label: `${label(busiest)} +${nodes.length - 1}`,
        keys,
        members: nodes.map(label),
        size: nodes.length,
      };
    })
    .sort((a, b) => b.size - a.size || a.label.localeCompare(b.label));

  return distinguish(pieces);
}

/**
 * Makes the names unique.
 *
 * Two pieces can genuinely be named the same thing: an entity with no targetname
 * is known by its classname, and a map with two unnamed trigger_multiples has
 * two pieces both called "trigger_multiple". dm_lockdown has exactly that. An
 * ambiguous name in the address picks the wrong piece, and two identical ones in
 * a list are two options that cannot be told apart.
 *
 * So a repeated name takes on the first of its members, alphabetically — still a
 * name rather than a position, and still the same after an edit elsewhere.
 */
function distinguish(pieces: Component[]): Component[] {
  const counts = new Map<string, number>();

  for (const piece of pieces) counts.set(piece.id, (counts.get(piece.id) ?? 0) + 1);

  return pieces.map(piece => {
    if ((counts.get(piece.id) ?? 0) < 2) return piece;

    const first = [...piece.members].sort()[0] ?? '';

    return { ...piece, id: `${piece.id}/${first}`, label: `${piece.label} (${first})` };
  });
}

/** Whether an entity belongs to the piece being looked at. Everything, if none is. */
export function inComponent(entity: Entity, component: Component | undefined): boolean {
  if (!component) return true;

  const key = keyOf(entity);
  return key !== null && component.keys.has(key);
}

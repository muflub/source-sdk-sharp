/*
 * The map's input/output graph.
 *
 * Nodes carry ports: inputs down the left, outputs down the right, with edges
 * landing on the port they belong to. Drawing every edge to the middle of a node
 * would answer "these two are connected" while hiding "by which output", which
 * is usually the question.
 *
 * Dragging from an output port to an input port makes a new connection. In
 * static mode that is an edit to the map; in live mode it is told to the running
 * game through AddOutput. Doing both would add it twice the moment the edited
 * map was loaded.
 *
 * SVG rather than canvas: a few hundred nodes at most, hit testing comes free,
 * and a node is an element that can be styled and found.
 */

import { useEffect, useMemo, useRef, useState, type MouseEvent as ReactMouseEvent, type ReactNode } from 'react';

import { connectEntities } from '../../api';
import type { CommandResult, Connection } from '../../types';
import { KIND_COLOURS, type Entity, type Mode } from '../model';
import { inComponent, type Component } from '../components';
import {
  bezier, build, curveFor, label, outputPortY, place, portY,
  HEADER_HEIGHT, NODE_WIDTH, PORT_HEIGHT, PORT_RADIUS,
  type GraphNode,
} from './layout';

interface View { x: number; y: number; scale: number }

/** A wire being dragged, from an output port towards the pointer. */
interface Wiring {
  from: GraphNode;
  output: string;
  x1: number;
  y1: number;
  to: { x: number; y: number };
}

interface Proposal {
  source: Entity;
  output: string;
  target: string;
  input: string;
}

const clip = (value: string, at: number): string =>
  value.length > at ? `${value.slice(0, at - 1)}…` : value;

export function GraphPane({
  entities, mode, selectedKey, components, group,
  onSelect, onChanged, onResult, onGroup,
}: {
  entities: Entity[];
  mode: Mode;
  selectedKey: string | null;

  /** The separate pieces of logic in the map, biggest first. */
  components: Component[];

  /** Which one is being drawn, or '' for all of them at once. */
  group: string;
  onSelect: (entity: Entity) => void;
  onChanged: () => void;
  onResult: (result: CommandResult) => void;
  onGroup: (group: string) => void;
}): ReactNode {
  const [view, setView] = useState<View>({ x: -40, y: -40, scale: 1 });
  const [wiring, setWiring] = useState<Wiring | null>(null);
  const [proposal, setProposal] = useState<Proposal | null>(null);
  const [status, setStatus] = useState('');

  const host = useRef<HTMLDivElement>(null);
  const drag = useRef<{ x: number; y: number; viewX: number; viewY: number } | null>(null);

  /*
   * The pane's size, measured rather than assumed.
   *
   * The viewBox is in graph units and the element is in pixels, so the two have
   * to agree or everything is drawn at the wrong scale — and a ref is null on
   * the first render, which is exactly when the viewBox is first needed. Left
   * unmeasured the graph draws against a guess and never corrects itself.
   */
  const [size, setSize] = useState({ width: 0, height: 0 });

  useEffect(() => {
    const element = host.current;
    if (!element) return;

    const observer = new ResizeObserver(() => setSize({
      width: element.clientWidth,
      height: element.clientHeight,
    }));

    observer.observe(element);
    setSize({ width: element.clientWidth, height: element.clientHeight });

    return () => observer.disconnect();
  }, []);

  /*
   * Only the piece being looked at, and laid out as though it were the whole
   * map: filtering after placing would leave the chosen piece wherever it
   * happened to sit among the others, which on a real map is off the screen.
   */
  const shown = useMemo(() => {
    const chosen = components.find(component => component.id === group);
    if (!chosen) return entities;

    return entities.filter(entity => inComponent(entity, chosen));
  }, [entities, components, group]);

  const layout = useMemo(() => place(build(shown)), [shown]);

  const toGraph = (event: { clientX: number; clientY: number }): { x: number; y: number } => {
    const rect = host.current?.getBoundingClientRect();

    return {
      x: view.x + ((event.clientX - (rect?.left ?? 0)) / view.scale),
      y: view.y + ((event.clientY - (rect?.top ?? 0)) / view.scale),
    };
  };

  const viewBox = (): string =>
    `${view.x} ${view.y} ${(size.width || 800) / view.scale} ${(size.height || 500) / view.scale}`;

  const finishWire = (event: ReactMouseEvent): void => {
    const started = wiring;
    setWiring(null);

    if (!started) return;

    // What is under the pointer decides the target. A port gives the input as
    // well; a node body gives only the target, and the editor opens with the
    // input blank rather than refusing a drag that plainly meant something.
    const under = document.elementFromPoint(event.clientX, event.clientY);
    const port = under?.closest<SVGGElement>('.port-in');
    const node = under?.closest<SVGGElement>('.node');

    const key = port?.dataset.node ?? node?.dataset.key;
    const target = layout.nodes.find(candidate => candidate.key === key);

    if (!target || target.missing || !started.from.source) return;

    if (!target.entity.targetname) {
      setStatus('that entity has no targetname, so nothing can be addressed to it');
      return;
    }

    setProposal({
      source: started.from.source,
      output: started.output,
      target: target.entity.targetname,
      input: port?.dataset.name ?? '',
    });
  };

  return (
    <>
      <div className="toolbar">
        <button onClick={() => setView({ x: -40, y: -40, scale: 1 })}>Reset view</button>
        <button onClick={() => {
          const node = layout.nodes.find(candidate => candidate.key === selectedKey);
          if (!node || !host.current) return;

          setView(previous => ({
            ...previous,
            x: node.x - (host.current!.clientWidth / previous.scale) / 2 + NODE_WIDTH / 2,
            y: node.y - (host.current!.clientHeight / previous.scale) / 2,
          }));
        }}>Centre on selection</button>
        {/* One piece at a time. A map's connections are dozens of separate
            graphs sharing no edges, and drawn together they are a wall. */}
        <label>
          logic
          <select value={group} onChange={event => onGroup(event.target.value)}>
            <option value="">all of it</option>
            {components.map(component => (
              <option key={component.id} value={component.id}>
                {component.label}
              </option>
            ))}
          </select>
        </label>

        <span className="hint">drag an output dot onto an input dot to wire them up</span>
        <span className="count">
          {layout.nodes.length} connected · {layout.edges.length} connections
          {layout.edges.some(edge => edge.connection.added) ? ' · some added here' : ''}
        </span>
      </div>

      {proposal && (
        <WireEditor
          proposal={proposal}
          mode={mode}
          onCancel={() => setProposal(null)}
          onDone={message => { setProposal(null); setStatus(message); }}
          onChanged={onChanged}
          onResult={onResult}
        />
      )}

      <p className="note" hidden={!status}>{status}</p>

      <div
        className={`graph${wiring ? ' wiring' : ''}`}
        ref={host}
        onMouseDown={event => {
          const port = (event.target as Element).closest<SVGGElement>('.port-out');

          if (port) {
            const node = layout.nodes.find(candidate => candidate.key === port.dataset.node);
            if (!node || node.missing) return;

            const output = port.dataset.name!;

            setWiring({
              from: node,
              output,
              x1: node.x + NODE_WIDTH,
              y1: node.y + outputPortY(node, output),
              to: toGraph(event),
            });

            event.preventDefault();
            return;
          }

          if ((event.target as Element).closest('.node-header')) return;

          drag.current = { x: event.clientX, y: event.clientY, viewX: view.x, viewY: view.y };
        }}
        onMouseMove={event => {
          if (wiring) {
            setWiring({ ...wiring, to: toGraph(event) });
            return;
          }

          if (!drag.current) return;

          const started = drag.current;

          setView(previous => ({
            ...previous,
            x: started.viewX - (event.clientX - started.x) / previous.scale,
            y: started.viewY - (event.clientY - started.y) / previous.scale,
          }));
        }}
        onMouseUp={event => { finishWire(event); drag.current = null; }}
        onMouseLeave={() => { drag.current = null; }}
        onWheel={event => {
          // About the pointer rather than the corner, so the thing being looked
          // at stays where it is.
          const rect = host.current?.getBoundingClientRect();
          const px = event.clientX - (rect?.left ?? 0);
          const py = event.clientY - (rect?.top ?? 0);

          setView(previous => {
            const beforeX = previous.x + px / previous.scale;
            const beforeY = previous.y + py / previous.scale;
            const scale = Math.min(4, Math.max(0.1, previous.scale * (event.deltaY < 0 ? 1.15 : 1 / 1.15)));

            return { scale, x: beforeX - px / scale, y: beforeY - py / scale };
          });
        }}
      >
        {layout.nodes.length === 0 ? (
          <p className="empty">nothing in this map is connected to anything</p>
        ) : (
          <svg className="graph-svg" viewBox={viewBox()}>
            <defs>
              <marker
                id="arrow" viewBox="0 0 10 10" refX={9} refY={5}
                markerWidth={5} markerHeight={5} orient="auto-start-reverse"
              >
                <path d="M 0 0 L 10 5 L 0 10 z" fill="currentColor" />
              </marker>
            </defs>

            <g className="edges">
              {layout.edges.map((edge, index) => (
                <path
                  key={index}
                  className={['edge',
                    edge.to.missing ? 'broken' : '',
                    edge.connection.added ? 'added' : '',
                    edge.from.key === selectedKey || edge.to.key === selectedKey ? 'related' : '',
                  ].filter(Boolean).join(' ')}
                  d={curveFor(edge)}
                  markerEnd="url(#arrow)"
                >
                  <title>
                    {`${label(edge.from)} . ${edge.connection.output}`
                      + `  ->  ${edge.connection.target} . ${edge.connection.input}`
                      + (edge.connection.param ? `  (${edge.connection.param})` : '')
                      + (edge.connection.delay ? `  after ${edge.connection.delay}s` : '')}
                  </title>
                </path>
              ))}
            </g>

            <g className="nodes">
              {layout.nodes.map(node => (
                <Node
                  key={node.key}
                  node={node}
                  selected={node.key === selectedKey}
                  onSelect={() => { if (node.source) onSelect(node.source); }}
                />
              ))}
            </g>

            <g className="wiring">
              {wiring && (
                <path className="edge wire" d={bezier(wiring.x1, wiring.y1, wiring.to.x, wiring.to.y)} />
              )}
            </g>
          </svg>
        )}
      </div>
    </>
  );
}

function Node({ node, selected, onSelect }: {
  node: GraphNode;
  selected: boolean;
  onSelect: () => void;
}): ReactNode {
  return (
    <g
      className={['node', selected ? 'selected' : '', node.missing ? 'missing' : ''].filter(Boolean).join(' ')}
      data-key={node.key}
      transform={`translate(${node.x},${node.y})`}
    >
      <rect className="node-body" width={NODE_WIDTH} height={node.height} rx={5} />

      {/* On the header only: a port starts a wire and the body is where one is
          dropped, so neither may also mean select. */}
      <rect
        className="node-header"
        width={NODE_WIDTH}
        height={HEADER_HEIGHT}
        rx={5}
        fill={node.missing ? 'none' : KIND_COLOURS[node.entity.kind]}
        style={node.missing ? undefined : { cursor: 'pointer' }}
        onClick={node.missing ? undefined : onSelect}
      />

      <text x={8} y={13} className="node-name">{clip(label(node), 26)}</text>
      <text x={8} y={25} className="node-class">{clip(node.entity.classname, 28)}</text>

      {node.inputs.map((name, index) => (
        <Port key={`in-${name}`} node={node} name={name} index={index} side="in" />
      ))}
      {node.outputs.map((name, index) => (
        <Port key={`out-${name}`} node={node} name={name} index={index} side="out" />
      ))}
    </g>
  );
}

function Port({ node, name, index, side }: {
  node: GraphNode;
  name: string;
  index: number;
  side: 'in' | 'out';
}): ReactNode {
  const y = portY(index);
  const x = side === 'in' ? 0 : NODE_WIDTH;

  return (
    <g className={`port port-${side}`} data-node={node.key} data-name={name} data-side={side}>
      {/* A generous invisible target: the visible dot is four pixels across,
          which is a very small thing to ask anyone to hit with a drag. */}
      <rect className="port-hit" x={x - 9} y={y - PORT_HEIGHT / 2} width={18} height={PORT_HEIGHT} />
      <circle className="port-dot" cx={x} cy={y} r={PORT_RADIUS} />
      <text
        className="port-label"
        x={side === 'in' ? 10 : NODE_WIDTH - 10}
        y={y + 3}
        textAnchor={side === 'in' ? 'start' : 'end'}
      >{clip(name, 18)}</text>
      <title>{side === 'in' ? `input: ${name}` : `output: ${name}`}</title>
    </g>
  );
}

function WireEditor({ proposal, mode, onCancel, onDone, onChanged, onResult }: {
  proposal: Proposal;
  mode: Mode;
  onCancel: () => void;
  onDone: (message: string) => void;
  onChanged: () => void;
  onResult: (result: CommandResult) => void;
}): ReactNode {
  const [input, setInput] = useState(proposal.input);
  const [param, setParam] = useState('');
  const [delay, setDelay] = useState('0');
  const [times, setTimes] = useState('-1');
  const [note, setNote] = useState('');

  const from = proposal.source.targetname || proposal.source.classname;

  const connect = async (): Promise<void> => {
    if (!input.trim()) {
      setNote('an input name is needed');
      return;
    }

    const connection: Connection = {
      output: proposal.output,
      target: proposal.target,
      input: input.trim(),
      param,
      delay: Number(delay) || 0,
      times: Number(times),
      added: true,
    };

    // Into the model either way, so the edge is on screen whatever the game
    // says. Nothing writes to the map here — a BSP is a compiled artefact, and
    // this is saved with the rest of the edits or exported as YAML.
    proposal.source.connections = [...proposal.source.connections, connection];
    onChanged();

    if (mode === 'static') {
      onDone(`added ${from}.${connection.output} → ${connection.target}.${connection.input} `
        + 'to the map — save it to keep it');
      return;
    }

    if (!proposal.source.targetname) {
      onDone('added to the model only — the source has no targetname, so the running game cannot be told');
      return;
    }

    const result = await connectEntities({
      source: proposal.source.targetname,
      output: connection.output,
      target: connection.target,
      input: connection.input,
      param: connection.param,
      delay: connection.delay,
      times: connection.times,
    });

    onResult(result);

    onDone(result.error
      ? `added to the model; the game refused it: ${result.error}`
      : `connected — the running game now has ${from}.${connection.output} firing `
        + `${connection.target}.${connection.input}`);
  };

  return (
    <div className="wire-editor">
      <span className="wire-from">{from} . {proposal.output}</span>
      <span className="arrow"> ▸ </span>
      <span className="wire-to">{proposal.target} . </span>

      <input
        autoFocus
        list="sg-inputs"
        placeholder="input"
        required
        value={input}
        onChange={event => setInput(event.target.value)}
      />
      <input placeholder="parameter" value={param} onChange={event => setParam(event.target.value)} />
      <input
        type="number" step="any" min="0" title="delay in seconds"
        value={delay} onChange={event => setDelay(event.target.value)}
      />
      <input
        type="number" title="times to fire; -1 is unlimited"
        value={times} onChange={event => setTimes(event.target.value)}
      />

      <button onClick={() => void connect()}>Connect</button>
      <button onClick={onCancel}>Cancel</button>
      <span className="count">{note}</span>
    </div>
  );
}

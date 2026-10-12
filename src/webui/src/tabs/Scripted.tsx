/*
 * Entity classes implemented in C#, and the instances of them that exist now.
 *
 * Distinct from the entity debugger next door: that one is about what the map
 * declares and what the engine holds, while this is about what this mod's own
 * code registered. The two answer different questions and joining them would
 * obscure both.
 *
 * Everything shown is read back from the registration itself rather than by
 * reflecting again on demand, so it describes what the engine was actually told.
 * Reflecting a second time could answer a little differently, which is the one
 * thing a debugging view must never do.
 */

import { useEffect, useMemo, useState, type ReactNode } from 'react';

import { getEntityClasses, getInstances } from '../api';
import type { ScriptedClass, ScriptedInstance } from '../types';
import { useUrlState } from '../hooks/useUrlState';

export function ScriptedTab({ visible, onLoaded }: {
  visible: boolean;
  onLoaded?: (classes: ScriptedClass[]) => void;
}): ReactNode {
  const [classes, setClasses] = useState<ScriptedClass[]>([]);
  const [instances, setInstances] = useState<ScriptedInstance[]>([]);
  const [filter, setFilter] = useUrlState('find');
  const [follow, setFollow] = useState(true);
  const [expanded, setExpanded] = useState<ReadonlySet<number>>(new Set());

  const loadClasses = async (): Promise<void> => {
    try {
      const data = await getEntityClasses();
      setClasses(data.classes);
      onLoaded?.(data.classes);
    } catch { /* left as it was; the shell reports the connection */ }
  };

  const loadInstances = async (): Promise<void> => {
    try {
      const data = await getInstances();
      if (!data.error) setInstances(data.instances);
    } catch { /* as above */ }
  };

  useEffect(() => { void loadClasses(); }, []);

  // Only while showing, and only while following: this one costs a game-thread
  // round trip, unlike everything else on the page.
  useEffect(() => {
    if (!visible) return;

    void loadInstances();
    if (!follow) return;

    const timer = setInterval(() => void loadInstances(), 1000);
    return () => clearInterval(timer);
  }, [visible, follow]);

  const text = filter.trim().toLowerCase();
  const shownClasses = useMemo(
    () => classes.filter(info => text === '' || info.className.toLowerCase().includes(text)),
    [classes, text]);

  const shownInstances = useMemo(
    () => instances.filter(instance => text === '' || instance.className.toLowerCase().includes(text)),
    [instances, text]);

  const toggle = (handle: number): void => setExpanded(previous => {
    const next = new Set(previous);
    next.has(handle) ? next.delete(handle) : next.add(handle);
    return next;
  });

  return (
    <>
      <div className="toolbar">
        <input
          type="search"
          list="sg-classnames"
          placeholder="filter classes and instances"
          value={filter}
          onChange={event => setFilter(event.target.value)}
        />
        <label>
          <input type="checkbox" checked={follow} onChange={event => setFollow(event.target.checked)} /> live
        </label>
        <button onClick={() => { void loadClasses(); void loadInstances(); }}>Refresh</button>
        {/* Labelled rather than pluralised: "1 classes" is the kind of wrong
            that makes a reader wonder whether the number is wrong too. */}
        <span className="count">live: {shownInstances.length} · classes: {classes.length}</span>
      </div>

      <h2>Declared classes</h2>
      <p className="note">
        Entity classes implemented in C#, with what a map may send them and what
        they send back. Read from the registration itself, so this is what the
        engine was actually told.
      </p>

      <div className="cards">
        {shownClasses.map(info => <ClassCard key={info.className} info={info} />)}
        {shownClasses.length === 0 && <p className="empty">no entity classes match</p>}
      </div>

      <h2>Live instances</h2>
      <div className="table-wrap">
        <table>
          <thead>
            <tr><th /><th>Class</th><th>Index</th><th>Origin</th><th>Health</th><th>Outputs fired</th></tr>
          </thead>
          <tbody>
            {shownInstances.map(instance => (
              <Instance
                key={instance.handle}
                instance={instance}
                open={expanded.has(instance.handle)}
                onToggle={() => toggle(instance.handle)}
              />
            ))}
            {shownInstances.length === 0 && (
              <tr><td className="empty" colSpan={6}>no live scripted entities</td></tr>
            )}
          </tbody>
        </table>
      </div>
    </>
  );
}

function ClassCard({ info }: { info: ScriptedClass }): ReactNode {
  const pills = (label: string, values: string[], extra = '') => (
    <>
      <dt>{label}</dt>
      <dd>
        {values.length === 0
          ? <span className="pill none">none</span>
          : values.map(value => <span className={`pill${extra}`} key={value}>{value}</span>)}
      </dd>
    </>
  );

  return (
    <div className="card">
      <h3>{info.className}</h3>
      <p className="sub">{info.kind} · {info.declaredBy} · {info.liveCount} live</p>
      <dl>
        {pills('Inputs', info.inputs)}
        {pills('Outputs', info.outputs, ' out')}
        {pills('Keys', info.vars.map(v => `${v.name}: ${v.type}`))}
      </dl>
    </div>
  );
}

function Instance({ instance, open, onToggle }: {
  instance: ScriptedInstance;
  open: boolean;
  onToggle: () => void;
}): ReactNode {
  const fired = instance.outputs.reduce((sum, output) => sum + output.fireCount, 0);

  return (
    <>
      <tr className={instance.valid ? undefined : 'invalid'}>
        <td><button className="expand" onClick={onToggle}>{open ? '▾' : '▸'}</button></td>
        <td className="name">{instance.className}</td>
        <td className="mono">{instance.valid ? instance.index : 'removed'}</td>
        <td className="mono">{instance.origin.map(n => n.toFixed(2)).join(', ')}</td>
        <td className="mono">{instance.health}</td>
        <td className="mono">{fired}</td>
      </tr>

      {open && (
        <tr className="detail">
          <td colSpan={6}>
            <div className="detail-grid">
              <Section
                title="Keys"
                pairs={instance.vars.map(v => [v.name, v.value === '' ? '""' : v.value])}
                empty="this class declares no map keys"
              />
              {/* The fire count is the point of showing outputs at all: "did this
                  connection ever run, and with what" is the question a map that
                  misbehaves actually asks. */}
              <Section
                title="Outputs"
                pairs={instance.outputs.map(o =>
                  [o.name, `${o.fireCount}×${o.lastValue !== null ? `  last: ${o.lastValue}` : ''}`])}
                empty="this class declares no outputs"
              />
              <Section
                title="Handle"
                pairs={[
                  ['handle', String(instance.handle)],
                  ['spawnflags', String(instance.spawnFlags)],
                  ['valid', instance.valid ? 'yes' : 'no — removed but still bound'],
                ]}
                empty=""
              />
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

function Section({ title, pairs, empty }: {
  title: string;
  pairs: [string, string][];
  empty: string;
}): ReactNode {
  return (
    <div>
      <h4>{title}</h4>
      {pairs.length === 0
        ? <div className="empty">{empty}</div>
        : (
          <div className="kv">
            {pairs.map(([key, value]) => (
              <span key={key} style={{ display: 'contents' }}>
                <span className="k">{key}</span>
                <span>{value}</span>
              </span>
            ))}
          </div>
        )}
    </div>
  );
}

/*
 * The bot inspector (plan_hl2mp_ai §7.2), web UI only: a bot list, and for
 * the chosen bot what it remembers (each opponent: where and when it was
 * last seen and heard, how sure it is, its health and ammo estimates as
 * uncertainty, its belief, its predicted intent and its learned habits; the
 * investigations; the items) and its skill allocation with what each drives.
 * The "doing and planning" part is the behaviour tree's: the chosen branch,
 * the push / hold / retreat verdict with its window arithmetic, the reason
 * terms, and the last decides' traces (the BT trace panel).
 */

import { useEffect, useState, type ReactNode } from 'react';

import { getAiBot, getAiBots } from '../api';
import { ammoBar, type AiActor, type AiBot, type AiBotListing, type AiDoing } from '../ai/aiInspect';

function AmmoBar({ actor }: { actor: AiActor }): ReactNode {
  const a = actor.ammo;
  if (a.clip <= 0) return <span className="muted">unknown</span>;
  const bar = ammoBar(a);
  return (
    <span className="ai-ammo" title={`P(empty) ${(a.pEmpty * 100).toFixed(0)} %`}>
      <span className="ai-ammo-track" data-testid={`ammo-${actor.slot}`}>
        <span className="ai-ammo-range" style={{ left: `${bar.left}%`, width: `${bar.width}%` }} data-width={bar.width.toFixed(1)} />
      </span>
      {a.min}–{a.max}/{a.clip}{a.reloading ? ' reloading' : ''}{a.push ? ' PUSH' : ''}
    </span>
  );
}

export function MemoryPart({ bot }: { bot: AiBot }): ReactNode {
  const memory = bot.record.memory;
  if (!memory) return <p className="muted">This bot has no memory: no map to remember.</p>;
  if (memory.actors.length === 0) return <p className="muted">It remembers nobody yet.</p>;
  return (
    <>
      <table className="ai-memory">
        <thead>
          <tr><th>actor</th><th>sight</th><th>sure</th><th>last seen</th><th>health</th><th>ammo</th><th>belief</th><th>intent</th><th>habits</th><th>rank</th></tr>
        </thead>
        <tbody>
          {memory.actors.map(a => (
            <tr key={`${a.slot}:${a.serial}`} data-testid={`actor-${a.slot}`}>
              <td>#{a.slot}</td>
              <td>{a.inSight ? 'seen' : a.lastHeardTick > a.lastSeenTick ? `heard ${a.lastHeardKind}` : 'lost'}</td>
              <td>{(a.confidence * 100).toFixed(0)} %</td>
              <td>{a.lastSeen.map(v => v.toFixed(0)).join(' ')} @{a.lastSeenTick}</td>
              <td>{a.healthEstimate}</td>
              <td><AmmoBar actor={a} /></td>
              <td>{a.belief ? `cell ${a.belief.argMaxCell}, ${a.belief.entropy.toFixed(1)} bits` : '—'}</td>
              <td>{a.intent} {a.intentScore.toFixed(2)}</td>
              <td>{a.habits ? `retreat ${a.habits.retreatTendency.toFixed(2)}, aggr ${a.habits.aggression.toFixed(2)}` : '—'}</td>
              <td>{a.rank || '—'} ({a.frags}/{a.deaths})</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p>{memory.investigations.length} investigations; items: {memory.items.filter(i => i.present === 'present').length} seen present,
        {' '}{memory.items.filter(i => i.present === 'gone').length} seen gone, {memory.items.filter(i => i.present === 'unknown').length} not looked at</p>
    </>
  );
}

export function SkillsPart({ bot }: { bot: AiBot }): ReactNode {
  const s = bot.record.skills;
  return (
    <div className="ai-skills">
      <p>{s.profile}: {s.spent} of {s.pool} points ({s.pool - s.spent} left)</p>
      <ul>
        {s.skills.map(k => (
          <li key={k.key} data-testid={`skill-${k.key}`}>
            {k.name} <meter min={0} max={s.cap} value={k.level} /> {k.level}
          </li>
        ))}
      </ul>
      <dl>
        {Object.entries(s.derived).map(([k, v]) => <div key={k}><dt>{k}</dt><dd>{Number.isFinite(v) ? v.toFixed(2) : String(v)}</dd></div>)}
      </dl>
    </div>
  );
}

export function AiTab({ visible }: { visible: boolean }): ReactNode {
  const [bots, setBots] = useState<AiBotListing[]>([]);
  const [slot, setSlot] = useState<number | null>(null);
  const [bot, setBot] = useState<AiBot | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!visible) return;
    let cancelled = false;
    void getAiBots().then(list => {
      if (cancelled) return;
      if (!Array.isArray(list)) {
        setError('the AI binding does not answer /ai/bots');
        return;
      }
      setBots(list);
      if (slot === null && list.length > 0) setSlot(list[0]!.slot);
    });
    return () => { cancelled = true; };
  }, [visible, slot]);

  useEffect(() => {
    if (!visible || slot === null) return;
    let cancelled = false;
    void getAiBot(slot).then(answer => {
      if (cancelled) return;
      if (answer.error) setError(answer.error);
      else setBot(answer);
    });
    return () => { cancelled = true; };
  }, [visible, slot]);

  return (
    <div className="ai-tab">
      <nav className="ai-bots">
        {bots.length === 0 ? <span className="muted">no bots</span> : bots.map(b => (
          <button key={b.slot} type="button" aria-pressed={b.slot === slot} onClick={() => setSlot(b.slot)}>{b.name}</button>
        ))}
      </nav>
      {error && <p className="error">{error}</p>}
      {bot && (
        <>
          <h3>Memory</h3>
          <MemoryPart bot={bot} />
          <h3>Skill allocation</h3>
          <SkillsPart bot={bot} />
          <h3>Doing and planning</h3>
          <DoingPart doing={bot.record.doing} />
        </>
      )}
    </div>
  );
}

const f2 = (n: number) => (Number.isFinite(n) ? n.toFixed(2) : String(n));

/** The tree's doing and planning, and the BT trace panel: each recent decide's visited nodes with their answers. */
function DoingPart({ doing }: { doing: AiDoing | null }): ReactNode {
  if (!doing) return <p className="muted">no decision layer</p>;
  return (
    <div className="ai-doing">
      <p data-testid="doing-now">
        {doing.branch}/{doing.leaf || '-'} · verdict <b>{doing.verdict || '-'}</b>
        {doing.targetSlot >= 0 && <> · target slot {doing.targetSlot}</>}
        {doing.goal && <> · goal ({doing.goal.map(v => v.toFixed(0)).join(' ')})</>}
      </p>
      <p data-testid="doing-window" className="muted">
        P(empty) {f2(doing.pEmpty)} + P(reloading) {f2(doing.pReloading)} · window {f2(doing.windowSeconds)} s vs arrive {f2(doing.arriveSeconds)} s
        {' '}· ammoTerm {f2(doing.ammoTerm)} · scoreboardTerm {f2(doing.scoreboardTerm)} · expires tick {doing.expires}
      </p>
      <ol className="ai-decides" data-testid="decides">
        {doing.decides.map(d => (
          <li key={d.tick}>
            tick {d.tick}: {d.chosen || '-'} (utility {f2(d.utility)}, ammoTerm {f2(d.ammoTerm)}, scoreboardTerm {f2(d.scoreboardTerm)})
            <span className="ai-trace">
              {d.nodes.map((n, i) => (
                <span key={i} className={`bt-${n.status}`}> {n.name}{n.value !== null ? `=${f2(n.value)}` : ''}</span>
              ))}
            </span>
          </li>
        ))}
      </ol>
    </div>
  );
}

/*
 * The bot inspector (plan_hl2mp_ai §7.2), web UI only: a bot list, and for
 * the chosen bot what it remembers (each opponent: where and when it was
 * last seen and heard, how sure it is, its health and ammo estimates as
 * uncertainty, its belief, its predicted intent and its learned habits; the
 * investigations; the items) and its skill allocation with what each drives.
 * The "doing and planning" part is the behaviour tree's: the chosen branch,
 * the push / hold / retreat verdict with its window arithmetic, the reason
 * terms, and the last decides' traces (the BT trace panel). Above them, the
 * live 3D map of every bot (ai/AiWorldPanel.tsx), sharing the selection.
 */

import { useEffect, useRef, useState, type ReactNode } from 'react';

import { getAiBot, getAiBots, getAiReplayBot, loadAiReplay } from '../api';
import { usePoll } from '../hooks/usePoll';
import { AiWorldPanel } from '../ai/AiWorldPanel';
import { ammoBar, type AiActor, type AiBot, type AiBotListing, type AiDoing, type AiReplayFrame, type AiReplayStatus } from '../ai/aiInspect';

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
          <tr><th>actor</th><th>sight</th><th>sure</th><th>last seen</th><th>health</th><th>ammo</th><th>belief</th><th>intent</th><th>habits</th><th>rank</th><th>grudge</th></tr>
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
              <td data-testid={`grudge-${a.slot}`}>{a.grudge > 0.05 ? a.grudge.toFixed(1) : '—'}</td>
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
            <span>{k.name}</span>
            <meter min={0} max={s.cap} value={k.level} />
            <span className="ai-skill-level">{k.level}</span>
          </li>
        ))}
      </ul>
      <dl>
        {Object.entries(s.derived).map(([k, v]) => <div key={k}><dt>{k}</dt><dd>{Number.isFinite(v) ? v.toFixed(2) : String(v)}</dd></div>)}
      </dl>
    </div>
  );
}

/** How often the tab asks the game again while it is showing. */
export const AI_REFRESH_MS = 2000;

export function AiTab({ visible }: { visible: boolean }): ReactNode {
  const [bots, setBots] = useState<AiBotListing[]>([]);
  const [slot, setSlot] = useState<number | null>(null);
  const [bot, setBot] = useState<AiBot | null>(null);
  const [error, setError] = useState<string | null>(null);

  // The newest selection, for answers that arrive after it moved on.
  const chosen = useRef(slot);
  chosen.current = slot;

  const refresh = async (): Promise<void> => {
    const list = await getAiBots();
    if (!Array.isArray(list)) {
      setError('the AI binding does not answer /ai/bots');
      return;
    }
    setBots(list);
    const asked = chosen.current;
    const playing = list.filter(b => b.state !== 'benched');
    const current = playing.some(b => b.slot === asked) ? asked : (playing[0]?.slot ?? null);
    if (current !== asked) {
      chosen.current = current;
      setSlot(current);
    }
    if (current === null) {
      setBot(null);
      setError(null);
      return;
    }
    const answer = await getAiBot(current);
    if (chosen.current !== current) return;
    if (answer.error) setError(answer.error);
    else {
      setBot(answer);
      setError(null);
    }
  };

  // At once when the tab is shown or another bot is chosen, then on the poll.
  useEffect(() => {
    if (visible) void refresh();
  }, [visible, slot]);

  usePoll(async () => {
    if (visible) await refresh();
  }, AI_REFRESH_MS);

  return (
    <div className="ai-tab">
      <nav className="ai-bots">
        {bots.length === 0 ? <span className="muted">no bots</span> : bots.map(b => (
          <button key={b.name} type="button" aria-pressed={b.slot === slot} disabled={b.state === 'benched'}
                  title={[b.tag, b.style, b.levels?.join(' '), b.state === 'benched' ? `benched: ${b.reason}` : ''].filter(Boolean).join(' · ')}
                  onClick={() => setSlot(b.slot)}>
            {b.name}{b.tag ? ` ${b.tag}` : ''}
          </button>
        ))}
      </nav>
      {error && <p className="error">{error}</p>}
      <AiWorldPanel visible={visible} selected={slot} onSelect={setSlot} />
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
      <h3>Replay</h3>
      <ReplayScrubber />
    </div>
  );
}

/**
 * The record/replay scrubber (plan_hl2mp_ai §7.1): an .airec loaded on the
 * game's machine, a tick to stand at, a bot; its record there (the same three
 * parts as live) and the intent the recording holds beside the one the replay
 * made, highlighted when they differ. While the replay seeks the answer says
 * so and is asked again.
 */
export function ReplayScrubber({ pollMs = 250 }: { pollMs?: number }): ReactNode {
  const [path, setPath] = useState('');
  const [status, setStatus] = useState<AiReplayStatus | null>(null);
  const [tick, setTick] = useState(0);
  const [slot, setSlot] = useState<number | null>(null);
  const [frame, setFrame] = useState<AiReplayFrame | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = async () => {
    setError(null);
    try {
      const s = await loadAiReplay(path);
      if (s.error) { setError(s.error); return; }
      setStatus(s);
      setTick(s.first);
      setSlot(s.bots.length > 0 ? s.bots[0]!.slot : null);
    } catch (e) {
      setError(`the recording would not load (${String(e)})`);
    }
  };

  useEffect(() => {
    if (!status || slot === null) return;
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const ask = () => {
      void getAiReplayBot(slot, tick).then(answer => {
        if (cancelled) return;
        if (answer.error) setError(answer.error);
        else if (answer.seeking !== undefined) timer = setTimeout(ask, pollMs);
        else setFrame(answer);
      });
    };
    ask();
    return () => { cancelled = true; if (timer) clearTimeout(timer); };
  }, [status, slot, tick, pollMs]);

  return (
    <div className="ai-replay">
      <form onSubmit={e => { e.preventDefault(); void load(); }}>
        <input aria-label="recording" placeholder="/path/to/session.airec" value={path} onChange={e => setPath(e.target.value)} />
        <button type="submit">Load</button>
      </form>
      {error && <p className="error">{error}</p>}
      {status && (
        <>
          <p data-testid="replay-status">{status.map}: ticks {status.first}–{status.last}, {status.bots.length} bots{status.world ? '' : ' (no world: the brains hold)'}</p>
          <input type="range" aria-label="tick" min={status.first} max={status.last} value={tick} onChange={e => setTick(Number(e.target.value))} />
          <span> tick {tick}</span>
          <nav className="ai-bots">
            {status.bots.map(b => (
              <button key={b.slot} type="button" aria-pressed={b.slot === slot} onClick={() => setSlot(b.slot)}>#{b.slot}</button>
            ))}
          </nav>
        </>
      )}
      {frame && (
        <>
          <p data-testid="replay-intent" className={frame.same ? 'ai-same' : 'ai-diff'}>
            at tick {frame.tick}, decide {frame.decide}: {frame.same ? 'the replay made the recorded intent' : 'the intents DIFFER'}
          </p>
          {!frame.same && (
            <div className="ai-diff-pair">
              <pre data-testid="replay-recorded">{JSON.stringify(frame.recorded, null, 1)}</pre>
              <pre data-testid="replay-replayed">{JSON.stringify(frame.replayed, null, 1)}</pre>
            </div>
          )}
          <MemoryPart bot={{ record: frame.record } as AiBot} />
          <DoingPart doing={frame.record.doing} />
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
      <p data-testid="doing-habit" className="muted">
        grudgeTerm {f2(doing.grudgeTerm)}
        {doing.habitSpot && <> · their usual place ({doing.habitSpot.map(v => v.toFixed(0)).join(' ')}){doing.preAiming ? ', looking at it' : ''}</>}
        {doing.moment && <> · last moment: {doing.moment}</>}
        {doing.streak && <> · on a {doing.streak} streak (aim error {f2(doing.streakAimErrorDeg ?? 0)}°)</>}
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

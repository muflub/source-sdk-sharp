/*
 * The log feeds.
 *
 * Two of them, because there are two: managed lines pass through a sink inside
 * the game and native ones do not, so the game's own console is a separate file
 * that this reads separately. They are deliberately not merged — managed output
 * reaches the console too, and one feed carrying both would show each line
 * twice. Guessing which console line was which managed line is the sort of
 * heuristic that is wrong exactly when the log matters.
 *
 * Each feed keeps its own accumulated lines. The ring at the other end holds the
 * last few thousand and hands back only what is new, so re-fetching to re-filter
 * would lose whatever had scrolled out of it in the meantime.
 */

import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';

import { getConsole, getLog } from '../api';
import { isMuted, load as loadMuted, save as saveMuted, signature } from './muting';
import { useUrlFlag, useUrlState } from '../hooks/useUrlState';
import type { LogLine, LogPage } from '../types';

export type Feed = 'console' | 'managed';

/** Enough to scroll back through without letting the page grow without bound. */
const KEEP = 5000;

interface FeedState {
  lines: LogLine[];
}

export interface LogsHandle {
  poll: () => Promise<void>;

  /** Marks everything from a sequence on, so a command's own output stands out. */
  markFrom: (sequence: number) => void;

  /** A line about this page rather than from the game. */
  pushLocal: (text: string) => void;

  reset: () => void;
}

export function LogsTab({ visible, handle }: {
  visible: boolean;
  handle: { current: LogsHandle | null };
}): ReactNode {
  /*
   * The mod's own lines first.
   *
   * The console carries everything, which is exactly its problem: the engine
   * says thousands of things a debugger has no interest in, and what somebody
   * opening this wants to know is what their code did. The console is one
   * selection away, and the muting is there for when it is the console that is
   * being read.
   */
  const [feed, setFeed] = useUrlState('feed', 'managed') as [Feed, (feed: Feed) => void];
  const [feeds, setFeeds] = useState<Record<Feed, FeedState>>({
    console: { lines: [] },
    managed: { lines: [] },
  });

  /*
   * How far each feed has been read.
   *
   * Refs rather than state, and moved the moment an answer arrives. State only
   * changes at the next render, and the poll runs from the shell's loop rather
   * than from a render — so a poll starting before React had committed the last
   * one asked for the same lines again and appended a second copy of each.
   */
  const since = useRef<Record<Feed, number>>({ console: 0, managed: 0 });

  const [filter, setFilter] = useUrlState('log');
  const [warningsOnly, setWarningsOnly] = useUrlFlag('warnings');
  const [follow, setFollow] = useState(true);
  const [paused, setPaused] = useState(false);
  const [markFrom, setMarkFrom] = useState(-1);
  const [muted, setMuted] = useState<string[]>(loadMuted);
  const [pane, setPane] = useUrlState('logpane', 'feed');
  const [consoleFile, setConsoleFile] = useState<{ path: string; following: boolean } | null>(null);

  const logPane = useRef<HTMLDivElement>(null);

  // Read by the poll callback, which is held across renders and would otherwise
  // close over whichever values existed when the loop started.
  const state = useRef({ feed, paused });
  state.current = { feed, paused };

  const append = (which: Feed, page: LogPage): void =>
    setFeeds(previous => {
      const held = previous[which].lines;

      /*
       * Only what is genuinely newer.
       *
       * A feed is a stream of increasing sequence numbers, so anything not
       * increasing has been shown already — and showing it twice reads as the
       * game having said it twice, which in a log is a lie. The position this
       * asks from should make that impossible; this is the guard that means a
       * poll answered twice, or answered out of order, cannot corrupt what is on
       * screen. React notices before anybody else does: two children with the
       * same key.
       */
      const last = held.length ? held[held.length - 1]!.seq : -1;
      const fresh = page.lines.filter(line => line.seq > last);

      if (fresh.length === 0) return previous;

      return { ...previous, [which]: { lines: [...held, ...fresh].slice(-KEEP) } };
    });

  useEffect(() => {
    handle.current = {
      poll: async () => {
        const { feed: which, paused: stopped } = state.current;
        if (stopped) return;

        const page = which === 'console'
          ? await getConsole(since.current[which])
          : await getLog(since.current[which]);

        if (page.next !== undefined) since.current[which] = page.next;

        if (page.following !== undefined) {
          setConsoleFile({ path: page.path ?? '', following: page.following });
        }

        if (page.lines?.length) append(which, page);
      },

      // Only meaningful against the feed the sequence came from: a command's log
      // range is the managed ring's, and marking the console with it would
      // highlight an unrelated stretch.
      markFrom: sequence => setMarkFrom(state.current.feed === 'managed' ? sequence : -1),

      pushLocal: text => setFeeds(previous => ({
        ...previous,
        [state.current.feed]: {
          ...previous[state.current.feed],
          lines: [
            ...previous[state.current.feed].lines,
            { seq: -1, warning: true, text, at: Date.now() },
          ],
        },
      })),

      reset: () => {
        setFeeds({ console: { lines: [] }, managed: { lines: [] } });
        since.current = { console: 0, managed: 0 };
        setMarkFrom(-1);
      },
    };

    return () => { handle.current = null; };
  }, [handle]);

  const shown = useMemo(() => {
    const text = filter.trim().toLowerCase();

    return feeds[feed].lines.filter(line =>
      (!warningsOnly || line.warning)
      && !isMuted(line.text, muted)
      && (text === '' || line.text.toLowerCase().includes(text)));
  }, [feeds, feed, filter, warningsOnly, muted]);

  /**
   * What is repeating most, in what is on screen.
   *
   * The counterpart to hiding a line by example: this is where you find out
   * which line to point at. A feed that is being drowned is usually being
   * drowned by two or three patterns, and they are invisible in the scroll —
   * they went past while you were reading something else.
   *
   * Counted by shape, so the same line about a different entity counts once, and
   * only over what is actually in the feed: this describes what is being read,
   * not everything the game has ever said.
   */
  const popular = useMemo(() => {
    const counts = new Map<string, number>();

    for (const line of feeds[feed].lines) {
      if (isMuted(line.text, muted)) continue;

      const shape = signature(line.text);
      counts.set(shape, (counts.get(shape) ?? 0) + 1);
    }

    return [...counts]
      .filter(([, count]) => count > 1)
      .sort((a, b) => b[1] - a[1])
      .slice(0, 12);
  }, [feeds, feed, muted]);

  const mute = (text: string): void => setMuted(previous => {
    const next = [...new Set([...previous, signature(text)])];

    saveMuted(next);
    return next;
  });

  const unmute = (pattern: string): void => setMuted(previous => {
    const next = previous.filter(one => one !== pattern);

    saveMuted(next);
    return next;
  });

  useEffect(() => {
    // Only while showing: a pane with no height cannot be scrolled, so doing it
    // hidden silently does nothing and the view opens somewhere in the middle.
    if (follow && visible && logPane.current) {
      logPane.current.scrollTop = logPane.current.scrollHeight;
    }
  }, [shown, follow, visible]);

  return (
    <>
      <div className="toolbar">
        <label>
          feed
          <select value={feed} onChange={event => setFeed(event.target.value as Feed)}>
            <option value="managed">C# only</option>
            <option value="console">game console</option>
          </select>
        </label>
        <input
          type="search"
          placeholder="filter lines"
          value={filter}
          onChange={event => setFilter(event.target.value)}
        />
        <label>
          <input
            type="checkbox"
            checked={warningsOnly}
            onChange={event => setWarningsOnly(event.target.checked)}
          /> warnings only
        </label>
        <label>
          <input type="checkbox" checked={follow} onChange={event => setFollow(event.target.checked)} /> follow
        </label>
        <button onClick={() => setPaused(!paused)}>{paused ? 'Resume' : 'Pause'}</button>
        <button onClick={() => {
          setFeeds(previous => ({ ...previous, [feed]: { lines: [] } }));
          setMarkFrom(-1);
        }}>Clear</button>
        <span className="count">{feeds[feed].lines.length} lines</span>
      </div>

      {/* The usual reason this feed is empty is a game started without
          -condebug, and an empty box does not say that. */}
      <p className="note" hidden={feed !== 'console' || consoleFile?.following !== false}>
        No console log at <b>{consoleFile?.path}</b> — the game console is only
        captured when the game was started with <code>-condebug</code>. The C# feed
        works either way.
      </p>

      <nav className="tabs sub">
        <button
          className="map-pane-tab"
          aria-selected={pane !== 'hidden'}
          onClick={() => setPane('feed')}
        >Feed</button>
        <button
          className="map-pane-tab"
          aria-selected={pane === 'hidden'}
          onClick={() => setPane('hidden')}
        >Hidden lines{muted.length > 0 ? ` (${muted.length})` : ''}</button>
      </nav>

      <div className="log-area" hidden={pane === 'hidden'}>
      <div className="log" ref={logPane} role="log" aria-live="polite">
        {shown.map((line, index) => (
          <div
            key={line.seq >= 0 ? `${feed}-${line.seq}` : `local-${index}`}
            className={`line${line.warning ? ' warn' : ''}${markFrom >= 0 && line.seq >= markFrom ? ' marked' : ''}`}
          >
            <span className="at">{clock(line.at)}</span>
            <span className="seq">{line.seq < 0 ? '' : line.seq}</span>
            <span className="text">{line.text}</span>

            <button
              className="mute"
              title={`hide lines like this: ${signature(line.text)}`}
              aria-label="hide lines like this"
              onClick={() => mute(line.text)}
            >hide</button>
          </div>
        ))}
      </div>

      {popular.length > 0 && (
        <aside className="popular" aria-label="Most repeated">
          <h3>Most repeated</h3>

          <ul>
            {popular.map(([shape, count]) => (
              <li key={shape}>
                <button
                  title={`hide lines like this: ${shape}`}
                  onClick={() => mute(shape)}
                >
                  <span className="count">{count}</span>
                  <span className="shape">{shape}</span>
                </button>
              </li>
            ))}
          </ul>

          <p className="note">Pick one to hide it. They come back from Hidden lines.</p>
        </aside>
      )}
      </div>

      <Hidden
        patterns={muted}
        hidden={pane !== 'hidden'}
        onShow={unmute}
        onShowAll={() => { setMuted([]); saveMuted([]); }}
      />
    </>
  );
}

/**
 * What is being hidden, and how to stop.
 *
 * Its own pane rather than a menu on the toolbar: these outlive the page — they
 * are kept in the browser, because the noise somebody is tired of is still noise
 * tomorrow — and something that persists needs somewhere it can be found later
 * by a person who has forgotten it exists.
 */
function Hidden({ patterns, hidden, onShow, onShowAll }: {
  patterns: readonly string[];
  hidden: boolean;
  onShow: (pattern: string) => void;
  onShowAll: () => void;
}): ReactNode {
  return (
    <div className="hidden-lines" hidden={hidden}>
      <p className="note">
        Lines matching these are left out of both feeds. A pattern is a line with
        its numbers replaced by <code>#</code>, so one entry covers the same line
        reported about a different entity, position or tick.
      </p>

      {patterns.length === 0
        ? <p className="empty">Nothing is hidden. Use <b>hide</b> on a line in the feed.</p>
        : (
          <>
            <ul className="pattern-list">
              {patterns.map(pattern => (
                <li key={pattern}>
                  <code>{pattern}</code>
                  <button onClick={() => onShow(pattern)}>Show again</button>
                </li>
              ))}
            </ul>

            <button onClick={onShowAll}>Show everything again</button>
          </>
        )}
    </div>
  );
}

/**
 * The time of day a line was logged.
 *
 * Seconds, no date: a log is read while it is happening, and the question it
 * answers is "was that when I pressed the button". The full stamp is on the
 * title, for the one time in twenty that the date matters.
 */
function clock(at: number | undefined): ReactNode {
  if (at === undefined) return '';

  const when = new Date(at);

  return <time dateTime={when.toISOString()} title={when.toString()}>
    {when.toLocaleTimeString(undefined, { hour12: false })}
  </time>;
}

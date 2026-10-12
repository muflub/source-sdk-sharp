/*
 * The log feeds.
 *
 * Two of them, kept apart on purpose: managed output reaches the game console
 * too, so one merged feed would show each managed line twice. The tests that
 * matter are about that separation and about the sequence numbers, which are
 * what let a poll ask for only what is new.
 */

import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createRef } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { LogsTab, type LogsHandle } from '../Logs';

vi.mock('../../api', () => ({ getLog: vi.fn(), getConsole: vi.fn() }));

const api = await import('../../api') as unknown as {
  getLog: ReturnType<typeof vi.fn>;
  getConsole: ReturnType<typeof vi.fn>;
};

const line = (seq: number, text: string, warning = false) =>
  ({ seq, text, warning, at: Date.UTC(2026, 7, 15, 12, 0, seq) });

beforeEach(() => {
  vi.clearAllMocks();
  api.getLog.mockResolvedValue({ next: 0, lines: [] });
  api.getConsole.mockResolvedValue({ next: 0, lines: [], path: '/x/console.log', following: true });
});

/*
 * Most of these are about the console feed, which is a selection away rather
 * than the one that opens — so they say so in the address, the same way a link
 * to this view would.
 */
function show({ feed = 'console' }: { feed?: string } = {}) {
  if (feed !== '') history.replaceState(null, '', `/?feed=${feed}`);

  const handle = createRef<LogsHandle>() as { current: LogsHandle | null };
  render(<LogsTab visible handle={handle} />);
  return handle;
}

const poll = async (handle: { current: LogsHandle | null }) =>
  await act(async () => { await handle.current!.poll(); });

// The mod's own lines first: the console carries everything, which is exactly
// its problem — what somebody opening this wants to know is what their code did.
it('shows the C# feed first', async () => {
  const handle = show({ feed: '' });

  api.getLog.mockResolvedValue({
    next: 2, lines: [line(0, '[SourceSharp/Server] hello'), line(1, 'Warning: refused', true)],
  });

  await poll(handle);

  expect(screen.getByText('[SourceSharp/Server] hello')).toBeTruthy();
  expect(api.getConsole).not.toHaveBeenCalled();
});

/*
 * The poll runs from the shell's loop rather than from a render, so it can start
 * again before React has committed the last one. Holding "how far I have read"
 * in state meant the second poll asked from the same place and appended a second
 * copy of every line it had just shown.
 */
it('does not re-read what it already has when polled twice in a row', async () => {
  const handle = show();
  const all = [line(0, 'Host_NewGame'), line(1, 'Host_Init')];

  api.getConsole.mockImplementation(async (from: number) => ({
    next: all.length,
    lines: all.filter(one => one.seq >= from),
    path: '/x/console.log',
    following: true,
  }));

  await act(async () => {
    await handle.current!.poll();
    await handle.current!.poll();
  });

  expect(api.getConsole).toHaveBeenLastCalledWith(2);
  expect(screen.getAllByText('Host_NewGame')).toHaveLength(1);
});

// The guard behind the position: whatever the server or the network does, a line
// already shown is not shown again.
it('ignores a page it has already been given', async () => {
  const handle = show();
  const page = {
    next: 2, lines: [line(0, 'Host_NewGame'), line(1, 'Host_Init')],
    path: '/x/console.log', following: true,
  };

  api.getConsole.mockResolvedValue(page);

  await poll(handle);
  await poll(handle);

  expect(screen.getAllByText('Host_NewGame')).toHaveLength(1);
});

// The whole reason the two feeds exist separately.
it('keeps each feed to itself', async () => {
  const handle = show();

  api.getConsole.mockResolvedValue({ next: 1, lines: [line(0, 'from the console')], path: '', following: true });
  await poll(handle);

  api.getLog.mockResolvedValue({ next: 1, lines: [line(0, 'from C#')] });
  await userEvent.selectOptions(screen.getByRole('combobox'), 'managed');
  await poll(handle);

  expect(screen.getByText('from C#')).toBeTruthy();
  expect(screen.queryByText('from the console')).toBeNull();
});

// A poll asks for what is newer than the last sequence it saw; asking from zero
// every time would re-send the whole ring on every tick.
it('asks only for what it has not seen', async () => {
  const handle = show();

  api.getConsole.mockResolvedValue({ next: 5, lines: [line(4, 'a')], path: '', following: true });
  await poll(handle);
  await poll(handle);

  expect(api.getConsole).toHaveBeenLastCalledWith(5);
});

describe('filtering', () => {
  const withLines = async () => {
    const handle = show();

    api.getConsole.mockResolvedValue({
      next: 3,
      lines: [line(0, 'ordinary'), line(1, 'Warning: something', true), line(2, 'another')],
      path: '', following: true,
    });

    await poll(handle);
    return handle;
  };

  it('matches text', async () => {
    await withLines();
    await userEvent.type(screen.getByPlaceholderText('filter lines'), 'another');

    expect(screen.getByText('another')).toBeTruthy();
    expect(screen.queryByText('ordinary')).toBeNull();
  });

  it('can show warnings alone', async () => {
    await withLines();
    await userEvent.click(screen.getByLabelText(/warnings only/));

    expect(screen.getByText('Warning: something')).toBeTruthy();
    expect(screen.queryByText('ordinary')).toBeNull();
  });
});

describe('pausing', () => {
  it('stops asking, and resumes where it left off', async () => {
    const handle = show();

    await userEvent.click(screen.getByRole('button', { name: 'Pause' }));
    await poll(handle);
    expect(api.getConsole).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole('button', { name: 'Resume' }));
    await poll(handle);
    expect(api.getConsole).toHaveBeenCalled();
  });
});

describe('the console log file', () => {
  // The usual reason this feed is empty is a game started without -condebug,
  // and an empty box does not say that.
  it('is named when the game is not writing one', async () => {
    const handle = show();

    api.getConsole.mockResolvedValue({ next: 0, lines: [], path: '/game/console.log', following: false });
    await poll(handle);

    await waitFor(() => expect(screen.getByText('/game/console.log')).toBeTruthy());
  });
});

describe('the handle the shell holds', () => {
  it('adds a line about this page when the game cannot be reached', async () => {
    const handle = show();

    await act(async () => { handle.current!.pushLocal('debug-api: could not reach the game'); });

    expect(screen.getByText('debug-api: could not reach the game')).toBeTruthy();
  });

  it('throws everything away when the instance changes', async () => {
    const handle = show();

    api.getConsole.mockResolvedValue({ next: 1, lines: [line(0, 'from the old game')], path: '', following: true });
    await poll(handle);

    await act(async () => { handle.current!.reset(); });

    expect(screen.queryByText('from the old game')).toBeNull();
  });
});

describe('hiding lines like an existing one', () => {
  // A Source console logs per-tick traces by the hundred, and filtering says
  // what to keep when what you know is what to throw away.
  const noisy = [
    line(0, '[SourceSharp] > PlayerMove (server)'),
    line(1, '[SourceSharp/Combat] player#1 hit worldspawn#0 for 25'),
    line(2, '[SourceSharp] > PlayerMove (server)'),
    line(3, '[SourceSharp/Server] hello from C#!'),
  ];

  const withLines = async () => {
    const handle = show();

    api.getConsole.mockResolvedValue({
      next: noisy.length, lines: noisy, path: '/x/console.log', following: true,
    });

    await poll(handle);
    return handle;
  };

  let seq = 10;

  const arriving = async (handle: { current: LogsHandle | null }, text: string) => {
    // Increasing, because a feed only accepts what is newer than what it holds —
    // the guard that keeps a re-read page from listing everything twice.
    seq += 1;

    api.getConsole.mockResolvedValue({
      next: seq + 1, lines: [line(seq, text)], path: '/x/console.log', following: true,
    });

    await poll(handle);
  };

  beforeEach(() => localStorage.clear());

  it('hides every line with the same shape', async () => {
    await withLines();

    await userEvent.click(screen.getAllByLabelText('hide lines like this')[0]!);

    // In the feed: the pattern itself is listed on the Hidden lines tab, which
    // is the same text and not what this is asking about.
    const feed = within(screen.getByRole('log'));

    expect(feed.queryByText(/PlayerMove/)).toBeNull();
    expect(feed.getByText(/hello from C#/)).toBeTruthy();
  });

  // The noisiest lines are the ones carrying a different number every time, so
  // matching the text exactly would hide one line and leave its thousand
  // siblings.
  it('ignores the numbers in it', async () => {
    const handle = await withLines();

    await userEvent.click(screen.getAllByLabelText('hide lines like this')[1]!);
    expect(within(screen.getByRole('log')).queryByText(/hit worldspawn/)).toBeNull();

    // The same line reported again with every number in it different, which is
    // what the noisiest lines do and what matching the text exactly would miss.
    await arriving(handle, '[SourceSharp/Combat] player#2 hit worldspawn#7 for 40');
    expect(within(screen.getByRole('log')).queryByText(/hit worldspawn/)).toBeNull();

    // But a line about something else is a different line, not the same one
    // with different numbers.
    await arriving(handle, '[SourceSharp/Combat] player#1 hit prop_physics#3 for 25');
    expect(within(screen.getByRole('log')).getByText(/hit prop_physics/)).toBeTruthy();
  });

  // Its own pane rather than a menu: these outlive the page, and something that
  // persists needs somewhere it can be found by a person who has forgotten it
  // exists.
  it('lists what is hidden on its own tab, and can show it again', async () => {
    await withLines();

    await userEvent.click(screen.getAllByLabelText('hide lines like this')[0]!);
    await userEvent.click(screen.getByRole('button', { name: /^Hidden lines/ }));

    expect(screen.getByRole('listitem')).toHaveProperty('textContent',
      expect.stringContaining('PlayerMove'));

    await userEvent.click(screen.getByRole('button', { name: 'Show again' }));

    expect(screen.getByRole('button', { name: 'Hidden lines' })).toBeTruthy();
    expect(screen.getByText(/nothing is hidden/i)).toBeTruthy();
  });

  // Kept in the browser, because the noise a person is tired of outlives the
  // page they got tired of it on — and this page reloads itself on a rebuild.
  it('remembers what was hidden', async () => {
    await withLines();
    await userEvent.click(screen.getAllByLabelText('hide lines like this')[0]!);

    expect(localStorage.getItem('debug-api.muted-lines')).toContain('PlayerMove');
  });
});

it('shows when each line was logged', async () => {
  const handle = show();

  api.getConsole.mockResolvedValue({
    next: 1, lines: [line(0, 'Host_NewGame')], path: '/x/console.log', following: true,
  });

  await poll(handle);

  const when = screen.getByText('Host_NewGame').closest('.line')!.querySelector('time');
  expect(when?.getAttribute('datetime')).toBe(new Date(Date.UTC(2026, 7, 15, 12, 0, 0)).toISOString());
});

describe('what is repeating', () => {
  const repeated = [
    line(0, '[SourceSharp/Client] netentity 41 Created: SpawnCount=0'),
    line(1, '[SourceSharp/Client] netentity 42 Created: SpawnCount=0'),
    line(2, '[SourceSharp/Client] netentity 43 Created: SpawnCount=0'),
    line(3, '[SourceSharp/Server] hello from C#!'),
  ];

  beforeEach(() => localStorage.clear());

  const withRepeats = async () => {
    const handle = show();

    api.getConsole.mockResolvedValue({
      next: repeated.length, lines: repeated, path: '/x/console.log', following: true,
    });

    await poll(handle);
    return handle;
  };

  // The point of the column: a feed being drowned is usually being drowned by
  // two or three patterns, and they are invisible in the scroll.
  it('counts by shape, so one entry covers the whole flood', async () => {
    await withRepeats();

    const list = within(screen.getByLabelText('Most repeated'));

    expect(list.getByText('3')).toBeTruthy();
    expect(list.getByText(/netentity # Created/)).toBeTruthy();
  });

  it('says nothing about a line that happened once', async () => {
    await withRepeats();

    expect(within(screen.getByLabelText('Most repeated')).queryByText(/hello from C#/)).toBeNull();
  });

  it('hides the pattern when it is picked', async () => {
    await withRepeats();

    await userEvent.click(within(screen.getByLabelText('Most repeated')).getByText(/netentity/));

    expect(within(screen.getByRole('log')).queryByText(/netentity/)).toBeNull();

    // And leaves the column, having stopped being what is repeating.
    expect(screen.queryByLabelText('Most repeated')).toBeNull();
  });
});

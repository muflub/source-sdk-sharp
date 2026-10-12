/*
 * The entity event log.
 *
 * The distinction the feed draws is the thing worth testing: outputs and inputs
 * are reported as they happen, spawns and removals are found by comparing
 * samples, and the footer says which is running rather than leaving a gap to be
 * mistaken for a quiet period.
 */

import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { EventsPane, type EventsHandle } from '../EventsPane';
import type { EntityEvent } from '../../types';

vi.mock('../../api', () => ({ getEvents: vi.fn() }));

const api = await import('../../api') as unknown as { getEvents: ReturnType<typeof vi.fn> };

const event = (over: Partial<EntityEvent>): EntityEvent => ({
  seq: 0, time: 1, kind: 'spawned', classname: 'logic_relay',
  name: '', detail: 'appeared', index: 7, handle: 42, ...over,
});

beforeEach(() => {
  vi.clearAllMocks();
  api.getEvents.mockResolvedValue({ next: 0, sampling: true, samplePeriod: 0.5, events: [], count: 0 });
});

function show() {
  const handle = { current: null } as { current: EventsHandle | null };
  render(<EventsPane handle={handle} />);
  return handle;
}

const poll = async (handle: { current: EventsHandle | null }) =>
  await act(async () => { await handle.current!.poll(); });

async function withEvents(events: EntityEvent[]) {
  const handle = show();

  api.getEvents.mockResolvedValue({
    next: events.length, sampling: true, samplePeriod: 0.5, events, count: events.length,
  });

  await poll(handle);
  return handle;
}

/*
 * The poll runs from the shell's loop, not from a render, so it can start again
 * before React has committed the last one. Keeping "how far I have read" in
 * state made that ask for the same events twice and list each of them twice —
 * which React reported as two children with the same key, and which looked to a
 * reader like the game firing everything twice.
 */
it('does not re-read what it already has when polled twice in a row', async () => {
  const handle = show();
  const all = [event({ seq: 0, name: 'first' }), event({ seq: 1, name: 'second' })];

  // The real feed hands back what is new, which is the whole point of asking
  // from somewhere: a poll that asks from the wrong place gets it all again.
  api.getEvents.mockImplementation(async (from: number) => {
    const events = all.filter(one => one.seq >= from);
    return { next: all.length, sampling: true, samplePeriod: 0.5, count: events.length, events };
  });

  // Two polls with no render between them, which is what a tick arriving while
  // the last one is still settling does.
  await act(async () => {
    await handle.current!.poll();
    await handle.current!.poll();
  });

  expect(api.getEvents).toHaveBeenLastCalledWith(2);
  expect(screen.getAllByRole('row').slice(1)).toHaveLength(2);
});

it('ignores a page it has already been given', async () => {
  const handle = await withEvents([event({ seq: 0, name: 'first' })]);

  // The same answer again, which is what a server that lost track of the
  // position would send. An event listed twice reads as it having happened
  // twice, in the one view whose whole job is to say what happened.
  await poll(handle);

  expect(screen.getAllByRole('row').slice(1)).toHaveLength(1);
});

it('lists what happened, newest first', async () => {
  await withEvents([
    event({ seq: 0, name: 'first', detail: 'appeared' }),
    event({ seq: 1, name: 'second', detail: 'gone', kind: 'removed' }),
  ]);

  const rows = screen.getAllByRole('row').slice(1);
  expect(rows[0]!.textContent).toContain('second');
});

// A poll asks for what is newer than the last sequence seen.
it('asks only for what it has not seen', async () => {
  const handle = await withEvents([event({ seq: 0 })]);
  await poll(handle);

  expect(api.getEvents).toHaveBeenLastCalledWith(1);
});

describe('the footer', () => {
  it('says the sampler is running, and how often', async () => {
    await withEvents([event({})]);
    expect(screen.getByText(/sampling every 0.5s/)).toBeTruthy();
  });

  // Sampling only runs while something is reading, so a gap is expected — and a
  // view that did not say so would show it as a quiet period.
  it('says when it is not', async () => {
    const handle = show();
    api.getEvents.mockResolvedValue({ next: 0, sampling: false, samplePeriod: 0.5, events: [], count: 0 });
    await poll(handle);

    expect(screen.getByText(/not sampling/)).toBeTruthy();
  });
});

describe('filtering', () => {
  it('can hide a kind', async () => {
    await withEvents([
      event({ seq: 0, name: 'a_spawn', kind: 'spawned' }),
      event({ seq: 1, name: 'an_output', kind: 'output' }),
    ]);

    await userEvent.click(screen.getByLabelText('spawns'));

    expect(screen.queryByText('a_spawn')).toBeNull();
    expect(screen.getByText('an_output')).toBeTruthy();
  });

  it('matches text in the name, class or detail', async () => {
    await withEvents([
      event({ seq: 0, name: 'plug_pull', detail: 'Trigger <- debug-api', kind: 'input' }),
      event({ seq: 1, name: 'other', detail: 'appeared' }),
    ]);

    await userEvent.type(screen.getByPlaceholderText('filter events'), 'debug-api');

    expect(screen.getByText('plug_pull')).toBeTruthy();
    expect(screen.queryByText('other')).toBeNull();
  });
});

describe('pausing', () => {
  it('stops asking, and resumes', async () => {
    const handle = show();

    await userEvent.click(screen.getByRole('button', { name: 'Pause' }));
    await poll(handle);
    expect(api.getEvents).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole('button', { name: 'Resume' }));
    await poll(handle);
    expect(api.getEvents).toHaveBeenCalled();
  });
});

it('is emptied when the instance changes', async () => {
  const handle = await withEvents([event({ name: 'from the old game' })]);

  await act(async () => { handle.current!.reset(); });

  expect(screen.queryByText('from the old game')).toBeNull();
  expect(screen.getByText('nothing recorded yet')).toBeTruthy();
});

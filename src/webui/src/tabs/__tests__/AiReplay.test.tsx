/*
 * The record/replay scrubber (plan_hl2mp_ai §7.1): a recording loaded, a
 * bot's frame at a tick, the intents' agreement shown, a seek waited out.
 */

import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';

import { ReplayScrubber } from '../Ai';
import type { AiReplayFrame, AiReplayStatus } from '../../ai/aiInspect';

vi.mock('../../api', () => ({ getAiBots: vi.fn(), getAiBot: vi.fn(), loadAiReplay: vi.fn(), getAiReplayBot: vi.fn() }));

const api = await import('../../api') as unknown as {
  loadAiReplay: ReturnType<typeof vi.fn>; getAiReplayBot: ReturnType<typeof vi.fn>;
};

const status: AiReplayStatus = {
  path: '/x/dm.airec', map: 'dm_lockdown', seed: 7, world: true, first: 1, last: 2400, ticks: 2400,
  bots: [{ slot: 2, levels: [6, 6, 5, 5, 5, 5, 6, 4, 5, 3] }], at: -1, seeking: -1, fault: null,
};

const frame = (same: boolean): AiReplayFrame => ({
  tick: 1, decide: -1, same, recorded: { seq: 1 }, replayed: same ? { seq: 1 } : { seq: 2 },
  record: { slot: 2, serial: 1, tick: 1, memory: null, doing: null,
    skills: { profile: 'recorded', skills: [], spent: 50, pool: 60, cap: 10, derived: {} } },
});

beforeEach(() => {
  vi.clearAllMocks();
  api.loadAiReplay.mockResolvedValue(status);
});

async function loaded(): Promise<void> {
  render(<ReplayScrubber pollMs={1} />);
  fireEvent.change(screen.getByLabelText('recording'), { target: { value: '/x/dm.airec' } });
  fireEvent.click(screen.getByText('Load'));
  await screen.findByTestId('replay-status');
}

it('loads a recording and shows a bot whose replay made the recorded intent', async () => {
  api.getAiReplayBot.mockResolvedValue(frame(true));
  await loaded();
  expect(api.loadAiReplay).toHaveBeenCalledWith('/x/dm.airec');
  expect(screen.getByTestId('replay-status').textContent).toContain('ticks 1–2400');
  expect((await screen.findByTestId('replay-intent')).className).toBe('ai-same');
  expect(api.getAiReplayBot).toHaveBeenCalledWith(2, 1);
});

it('highlights intents that differ, both side by side', async () => {
  api.getAiReplayBot.mockResolvedValue(frame(false));
  await loaded();
  expect((await screen.findByTestId('replay-intent')).className).toBe('ai-diff');
  expect(screen.getByTestId('replay-recorded').textContent).toContain('1');
  expect(screen.getByTestId('replay-replayed').textContent).toContain('2');
});

it('asks again while the replay seeks', async () => {
  api.getAiReplayBot.mockResolvedValueOnce({ seeking: 1, at: -1 }).mockResolvedValueOnce({ seeking: 1, at: -1 }).mockResolvedValue(frame(true));
  await loaded();
  await screen.findByTestId('replay-intent');
  expect(api.getAiReplayBot).toHaveBeenCalledTimes(3);
});

it('moves to the tick the slider names', async () => {
  api.getAiReplayBot.mockResolvedValue(frame(true));
  await loaded();
  fireEvent.change(screen.getByLabelText('tick'), { target: { value: '900' } });
  await vi.waitFor(() => expect(api.getAiReplayBot).toHaveBeenCalledWith(2, 900));
});

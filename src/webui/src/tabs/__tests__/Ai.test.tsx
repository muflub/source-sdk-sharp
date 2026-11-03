/*
 * The bot inspector tab (plan_hl2mp_ai §7.2) over recorded samples: a full
 * memory (an opponent with an ammo estimate, a belief and habits) and an
 * empty one; the skills part's meters.
 */

import { render, screen } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';

import { AiTab } from '../Ai';
import type { AiBot } from '../../ai/aiInspect';

vi.mock('../../api', () => ({ getAiBots: vi.fn(), getAiBot: vi.fn() }));

const api = await import('../../api') as unknown as { getAiBots: ReturnType<typeof vi.fn>; getAiBot: ReturnType<typeof vi.fn> };

const skills = {
  profile: 'hard', spent: 50, pool: 60, cap: 10,
  skills: [{ key: 'accuracy', name: 'Accuracy', level: 7 }, { key: 'alertness', name: 'Alertness', level: 6 }],
  derived: { recognize_s: 0.3, memory_half_life_s: 8 },
};

const full: AiBot = {
  record: {
    slot: 2, serial: 1, tick: 900, skills,
    doing: {
      branch: 'Fight', leaf: 'push', goal: [150, 0, 0], targetSlot: 3, verdict: 'push', pEmpty: 0.95, pReloading: 0, windowSeconds: 1.5,
      arriveSeconds: 0.99, scoreboardTerm: 0.25, ammoTerm: 0.95, expires: 916,
      decides: [{ tick: 900, chosen: 'push', utility: 0.8, scoreboardTerm: 0.25, ammoTerm: 0.95,
        nodes: [{ id: 0, name: 'root', status: 'success', value: 0.8 }, { id: 9, name: 'push test', status: 'success', value: 1 }] }],
    },
    memory: {
      actors: [{
        slot: 1, serial: 1073741829, inSight: false, confidence: 0.5, lastSeen: [100, 200, 0], lastSeenTick: 800, lastHeard: [120, 210, 0],
        lastHeardTick: 850, lastHeardKind: 'Gunfire', healthEstimate: 80, weaponSeen: 3, rank: 1, frags: 4, deaths: 1,
        ammo: { weapon: 3, clip: 45, min: 10, max: 28, histogram: [], reloading: false, reloadEndTick: 0, pEmpty: 0.1, pReloading: 0, push: false, windowSeconds: 1.5, barWidth: 0.4 },
        belief: { argMaxCell: 12, argMax: [130, 220, 0], entropy: 2.5, cells: [12], mass: [1] }, intent: 'Approach', intentScore: 0.6,
        habits: { retreatTendency: 0.25, aggression: 0.75, reloadStyle: 0.9, engagementRange: [1, 2, 0, 0], campCells: [], campCounts: [], pickupItems: [], reloadBehaviours: [0, 1, 0], routeCells: [12], routeCounts: [3] },
      }],
      investigations: [], items: [{ index: 0, className: 'item_healthkit', position: [0, 0, 0], present: 'gone', seenTick: 700, expectedBack: 2700, respawnSeconds: 30 }],
    },
  },
  perception: { slot: 2, eye: [0, 0, 64], yaw: 0, pitch: 0, fov: 90, range: 6000, candidates: [], heard: [] },
};

beforeEach(() => {
  vi.clearAllMocks();
  api.getAiBots.mockResolvedValue([{ slot: 2, serial: 1, name: 'Alyx', profile: 'hard', spent: 50 }]);
});

it('shows what a bot remembers of an opponent, its ammo as a bar as wide as the range', async () => {
  api.getAiBot.mockResolvedValue(full);
  render(<AiTab visible />);
  const row = await screen.findByTestId('actor-1');
  expect(row.textContent).toContain('heard Gunfire');
  expect(row.textContent).toContain('10–28/45');
  expect(row.textContent).toContain('cell 12');
  expect(row.textContent).toContain('retreat 0.25');
  const range = screen.getByTestId('ammo-1').firstElementChild as HTMLElement;
  expect(range.dataset.width).toBe('40.0');
  expect(range.style.left).toBe(`${(10 / 45) * 100}%`);
});

it('says so when the bot remembers nobody', async () => {
  api.getAiBot.mockResolvedValue({ ...full, record: { ...full.record, memory: { actors: [], investigations: [], items: [] } } });
  render(<AiTab visible />);
  expect(await screen.findByText('It remembers nobody yet.')).toBeTruthy();
});

it('shows the skill allocation with a meter per skill', async () => {
  api.getAiBot.mockResolvedValue(full);
  render(<AiTab visible />);
  const accuracy = await screen.findByTestId('skill-accuracy');
  expect((accuracy.querySelector('meter') as HTMLMeterElement).value).toBe(7);
  expect(screen.getByText('hard: 50 of 60 points (10 left)')).toBeTruthy();
});

it('shows what the bot is doing: the verdict, its window arithmetic and the reason terms, and the trace', async () => {
  api.getAiBot.mockResolvedValue(full);
  render(<AiTab visible />);
  const now = await screen.findByTestId('doing-now');
  expect(now.textContent).toContain('Fight/push');
  expect(now.textContent).toContain('verdict push');
  expect(screen.getByTestId('doing-window').textContent).toContain('window 1.50 s vs arrive 0.99 s');
  expect(screen.getByTestId('doing-window').textContent).toContain('ammoTerm 0.95');
  expect(screen.getByTestId('doing-window').textContent).toContain('scoreboardTerm 0.25');
  expect(screen.getByTestId('decides').textContent).toContain('push test=1.00');
});

it('says a bot with no decision layer has none', async () => {
  api.getAiBot.mockResolvedValue({ ...full, record: { ...full.record, doing: null } });
  render(<AiTab visible />);
  expect(await screen.findByText('no decision layer')).toBeTruthy();
});

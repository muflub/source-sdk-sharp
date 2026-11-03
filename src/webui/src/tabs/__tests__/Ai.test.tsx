/*
 * The bot inspector tab (plan_hl2mp_ai §7.2) over recorded samples: a full
 * memory (an opponent with an ammo estimate, a belief and habits) and an
 * empty one; the skills part's meters.
 */

import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';

import { AI_REFRESH_MS, AiTab } from '../Ai';
import type { AiBot } from '../../ai/aiInspect';

vi.mock('../../api', () => ({ getAiBots: vi.fn(), getAiBot: vi.fn(), getAiWorld: vi.fn(), getMapGeometry: vi.fn(), getAiNav: vi.fn() }));

const api = await import('../../api') as unknown as {
  getAiBots: ReturnType<typeof vi.fn>; getAiBot: ReturnType<typeof vi.fn>; getAiWorld: ReturnType<typeof vi.fn>; getMapGeometry: ReturnType<typeof vi.fn>;
};

// jsdom has no WebGL: the panel says so, and the tab under it works.
HTMLCanvasElement.prototype.getContext = (() => null) as unknown as HTMLCanvasElement['getContext'];

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
      grudgeTerm: 0.4, preAiming: true, habitSpot: [100, 200, 0], moment: 'spot', streak: 'hot', streakAimErrorDeg: 1.8,
      decides: [{ tick: 900, chosen: 'push', utility: 0.8, scoreboardTerm: 0.25, ammoTerm: 0.95,
        nodes: [{ id: 0, name: 'root', status: 'success', value: 0.8 }, { id: 9, name: 'push test', status: 'success', value: 1 }] }],
    },
    memory: {
      actors: [{
        slot: 1, serial: 1073741829, inSight: false, confidence: 0.5, lastSeen: [100, 200, 0], lastSeenTick: 800, lastHeard: [120, 210, 0],
        lastHeardTick: 850, lastHeardKind: 'Gunfire', healthEstimate: 80, weaponSeen: 3, rank: 1, frags: 4, deaths: 1,
        ammo: { weapon: 3, clip: 45, min: 10, max: 28, histogram: [], reloading: false, reloadEndTick: 0, pEmpty: 0.1, pReloading: 0, push: false, windowSeconds: 1.5, barWidth: 0.4 },
        belief: { argMaxCell: 12, argMax: [130, 220, 0], entropy: 2.5, cells: [12], mass: [1] }, intent: 'Approach', intentScore: 0.6, grudge: 1.5,
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
  api.getAiWorld.mockResolvedValue({ tick: 1, tickSeconds: 0.015, map: 'dm_x', navRefusal: null, costUs: 1, actors: [], bots: [] });
  api.getMapGeometry.mockResolvedValue({ error: 'none' });
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

it('lists the roster: a benched bot says why and cannot be inspected, a bot in the game shows its tag and drawn levels', async () => {
  api.getAiBots.mockResolvedValue([
    { slot: -1, serial: 0, name: 'Benny', profile: 'easy', spent: 20, tag: '(Easy)', style: 'Nerve Cover', levels: [1, 1, 2, 2, 2, 2, 2, 2, 5, 1], state: 'benched', reason: 'no free slot' },
    { slot: 2, serial: 1, name: 'Alyx', profile: 'normal', spent: 35, tag: '(Medium)', style: 'Nerve Agility', levels: [4, 3, 2, 3, 3, 3, 4, 4, 7, 2], state: 'active', reason: '' },
  ]);
  api.getAiBot.mockResolvedValue(full);
  render(<AiTab visible />);
  const alyx = await screen.findByRole('button', { name: 'Alyx (Medium)' });
  expect(alyx.getAttribute('aria-pressed')).toBe('true');
  expect(alyx.getAttribute('title')).toContain('4 3 2 3 3 3 4 4 7 2');
  const benny = screen.getByRole('button', { name: 'Benny (Easy)' }) as HTMLButtonElement;
  expect(benny.disabled).toBe(true);
  expect(benny.title).toContain('benched: no free slot');
  await screen.findByTestId('actor-1');
  expect(api.getAiBot).toHaveBeenCalledWith(2);
  expect(api.getAiBot).not.toHaveBeenCalledWith(-1);
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

it('lays each skill out as a row of three cells, so the meters share a column', async () => {
  api.getAiBot.mockResolvedValue(full);
  render(<AiTab visible />);
  const row = await screen.findByTestId('skill-accuracy');
  const cells = Array.from(row.children);
  expect(cells.map(c => c.tagName)).toEqual(['SPAN', 'METER', 'SPAN']);
  expect(cells[0]!.textContent).toBe('Accuracy');
  expect(cells[2]!.textContent).toBe('7');
});

it('marks the bot being looked at, and only it, as the pressed button', async () => {
  api.getAiBots.mockResolvedValue([
    { slot: 2, serial: 1, name: 'Alyx', profile: 'hard', spent: 50 },
    { slot: 3, serial: 1, name: 'Barney', profile: 'easy', spent: 20 },
  ]);
  api.getAiBot.mockResolvedValue(full);
  render(<AiTab visible />);
  const alyx = await screen.findByRole('button', { name: 'Alyx' });
  const barney = screen.getByRole('button', { name: 'Barney' });
  expect(alyx.getAttribute('aria-pressed')).toBe('true');
  expect(barney.getAttribute('aria-pressed')).toBe('false');
  fireEvent.click(barney);
  expect(alyx.getAttribute('aria-pressed')).toBe('false');
  expect(barney.getAttribute('aria-pressed')).toBe('true');
});

const atSkill = (level: number): AiBot => ({
  ...full,
  record: { ...full.record, skills: { ...skills, skills: [{ key: 'accuracy', name: 'Accuracy', level }] } },
});

const passTime = (ms: number) => act(async () => { await vi.advanceTimersByTimeAsync(ms); });

afterEach(() => { vi.useRealTimers(); });

it('asks for the chosen bot again every few seconds, and shows the new answer', async () => {
  vi.useFakeTimers();
  api.getAiBot.mockResolvedValue(atSkill(7));
  render(<AiTab visible />);
  await passTime(0);
  expect((screen.getByTestId('skill-accuracy').querySelector('meter') as HTMLMeterElement).value).toBe(7);

  api.getAiBot.mockResolvedValue(atSkill(2));
  await passTime(AI_REFRESH_MS + 50);
  expect((screen.getByTestId('skill-accuracy').querySelector('meter') as HTMLMeterElement).value).toBe(2);
  expect(AI_REFRESH_MS).toBeLessThanOrEqual(5000);
});

it('picks up a bot that joined after the tab first asked', async () => {
  vi.useFakeTimers();
  api.getAiBots.mockResolvedValue([]);
  api.getAiBot.mockResolvedValue(full);
  render(<AiTab visible />);
  await passTime(0);
  expect(screen.getByText('no bots')).toBeTruthy();

  api.getAiBots.mockResolvedValue([{ slot: 2, serial: 1, name: 'Alyx', profile: 'hard', spent: 50 }]);
  await passTime(AI_REFRESH_MS + 50);
  expect(screen.getByRole('button', { name: 'Alyx' }).getAttribute('aria-pressed')).toBe('true');
  expect(screen.getByTestId('skill-accuracy')).toBeTruthy();
});

it('moves to the first bot when the chosen one has left', async () => {
  vi.useFakeTimers();
  api.getAiBots.mockResolvedValue([
    { slot: 2, serial: 1, name: 'Alyx', profile: 'hard', spent: 50 },
    { slot: 3, serial: 1, name: 'Barney', profile: 'easy', spent: 20 },
  ]);
  api.getAiBot.mockResolvedValue(full);
  render(<AiTab visible />);
  await passTime(0);
  fireEvent.click(screen.getByRole('button', { name: 'Barney' }));
  await passTime(0);

  api.getAiBots.mockResolvedValue([{ slot: 2, serial: 1, name: 'Alyx', profile: 'hard', spent: 50 }]);
  await passTime(AI_REFRESH_MS + 50);
  expect(screen.queryByRole('button', { name: 'Barney' })).toBeNull();
  expect(screen.getByRole('button', { name: 'Alyx' }).getAttribute('aria-pressed')).toBe('true');
});

it('asks nothing while the tab is hidden', async () => {
  vi.useFakeTimers();
  api.getAiBot.mockResolvedValue(full);
  render(<AiTab visible={false} />);
  await passTime(AI_REFRESH_MS * 3);
  expect(api.getAiBots).not.toHaveBeenCalled();
  expect(api.getAiBot).not.toHaveBeenCalled();
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

it('shows who it holds a grudge against and the habit it is acting on', async () => {
  api.getAiBot.mockResolvedValue(full);
  render(<AiTab visible />);
  const habit = await screen.findByTestId('doing-habit');
  expect(habit.textContent).toContain('grudgeTerm 0.40');
  expect(habit.textContent).toContain('their usual place (100 200 0), looking at it');
  expect(habit.textContent).toContain('last moment: spot');
  expect(habit.textContent).toContain('on a hot streak (aim error 1.80°)');
  expect(screen.getByTestId('grudge-1').textContent).toBe('1.5');
});

it('says a bot with no decision layer has none', async () => {
  api.getAiBot.mockResolvedValue({ ...full, record: { ...full.record, doing: null } });
  render(<AiTab visible />);
  expect(await screen.findByText('no decision layer')).toBeTruthy();
});

it('shares one selection between the bot buttons and the 3D panel', async () => {
  api.getAiBot.mockResolvedValue(full);
  api.getAiBots.mockResolvedValue([{ slot: 2, serial: 1, name: 'Alyx', profile: 'hard', spent: 50 }, { slot: 3, serial: 2, name: 'Barney', profile: 'easy', spent: 30 }]);
  const bot = (slot: number, name: string) => ({ slot, name, stepping: false, truth: null, going: null, route: null, beliefs: { heard: [], remembered: [] } });
  api.getAiWorld.mockResolvedValue({ tick: 1, tickSeconds: 0.015, map: 'dm_x', navRefusal: null, costUs: 1, actors: [], bots: [bot(2, 'Alyx'), bot(3, 'Barney')] });
  render(<AiTab visible />);
  await screen.findByTestId('actor-1');
  await vi.waitFor(() => expect(screen.getByTestId('world-doing').textContent).toContain('Alyx'));
  fireEvent.click(screen.getByText('Barney'));
  await vi.waitFor(() => expect(screen.getByTestId('world-doing').textContent).toContain('Barney'));
  expect(screen.getByText('Barney').getAttribute('aria-pressed')).toBe('true');
});

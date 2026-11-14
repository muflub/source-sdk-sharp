/*
 * The Match card over a stand-in /match: the mode and its settings, the vote
 * with its counts, the next map, and asking only while the tab shows.
 */

import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';

import { MatchCard } from '../MatchCard';
import { modeName, settingValue, voteLine, type Match } from '../match';

vi.mock('../../api', () => ({ getMatch: vi.fn() }));

const api = await import('../../api') as unknown as { getMatch: ReturnType<typeof vi.fn> };

const answer = (over: Partial<Match> = {}): Match => ({
  map: 'dm_lockdown',
  mode: { id: 'tdm', title: '#SS_UI_Mode_TeamDeathmatch', teamplay: true },
  settings: [
    { key: 'mp_friendlyfire', value: '0', applies: 'Now', votable: true, adminOnly: false, pending: null },
    { key: 'mp_teams_unbalance_limit', value: '0', applies: 'NextRound', votable: true, adminOnly: false, pending: '2' },
  ],
  vote: null,
  nextMap: 'ss_ai_tdm',
  nextLevelSet: false,
  gameOver: false,
  candidates: [{ map: 'ss_ai_tdm', label: 'ss_ai_tdm', modes: [], hasNav: true }, { map: 'dm_x', label: 'dm_x [no nav]', modes: [], hasNav: false }],
  ...over,
});

beforeEach(() => { api.getMatch.mockReset(); });
afterEach(() => { vi.useRealTimers(); });

it('names the core modes as the menus do and a mod mode by its id', () => {
  expect(modeName({ id: 'null', title: 'x', teamplay: false })).toBe('Deathmatch');
  expect(modeName({ id: 'tdm', title: '#SS_UI_Mode_TeamDeathmatch', teamplay: true })).toBe('Team Deathmatch');
  expect(modeName({ id: 'ctf', title: '#TF2_Gametype_Ctf', teamplay: true })).toBe('ctf');
  expect(modeName(null)).toBe('none');
});

it('shows a setting waiting for a round beside the value it holds', () => {
  expect(settingValue(answer().settings[1]!)).toBe('0 → 2 (NextRound)');
  expect(settingValue(answer().settings[0]!)).toBe('0');
});

it('says who called a vote and each choice count', () => {
  expect(voteLine({ kind: 'NextMap', detail: '', caller: 0, phase: 'Voting', options: ['a', 'b'], counts: [1, 0], yes: 1, no: 0, voters: 2, secondsLeft: 9.6 }))
    .toBe('NextMap, called by the server: a 1, b 0 of 2 voter(s), voting, 10 s left');
  expect(voteLine({ kind: 'Mode', detail: 'tdm', caller: 3, phase: 'Passed', options: ['Yes', 'No'], counts: [1, 0], yes: 1, no: 0, voters: 1, secondsLeft: 0 }))
    .toBe('Mode tdm, called by player 3: Yes 1, No 0 of 1 voter(s), passed');
});

it('shows the mode, the settings, the vote and the next map from /match', async () => {
  api.getMatch.mockResolvedValue(answer({
    vote: { kind: 'NextMap', detail: '', caller: 0, phase: 'Voting', options: ['ss_ai_tdm', 'dm_x'], counts: [0, 1], yes: 0, no: 1, voters: 1, secondsLeft: 12 },
    nextMap: 'dm_x', nextLevelSet: true,
  }));

  render(<MatchCard visible />);

  await waitFor(() => expect(screen.getByTestId('match-mode').textContent).toBe('Team Deathmatch (teams)'));
  expect(screen.getByTestId('match-setting-mp_friendlyfire').textContent).toBe('0');
  expect(screen.getByTestId('match-vote').textContent).toContain('ss_ai_tdm 0, dm_x 1 of 1 voter(s)');
  expect(screen.getByTestId('match-next').textContent).toBe('dm_x (chosen)');
  expect(screen.getByTestId('match-candidates').textContent).toBe('ss_ai_tdm, dm_x [no nav]');
});

it('says when the game does not answer /match', async () => {
  api.getMatch.mockResolvedValue({ error: 'no such route' });

  render(<MatchCard visible />);

  await waitFor(() => expect(screen.getByText('the game does not answer /match')).toBeTruthy());
});

it('asks nothing while the tab is hidden', async () => {
  vi.useFakeTimers();
  api.getMatch.mockResolvedValue(answer());

  render(<MatchCard visible={false} />);
  await vi.advanceTimersByTimeAsync(3000);

  expect(api.getMatch).not.toHaveBeenCalled();
});

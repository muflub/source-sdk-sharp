/*
 * The match as /match answers it (plan_bots_teams_tf2 D.3): the level and its
 * mode, the mode's settings, the vote on now, the next map and the candidates.
 * Read only: the admin menu, in game and signed, is the way to change any of it.
 */

export interface MatchMode { id: string; title: string; teamplay: boolean }

export interface MatchSetting {
  key: string;
  value: string | null;
  applies: string;
  votable: boolean;
  adminOnly: boolean;
  pending: string | null;
}

export interface MatchVote {
  kind: string;
  detail: string;
  caller: number;
  phase: string;
  options: string[];
  counts: number[];
  yes: number;
  no: number;
  voters: number;
  secondsLeft: number;
}

export interface MatchCandidate { map: string; label: string; modes: string[] | null; hasNav: boolean }

export interface Match {
  map: string;
  mode: MatchMode | null;
  settings: MatchSetting[];
  vote: MatchVote | null;
  nextMap: string;
  nextLevelSet: boolean;
  gameOver: boolean;
  candidates: MatchCandidate[];
}

/** What the menus call the core's modes; a mod's by its title, or its id when that is a localisation token. */
export function modeName(mode: MatchMode | null): string {
  if (!mode) return 'none';
  if (mode.id === 'null') return 'Deathmatch';
  if (mode.id === 'tdm') return 'Team Deathmatch';
  return mode.title.startsWith('#') ? mode.id : mode.title;
}

/** A setting's value as the card shows it, with one waiting for a round or a level. */
export function settingValue(s: MatchSetting): string {
  const now = s.value ?? '(not in this game)';
  return s.pending !== null ? `${now} → ${s.pending} (${s.applies})` : now;
}

/** The vote in a line: what, who called it, and each choice's count. */
export function voteLine(v: MatchVote): string {
  const who = v.caller === 0 ? 'the server' : `player ${v.caller}`;
  const what = v.detail ? `${v.kind} ${v.detail}` : v.kind;
  const tally = v.options.map((o, i) => `${o} ${v.counts[i] ?? 0}`).join(', ');
  return `${what}, called by ${who}: ${tally} of ${v.voters} voter(s), ${v.phase.toLowerCase()}${v.phase === 'Voting' ? `, ${v.secondsLeft.toFixed(0)} s left` : ''}`;
}

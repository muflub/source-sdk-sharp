/*
 * The Match card (plan_bots_teams_tf2 D.3, D.5), on the AI tab: the mode and
 * its settings, the vote in progress with its counts, and the next map. It
 * asks /match on the tab's poll, and only while the tab shows. Read only.
 */

import { Fragment, useEffect, useState, type ReactNode } from 'react';

import { getMatch } from '../api';
import { usePoll } from '../hooks/usePoll';
import { modeName, settingValue, voteLine, type Match } from './match';

/** How often the card asks again while it shows. */
export const MATCH_POLL_MS = 1000;

export function MatchCard({ visible }: { visible: boolean }): ReactNode {
  const [match, setMatch] = useState<Match | null>(null);
  const [error, setError] = useState<string | null>(null);

  const refresh = async (): Promise<void> => {
    try {
      const answer = await getMatch();
      if (!answer || typeof answer.map !== 'string') {
        setError('the game does not answer /match');
        return;
      }
      setMatch(answer);
      setError(null);
    } catch (e) {
      setError(`/match failed (${String(e)})`);
    }
  };

  useEffect(() => {
    if (visible) void refresh();
  }, [visible]);

  usePoll(async () => {
    if (visible) await refresh();
  }, MATCH_POLL_MS);

  return (
    <section className="card match-card" data-testid="match-card">
      <h3>Match</h3>
      {error && <p className="error">{error}</p>}
      {match && (
        <>
          <p className="sub">{match.map}{match.gameOver ? ' · intermission' : ''}</p>
          <dl>
            <dt>mode</dt>
            <dd data-testid="match-mode">{modeName(match.mode)}{match.mode?.teamplay ? ' (teams)' : ''}</dd>
            {match.settings.map(s => (
              <Fragment key={s.key}>
                <dt>{s.key}</dt>
                <dd data-testid={`match-setting-${s.key}`}>{settingValue(s)}</dd>
              </Fragment>
            ))}
            <dt>vote</dt>
            <dd data-testid="match-vote">{match.vote ? voteLine(match.vote) : 'none'}</dd>
            <dt>next map</dt>
            <dd data-testid="match-next">{match.nextMap}{match.nextLevelSet ? ' (chosen)' : ' (the cycle)'}</dd>
            <dt>maps</dt>
            <dd data-testid="match-candidates">{match.candidates.map(c => c.label).join(', ') || 'none installed'}</dd>
          </dl>
        </>
      )}
    </section>
  );
}

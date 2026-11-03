//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Rules;

/// <summary>
/// <c>mp_autoteambalance 2</c>'s balancer, <c>CTFAutobalance</c>
/// (tf_autobalance.cpp:77-618): a per-frame state machine that watches for
/// unbalanced teams during a running round, then — once, when the imbalance
/// has held — picks candidates from the heaviest team by the TF player
/// resource's scores (<see cref="ITFPlayerStats"/>, the rows the resource
/// sends), moves the dead ones at once and asks the rest with a centre
/// print (<c>#TF_Autobalance_TeamChangePending</c>). Stock has no accept
/// command: the ask is a warning, and when
/// <c>tf_autobalance_force_candidates_maxtime</c> runs out the warned
/// candidates are force-moved (<c>PlayerChangeTeam</c>'s change, respawn,
/// <c>teamplay_teambalanced_player</c> and its two prints).
/// </summary>
/// <para>
/// Of <c>CanBeAutobalanced</c>'s refusals only the bot arm is ported
/// (tf_player.cpp:19827-19828): a bot is never a candidate. The duel, coach
/// and Halloween ghost/kart arms have no subjects in this build
/// (docs/plans/tf2/deferred.tsv). Stock's <c>SetLastAutobalanceTime</c> is
/// write-only there.
/// </para>
[SideService(Side.Server)]
public sealed class TFAutobalance(ITeamRoundSettings settings, ITeamRoundRoster roster, IRoundAnnouncer announcer,
                                  IGameClock clock, ILog log, ITeamRoundState? rounds = null,
                                  ITFPlayerStats? stats = null, IUserMessageSender? messages = null,
                                  ConVarRegistry? convars = null, IBotPlayers? bots = null)
    : IGameLifecycle, IFrameLifecycle
{
    /// <summary>Stock's <c>autobalance_state_t</c>.</summary>
    private enum AutobalanceState
    {
        Inactive = 0,
        Monitor,
        ForceDeadCandidates,
        ForceCandidatesSetup,
        ForceCandidatesExecution,
    }

    private sealed class Candidate
    {
        public EntityHandle Player;
        public bool SentForceMessage;
    }

    private readonly ITeamRoundSettings _settings = settings;
    private readonly ITeamRoundRoster _roster = roster;
    private readonly IRoundAnnouncer _announcer = announcer;
    private readonly IGameClock _clock = clock;
    private readonly ILog _log = log;
    private readonly ITeamRoundState? _rounds = rounds;
    private readonly ITFPlayerStats? _stats = stats;
    private readonly IUserMessageSender? _messages = messages;
    private readonly ConVarRegistry? _convars = convars;
    private readonly IBotPlayers? _bots = bots;

    private AutobalanceState _state;
    private TFTeam _lightestTeam = TFTeam.Unassigned;
    private TFTeam _heaviestTeam = TFTeam.Unassigned;
    private int _needed;
    private float _nextStateChange = -1f;
    private readonly List<Candidate> _candidates = new();

    int IGameLifecycle.LifecycleOrder => TeamRoundOrders.Autobalance;

    void IGameLifecycle.OnLevelInit(string mapName) => Reset();

    void IGameLifecycle.OnLevelShutdown() => Reset();

    private float Now => _clock.Now;

    private T? ConVar<T>() where T : ConVarBase => _convars?.Get<T>();

    private float DeadCandidatesMaxTime => ConVar<TFAutobalanceDeadCandidatesMaxTimeConVar>()?.Value ?? 15f;

    private float ForceCandidatesMaxTime => ConVar<TFAutobalanceForceCandidatesMaxTimeConVar>()?.Value ?? 5f;

    private int XpBonus => (int)(ConVar<TFAutobalanceXpBonusConVar>()?.Value ?? 500f);

    /// <summary>Stock's <c>Reset</c>: the state machine back to its floor.</summary>
    public void Reset()
    {
        _state = AutobalanceState.Inactive;
        _lightestTeam = _heaviestTeam = TFTeam.Unassigned;
        _needed = 0;
        _nextStateChange = -1f;
        _candidates.Clear();
    }

    /// <summary>Stock's <c>ShouldBeActive</c> minus the modes this build has none of.</summary>
    public bool ShouldBeActive()
    {
        if (_settings.AutoTeamBalance != 2)
            return false;

        if (_settings.TeamsUnbalanceLimit <= 0)
            return false;

        if (_rounds is null)
            return false;

        // Stock's tournament, training, item-testing, arena-queue and
        // community arms; the round settings carry the one this build has.
        return !_settings.Tournament;
    }

    /// <summary>Stock's <c>AreTeamsUnbalanced</c>: a running round, and the
    /// heaviest/lightest pair more than the limit apart. Refreshes
    /// <see cref="_needed"/> (the switches the ask will want).</summary>
    public bool AreTeamsUnbalanced()
    {
        // Don't bother switching teams if the round isn't running.
        if (_rounds is null || _rounds.State != RoundState.RoundRunning)
            return false;

        int limit = _settings.TeamsUnbalanceLimit;
        if (limit <= 0)
            return false;

        int most = 0;
        int least = int.MaxValue;
        _lightestTeam = _heaviestTeam = TFTeam.Unassigned;
        _needed = 0;

        for (var team = TFTeam.Red; team <= TFTeam.Blue; team++)
        {
            int players = _roster.Players(team).Count;

            if (players < least)
            {
                least = players;
                _lightestTeam = team;
            }

            if (players > most)
            {
                most = players;
                _heaviestTeam = team;
            }
        }

        if (most - least > limit)
        {
            _needed = (most - least) / 2;
            return true;
        }

        return false;
    }

    /// <summary>Stock's <c>GetTeamAutoBalanceScore</c>: the last heaviest-team
    /// player's score over everyone's total (the assignment is stock's), both
    /// sides of the division read off the TF player resource's score rows.</summary>
    private double TeamAutoBalanceScore(TFTeam team)
    {
        if (_stats is null)
            return 0.0;

        int teamScore = 0;
        int totalScore = 0;

        foreach (var team_ in Teams())
        {
            foreach (var player in _roster.Players(team_))
            {
                int index = _roster.EntIndex(player);

                if (team_ == team)
                    teamScore = _stats.Score(index);

                totalScore += _stats.Score(index);
            }
        }

        return (double)teamScore / (double)totalScore;
    }

    /// <summary>Stock's <c>GetPlayerAutoBalanceScore</c>: the player's score
    /// over everyone's total.</summary>
    private double PlayerAutoBalanceScore(EntityHandle player)
    {
        if (_stats is null)
            return 0.0;

        int playerScore = 0;
        int totalScore = 0;

        foreach (var team in Teams())
        {
            foreach (var other in _roster.Players(team))
            {
                int index = _roster.EntIndex(other);

                if (other == player)
                    playerScore = _stats.Score(index);

                totalScore += _stats.Score(index);
            }
        }

        return (double)playerScore / (double)totalScore;
    }

    private static IEnumerable<TFTeam> Teams()
    {
        yield return TFTeam.Unassigned;
        yield return TFTeam.Spectator;
        yield return TFTeam.Red;
        yield return TFTeam.Blue;
    }

    private bool StillOnAPlayingTeam(EntityHandle player)
        => _roster.Players(TFTeam.Red).Contains(player) || _roster.Players(TFTeam.Blue).Contains(player);

    /// <summary>Stock's <c>ValidateCandidates</c>: drop the candidates that
    /// are gone (stock asks <c>CanBeAutobalanced</c>, whose refusals this
    /// build has no arms of — a candidate who left or moved is off both
    /// rosters).</summary>
    private bool ValidateCandidates()
    {
        _candidates.RemoveAll(c => !StillOnAPlayingTeam(c.Player));
        return _candidates.Count > 0;
    }

    /// <summary>Stock's <c>FindNextCandidate</c>: a heaviest-team player not
    /// already a candidate, and never a bot (<c>CanBeAutobalanced</c>'s bot
    /// arm, tf_player.cpp:19827-19828); among more than one, the one whose
    /// resource score sits closest to the average the switch asks for.</summary>
    private EntityHandle? FindNextCandidate()
    {
        List<EntityHandle> candidates = new();
        foreach (var player in _roster.Players(_heaviestTeam))
        {
            if (_bots?.IsBot(player) == true)
                continue;

            if (!_candidates.Any(c => c.Player == player) && StillOnAPlayingTeam(player))
                candidates.Add(player);
        }

        if (candidates.Count == 1)
            return candidates[0];

        if (candidates.Count > 1)
        {
            double totalDiff = Math.Abs(TeamAutoBalanceScore(_heaviestTeam) - TeamAutoBalanceScore(_lightestTeam));
            double averageNeeded = (totalDiff / 2.0) / _needed;

            double closest = double.MaxValue;
            EntityHandle? best = null;
            foreach (var candidate in candidates)
            {
                double diff = Math.Abs(averageNeeded - PlayerAutoBalanceScore(candidate));
                if (diff < closest)
                {
                    closest = diff;
                    best = candidate;
                }
            }

            return best;
        }

        return null;
    }

    /// <summary>Stock's <c>FindCandidates</c>: up to twice the switches
    /// wanted (at least 40% of the team), best found first (stock's
    /// <c>AddToHead</c> puts each new best at the head, so the tail is the
    /// best).</summary>
    private bool FindCandidates()
    {
        if (!AreTeamsUnbalanced())
        {
            Reset();
            return false;
        }

        _candidates.Clear();

        int minToFind = (int)(_roster.Players(_heaviestTeam).Count * 0.4f);
        int total = Math.Max(_needed * 2, minToFind);
        int found = 0;

        while (found < total)
        {
            var player = FindNextCandidate();
            if (player is { } candidate)
            {
                // The best candidates are towards the tail of the list so
                // the back walks can reach them first.
                _candidates.Insert(0, new Candidate { Player = candidate, SentForceMessage = false });
                found++;
                continue;
            }

            break;
        }

        if (found <= 0)
        {
            Reset();
            return false;
        }

        return true;
    }

    /// <summary>Stock's <c>PlayerChangeTeam</c>: the forced move, the respawn,
    /// the <c>teamplay_teambalanced_player</c> event and the two prints (the
    /// chat line to everyone, the moved player's own done line with the team
    /// name and the XP bonus the ask promised).</summary>
    private void PlayerChangeTeam(EntityHandle player)
    {
        if (!_roster.ChangeTeam(player, _lightestTeam))
            return;

        _roster.ForceRespawn(player);

        string name = _roster.Name(player);
        _announcer.TeamBalanced(_roster.EntIndex(player), _lightestTeam, name);
        _log.Msg($"[TF2/Teams] {name} ({player}) was moved to {_lightestTeam} to balance the teams\n");

        if (_messages is not null)
        {
            string teamName = TFMessages.BalanceTeamName(_lightestTeam);
            _messages.Send(new TextMsg
            {
                Destination = TFMessages.HudPrintTalk,
                Text = TFMessages.AutoBalanceTeamChangeDone,
                Params = [teamName, XpBonus.ToString()],
            }, player: _roster.EntIndex(player), reliable: true);
        }
    }

    /// <summary>Stock's <c>ForceDeadCandidates</c>: a dead candidate is moved
    /// at once, until the teams balance.</summary>
    private void ForceDeadCandidates()
    {
        if (!AreTeamsUnbalanced() || !ValidateCandidates())
        {
            Reset();
            return;
        }

        for (int i = _candidates.Count - 1; i >= 0; i--)
        {
            var player = _candidates[i].Player;
            if (!_roster.IsAlive(player))
            {
                PlayerChangeTeam(player);
                _candidates.RemoveAt(i);

                if (!AreTeamsUnbalanced())
                {
                    Reset();
                    break;
                }
            }
        }
    }

    /// <summary>Stock's <c>ForceCandidatesSetup</c>: tell the first
    /// <see cref="_needed"/> (the best, from the tail) how long they have.</summary>
    private void ForceCandidatesSetup()
    {
        if (!AreTeamsUnbalanced() || !ValidateCandidates())
        {
            Reset();
            return;
        }

        string teamName = TFMessages.BalanceTeamName(_lightestTeam);
        int told = 0;
        for (int i = _candidates.Count - 1; i >= 0; i--)
        {
            if (told >= _needed)
                break;

            var candidate = _candidates[i];
            candidate.SentForceMessage = true;
            told++;

            // Stock re-sends every frame of the window, so the centre print
            // stays up; a candidate who moves themselves first stops being
            // needed when the teams balance.
            if (_messages is not null)
            {
                _messages.Send(new TextMsg
                {
                    Destination = TFMessages.HudPrintCenter,
                    Text = TFMessages.AutoBalanceTeamChangePending,
                    Params = [teamName, ((int)ForceCandidatesMaxTime).ToString()],
                }, player: _roster.EntIndex(candidate.Player), reliable: true);
            }
        }
    }

    /// <summary>Stock's <c>ForceCandidatesExecution</c>: the warned ones move.</summary>
    private void ForceCandidatesExecution()
    {
        if (!AreTeamsUnbalanced() || !ValidateCandidates())
        {
            Reset();
            return;
        }

        int switched = 0;
        for (int i = _candidates.Count - 1; i >= 0; i--)
        {
            if (!_candidates[i].SentForceMessage)
                continue;

            if (switched >= _needed)
                break;

            PlayerChangeTeam(_candidates[i].Player);
            switched++;
        }
    }

    /// <summary>Stock's <c>FrameUpdatePostEntityThink</c>.</summary>
    void IFrameLifecycle.OnThink(float frameTime)
    {
        if (!ShouldBeActive())
        {
            Reset();
            return;
        }

        switch (_state)
        {
            case AutobalanceState.Inactive:
                _state = AutobalanceState.Monitor;
                _nextStateChange = -1f;
                break;

            case AutobalanceState.Monitor:
                if (_nextStateChange > 0f && _nextStateChange < Now)
                {
                    if (FindCandidates())
                    {
                        _state = AutobalanceState.ForceDeadCandidates;
                        _nextStateChange = -1f;
                    }
                }
                else if (AreTeamsUnbalanced())
                {
                    if (_nextStateChange < 0f)
                        _nextStateChange = Now;
                }
                else
                {
                    _nextStateChange = -1f;
                }

                break;

            case AutobalanceState.ForceDeadCandidates:
                if (_nextStateChange > 0f && _nextStateChange < Now)
                {
                    _state = AutobalanceState.ForceCandidatesSetup;
                    _nextStateChange = -1f;
                }
                else
                {
                    if (_nextStateChange < 0f)
                        _nextStateChange = Now + DeadCandidatesMaxTime;

                    ForceDeadCandidates();
                }

                break;

            case AutobalanceState.ForceCandidatesSetup:
                if (_nextStateChange > 0f && _nextStateChange < Now)
                {
                    _state = AutobalanceState.ForceCandidatesExecution;
                    _nextStateChange = -1f;
                }
                else
                {
                    if (_nextStateChange < 0f)
                    {
                        _nextStateChange = Now + ForceCandidatesMaxTime;
                        ForceCandidatesSetup();
                    }
                }

                break;

            case AutobalanceState.ForceCandidatesExecution:
                ForceCandidatesExecution();
                Reset();
                break;
        }
    }
}

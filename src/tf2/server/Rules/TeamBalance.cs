//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Econ;
using TF2.Shared;

namespace TF2.Server.Rules;

/// <summary>
/// The team balance rules of <c>CTeamplayRoundBasedRules</c>:
/// <c>ShouldBalanceTeams</c>, <c>WouldChangeUnbalanceTeams</c> (the join
/// refusal), <c>AreTeamsUnbalanced</c> and <c>BalanceTeams</c> (the forced
/// switch, <c>mp_autoteambalance 1</c>), with <c>CBaseMultiplayerPlayer::CalculateTeamBalanceScore</c>'s
/// pick order.
///
/// <para>
/// <c>mp_autoteambalance 2</c> is TF2's newer <c>CTFAutobalance</c> (ask
/// volunteers, then force), which reads the TF player resource's scores:
/// <c>CTFGameRules::BalanceTeams</c> returns at 2, and so does this; that
/// path is <see cref="TFAutobalance"/>'s. Bots and coaches
/// (<c>CTFPlayer::CanBeAutobalanced</c>) are not told apart.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TeamBalance(ITeamRoundSettings settings, ITeamRoundRoster roster, IRoundAnnouncer announcer,
                                IRoundTimers timers, IGameClock clock, ILog log,
                                ITFPlayerClasses? classes = null) : IGameLifecycle
{
    private readonly ITFPlayerClasses? _classes = classes;
    // Who was first seen when (the connection time CalculateTeamBalanceScore
    // reads; a divergence row: the first spawn stands for the connection),
    // and who was last switched by force.
    private readonly Dictionary<uint, float> _firstSeen = new();
    private readonly Dictionary<uint, float> _lastForcedSwitch = new();

    private float _foundUnbalancedTeamsTime = -1f;
    private bool _printedUnbalanceWarning;
    private float _autoBalanceQueueTimeEnd = -1f;
    private EntityHandle _autoBalanceQueuePlayer = EntityHandle.None;
    private int _autoBalanceQueuePlayerScore = -1;

    /// <summary>Stock <c>m_flStartBalancingTeamsAt</c>: no balancing before it (a minute into each PREROUND).</summary>
    public float StartBalancingAt { get; set; }

    int IGameLifecycle.LifecycleOrder => TeamRoundOrders.TeamBalance;

    void IGameLifecycle.OnLevelInit(string mapName)
    {
        _firstSeen.Clear();
        _lastForcedSwitch.Clear();
        Reset();
    }

    /// <summary>The round machine's reset: the balance timers, not who has been seen.</summary>
    public void Reset()
    {
        StartBalancingAt = 0f;
        _foundUnbalancedTeamsTime = -1f;
        _printedUnbalanceWarning = false;
        _autoBalanceQueueTimeEnd = -1f;
        _autoBalanceQueuePlayer = EntityHandle.None;
        _autoBalanceQueuePlayerScore = -1;
    }

    /// <summary>A player spawned: the first time is their "connection".</summary>
    public void NoteSeen(EntityHandle player, float now) => _firstSeen.TryAdd(player.Value, now);

    private float Now => clock.Now;

    /// <summary>Stock <c>ShouldBalanceTeams</c>.</summary>
    public bool ShouldBalanceTeams()
    {
        if (settings.Tournament)
            return false;

        if (settings.TeamsUnbalanceLimit <= 0)
            return false;

        return true;
    }

    /// <summary>Stock <c>WouldChangeUnbalanceTeams</c>: whether moving to <paramref name="newTeam"/> would unbalance.</summary>
    public bool WouldChangeUnbalanceTeams(TFTeam newTeam, TFTeam currentTeam)
    {
        // Players are allowed to change to their own team.
        if (newTeam == currentTeam)
            return false;

        // If mp_teams_unbalance_limit is 0, don't check.
        if (!ShouldBalanceTeams())
            return false;

        // If they are joining a non-playing team, allow.
        if (newTeam < TFTeam.Red)
            return false;

        // Add one because we're joining this team.
        int newTeamPlayers = roster.Players(newTeam).Count + 1;

        for (var team = TFTeam.Red; team <= TFTeam.Blue; team++)
        {
            if (team == newTeam)
                continue;

            int players = roster.Players(team).Count;
            if (team == currentTeam)
                players = Math.Max(0, players - 1);

            if (newTeamPlayers - players > settings.TeamsUnbalanceLimit)
                return true;
        }

        return false;
    }

    /// <summary>Stock <c>AreTeamsUnbalanced</c>.</summary>
    public bool AreTeamsUnbalanced(out TFTeam heaviest, out TFTeam lightest)
    {
        heaviest = TFTeam.Unassigned;
        lightest = TFTeam.Unassigned;

        if (!ShouldBalanceTeams())
            return false;

        int most = 0;
        int least = int.MaxValue;

        for (var team = TFTeam.Red; team <= TFTeam.Blue; team++)
        {
            int players = roster.Players(team).Count;

            if (players < least)
            {
                least = players;
                lightest = team;
            }

            if (players > most)
            {
                most = players;
                heaviest = team;
            }
        }

        return most - least > settings.TeamsUnbalanceLimit;
    }

    /// <summary>
    /// Stock <c>CTFPlayer::CalculateTeamBalanceScore</c> (tf_player.cpp:20244-20255):
    /// the base score (<c>CBaseMultiplayerPlayer</c>'s: minus the seconds on
    /// the server, and way down for a player switched by force in the last
    /// five minutes), then -120 for an engineer ("switch engineers less
    /// often"). Higher is more likely to be switched.
    /// </summary>
    public int BalanceScore(EntityHandle player)
    {
        float connected = _firstSeen.TryGetValue(player.Value, out float at) ? Now - at : 0f;
        int score = 0 - (int)connected;

        if (_lastForcedSwitch.TryGetValue(player.Value, out float switched) && switched > 0f && Now - switched < 300f)
            score -= 10000;

        if (_classes?.ClassOf(player) == TFClass.Engineer)
            score -= 120;

        return score;
    }

    /// <summary>
    /// Stock <c>BalanceTeams</c>: once the teams have been unbalanced for a
    /// second, warn; then move the best candidates (the highest balance
    /// score) from the heaviest team to the lightest. During a round
    /// (<paramref name="requireDead"/>) only the dead move, a recently
    /// switched player is queued three seconds in the hope of a better one,
    /// and nothing moves with under a minute on the HUD timer.
    /// </summary>
    public void BalanceTeams(bool requireDead)
    {
        // CTFGameRules::BalanceTeams: 2 is CTFAutobalance's.
        if (settings.AutoTeamBalance == 0 || settings.AutoTeamBalance == 2)
            return;

        // We don't balance for a period of time at the start of the game.
        if (Now < StartBalancingAt)
            return;

        // A round running switch, not a between rounds insta-switch: not with
        // less than 60 seconds on the active timer.
        if (requireDead && timers.Active is { } active && active.TimeRemaining < 60f)
            return;

        if (!AreTeamsUnbalanced(out var heaviest, out var lightest))
        {
            _foundUnbalancedTeamsTime = -1f;
            _printedUnbalanceWarning = false;
            return;
        }

        if (_foundUnbalancedTeamsTime < 0f)
            _foundUnbalancedTeamsTime = Now;

        // If teams have been unbalanced for X seconds, play a warning.
        if (!_printedUnbalanceWarning && Now - _foundUnbalancedTeamsTime > 1.0f)
        {
            announcer.BalanceWarning(5);
            _printedUnbalanceWarning = true;
        }

        var heavy = roster.Players(heaviest);
        int switchesRequired = (heavy.Count - roster.Players(lightest).Count) / 2;

        // Sort the eligible players and switch the n best candidates: the
        // highest score first (SwitchPlayersSort), stable for equal scores.
        var candidates = heavy.Select(p => (Player: p, Score: BalanceScore(p)))
                              .OrderByDescending(c => c.Score)
                              .ToList();

        int eligible = switchesRequired + 2;

        for (int i = 0; i < candidates.Count && switchesRequired > 0 && i < eligible; i++)
        {
            var player = candidates[i].Player;

            if (requireDead && roster.IsAlive(player))
                continue;

            // Avoid picking a player who was recently auto-balanced by
            // delaying their selection, hoping a better candidate comes along.
            if (requireDead)
            {
                int score = BalanceScore(player);

                if (_autoBalanceQueuePlayer != EntityHandle.None)
                {
                    // Is this player's score worse?
                    if (score < _autoBalanceQueuePlayerScore)
                    {
                        _autoBalanceQueuePlayer = player;
                        _autoBalanceQueuePlayerScore = score;
                    }
                }
                else if (score < -10000)
                {
                    // Switched recently: put them in the queue.
                    _autoBalanceQueuePlayer = player;
                    _autoBalanceQueuePlayerScore = score;
                    _autoBalanceQueueTimeEnd = Now + 3.0f;
                    continue;
                }

                // The queued player passes until their timer is up.
                if (_autoBalanceQueuePlayer == player && _autoBalanceQueueTimeEnd > Now)
                    continue;
            }

            if (!roster.ChangeTeam(player, lightest))
                continue;

            _lastForcedSwitch[player.Value] = Now;

            _autoBalanceQueuePlayerScore = -1;
            _autoBalanceQueuePlayer = EntityHandle.None;

            string name = roster.Name(player);
            announcer.TeamBalanced(roster.EntIndex(player), lightest, name);
            log.Msg($"[TF2/Teams] {name} ({player}) was moved to {lightest} to balance the teams\n");

            switchesRequired--;
        }
    }
}

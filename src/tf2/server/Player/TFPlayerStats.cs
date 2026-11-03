//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Player;

/// <summary>
/// FOR THE LANES THAT SCORE (2.7's kills, damage and assists; 2.9's healing;
/// 2.5/2.6's captures and defenses; 2.10's buildings): a player's stats,
/// stock's <c>CTFGameStats</c> <c>PlayerStats_t</c> — the round's
/// (<c>statsCurrentRound</c>) and the accumulated (<c>statsAccumulated</c>) —
/// which the player resource sends and the score is made of
/// (<c>CTFGameRules::CalcPlayerScore</c>). A lane counts an event with
/// <see cref="Increment"/>; the round's half resets at each round's respawn
/// (<c>CTFGameRules::RoundRespawn</c> → <c>ResetRoundStats</c>), the whole
/// when the player leaves or the level ends.
/// </summary>
public interface ITFPlayerStats
{
    /// <summary>Adds <paramref name="amount"/> to the player's round and accumulated <paramref name="stat"/>.</summary>
    void Increment(EntityHandle player, TFStat stat, int amount = 1);

    /// <summary>The round's value.</summary>
    int Round(int playerIndex, TFStat stat);

    /// <summary>The accumulated value (this level, since the player joined).</summary>
    int Accumulated(int playerIndex, TFStat stat);

    /// <summary><c>CalcPlayerScore</c> over the round's stats — the win panel's points.</summary>
    int RoundScore(int playerIndex);

    /// <summary><c>CalcPlayerScore</c> over the accumulated stats — the win panel's tie-break.</summary>
    int Score(int playerIndex);

    /// <summary>
    /// The player's stats, round and accumulated, back to empty —
    /// <c>CTFPlayer::ResetScores</c>' clearing of
    /// <c>CTF_GameStats.ResetPlayerStats</c> (tf_player.cpp:3284-3292), what
    /// the player arm of <c>ResetScores</c> spends and
    /// <c>mp_scrambleteams</c> deliberately does not
    /// (teamplayroundbased_gamerules.cpp:307), because the scramble's sort and
    /// swap pass read the accumulated score
    /// (<c>CTFPlayerResource::GetTotalScore</c>).
    /// </summary>
    void ResetPlayer(EntityHandle player);
}

/// <summary>The server's <see cref="ITFPlayerStats"/>, and <c>CalcPlayerScore</c> over them.</summary>
[SideService(Side.Server, As = [typeof(ITFPlayerStats)])]
public sealed class TFPlayerStats : ITFPlayerStats, ITeamRoundListener, IOrderedHook, IGameLifecycle, IClientLifecycle
{
    private const int Count = (int)TFStat.Total;

    // TF_SCORE_* (tf_shareddefs.h:1450-1467), the non-MvM, non-powerup arms.
    public const int ScoreKill = 1;
    public const int ScoreKillRunecarrier = 1;
    public const int ScoreCapture = 2;
    public const int ScoreFlagReturn = 4;
    public const int ScoreDefend = 1;
    public const int ScoreDestroyBuilding = 1;
    public const int ScoreHeadshotDivisor = 2;
    public const int ScoreBackstab = 1;
    public const int ScoreHealHealthUnitsPerPoint = 600;
    public const int ScoreKillAssistsPerPoint = 2;
    public const int ScoreTeleportsPerPoint = 2;
    public const int ScoreInvuln = 1;
    public const int ScoreRevenge = 1;
    public const int ScoreBonusPointDivisor = 10;
    public const int ScoreCurrencyCollected = 20;

    private readonly Dictionary<int, (int[] Round, int[] Accumulated)> _byPlayer = new();

    public int HookOrder => TFPlayerOrders.Stats;

    public int LifecycleOrder => TFPlayerOrders.Stats;

    public int ClientLifecycleOrder => TFPlayerOrders.Stats;

    public void Increment(EntityHandle player, TFStat stat, int amount = 1)
    {
        if (stat is <= TFStat.Undefined or >= TFStat.Total)
            return;

        var (round, accumulated) = Of(player.EntryIndex);
        round[(int)stat] += amount;
        accumulated[(int)stat] += amount;
    }

    public int Round(int playerIndex, TFStat stat) => Read(playerIndex, stat, accumulated: false);

    public int Accumulated(int playerIndex, TFStat stat) => Read(playerIndex, stat, accumulated: true);

    /// <summary>
    /// <c>CTFGameRules::CalcPlayerScore</c> (tf_gamerules.cpp:16990-17022) over
    /// the accumulated stats: a bogus healing value (below 0 or above
    /// 10,000,000) counts as none, as stock's defensive fix has it.
    /// </summary>
    public int Score(int playerIndex) => CalcPlayerScore(s => Accumulated(playerIndex, s));

    /// <summary><c>CalcPlayerScore</c> over this round's stats only.</summary>
    public int RoundScore(int playerIndex) => CalcPlayerScore(s => Round(playerIndex, s));

    private static int CalcPlayerScore(Func<TFStat, int> stat)
    {
        int healing = stat(TFStat.Healing);
        if (healing is < 0 or > 10_000_000)
            healing = 0;

        return stat(TFStat.Kills) * ScoreKill
               + stat(TFStat.KillsRunecarrier) * ScoreKillRunecarrier
               + stat(TFStat.Captures) * ScoreCapture
               + stat(TFStat.Flagreturns) * ScoreFlagReturn
               + stat(TFStat.Defenses) * ScoreDefend
               + stat(TFStat.Buildingsdestroyed) * ScoreDestroyBuilding
               + stat(TFStat.Headshots) / ScoreHeadshotDivisor
               + stat(TFStat.Backstabs) * ScoreBackstab
               + healing / ScoreHealHealthUnitsPerPoint
               + stat(TFStat.Killassists) / ScoreKillAssistsPerPoint
               + stat(TFStat.Teleports) / ScoreTeleportsPerPoint
               + stat(TFStat.Invulns) / ScoreInvuln
               + stat(TFStat.Revenge) / ScoreRevenge
               + stat(TFStat.BonusPoints) / ScoreBonusPointDivisor
               + stat(TFStat.CurrencyCollected) / ScoreCurrencyCollected;
    }

    /// <summary>Every player's round stats to zero (<c>CTFGameStats::ResetRoundStats</c>).</summary>
    public void ResetRoundStats()
    {
        foreach (var (round, _) in _byPlayer.Values)
            Array.Clear(round);
    }

    /// <summary>
    /// One player's row goes, round and accumulated
    /// (<c>CTFGameStats::ResetPlayerStats</c>, via
    /// <c>CTFPlayer::ResetScores</c>, tf_player.cpp:3284-3292).
    /// </summary>
    public void ResetPlayer(EntityHandle player) => _byPlayer.Remove(player.EntryIndex);

    // RoundRespawn runs as the machine enters PREROUND.
    void ITeamRoundListener.OnStateChanged(RoundState from, RoundState to)
    {
        if (to == RoundState.Preround)
            ResetRoundStats();
    }

    void ITeamRoundListener.OnRoundWon(TFTeam team, WinReason reason, float bonusSeconds) { }

    void IGameLifecycle.OnLevelShutdown() => _byPlayer.Clear();

    void IClientLifecycle.OnClientDisconnected(int entIndex) => _byPlayer.Remove(entIndex);

    private (int[] Round, int[] Accumulated) Of(int playerIndex)
    {
        if (!_byPlayer.TryGetValue(playerIndex, out var stats))
            _byPlayer[playerIndex] = stats = (new int[Count], new int[Count]);
        return stats;
    }

    private int Read(int playerIndex, TFStat stat, bool accumulated)
    {
        if (stat is <= TFStat.Undefined or >= TFStat.Total || !_byPlayer.TryGetValue(playerIndex, out var stats))
            return 0;
        return (accumulated ? stats.Accumulated : stats.Round)[(int)stat];
    }
}

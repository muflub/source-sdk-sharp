//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Player;
using TF2.Shared.Rules;

namespace TF2.Server.Combat;

/// <summary>
/// Stock's domination and revenge state
/// (<c>CTFGameRules::CalcDominationAndRevenge</c>, tf_gamerules.cpp:11961-12049,
/// and <c>CTFGameStats::TrackKillStats</c>, tf_gamestats.cpp:1856-1871): the
/// per-pair unanswered-kill counts, who dominates whom, and each player's
/// count of dominations (<c>GetNumberofDominations</c>, the resource's
/// <c>m_iActiveDominations</c>). The death notice asks for a death's flags
/// (<see cref="DeathFlags"/>) — stock computes them
/// (<c>SetDeathFlags</c>, tf_gamerules.cpp:11500-11518) before the kill is
/// recorded, and <see cref="DeathFlags"/> does both in that order.
///
/// <para>
/// Stock holds the state on the players' shared data and clears it on round
/// restart (<c>RemoveNemesisRelationships</c> from <c>ResetScores</c>) and on
/// a player leaving (<c>ResetPlayerStats</c>). Those are the
/// <see cref="ITeamRoundListener"/> and <see cref="IClientLifecycle"/> arms
/// here. Not carried: the nemesis clear on a team change, the
/// <c>player_domination</c>/<c>player_revenge</c> events and the
/// training/powerup/competitive/PvE guards.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFDominations : ITeamRoundListener, IClientLifecycle
{
    /// <summary><c>TF_KILLS_DOMINATION</c> (tf_shareddefs.h:929).</summary>
    public const int KillsToDominate = 4;

    /// <summary><c>TF_DEATH_DOMINATION</c> (tf_shareddefs.h:931).</summary>
    public const int Domination = 0x0001;
    /// <summary><c>TF_DEATH_ASSISTER_DOMINATION</c> (tf_shareddefs.h:932).</summary>
    public const int AssisterDomination = 0x0002;
    /// <summary><c>TF_DEATH_REVENGE</c> (tf_shareddefs.h:933).</summary>
    public const int Revenge = 0x0004;
    /// <summary><c>TF_DEATH_ASSISTER_REVENGE</c> (tf_shareddefs.h:934).</summary>
    public const int AssisterRevenge = 0x0008;

    private const int Slots = TFPlayerResourceNetState.Slots;

    private readonly TFPlayerCarriers? _carriers;

    public TFDominations(TFPlayerCarriers? carriers = null) => _carriers = carriers;

    // [victim, attacker]: how many unanswered deaths the victim has been dealt by the attacker.
    private readonly int[,] _unanswered = new int[Slots, Slots];
    private readonly bool[,] _dominates = new bool[Slots, Slots];
    private readonly int[] _dominations = new int[Slots];

    /// <summary>
    /// A death's flags for the scorer (-1 when none) and the assister (-1 when
    /// none), then the kill recorded — stock's Calc-and-SetDeathFlags order
    /// before <c>TrackKillStats</c>.
    /// </summary>
    public int DeathFlags(int victimIndex, int scorerIndex, int assisterIndex)
    {
        int flags = 0;
        if (IsValidKiller(victimIndex, scorerIndex))
            flags |= Calc(victimIndex, scorerIndex, assist: false);
        if (IsValidKiller(victimIndex, assisterIndex))
            flags |= Calc(victimIndex, assisterIndex, assist: true);

        Record(scorerIndex, victimIndex);
        Record(assisterIndex, victimIndex);
        return flags;
    }

    /// <summary>Does <paramref name="dominatorIndex"/> dominate <paramref name="dominatedIndex"/>? (<c>IsPlayerDominated</c>.)</summary>
    public bool Dominates(int dominatorIndex, int dominatedIndex) =>
        InRange(dominatorIndex) && InRange(dominatedIndex) && _dominates[dominatorIndex, dominatedIndex];

    /// <summary>The player's live dominations (<c>GetNumberofDominations</c>).</summary>
    public int CountOf(int playerIndex) => InRange(playerIndex) ? _dominations[playerIndex] : 0;

    private int Calc(int victimIndex, int attackerIndex, bool assist)
    {
        // tf_gamerules.cpp:11993-11995: the count plus the current kill.
        if (_unanswered[victimIndex, attackerIndex] + 1 == KillsToDominate)
        {
            _dominates[attackerIndex, victimIndex] = true;   // :12000
            _dominations[attackerIndex]++;                   // :12002-12003
            Mark(attackerIndex, victimIndex, true);          // :12000
            return assist ? AssisterDomination : Domination; // :11998
        }

        if (_dominates[victimIndex, attackerIndex])          // :12022 the dying player dominated their killer
        {
            _dominates[victimIndex, attackerIndex] = false;  // :12027
            _dominations[victimIndex] = Math.Max(0, _dominations[victimIndex] - 1);  // :12029-12034
            Mark(victimIndex, attackerIndex, false);         // :12027
            return assist ? AssisterRevenge : Revenge;       // :12025
        }

        return 0;                                            // :12042-12048 already dominating: no flag
    }

    /// <summary>
    /// Stock's <c>CTFPlayerShared::SetPlayerDominated</c>
    /// (tf_player_shared.cpp:7701-7706): the dominator's
    /// <c>m_bPlayerDominated</c> row and the dominated player's
    /// <c>m_bPlayerDominatingMe</c> row, on their carriers.
    /// </summary>
    private void Mark(int dominatorIndex, int dominatedIndex, bool dominates)
    {
        if (_carriers?.Get(dominatorIndex) is { } dominator)
            dominator.PlayerDominated[dominatedIndex] = dominates;
        if (_carriers?.Get(dominatedIndex) is { } dominated)
            dominated.PlayerDominatingMe[dominatorIndex] = dominates;
    }

    /// <summary><c>TrackKillStats</c> (tf_gamestats.cpp:1867-1870): the death counts, the answer clears.</summary>
    private void Record(int attackerIndex, int victimIndex)
    {
        if (!IsValidKiller(victimIndex, attackerIndex))
            return;

        _unanswered[victimIndex, attackerIndex]++;
        _unanswered[attackerIndex, victimIndex] = 0;
    }

    /// <summary><c>IsIndexIntoPlayerArrayValid</c> and a killer that is not the victim.</summary>
    private static bool IsValidKiller(int victimIndex, int attackerIndex) =>
        attackerIndex != victimIndex && InRange(attackerIndex);

    private static bool InRange(int index) => index >= 1 && index < Slots;

    void ITeamRoundListener.OnStateChanged(RoundState from, RoundState to)
    {
        // ResetScores → RemoveNemesisRelationships (tf_player.cpp:3950-4009) on the round's respawn.
        if (to is RoundState.Preround or RoundState.Pregame)
            Clear();
    }

    void ITeamRoundListener.OnRoundWon(TFTeam team, WinReason reason, float bonusSeconds) { }

    void IClientLifecycle.OnClientDisconnected(int entIndex)
    {
        if (!InRange(entIndex))
            return;

        for (int other = 1; other < Slots; other++)
        {
            _unanswered[entIndex, other] = 0;
            _unanswered[other, entIndex] = 0;
            // RemoveNemesisRelationships (tf_player.cpp:3979-3992): the pair's
            // bits clear and the dominator's count falls by what it held.
            if (_dominates[other, entIndex])
                _dominations[other] = Math.Max(0, _dominations[other] - 1);
            _dominates[entIndex, other] = false;
            _dominates[other, entIndex] = false;
            Mark(entIndex, other, false);
            Mark(other, entIndex, false);
        }
        _dominations[entIndex] = 0;
    }

    private void Clear()
    {
        Array.Clear(_unanswered);
        Array.Clear(_dominates);
        Array.Clear(_dominations);

        if (_carriers is null)
            return;
        for (int i = 1; i < Slots; i++)
        {
            if (_carriers.Get(i) is not { } state)
                continue;
            state.PlayerDominated.Fill(false);
            state.PlayerDominatingMe.Fill(false);
        }
    }
}

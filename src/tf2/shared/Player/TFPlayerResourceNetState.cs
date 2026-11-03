//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared.Player;

/// <summary>
/// <c>tf_player_manager</c>: the TF scoreboard columns beside the core's
/// <c>player_manager</c> (which keeps score, deaths, health, ping, team),
/// as stock's <c>CTFPlayerResource</c> sends them (tf_player_resource.cpp:23-50,
/// <c>DT_TFPlayerResource</c>), one row per player slot indexed by entity
/// index. The rows carried are the ones a non-MvM, non-matchmaking game
/// fills: the total score, the max health and the max health for buffing,
/// the class, the active dominations, the next respawn time, the medigun
/// charge (tournament mode only), and the round's damage, damage assist,
/// healing, healing assist, damage blocked and bonus points. Stock's boss
/// damage, currency, player level, streaks, MvM credits, party leaders,
/// event status, class-when-killed, connection state and connect time are
/// not (docs/plans/tf2/deferred.tsv).
/// </summary>
[StandaloneNetState(TFEntityNames.PlayerResource)]
public partial class TFPlayerResourceNetState
{
    /// <summary>Rows: the core's <c>PlayerResourceNetState.Slots</c> (MAX_PLAYERS_ARRAY_SAFE).</summary>
    public const int Slots = 34;

    /// <summary><c>m_iTotalScore</c>: <c>CTFGameRules::CalcPlayerScore</c> over the accumulated stats.</summary>
    [NetArray(Slots)] public partial NetArray<int> TotalScore { get; }

    /// <summary><c>m_iTeam</c> (DT_PlayerResource, player_resource.cpp): the player's team, so a HUD names the local one's reads.</summary>
    [NetArray(Slots)] public partial NetArray<byte> Team { get; }

    /// <summary><c>m_iMaxHealth</c>: <c>GetMaxHealth</c>.</summary>
    [NetArray(Slots)] public partial NetArray<short> MaxHealth { get; }

    /// <summary><c>m_iMaxBuffedHealth</c>: <c>GetMaxHealthForBuffing</c> (stock's name is its own).</summary>
    [NetArray(Slots)] public partial NetArray<short> MaxBuffedHealth { get; }

    /// <summary><c>m_iPlayerClass</c>: a <see cref="TFClass"/>.</summary>
    [NetArray(Slots)] public partial NetArray<byte> PlayerClass { get; }

    /// <summary><c>m_iActiveDominations</c>.</summary>
    [NetArray(Slots)] public partial NetArray<byte> ActiveDominations { get; }

    /// <summary><c>m_flNextRespawnTime</c>: 0 while alive, else the next wave that lets the player out.</summary>
    [NetArray(Slots)] public partial NetArray<float> NextRespawnTime { get; }

    /// <summary><c>m_iChargeLevel</c>: the medigun charge in percent, in tournament mode; 0 otherwise.</summary>
    [NetArray(Slots)] public partial NetArray<byte> ChargeLevel { get; }

    /// <summary><c>m_iDamage</c>: the round's <c>TFSTAT_DAMAGE</c>, sent once a second.</summary>
    [NetArray(Slots)] public partial NetArray<int> Damage { get; }

    /// <summary><c>m_iDamageAssist</c>: the round's <c>TFSTAT_DAMAGE_ASSIST</c>.</summary>
    [NetArray(Slots)] public partial NetArray<int> DamageAssist { get; }

    /// <summary><c>m_iHealing</c>: the round's <c>TFSTAT_HEALING</c>.</summary>
    [NetArray(Slots)] public partial NetArray<int> Healing { get; }

    /// <summary><c>m_iHealingAssist</c>: the round's <c>TFSTAT_HEALING_ASSIST</c>.</summary>
    [NetArray(Slots)] public partial NetArray<int> HealingAssist { get; }

    /// <summary><c>m_iDamageBlocked</c>: the round's <c>TFSTAT_DAMAGE_BLOCKED</c>.</summary>
    [NetArray(Slots)] public partial NetArray<int> DamageBlocked { get; }

    /// <summary><c>m_iBonusPoints</c>: the round's <c>TFSTAT_BONUS_POINTS</c>.</summary>
    [NetArray(Slots)] public partial NetArray<int> BonusPoints { get; }

    /// <summary>
    /// The gamerules' round state, a <see cref="TF2.Shared.Rules.RoundState"/>:
    /// stock's client reads <c>m_iRoundState</c> off the gamerules proxy,
    /// which this build does not carry — the resource's one row-independent
    /// word is the smallest read that reaches it (a dead player's HUD names
    /// the stalemate and next-round arms of the respawn countdown).
    /// </summary>
    public partial byte RoundState { get; set; }

    /// <summary>
    /// The team that took the round while <see cref="RoundState"/> is
    /// <c>TeamWin</c>, a <see cref="TF2.Shared.TFTeam"/>: stock's
    /// <c>m_iWinningTeam</c> on the gamerules proxy, riding beside the state
    /// word so the client's round-win arm of
    /// <c>CalcIsAttackCritical</c> can tell whose winners it is predicting.
    /// </summary>
    public partial byte WinningTeam { get; set; }

    /// <summary>
    /// Stock's <c>m_bInWaitingForPlayers</c> on the gamerules proxy: the
    /// pre-game wait the clock's band names (<c>SetExtraTimePanels</c>,
    /// tf_time_panel.cpp:573-589), which hides the setup band while it is
    /// up.
    /// </summary>
    public partial byte WaitingForPlayers { get; set; }

    /// <summary>
    /// Stock's <c>m_bInSetup</c> on the gamerules proxy
    /// (teamplayroundbased_gamerules.cpp:91/:111): the setup period a round
    /// timer's state change sets through <c>SetSetup</c> (:910-916) — inside
    /// the running round, its doors shut, not the brief preround freeze. The
    /// medigun's charge gate reads it on the client as the server's
    /// <see cref="TF2.Server.Rules.ITeamRoundState.InSetup"/> half does.
    /// </summary>
    public partial byte InSetup { get; set; }

    /// <summary>
    /// Stock's <c>m_flMapResetTime</c> (gamerules netvar): the map clock's
    /// start, what the clock's server-time arm counts
    /// <c>GetTimeLeft</c> from (teamplayroundbased_gamerules.cpp:1226-1239)
    /// — a float, not a word, but the same read: no gamerules proxy to
    /// carry it.
    /// </summary>
    public partial float MapResetTime { get; set; }
}

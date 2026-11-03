//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Client.Entities;
using TF2.Client.Player;
using TF2.Shared.Player;

using EntityTimer = TF2.Client.Entities.ITFRoundTimer;

namespace TF2.Client.Ui;

/// <summary>
/// The round timer's HUD row (what stock's <c>CTFHudTimeStatus</c> reads,
/// tf_time_panel.cpp:397-569): the active <c>team_round_timer</c> as the
/// client's election picked it (<c>TFClientRoundTimers</c>, the way
/// <c>CTFGameRules::GetActiveRoundTimer</c> does it), through the client's
/// own clock arithmetic (<c>GetTimeRemaining</c>,
/// <c>GetTimerMaxLength</c>, teamplay_round_timer.cpp:377-430). Overtime
/// is the one thing not on the wire: stock asks
/// <c>TFGameRules()-&gt;InOvertime()</c> (tf_time_panel.cpp:512), and the
/// flag is not replicated to the client, so it is derived here — the
/// clock past zero, in play, unpaused (the docs/divergences.md row says
/// why). The team is the local player's, what
/// <c>SetTeamBackground</c> (tf_time_panel.cpp:327-356) picks the panel's
/// art by, off the client's player-resource mirror the way the flag row's
/// <c>ZoneBearing</c> reads it. The sudden-death and waiting-for-players
/// words and the map clock's start ride the same mirror as gamerules
/// proxy words (see <see cref="TFPlayerResourceNetState"/>), and the
/// server limit is the client's own <c>mp_timelimit</c> half
/// (<c>TFGameRules()-&gt;GetTimeLeft</c>,
/// teamplayroundbased_gamerules.cpp:1226-1239).
/// </summary>
[SideService(Side.Client, As = [typeof(ITFRoundTimer)])]
public sealed class TFHudRoundTimer(ITFRoundTimers timers, IGameClock clock, IEngineHud screen,
                                    TFClientPlayerResources resources, ClMpTimeLimitConVar timeLimit,
                                    TFHudShowServerTimeLimitConVar showServerLimit) : ITFRoundTimer
{
    public bool Present => Active is not null;

    public bool InSetup => Active?.InSetup ?? false;

    public bool IsPaused => Active?.IsPaused ?? false;

    public bool ShowTimeRemaining => Active?.ShowTimeRemaining ?? false;

    public int SecondsLeft => Active is { } timer ? (int)System.Math.Floor(timer.TimeRemaining(clock.Now)) : 0;

    public int TotalSeconds => Active?.TimerMaxLength ?? 0;

    public bool Overtime => Active is { } timer
        && !timer.InSetup && !timer.IsPaused && timer.TimeRemaining(clock.Now) <= 0f;

    public bool StopWatch => Active?.IsStopWatch ?? false;

    /// <summary>The stalemate word off the resource's mirror (the
    /// respawn-countdown's read of the same word).</summary>
    public bool Stalemate => Word(TF2.Shared.Rules.RoundState.Stalemate);

    public bool WaitingForPlayers => (resources.Live?.State.WaitingForPlayers ?? 0) != 0;

    public int ServerTimeLimit => (int)(timeLimit.Value * 60f);

    public bool ShowServerLimit => showServerLimit.Value;

    public int ServerTimeLeft
    {
        get
        {
            if (resources.Live is not { } live)
                return 0;                        // no mirror: no reset time to count from

            float left = live.State.MapResetTime + timeLimit.Value * 60f - clock.Now;
            return left <= 0f ? 0 : (int)left;
        }
    }

    public int Team
    {
        get
        {
            int local = screen.LocalPlayerIndex;
            if (local <= 0 || local >= TFPlayerResourceNetState.Slots)
                return 0;
            return resources.Live?.State.Team[local] ?? 0;
        }
    }

    /// <summary>Whether the resource's round-state word names
    /// <paramref name="state"/> (TFRespawnCountdown.RoundIs).</summary>
    private bool Word(TF2.Shared.Rules.RoundState state)
        => resources.Live is { } live && live.State.RoundState == (byte)state;

    /// <summary>The elected timer, read once per member so one paint sees
    /// one election.</summary>
    private EntityTimer? Active => timers.Active;
}

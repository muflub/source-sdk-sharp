//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared.Rules;

/// <summary>
/// <c>team_round_timer</c>'s networked state: stock's <c>DT_TeamRoundTimer</c>
/// (teamplay_round_timer.cpp), field for field and in its order, so the HUD
/// timer (Stage 3) reads what stock's <c>CHudTFRoundTimer</c> read. Written
/// by the server's <c>TeamRoundTimer</c> after every change.
/// </summary>
[StandaloneNetState(ClassName)]
public partial class TeamRoundTimerNetState
{
    /// <summary>The entity's classname.</summary>
    public const string ClassName = "team_round_timer";

    /// <summary><c>RT_STATE_SETUP</c> (shareddefs.h).</summary>
    public const int StateSetup = 0;

    /// <summary><c>RT_STATE_NORMAL</c>.</summary>
    public const int StateNormal = 1;

    public partial bool TimerPaused { get; set; }

    /// <summary>Seconds left when paused.</summary>
    public partial float TimeRemaining { get; set; }

    /// <summary>When the timer reaches zero while running.</summary>
    public partial float TimerEndTime { get; set; }

    public partial int TimerMaxLength { get; set; }
    public partial bool IsDisabled { get; set; }
    public partial bool ShowInHud { get; set; }
    public partial int TimerLength { get; set; }
    public partial int TimerInitialLength { get; set; }
    public partial bool AutoCountdown { get; set; }
    public partial int SetupTimeLength { get; set; }

    /// <summary><see cref="StateSetup"/> or <see cref="StateNormal"/>.</summary>
    public partial int State { get; set; }

    public partial bool StartPaused { get; set; }
    public partial bool ShowTimeRemaining { get; set; }
    public partial bool StopWatchTimer { get; set; }
    public partial bool InCaptureWatchState { get; set; }
    public partial float TotalTime { get; set; }
}

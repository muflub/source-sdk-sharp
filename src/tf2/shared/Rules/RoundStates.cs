//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared.Rules;

// The team-round machine's enums (teamplayroundbased_gamerules.h), shared so
// the server's round flow and the client's HUD name the same values. The
// stock prefix (GR_STATE_, WINREASON_, STALEMATE_) is dropped and the rest is
// PascalCase; the count members are kept. WinReason includes the TF_DLL arm.

/// <summary>gamerules_roundstate_t.</summary>
public enum RoundState
{
    Init = 0,
    Pregame,
    StartGame,
    Preround,
    RoundRunning,
    TeamWin,
    Restart,
    Stalemate,
    GameOver,
    Bonus,
    BetweenRounds,
    Count,
}

/// <summary>The anonymous WINREASON_ enum.</summary>
public enum WinReason
{
    None = 0,
    AllPointsCaptured,
    OpponentsDead,
    FlagCaptureLimit,
    DefendUntilTimeLimit,
    Stalemate,
    TimeLimit,
    WinLimit,
    WinDiffLimit,
    RdReactorCaptured,
    RdCoresCollected,
    RdReactorReturned,
    PdPoints,
    Scored,
    StopwatchWatchingRounds,
    StopwatchWatchingFinalRound,
    StopwatchPlayingRounds,
}

/// <summary>stalemate_reasons_t.</summary>
public enum StalemateReason
{
    JoinMid,
    Timer,
    ServerTimeLimit,
    Count,
}

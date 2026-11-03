using SourceSharp.Abi;

namespace TF2.Server.Rules;

/// <summary>
/// The orders TF2's rules take in the core's unique-order sequences
/// (<see cref="StageUniqueness"/> refuses two participants at one number,
/// across mods, and a tie aborts the whole mod's load): TF2's thousand below
/// <see cref="ModStages.First"/> (999,000-999,999; Descent sits at First and
/// above, <c>TF2LifecycleOrderTests</c> holds the range), the rules at +100..+199
/// (the objectives and econ use +10..+60), and the same numbers for the
/// rules' hook orders.
/// </summary>
public static class TeamRoundOrders
{
    /// <summary>TF2's rules band.</summary>
    public const int Band = ModStages.First - 1_000 + 100;

    /// <summary><see cref="RoundTimers"/>' per-level clear.</summary>
    public const int RoundTimers = Band + 1;

    /// <summary><see cref="TeamBalance"/>'s per-level clear.</summary>
    public const int TeamBalance = Band + 2;

    /// <summary><see cref="TFAutobalance"/>'s per-level clear (<c>CTFAutobalance</c>'s LevelShutdownPostEntity).</summary>
    public const int Autobalance = Band + 3;

    /// <summary><see cref="TFJoinTeamCommand"/>: TF2's <c>jointeam</c> words.</summary>
    public const int JoinTeamCommand = Band + 10;

    /// <summary><see cref="TFChangeTeamKillRule"/>: the live switch dies, stock's arm.</summary>
    public const int ChangeTeamKill = Band + 11;

    /// <summary><see cref="TFWelcomeMenuCommands"/>: the welcome menu's close.</summary>
    public const int WelcomeMenu = Band + 12;

    /// <summary>TF2's mode selector, in the <c>IGameModeSelector</c> chain.</summary>
    public const int ModeSelector = Band + 20;

    /// <summary><see cref="TFMapCleanupPreserve"/>, in the <c>IMapCleanupPreserve</c> chain.</summary>
    public const int MapCleanupPreserve = Band + 30;

    /// <summary><see cref="CappingTeamTimerVeto"/>, in the <c>ITimerExpiryVeto</c> chain, before the watcher walk.</summary>
    public const int CappingTeamTimerVeto = Band + 40;

    /// <summary><see cref="Entities.CPTimerLogicTimerVeto"/>, in the <c>ITimerExpiryVeto</c> chain: stock walks the CP timers after the capping-team arms and before the base class's watchers.</summary>
    public const int CPTimerLogicTimerVeto = Band + 45;
}

using SourceSharp.Abi;

using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Modes;

/// <summary>
/// What a further mod hooks into a TF2 mode without subclassing it (D10):
/// told when a round starts and when an objective moves, asked whether a win
/// may stand and how long a team's respawn wave is. A chain (<c>IHooks</c>,
/// registered by <see cref="TFModesRegistrar"/>): every hook hears every TF2
/// mode, reads <see cref="TFModeBase.Id"/> to tell them apart, and implements
/// <see cref="IOrderedHook"/> with an order no other hook uses.
/// </summary>
public interface ITFModeHook
{
    /// <summary>A round of <paramref name="mode"/> is starting (stock <c>SetupOnRoundStart</c>, after the mode's own part).</summary>
    void OnRoundStart(TFModeBase mode);

    /// <summary>An objective of <paramref name="mode"/> moved (a capture, a flag pick-up, a checkpoint).</summary>
    void OnObjectiveEvent(TFModeBase mode, TFObjectiveEvent objective);

    /// <summary>Whether <paramref name="team"/>'s win for <paramref name="reason"/> may stand; false vetoes it.</summary>
    bool ModifyWinCheck(TFModeBase mode, TFTeam team, WinReason reason);

    /// <summary>The team's respawn wave length, given the length so far.</summary>
    float ModifyRespawnWave(TFModeBase mode, TFTeam team, float seconds);
}

/// <summary>
/// One objective event a mode reports to its hooks: what happened
/// (<see cref="Kind"/>, e.g. <c>"flag_captured"</c>, <c>"point_captured"</c>,
/// <c>"checkpoint"</c>), for which team, and which objective (a point or flag
/// index, -1 for none).
/// </summary>
public readonly record struct TFObjectiveEvent(string Kind, TFTeam Team, int Index = -1);

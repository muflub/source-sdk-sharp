//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Rules;

namespace TF2.Server.Modes;

/// <summary>
/// The classnames the current map placed: what a mode selector reads.
/// </summary>
public interface IMapClassnames
{
    /// <summary>Whether the map has at least one entity of <paramref name="className"/>.</summary>
    bool Contains(string className);

    /// <summary>The map's name, without path or extension; empty between levels.</summary>
    string MapName { get; }
}

/// <summary>
/// The classnames over the core's map spawner: every block it made a managed
/// entity of, and every block it handed back to the native parse (a class
/// TF2 has not ported yet is still on the map).
/// </summary>
[SideService(Side.Server, As = [typeof(IMapClassnames)])]
public sealed class SpawnedMapClassnames(MapSpawner spawner, ILevelInfo level) : IMapClassnames
{
    public bool Contains(string className)
        => spawner.Entities.Any(e => string.Equals(e.ClassName, className, StringComparison.OrdinalIgnoreCase))
           || spawner.LastResidueClasses.Any(c => string.Equals(c, className, StringComparison.OrdinalIgnoreCase));

    public string MapName => level.Map ?? string.Empty;
}

/// <summary>
/// TF2's mode selector (<see cref="IGameModeSelector"/>, a chain member):
/// stock <c>CTFGameRules::Activate</c>'s detection, for the four milestone
/// modes, in stock's order — an <c>sd_</c> map is not CTF however many flags
/// it has; any <c>item_teamflag</c> is CTF; else any <c>team_train_watcher</c>
/// is Payload; else any <c>team_control_point_master</c> is control points
/// (stock's guard also refuses the arena game type, whose points are not a CP
/// round — arena is deferred, so its arm cannot answer yet), and that CP map
/// is KOTH when it also holds a <c>tf_logic_koth</c> (stock sets
/// <c>m_bPlayingKoth</c> at tf_gamerules.cpp:4384-4388 on top of the CP game
/// type); else no opinion, and the next selector (finally the core's
/// <c>"null"</c>) answers. The arena, robot-destruction and MvM arms of the
/// same walk are not milestone modes (deferred).
/// </summary>
[SideService(Side.Server)]
public sealed class TFGameModeSelector(IMapClassnames map) : IGameModeSelector, IOrderedHook
{
    public const string FlagClass = "item_teamflag";
    public const string TrainWatcherClass = "team_train_watcher";
    public const string ControlPointMasterClass = "team_control_point_master";
    public const string KothLogicClass = "tf_logic_koth";

    public int HookOrder => TeamRoundOrders.ModeSelector;

    public string? PickMode()
    {
        if (map.MapName.StartsWith("sd_", StringComparison.OrdinalIgnoreCase))
            return null;

        if (map.Contains(FlagClass))
            return CtfMode.ModeId;

        if (map.Contains(TrainWatcherClass))
            return PayloadMode.ModeId;

        if (map.Contains(ControlPointMasterClass))
            return map.Contains(KothLogicClass) ? KothMode.ModeId : CPMode.ModeId;

        return null;
    }
}

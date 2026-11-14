using TF2.Descent.Rules;

namespace TF2.Descent.Server;

/// <summary>How a party goes down to the next depth: each way is its own travel notice (plan_descent_mine_levels D9).</summary>
public enum WayDown
{
    /// <summary>The town's cage to levels 1 to 5, and each of levels 1 to 4's cage to the next (section 8, M6).</summary>
    Cage,

    /// <summary>The town's hoist down to a camp, at 6 or 11 (section 8, M10).</summary>
    Hoist,

    /// <summary>Level 5: the lift shaft's floor, collapsed by Sir Nukesalot's death blast.</summary>
    CollapsedFloor,

    /// <summary>Level 6: a rubble ramp into the cave.</summary>
    RubbleRamp,

    /// <summary>Level 7: a rope-line stair down a shaft.</summary>
    RopeStair,

    /// <summary>Level 8: a sump, a drained shaft with a stair.</summary>
    DrainedSump,

    /// <summary>Level 9: a broken crypt floor.</summary>
    CryptFloor,

    /// <summary>Level 10: the bore hole the Tank came up.</summary>
    BoreHole,

    /// <summary>Level 11: a waste chute (a slide).</summary>
    WasteChute,

    /// <summary>Level 12: a dive into a sump.</summary>
    SumpDive,

    /// <summary>Level 13: a fissure drop.</summary>
    Fissure,

    /// <summary>Level 14: a slide of slime into the pit.</summary>
    SlimeSlide,

    /// <summary>A depth the plan names no way down from (level 15 is the bottom).</summary>
    Deeper,
}

/// <summary>
/// Which way down each depth uses, from the mine plan's section 7 "Way down"
/// rows and section 8 (the town's cage and the camps' hoist). The levels'
/// generator (src/mapgen/Descent/Shapes/ExitShape.cs) draws its exits from a
/// coarser set (cage, ramp, collapse, chute, fissure, picked per seed): this
/// table is the plan's, and the notice follows it.
/// </summary>
public static class WaysDown
{
    /// <summary>The way the town sends a party to <paramref name="depth"/>: the hoist to a camp, else the cage.</summary>
    public static WayDown FromTown(int depth) =>
        depth is TownDescents.FirstCamp or TownDescents.SecondCamp ? WayDown.Hoist : WayDown.Cage;

    /// <summary>The way down out of the level at <paramref name="depth"/>.</summary>
    public static WayDown FromLevel(int depth) => depth switch
    {
        >= 1 and <= 4 => WayDown.Cage,
        5 => WayDown.CollapsedFloor,
        6 => WayDown.RubbleRamp,
        7 => WayDown.RopeStair,
        8 => WayDown.DrainedSump,
        9 => WayDown.CryptFloor,
        10 => WayDown.BoreHole,
        11 => WayDown.WasteChute,
        12 => WayDown.SumpDive,
        13 => WayDown.Fissure,
        14 => WayDown.SlimeSlide,
        _ => WayDown.Deeper,
    };

    /// <summary>The way's notice, a token of the mod's language file.</summary>
    public static string Token(WayDown way) => "#Descent_Travel_" + way;
}

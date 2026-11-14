namespace TF2.Descent.Client;

/// <summary>
/// The mine's fifteen levels and three acts by name, as tokens of the mod's
/// language file (plan_descent_mine_levels E3, section 7): five levels an act,
/// Act I the Mann Co. mine (1 to 5), Act II the breakthrough (6 to 10), Act
/// III below (11 to 15).
/// </summary>
public static class LevelNames
{
    /// <summary>The mine's levels, and how many make an act.</summary>
    public const int Levels = 15, LevelsPerAct = 5;

    /// <summary>The arrival's second line: <c>%s1</c> the depth, <c>%s2</c> the level's name.</summary>
    public const string ArrivalDepthToken = "#Descent_Arrival_Depth";

    /// <summary>The act <paramref name="depth"/> is in, 1 to 3; 0 for a depth the mine does not have.</summary>
    public static int ActOf(int depth) => IsLevel(depth) ? (depth - 1) / LevelsPerAct + 1 : 0;

    /// <summary>The level's name token; null for a depth the mine does not have.</summary>
    public static string? LevelToken(int depth) => IsLevel(depth) ? "#Descent_Depth_Level_" + depth : null;

    /// <summary>The act's name token; null for a depth the mine does not have.</summary>
    public static string? ActToken(int depth) => IsLevel(depth) ? "#Descent_Depth_Act_" + ActOf(depth) : null;

    private static bool IsLevel(int depth) => depth is >= 1 and <= Levels;
}

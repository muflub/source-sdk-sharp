using System.Globalization;

namespace SourceSharp.MapGen.Descent;

/// <summary>
/// A point of a boss arena's track (the Tank's, level 10), room-local, on the floor: a plain point
/// that shapes the path, a station where the Tank stops and drills a pillar, or the final station
/// at the central pillar, where it brings the roof down.
/// </summary>
public readonly record struct TrackPoint(Point At, bool Station = false, bool Final = false)
{
    /// <summary>The point's flags as the <c>.map2d</c>'s <c>ARNA</c> section writes them: bit 0 a station, bit 1 the final one.</summary>
    public uint Flags => (Station || Final ? 1u : 0u) | (Final ? 2u : 0u);
}

/// <summary>
/// A robot spawn mouth of a boss arena, room-local: where robots come in, facing <paramref name="Yaw"/>,
/// open from the boss's phase <paramref name="Leg"/> (1-based; a leg-1 mouth is open from the start).
/// </summary>
public readonly record struct ArenaMouth(Point At, int Yaw, int Leg);

/// <summary>
/// What a boss arena tells the server beyond its markers (the game-logic session's contract, brief p13,
/// <c>.scratch/p13-contract.md</c>): where the boss stands and which way it faces at its intro (toward
/// the way in), the Tank's track in order (closing back to its first point), and the robots' spawn
/// mouths by leg. Room-local; <c>descent-level</c> turns it into the level's frame
/// (<see cref="DescentLevel.ArenaFile"/>) and <c>tools/descent-arena</c> writes it into the level's
/// <c>.map2d</c> as the section <c>ARNA</c>.
/// </summary>
public sealed record ArenaData(Point Boss, int BossYaw, IReadOnlyList<TrackPoint> Track, IReadOnlyList<ArenaMouth> Mouths)
{
    /// <summary>The track's length, point to point in file order, not closing back to the first.</summary>
    public float TrackLength
    {
        get
        {
            float sum = 0;
            for (int i = 1; i < Track.Count; i++)
                sum += MathF.Sqrt(Sq(Track[i].At.X - Track[i - 1].At.X) + Sq(Track[i].At.Y - Track[i - 1].At.Y));
            return sum;
        }
    }

    /// <summary>The track's stations in order (the final one last).</summary>
    public IReadOnlyList<TrackPoint> Stations => [.. Track.Where(p => p.Station || p.Final)];

    private static float Sq(float v) => v * v;
}

/// <summary>
/// The names the server's boss encounter drives by its Enable and Disable inputs (the contract, brief
/// p13): exact, lower case, decimal with no padding. Global names, which the link keeps as written
/// (a room-local one starts <c>cxry_</c>). One place, so a renaming is one edit.
/// </summary>
public static class BossNames
{
    /// <summary>The solid cover over a boss level's way down (level 5's collapsed shaft floor, level 10's bore hole), disabled on the kill.</summary>
    public const string Exit = "descent_boss_exit";

    /// <summary>The roof pillar the Tank drills at its station <paramref name="n"/> (1-based along the track; the central pillar the final station's).</summary>
    public static string Pillar(int n) => Name("descent_boss_pillar_", n);

    /// <summary>The rubble pillar <paramref name="n"/> leaves when it falls, disabled until then.</summary>
    public static string Rubble(int n) => Name("descent_boss_rubble_", n);

    /// <summary>A section of level 15's platform, disabled when the boss's phase <paramref name="phase"/> (2 to 4) starts.</summary>
    public static string Platform(int phase) => Name(PlatformPrefix, phase);

    /// <summary>What every platform section's name starts with: what breaks away, which no dressing stands on (brief p15).</summary>
    public const string PlatformPrefix = "descent_boss_platform_";

    /// <summary>How far dressing keeps from a section that breaks away: a torch at its very edge read as floating once it dropped (brief p15).</summary>
    public const float PlatformEdge = 32;

    /// <summary>A staged hazard volume of <paramref name="kind"/>, off until the boss's phase <paramref name="phase"/>.</summary>
    public static string Hazard(int phase, string kind) => Name("descent_boss_hazard_", phase) + "_" + kind;

    /// <summary>A cover over a spawn mouth of leg <paramref name="leg"/>, disabled when its leg opens.</summary>
    public static string Mouth(int leg) => Name("descent_boss_mouth_", leg);

    private static string Name(string prefix, int n) =>
        n >= 1 ? prefix + n.ToString(CultureInfo.InvariantCulture) : throw new ArgumentOutOfRangeException(nameof(n), n, "a boss name's number is 1 or more");
}

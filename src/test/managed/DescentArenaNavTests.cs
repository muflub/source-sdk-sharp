using System.Globalization;
using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The Bore's switched brushes as its built <c>.nav3d</c> holds them (brief p15): each named <c>func_brush</c>
/// is a Toggle obstacle whose leaves the server blocks while it stands, and the server's graph keeps one
/// obstacle per leaf, so no leaf may lie inside two of them (a heap of rubble over its pillar's leaves left the
/// pillar's fall unable to open them, brief p15). Reads this tree's build of the boss levels at seeds 1 to 3
/// (<c>make descent-rooms DEPTH=d</c>, <c>make descent-level DEPTH=d SEED=s</c>); each fact skips where its own level is not built.
/// </summary>
public sealed class DescentArenaNavTests
{
    [BuiltDescentLevelFact(10, 1)] public void No_leaf_of_depth_10_seed_1_lies_inside_two_toggle_obstacles() => NoLeafInsideTwoToggles(10, 1);
    [BuiltDescentLevelFact(10, 2)] public void No_leaf_of_depth_10_seed_2_lies_inside_two_toggle_obstacles() => NoLeafInsideTwoToggles(10, 2);
    [BuiltDescentLevelFact(10, 3)] public void No_leaf_of_depth_10_seed_3_lies_inside_two_toggle_obstacles() => NoLeafInsideTwoToggles(10, 3);
    [BuiltDescentLevelFact(5, 1)] public void No_leaf_of_depth_5_seed_1_lies_inside_two_toggle_obstacles() => NoLeafInsideTwoToggles(5, 1);
    [BuiltDescentLevelFact(5, 2)] public void No_leaf_of_depth_5_seed_2_lies_inside_two_toggle_obstacles() => NoLeafInsideTwoToggles(5, 2);
    [BuiltDescentLevelFact(5, 3)] public void No_leaf_of_depth_5_seed_3_lies_inside_two_toggle_obstacles() => NoLeafInsideTwoToggles(5, 3);
    [BuiltDescentLevelFact(15, 1)] public void No_leaf_of_depth_15_seed_1_lies_inside_two_toggle_obstacles() => NoLeafInsideTwoToggles(15, 1);
    [BuiltDescentLevelFact(15, 2)] public void No_leaf_of_depth_15_seed_2_lies_inside_two_toggle_obstacles() => NoLeafInsideTwoToggles(15, 2);
    [BuiltDescentLevelFact(15, 3)] public void No_leaf_of_depth_15_seed_3_lies_inside_two_toggle_obstacles() => NoLeafInsideTwoToggles(15, 3);

    private static void NoLeafInsideTwoToggles(int depth, int seed)
    {
        Nav3dReader nav = Nav3dReader.Open(File.ReadAllBytes(BuiltDescentLevelFactAttribute.Nav3d(depth, seed)));

        var shared = SharedLeaves(nav);

        Assert.Contains(Enumerable.Range(0, nav.ObstacleCount), o => nav.Obstacle(o).Name?.StartsWith("descent_boss_", StringComparison.Ordinal) == true);
        Assert.True(shared.Count == 0, string.Join("\n", shared.Take(12)));
    }

    [Fact]
    public void Two_overlapping_toggle_boxes_share_the_leaves_between_them()
    {
        // The rule's own arithmetic on boxes: overlapping by a voxel shares it, touching does not.
        var a = (new Vec3(0, 0, 0), new Vec3(64, 64, 64));
        Assert.True(Overlaps(a, (new Vec3(48, 0, 0), new Vec3(128, 64, 64))));
        Assert.False(Overlaps(a, (new Vec3(64, 0, 0), new Vec3(128, 64, 64))));
        Assert.False(Overlaps(a, (new Vec3(0, 0, 64), new Vec3(64, 64, 128))));
    }

    /// <summary>
    /// Every leaf inside two or more named Toggle obstacles' boxes, one line each. The unnamed ones are the shells'
    /// own (door caps, sill slabs, the Bore's worn treads, which are never solid): the server switches none of them.
    /// </summary>
    internal static List<string> SharedLeaves(Nav3dReader nav)
    {
        var toggles = Enumerable.Range(0, nav.ObstacleCount).Where(o => nav.Obstacle(o).Kind == Nav3dObstacleKind.Toggle && nav.Obstacle(o).Name is not null).ToArray();
        var found = new List<string>();
        for (int leaf = 0; leaf < nav.LeafCount; leaf++)
        {
            var box = nav.LeafBounds(leaf);
            var inside = toggles.Where(o => Overlaps(box, (nav.Obstacle(o).Mins, nav.Obstacle(o).Maxs))).ToArray();
            if (inside.Length > 1)
                found.Add(string.Create(CultureInfo.InvariantCulture,
                    $"leaf {leaf} ({box.Mins.X:0} {box.Mins.Y:0} {box.Mins.Z:0})-({box.Maxs.X:0} {box.Maxs.Y:0} {box.Maxs.Z:0}) inside {string.Join(", ", inside.Select(o => nav.Obstacle(o).Name))}"));
        }

        return found;
    }

    /// <summary>Whether two boxes share a volume (touching faces do not).</summary>
    internal static bool Overlaps((Vec3 Mins, Vec3 Maxs) a, (Vec3 Mins, Vec3 Maxs) b) =>
        a.Mins.X < b.Maxs.X && b.Mins.X < a.Maxs.X && a.Mins.Y < b.Maxs.Y && b.Mins.Y < a.Maxs.Y && a.Mins.Z < b.Maxs.Z && b.Mins.Z < a.Maxs.Z;
}

/// <summary>A fact over one level this tree built (per-tree build output, CLAUDE.md): skipped, naming the command, when that level is not built.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class BuiltDescentLevelFactAttribute : FactAttribute
{
    public BuiltDescentLevelFactAttribute(int depth, int seed)
    {
        string nav = Nav3d(depth, seed);
        if (!File.Exists(nav))
            Skip = string.Create(CultureInfo.InvariantCulture, $"no {nav}: build it with make descent-rooms DEPTH={depth} && make descent-level DEPTH={depth} SEED={seed}");
    }

    /// <summary>A built level's <c>.nav3d</c> in this tree.</summary>
    public static string Nav3d(int depth, int seed) =>
        RepoLayout.Repo("bin", RepoLayout.Config, "descent-rooms", "d" + depth.ToString("00", CultureInfo.InvariantCulture), "maps",
            string.Create(CultureInfo.InvariantCulture, $"tf2_descent-{depth}-s{seed}.nav3d"));
}

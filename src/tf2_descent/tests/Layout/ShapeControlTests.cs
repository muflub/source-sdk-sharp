using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Layout;
using Xunit;

namespace TF2.Descent.Tests.Layout;

/// <summary>
/// M19's shape controls, each set high and low on the same seeds and pool: the shape moves
/// the way the control says.
/// </summary>
public sealed class ShapeControlTests
{
    private const int Seeds = 150;

    private static RoomInfo R(string name, params string[] tags) => new(name, tags, 1, 1, 15);

    private static List<RoomInfo> Pool() =>
    [
        R("arrival_cage-e-c", "start"),
        R("exit_cage-w-c", "stairs"),
        R("drift_a-ew-l", "corridor"),
        R("bend_a-ne-l", "corridor"),
        R("tee_a-esw-l", "junction"),
        R("cross_a-nesw-l", "junction"),
        R("stub_a-e-l", "shrine"),
        R("pillar_a-ew-c"),
        R("pillar_b-ne-c"),
        R("pillar_e-e-c"),
        R("split_a-ew-c-j.s2.s3"),
        R("pit_a-nesw-c-j.s2.s3"),
        R("gallery_a-ew-h-j.b1.b2"),
        R("gallery_b-ne-h-j.b1"),
        R("gallery_c-esw-h-j.b2"),
        R("gallery_d-nesw-h-j.b1.b2"),
        R("gallery_e-e-h-j.b1.b2"),
        R("vault_a-e-c", "treasure"),
    ];

    /// <summary>Depth 8's row with one control changed, over the same seeds.</summary>
    private static List<(LevelPlan Plan, LayoutReport Report)> Levels(Func<DepthProfile, DepthProfile> change)
    {
        var profile = change(DepthTable.For(8));
        var pool = Pool();
        var levels = new List<(LevelPlan, LayoutReport)>();
        for (ulong seed = 1; seed <= Seeds; seed++)
        {
            var result = new LevelLayout().Generate(profile, seed, pool);
            Assert.True(result.Ok, result.Error);
            Assert.Equal(profile.Rooms, result.Report!.Rooms);
            levels.Add((result.Plan!, result.Report));
        }
        return levels;
    }

    [Fact]
    public void A_straighter_level_runs_its_critical_path_further_in_a_line()
    {
        var straight = Levels(p => p with { Straightness = 0.95 }).Average(l => l.Report.LongestStraightRun);
        var winding = Levels(p => p with { Straightness = 0.15 }).Average(l => l.Report.LongestStraightRun);

        Assert.True(straight > winding + 1, $"straight {straight:F2}, winding {winding:F2}");
    }

    [Fact]
    public void A_sprawling_level_is_thinner_and_fills_less_of_its_box()
    {
        var thin = Levels(p => p with { Sprawl = 0.95 });
        var compact = Levels(p => p with { Sprawl = 0.05 });

        Assert.True(thin.Average(l => l.Report.MeanNeighbours) < compact.Average(l => l.Report.MeanNeighbours) - 0.2,
            $"neighbours {thin.Average(l => l.Report.MeanNeighbours):F2} and {compact.Average(l => l.Report.MeanNeighbours):F2}");
        Assert.True(thin.Average(l => l.Report.FillRatio) < compact.Average(l => l.Report.FillRatio),
            $"fill {thin.Average(l => l.Report.FillRatio):F2} and {compact.Average(l => l.Report.FillRatio):F2}");
    }

    [Fact]
    public void A_longer_branch_length_makes_longer_side_tunnels()
    {
        double Tunnel(List<(LevelPlan Plan, LayoutReport Report)> levels) =>
            levels.Average(l => (double)new LevelCheck(8, l.Plan, l.Report, Pool()).TunnelDoors / Math.Max(1, l.Report.DeadEnds));

        var longer = Tunnel(Levels(p => p with { BranchLength = 8 }));
        var shorter = Tunnel(Levels(p => p with { BranchLength = 1 }));

        Assert.True(longer > shorter, $"doors from a dead end to the path: {longer:F2} and {shorter:F2}");
    }

    [Fact]
    public void More_loops_asked_close_more_loops()
    {
        var many = Levels(p => p with { Loops = 8 }).Average(l => l.Report.Loops);
        var few = Levels(p => p with { Loops = 1 }).Average(l => l.Report.Loops);

        Assert.True(many > few + 3, $"loops {many:F2} and {few:F2}");
        Assert.True(few <= 1.5, $"a level asked one loop closes {few:F2} on average");
    }

    [Fact]
    public void Clustering_groups_the_halls_together()
    {
        var grouped = Levels(p => p with { Clusters = 1 }).Average(l => l.Report.TallClusterMean);
        var scattered = Levels(p => p with { Clusters = 0 }).Average(l => l.Report.TallClusterMean);

        Assert.True(grouped > scattered, $"tall groups {grouped:F2} and {scattered:F2}");
    }
}

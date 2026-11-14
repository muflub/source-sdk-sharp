using TF2.Descent.Rules.Layout;
using Xunit;

namespace TF2.Descent.Tests.Layout;

/// <summary>The per-depth table (C2) against plan_descent_mine_levels sections 4.2, 7 and M10, M11, M16, M18 to M21, E4.</summary>
public sealed class DepthTableTests
{
    private static IEnumerable<DepthProfile> All() => Enumerable.Range(1, DepthTable.Depths).Select(DepthTable.For);

    [Theory]
    [InlineData(1, 18, 6, 8, 3)]
    [InlineData(2, 21, 7, 10, 3)]
    [InlineData(3, 27, 9, 12, 4)]
    [InlineData(4, 27, 9, 12, 4)]
    [InlineData(6, 27, 9, 12, 4)]
    [InlineData(7, 32, 10, 13, 5)]
    [InlineData(8, 37, 11, 15, 5)]
    [InlineData(9, 37, 11, 15, 6)]
    [InlineData(11, 37, 11, 15, 6)]
    [InlineData(12, 44, 13, 17, 6)]
    [InlineData(13, 51, 15, 19, 7)]
    [InlineData(14, 51, 15, 19, 7)]
    public void A_normal_level_has_its_room_budget_path_and_loops(int depth, int rooms, int pathMin, int pathMax, int loops)
    {
        // M16 and M19 (2026-11-10): the rooms are the budget, the grid is whatever they make.
        var row = DepthTable.For(depth);

        Assert.Equal((rooms, pathMin, pathMax, loops), (row.Rooms, row.PathMin, row.PathMax, row.Loops));
        Assert.False(row.IsBoss);
    }

    [Fact]
    public void The_bounding_box_stays_inside_the_links_world_extent_and_cell_limit()
    {
        // LevelLinker.CheckExtent lays cells from the origin and stops at 16,384 units; LevelYaml.MaxCells is 4,096.
        Assert.Equal(16384, DepthTable.WorldExtent);
        Assert.All(All(), r => Assert.True(r.MaxSpan * DepthTable.CellUnits < DepthTable.WorldExtent, $"depth {r.Depth}: {r.MaxSpan} cells of {DepthTable.CellUnits}"));
        Assert.All(All(), r => Assert.True(r.MaxSpan * r.MaxSpan <= 4096));
        Assert.All(All().Where(r => !r.IsBoss), r => Assert.Equal(DepthTable.DefaultMaxSpan, r.MaxSpan));
    }

    [Fact]
    public void A_normal_level_fits_its_rooms_in_its_span()
    {
        Assert.All(All().Where(r => !r.IsBoss), r => Assert.True(r.Rooms <= r.MaxSpan * r.MaxSpan && r.PathMax < r.Rooms, $"depth {r.Depth}"));
    }

    [Theory]
    [InlineData(1, 0.85, 0.6, 4, 0.3)]
    [InlineData(2, 0.85, 0.6, 4, 0.3)]
    [InlineData(3, 0.8, 0.55, 3, 0.4)]
    [InlineData(4, 0.8, 0.55, 3, 0.5)]
    [InlineData(6, 0.5, 0.75, 4, 0.5)]
    [InlineData(7, 0.45, 0.7, 3, 0.9)]
    [InlineData(8, 0.45, 0.75, 4, 0.5)]
    [InlineData(9, 0.3, 0.7, 2, 0.2)]
    [InlineData(11, 0.9, 0.35, 3, 0.4)]
    [InlineData(12, 0.6, 0.7, 4, 0.5)]
    [InlineData(13, 0.5, 0.8, 5, 0.6)]
    [InlineData(14, 0.4, 0.85, 5, 0.6)]
    public void A_normal_level_has_its_mine_tunnel_shape(int depth, double straightness, double sprawl, int branchLength, double clusters)
    {
        // M19: Act I engineered drifts, Act II winding caves (7 the big caverns, 9 tight and twisty),
        // Act III a regular factory floor at 11 and ever more winding below.
        var row = DepthTable.For(depth);

        Assert.Equal((straightness, sprawl, branchLength, clusters), (row.Straightness, row.Sprawl, row.BranchLength, row.Clusters));
    }

    [Fact]
    public void The_boss_levels_are_5_10_and_15_and_are_one_row()
    {
        Assert.Equal([5, 10, 15], All().Where(r => r.IsBoss).Select(r => r.Depth));
        Assert.Equal([4, 4, 3], All().Where(r => r.IsBoss).Select(r => r.Rooms));
    }

    [Fact]
    public void Only_the_bottom_level_has_no_way_down()
    {
        Assert.Equal([15], All().Where(r => !r.HasDownExit).Select(r => r.Depth));
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(2, 2, 2)]
    [InlineData(3, 2, 2)]
    [InlineData(4, 2, 2)]
    [InlineData(6, 2, 2)]
    [InlineData(7, 2, 2)]
    [InlineData(8, 2, 2)]
    [InlineData(9, 1, 1)]
    [InlineData(11, 2, 2)]
    [InlineData(12, 2, 2)]
    [InlineData(13, 2, 2)]
    [InlineData(14, 2, 2)]
    public void A_normal_level_asks_its_jump_spots(int depth, int scout, int blast)
    {
        // Two of each (plan 4.2), but the hallway level 1 and the crypt level 9 (section 7).
        var row = DepthTable.For(depth);

        Assert.Equal((scout, blast), (row.MinSTierSpots, row.MinBTierSpots));
    }

    [Theory]
    [InlineData(1, 15, 2)]
    [InlineData(2, 15, 1)]
    [InlineData(3, 15, 1)]
    [InlineData(4, 15, 1)]
    [InlineData(6, 18, 1)]
    [InlineData(7, 18, 1)]
    [InlineData(8, 18, 1)]
    [InlineData(9, 20, 3)]
    [InlineData(11, 15, 1)]
    [InlineData(12, 18, 1)]
    [InlineData(13, 18, 2)]
    [InlineData(14, 18, 2)]
    public void A_normal_level_steers_its_tunnel_ends_and_counts_its_shrines(int depth, int endShare, int shrines)
    {
        // M20 (2026-11-10): no cap on tunnels that end in a room, a share to steer towards instead.
        var row = DepthTable.For(depth);

        Assert.Equal((endShare, shrines), (row.EndSharePercent, row.MaxShrines));
    }

    [Theory]
    [InlineData(1, 75, 25, 0, 0)]
    [InlineData(2, 45, 40, 15, 0)]
    [InlineData(3, 30, 35, 35, 0)]
    [InlineData(4, 30, 25, 40, 5)]
    [InlineData(6, 40, 20, 40, 0)]
    [InlineData(7, 20, 0, 45, 35)]
    [InlineData(8, 35, 35, 30, 0)]
    [InlineData(9, 40, 50, 10, 0)]
    [InlineData(11, 35, 25, 40, 0)]
    [InlineData(12, 30, 35, 20, 15)]
    [InlineData(13, 25, 0, 45, 30)]
    [InlineData(14, 35, 10, 30, 25)]
    public void A_normal_level_has_its_mix_by_kind(int depth, int connector, int chamber, int hall, int shaft)
    {
        // M21: plan section 7's "Shapes" rows as shares of the fill rooms.
        var row = DepthTable.For(depth);

        Assert.Equal([connector, chamber, hall, shaft], row.Kinds.OrderBy(k => k.Kind).Select(k => k.SharePercent));
        Assert.Equal(100, row.Kinds.Sum(k => k.SharePercent));
    }

    [Theory]
    [InlineData(1, "drift:30 bend:15 tee:10 store:0[1-1]")]
    [InlineData(2, "widedrift:20 yard:15 stope:0[1-3]")]
    [InlineData(3, "stope:20 split:15 pit:15")]
    [InlineData(4, "gallery:25 chimney:0[1-2] store:15")]
    [InlineData(6, "drift:25 cavern:25 chasm:0[1-2]")]
    [InlineData(7, "cavern:30 chimney:20 chasm:15")]
    [InlineData(8, "pool:25 pit:20 drift:20")]
    [InlineData(9, "crypt:35 drift:20")]
    [InlineData(11, "gallery:30 yard:20 widedrift:20")]
    [InlineData(12, "pool:20 pit:20 chimney:15")]
    [InlineData(13, "chasm:20 cavern:25 stope:15")]
    [InlineData(14, "cavern:25 pool:20 dogleg:20 drift:0[0-0]")]
    public void A_normal_level_names_the_families_of_its_shapes_row(int depth, string families)
    {
        static string Spell(FamilyTarget f) =>
            $"{f.Family}:{f.SharePercent}" + (f.Min > 0 || f.Max < int.MaxValue ? $"[{f.Min}-{f.Max}]" : "");

        Assert.Equal(families, string.Join(' ', DepthTable.For(depth).FamilyTargets.Select(Spell)));
    }

    [Fact]
    public void Level_9_holds_two_tall_naves_at_most_and_level_1_five_chambers()
    {
        Assert.Equal(2, DepthTable.For(9).KindTargetOf(RoomClass.Hall)!.Max);
        Assert.Equal(5, DepthTable.For(1).KindTargetOf(RoomClass.Chamber)!.Max);
    }

    [Fact]
    public void A_boss_level_asks_no_jump_spots_of_the_layout()
    {
        Assert.All(All().Where(r => r.IsBoss), r => Assert.Equal((0, 0), (r.MinSTierSpots, r.MinBTierSpots)));
    }

    [Fact]
    public void Level_1_favours_the_connectors_over_the_chambers()
    {
        var row = DepthTable.For(1);

        Assert.Equal(75, row.KindTargetOf(RoomClass.Connector)!.SharePercent);
        Assert.True(row.Heights.Low >= 6 * row.Heights.Chamber, $"low {row.Heights.Low}, chamber {row.Heights.Chamber}");
        Assert.Equal((0, 0), (row.Heights.Hall, row.Heights.Shaft));
    }

    [Fact]
    public void Each_act_steers_at_least_60_percent_of_its_rooms_to_jump_rooms()
    {
        // E4: R19's share is held per act, weighted by how many rooms each level fills.
        foreach (var act in All().Where(r => !r.IsBoss).GroupBy(r => r.Act))
        {
            var rooms = act.Sum(r => r.Rooms);
            var jumps = act.Sum(r => r.Rooms * r.JumpSharePercent);
            Assert.True(jumps >= 60 * rooms, $"act {act.Key} steers {(double)jumps / rooms:F1} % of its rooms to jump rooms");
        }
    }

    [Fact]
    public void The_camps_are_the_arrivals_of_6_and_11()
    {
        Assert.Equal([6, 11], All().Where(r => r.ArrivalFamily == "camp").Select(r => r.Depth));
    }

    [Fact]
    public void The_critical_path_is_under_half_the_rooms_and_grows_with_them()
    {
        var normal = All().Where(r => !r.IsBoss).OrderBy(r => r.Rooms).ToList();
        foreach (var r in normal)
        {
            Assert.InRange(r.PathMin, 3, r.PathMax);
            Assert.True(r.PathMax * 2 <= r.Rooms, $"depth {r.Depth}'s path outgrows half its rooms");
        }
        for (var i = 1; i < normal.Count; i++)
            Assert.True(normal[i].PathMax >= normal[i - 1].PathMax, $"depth {normal[i].Depth} has a shorter path than depth {normal[i - 1].Depth}");
    }

    [Fact]
    public void Level_14_has_no_straight_drift()
    {
        Assert.Equal(0, DepthTable.For(14).FamilyWeight("drift"));
        Assert.Equal(1, DepthTable.For(13).FamilyWeight("drift"));
        Assert.Equal(1, DepthTable.For(1).FamilyWeight("drift"));
        Assert.Equal(0, DepthTable.For(14).FamilyTargetOf("drift")!.Max);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void A_depth_outside_the_shaft_is_refused(int depth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DepthTable.For(depth));
    }
}

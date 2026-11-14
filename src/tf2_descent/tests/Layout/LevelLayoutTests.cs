using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Layout;
using Xunit;

namespace TF2.Descent.Tests.Layout;

/// <summary>The layout generator's rules (C1, C3) on small hand-made pools, each with a refusing or negative fixture.</summary>
public sealed class LevelLayoutTests
{
    private static RoomInfo R(string name, params string[] tags) => new(name, tags, 1, 1, 15);

    private static List<RoomInfo> Basic() =>
    [
        R("arrival_cage-e-c", "start"),
        R("exit_cage-w-c", "stairs"),
        R("drift_a-ew-l", "corridor"),
        R("bend_a-ne-l", "corridor"),
        R("tee_a-esw-l", "junction"),
        R("cross_a-nesw-l", "junction"),
        R("stub_a-e-l", "shrine"),
        R("pillar_a-ew-c"),
        R("split_a-ew-c-j.s2.s3"),
        R("gallery_a-ew-h-j.b1.b2"),
        R("vault_a-e-c", "treasure"),
    ];

    private static List<RoomInfo> Boss() =>
    [
        R("arrival_lift-e-c", "start"),
        R("antechamber_a-ew-c"),
        R("antechamber_b-ew-h"),
        R("antechamber_c-nesw-c"),
        R("arena_magazine-ew-h-j.s2.b1", "boss_arena"),
        R("exit_hole-w-c", "stairs"),
    ];

    private static IEnumerable<LayoutResult> Levels(int depth, IReadOnlyList<RoomInfo> pool, int seeds = 300) =>
        Enumerable.Range(0, seeds).Select(s => new LevelLayout().Generate(depth, (ulong)s * 7919 + 1, pool));

    private static List<string> Faults(int depth, LayoutResult level, IReadOnlyList<RoomInfo> pool, params string[] properties) =>
        new LevelCheck(depth, level.Plan!, level.Report!, pool).Faults.Where(f => properties.Contains(f.Property)).Select(f => f.Message).ToList();

    // -- Refusals ---------------------------------------------------------------

    [Fact]
    public void A_name_that_does_not_parse_is_reported_naming_the_room()
    {
        var level = new LevelLayout().Generate(1, 1, [.. Basic(), R("bogus")]);

        Assert.False(level.Ok);
        Assert.Contains("room name 'bogus'", level.Error);
    }

    [Fact]
    public void A_pool_without_an_arrival_is_reported()
    {
        var level = new LevelLayout().Generate(1, 1, Basic().Where(r => !r.Tags.Contains("start")).ToList());

        Assert.False(level.Ok);
        Assert.Contains("no arrival", level.Error);
    }

    [Fact]
    public void A_pool_without_an_exit_is_reported_above_the_bottom()
    {
        var level = new LevelLayout().Generate(4, 1, Basic().Where(r => !r.Tags.Contains("stairs")).ToList());

        Assert.False(level.Ok);
        Assert.Contains("no exit", level.Error);
    }

    [Fact]
    public void A_boss_level_without_an_arena_is_reported()
    {
        var level = new LevelLayout().Generate(10, 1, Boss().Where(r => !r.Tags.Contains("boss_arena")).ToList());

        Assert.False(level.Ok);
        Assert.Contains("boss_arena", level.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void A_depth_outside_the_shaft_is_reported(int depth)
    {
        var level = new LevelLayout().Generate(depth, 1, Basic());

        Assert.False(level.Ok);
        Assert.Contains("not a level of the shaft", level.Error);
    }

    [Fact]
    public void A_pool_with_no_room_for_the_depth_is_reported()
    {
        var level = new LevelLayout().Generate(3, 1, [new RoomInfo("drift_a-ew-l", ["corridor"], 1, 7, 9)]);

        Assert.False(level.Ok);
        Assert.Contains("no room for depth 3", level.Error);
    }

    [Fact]
    public void A_pool_that_cannot_join_a_path_is_reported_not_mislaid()
    {
        // Dead ends only: no room has two doors, so no critical path can pass through a cell.
        List<RoomInfo> pool = [R("arrival_cage-e-c", "start"), R("exit_cage-w-c", "stairs"), R("stub_a-e-l"), R("store_a-n-c")];

        var level = new LevelLayout().Generate(3, 1, pool);

        Assert.False(level.Ok);
        Assert.Null(level.Plan);
        Assert.Contains($"no layout in {LevelLayout.MaxAttempts} attempts", level.Error);
        Assert.Contains("critical path", level.Error);
    }

    // -- Doors ------------------------------------------------------------------

    [Fact]
    public void A_cell_takes_a_room_whose_doors_are_exactly_its_joints_when_the_pool_has_one()
    {
        var pool = Basic();
        var levels = Levels(3, pool).ToList();

        Assert.All(levels, l => Assert.True(l.Ok, l.Error));
        Assert.All(levels, l => Assert.Equal((0, 0), (l.Report!.CappedDoors, l.Report.WastedDoors)));
        Assert.All(levels, l => Assert.Empty(Faults(3, l, pool, LevelCheck.DoorRule, LevelCheck.Connected, LevelCheck.Path)));
    }

    [Fact]
    public void A_pool_with_no_dead_ends_caps_the_far_doors_of_its_branch_ends()
    {
        List<RoomInfo> pool = [.. Basic().Where(r => r.Name is not ("stub_a-e-l" or "cross_a-nesw-l"))];
        var levels = Levels(3, pool).ToList();

        Assert.All(levels, l => Assert.True(l.Ok, l.Error));
        Assert.True(levels.Sum(l => l.Report!.CappedDoors) > 0);
        Assert.All(levels, l => Assert.Empty(Faults(3, l, pool, LevelCheck.Connected, LevelCheck.Path)));
    }

    // -- R19's low rooms -----------------------------------------------------------

    private static List<RoomInfo> Crypts() =>
    [
        R("arrival_cage-e-c", "start"),
        R("exit_cage-w-c", "stairs"),
        R("crypt_a-ew-l"),
        R("crypt_b-ne-l"),
        R("crypt_c-esw-l"),
        R("crypt_d-nesw-l"),
        R("crypt_e-e-l"),
    ];

    [Fact]
    public void No_third_low_combat_room_follows_two_on_the_critical_path()
    {
        // A corridor or junction of every footprint, so a path cell after two crypts always has a room.
        List<RoomInfo> pool = [.. Crypts(), R("drift_a-ew-l", "corridor"), R("bend_a-ne-l", "corridor"), R("tee_a-esw-l", "junction"), R("cross_a-nesw-l", "junction")];
        var levels = Levels(9, pool).ToList();

        Assert.All(levels, l => Assert.True(l.Ok, l.Error));
        Assert.All(levels, l => Assert.Empty(Faults(9, l, pool, LevelCheck.LowRule)));
        // The stimulus reached the rule: some paths hold two low combat rooms in a row.
        Assert.Contains(levels, l => TwoLowInARow(l));
    }

    private static bool TwoLowInARow(LayoutResult level)
    {
        var at = level.Plan!.Placements.ToDictionary(p => new GridCell(p.Row, p.Column), p => p.Room);
        var low = level.Report!.CriticalPath.Select(c => at[c].StartsWith("crypt_", StringComparison.Ordinal)).ToList();
        return low.Zip(low.Skip(1)).Any(p => p.First && p.Second);
    }

    [Fact]
    public void A_pool_of_only_low_combat_rooms_cannot_make_a_long_path_and_says_so()
    {
        var level = new LevelLayout().Generate(9, 1, Crypts());

        Assert.False(level.Ok);
        Assert.Contains("three in a row on the critical path", level.Error);
    }

    // -- The treasure ----------------------------------------------------------------

    [Fact]
    public void The_treasure_room_stands_off_the_critical_path()
    {
        var pool = Basic();
        var levels = Levels(4, pool).ToList();

        Assert.All(levels, l => Assert.True(l.Report!.TreasurePlaced));
        Assert.All(levels, l => Assert.Empty(Faults(4, l, pool, LevelCheck.Treasure)));
    }

    [Fact]
    public void A_pool_without_a_treasure_room_places_none()
    {
        var level = new LevelLayout().Generate(4, 1, Basic().Where(r => !r.Tags.Contains("treasure")).ToList());

        Assert.True(level.Ok, level.Error);
        Assert.False(level.Report!.TreasurePlaced);
        Assert.DoesNotContain(level.Plan!.Placements, p => p.Role == "treasure");
    }

    // -- Jump spots (plan 4.2) ---------------------------------------------------------

    [Fact]
    public void A_pool_with_every_tier_on_every_footprint_meets_the_jump_minimums()
    {
        // Basic() offers its tiers on straight rooms only, and a 3x4 level often has one
        // straight fill cell; spots on bends and tees too leave the minimums always in reach.
        List<RoomInfo> pool = [.. Basic(), R("pit_c-ne-c-j.s2.s3"), R("stope_c-ne-h-j.b1.b2"), R("yard_b-esw-c-j.s2.b1"), R("store_b-e-c-j.b1"), R("store_c-e-c-j.s2")];
        var levels = Levels(2, pool).ToList();

        Assert.All(levels, l => Assert.False(l.Report!.JumpFallback, l.Report.JumpFallbackReason));
        Assert.All(levels, l => Assert.True(l.Report!.STierSpots >= 2 && l.Report.BTierSpots >= 2));
    }

    [Fact]
    public void A_pool_with_no_blast_tier_room_falls_back_and_says_why()
    {
        var pool = Basic().Where(r => !r.Name.StartsWith("gallery_", StringComparison.Ordinal)).ToList();

        var level = new LevelLayout().Generate(2, 1, pool);

        Assert.True(level.Ok, level.Error);
        Assert.True(level.Report!.JumpFallback);
        Assert.Contains("the pool offers no B-tier room", level.Report.JumpFallbackReason);
        Assert.DoesNotContain("S-tier", level.Report.JumpFallbackReason);
        Assert.True(level.Report.STierSpots >= 2);
        Assert.Equal(0, level.Report.BTierSpots);
    }

    // -- Arrival, determinism -------------------------------------------------------------

    [Fact]
    public void A_camp_depth_opens_on_the_camp_when_the_pool_has_one()
    {
        List<RoomInfo> pool = [.. Basic(), R("camp_a-e-c", "start")];

        Assert.All(Levels(6, pool, 100), l => Assert.Equal("camp_a-e-c", l.Plan!.UpRoom));
        Assert.Contains(Levels(4, pool, 100), l => l.Plan!.UpRoom == "arrival_cage-e-c");
    }

    [Fact]
    public void The_order_of_the_pool_does_not_change_the_level()
    {
        var pool = Basic();
        var reversed = Enumerable.Reverse(pool).ToList();

        for (ulong seed = 0; seed < 50; seed++)
        {
            var a = new LevelLayout().Generate(4, seed, pool).Plan!;
            var b = new LevelLayout().Generate(4, seed, reversed).Plan!;
            Assert.Equal(a.Placements, b.Placements);
        }
    }

    [Fact]
    public void Different_seeds_give_different_levels()
    {
        var distinct = Levels(4, Basic(), 100)
            .Select(l => string.Join(";", l.Plan!.Placements.Select(p => $"{p.Room}@{p.Row},{p.Column},{p.Rotation}")))
            .Distinct()
            .Count();

        Assert.True(distinct > 90, $"{distinct} distinct levels from 100 seeds");
    }

    // -- Boss levels (C3) ----------------------------------------------------------------

    [Fact]
    public void A_boss_level_is_arrival_antechamber_arena_and_exit_in_one_row()
    {
        var level = new LevelLayout().Generate(5, 3, Boss());

        Assert.True(level.Ok, level.Error);
        Assert.Equal((1, 4), (level.Plan!.Rows, level.Plan.Columns));
        Assert.Equal(["up", "antechamber", "arena", "down"], level.Plan.Placements.OrderBy(p => p.Column).Select(p => p.Role));
        Assert.All(level.Plan.Placements, p => Assert.Equal(0, p.Row));
        Assert.Empty(Faults(5, level, Boss(), LevelCheck.DoorRule, LevelCheck.Connected, LevelCheck.Path, LevelCheck.Boss));
    }

    [Fact]
    public void The_antechamber_is_drawn_by_seed_from_its_variants_that_fit_the_row()
    {
        var drawn = Levels(5, Boss(), 100).Select(l => l.Plan!.Placements.Single(p => p.Role == "antechamber").Room).Distinct().Order(StringComparer.Ordinal);

        // The cross would cap two doors while two straight variants fit exactly, so it is never drawn.
        Assert.Equal(["antechamber_a-ew-c", "antechamber_b-ew-h"], drawn);
    }

    [Fact]
    public void A_boss_pool_without_an_antechamber_keeps_arrival_arena_and_exit()
    {
        var level = new LevelLayout().Generate(10, 1, Boss().Where(r => !r.Name.StartsWith("antechamber_", StringComparison.Ordinal)).ToList());

        Assert.True(level.Ok, level.Error);
        Assert.False(level.Report!.AntechamberPlaced);
        Assert.Equal(["up", "arena", "down"], level.Plan!.Placements.OrderBy(p => p.Column).Select(p => p.Role));
    }

    [Fact]
    public void The_bottom_boss_level_has_no_way_down()
    {
        var pool = Boss().Where(r => !r.Tags.Contains("stairs")).ToList();

        var level = new LevelLayout().Generate(15, 1, pool);

        Assert.True(level.Ok, level.Error);
        Assert.Equal((1, 3), (level.Plan!.Rows, level.Plan.Columns));
        Assert.Equal("", level.Plan.DownRoom);
        Assert.DoesNotContain(level.Plan.Placements, p => p.Role == "down");
    }
}

using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Layout;
using Xunit;

namespace TF2.Descent.Tests.Layout;

/// <summary>M18 (2026-11-10): limit the dead-end hallways. Each rule with a refusing fixture.</summary>
public sealed class DeadEndTests
{
    private static RoomInfo R(string name, params string[] tags) => new(name, tags, 1, 1, 15);

    private static LayoutRoom Room(string name, params string[] tags) => new(R(name, tags), RoomName.Parse(name));

    private static List<RoomInfo> Basic() =>
    [
        R("arrival_cage-e-c", "start"),
        R("exit_cage-w-c", "stairs"),
        R("drift_a-ew-l", "corridor"),
        R("bend_a-ne-l", "corridor"),
        R("tee_a-esw-l", "junction"),
        R("cross_a-nesw-l", "junction"),
        R("stub_a-e-l", "shrine"),
        R("stub_b-e-l"),
        R("pillar_a-ew-c"),
        R("pillar_e-e-c"),
        R("split_a-ew-c-j.s2.s3"),
        R("pit_c-ne-c-j.s2.s3"),
        R("gallery_a-ew-h-j.b1.b2"),
        R("stope_c-ne-h-j.b1.b2"),
        R("vault_a-e-c", "treasure"),
    ];

    private static IEnumerable<LayoutResult> Levels(int depth, IReadOnlyList<RoomInfo> pool, int seeds = 300) =>
        Enumerable.Range(0, seeds).Select(s => new LevelLayout().Generate(depth, (ulong)s * 7919 + 3, pool));

    // -- What a connector and a destination are ------------------------------------------

    [Theory]
    [InlineData("drift_a-ew-l", "corridor")]
    [InlineData("tee_a-esw-l", "junction")]
    [InlineData("bend_a-ne-l")]
    [InlineData("widedrift_a-ew-l")]
    [InlineData("stub_b-e-l")]
    [InlineData("pillar_a-ew-c", "corridor")]
    public void A_hallway_is_not_a_destination(string name, params string[] tags)
    {
        var room = Room(name, tags);

        Assert.True(room.IsConnector);
        Assert.False(room.IsDestination);
    }

    [Theory]
    [InlineData("stub_a-e-l", "shrine")]
    [InlineData("stub_c-e-l", "treasure")]
    [InlineData("vault_a-e-c", "treasure")]
    [InlineData("pillar_e-e-c")]
    [InlineData("crypt_d-e-l")]
    [InlineData("chimney_a-e-s-j.b2.b3")]
    public void A_combat_room_a_purposed_stub_and_the_treasure_are_destinations(string name, params string[] tags)
    {
        Assert.True(Room(name, tags).IsDestination);
    }

    // -- The generator --------------------------------------------------------------------

    [Fact]
    public void Every_dead_end_is_a_destination_when_the_pool_has_one()
    {
        var pool = Basic();
        var levels = Levels(3, pool).ToList();

        Assert.All(levels, l => Assert.True(l.Ok, l.Error));
        Assert.All(levels, l => Assert.Equal(0, l.Report!.DeadEndConnectors));
        Assert.All(levels, l => Assert.Null(l.Report!.DeadEndFallbackReason));
        Assert.All(levels, l => Assert.DoesNotContain(l.Plan!.Placements, p => p.Room == "stub_b-e-l"));
        Assert.Contains(levels, l => l.Report!.DeadEnds > 1);
    }

    [Fact]
    public void A_pool_with_no_destination_reports_its_dead_end_hallways()
    {
        // Hallways only, no treasure: a branch can end in nothing but a hallway.
        List<RoomInfo> pool = [.. Basic().Where(r => !Room(r.Name, [.. r.Tags]).IsDestination || r.Tags.Contains("start") || r.Tags.Contains("stairs"))];
        Assert.DoesNotContain(pool, r => r.Tags.Contains("treasure"));
        var levels = Levels(3, pool).ToList();

        Assert.All(levels, l => Assert.True(l.Ok, l.Error));
        Assert.All(levels, l => Assert.Equal(l.Report!.DeadEndConnectors > 0, l.Report.DeadEndFallbackReason is not null));
        Assert.Contains(levels, l => l.Report!.DeadEndConnectors > 0);
        Assert.All(levels.Where(l => l.Report!.DeadEndConnectors > 0),
            l => Assert.Contains("offers no destination room", l.Report!.DeadEndFallbackReason));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(13)]
    public void A_level_meets_its_budget_and_ends_its_tunnels_in_rooms(int depth)
    {
        // M20: no cap on tunnels that end in a room; every end is a destination.
        var profile = DepthTable.For(depth);
        var levels = Levels(depth, Basic(), 200).ToList();

        Assert.All(levels, l => Assert.Equal(profile.Rooms, l.Report!.Rooms));
        Assert.All(levels, l => Assert.Equal(0, l.Report!.DeadEndConnectors));
        Assert.Contains(levels, l => l.Report!.DeadEnds > 0);
    }

    [Fact]
    public void A_higher_end_share_ends_more_tunnels_in_rooms()
    {
        double Ends(int share) => Enumerable.Range(1, 150)
            .Select(s => new LevelLayout().Generate(DepthTable.For(8) with { EndSharePercent = share }, (ulong)s, Basic()).Report!)
            .Average(r => r.DeadEnds);

        var many = Ends(30);
        var few = Ends(5);

        Assert.True(many > few + 2, $"tunnel ends {many:F2} at 30 %, {few:F2} at 5 %");
    }

    [Fact]
    public void A_two_door_arrival_opens_its_spare_door_onto_a_branch_rather_than_a_cap()
    {
        List<RoomInfo> pool = [.. Basic().Where(r => !r.Tags.Contains("start")), R("arrival_station-ew-c", "start")];
        var levels = Levels(3, pool).ToList();

        Assert.All(levels, l => Assert.True(l.Ok, l.Error));
        Assert.All(levels, l => Assert.Equal(0, l.Report!.CappedDoors));
        Assert.All(levels, l => Assert.Equal(0, new LevelCheck(3, l.Plan!, l.Report!, pool).DoorsOnTheEdge));
    }

    // -- The checker refuses what the rule forbids -----------------------------------------

    private static LayoutReport Report(int deadEnds, int connectors, params GridCell[] path) =>
        new(path, 0, 0, 0, 0, false, 0, 0, false, null, 0, 0, 0, false, deadEnds, connectors, null, 0, 0, 0, 0, 0);

    private static List<RoomInfo> Plain() =>
    [
        R("arrival_cage-e-c", "start"), R("exit_cage-w-c", "stairs"), R("cross_a-nesw-l", "junction"),
        R("drift_b-ns-l", "corridor"), R("pillar_e-e-c"),
    ];

    [Fact]
    public void The_check_refuses_a_hallway_that_ends()
    {
        // Arrival, cross, exit along the south row; a drift north of the cross that stops.
        var plan = new LevelPlan(5, 5,
        [
            new Placement("arrival_cage-e-c", 0, 0, 0, "up"), new Placement("cross_a-nesw-l", 0, 1, 0, "path"),
            new Placement("exit_cage-w-c", 0, 2, 0, "down"), new Placement("drift_b-ns-l", 1, 1, 0, "branch"),
        ], "arrival_cage-e-c", "exit_cage-w-c");

        var check = new LevelCheck(1, plan, Report(1, 1, new(0, 0), new(0, 1), new(0, 2)), Plain());

        Assert.Contains(check.Faults, f => f.Property == LevelCheck.DeadEndRule && f.Message.Contains("hallway that ends", StringComparison.Ordinal));
        Assert.Equal((1, 1), (check.DeadEnds, check.DeadEndConnectors));
    }

    [Fact]
    public void The_map_draws_rooms_north_up_with_their_joined_doors()
    {
        // The hallway plan above: arrival, cross and exit on the south row, the drift north of the cross.
        var plan = new LevelPlan(2, 3,
        [
            new Placement("arrival_cage-e-c", 0, 0, 0, "up"), new Placement("cross_a-nesw-l", 0, 1, 0, "path"),
            new Placement("exit_cage-w-c", 0, 2, 0, "down"), new Placement("drift_b-ns-l", 1, 1, 0, "branch"),
        ], "arrival_cage-e-c", "exit_cage-w-c");

        var map = LevelMap.Render(plan, [new(0, 0), new(0, 1), new(0, 2)], Plain());

        Assert.Equal("  o\n  |\nA-O-Z\n", map);
    }

    [Fact]
    public void The_check_refuses_a_family_past_its_most()
    {
        // Level 1 holds one store at most; this plan hangs two off the crosses.
        var plan = new LevelPlan(2, 4,
        [
            new Placement("arrival_cage-e-c", 0, 0, 0, "up"), new Placement("cross_a-nesw-l", 0, 1, 0, "path"),
            new Placement("cross_a-nesw-l", 0, 2, 0, "path"), new Placement("exit_cage-w-c", 0, 3, 0, "down"),
            new Placement("store_a-e-c", 1, 1, 270, "branch"), new Placement("store_a-e-c", 1, 2, 270, "branch"),
        ], "arrival_cage-e-c", "exit_cage-w-c");
        List<RoomInfo> pool = [.. Plain(), R("store_a-e-c")];

        var check = new LevelCheck(1, plan, Report(2, 0, new(0, 0), new(0, 1), new(0, 2), new(0, 3)), pool);

        Assert.Equal(1, DepthTable.For(1).FamilyTargetOf("store")!.Max);
        Assert.Contains(check.Faults, f => f.Property == LevelCheck.Mix && f.Message.Contains("2 store rooms, the most is 1", StringComparison.Ordinal));
    }
}

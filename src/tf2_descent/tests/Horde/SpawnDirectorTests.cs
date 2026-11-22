using SourceSharp.Ai;
using TF2.Descent.Ai.Director;
using TF2.Descent.Ai.Level;
using TF2.Descent.Rules;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The spawn director (plan_descent_logic HO9): "no generated pack breaks the rules over 10k
/// seeds". Each rule of <see cref="DirectorOracle"/> is a fact over the routine sweep of
/// <see cref="DirectorSweep"/> (10,000 seeds, every depth 1 to 5 and party 1 to 4), with the
/// stimulus it needs shown to have reached it; the full product is the slow tier's.
/// </summary>
public sealed class SpawnDirectorSweepTests(DirectorSweep sweep) : IClassFixture<DirectorSweep>
{
    private static void Holds(DirectorSweep sweep, Func<SweepCase, string?> rule)
    {
        foreach (var c in sweep.Cases)
            if (rule(c) is { } broken)
                Assert.Fail($"seed {c.Seed} depth {c.Depth} party {c.Party}: {broken}");
    }

    private IEnumerable<PlannedRobot> Robots => sweep.Cases.SelectMany(DirectorOracle.Robots);

    [Fact]
    public void Every_depth_and_party_size_is_swept()
    {
        Assert.Equal(DirectorSweep.Seeds, sweep.Cases.Length);
        foreach (int depth in Enumerable.Range(1, 5))
            foreach (int party in Enumerable.Range(1, 4))
                Assert.True(sweep.Cases.Count(c => c.Depth == depth && c.Party == party) >= 400, $"depth {depth} party {party}");
    }

    [Fact]
    public void Pack_sizes_lie_in_the_depths_range_raised_for_the_party()
    {
        Holds(sweep, DirectorOracle.PackSize);
        Assert.True(sweep.Cases.Sum(c => c.Plan.Packs.Length) > 15_000);
    }

    [Fact]
    public void Every_robot_is_a_template_of_its_depth_at_a_scale_in_its_range()
    {
        Holds(sweep, DirectorOracle.Templates);
        Assert.Contains(Robots, r => r.Roll.Template == "giant_scout");
    }

    [Fact]
    public void Every_robot_fits_its_marker_and_can_leave_its_room()
    {
        Holds(sweep, DirectorOracle.Clearance);
        // The stimulus: closets, slots and raised sills refused robots, which were drawn again or left out.
        Assert.True(sweep.Cases.Sum(c => c.Plan.Refits) > 1000, $"drawn again {sweep.Cases.Sum(c => c.Plan.Refits)}");
        Assert.True(sweep.Cases.Sum(c => c.Plan.Unplaced) > 100, $"left out {sweep.Cases.Sum(c => c.Plan.Unplaced)}");
        Assert.True(Robots.Count(r => r.Roll.Template == "giant_scout") > 50);
    }

    [Fact]
    public void No_pack_is_more_than_half_immune_to_one_type()
    {
        Holds(sweep, DirectorOracle.HalfImmune);
        Assert.True(Robots.Count(r => r.Roll.Stats.Immunity is not null) > 50, $"immune {Robots.Count(r => r.Roll.Stats.Immunity is not null)}");
    }

    [Fact]
    public void Champions_carry_at_most_their_depths_affixes_and_one_immunity()
    {
        Holds(sweep, DirectorOracle.Champions);
        Assert.Contains(Robots, r => r.Roll.Affixes.Length == 2);   // depth 5's most
    }

    [Fact]
    public void Packs_are_mixed_unless_their_rerolls_ran_out()
    {
        Holds(sweep, DirectorOracle.Mixed);
        Assert.Contains(sweep.Cases.SelectMany(c => c.Plan.Packs), p => p.StyleRerolls > 0);
    }

    [Fact]
    public void The_roster_never_passes_its_capacity()
    {
        Holds(sweep, DirectorOracle.Capacity);
    }

    [Fact]
    public void Posts_are_inside_the_room_inside_the_clearance_and_apart()
    {
        Holds(sweep, DirectorOracle.Posts);
        int robots = Robots.Count(), crowded = Robots.Count(r => r.Crowded);
        Assert.True(crowded * 50 < robots, $"{crowded} of {robots} crowded onto their markers");
        Assert.True(Robots.Count(r => r.Post != sweep.Cases[0].Level.Rooms[0].Markers[0].Position) > 10_000);
    }
}

/// <summary>
/// The full sweep the plan's proving line names: 10,000 seeds x depths 1 to 5 x parties 1 to 4,
/// every rule. In the slow tier (tools/test-tiers.tsv): about 200,000 plans.
/// </summary>
public sealed class SpawnDirectorFullSweepTests
{
    [Fact]
    public void Ten_thousand_seeds_at_every_depth_and_party_break_no_rule()
    {
        var director = new SpawnDirector(TheRules);
        var broken = new System.Collections.Concurrent.ConcurrentQueue<string>();
        long robots = 0;
        Parallel.For(0, DirectorSweep.Seeds, s =>
        {
            for (int depth = 1; depth <= 5; depth++)
                for (int party = 1; party <= 4; party++)
                {
                    var c = DirectorSweep.Plan(director, (ulong)s, depth, party);
                    Interlocked.Add(ref robots, c.Plan.Robots);
                    foreach (var (name, check) in DirectorOracle.All)
                        if (check(c) is { } why)
                            broken.Enqueue($"seed {s} depth {depth} party {party}: {name}: {why}");
                }
        });
        Assert.Empty(broken.Take(10));
        Assert.True(robots > 1_000_000, $"{robots} robots");
    }
}

/// <summary>The director's own facts: determinism, clearance, leaving, posts, the boss and capacity.</summary>
public sealed class SpawnDirectorTests
{
    private static readonly SpawnDirector Director = new(TheRules);

    private static HordeDoor Door(int a, float width = 208, float height = 192, float sill = 0) =>
        new(a, a + 1, At((a + 1) * 1024, 512, sill), At(1, 0, 0), width, height);

    private static HordeRoom Room(int id, params PackMarker[] markers) =>
        new(id, At(id * 1024, 0, -64), At(id * 1024 + 1024, 1024, 512), [.. markers]);

    private static PackMarker Marker(int room, float x, float y, MarkerClearance? clearance = null, float z = 0) =>
        new(At(room * 1024 + x, y, z), 0, clearance);

    private static MarkerClearance Open(float height = 400) => new(400, 400, 400, 400, height);

    [Fact]
    public void The_same_inputs_plan_the_same_roster_on_a_fresh_rules_instance()
    {
        var level = DirectorLevels.Make(77, 5);

        Assert.Equal(Director.Plan(level, 3).Digest(), new SpawnDirector(new TF2DescentRules()).Plan(DirectorLevels.Make(77, 5), 3).Digest());
    }

    [Fact]
    public void Another_seed_plans_another_roster()
    {
        Assert.NotEqual(Director.Plan(DirectorLevels.Make(77, 5), 3).Digest(), Director.Plan(DirectorLevels.Make(78, 5), 3).Digest());
        Assert.NotEqual(Director.Plan(Row(4, seed: 77, depth: 5), 3).Digest(), Director.Plan(Row(4, seed: 78, depth: 5), 3).Digest());
    }

    [Fact]
    public void A_larger_party_plans_larger_packs()
    {
        var level = Row(6, markers: 3, seed: 11, depth: 3);

        Assert.True(Director.Plan(level, 4).Robots > Director.Plan(level, 1).Robots);
    }

    [Fact]
    public void A_giant_never_stands_at_a_low_marker()
    {
        // Marker 0 is 120 high, below every giant (82 x 1.5 = 123); marker 1 is open.
        int giants = 0;
        for (ulong seed = 0; seed < 3000; seed++)
        {
            var level = HordeLevel.Create(seed, 5, [Room(0, Marker(0, 300, 300, Open(120)), Marker(0, 700, 700, Open())), Room(1)], [Door(0)]);
            foreach (var r in Director.Plan(level, 4).Packs.SelectMany(p => p.Robots).Where(r => r.Roll.Template == "giant_scout"))
            {
                giants++;
                Assert.Equal(1, r.Marker);
            }
        }
        Assert.True(giants > 20, $"{giants} giants");
    }

    [Fact]
    public void A_robot_is_never_placed_in_a_room_it_cannot_leave()
    {
        // Room 1's only door is 60 wide: with 4 to spare each side, a hull of 52 at most, scale 52/48.
        int inside = 0;
        for (ulong seed = 0; seed < 1000; seed++)
        {
            var level = HordeLevel.Create(seed, 3, [Room(0), Room(1, Marker(1, 500, 500, Open()))], [Door(0, width: 60)]);
            foreach (var r in Director.Plan(level, 2).Packs.SelectMany(p => p.Robots))
            {
                inside++;
                Assert.True(48 * r.Roll.Scale <= 52.0001, $"seed {seed}: {r.Roll.Template} at {r.Roll.Scale} in a room behind a 60-unit door");
            }
        }
        Assert.True(inside > 1000);
    }

    [Fact]
    public void A_giant_keeps_out_of_a_room_whose_doors_are_all_off_its_floor()
    {
        // Room 1's floor is 48 below its only door's sill: a stair a giant cannot climb.
        int ordinary = 0;
        for (ulong seed = 0; seed < 3000; seed++)
        {
            var level = HordeLevel.Create(seed, 5, [Room(0), Room(1, Marker(1, 500, 500, Open()))], [Door(0, sill: 48)]);
            foreach (var r in Director.Plan(level, 4).Packs.SelectMany(p => p.Robots))
            {
                Assert.NotEqual("giant", TheRules.Robots.Template(r.Roll.Template).Size);
                ordinary++;
            }
        }
        Assert.True(ordinary > 1000);
    }

    /// <summary>A level's floor by hull as a fact draws it: rooms whose floor a hull above a scale does not share with the arrival's.</summary>
    private sealed class CutOff(double above, params int[] rooms) : IHullReach
    {
        public int Asked { get; private set; }

        public bool Joins(int room, AiVector at, double scale)
        {
            Asked++;
            return scale <= above || !rooms.Contains(room);
        }
    }

    /// <summary>
    /// A room whose doors fit a giant but whose floor its hull does not share with the arrival
    /// (depth 1 seed 1's widedrift rooms cut rooms 6, 16 and 17 off for scale 1.6 and up): the
    /// director never posts a robot that large there.
    /// </summary>
    [Fact]
    public void A_robot_is_never_posted_where_its_hulls_floor_does_not_join_the_arrivals()
    {
        int large = 0, cutOff = 0;
        for (ulong seed = 0; seed < 1500; seed++)
        {
            var level = HordeLevel.Create(seed, 5, [Room(0, Marker(0, 500, 500, Open())), Room(1, Marker(1, 500, 500, Open()))], [Door(0)]);
            large += Director.Plan(level, 4).Packs.SelectMany(p => p.Robots).Count(r => r.Room == 1 && r.Roll.Scale > 1.5);
            var reach = new CutOff(1.5, 1);
            foreach (var r in Director.Plan(level, 4, reach).Packs.SelectMany(p => p.Robots).Where(r => r.Room == 1))
            {
                Assert.True(r.Roll.Scale <= 1.5, $"seed {seed}: {r.Roll.Template} at {r.Roll.Scale} posted in room 1, whose floor its hull cannot leave");
                cutOff++;
            }

            Assert.True(reach.Asked > 0, "the control: the director never asked the reach");
        }

        Assert.True(large > 20, $"the control: only {large} robots above 1.5 were posted in room 1 with no reach");
        Assert.True(cutOff > 1000, $"{cutOff} robots in room 1");
    }

    [Fact]
    public void A_room_no_robot_fits_is_emptied_and_named()
    {
        // A closet 10 units round its marker holds no hull.
        var level = HordeLevel.Create(5, 2, [Room(0, Marker(0, 500, 500, new MarkerClearance(10, 10, 10, 10, 400))), Room(1, Marker(1, 500, 500, Open()))], [Door(0)]);

        var plan = Director.Plan(level, 1);

        Assert.DoesNotContain(plan.Packs, p => p.Room == 0);
        var emptied = Assert.Single(plan.Emptied);
        Assert.Equal(0, emptied.Room);
        Assert.True(emptied.Robots >= 3);
        Assert.Contains("1 rooms emptied", plan.Summary);
    }

    [Fact]
    public void An_unmeasured_marker_holds_anything_and_is_counted()
    {
        var plan = Director.Plan(Row(3, markers: 2, depth: 2), 1);

        Assert.Equal(6, plan.Unmeasured);
        Assert.Equal(0, plan.Refits);
    }

    [Fact]
    public void A_pack_on_one_marker_stands_on_posts_of_its_own()
    {
        var level = HordeLevel.Create(3, 3, [Room(0, Marker(0, 512, 512, Open())), Room(1)], [Door(0)]);

        var pack = Assert.Single(Director.Plan(level, 4).Packs);

        Assert.True(pack.Robots.Length >= 4);
        Assert.Equal(pack.Robots.Length, pack.Robots.Select(r => r.Post).Distinct().Count());
        Assert.All(pack.Robots, r => Assert.False(r.Crowded));
        Assert.Equal(At(512, 512), pack.Robots[0].Post);   // the first on the marker itself
        float step = 2 * pack.Robots.Max(r => Director.HalfWidth(r.Roll.Scale)) + 8;
        Assert.Equal(At(512 + step, 512), pack.Robots[1].Post);   // then the ring round it, from the east
    }

    [Fact]
    public void A_post_keeps_inside_its_markers_clearance()
    {
        // A marker in a corridor running east: 30 to the walls north and south, 400 east and west.
        var level = HordeLevel.Create(3, 3, [Room(0, Marker(0, 512, 512, new MarkerClearance(400, 400, 30, 30, 400))), Room(1)], [Door(0)]);

        var pack = Assert.Single(Director.Plan(level, 4).Packs);

        Assert.All(pack.Robots.Where(r => !r.Crowded), r => Assert.Equal(512, r.Post.Y));
        Assert.True(pack.Robots.Count(r => !r.Crowded) >= 3);
    }

    [Fact]
    public void A_pack_with_no_free_post_stands_crowded_on_its_marker()
    {
        // A closet just wide enough for one hull: one robot on the marker, the rest crowded.
        var level = HordeLevel.Create(3, 1, [Room(0, Marker(0, 512, 512, new MarkerClearance(28, 28, 28, 28, 400))), Room(1)], [Door(0)]);

        var pack = Assert.Single(Director.Plan(level, 4).Packs);

        Assert.False(pack.Robots[0].Crowded);
        Assert.All(pack.Robots.Skip(1), r => Assert.True(r.Crowded));
        Assert.All(pack.Robots, r => Assert.Equal(At(512, 512), r.Post));
        Assert.Equal(pack.Robots.Length - 1, pack.Crowded);
    }

    [Fact]
    public void A_post_keeps_clear_of_the_bosss_spot()
    {
        var boss = new HordeBoss("sir_nukesalot", 0, new PackMarker(At(512, 512), 0));
        var level = HordeLevel.Create(3, 5, [Room(0, Marker(0, 512, 512, Open())), Room(1)], [Door(0)], boss);

        var plan = Director.Plan(level, 4);

        Assert.True(plan.Packs[0].Robots[0].Boss);
        float bossHalf = Director.HalfWidth(plan.Packs[0].Robots[0].Roll.Scale);
        foreach (var r in plan.Packs[1].Robots)
        {
            float apart = bossHalf + Director.HalfWidth(r.Roll.Scale) + 8;
            Assert.True(MathF.Abs(r.Post.X - 512) >= apart - 0.01f || MathF.Abs(r.Post.Y - 512) >= apart - 0.01f, $"a post at {r.Post} on the boss");
        }
    }

    [Fact]
    public void Past_capacity_whole_packs_are_dropped_and_the_roster_stays_inside_it()
    {
        // Depth 15, a party of four: packs of 11 to 16, so 40 rooms overflow 256.
        var row = Row(40, markers: 3, seed: 3, depth: 15);
        var plan = Director.Plan(HordeLevel.Create(3, 15, row.Rooms, row.Doors.Select(d => d with { Width = 208 })), 4);

        Assert.NotEmpty(plan.Dropped);
        Assert.InRange(plan.Robots, 1, TheRules.Content.Horde.RosterCapacity);
    }

    [Fact]
    public void The_digest_names_every_row_field()
    {
        var plan = Director.Plan(DirectorLevels.Make(5, 4), 2);
        var rows = plan.Rows().ToList();
        var moved = rows.Select((r, i) => i == 0 ? r with { Post = r.Post + At(1, 0) } : r);

        Assert.NotEmpty(rows);
        Assert.NotEqual(plan.Digest(), RosterRow.Digest(moved));
        Assert.Equal(plan.Digest(), RosterRow.Digest(rows));
    }
}

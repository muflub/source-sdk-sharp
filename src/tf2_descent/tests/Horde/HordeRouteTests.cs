using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>Paths a fact answers: a straight path of three corners to the goal, or what it says; counts every search.</summary>
internal sealed class FakePaths : IHordePaths
{
    public int Epoch { get; set; } = 77;

    public PathAnswer Answer { get; set; } = PathAnswer.Found;

    public NavEdgeType FirstArrive { get; set; } = NavEdgeType.Walk;

    public int Searches { get; private set; }

    public List<(AiVector From, AiVector To, ClearanceClass Class)> Asked { get; } = [];

    public PathAnswer Find(AiVector from, AiVector to, ClearanceClass cls, int budget, Span<AiVector> corners, Span<NavEdgeType> arrive,
                           out int count, out bool truncated)
    {
        Searches++;
        Asked.Add((from, to, cls));
        count = 0;
        truncated = false;
        if (Answer != PathAnswer.Found)
            return Answer;
        // A dogleg: up, across, then the goal.
        corners[0] = new AiVector(from.X, from.Y + 200f, from.Z);
        corners[1] = new AiVector(to.X, from.Y + 200f, from.Z);
        corners[2] = to;
        arrive[0] = FirstArrive;
        arrive[1] = NavEdgeType.Walk;
        arrive[2] = NavEdgeType.Walk;
        count = 3;
        return PathAnswer.Found;
    }
}

/// <summary>
/// One brain's walker (<see cref="HordeRoute"/>): the room route until the view and the paths
/// name one ready graph, the path then; reuse while the goal stays put; the search budget; the
/// fallback for an unreachable goal; a jump's corner.
/// </summary>
public sealed class HordeRouteTests
{
    private static readonly AiVector From = Centre(0), Goal = Centre(2);

    private static SelfState View(int epoch, ClearanceClass cls = ClearanceClass.Stand) =>
        new(From, default, default, 100, 100, 0, 0, 0, SelfFlags.Alive, AltAmmo: epoch, Grenades: (int)cls);

    [Fact]
    public void Before_the_view_names_a_ready_graph_the_walker_takes_the_room_route_and_asks_no_path()
    {
        var paths = new FakePaths();
        var route = new HordeRoute(Row(4, markers: 0), paths);

        var move = route.Follow(new Tick(120), View(0), From, Goal, MoveSpeed.Run);

        Assert.Equal(RoomRoute.Follow(Row(4, markers: 0), From, Goal, MoveSpeed.Run), move);
        Assert.Equal(0, paths.Searches);
        Assert.Equal(1, route.ByRooms);
    }

    [Fact]
    public void A_view_whose_epoch_is_not_the_paths_graph_takes_the_room_route()
    {
        var paths = new FakePaths { Epoch = 5 };
        var route = new HordeRoute(Row(4, markers: 0), paths);

        route.Follow(new Tick(120), View(6), From, Goal, MoveSpeed.Run);

        Assert.Equal(0, paths.Searches);
        Assert.Equal(1, route.ByRooms);
    }

    [Fact]
    public void With_the_graph_ready_the_move_is_the_paths_corners_for_the_views_class()
    {
        var paths = new FakePaths();
        var route = new HordeRoute(Row(4, markers: 0), paths);

        var move = route.Follow(new Tick(120), View(77, ClearanceClass.Hull4), From, Goal, MoveSpeed.Run);

        Assert.Equal(MoveKind.Follow, move.Kind);
        Assert.Equal(3, move.CornerCount);
        Assert.Equal(From + new AiVector(0f, 200f, 0f), move.Corner(0));
        Assert.Equal(Goal, move.Corner(2));
        Assert.Equal(ClearanceClass.Hull4, paths.Asked.Single().Class);
        Assert.Equal(1, route.OnGraph);
    }

    [Fact]
    public void A_goal_that_moves_less_than_the_reuse_distance_keeps_the_path_and_its_last_corner_follows()
    {
        var paths = new FakePaths();
        var route = new HordeRoute(Row(4, markers: 0), paths);
        route.Follow(new Tick(120), View(77), From, Goal, MoveSpeed.Run);

        var moved = Goal + new AiVector(HordeRoute.ReuseUnits - 10f, 0f, 0f);
        var move = route.Follow(new Tick(120 + 10 * HordeRoute.SearchEveryTicks), View(77), From, moved, MoveSpeed.Run);

        Assert.Equal(1, paths.Searches);
        Assert.Equal(moved, move.Corner(move.CornerCount - 1));
    }

    [Fact]
    public void A_goal_that_moves_farther_is_planned_again_at_the_brains_next_turn()
    {
        var paths = new FakePaths();
        var route = new HordeRoute(Row(4, markers: 0), paths);
        route.Follow(new Tick(120), View(77), From, Goal, MoveSpeed.Run);
        var moved = Goal + new AiVector(0f, HordeRoute.ReuseUnits + 10f, 0f);

        // A decide that is not its turn: no search; the room route meanwhile.
        route.Follow(new Tick(120 + HordeRoute.SearchEveryTicks - DecideSchedule.Period), View(77), From, moved, MoveSpeed.Run);
        Assert.Equal(1, paths.Searches);
        Assert.Equal(1, route.ByRooms);

        route.Follow(new Tick(120 + HordeRoute.SearchEveryTicks), View(77), From, moved, MoveSpeed.Run);
        Assert.Equal(2, paths.Searches);
    }

    [Fact]
    public void An_unreachable_goal_takes_the_room_route_and_is_not_asked_again_before_the_brains_next_turn()
    {
        var paths = new FakePaths { Answer = PathAnswer.Unreachable };
        var route = new HordeRoute(Row(4, markers: 0), paths);

        for (int t = 0; t < HordeRoute.SearchEveryTicks; t += DecideSchedule.Period)
        {
            var move = route.Follow(new Tick(120 + t), View(77), From, Goal, MoveSpeed.Run);
            Assert.Equal(RoomRoute.Follow(Row(4, markers: 0), From, Goal, MoveSpeed.Run), move);
        }

        Assert.Equal(1, paths.Searches);
    }

    [Fact]
    public void A_corner_reached_by_a_jump_presses_jump_near_its_take_off()
    {
        var paths = new FakePaths { FirstArrive = NavEdgeType.Jump };
        var route = new HordeRoute(Row(4, markers: 0), paths);

        Assert.Equal(MoveKind.Jump, route.Follow(new Tick(120), View(77), From, Goal, MoveSpeed.Run).Kind);
    }

    [Fact]
    public void A_corner_the_body_has_reached_is_passed()
    {
        var paths = new FakePaths();
        var route = new HordeRoute(Row(4, markers: 0), paths);
        route.Follow(new Tick(120), View(77), From, Goal, MoveSpeed.Run);

        var atFirst = From + new AiVector(0f, 200f - HordeRoute.PassUnits + 5f, 0f);
        var move = route.Follow(new Tick(126), View(77) with { Origin = atFirst }, atFirst, Goal, MoveSpeed.Run);

        Assert.Equal(2, move.CornerCount);
        Assert.Equal(new AiVector(Goal.X, From.Y + 200f, From.Z), move.Corner(0));
    }
}

/// <summary>
/// The search budget over a whole pool (plan_descent_logic 3.7): 48 robots on the depth 1 seed 1
/// level chase a hero running up and down the drifts, every one asking its route at every
/// decide, and no tick runs more searches than the walkers' interval allows.
/// </summary>
public sealed class HordePathBudgetTests
{
    [Fact]
    public void Forty_eight_robots_chasing_every_decide_never_search_more_than_twice_a_tick()
    {
        var level = HordeNavDataTests.FixtureLevel;
        using var paths = new TF2.Descent.Server.Horde.Nav.HordePaths();
        paths.Ready(HordeNavDataTests.Nav, level);
        var rig = new BrainRig(level, register: d => TF2.Descent.Ai.Families.HordeFamilies.Register(d, TF2.Descent.Ai.Families.FamilyNumbers.For(TheRules)),
                               paths: paths);
        for (int r = 0; r < 48; r++)
            rig.Lend(rig.Robot(At(2480 + 40 * (r % 5), 3200 + 55 * r, 16), r % 2 == 0 ? "spy" : "soldier", seed: (ulong)(100 + r)), yaw: 90f);

        int most = 0;
        long last = paths.Searches;
        for (int t = 0; t < 600; t++)
        {
            rig.Hero(1, At(2560, 6500f - 3000f * MathF.Abs(MathF.Sin(t * 0.01f)), 16));
            rig.Tick();
            most = Math.Max(most, (int)(paths.Searches - last));
            last = paths.Searches;
        }

        int cap = (48 + HordeRoute.SearchEveryTicks - 1) / HordeRoute.SearchEveryTicks;
        Assert.True(paths.Searches > 48, $"the control: only {paths.Searches} searches in 600 ticks");
        Assert.True(most <= cap, $"{most} searches in one tick, over the cap of {cap}");
    }
}


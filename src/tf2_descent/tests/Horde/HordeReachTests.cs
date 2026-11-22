using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Server.Horde.Nav;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>Paths a fact scripts: each search answers the next leg in the list, its corners as given.</summary>
internal sealed class ScriptedPaths(params (PathAnswer Answer, AiVector[] Corners, bool Truncated)[] legs) : IHordePaths
{
    private int _next;

    public int Epoch => 1;

    public List<AiVector> From { get; } = [];

    public PathAnswer Find(AiVector from, AiVector to, ClearanceClass cls, int budget, Span<AiVector> corners, Span<NavEdgeType> arrive,
                           out int count, out bool truncated)
    {
        From.Add(from);
        var (answer, legCorners, legTruncated) = legs[Math.Min(_next++, legs.Length - 1)];
        count = legCorners.Length;
        truncated = legTruncated;
        for (int i = 0; i < count; i++)
        {
            corners[i] = legCorners[i];
            arrive[i] = NavEdgeType.Walk;
        }

        return answer;
    }
}

/// <summary>A walk along the brains' planner (<see cref="HordeReach"/>): the dev reach command's measure.</summary>
public sealed class HordeReachTests
{
    private static readonly AiVector Start = new(0f, 0f, 0f), Goal = new(300f, 400f, 0f);

    [Fact]
    public void A_found_untruncated_path_reaches_the_goal_in_one_leg_its_length_summed()
    {
        var paths = new ScriptedPaths((PathAnswer.Found, [new AiVector(300f, 0f, 0f), Goal], false));

        var walk = HordeReach.Walk(paths, Start, Goal, ClearanceClass.Stand);

        Assert.True(walk.Reached);
        Assert.Equal(1, walk.Legs);
        Assert.Equal(700f, walk.Length, 1);
    }

    [Fact]
    public void A_truncated_path_is_searched_again_from_its_last_corner()
    {
        var mid = new AiVector(300f, 0f, 0f);
        var paths = new ScriptedPaths((PathAnswer.Found, [mid], true), (PathAnswer.Found, [Goal], false));

        var walk = HordeReach.Walk(paths, Start, Goal, ClearanceClass.Stand);

        Assert.True(walk.Reached);
        Assert.Equal(2, walk.Legs);
        Assert.Equal(mid, paths.From[1]);
    }

    [Fact]
    public void A_goal_off_the_class_floor_stops_the_walk_unreached_at_the_nearest_place()
    {
        var near = new AiVector(300f, 0f, 0f);
        var paths = new ScriptedPaths((PathAnswer.Nearest, [near], false));

        var walk = HordeReach.Walk(paths, Start, Goal, (ClearanceClass)5);

        Assert.False(walk.Reached);
        Assert.Equal(PathAnswer.Nearest, walk.Answer);
        Assert.Equal(near, walk.End);
    }

    [Fact]
    public void A_truncated_way_to_the_nearest_place_is_followed_to_its_end_and_stops_unreached()
    {
        var mid = new AiVector(300f, 0f, 0f);
        var near = new AiVector(300f, 300f, 0f);
        var paths = new ScriptedPaths((PathAnswer.Nearest, [mid], true), (PathAnswer.Nearest, [near], false));

        var walk = HordeReach.Walk(paths, Start, Goal, ClearanceClass.Stand);

        Assert.False(walk.Reached);
        Assert.Equal(2, walk.Legs);
        Assert.Equal(near, walk.End);
    }

    [Fact]
    public void A_truncated_leg_that_gets_nowhere_stops_the_walk()
    {
        var paths = new ScriptedPaths((PathAnswer.Found, [new AiVector(2f, 0f, 0f)], true));

        var walk = HordeReach.Walk(paths, Start, Goal, ClearanceClass.Stand);

        Assert.False(walk.Reached);
        Assert.Equal(1, walk.Legs);
    }

    [Fact]
    public void A_walk_that_never_ends_stops_after_its_legs()
    {
        int x = 0;
        var legs = Enumerable.Range(0, 10).Select(_ => (PathAnswer.Found, new[] { new AiVector(x += 100, 0f, 0f) }, true)).ToArray();
        var paths = new ScriptedPaths(legs);

        var walk = HordeReach.Walk(paths, Start, Goal, ClearanceClass.Stand, maxLegs: 4);

        Assert.False(walk.Reached);
        Assert.Equal(4, walk.Legs);
        Assert.Equal(4, paths.From.Count);
    }

    [Fact]
    public void Every_class_a_robot_plans_by_is_walked_smallest_first()
    {
        var classes = HordeReach.Classes().ToArray();

        Assert.Equal(1 + HordeHulls.Scales.Length, classes.Length);
        Assert.Equal(ClearanceClass.Stand, classes[0]);
        Assert.Equal(HordeHulls.ClassOf(1.9f), classes[^1]);
    }
}

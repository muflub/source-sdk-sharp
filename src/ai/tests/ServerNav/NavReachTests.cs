using SourceSharp.Ai.Server.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// <see cref="NavReach"/>: the cells a player reaches from the ways in, by the graph's edges and
/// then, as a bound, by blast jumps. The graphs are platforms built by hand: a row of 64-unit
/// cells joined by walks, each platform its own joined-up part, in free space wherever a fact
/// does not put a wall.
/// </summary>
public sealed class NavReachTests
{
    private sealed class Space(Func<AiVector, bool> free) : IVoxelSpace
    {
        public bool Free(AiVector p) => free(p);
    }

    private static readonly IVoxelSpace Open = new Space(_ => true);

    /// <summary>Platforms, each a row of <paramref name="cells"/> cells along x from its start, at its floor; answers the builder and each platform's first cell.</summary>
    private static (NavGraph Graph, int[] First) Platforms(int cells, params (float X, float Z)[] platforms)
    {
        var b = new NavGraphBuilder();
        var first = new int[platforms.Length];
        for (int p = 0; p < platforms.Length; p++)
        {
            first[p] = b.CellCount;
            for (int i = 0; i < cells; i++)
            {
                float x = platforms[p].X + i * 64f;
                CellId c = b.AddCell(new NavCell(x, 0f, x + 64f, 64f, platforms[p].Z, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
                if (i > 0)
                    b.AddBoth(new CellId(c.Index - 1), c, NavEdgeType.Walk);
            }
        }

        return (b.Build(), first);
    }

    private static int Count(bool[] m) => m.Count(x => x);

    [Fact]
    public void TheEdgesReachTheirOwnPlatformAndNoOther()
    {
        (NavGraph g, int[] first) = Platforms(3, (0f, 0f), (1000f, 0f));
        bool[] r = NavReach.From(g, [first[0]]);
        Assert.Equal(3, Count(r));
        Assert.False(r[first[1]]);
    }

    [Fact]
    public void AStopIsReachedButNothingIsReachedThroughIt()
    {
        (NavGraph g, int[] first) = Platforms(3, (0f, 0f));
        var stops = new bool[g.CellCount];
        stops[first[0] + 1] = true;
        bool[] r = NavReach.From(g, [first[0]], stops: stops);
        Assert.Equal([true, true, false], r);
    }

    [Fact]
    public void ABlastJumpReachesAPlatformAcrossAGapWithinItsBound()
    {
        (NavGraph g, int[] first) = Platforms(3, (0f, 0f), (600f, 300f));
        bool[] r = NavReach.From(g, [first[0]], new BlastBound(580f, 1040f), Open);
        Assert.Equal(6, Count(r));
    }

    [Fact]
    public void APlatformBeyondTheRangeIsNotReached()
    {
        (NavGraph g, int[] first) = Platforms(2, (0f, 0f), (2000f, 0f));
        Assert.Equal(2, Count(NavReach.From(g, [first[0]], new BlastBound(580f, 1040f), Open)));
    }

    [Fact]
    public void APlatformHigherThanTheRiseIsNotReached()
    {
        (NavGraph g, int[] first) = Platforms(2, (0f, 0f), (300f, 700f));
        Assert.Equal(2, Count(NavReach.From(g, [first[0]], new BlastBound(580f, 1040f), Open)));
    }

    [Fact]
    public void APlatformBehindAWallToTheCeilingIsNotReached()
    {
        (NavGraph g, int[] first) = Platforms(2, (0f, 0f), (600f, 0f));
        var walled = new Space(p => p.X < 400f || p.X > 432f);
        Assert.Equal(2, Count(NavReach.From(g, [first[0]], new BlastBound(580f, 1040f), walled)));
    }

    [Fact]
    public void APlatformOverALowWallIsReached()
    {
        (NavGraph g, int[] first) = Platforms(2, (0f, 0f), (600f, 0f));
        var low = new Space(p => p.X < 400f || p.X > 432f || p.Z > 200f);
        Assert.Equal(4, Count(NavReach.From(g, [first[0]], new BlastBound(580f, 1040f), low)));
    }

    [Fact]
    public void APlatformReachedByABlastJumpLeadsOnToTheNext()
    {
        (NavGraph g, int[] first) = Platforms(2, (0f, 0f), (700f, 400f), (1400f, 800f));
        Assert.Equal(6, Count(NavReach.From(g, [first[0]], new BlastBound(580f, 1040f), Open)));
    }
}

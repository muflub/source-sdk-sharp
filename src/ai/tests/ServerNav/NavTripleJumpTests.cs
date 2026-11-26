using SourceSharp.Ai.Server.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// The triple jump in the graph (round 2's S2): a rise past the double jump's and up to the numbers' third jump is a
/// <see cref="NavEdgeType.TripleJump"/>, taken only by an agent with <see cref="JumpReach.Triple"/>; numbers without a third
/// jump derive the graphs they did.
/// </summary>
public class NavTripleJumpTests
{
    /// <summary>TF's numbers: a double jump to 97.2, a triple to 142.2.</summary>
    private static readonly NavNumbers TripleJumper = NavNumbers.Stock(56f) with { MaxDoubleJumpRise = 97.2f, MaxTripleJumpRise = 142.2f };

    private static (FakeNavSource Src, int Floor, int Mid, int High, int Beyond) Course()
    {
        var s = new FakeNavSource();
        int floor = s.Add(0, 0, 0);
        int mid = s.Add(0, 1, 90);
        int high = s.Add(1, 0, 120);
        int beyond = s.Add(-1, 0, 150);
        s.JumpLink(floor, mid);
        s.JumpLink(floor, high);
        s.JumpLink(floor, beyond);
        return (s, floor, mid, high, beyond);
    }

    private static NavEdgeType? EdgeType(DerivedNav d, int fromLeaf, int toLeaf)
        => d.Graph.TryEdge(d.CellOfLeaf[fromLeaf], d.CellOfLeaf[toLeaf], out NavEdge e) ? e.Type : null;

    [Fact]
    public void ARiseOverTheDoubleJumpsAndUnderTheTriplesIsATripleJump()
    {
        var (src, floor, mid, high, beyond) = Course();
        DerivedNav d = NavGraphFromNav3d.Derive(src, TripleJumper, NavEdgeCosts.Default, []);
        Assert.Equal((NavEdgeType.DoubleJump, NavEdgeType.TripleJump, (NavEdgeType?)null),
                     (EdgeType(d, floor, mid), EdgeType(d, floor, high), EdgeType(d, floor, beyond)));
    }

    /// <summary>The same course under numbers with no third jump: the 120 u rise is no move, and the graph is the one it was.</summary>
    [Fact]
    public void NumbersWithoutATripleJumpDeriveNoTripleJump()
    {
        var (src, floor, mid, high, _) = Course();
        DerivedNav d = NavGraphFromNav3d.Derive(src, TripleJumper with { MaxTripleJumpRise = 0f }, NavEdgeCosts.Default, []);
        Assert.Equal((NavEdgeType.DoubleJump, (NavEdgeType?)null), (EdgeType(d, floor, mid), EdgeType(d, floor, high)));
        DerivedNav with = NavGraphFromNav3d.Derive(Course().Src, TripleJumper, NavEdgeCosts.Default, []);
        Assert.Equal(d.Graph.EdgeCount + 1, with.Graph.EdgeCount);
    }

    [Fact]
    public void ATripleJumpCostsItsOwnExtra()
    {
        float Cost(float extra)
        {
            var (src, floor, _, high, _) = Course();
            DerivedNav d = NavGraphFromNav3d.Derive(src, TripleJumper, NavEdgeCosts.Default with { TripleJump = extra }, []);
            Assert.True(d.Graph.TryEdge(d.CellOfLeaf[floor], d.CellOfLeaf[high], out NavEdge e));
            return e.Cost;
        }

        Assert.Equal(36f, Cost(100f) - Cost(64f), 3);
    }

    /// <summary>Only an agent with the third jump routes up it: the double jump alone does not take it.</summary>
    [Theory]
    [InlineData(JumpReach.None, false)]
    [InlineData(JumpReach.Double, false)]
    [InlineData(JumpReach.Double | JumpReach.Triple, true)]
    public void OnlyAnAgentWithTheTripleReachRoutesUpATripleJump(JumpReach reach, bool takes)
    {
        var (src, floor, _, high, _) = Course();
        DerivedNav d = NavGraphFromNav3d.Derive(src, TripleJumper, NavEdgeCosts.Default, []);
        var field = new FlowField();
        field.Build(d.Graph, new CellId(d.CellOfLeaf[high]), ClearanceClass.Crouch, new NavObstacleState(d.Graph.ObstacleCount) { Reach = reach });
        Assert.Equal(takes, field.ViaFrom(d.CellOfLeaf[floor]) == NavEdgeType.TripleJump);
        Assert.Equal(takes, float.IsFinite(field.CostAt(d.CellOfLeaf[floor])));
    }

    [Fact]
    public void ATripleJumpsExtraIsItsOwnInTheDefaultCosts()
        => Assert.Equal((64f, 48f), (NavEdgeCosts.Default.ExtraFor(NavEdgeType.TripleJump), NavEdgeCosts.Default.ExtraFor(NavEdgeType.DoubleJump)));

    /// <summary>
    /// A triple jump needs the open space over its take-off to hold a crouched body (62) with its feet at the rise: from under
    /// a ceiling 150 u up a 120 u rise is the storey above, no move; with the ceiling at 200 it is a triple jump. The open
    /// space runs on through the leaves touching from above (the column's run, as a nav3d splits it).
    /// </summary>
    [Theory]
    [InlineData(150f, false)]
    [InlineData(200f, true)]
    public void ATripleJumpNeedsTheRoomOverItsTakeOff(float ceiling, bool jumps)
    {
        var s = new FakeNavSource();
        int floor = s.Add(0, 0, 0, ceiling: 100f);
        int over = s.Add(0, 0, 100f, ceiling: ceiling, walkable: false);
        s.Stack(floor, over);
        int high = s.Add(1, 0, 120);
        DerivedNav d = NavGraphFromNav3d.Derive(s, TripleJumper, NavEdgeCosts.Default, []);
        Assert.Equal(jumps ? NavEdgeType.TripleJump : null, EdgeType(d, floor, high));
    }
}

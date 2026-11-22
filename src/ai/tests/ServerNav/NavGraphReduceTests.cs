using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// <see cref="NavGraphReduce"/>: a derived graph made smaller after the derivation. The graphs
/// are a floor of 16-unit columns, derived from a hand-built source (8 by 8) or built by hand
/// (6 by 6, a walk each way between columns side by side).
/// </summary>
public sealed class NavGraphReduceTests
{
    private static DerivedNav Floor(int n, Action<FakeNavSource, int[,]>? change = null)
    {
        var src = new FakeNavSource();
        var at = new int[n, n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
                at[x, y] = -1;
        }

        change?.Invoke(src, at);
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                if (at[x, y] < 0)
                    at[x, y] = src.Add(x, y, 0f, 200f);
            }
        }

        return NavGraphFromNav3d.Derive(src, TFNavProfile.ModNumbers, NavEdgeCosts.Default, []);
    }

    /// <summary>A grid built by hand: cell y × n + x at column (x, y), floor 0, a walk each way to the columns beside.</summary>
    private static (NavGraphBuilder Builder, int N) Grid(int n)
    {
        var b = new NavGraphBuilder();
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
                b.AddCell(new NavCell(x * 16f, y * 16f, x * 16f + 16f, y * 16f + 16f, 0f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
        }

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                if (x + 1 < n)
                    b.AddBoth(new CellId(y * n + x), new CellId(y * n + x + 1), NavEdgeType.Walk);
                if (y + 1 < n)
                    b.AddBoth(new CellId(y * n + x), new CellId((y + 1) * n + x), NavEdgeType.Walk);
            }
        }

        return (b, n);
    }

    private static DerivedNav Of(NavGraphBuilder b, int cells)
    {
        int[] identity = [.. Enumerable.Range(0, cells)];
        return new DerivedNav(b.Build(), identity, [.. identity], [], cells);
    }

    [Fact]
    public void KeepLeavesOutTheCellsNotMarkedAndTheirEdges()
    {
        (NavGraphBuilder b, int n) = Grid(3);
        DerivedNav d = Of(b, n * n);
        bool[] keep = [.. Enumerable.Range(0, 9).Select(c => c != 4)];
        DerivedNav kept = NavGraphReduce.Keep(d, keep, NavEdgeCosts.Default);
        Assert.Equal(8, kept.Graph.CellCount);
        Assert.Equal(d.Graph.EdgeCount - 8, kept.Graph.EdgeCount);
        Assert.Equal(-1, kept.CellOfLeaf[4]);
        Assert.Equal(4, kept.CellOfLeaf[5]);
    }

    [Fact]
    public void ACellIsFlatWhenTheEightColumnsAroundHoldItsFloor()
    {
        DerivedNav d = Floor(8);
        Assert.Equal(36, NavGraphReduce.Flat(d, 16f).Count(f => f));
    }

    [Fact]
    public void ACellBesideAStepIsNotFlat()
    {
        DerivedNav d = Floor(8, (src, at) => at[4, 4] = src.Add(4, 4, 8f, 200f));
        Assert.Equal(36 - 9, NavGraphReduce.Flat(d, 16f).Count(f => f));
    }

    [Fact]
    public void ABlockOfFlatColumnsIsOneCellAndTheEdgeCellsStay()
    {
        DerivedNav d = Floor(8);
        DerivedNav k2 = NavGraphReduce.Coarsen(d, 16f, 2, NavEdgeCosts.Default);
        Assert.Equal(64 - 16 + 4, k2.Graph.CellCount);
        Assert.Equal(4, Enumerable.Range(0, k2.Graph.CellCount).Count(c => k2.Graph.Cell(c).MaxX - k2.Graph.Cell(c).MinX == 32f && k2.Graph.Cell(c).MaxY - k2.Graph.Cell(c).MinY == 32f));
    }

    [Fact]
    public void AMergedFloorIsStillOneJoinedUpPart()
    {
        DerivedNav k4 = NavGraphReduce.Coarsen(Floor(12), 16f, 4, NavEdgeCosts.Default);
        Assert.True(k4.Graph.CellCount < 144);
        Assert.Single(Enumerable.Range(0, k4.Graph.CellCount).Select(k4.Graph.ComponentOf).Distinct());
    }

    [Fact]
    public void EveryLeafOfAMergedBlockAnswersItsCell()
    {
        DerivedNav d = Floor(8);
        DerivedNav k2 = NavGraphReduce.Coarsen(d, 16f, 2, NavEdgeCosts.Default);
        int merged = k2.CellOfLeaf[d.LeafOfCell[d.Graph.FindCell(new AiVector(40, 40, 8)).Index]];
        foreach ((float x, float y) in (ReadOnlySpan<(float, float)>)[(40, 40), (56, 40), (40, 56), (56, 56)])
            Assert.Equal(merged, k2.CellOfLeaf[d.LeafOfCell[d.Graph.FindCell(new AiVector(x, y, 8)).Index]]);
    }

    [Fact]
    public void AMergedCellsEdgeToANeighbourIsAWalkWhereAMemberWalksThere()
    {
        (NavGraphBuilder b, int n) = Grid(6);
        b.AddEdge(new CellId(2 * n + 2), new CellId(2 * n + 4), NavEdgeType.Jump, NavEdgeCosts.Default.Jump);
        DerivedNav d = Of(b, n * n);
        DerivedNav k2 = NavGraphReduce.Coarsen(d, 16f, 2, NavEdgeCosts.Default);
        int block = k2.CellOfLeaf[2 * n + 2], east = k2.CellOfLeaf[2 * n + 4];
        Assert.NotEqual(k2.CellOfLeaf[2 * n + 2], k2.CellOfLeaf[2 * n + 1]);
        Assert.True(k2.Graph.TryEdge(block, east, out NavEdge e));
        Assert.Equal(NavEdgeType.Walk, e.Type);
    }

    [Fact]
    public void AMergedCellsJumpCostsItsNewLengthAndTheJumpsExtra()
    {
        (NavGraphBuilder b, int n) = Grid(6);
        b.AddEdge(new CellId(3 * n + 3), new CellId(5 * n + 2), NavEdgeType.Jump, NavEdgeCosts.Default.Jump);
        DerivedNav d = Of(b, n * n);
        DerivedNav k2 = NavGraphReduce.Coarsen(d, 16f, 2, NavEdgeCosts.Default);
        int block = k2.CellOfLeaf[3 * n + 3], far = k2.CellOfLeaf[5 * n + 2];
        Assert.Equal(block, k2.CellOfLeaf[2 * n + 2]);
        Assert.True(k2.Graph.TryEdge(block, far, out NavEdge e));
        Assert.Equal(NavEdgeType.Jump, e.Type);
        Assert.Equal(k2.Graph.Cell(block).Centre.DistanceTo(k2.Graph.Cell(far).Centre) + NavEdgeCosts.Default.Jump, e.Cost, 3);
    }

    [Fact]
    public void AMergedGraphReadsBackFromTheCacheAsTheSameGraph()
    {
        DerivedNav k2 = NavGraphReduce.Coarsen(Floor(8), 16f, 2, NavEdgeCosts.Default);
        var cache = new NavGraphCache(Path.GetTempPath(), NavEdgeCosts.Default);
        DerivedNav back = cache.Decode(cache.Encode("m", "b", "n", k2), "m", "b", "n");
        Assert.Equal(k2.Graph.Hash, back.Graph.Hash);
    }
}

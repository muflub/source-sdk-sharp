using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// <see cref="NavRegionSource"/>: one region of another source, derived by the game's own
/// derivation unchanged. Over a hand-built source: a row of six floor columns at y 0, a column
/// stacked over the second, a jump link from the first to the sixth, and a door on the third and
/// fourth. Region A is the first three and the stacked one, region B the last two, and the fourth
/// is in neither, so the two do not touch.
/// </summary>
public sealed class NavRegionSourceTests
{
    private static readonly NavNumbers Numbers = TFNavProfile.ModNumbers;

    private sealed record Row(FakeNavSource Source, int[] Floor, int Upper, int Door);

    private static Row Build()
    {
        var src = new FakeNavSource();
        int[] floor = [.. Enumerable.Range(0, 6).Select(x => src.Add(x, 0, 0f, 100f))];
        int upper = src.Add(1, 0, 100f, 300f);
        src.Stack(floor[1], upper);
        src.JumpLink(floor[0], floor[5]);
        int door = src.Door("gate", floor[2], floor[3]);
        return new Row(src, floor, upper, door);
    }

    private static int[] RegionA(Row r) => [r.Floor[0], r.Floor[1], r.Floor[2], r.Upper];

    private static int[] RegionB(Row r) => [r.Floor[4], r.Floor[5]];

    private static DerivedNav Derive(INavSource src) => NavGraphFromNav3d.Derive(src, Numbers, NavEdgeCosts.Default, []);

    /// <summary>The other source's leaves the derived graph has cells on.</summary>
    private static int[] CellLeaves(DerivedNav d, NavRegionSource region)
        => [.. d.LeafOfCell.Select(region.InnerLeaf).Order()];

    [Fact]
    public void ARegionOfEveryLeafDerivesTheSourcesOwnGraph()
    {
        Row r = Build();
        DerivedNav whole = Derive(r.Source);
        Assert.True(whole.Graph.CellCount > 0 && whole.Graph.EdgeCount > 0);
        int[] shuffled = [.. Enumerable.Range(0, r.Source.LeafCount).Reverse(), 3, 0];
        Assert.Equal(whole.Graph.Hash, Derive(NavRegionSource.Over(r.Source, shuffled)).Graph.Hash);
    }

    [Fact]
    public void ARegionOverAScratchAnswersAsOneWithoutIt()
    {
        Row r = Build();
        var scratch = NavRegionScratch.For(r.Source);
        foreach (int[] leaves in (int[][])[RegionA(r), RegionB(r), [.. Enumerable.Range(0, r.Source.LeafCount)]])
        {
            NavRegionSource plain = NavRegionSource.Over(r.Source, leaves), fast = NavRegionSource.Over(scratch, leaves);
            Assert.Equal(Derive(plain).Graph.Hash, Derive(fast).Graph.Hash);
            var a = new int[8];
            var b = new int[8];
            for (int o = 0; o < plain.ObstacleCount; o++)
                Assert.Equal(a.AsSpan(0, plain.ObstacleLeaves(o, a)).ToArray(), b.AsSpan(0, fast.ObstacleLeaves(o, b)).ToArray());
            for (int leaf = 0; leaf < r.Source.LeafCount; leaf++)
                Assert.Equal(plain.LeafOf(leaf), fast.LeafOf(leaf));
            fast.Release();
        }
    }

    [Fact]
    public void AScratchServesOneRegionAtATime()
    {
        Row r = Build();
        var scratch = NavRegionScratch.For(r.Source);
        NavRegionSource a = NavRegionSource.Over(scratch, RegionA(r));
        Assert.Throws<InvalidOperationException>(() => NavRegionSource.Over(scratch, RegionB(r)));
        a.Release();
        NavRegionSource b = NavRegionSource.Over(scratch, RegionB(r));

        // Region A's leaves were given back: region B does not hold them.
        Assert.All(RegionA(r), leaf => Assert.Equal(-1, b.LeafOf(leaf)));
        Assert.Equal(0, b.LeafOf(r.Floor[4]));
    }

    [Fact]
    public void AForkedScratchServesAnotherRegionAtTheSameTime()
    {
        Row r = Build();
        var scratch = NavRegionScratch.For(r.Source);
        NavRegionSource a = NavRegionSource.Over(scratch, RegionA(r)), b = NavRegionSource.Over(scratch.Fork(), RegionB(r));
        Assert.Equal(0, a.LeafOf(r.Floor[0]));
        Assert.Equal(-1, a.LeafOf(r.Floor[4]));
        Assert.Equal(0, b.LeafOf(r.Floor[4]));
        Assert.Equal(-1, b.LeafOf(r.Floor[0]));
    }

    [Fact]
    public void TwoRegionsThatDoNotTouchDeriveOnlyTheirOwnCells()
    {
        Row r = Build();
        NavRegionSource a = NavRegionSource.Over(r.Source, RegionA(r)), b = NavRegionSource.Over(r.Source, RegionB(r));
        Assert.Equal(RegionA(r).Order(), CellLeaves(Derive(a), a));
        Assert.Equal(RegionB(r).Order(), CellLeaves(Derive(b), b));
    }

    [Fact]
    public void LeavesAreNumberedInTheSourcesOrder()
    {
        Row r = Build();
        NavRegionSource region = NavRegionSource.Over(r.Source, [r.Floor[5], r.Floor[1], r.Floor[3], r.Floor[1]]);
        Assert.Equal([r.Floor[1], r.Floor[3], r.Floor[5]], Enumerable.Range(0, region.LeafCount).Select(region.InnerLeaf));
        Assert.Equal(-1, region.LeafOf(r.Floor[4]));
    }

    [Fact]
    public void ALeafOutsideTheSourceIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => NavRegionSource.Over(Build().Source, [0, 99]));

    [Fact]
    public void AColumnBesideOutsideTheRegionAnswersNoLeaves()
    {
        Row r = Build();
        Span<int> into = new int[8];
        Assert.Equal(1, r.Source.ColumnLeaves(r.Floor[2], 1, 0, into));
        NavRegionSource a = NavRegionSource.Over(r.Source, RegionA(r));
        Assert.Equal(0, a.ColumnLeaves(a.LeafOf(r.Floor[2]), 1, 0, into));
    }

    [Fact]
    public void AColumnBesideAnswersItsLeavesInTheRegionRenumbered()
    {
        Row r = Build();
        Span<int> into = new int[8];
        NavRegionSource a = NavRegionSource.Over(r.Source, RegionA(r));
        int count = a.ColumnLeaves(a.LeafOf(r.Floor[0]), 1, 0, into);
        Assert.Equal([a.LeafOf(r.Floor[1]), a.LeafOf(r.Upper)], into[..count].ToArray());
    }

    [Fact]
    public void ALeafTouchingFromAboveOutsideTheRegionIsNone()
    {
        Row r = Build();
        Assert.Equal(r.Upper, r.Source.Above(r.Floor[1]));
        NavRegionSource floorOnly = NavRegionSource.Over(r.Source, [r.Floor[1]]);
        Assert.Equal(-1, floorOnly.Above(0));
        NavRegionSource upperOnly = NavRegionSource.Over(r.Source, [r.Upper]);
        Assert.Equal(-1, upperOnly.Below(0));
    }

    [Fact]
    public void AJumpLinkToALeafOutsideTheRegionIsNotListed()
    {
        Row r = Build();
        Span<NavJumpLink> into = new NavJumpLink[8];
        Assert.Equal(1, r.Source.Jumps(r.Floor[0], into));
        NavRegionSource a = NavRegionSource.Over(r.Source, RegionA(r));
        Assert.Equal(0, a.Jumps(a.LeafOf(r.Floor[0]), into));
        NavRegionSource ends = NavRegionSource.Over(r.Source, [r.Floor[0], r.Floor[5]]);
        Assert.Equal(1, ends.Jumps(ends.LeafOf(r.Floor[0]), into));
        Assert.Equal(ends.LeafOf(r.Floor[5]), into[0].Leaf);
    }

    [Fact]
    public void AnObstacleNamesOnlyItsLeavesInTheRegion()
    {
        Row r = Build();
        NavRegionSource a = NavRegionSource.Over(r.Source, RegionA(r));
        Span<int> into = new int[8];
        Assert.Equal(r.Source.ObstacleCount, a.ObstacleCount);
        Assert.Equal("gate", a.Obstacle(r.Door).Name);
        int count = a.ObstacleLeaves(r.Door, into);
        Assert.Equal([a.LeafOf(r.Floor[2])], into[..count].ToArray());
    }
}

/// <summary>
/// <see cref="NavRegionSource"/> over this tree's hull navigation of ss_ai_tf_jump: the whole map as
/// a region derives the map's graph; two slabs of it that do not touch (by the columns' x, a
/// 128-unit gap between) each derive exactly the map graph's cells on their leaves and its edges
/// between them; and no answer about a slab's leaf names a leaf outside the slab.
/// </summary>
public sealed class NavRegionSourceMapTests
{
    private const string Jump = "ss_ai_tf_jump";

    private sealed record Map(NavHullNavSource Source, MapEntities Entities, DerivedNav Whole, int[] West, int[] East);

    private static readonly Lazy<Map> Loaded = new(() =>
    {
        byte[] bsp = File.ReadAllBytes(TFHullNav.BspPath(Jump)!);
        NavHullNavSource source = NavHullNavSource.Over(NavHullReader.Open(File.ReadAllBytes(TFHullNav.HullPath(Jump))));
        MapEntities entities = MapEntities.Read(bsp);
        DerivedNav whole = NavGraphFromNav3d.Derive(source, TFNavProfile.ModNumbers, NavEdgeCosts.Default, entities.Hurt, entities.LadderClimbs);
        float[] x = [.. Enumerable.Range(0, source.LeafCount).Select(l => source.Leaf(l).MinX)];
        float mid = (x.Min() + x.Max()) * 0.5f;
        int[] west = [.. Enumerable.Range(0, source.LeafCount).Where(l => x[l] < mid - 64f)];
        int[] east = [.. Enumerable.Range(0, source.LeafCount).Where(l => x[l] >= mid + 64f)];
        return new Map(source, entities, whole, west, east);
    });

    private static DerivedNav Derive(INavSource src, MapEntities e)
        => NavGraphFromNav3d.Derive(src, TFNavProfile.ModNumbers, NavEdgeCosts.Default, e.Hurt, e.LadderClimbs);

    [PerTreeHullNavFact(Jump)]
    public void TheWholeMapAsARegionDerivesTheMapsGraph()
    {
        Map m = Loaded.Value;
        Assert.True(m.Whole.Graph.CellCount > 10_000);
        DerivedNav region = Derive(NavRegionSource.Over(m.Source, Enumerable.Range(0, m.Source.LeafCount)), m.Entities);
        Assert.Equal(m.Whole.Graph.Hash, region.Graph.Hash);
    }

    [PerTreeHullNavFact(Jump)]
    public void TheWestSlabDerivesTheMapGraphsCellsAndEdgesOnIt() => SlabIsTheMapsOwn(Loaded.Value.West);

    [PerTreeHullNavFact(Jump)]
    public void TheEastSlabDerivesTheMapGraphsCellsAndEdgesOnIt() => SlabIsTheMapsOwn(Loaded.Value.East);

    /// <summary>The slab's graph, as the map's leaves and edges between them, is the map graph's on the slab's leaves; neither is empty.</summary>
    private static void SlabIsTheMapsOwn(int[] slab)
    {
        Map m = Loaded.Value;
        NavRegionSource region = NavRegionSource.Over(m.Source, slab);
        DerivedNav d = Derive(region, m.Entities);
        var inSlab = new HashSet<int>(slab);
        int[] expectedCells = [.. Enumerable.Range(0, m.Whole.Graph.CellCount).Select(c => m.Whole.LeafOfCell[c]).Where(inSlab.Contains).Order()];
        int[] actualCells = [.. d.LeafOfCell.Select(region.InnerLeaf).Order()];
        Assert.True(expectedCells.Length > 1_000, $"{expectedCells.Length} cells on the slab");
        Assert.Equal(expectedCells, actualCells);
        Assert.Equal(Edges(m.Whole, l => l, inSlab), Edges(d, region.InnerLeaf, null));
    }

    /// <summary>A graph's edges as (from leaf, to leaf, type, obstacle), sorted; with <paramref name="only"/>, those with both ends in it.</summary>
    private static List<(int, int, NavEdgeType, int)> Edges(DerivedNav d, Func<int, int> inner, HashSet<int>? only)
    {
        var edges = new List<(int, int, NavEdgeType, int)>();
        for (int c = 0; c < d.Graph.CellCount; c++)
        {
            foreach (NavEdge e in d.Graph.EdgesFrom(c))
            {
                int from = inner(d.LeafOfCell[c]), to = inner(d.LeafOfCell[e.To]);
                if (only is null || (only.Contains(from) && only.Contains(to)))
                    edges.Add((from, to, e.Type, e.Obstacle));
            }
        }

        edges.Sort();
        return edges;
    }

    [PerTreeHullNavFact(Jump)]
    public void EveryAnswerAboutASlabsLeafIsTheMapsAnswerOnTheSlab()
    {
        // Each question asked of the slab and of the map: the slab answers the map's leaves that are
        // on it, renumbered, and nothing else (so no leaf outside the slab, and none of its own lost).
        Map m = Loaded.Value;
        NavRegionSource region = NavRegionSource.Over(m.Source, m.West);
        var inSlab = new HashSet<int>(m.West);
        int[] mine = new int[512], theirs = new int[512];
        var mineJ = new NavJumpLink[512];
        var theirsJ = new NavJumpLink[512];
        long named = 0, dropped = 0;
        void Same(IEnumerable<int> slabAnswer, IEnumerable<int> mapAnswer)
        {
            int[] expected = [.. mapAnswer.Where(inSlab.Contains)];
            dropped += mapAnswer.Count() - expected.Length;
            named += expected.Length;
            Assert.Equal(expected, slabAnswer.Select(region.InnerLeaf).ToArray());
        }

        for (int leaf = 0; leaf < region.LeafCount; leaf++)
        {
            int at = region.InnerLeaf(leaf);
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                    Same(mine[..region.ColumnLeaves(leaf, dx, dy, mine)], theirs[..m.Source.ColumnLeaves(at, dx, dy, theirs)]);
            }

            Same(Some(region.Below(leaf)), Some(m.Source.Below(at)));
            Same(Some(region.Above(leaf)), Some(m.Source.Above(at)));
            Same(mineJ[..region.Jumps(leaf, mineJ)].Select(j => j.Leaf), theirsJ[..m.Source.Jumps(at, theirsJ)].Select(j => j.Leaf));
        }

        int[] slabLeaves = new int[region.LeafCount], mapLeaves = new int[m.Source.LeafCount];
        for (int o = 0; o < region.ObstacleCount; o++)
        {
            int slabCount = region.ObstacleLeaves(o, slabLeaves), mapCount = m.Source.ObstacleLeaves(o, mapLeaves);
            Same(slabLeaves[..slabCount], mapLeaves[..mapCount]);
        }

        // The stimulus reached the subject: answers were given, and the map's answers off the slab were left out.
        Assert.True(named > region.LeafCount && dropped > 0, $"{named} leaves named, {dropped} left out");
    }

    private static int[] Some(int leaf) => leaf < 0 ? [] : [leaf];
}

using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// <see cref="NavGraphReduce.DeriveCoarse"/>: the derivation and the 4 by 4 merge made a tile of
/// columns at a time (the user's Q-R5 of 2026-11-16) give the graph the whole derivation and
/// <see cref="NavGraphReduce.Coarsen"/> give, cell for cell and edge for edge, over hand-built
/// sources whose tiles are a few columns wide, so every feature sits on a tile's edge somewhere.
/// </summary>
public sealed class NavDeriveCoarseTests
{
    private static NavNumbers Numbers => TFNavProfile.ModNumbers;

    /// <summary>
    /// A 24 by 24 course: a floor, a platform up a jump with links far across the floor, a step, a
    /// pit under a hurt volume, a low roof, water, a column with a floor over the floor, a ladder
    /// and a door.
    /// </summary>
    private static (FakeNavSource Source, List<HurtVolume> Hurt) Course()
    {
        var src = new FakeNavSource();
        var at = new Dictionary<(int, int), int>();
        for (int y = 0; y < 24; y++)
        {
            for (int x = 0; x < 24; x++)
            {
                float floor = x is >= 13 and <= 18 && y is >= 2 and <= 7 ? 128f
                    : x is >= 3 and <= 9 && y is >= 15 and <= 20 ? -64f
                    : x == 20 && y >= 10 ? 16f
                    : 0f;
                float ceiling = x is >= 2 and <= 5 && y is >= 2 and <= 4 ? floor + 50f : floor + 400f;
                bool water = x is >= 10 and <= 12 && y is >= 18;
                bool ladder = x == 22 && y == 22;
                at[(x, y)] = src.Add(x, y, floor, ceiling, water: water, ladder: ladder);
            }
        }

        // A floor over the floor at (7..8, 9..10): the lower leaf's roof is the upper one's floor.
        for (int y = 9; y <= 10; y++)
        {
            for (int x = 7; x <= 8; x++)
            {
                int upper = src.Add(x, y, 200f, 500f);
                src.Stack(at[(x, y)], upper);
            }
        }

        // The platform's links: a ledge, and gaps to the far floor, across tiles.
        src.JumpLink(at[(13, 4)], at[(12, 4)]);
        src.JumpLink(at[(15, 7)], at[(15, 12)]);
        src.JumpLink(at[(18, 2)], at[(2, 22)]);
        src.JumpLink(at[(16, 5)], at[(4, 17)]);
        src.Door("door", at[(11, 0)], at[(11, 1)], at[(12, 0)], at[(12, 1)]);
        var hurt = new List<HurtVolume> { new(new AiVector(3 * 16f, 15 * 16f, -100f), new AiVector(10 * 16f, 21 * 16f, -10f)) };
        return (src, hurt);
    }

    /// <summary>A 20 by 20 floor of random heights in steps, jumps and drops, with random links, a few roofs and a hurt volume.</summary>
    private static (FakeNavSource Source, List<HurtVolume> Hurt) Random(int seed)
    {
        var rng = new System.Random(seed);
        var src = new FakeNavSource();
        var heights = new float[20, 20];
        var at = new int[20, 20];
        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                // Mostly patches of one floor, so blocks merge; the rest steps, jumps and drops.
                int patch = (x / 5) + (y / 5) * 4;
                heights[x, y] = rng.Next(10) < 8 ? (patch % 3) * 16f : rng.Next(-4, 6) * 16f;
                float ceiling = heights[x, y] + (rng.Next(12) == 0 ? 60f : 400f);
                at[x, y] = src.Add(x, y, heights[x, y], ceiling, water: rng.Next(40) == 0);
            }
        }

        for (int i = 0; i < 12; i++)
            src.JumpLink(at[rng.Next(20), rng.Next(20)], at[rng.Next(20), rng.Next(20)]);
        var hurt = new List<HurtVolume> { new(new AiVector(rng.Next(15) * 16f, rng.Next(15) * 16f, -200f), new AiVector(rng.Next(15, 20) * 16f, rng.Next(15, 20) * 16f, 32f)) };
        return (src, hurt);
    }

    private static void AssertSame(DerivedNav whole, DerivedNav tiled)
    {
        Assert.Equal(whole.Graph.CellCount, tiled.Graph.CellCount);
        Assert.Equal(whole.Graph.EdgeCount, tiled.Graph.EdgeCount);
        Assert.Equal(whole.LeafOfCell, tiled.LeafOfCell);
        Assert.Equal(whole.CellOfLeaf, tiled.CellOfLeaf);
        Assert.Equal(whole.Graph.Hash, tiled.Graph.Hash);
    }

    private static DerivedNav Whole(INavSource src, IReadOnlyList<HurtVolume> hurt, int k)
    {
        DerivedNav d = NavGraphFromNav3d.Derive(src, Numbers, NavEdgeCosts.Default, hurt);
        return NavGraphReduce.Coarsen(d, src.VoxelSize, k, NavEdgeCosts.Default, NavGraphReduce.GridOrigin(src));
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(4, 8)]
    [InlineData(4, 12)]
    [InlineData(2, 2)]
    [InlineData(2, 6)]
    [InlineData(4, 128)]
    public void TheCourseTiledIsTheWholeDerivationMerged(int k, int tile)
    {
        (FakeNavSource src, List<HurtVolume> hurt) = Course();
        DerivedNav whole = Whole(src, hurt, k);
        var stats = new CoarseDeriveStats();
        DerivedNav tiled = NavGraphReduce.DeriveCoarse(src, Numbers, NavEdgeCosts.Default, hurt, null, k, tile, stats);
        AssertSame(whole, tiled);

        // The merge happened, and the tiles were more than one where they were asked to be.
        Assert.True(tiled.Graph.CellCount < NavGraphFromNav3d.Derive(src, Numbers, NavEdgeCosts.Default, hurt).Graph.CellCount);
        Assert.Equal(tile >= 24 ? 1 : (24 + tile - 1) / tile * ((24 + tile - 1) / tile), stats.Tiles);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ARandomFloorTiledIsTheWholeDerivationMerged(int seed)
    {
        (FakeNavSource src, List<HurtVolume> hurt) = Random(seed);
        DerivedNav whole = Whole(src, hurt, 4);
        AssertSame(whole, NavGraphReduce.DeriveCoarse(src, Numbers, NavEdgeCosts.Default, hurt, null, 4, 4));
        AssertSame(whole, NavGraphReduce.DeriveCoarse(src, Numbers, NavEdgeCosts.Default, hurt, null, 4, 8));
    }

    /// <summary>
    /// A real hull file: four cells of twelve columns in a row, a floor in each, a ledge two voxels
    /// up in cell 1 and a ledge in cell 3, and jump links between the cells, one from cell 0 to
    /// cell 3 across two tiles. Its columns and links are read box by box
    /// (<see cref="NavHullNavSource.LeafColumns"/>, <see cref="NavHullNavSource.JumpPairs"/>), not
    /// leaf by leaf as any other source's.
    /// </summary>
    private static NavHullNavSource HullCourse()
    {
        var f = new HullNavFixture(4, 1, 12, 32);
        uint floor = f.Key(NavHullFlags.Grounded | NavHullFlags.Walkable, 16f, 144f, 144f);
        uint ledge = f.Key(NavHullFlags.Grounded | NavHullFlags.Walkable, 48f, 176f, 176f);
        int f0 = f.Box(0, 0, 0, 12, 12, 1, 8, floor);
        int f1 = f.Box(1, 0, 0, 7, 12, 1, 8, floor);
        int l1 = f.Box(1, 7, 0, 12, 12, 3, 6, ledge);
        int f2 = f.Box(2, 0, 0, 12, 12, 1, 8, floor);
        int f3 = f.Box(3, 0, 0, 9, 12, 1, 8, floor);
        int l3 = f.Box(3, 9, 0, 12, 12, 3, 6, ledge);
        f.Jump(f1, 6, 5, l1, 7, 5, 32f, Nav3dDirection.East, 1);
        f.Jump(f0, 11, 6, l3, 9, 6, 32f, Nav3dDirection.East, 34);
        f.Jump(l1, 8, 2, f2, 2, 2, 32f, Nav3dDirection.East, 6);
        return NavHullNavSource.Over(f.Open());
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public void AHullFileTiledIsTheWholeDerivationMergedAndAnyOtherSourcesTiling(int tile)
    {
        NavHullNavSource hull = HullCourse();
        DerivedNav whole = Whole(hull, [], 4);
        DerivedNav tiled = NavGraphReduce.DeriveCoarse(hull, Numbers, NavEdgeCosts.Default, [], null, 4, tile);
        AssertSame(whole, tiled);

        // The same leaves through a region source, which is no hull source: its columns and links are asked leaf by leaf.
        NavRegionSource all = NavRegionSource.Over(hull, Enumerable.Range(0, hull.LeafCount));
        AssertSame(tiled, NavGraphReduce.DeriveCoarse(all, Numbers, NavEdgeCosts.Default, [], null, 4, tile));
        int voxel = NavGraphFromNav3d.Derive(hull, Numbers, NavEdgeCosts.Default, []).Graph.CellCount;
        Assert.True(whole.Graph.CellCount < voxel, $"{whole.Graph.CellCount} merged cells of {voxel}");
    }

    [Fact]
    public void AHullFilesColumnsAreTheLeavesCorners()
    {
        NavHullNavSource hull = HullCourse();
        var x = new int[hull.LeafCount];
        var y = new int[hull.LeafCount];
        hull.LeafColumns(x, y, (0f, 0f));
        for (int leaf = 0; leaf < hull.LeafCount; leaf++)
        {
            Assert.Equal((int)MathF.Round(hull.Leaf(leaf).MinX / 16f), x[leaf]);
            Assert.Equal((int)MathF.Round(hull.Leaf(leaf).MinY / 16f), y[leaf]);
        }
    }

    [Fact]
    public void AHullFilesJumpPairsAreEachLeafsJumpsBothWays()
    {
        NavHullNavSource hull = HullCourse();
        var pairs = hull.JumpPairs().ToList();
        Assert.Equal(3, pairs.Count);
        var links = new NavJumpLink[16];
        foreach ((int a, int b) in pairs)
        {
            Assert.Contains(b, links.AsSpan(0, hull.Jumps(a, links)).ToArray().Select(l => l.Leaf));
            Assert.Contains(a, links.AsSpan(0, hull.Jumps(b, links)).ToArray().Select(l => l.Leaf));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void TheTilesOnManyThreadsGiveTheGraphOfOne(int seed)
    {
        (FakeNavSource src, List<HurtVolume> hurt) = Random(seed);
        DerivedNav one = NavGraphReduce.DeriveCoarse(src, Numbers, NavEdgeCosts.Default, hurt, null, 4, 4);
        AssertSame(one, NavGraphReduce.DeriveCoarse(src, Numbers, NavEdgeCosts.Default, hurt, null, 4, 4, threads: 4));
    }

    [Fact]
    public void ACancelledDerivationThrows()
    {
        (FakeNavSource src, List<HurtVolume> hurt) = Course();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => NavGraphReduce.DeriveCoarse(src, Numbers, NavEdgeCosts.Default, hurt, null, 4, 8, cancel: cancel.Token));
    }

    /// <summary>A flat floor of <paramref name="w"/> by <paramref name="h"/> columns at z 0, leaves added row by row.</summary>
    private static (FakeNavSource Source, int[,] At) FlatFloor(int w, int h)
    {
        var src = new FakeNavSource();
        var at = new int[w, h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
                at[x, y] = src.Add(x, y, 0f, 400f);
        }

        return (src, at);
    }

    [Fact]
    public void AMergedCellsEdgeIsAWalkWhereAMemberWalksThereAfterAnotherJumpedThere()
    {
        // Block X (4..7, 4..7) and block Y (8..11, 4..7): X's first member (4, 4) has a link to (9, 5) in Y, met before
        // (7, 4) walks to (8, 4) in Y. Coarsen keeps the walk; so must the tiles.
        (FakeNavSource src, int[,] at) = FlatFloor(16, 12);
        src.JumpLink(at[4, 4], at[9, 5]);
        DerivedNav whole = Whole(src, [], 4);
        DerivedNav tiled = NavGraphReduce.DeriveCoarse(src, Numbers, NavEdgeCosts.Default, [], null, 4, 8);
        AssertSame(whole, tiled);
        int x = tiled.CellOfLeaf[at[4, 4]], y = tiled.CellOfLeaf[at[9, 5]];
        Assert.Equal(x, tiled.CellOfLeaf[at[7, 7]]);
        Assert.Equal(y, tiled.CellOfLeaf[at[8, 4]]);
        Assert.Equal(NavEdgeType.Walk, tiled.Graph.EdgesFrom(x).ToArray().Single(e => e.To == y).Type);
    }

    [Fact]
    public void AHurtVolumeOverATilesMarginAloneIsAskedOfThatTile()
    {
        // The volume covers columns 8..11, the next tile's: column 7's neighbours there have no cell, so it is no flat
        // cell and block (4..7, 4..7) is not merged; a tile that did not ask the volume would merge it.
        (FakeNavSource src, int[,] at) = FlatFloor(16, 12);
        var hurt = new List<HurtVolume> { new(new AiVector(8 * 16f, -100f, -50f), new AiVector(12 * 16f, 500f, 50f)) };
        DerivedNav whole = Whole(src, hurt, 4);
        DerivedNav tiled = NavGraphReduce.DeriveCoarse(src, Numbers, NavEdgeCosts.Default, hurt, null, 4, 8);
        AssertSame(whole, tiled);
        Assert.NotEqual(tiled.CellOfLeaf[at[4, 4]], tiled.CellOfLeaf[at[7, 7]]);
        Assert.Equal(-1, tiled.CellOfLeaf[at[8, 4]]);
    }

    [Fact]
    public void TheCellsAtTheVoxelAreCountedAsTheWholeDerivationHasThem()
    {
        (FakeNavSource src, List<HurtVolume> hurt) = Course();
        var stats = new CoarseDeriveStats();
        NavGraphReduce.DeriveCoarse(src, Numbers, NavEdgeCosts.Default, hurt, null, 4, 8, stats);
        Assert.Equal(NavGraphFromNav3d.Derive(src, Numbers, NavEdgeCosts.Default, hurt).Graph.CellCount, stats.VoxelCells);
        Assert.True(stats.LeavesDerived > stats.Leaves, "the margins are derived again");
    }

    [Fact]
    public void ACellInsideAHurtVolumeIsLeftOut()
    {
        (FakeNavSource src, List<HurtVolume> hurt) = Course();
        DerivedNav with = NavGraphReduce.DeriveCoarse(src, Numbers, NavEdgeCosts.Default, hurt, null, 4, 8);
        DerivedNav without = NavGraphReduce.DeriveCoarse(src, Numbers, NavEdgeCosts.Default, [], null, 4, 8);
        Assert.True(without.Graph.CellCount > with.Graph.CellCount);

        // The pit's floor (5, 17) has a cell only without the volume.
        int pit = Enumerable.Range(0, src.LeafCount).Single(l => src.Leaf(l).MinX == 5 * 16f && src.Leaf(l).MinY == 17 * 16f);
        Assert.Equal(-1, with.CellOfLeaf[pit]);
        Assert.True(without.CellOfLeaf[pit] >= 0);
    }

    [Fact]
    public void TheMarginReachesAClimbsWayOnAndOff()
    {
        // TF's hull is 48 wide: a climb reaches ceil((24 + 8) / 16) = 2 columns, the near moves 2.
        Assert.Equal(2, NavGraphReduce.TileMargin(Numbers, 16f));
        NavNumbers wide = Numbers with { HullWidth = 64f };
        Assert.Equal(3, NavGraphReduce.TileMargin(wide, 16f));
    }

    [Fact]
    public void TheGridOriginIsTheWorldsMovedToTheColumns()
    {
        var src = new FakeNavSource();
        src.Add(-3, 5, 0f);
        Assert.Equal((0f, 0f), NavGraphReduce.GridOrigin(src));
    }

    [Fact]
    public void ATileThatIsNotWholeBlocksIsRefused()
    {
        (FakeNavSource src, List<HurtVolume> hurt) = Course();
        Assert.Throws<ArgumentOutOfRangeException>(() => NavGraphReduce.DeriveCoarse(src, Numbers, NavEdgeCosts.Default, hurt, null, 4, 6));
    }
}

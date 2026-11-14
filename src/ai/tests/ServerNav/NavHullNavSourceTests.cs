using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// The hull navigation read as leaves (<see cref="NavHullNavSource"/>), over a
/// file written by map-tools' writer: two cells of four columns a side. Cell 0
/// is one floor box. Cell 1 is a door's strip (column 0), a lower floor
/// (column 1) and a ledge two voxels up (columns 2 and 3) whose floor is 4 under its bottom voxel, with a player
/// clip's box lying on the strip and the lower floor, and a jump link from
/// each of the lower floor's columns to the ledge's beside it.
/// </summary>
public class NavHullNavSourceTests
{
    private const NavHullFlags Floor = NavHullFlags.Grounded | NavHullFlags.Walkable;

    private sealed record Course(NavHullReader Reader, NavHullNavSource Src, int Wide, int Strip, int Lower, int Ledge, int Clip);

    private static HullNavFixture Fixture(out int wide, out int strip, out int lower, out int ledge, out int clip)
    {
        var f = new HullNavFixture(2, 1, 4, 16);
        int door = f.Obstacle("door_east", "func_door", Nav3dObstacleKind.Door, new Vec3(64, 0, 16), new Vec3(80, 64, 144));
        uint floor = f.Key(Floor, 16f, 144f, 144f);
        uint doorway = f.KeyShutBy(Floor, 16f, 144f, 144f, door, float.NegativeInfinity, float.NegativeInfinity);
        uint high = f.Key(Floor, 44f, 144f, 144f);
        uint clipped = f.Key(NavHullFlags.PlayerBlocked, 0f, float.NegativeInfinity, float.NegativeInfinity);
        wide = f.Box(0, 0, 0, 4, 4, 1, 8, floor);
        strip = f.Box(1, 0, 0, 1, 4, 1, 8, doorway);
        lower = f.Box(1, 1, 0, 2, 4, 1, 8, floor);
        ledge = f.Box(1, 2, 0, 4, 4, 3, 6, high);
        clip = f.Box(1, 0, 0, 2, 4, 9, 2, clipped);
        for (int y = 0; y < 4; y++)
            f.Jump(lower, 1, y, ledge, 2, y, 28f, Nav3dDirection.East);
        return f;
    }

    private static Course Build()
    {
        NavHullReader reader = Fixture(out int wide, out int strip, out int lower, out int ledge, out int clip).Open();
        return new Course(reader, NavHullNavSource.Over(reader), wide, strip, lower, ledge, clip);
    }

    [Fact]
    public void ABoxIsALeafPerColumnOfItsFootprint()
        => Assert.Equal(16 + 4 + 4 + 8 + 8, Build().Src.LeafCount);

    [Fact]
    public void ALeafIsItsColumnsFootprintWithTheBoxsBottomAndTop()
    {
        Course c = Build();
        NavLeaf l = c.Src.Leaf(c.Src.LeafOf(c.Ledge, 3, 1));
        // Cell 1's column 3 is the grid's column 7; the ledge runs voxels 3 to 8.
        Assert.Equal((112f, 16f, 128f, 32f, 48f, 144f), (l.MinX, l.MinY, l.MaxX, l.MaxY, l.BottomZ, l.TopZ));
    }

    [Fact]
    public void EveryLeafOfABoxIsADifferentColumnOfIt()
    {
        Course c = Build();
        var seen = new HashSet<(float, float)>();
        for (int leaf = 0; leaf < c.Src.LeafCount; leaf++)
        {
            if (c.Src.BoxOf(leaf) == c.Ledge)
                seen.Add((c.Src.Leaf(leaf).MinX, c.Src.Leaf(leaf).MinY));
        }

        Assert.Equal(8, seen.Count);
    }

    [Fact]
    public void AGroundedBoxsLeafStandsOnTheKeysFloor()
    {
        Course c = Build();
        NavLeaf l = c.Src.Leaf(c.Src.LeafOf(c.Ledge, 2, 0));
        Assert.True(l.Walkable);
        Assert.Equal(44f, l.FloorZ);
    }

    [Fact]
    public void ABoxWithNoFloorHasItsBottomForOneAndIsNoPlaceToWalk()
    {
        Course c = Build();
        NavLeaf l = c.Src.Leaf(c.Src.LeafOf(c.Clip, 1, 2));
        Assert.False(l.Walkable);
        Assert.Equal(144f, l.FloorZ);
    }

    [Fact]
    public void TheColumnBesideCrossesACellsFace()
    {
        Course c = Build();
        Span<int> into = stackalloc int[8];
        int count = c.Src.ColumnLeaves(c.Src.LeafOf(c.Wide, 3, 2), 1, 0, into);
        Assert.Equal([c.Src.LeafOf(c.Strip, 0, 2), c.Src.LeafOf(c.Clip, 0, 2)], into[..count].ToArray());
    }

    [Fact]
    public void AColumnPastTheGridsEdgeIsNotTheNextRowsFirst()
    {
        // Two rows of two cells: east of the first row's last column is outside the grid, not the second row's first cell.
        var f = new HullNavFixture(2, 2, 4, 16);
        uint floor = f.Key(Floor, 16f, 144f, 144f);
        int east = f.Box(1, 0, 0, 4, 4, 1, 8, floor);
        f.Box(2, 0, 0, 4, 4, 1, 8, floor);
        NavHullNavSource src = NavHullNavSource.Over(f.Open());
        Assert.Equal(0, src.ColumnLeaves(src.LeafOf(east, 3, 0), 1, 0, stackalloc int[8]));
    }

    [Fact]
    public void AboveIsTheBoxTouchingFromAboveAtTheSameColumn()
    {
        Course c = Build();
        Assert.Equal(c.Src.LeafOf(c.Clip, 1, 3), c.Src.Above(c.Src.LeafOf(c.Lower, 1, 3)));
    }

    [Fact]
    public void BelowIsTheBoxTouchingFromBelowAtTheSameColumn()
    {
        Course c = Build();
        Assert.Equal(c.Src.LeafOf(c.Strip, 0, 1), c.Src.Below(c.Src.LeafOf(c.Clip, 0, 1)));
    }

    [Fact]
    public void ABoxWithNothingOverItHasNoLeafAbove()
    {
        Course c = Build();
        Assert.Equal(-1, c.Src.Above(c.Src.LeafOf(c.Wide, 2, 2)));
    }

    [Fact]
    public void AJumpLinkJoinsTheLeavesAtItsTwoColumns()
    {
        Course c = Build();
        Span<NavJumpLink> into = stackalloc NavJumpLink[8];
        int count = c.Src.Jumps(c.Src.LeafOf(c.Lower, 1, 2), into);
        Assert.Equal([new NavJumpLink(c.Src.LeafOf(c.Ledge, 2, 2), 1)], into[..count].ToArray());
    }

    [Fact]
    public void AJumpLinkIsListedFromItsOtherEndToo()
    {
        Course c = Build();
        Span<NavJumpLink> into = stackalloc NavJumpLink[8];
        int count = c.Src.Jumps(c.Src.LeafOf(c.Ledge, 2, 0), into);
        Assert.Equal([new NavJumpLink(c.Src.LeafOf(c.Lower, 1, 0), 1)], into[..count].ToArray());
    }

    [Fact]
    public void ABoxsColumnNoLinkLeavesFromHasNone()
    {
        Course c = Build();
        Assert.Equal(0, c.Src.Jumps(c.Src.LeafOf(c.Ledge, 3, 2), stackalloc NavJumpLink[8]));
    }

    [Fact]
    public void ABodyFitsUnderTheKeysCeiling()
    {
        Course c = Build();
        int leaf = c.Src.LeafOf(c.Wide, 1, 1);
        Assert.True(c.Src.FitsAt(leaf, 62f, 48f, 82f));
        Assert.False(c.Src.FitsAt(leaf, 63f, 48f, 82f));
    }

    [Fact]
    public void AFitIsAskedOnlyInsideTheBoxsHeight()
    {
        NavHullReader reader = OpenRun(out int box);
        NavHullNavSource src = NavHullNavSource.Over(reader);
        // The run is voxels 4 and 5 (z 64 to 96) under no ceiling: a body fits anywhere in it, and nowhere under or over it.
        Assert.True(src.FitsAt(src.LeafOf(box, 0, 0), 64f, 32f, 62f));
        Assert.False(src.FitsAt(src.LeafOf(box, 0, 0), 63f, 32f, 62f));
        Assert.False(src.FitsAt(src.LeafOf(box, 0, 0), 96f, 32f, 62f));
    }

    [Fact]
    public void AFitAboveTheFeetIsAskedOfTheFirstVoxelAtOrOverThem()
    {
        NavHullReader reader = OpenRun(out int box);
        NavHullNavSource src = NavHullNavSource.Over(reader);
        // Feet inside voxel 3 ask voxel 4, the run's first; feet inside voxel 5 ask voxel 6, past its top.
        Assert.True(src.FitsAbove(src.LeafOf(box, 0, 0), 50f, 32f, 62f));
        Assert.False(src.FitsAbove(src.LeafOf(box, 0, 0), 81f, 32f, 62f));
    }

    /// <summary>One column's free run in mid-air, voxels 4 and 5, with nothing over it.</summary>
    private static NavHullReader OpenRun(out int box)
    {
        var f = new HullNavFixture(1, 1, 4, 16);
        box = f.Box(0, 0, 0, 1, 1, 4, 2, f.Key(NavHullFlags.None, 0f, float.PositiveInfinity, float.PositiveInfinity));
        return f.Open();
    }

    [Fact]
    public void AnObstaclesLeavesAreItsBoxesColumns()
    {
        Course c = Build();
        Span<int> into = stackalloc int[64];
        int count = c.Src.ObstacleLeaves(0, into);
        Assert.Equal([.. Enumerable.Range(0, 4).Select(y => c.Src.LeafOf(c.Strip, 0, y))], into[..count].ToArray());
    }

    [Fact]
    public void AnObstacleShutTakesItsLeavesStandingAway()
    {
        Course c = Build();
        int leaf = c.Src.LeafOf(c.Strip, 0, 2);
        Assert.True(c.Src.Stands(leaf, 48f, 82f));
        Assert.False(c.Src.StandsWithShut(leaf, 0, 48f, 82f));
    }

    [Fact]
    public void AnObstacleIsNamedAsTheNav3dsIs()
    {
        NavObstacleInfo o = Build().Src.Obstacle(0);
        Assert.Equal(("door_east", NavObstacleKind.Door, "func_door"), (o.Name, o.Kind, o.ClassName));
    }

    /// <summary>What tools/ai-nav-build builds a TF2 map's hull navigation for (its HULL_WIDTHS): the hull, and the two voxels it drops past a ledge as.</summary>
    [Fact]
    public void TF2AsksItsHullsWidthAndTheTwoVoxelsItDropsAs()
        => Assert.Equal([48f, 32f], NavHullNavSource.WidthsFor(TFNavProfile.ModNumbers, 16f));

    /// <summary>The core game's 32-wide hull is no wider than two voxels: one width (the tool's default).</summary>
    [Fact]
    public void TheCoreGameAsksItsHullsWidthAlone()
        => Assert.Equal([32f], NavHullNavSource.WidthsFor(NavNumbers.Stock(56f), 16f));

    [Fact]
    public void AFileBuiltForTheGamesWidthsLacksNone()
        => Assert.Null(NavHullNavSource.MissingWidth(Build().Reader, TFNavProfile.ModNumbers, "maps/m.navhull"));

    [Fact]
    public void AFileWithoutTheGamesHullIsRefusedByItsNameAndTheWidth()
    {
        string? why = NavHullNavSource.MissingWidth(Build().Reader, TFNavProfile.ModNumbers with { HullWidth = 40f }, "maps/m.navhull");
        Assert.Contains("maps/m.navhull holds no ceilings for a body 40 wide", why);
    }

    [Fact]
    public void AFileWithoutTheWidthAWideHullDropsAsIsRefused()
    {
        var f = new HullNavFixture(1, 1, 4, 16) { Widths = [48f] };
        f.Box(0, 0, 0, 4, 4, 1, 8, f.Key(Floor, 16f, 144f, 144f));
        string? why = NavHullNavSource.MissingWidth(f.Open(), TFNavProfile.ModNumbers, "maps/m.navhull");
        Assert.Contains("a body 32 wide", why);
    }

    [Fact]
    public void AFileWithoutAnExtraHullsWidthIsRefused()
    {
        NavNumbers n = TFNavProfile.ModNumbers with { ExtraHulls = NavHulls.Of(new NavHull(29f, 50f)) };
        Assert.Contains("a body 29 wide", NavHullNavSource.MissingWidth(Build().Reader, n, "maps/m.navhull"));
    }

    [Fact]
    public void ARefusalSaysWhatToBuildItWith()
        => Assert.EndsWith("build it with --hull 40,32", NavHullNavSource.MissingWidth(Build().Reader, TFNavProfile.ModNumbers with { HullWidth = 40f }, "maps/m.navhull"));

    [Fact]
    public void APointInABoxIsFree()
        => Assert.True(new NavHullVoxelSpace(Build().Reader).Free(new AiVector(8f, 8f, 20f)));

    [Fact]
    public void APointInSolidIsNotFree()
        => Assert.False(new NavHullVoxelSpace(Build().Reader).Free(new AiVector(8f, 8f, 8f)));

    [Fact]
    public void APlayerClipsSpaceIsFreeToAPoint()
        => Assert.True(new NavHullVoxelSpace(Build().Reader).Free(new AiVector(72f, 8f, 150f)));

    private static DerivedNav Derive(Course c) => NavGraphFromNav3d.Derive(c.Src, TFNavProfile.ModNumbers, NavEdgeCosts.Default, []);

    private static NavEdgeType? EdgeType(DerivedNav d, int fromLeaf, int toLeaf)
        => d.Graph.TryEdge(d.CellOfLeaf[fromLeaf], d.CellOfLeaf[toLeaf], out NavEdge e) ? e.Type : null;

    [Fact]
    public void TheDerivationPlacesACellOnEveryColumnAFloorBoxCovers()
        => Assert.Equal(16 + 4 + 4 + 8, Derive(Build()).Graph.CellCount);

    [Fact]
    public void TheDerivationWalksFromColumnToColumnInsideABox()
    {
        Course c = Build();
        Assert.Equal(NavEdgeType.Walk, EdgeType(Derive(c), c.Src.LeafOf(c.Wide, 1, 1), c.Src.LeafOf(c.Wide, 2, 1)));
    }

    [Fact]
    public void TheDerivationGoesThroughADoorsBoxAsADoor()
    {
        Course c = Build();
        Assert.Equal(NavEdgeType.Door, EdgeType(Derive(c), c.Src.LeafOf(c.Wide, 3, 1), c.Src.LeafOf(c.Strip, 0, 1)));
    }

    [Fact]
    public void TheDerivationJumpsALedgeBetweenTwoBoxes()
    {
        Course c = Build();
        Assert.Equal(NavEdgeType.Jump, EdgeType(Derive(c), c.Src.LeafOf(c.Lower, 1, 1), c.Src.LeafOf(c.Ledge, 2, 1)));
    }

    [Fact]
    public void TheDerivationDropsBackOffTheLedge()
    {
        Course c = Build();
        Assert.Equal(NavEdgeType.Drop, EdgeType(Derive(c), c.Src.LeafOf(c.Ledge, 2, 1), c.Src.LeafOf(c.Lower, 1, 1)));
    }

    [Fact]
    public void TheJumpSimulatorsColumnsJoinBoxesThatTouch()
    {
        Course c = Build();
        VoxelColumns columns = VoxelColumns.FromNav(c.Src);
        // The lower floor (16 to 144) and the clip's box on it (to 176): one span of free space.
        Assert.Equal([new FreeSpan(16f, 176f)], columns.Spans(5, 2).ToArray());
    }

    [Fact]
    public void TheJumpSimulatorsGridIsTheBoxesColumns()
    {
        VoxelColumns columns = VoxelColumns.FromNav(Build().Src);
        Assert.Equal((8, 4), (columns.Width, columns.Depth));
    }
}

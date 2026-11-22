using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// <see cref="NavHullSplit"/>: a region of a hull navigation written as a file of its own. The
/// fixture is three cells of eight columns in a row, sixteen voxels up. Region 0 is a floor in
/// cell 0 with air over it, its key shut by a far door (so the whole file's first shut record is
/// not region 1's); region 1 is a floor in cell 2 and a ledge beside it two voxels up, a
/// jump link between them, the ledge's key shut by a door; a jump link the fixture names from
/// region 0's floor to region 1's ledge crosses between them. Cell 1 is empty.
/// </summary>
public sealed class NavHullSplitTests
{
    private sealed record Map(NavHullReader Reader, NavHullRegions Regions, int Floor0, int Air0, int Floor1, int Ledge1);

    private static Map Build()
    {
        var f = new HullNavFixture(3, 1, 8, 16);
        uint floor = f.Key(NavHullFlags.Grounded | NavHullFlags.Walkable, 16f, 144f, 144f);
        uint air = f.Key(0, 0f, 256f, 256f);
        int door = f.Obstacle("gate", "func_door", Nav3dObstacleKind.Door, new Vec3(296, 0, 48), new Vec3(312, 128, 160));
        int far = f.Obstacle("far", "func_door", Nav3dObstacleKind.Door, new Vec3(0, 0, 0), new Vec3(8, 8, 8));
        uint floorShut = f.KeyShutBy(NavHullFlags.Grounded | NavHullFlags.Walkable, 16f, 144f, 144f, far, 100f, 100f);
        uint ledge = f.KeyShutBy(NavHullFlags.Grounded | NavHullFlags.Walkable, 48f, 176f, 176f, door, 48f, 48f);
        int floor0 = f.Box(0, 0, 0, 4, 8, 1, 8, floorShut);
        int air0 = f.Box(0, 0, 0, 4, 8, 9, 4, air);
        int floor1 = f.Box(2, 0, 0, 2, 8, 1, 8, floor);
        int ledge1 = f.Box(2, 2, 0, 4, 8, 3, 6, ledge);
        f.Jump(floor0, 3, 4, ledge1, 2, 4, 32f, Nav3dDirection.East, 15);
        f.Jump(floor1, 1, 4, ledge1, 2, 4, 32f, Nav3dDirection.East, 1);
        NavHullReader reader = f.Open();
        return new Map(reader, NavHullRegions.Of(reader), floor0, air0, floor1, ledge1);
    }

    private static NavHullPart Part(Map m, int region) => NavHullSplit.Region(m.Reader, m.Reader.ToLevel(), m.Regions, region);

    private static NavHullReader Written(NavHullPart p) => NavHullReader.Open(NavHullWriter.Write(p.Level));

    [Fact]
    public void APartHoldsItsRegionsBoxesAlone()
    {
        Map m = Build();
        int r1 = m.Regions.RegionOf(m.Floor1);
        NavHullReader part = Written(Part(m, r1));
        Assert.Equal(2, part.BoxCount);
        Assert.Equal(m.Reader.Box(m.Floor1) with { Key = 0 }, part.Box(0) with { Key = 0 });
        Assert.Equal(m.Reader.Box(m.Ledge1) with { Key = 0 }, part.Box(1) with { Key = 0 });
    }

    [Fact]
    public void APartsGridIsCutToItsCellsWithTheOriginMovedByWholeCells()
    {
        Map m = Build();
        NavHullPart p = Part(m, m.Regions.RegionOf(m.Floor1));
        Assert.Equal((1, 1, 2, 0), (p.Level.Columns, p.Level.Rows, p.FirstColumn, p.FirstRow));
        Assert.Equal(new Vec3(256, 0, 0), p.Level.Origin);
        NavHullReader part = Written(p);
        Assert.Equal(m.Reader.BoxBounds(m.Ledge1), part.BoxBounds(1));
    }

    [Fact]
    public void APartDerivesTheGraphItsRegionDerivesOutOfTheWholeFile()
    {
        Map m = Build();
        NavNumbers n = TFNavProfile.ModNumbers;
        foreach (int region in (ReadOnlySpan<int>)[m.Regions.RegionOf(m.Floor0), m.Regions.RegionOf(m.Floor1)])
        {
            var whole = NavHullNavSource.Over(m.Reader);
            DerivedNav a = NavGraphFromNav3d.Derive(NavRegionSource.Over(whole, m.Regions.Leaves(whole, region)), n, NavEdgeCosts.Default, []);
            DerivedNav b = NavGraphFromNav3d.Derive(NavHullNavSource.Over(Written(Part(m, region))), n, NavEdgeCosts.Default, []);
            Assert.True(a.Graph.CellCount > 0);
            Assert.Equal((a.Graph.Hash, a.Graph.CellCount, a.Graph.EdgeCount), (b.Graph.Hash, b.Graph.CellCount, b.Graph.EdgeCount));
            Assert.Equal(a.LeafOfCell, b.LeafOfCell);
        }
    }

    [Fact]
    public void AJumpLinkWithBothEndsInTheRegionIsKeptRenumbered()
    {
        Map m = Build();
        NavHullReader part = Written(Part(m, m.Regions.RegionOf(m.Floor1)));
        NavHullJump j = Assert.Single(Enumerable.Range(0, part.JumpCount).Select(part.Jump));
        Assert.Equal((0u, 1u, (byte)1, (byte)4, (byte)2, (byte)4), (j.BoxA, j.BoxB, j.AX, j.AY, j.BX, j.BY));
    }

    [Fact]
    public void AJumpLinkOutOfTheRegionIsLeftOutAndCounted()
    {
        Map m = Build();
        NavHullPart p0 = Part(m, m.Regions.RegionOf(m.Floor0)), p1 = Part(m, m.Regions.RegionOf(m.Floor1));
        Assert.Equal((0, 1), (p0.Level.Jumps.Count, p0.CrossingJumps));
        Assert.Equal(1, p1.CrossingJumps);
    }

    [Fact]
    public void KeysAreTheRegionsOwnAndKeepTheirShutRecords()
    {
        Map m = Build();
        NavHullReader part = Written(Part(m, m.Regions.RegionOf(m.Floor1)));
        Assert.Equal(2, part.KeyCount);
        Assert.Equal([1], part.ObstacleBoxes(0).ToArray());
        Assert.False(part.StandsWithShut(1, 0, 48f, 62f));
        Assert.True(part.Stands(1, 48f, 62f));
    }

    [Fact]
    public void EveryObstacleIsKeptInOrder()
    {
        Map m = Build();
        NavHullReader part = Written(Part(m, m.Regions.RegionOf(m.Floor1)));
        Assert.Equal(["gate", "far"], Enumerable.Range(0, part.ObstacleCount).Select(o => part.Obstacle(o).Name));
    }

    [Fact]
    public void AnObstaclesCellIsCountedInTheCutGridAndOneOutsideItIsItsFirst()
    {
        Map m = Build();
        NavHullLevel whole = m.Reader.ToLevel();
        Nav3dObstacle gate = whole.Obstacles[0];
        var moved = new NavHullLevel
        {
            VoxelSize = whole.VoxelSize, CellVoxels = whole.CellVoxels, Columns = whole.Columns, Rows = whole.Rows, Origin = whole.Origin,
            ColumnVoxels = whole.ColumnVoxels, FloorNormalZ = whole.FloorNormalZ, StepHeight = whole.StepHeight, JumpHeight = whole.JumpHeight,
            JumpDistance = whole.JumpDistance, Widths = whole.Widths, CellStarts = whole.CellStarts, Boxes = whole.Boxes, Keys = whole.Keys,
            Shuts = whole.Shuts, Jumps = whole.Jumps, Bsp = whole.Bsp,
            Obstacles = [gate with { Cell = 2 }, whole.Obstacles[1] with { Cell = 1 }],
        };
        NavHullReader part = Written(NavHullSplit.Region(m.Reader, moved, m.Regions, m.Regions.RegionOf(m.Floor1)));
        Assert.Equal((0u, 0u), (part.Obstacle(0).Cell, part.Obstacle(1).Cell));
    }
}

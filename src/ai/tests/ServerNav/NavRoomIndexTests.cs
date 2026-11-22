using SourceSharp.Ai.Server.Nav;
using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// <see cref="NavRoomIndex"/>: which region a place is in, by a cell's spans of voxels. The
/// fixture is two cells of eight columns, 32 voxels up: in cell 0 a room low (voxels 1 to 8) and
/// another stacked over it past solid (voxels 12 to 19); in cell 1 a room of three boxes along one
/// side, away from cell 0, running from voxel 1 to 24, and a fourth room's box (voxels 10 to 14) apart from it,
/// so the two share the cell's height.
/// </summary>
public sealed class NavRoomIndexTests
{
    private static (NavHullReader Reader, NavHullRegions Regions) Build()
    {
        var f = new HullNavFixture(2, 1, 8, 32);
        uint floor = f.Key(NavHullFlags.Grounded | NavHullFlags.Walkable, 16f, 144f, 144f);
        f.Box(0, 0, 0, 8, 8, 1, 8, floor);
        f.Box(0, 0, 0, 8, 8, 12, 8, floor);
        f.Box(1, 3, 6, 5, 8, 10, 5, floor);
        f.Box(1, 6, 0, 8, 2, 1, 4, floor);
        f.Box(1, 6, 0, 8, 2, 20, 5, floor);
        f.Box(1, 6, 2, 8, 8, 1, 24, floor);
        NavHullReader reader = f.Open();
        return (reader, NavHullRegions.Of(reader));
    }

    private static List<int> At(NavRoomIndex index, float x, float y, float z)
    {
        var into = new List<int>();
        index.RegionsAt(new Vec3(x, y, z), into);
        return into;
    }

    [Fact]
    public void APlaceInARoomAnswersThatRoomAlone()
    {
        (NavHullReader reader, NavHullRegions regions) = Build();
        NavRoomIndex index = NavRoomIndex.Of(reader, regions);
        Assert.Equal([regions.RegionOf(0)], At(index, 64, 64, 40));
        Assert.Equal([regions.RegionOf(1)], At(index, 64, 64, 230));
    }

    [Fact]
    public void APlaceInARoomsTopVoxelAnswersThatRoom()
    {
        (NavHullReader reader, NavHullRegions regions) = Build();
        Assert.Equal([regions.RegionOf(0)], At(NavRoomIndex.Of(reader, regions), 64, 64, 136));
    }

    [Fact]
    public void APlaceInSolidBetweenTwoStackedRoomsAnswersNone()
    {
        (NavHullReader reader, NavHullRegions regions) = Build();
        Assert.Empty(At(NavRoomIndex.Of(reader, regions), 64, 64, 168));
    }

    [Fact]
    public void WhereTwoRoomsShareACellsHeightBothAreAnswered()
    {
        (NavHullReader reader, NavHullRegions regions) = Build();
        NavRoomIndex index = NavRoomIndex.Of(reader, regions);
        Assert.Equal(1, index.SharedCells);
        Assert.Equal(new[] { regions.RegionOf(2), regions.RegionOf(3) }.Order(), At(index, 200, 120, 200).Order());
    }

    [Fact]
    public void APlaceOutsideTheGridAnswersNone()
    {
        (NavHullReader reader, NavHullRegions regions) = Build();
        NavRoomIndex index = NavRoomIndex.Of(reader, regions);
        Assert.Empty(At(index, -8, 64, 40));
        Assert.Empty(At(index, 300, 64, 40));
    }

    [Fact]
    public void TheIndexReadBackAnswersAsItDid()
    {
        (NavHullReader reader, NavHullRegions regions) = Build();
        NavRoomIndex index = NavRoomIndex.Of(reader, regions), back = NavRoomIndex.Read(index.Write());
        Assert.Equal((index.Cells, index.Spans, index.SharedCells), (back.Cells, back.Spans, back.SharedCells));
        Assert.Equal(At(index, 200, 120, 200), At(back, 200, 120, 200));
        Assert.Equal(At(index, 64, 64, 230), At(back, 64, 64, 230));
    }

    [Fact]
    public void ATruncatedIndexIsRefused()
    {
        (NavHullReader reader, NavHullRegions regions) = Build();
        byte[] bytes = NavRoomIndex.Of(reader, regions).Write();
        Assert.Throws<InvalidDataException>(() => NavRoomIndex.Read(bytes[..^3]));
    }
}

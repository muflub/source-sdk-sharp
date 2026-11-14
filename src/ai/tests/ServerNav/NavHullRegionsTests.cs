using SourceSharp.Ai.Server.Nav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// <see cref="NavHullRegions"/>: boxes joined by a chain of boxes, each touching the next across a
/// side or from above or below, are one region of free space; walkable or not makes no difference.
/// The fixtures are one cell of eight columns a side (or two cells side by side), sixteen voxels up.
/// </summary>
public sealed class NavHullRegionsTests
{
    private static uint Floor(HullNavFixture f) => f.Key(NavHullFlags.Grounded | NavHullFlags.Walkable, 16f, 144f, 144f);

    private static uint Air(HullNavFixture f) => f.Key(0, 0f, 256f, 256f);

    /// <summary>Two boxes of three columns by eight, two columns of solid between them.</summary>
    internal static HullNavFixture Sealed()
    {
        var f = new HullNavFixture(1, 1, 8, 16);
        uint floor = Floor(f);
        f.Box(0, 0, 0, 3, 8, 1, 8, floor);
        f.Box(0, 5, 0, 8, 8, 1, 8, floor);
        return f;
    }

    [Fact]
    public void TwoBoxesSealedApartAreTwoRegions()
    {
        NavHullRegions r = NavHullRegions.Of(Sealed().Open());
        Assert.Equal(2, r.Count);
        Assert.NotEqual(r.RegionOf(0), r.RegionOf(1));
    }

    [Fact]
    public void BoxesTouchingAcrossASideIntoTheNextCellAreOneRegion()
        => Assert.Equal(1, NavHullRegions.Of(HullNavFixtures.Floor().Open()).Count);

    [Fact]
    public void AnAirBoxTouchingFromAboveJoinsTheFloorUnderIt()
    {
        var f = new HullNavFixture(1, 1, 8, 16);
        f.Box(0, 0, 0, 3, 8, 1, 8, Floor(f));
        f.Box(0, 0, 0, 3, 8, 9, 4, Air(f));
        Assert.Equal(1, NavHullRegions.Of(f.Open()).Count);
    }

    [Fact]
    public void ABoxWithSolidBetweenItAndTheOneBelowIsARegionOfItsOwn()
    {
        var f = new HullNavFixture(1, 1, 8, 16);
        f.Box(0, 0, 0, 3, 8, 1, 8, Floor(f));
        f.Box(0, 0, 0, 3, 8, 10, 4, f.Key(NavHullFlags.Grounded | NavHullFlags.Walkable, 160f, 224f, 224f));
        Assert.Equal(2, NavHullRegions.Of(f.Open()).Count);
    }

    [Fact]
    public void TwoSealedBoxesJoinedByAnAirBridgeAreOneRegion()
    {
        HullNavFixture f = Sealed();
        f.Box(0, 3, 0, 5, 8, 6, 3, Air(f));
        Assert.Equal(1, NavHullRegions.Of(f.Open()).Count);
    }

    [Fact]
    public void RegionsAreNumberedByTheirLowestBox()
    {
        NavHullRegions r = NavHullRegions.Of(Sealed().Open());
        Assert.Equal((0, 1), (r.RegionOf(0), r.RegionOf(1)));
        Assert.Equal([1], r.Boxes(1).ToArray());
    }

    [Fact]
    public void ARegionsLeavesAreItsBoxesColumns()
    {
        NavHullReader reader = Sealed().Open();
        NavHullNavSource source = NavHullNavSource.Over(reader);
        NavHullRegions r = NavHullRegions.Of(reader);
        int[] leaves = r.Leaves(source, 1);
        Assert.Equal(24, leaves.Length);
        Assert.All(leaves, l => Assert.Equal(1, source.BoxOf(l)));
    }
}

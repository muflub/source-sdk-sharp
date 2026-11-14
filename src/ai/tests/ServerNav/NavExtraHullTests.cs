using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// Cells classed for more bodies than the game's own (<see cref="NavNumbers.ExtraHulls"/>): extra
/// hull i is <see cref="ClearanceClass"/> 2 + i, admitted in a cell where its box stands, so a
/// flow field for a wide body keeps to the doors it fits. Over a real <c>.nav3d</c> of a wall
/// with a narrow door and a wide one, read through the real reader.
/// </summary>
public sealed class NavExtraHullTests : IDisposable
{
    private static readonly NavHulls Three = NavHulls.Of(new(29f, 50f), new(49f, 83f), new(86f, 145f));

    private const ClearanceClass Small = ClearanceClass.Hull2, Middle = ClearanceClass.Hull3, Giant = ClearanceClass.Hull4;

    private static readonly NavNumbers Numbers = NavNumbers.Stock(56f) with { ExtraHulls = Three };

    // The wall's plane, and each door's span along it.
    private const float WallX0 = 368f, WallX1 = 384f, NarrowY0 = 96f, NarrowY1 = 176f, WideY0 = 320f, WideY1 = 448f;

    /// <summary>
    /// A floor (top at 16) 768 by 512, and across it at x 368-384 a wall to the ceiling with two
    /// doors: one 80 wide (y 96-176) and one 128 wide (y 320-448).
    /// </summary>
    private static VoxelNavFixture Doors() => new VoxelNavFixture(3, 2, 16)
        .Solid(0, 0, 0, 768, 512, 16)
        .Solid(WallX0, 0, 16, WallX1, NarrowY0, 256)
        .Solid(WallX0, NarrowY1, 16, WallX1, WideY0, 256)
        .Solid(WallX0, WideY1, 16, WallX1, 512, 256);

    private static readonly Lazy<DerivedNav> Derived = new(()
        => NavGraphFromNav3d.Derive(new Nav3dNavSource(Doors().Open()), Numbers, NavEdgeCosts.Default, []));

    private static NavGraph Graph => Derived.Value.Graph;

    private static int CellAt(float x, float y)
    {
        CellId c = Graph.FindCell(new AiVector(x, y, 17f));
        Assert.True(c.IsValid, $"no cell at ({x} {y})");
        return c.Index;
    }

    private static bool InWall(in NavCell c) => c.MinX >= WallX0 && c.MaxX <= WallX1;

    private static bool InNarrow(in NavCell c) => InWall(c) && c.MinY >= NarrowY0 && c.MaxY <= NarrowY1;

    private static bool InWide(in NavCell c) => InWall(c) && c.MinY >= WideY0 && c.MaxY <= WideY1;

    /// <summary>The cells a body of <paramref name="cls"/> crosses from the west side at height <paramref name="y"/> to the east side at the same height.</summary>
    private static List<NavCell> Crossing(ClearanceClass cls, float y)
    {
        var field = new FlowField();
        field.Build(Graph, new CellId(CellAt(600f, y)), cls, new NavObstacleState(Graph.ObstacleCount));
        int from = CellAt(152f, y);
        Assert.True(float.IsFinite(field.CostAt(from)), $"no way across for {cls} at y {y}");
        var route = new List<NavCell> { Graph.Cell(from) };
        for (int at = from, guard = 0; field.NextFrom(at).IsValid && guard < 10_000; guard++)
        {
            at = field.NextFrom(at).Index;
            route.Add(Graph.Cell(at));
        }

        return route;
    }

    [Fact]
    public void TheNarrowDoorAdmitsTheSmallAndTheMiddleHullAndNotTheGiant()
    {
        NavCell door = Graph.Cell(CellAt(376f, 136f));
        Assert.Equal((true, true, false), (door.Admits(Small), door.Admits(Middle), door.Admits(Giant)));
    }

    [Fact]
    public void TheWideDoorAdmitsEveryHull()
    {
        NavCell door = Graph.Cell(CellAt(376f, 392f));
        Assert.Equal((true, true, true), (door.Admits(Small), door.Admits(Middle), door.Admits(Giant)));
    }

    /// <summary>Of the wall's plane, the giant is admitted in the wide door only, and it is admitted there.</summary>
    [Fact]
    public void TheGiantIsAdmittedThroughTheWideDoorOnly()
    {
        NavCell[] through = [.. Enumerable.Range(0, Graph.CellCount).Select(Graph.Cell).Where(c => InWall(c) && c.Admits(Giant))];
        Assert.NotEmpty(through);
        Assert.All(through, c => Assert.True(InWide(c), $"the giant is admitted at ({c.MinX} {c.MinY})"));
    }

    /// <summary>The plan's gate: from beside the narrow door to the far side of it, the giant goes by the wide door.</summary>
    [Fact]
    public void AFlowFieldNeverRoutesTheGiantThroughTheNarrowDoor()
    {
        List<NavCell> route = Crossing(Giant, 136f);
        Assert.DoesNotContain(route, c => InNarrow(c));
        Assert.Contains(route, c => InWide(c));
        Assert.All(route, c => Assert.True(c.Admits(Giant)));
    }

    /// <summary>The control: the same crossing by the small hull goes straight through the narrow door.</summary>
    [Fact]
    public void TheSmallHullCrossesByTheNarrowDoor()
    {
        List<NavCell> route = Crossing(Small, 136f);
        Assert.Contains(route, c => InNarrow(c));
        Assert.DoesNotContain(route, c => InWide(c));
    }

    [Fact]
    public void TheSmallHullCrossesByTheWideDoorToo()
    {
        List<NavCell> route = Crossing(Small, 392f);
        Assert.Contains(route, c => InWide(c));
        Assert.DoesNotContain(route, c => InNarrow(c));
    }

    /// <summary>What the extra hulls add is bits: the cells, their floors, costs and edges are the game's own body's.</summary>
    [Fact]
    public void ExtraHullsChangeNothingOfTheGraphButTheirBits()
    {
        NavGraph plain = NavGraphFromNav3d.Derive(new Nav3dNavSource(Doors().Open()), NavNumbers.Stock(56f), NavEdgeCosts.Default, []).Graph;
        Assert.Equal((plain.CellCount, plain.EdgeCount), (Graph.CellCount, Graph.EdgeCount));
        const ClearanceClasses Body = ClearanceClasses.Stand | ClearanceClasses.Crouch;
        for (int c = 0; c < plain.CellCount; c++)
        {
            Assert.Equal(plain.Cell(c), Graph.Cell(c) with { Classes = Graph.Cell(c).Classes & Body });
            Assert.Equal(plain.EdgesFrom(c).ToArray(), Graph.EdgesFrom(c).ToArray());
        }

        Assert.NotEqual(plain.Hash, Graph.Hash);
    }

    // ---------------------------------------------------------------- the cache's bytes

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai-navhulls-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void ACachedGraphKeepsItsExtraHullBits()
    {
        var cache = new NavGraphCache(_dir, NavEdgeCosts.Default);
        string a = new('a', 64), b = new('b', 64), key = NavGraphCache.NumbersKey(Numbers, 56f);
        DerivedNav read = cache.Decode(cache.Encode("m", a, b, Derived.Value, key), "m", a, b, key);
        Assert.Contains(Enumerable.Range(0, Graph.CellCount), c => Graph.Cell(c).Admits(Giant));
        Assert.Equal(Enumerable.Range(0, Graph.CellCount).Select(c => Graph.Cell(c).Classes),
                     Enumerable.Range(0, read.Graph.CellCount).Select(c => read.Graph.Cell(c).Classes));
        Assert.Equal(Graph.Hash, read.Graph.Hash);
    }

    private sealed class Profile(NavNumbers numbers) : INavProfile
    {
        public NavNumbers Numbers(float fileJumpHeight) => numbers;

        public JumpReach Reach(EntityHandle player) => JumpReach.None;

        public IReadOnlyList<TeamSolid> TeamSolids(MapEntities entities) => [];
    }

    /// <summary>Through the loader: the second load reads the first's file, named for the hulls, and is the same graph.</summary>
    [Fact]
    public void ALevelWithExtraHullsLoadsFromItsOwnCacheFile()
    {
        byte[] bsp = TinyBsp.Make(TinyBsp.Stock), nav = NavFixtures.Course(Nav3dBspBinding.Of(bsp)).Write();
        var cache = new NavGraphCache(_dir, NavEdgeCosts.Default);
        NavLoadResult Load(NavNumbers n) => NavLoader.Load("dm_test", p => p.EndsWith(".bsp", StringComparison.Ordinal) ? bsp : nav, cache, new Profile(n));
        LoadedNav plain = Load(NavNumbers.Stock(56f)).Nav!, first = Load(Numbers).Nav!, second = Load(Numbers).Nav!;
        Assert.Equal((false, false, true), (plain.FromCache, first.FromCache, second.FromCache));
        Assert.Equal(first.Nav.Graph.Hash, second.Nav.Graph.Hash);
        Assert.NotEqual(plain.Nav.Graph.Hash, first.Nav.Graph.Hash);
        Assert.EndsWith($"-n{NavGraphCache.NumbersKey(Numbers, 56f)}.navgraph", cache.PathFor("dm_test", first.BspSha256, first.NavSha256, first.NumbersKey), StringComparison.Ordinal);
        Assert.Equal(2, Directory.GetFiles(_dir, "*.navgraph").Length);
    }
}

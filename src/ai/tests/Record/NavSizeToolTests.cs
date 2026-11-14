using SourceSharp.Ai.Replay;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Tests.ServerNav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Record;

/// <summary>
/// <c>ai-replay navsize</c> (<see cref="NavSizeTool"/>): what loading a navigation costs, step by
/// step. A step's CPU time is the clock's advance across it; a stop ends the run after the step it
/// names; a hull navigation's leaves are its boxes' columns; the cache's size is the encoded graph's
/// under the name the loader gives it; a hull navigation without the game's width is refused. The
/// navigation is <see cref="HullNavFixtures.Floor"/>: two cells of 16 columns, one floor box each.
/// </summary>
public sealed class NavSizeToolTests
{
    private static readonly byte[] Bsp = TinyBsp.Make(TinyBsp.Stock);
    private static readonly byte[] Hull = HullNavFixtures.Floor(Nav3dBspBinding.Of(Bsp)).Write();

    /// <summary>A clock that moves 250 µs each time it is read, so a step read twice costs 0.25 ms.</summary>
    private static Func<double> Ticking()
    {
        double now = 0;
        return () => now += 250;
    }

    private static NavSizeReport Measure(string stop, bool sight = true, string game = CostTool.Hl2mp)
        => NavSizeTool.Measure(game, "dm_test", "dm_test.navhull", Hull, Bsp, stop, Ticking(), sight);

    [Fact]
    public void AStepsCpuTimeIsTheClocksAdvanceAcrossIt()
        => Assert.All(Measure("cache").Steps, s => Assert.Equal(0.25, s.CpuMs));

    [Fact]
    public void TheStepsRunInOrderUpToTheStop()
        => Assert.Equal(["open", "leaves", "derive"], Measure("derive").Steps.Select(s => s.Name));

    [Fact]
    public void AStopAtOpenDerivesNothing()
        => Assert.Null(Measure("open").Nav);

    [Fact]
    public void AnUnknownStopIsRefusedNamingTheSteps()
        => Assert.Contains("open, leaves, derive, sight, cache", Assert.Throws<ArgumentException>(() => Measure("graph")).Message);

    [Fact]
    public void AHullNavigationsLeavesAreItsBoxesColumns()
        => Assert.EndsWith("; 32 leaves (a box's columns)", Measure("open").Steps[0].What, StringComparison.Ordinal);

    private static LoadedNav Loaded()
        => NavLoader.Load("dm_test", p => p.EndsWith(".bsp", StringComparison.Ordinal) ? Bsp : p.EndsWith(".navhull", StringComparison.Ordinal) ? Hull : null, null).Nav!;

    [Fact]
    public void TheDerivedGraphIsTheLoadersGraph()
        => Assert.Equal(Loaded().Nav.Graph.Hash, Measure("derive").Nav!.Graph.Hash);

    [Fact]
    public void TheCachesSizeIsTheEncodedGraphUnderTheLoadersName()
    {
        LoadedNav loaded = Loaded();
        byte[] encoded = new NavGraphCache(Path.GetTempPath(), NavEdgeCosts.Default)
            .Encode("dm_test", loaded.BspSha256, loaded.NavSha256, loaded.Nav, loaded.NumbersKey);
        Assert.Equal(encoded.Length, Measure("cache").CacheBytes);
    }

    [Fact]
    public void WithoutSightTheTableIsLeftOutAndTheCacheStillMeasured()
    {
        NavSizeReport r = Measure("cache", sight: false);
        Assert.Null(r.Sight);
        Assert.Equal(["open", "leaves", "derive", "cache"], r.Steps.Select(s => s.Name));
    }

    [Fact]
    public void AHullNavigationWithoutTheGamesWidthIsRefused()
    {
        var f = new HullNavFixture(1, 1, 4, 16) { Bsp = Nav3dBspBinding.Of(Bsp), Widths = [32f] };
        f.Box(0, 0, 0, 4, 4, 1, 8, f.Key(NavHullFlags.Grounded | NavHullFlags.Walkable, 16f, 144f, 144f));
        Assert.Throws<InvalidDataException>(() => NavSizeTool.Measure(CostTool.TF2, "dm_test", "dm_test.navhull", f.Write(), Bsp, "open", Ticking()));
    }
}

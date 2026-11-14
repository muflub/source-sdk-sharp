using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Tests.Senses;
using SourceSharp.MapFormats.Nav;
using SourceSharp.Server;
using SourceSharp.Tests;
using Xunit;
using NavService = SourceSharp.Ai.Server.Nav.AiNav;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// The level's navigation from a hull navigation (plan_bots_teams_tf2_part_g G.7): a level whose
/// map has only <c>maps/&lt;map&gt;.navhull</c> gets its graph and its sight table from it, and the
/// level's line and <c>ai_nav_status</c> name the file that was read, whichever kind it is.
/// </summary>
public sealed class AiNavHullTests : IDisposable
{
    private sealed class Mod(string dir) : IModInfo
    {
        public string Id => "test";
        public string GameDir => dir;
        public string ManagedDir => dir;
        public IReadOnlyList<string> AssembliesFor(Side side) => [];
    }

    private sealed class Lines : ILog
    {
        public List<string> All { get; } = [];

        public void Msg(string message) => All.Add(message);

        public void Warning(string message) => All.Add(message);
    }

    private sealed class NoTraces : SourceSharp.Server.Interop.ITraceQueries
    {
        public SourceSharp.Server.Interop.TraceAnswer Line(Vector start, Vector end, uint mask, EntityHandle pass) => throw new InvalidOperationException("no traces");

        public SourceSharp.Server.Interop.TraceAnswer Hull(Vector start, Vector end, Vector mins, Vector maxs, uint mask, EntityHandle pass) => throw new InvalidOperationException("no traces");
    }

    private static readonly byte[] Tiny = TinyBsp.Make(TinyBsp.Stock);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai-navhull-level-" + Guid.NewGuid().ToString("N"));
    private readonly Lines _log = new();
    private NavService? _nav;

    public void Dispose()
    {
        _nav?.OnShutdown();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>A level of dm_test loaded with <paramref name="files"/> beside its bsp, in a game whose binding builds no navigation.</summary>
    private NavService Loaded(params (string Name, byte[] Bytes)[] files)
    {
        IEngineFiles seam = DispatchProxy.Create<IEngineFiles, AiNavBuildTests.Files>();
        var held = ((AiNavBuildTests.Files)(object)seam).Held;
        held["maps/dm_test.bsp"] = Tiny;
        foreach ((string name, byte[] bytes) in files)
            held[name] = bytes;
        IEngineEntities world = FakeEngine.Of<IEngineEntities>((m, _) => m.Name switch
        {
            nameof(IEngineEntities.FindByName) => EntityHandle.None,
            nameof(IEngineEntities.FindAllByClassname) => Array.Empty<EntityHandle>(),
            _ => throw FakeEngine.Unasked(m),
        });
        _nav = new NavService(seam, new Mod(_dir), world, _log, FakeEngine.Of<IEnginePlayers>((m, _) => throw FakeEngine.Unasked(m)), new NoTraces(),
                              new SourceSharp.Ai.Hl2mp.Nav.Hl2mpNavProfile(), NavBuildTestKnobs.Of("1"));
        _nav.Load("dm_test");
        return _nav;
    }

    private static (string, byte[]) Hull => ("maps/dm_test.navhull", HullNavFixtures.Floor(Nav3dBspBinding.Of(Tiny)).Write());

    private static (string, byte[]) Nav3d => ("maps/dm_test.nav3d", NavFixtures.Course(Nav3dBspBinding.Of(Tiny)).Write());

    private string Status(NavService nav)
    {
        _log.All.Clear();
        new AiNavStatusCommand(nav, _log).Execute();
        return Assert.Single(_log.All);
    }

    [Fact]
    public void ALevelWithOnlyAHullNavigationHasItsGraph()
    {
        NavService nav = Loaded(Hull);
        Assert.Null(nav.Refusal);
        Assert.Equal(32, nav.Current!.Nav.Graph.CellCount);
    }

    [Fact]
    public void ItsSightTableIsBuiltOverTheHullsFreeSpace()
    {
        NavService nav = Loaded(Hull);
        // The two cells' floors are one open room: every place sees every other.
        Assert.True(nav.Sight!.Visible(0, 31));
    }

    [Fact]
    public void TheLevelsLineNamesTheHullNavigationItRead()
    {
        Loaded(Hull);
        Assert.EndsWith("; from maps/dm_test.navhull\n", Assert.Single(_log.All, l => l.Contains("navigation for dm_test: 32 cells", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void TheLevelsLineNamesTheNav3dItRead()
    {
        Loaded(Nav3d);
        Assert.EndsWith("; from maps/dm_test.nav3d\n", Assert.Single(_log.All, l => l.Contains("navigation for dm_test: ", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void TheStatusNamesTheHullNavigationItRead()
        => Assert.EndsWith(", from maps/dm_test.navhull\n", Status(Loaded(Hull)), StringComparison.Ordinal);

    [Fact]
    public void TheStatusNamesTheNav3dItRead()
        => Assert.EndsWith(", from maps/dm_test.nav3d\n", Status(Loaded(Nav3d)), StringComparison.Ordinal);

    [Fact]
    public void WithBothBesideTheMapTheStatusNamesTheNav3d()
        => Assert.EndsWith(", from maps/dm_test.nav3d\n", Status(Loaded(Nav3d, Hull)), StringComparison.Ordinal);

    [Fact]
    public void AHullNavigationOfAnotherMapIsTheLevelsRefusal()
    {
        byte[] other = TinyBsp.Make(TinyBsp.Stock, planes: 9);
        NavService nav = Loaded(("maps/dm_test.navhull", HullNavFixtures.Floor(Nav3dBspBinding.Of(other)).Write()));
        Assert.Null(nav.Current);
        Assert.StartsWith("maps/dm_test.navhull was built from another map: ", nav.Refusal, StringComparison.Ordinal);
    }
}

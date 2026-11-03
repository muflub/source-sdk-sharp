using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Ai.Tests.Senses;
using SourceSharp.MapFormats.Nav;
using SourceSharp.Server;
using Xunit;
using NavService = SourceSharp.Ai.Hl2mp.Nav.Hl2mpNav;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// A hot reload swaps the composition under a running level and no level init
/// runs for the new one: the navigation comes up on
/// <see cref="IGameLifecycle.OnLevelCarried"/>. Without it a reloaded server
/// answered "no level is loaded" to every bot until the next map.
/// </summary>
public sealed class Hl2mpNavCarryTests : IDisposable
{
    public class Files : DispatchProxy
    {
        public byte[]? Bsp, Nav;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(IEngineFiles.ReadGameFileBytes))
                return null;
            return ((string)args![0]!).EndsWith(".bsp", StringComparison.Ordinal) ? Bsp : Nav;
        }
    }

    private sealed class Mod(string dir) : IModInfo
    {
        public string Id => "test";
        public string GameDir => dir;
        public string ManagedDir => dir;
        public IReadOnlyList<string> AssembliesFor(Side side) => [];
    }

    private sealed class Lines : ILog
    {
        public void Msg(string message) { }
        public void Warning(string message) { }
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai-navcarry-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private NavService Nav()
    {
        byte[] bsp = TinyBsp.Make(TinyBsp.Stock);
        IEngineFiles files = DispatchProxy.Create<IEngineFiles, Files>();
        var f = (Files)(object)files;
        (f.Bsp, f.Nav) = (bsp, NavFixtures.Course(Nav3dBspBinding.Of(bsp)).Write());
        IEngineEntities world = FakeEngine.Of<IEngineEntities>((m, _) => m.Name switch
        {
            nameof(IEngineEntities.FindByName) => EntityHandle.None,
            nameof(IEngineEntities.FindAllByClassname) => Array.Empty<EntityHandle>(),
            _ => throw FakeEngine.Unasked(m),
        });
        return new NavService(files, new Mod(_dir), world, new Lines(), FakeEngine.Of<IEnginePlayers>((m, _) => throw FakeEngine.Unasked(m)), new NoTraces());
    }

    [Fact]
    public void ANewNavHasNoLevelUntilItIsTold()
    {
        NavService nav = Nav();
        Assert.Equal(("no level is loaded", true), (nav.Refusal, nav.Current is null));
    }

    [Fact]
    public void ACarriedLevelLoadsTheNavAsALevelStartDoes()
    {
        NavService nav = Nav();

        ((IGameLifecycle)nav).OnLevelCarried("course");

        Assert.Null(nav.Refusal);
        Assert.Equal("course", nav.Current!.Map);
        Assert.NotNull(nav.Sight);
    }

    [Fact]
    public void ACarriedLevelBuildsTheObstacleFieldAgainWithNothingLearned()
    {
        NavService nav = Nav();
        var player = new EntityHandle(0x0001_0002u);
        Assert.Null(nav.Field);
        Assert.Null(nav.ObstaclesFor(player));

        ((IGameLifecycle)nav).OnLevelCarried("course");

        Assert.NotNull(nav.ObstaclesFor(player));
        Assert.Equal(0L, nav.Field!.Learned);
    }

    private sealed class NoTraces : SourceSharp.Server.Interop.ITraceQueries
    {
        public SourceSharp.Server.Interop.TraceAnswer Line(SourceSharp.Abi.Vector start, SourceSharp.Abi.Vector end, uint mask, EntityHandle pass) => throw new InvalidOperationException("no traces");

        public SourceSharp.Server.Interop.TraceAnswer Hull(SourceSharp.Abi.Vector start, SourceSharp.Abi.Vector end, SourceSharp.Abi.Vector mins, SourceSharp.Abi.Vector maxs, uint mask, EntityHandle pass) => throw new InvalidOperationException("no traces");
    }
}

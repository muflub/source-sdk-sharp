using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Ai.Tests.Senses;
using SourceSharp.MapFormats.Nav;
using SourceSharp.Server;
using Xunit;
using SourceSharp.Ai.Server.Nav;
using NavService = SourceSharp.Ai.Server.Nav.AiNav;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// A hot reload swaps the composition under a running level and no level init
/// runs for the new one: the navigation comes up on
/// <see cref="IGameLifecycle.OnLevelCarried"/>. Without it a reloaded server
/// answered "no level is loaded" to every bot until the next map.
/// </summary>
public sealed class AiNavCarryTests : IDisposable
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

    /// <summary>A game's profile with fixed numbers and one reach for every player.</summary>
    private sealed class Profile(NavNumbers? numbers, JumpReach reach, params TeamSolid[] solids) : INavProfile
    {
        public NavNumbers Numbers(float fileJumpHeight) => numbers ?? NavNumbers.Stock(fileJumpHeight);

        public JumpReach Reach(EntityHandle player) => reach;

        public IReadOnlyList<TeamSolid> TeamSolids(MapEntities entities) => solids;
    }

    /// <summary>The players' teams the fake engine answers, by handle.</summary>
    private readonly Dictionary<EntityHandle, int> _teams = [];

    private NavService Nav(INavProfile? profile = null)
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
        IEnginePlayers players = FakeEngine.Of<IEnginePlayers>((m, a) => m.Name switch
        {
            nameof(IEnginePlayers.GetTeam) => _teams.GetValueOrDefault((EntityHandle)a![0]!),
            _ => throw FakeEngine.Unasked(m),
        });
        return new NavService(files, new Mod(_dir), world, new Lines(), players, new NoTraces(),
                              profile ?? new SourceSharp.Ai.Hl2mp.Nav.Hl2mpNavProfile());
    }

    [Fact]
    public void APlayersObstaclesCarryTheReachTheGamesProfileGivesIt()
    {
        NavService nav = Nav(new Profile(null, JumpReach.Double));
        ((IGameLifecycle)nav).OnLevelCarried("course");

        Assert.Equal(JumpReach.Double, nav.ObstaclesFor(new EntityHandle(0x0001_0002u))!.Reach);
    }

    [Fact]
    public void APlayersObstaclesCloseTheCellsSolidToItsTeamAndOnlyThose()
    {
        var red = new EntityHandle(0x0001_0002u);
        var blue = new EntityHandle(0x0001_0003u);
        (_teams[red], _teams[blue]) = (2, 3);
        var everywhere = new TeamSolid(new AiVector(-1e5f, -1e5f, -1e5f), new AiVector(1e5f, 1e5f, 1e5f), 2);
        NavService nav = Nav(new Profile(null, JumpReach.None, everywhere));
        ((IGameLifecycle)nav).OnLevelCarried("course");

        NavObstacleState blues = nav.ObstaclesFor(blue)!;
        Assert.True(blues.TeamCells!.Count().Blocked > 0);
        Assert.True(blues.CellBlocked(0));
        Assert.Null(nav.ObstaclesFor(red)!.TeamCells);
    }

    [Fact]
    public void HL2MPsProfileGivesNoReachAndTheDefaultNumbers()
    {
        NavService nav = Nav();
        ((IGameLifecycle)nav).OnLevelCarried("course");

        Assert.Equal((JumpReach.None, ""), (nav.ObstaclesFor(new EntityHandle(0x0001_0002u))!.Reach, nav.Current!.NumbersKey));
    }

    [Fact]
    public void TheLevelIsDerivedByTheProfilesNumbers()
    {
        NavService stock = Nav(), tf = Nav(new Profile(TF2NavNumbers.Numbers, JumpReach.None));
        ((IGameLifecycle)stock).OnLevelCarried("course");
        ((IGameLifecycle)tf).OnLevelCarried("course");

        Assert.Equal(16, tf.Current!.NumbersKey.Length);
        Assert.NotEqual(stock.Current!.Nav.Graph.Hash, tf.Current.Nav.Graph.Hash);
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

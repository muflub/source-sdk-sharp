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
/// The level's navigation when the map has none that binds (plan_bots_teams_tf2
/// E.3, E.7): a missing file, a truncated one, a flipped byte, a wrong version
/// and a file of another map each post a build and the refusal says
/// <c>building</c>; <c>ai_nav_build 0</c> keeps the refusal; a failed build is
/// a refusal naming why, retried once only for an I/O error, and never posted
/// again for the same bsp; a level's end cancels the build; a finished build
/// is the level's navigation at the next frame.
/// </summary>
public sealed class AiNavBuildTests : IDisposable
{
    public class Files : DispatchProxy
    {
        public Dictionary<string, byte[]> Held = new(StringComparer.Ordinal);

        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.Name == nameof(IEngineFiles.ReadGameFileBytes) ? Held.GetValueOrDefault((string)args![0]!) : null;
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

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai-navlevel-" + Guid.NewGuid().ToString("N"));
    private readonly Lines _log = new();
    private NavService? _nav;

    public void Dispose()
    {
        _nav?.OnShutdown();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>A binding that builds every map's navigation, as HL2MP's does.</summary>
    private sealed class BuildsEvery : INavBuildPolicy
    {
        public bool Builds(string map) => true;
    }

    private IEngineFiles? _files;

    private IEngineFiles FilesOf(NavService nav) => nav == _nav ? _files! : throw new InvalidOperationException("another rig's files");

    /// <summary>A game's profile with numbers of its own, as TF2's has (plan_bots_teams_tf2 P6 step 2).</summary>
    private sealed class NumbersProfile(NavNumbers numbers) : INavProfile
    {
        public NavNumbers Numbers(float fileJumpHeight) => numbers;

        public JumpReach Reach(EntityHandle player) => JumpReach.None;

        public IReadOnlyList<TeamSolid> TeamSolids(MapEntities entities) => [];
    }

    private NavService Nav(string map, byte[] bsp, byte[]? shipped = null, string build = "1", bool gameBuilds = true, INavProfile? profile = null)
    {
        IEngineFiles files = _files = DispatchProxy.Create<IEngineFiles, Files>();
        var held = ((Files)(object)files).Held;
        held[$"maps/{map}.bsp"] = bsp;
        if (shipped is not null)
            held[$"maps/{map}.nav3d"] = shipped;
        IEngineEntities world = FakeEngine.Of<IEngineEntities>((m, _) => m.Name switch
        {
            nameof(IEngineEntities.FindByName) => EntityHandle.None,
            nameof(IEngineEntities.FindAllByClassname) => Array.Empty<EntityHandle>(),
            _ => throw FakeEngine.Unasked(m),
        });
        _nav = new NavService(files, new Mod(_dir), world, _log, FakeEngine.Of<IEnginePlayers>((m, _) => throw FakeEngine.Unasked(m)), new NoTraces(), profile ?? new SourceSharp.Ai.Hl2mp.Nav.Hl2mpNavProfile(),
                              NavBuildTestKnobs.Of(build), gameBuilds ? new BuildsEvery() : null);
        return _nav;
    }

    /// <summary>Frames until the build in flight has an outcome and it has been taken.</summary>
    private static void UntilBuilt(NavService nav, int seconds = 120)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (nav.Job is not null && clock.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            ((IFrameLifecycle)nav).OnPreThink(0.015f);
            Thread.Sleep(5);
        }

        Assert.Null(nav.Job);
    }

    private static byte[] CourseFor(byte[] bsp) => NavFixtures.Course(Nav3dBspBinding.Of(bsp)).Write();

    private void AssertBuildPosted(NavService nav)
    {
        Assert.True(nav.Building);
        Assert.NotNull(nav.Job);
        Assert.Equal("navigation for dm_test: building", nav.Refusal);
    }

    [Fact]
    public void AMissingNavPostsABuildAndTheRefusalSaysBuilding()
    {
        NavService nav = Nav("dm_test", Tiny);

        nav.Load("dm_test");

        AssertBuildPosted(nav);
    }

    [Fact]
    public void ATruncatedNavPostsABuild()
    {
        byte[] nav = CourseFor(Tiny);
        NavService service = Nav("dm_test", Tiny, nav[..(nav.Length / 2)]);

        service.Load("dm_test");

        AssertBuildPosted(service);
    }

    [Fact]
    public void AFlippedByteInTheSectionDirectoryPostsABuild()
    {
        byte[] nav = CourseFor(Tiny);
        // The first directory entry's length, its high byte: the section now runs past the file.
        nav[Nav3dFormat.EnvelopeBytes + Nav3dFormat.HeaderBytes + 11] ^= 0x40;
        NavService service = Nav("dm_test", Tiny, nav);

        service.Load("dm_test");

        AssertBuildPosted(service);
        Assert.Contains("cannot be read", _log.All[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AWrongVersionPostsABuild()
    {
        byte[] nav = CourseFor(Tiny);
        nav[8] = 9;
        NavService service = Nav("dm_test", Tiny, nav);

        service.Load("dm_test");

        AssertBuildPosted(service);
        Assert.Contains(".nav3d version 9", _log.All[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ANavOfAnotherMapPostsABuild()
    {
        NavService service = Nav("dm_test", Tiny, CourseFor(TinyBsp.Make(TinyBsp.Stock, planes: 9)));

        service.Load("dm_test");

        AssertBuildPosted(service);
    }

    [Fact]
    public void WithBuildingOffTheRefusalStandsAndNothingIsPosted()
    {
        NavService nav = Nav("dm_test", Tiny, build: "0");

        nav.Load("dm_test");

        Assert.Equal(("no navigation for dm_test: expected maps/dm_test.nav3d", false), (nav.Refusal, nav.Building));
        Assert.Null(nav.Job);
    }

    [Fact]
    public void WithNoBuildPolicyFromTheGamesBindingTheRefusalSaysWhyAndNothingIsPosted()
    {
        NavService nav = Nav("dm_test", Tiny, gameBuilds: false);

        nav.Load("dm_test");

        Assert.Equal(("no navigation for dm_test: expected maps/dm_test.nav3d; not built: this game's binding builds no navigation in the server", false),
                     (nav.Refusal, nav.Building));
        Assert.Null(nav.Job);
        Assert.Contains(_log.All, l => l.Contains("not built: this game's binding builds no navigation", StringComparison.Ordinal));
    }

    [Fact]
    public void AFailedBuildIsARefusalNamingWhyAndIsNotRetried()
    {
        NavService nav = Nav("dm_test", Tiny);
        nav.Load("dm_test");

        UntilBuilt(nav);

        Assert.StartsWith("no navigation for dm_test: build failed: ", nav.Refusal, StringComparison.Ordinal);
        Assert.Contains("no world model", nav.Refusal, StringComparison.Ordinal);
        Assert.False(nav.Building);
        Assert.DoesNotContain(_log.All, l => l.Contains("trying once more", StringComparison.Ordinal));
    }

    [Fact]
    public void ABspWhoseBuildFailedIsNotBuiltAgainThisSession()
    {
        NavService nav = Nav("dm_test", Tiny);
        nav.Load("dm_test");
        UntilBuilt(nav);

        nav.Load("dm_test");

        Assert.Null(nav.Job);
        Assert.Contains("build failed", nav.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIoFailureIsRetriedOnceThenRefused()
    {
        // A file where the cache directory goes: every build's write fails.
        Directory.CreateDirectory(Path.Combine(_dir, "maps"));
        File.WriteAllText(Path.Combine(_dir, "maps", "ai-nav"), "in the way");
        NavService nav = Nav("dm_test", File.ReadAllBytes(RepoLayout.Repo("game", "mod_test", "maps", "dm_lockdown.bsp")));
        nav.Load("dm_test");

        UntilBuilt(nav);
        UntilBuilt(nav);

        Assert.Single(_log.All, l => l.Contains("trying once more", StringComparison.Ordinal));
        Assert.StartsWith("no navigation for dm_test: build failed: IOException", nav.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void ALevelsEndCancelsTheBuildAndLeavesNoFile()
    {
        byte[] lockdown = File.ReadAllBytes(RepoLayout.Repo("game", "mod_test", "maps", "dm_lockdown.bsp"));
        NavService nav = Nav("dm_lockdown", lockdown);
        nav.Load("dm_lockdown");
        Assert.True(nav.Building);
        NavBuildJob job = nav.Job!;

        ((IGameLifecycle)nav).OnLevelShutdown();

        // The build was cut short, not waited out: a bound on the stop's wall
        // time failed on a box at load 50.
        Assert.IsType<NavBuildOutcome.Cancelled>(job.Outcome);
        Assert.True(job.Cells.Done < job.Cells.Total || job.Cells.Total == 0, $"{job.Cells.Done} of {job.Cells.Total} cells built");
        Assert.Null(nav.Job);
        Assert.Equal("no level is loaded", nav.Refusal);
        Assert.False(File.Exists(nav.Store.PathFor("dm_lockdown", BuiltNavStore.Sha256(lockdown))));
    }

    [Fact]
    public void BuildingIsOnByDefault()
        => Assert.Equal("1", typeof(AiNavBuildConVar).GetCustomAttribute<ConVarAttribute>()!.DefaultValue);

    [Fact]
    public void APrebuildOfAnotherMapBuildsItWithoutHoldingTheLevelsBots()
    {
        NavService nav = Nav("dm_test", Tiny, CourseFor(Tiny));
        ((Files)(object)FilesOf(nav)).Held["maps/dm_other.bsp"] = TinyBsp.Make(TinyBsp.Stock, planes: 9);
        nav.Load("dm_test");

        nav.Prebuild(["dm_other"]);
        ((IFrameLifecycle)nav).OnPreThink(0.015f);

        Assert.Equal("dm_other", nav.Job?.Map);
        Assert.False(nav.Building);
        Assert.Null(nav.Refusal);
        Assert.StartsWith("navigation for dm_other: ", nav.BuildStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void APrebuildForAGameWhoseBindingAsksForNoBuildQueuesNothingAndSaysSo()
    {
        NavService nav = Nav("dm_test", Tiny, CourseFor(Tiny), gameBuilds: false);
        ((Files)(object)FilesOf(nav)).Held["maps/dm_other.bsp"] = TinyBsp.Make(TinyBsp.Stock, planes: 9);
        nav.Load("dm_test");

        string said = nav.Prebuild(["dm_other"]);
        ((IFrameLifecycle)nav).OnPreThink(0.015f);

        Assert.Null(nav.Job);
        Assert.Empty(nav.Queued);
        Assert.Equal("ai_nav_prebuild: 0 map(s) queued, 0 waiting; 1 refused: this game's binding builds no navigation for them", said);
    }

    [Fact]
    public void APrebuildOfAMapWithANavSkipsIt()
    {
        NavService nav = Nav("dm_test", Tiny, CourseFor(Tiny));
        nav.Load("dm_test");

        nav.Prebuild(["dm_test"]);
        ((IFrameLifecycle)nav).OnPreThink(0.015f);

        Assert.Null(nav.Job);
        Assert.Contains(_log.All, l => l.Contains("ai_nav_prebuild dm_test: navigation is there (shipped)", StringComparison.Ordinal));
    }

    [Fact]
    public void APrebuildQueueGoesWithTheLevel()
    {
        NavService nav = Nav("dm_test", Tiny, CourseFor(Tiny));
        nav.Load("dm_test");
        nav.Prebuild(["dm_a", "dm_b"]);

        ((IGameLifecycle)nav).OnLevelShutdown();

        Assert.Empty(nav.Queued);
    }

    [Fact]
    public void AFinishedBuildIsTheLevelsNavigationAtTheNextFrame()
    {
        byte[] lockdown = File.ReadAllBytes(RepoLayout.Repo("game", "mod_test", "maps", "dm_lockdown.bsp"));
        NavService nav = Nav("dm_lockdown", lockdown);
        nav.Load("dm_lockdown");
        int before = nav.Version;

        UntilBuilt(nav);

        Assert.Null(nav.Refusal);
        Assert.True(nav.Current!.Built);
        Assert.Equal(before + 1, nav.Version);
        Assert.False(nav.Building);
        Assert.Contains(_log.All, l => l.Contains("navigation for dm_lockdown: building 100 %", StringComparison.Ordinal));
        Assert.Contains(_log.All, l => l.Contains("(built)", StringComparison.Ordinal));
    }

    /// <summary>The server's build is derived by the game's profile, as a shipped file is: its numbers name the graph.</summary>
    [Fact]
    public void ABuildIsDerivedByTheGamesProfile()
    {
        byte[] lockdown = File.ReadAllBytes(RepoLayout.Repo("game", "mod_test", "maps", "dm_lockdown.bsp"));
        NavService nav = Nav("dm_lockdown", lockdown, profile: new NumbersProfile(SourceSharp.Ai.TF2.Nav.TFNavProfile.ModNumbers));
        nav.Load("dm_lockdown");

        UntilBuilt(nav);

        Assert.True(nav.Current!.Built);
        Assert.Equal(16, nav.Current.NumbersKey.Length);
    }
}

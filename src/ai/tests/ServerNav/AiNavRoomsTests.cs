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
/// A level in rooms (plan_bots_teams_tf2_part_g R.2 stage A): the level's navigation is one room's,
/// made ready off the main thread, and it follows the bots. The map: two cells of eight columns in a
/// row, a floor in each, apart (region 0 in cell 0, region 1 in the far half of cell 1), each a room
/// file of its own beside the room index and the room table, which names a whole navigation over the
/// leaf limit; the spawn is in room 0.
/// </summary>
public sealed class AiNavRoomsTests : IDisposable
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

    // Room 0's floor and room 1's, where a body stands on each.
    private static readonly AiVector In0 = new(64f, 64f, 16f), In1 = new(128f + 96f, 64f, 16f);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai-navrooms-level-" + Guid.NewGuid().ToString("N"));
    private readonly Lines _log = new();
    private NavService? _nav;
    private Dictionary<string, byte[]> _held = null!;

    public void Dispose()
    {
        _nav?.OnShutdown();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>The map's files: each room's, the index and the table (its whole navigation <paramref name="wholeLeaves"/> leaves).</summary>
    private static List<(string Name, byte[] Bytes)> RoomFiles(long wholeLeaves)
    {
        var f = new HullNavFixture(2, 1, 8, 16) { Bsp = Nav3dBspBinding.Of(Tiny) };
        uint floor = f.Key(NavHullFlags.Grounded | NavHullFlags.Walkable, 16f, 144f, 144f);
        f.Box(0, 0, 0, 8, 8, 1, 8, floor);
        f.Box(1, 4, 0, 8, 8, 1, 8, floor);
        NavHullReader whole = f.Open();
        NavHullRegions regions = NavHullRegions.Of(whole);
        Assert.Equal(2, regions.Count);
        NavHullLevel level = whole.ToLevel();
        var files = new List<(string, byte[])>();
        var rooms = new List<NavRoom>();
        for (int r = 0; r < regions.Count; r++)
        {
            string file = $"dm_test.r{r}.navhull";
            files.Add(($"maps/{file}", NavHullWriter.Write(NavHullSplit.Region(whole, level, regions, r).Level)));
            AiVector spawn = r == 0 ? In0 : In1;
            rooms.Add(new NavRoom(r, file, 64, [spawn], [], [], [], []) { Spawns = r == 0 ? [spawn] : [] });
        }

        files.Add(("maps/dm_test.navrooms", NavRoomIndex.Of(whole, regions).Write()));
        files.Add(("maps/dm_test.navrooms.json", System.Text.Encoding.UTF8.GetBytes(new NavRoomTable("dm_test", NavRoomTable.RulesRevision, wholeLeaves, rooms).ToJson())));
        return files;
    }

    private NavService Loaded(IEnumerable<(string Name, byte[] Bytes)> files, int threads = 2)
    {
        IEngineFiles seam = DispatchProxy.Create<IEngineFiles, AiNavBuildTests.Files>();
        _held = ((AiNavBuildTests.Files)(object)seam).Held;
        _held["maps/dm_test.bsp"] = Tiny;
        foreach ((string name, byte[] bytes) in files)
            _held[name] = bytes;
        IEngineEntities world = FakeEngine.Of<IEngineEntities>((m, _) => m.Name switch
        {
            nameof(IEngineEntities.FindByName) => EntityHandle.None,
            nameof(IEngineEntities.FindAllByClassname) => Array.Empty<EntityHandle>(),
            _ => throw FakeEngine.Unasked(m),
        });
        _nav = new NavService(seam, new Mod(_dir), world, _log, FakeEngine.Of<IEnginePlayers>((m, _) => throw FakeEngine.Unasked(m)), new NoTraces(),
                              new SourceSharp.Ai.Hl2mp.Nav.Hl2mpNavProfile(), NavBuildTestKnobs.Of("1", threads));
        _nav.Load("dm_test");
        return _nav;
    }

    /// <summary>Frames until the room job lands (or ten seconds).</summary>
    private static void Settle(NavService nav)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (nav.RoomComing >= 0 && clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            Thread.Sleep(5);
            nav.OnPreThink(0.015f);
        }

        Assert.Equal(-1, nav.RoomComing);
    }

    [Fact]
    public void ALevelInRoomsMakesItsSpawnsRoomReadyOffTheMainThread()
    {
        NavService nav = Loaded(RoomFiles(long.MaxValue / 2));
        Assert.Null(nav.Current);
        Assert.True(nav.Building, "bots wait for the first room");
        Assert.Equal(0, nav.RoomComing);
        Settle(nav);
        Assert.Equal(0, nav.RoomInPlay);
        Assert.NotNull(nav.Current);
        Assert.Equal("dm_test.r0", nav.Current!.Map);
        Assert.Equal("maps/dm_test.r0.navhull", nav.Current.File.Name);
        Assert.Equal(1, nav.Version);
        Assert.Null(nav.Sight);
        Assert.False(nav.Building);

        // The room's graph is its file's, derived a tile at a time and merged.
        var source = NavHullNavSource.Over(NavHullReader.Open(_held["maps/dm_test.r0.navhull"]));
        DerivedNav expected = NavGraphReduce.DeriveCoarse(source, new SourceSharp.Ai.Hl2mp.Nav.Hl2mpNavProfile().Numbers(source.JumpHeight), NavEdgeCosts.Default, [], []);
        Assert.Equal(expected.Graph.Hash, nav.Current.Nav.Graph.Hash);
        Assert.Contains(_log.All, l => l.Contains("room r0 in play", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRoomInPlayFollowsTheBotsAndIsReadFromTheCacheTheSecondTime()
    {
        NavService nav = Loaded(RoomFiles(long.MaxValue / 2));
        Settle(nav);
        ulong first = nav.Current!.Nav.Graph.Hash;

        // A bot in room 0 keeps it; one carried to room 1 brings room 1.
        nav.FollowRooms([new RoomActor(In0, 2, true)]);
        Assert.Equal(-1, nav.RoomComing);
        nav.FollowRooms([new RoomActor(In1, 2, true), new RoomActor(In0, 3, false)]);
        Assert.Equal(1, nav.RoomComing);
        Settle(nav);
        Assert.Equal((1, 2), (nav.RoomInPlay, nav.Version));
        Assert.False(nav.LastRoom.FromCache);

        // Back to room 0: its graph comes from the cache this time, the same graph.
        nav.FollowRooms([new RoomActor(In0, 2, true)]);
        Settle(nav);
        Assert.Equal((0, 3), (nav.RoomInPlay, nav.Version));
        Assert.True(nav.LastRoom.FromCache);
        Assert.Equal(first, nav.Current!.Nav.Graph.Hash);
    }

    [Fact]
    public void ARoomIsMadeWithItsSightTableOnlyWithAnEnemyOfABotInIt()
    {
        NavService nav = Loaded(RoomFiles(long.MaxValue / 2));
        Settle(nav);
        nav.FollowRooms([new RoomActor(In1, 2, true), new RoomActor(In1, 2, false)]);
        Settle(nav);
        Assert.Null(nav.Sight);
        nav.FollowRooms([new RoomActor(In0, 2, true), new RoomActor(In0, 3, false)]);
        Settle(nav);
        Assert.Equal(0, nav.RoomInPlay);
        Assert.NotNull(nav.Sight);
    }

    [Fact]
    public void WhereMostBotsAreWinsAndATieKeepsTheRoomInPlay()
    {
        NavService nav = Loaded(RoomFiles(long.MaxValue / 2));
        Settle(nav);
        nav.FollowRooms([new RoomActor(In1, 2, true), new RoomActor(In0, 2, true)]);
        Assert.Equal(-1, nav.RoomComing);
        nav.FollowRooms([new RoomActor(In1, 2, true), new RoomActor(In1, 2, true), new RoomActor(In0, 2, true)]);
        Assert.Equal(1, nav.RoomComing);
    }

    [Fact]
    public void ALevelWhoseWholeNavigationIsWithinTheLimitIsNotInRooms()
    {
        List<(string Name, byte[] Bytes)> files = RoomFiles(1000);
        files.Add(("maps/dm_test.navhull", HullNavFixtures.Floor(Nav3dBspBinding.Of(Tiny)).Write()));
        NavService nav = Loaded(files);
        Assert.Equal(-1, nav.RoomComing);
        Assert.NotNull(nav.Current);
        Assert.Equal("maps/dm_test.navhull", nav.Current!.File.Name);
        Assert.Null(nav.Rooms);
    }

    [Fact]
    public void ARoomFileOfAnotherMapFailsThatRoomByName()
    {
        List<(string Name, byte[] Bytes)> files = RoomFiles(long.MaxValue / 2);
        byte[] other = (byte[])Tiny.Clone();
        other[^1] ^= 0xFF;
        files[0] = (files[0].Name, HullNavFixtures.Floor(Nav3dBspBinding.Of(other)).Write());
        NavService nav = Loaded(files);
        Settle(nav);
        Assert.Null(nav.Current);
        Assert.Contains("room r0 failed", nav.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void RoomsMadeByOtherRulesAreSaidAndTheLevelLoadsWhole()
    {
        List<(string Name, byte[] Bytes)> files = RoomFiles(long.MaxValue / 2);
        int at = files.FindIndex(f => f.Name.EndsWith(".json", StringComparison.Ordinal));
        string json = System.Text.Encoding.UTF8.GetString(files[at].Bytes).Replace($"\"revision\": {NavRoomTable.RulesRevision}", "\"revision\": 999", StringComparison.Ordinal);
        files[at] = (files[at].Name, System.Text.Encoding.UTF8.GetBytes(json));
        files.Add(("maps/dm_test.navhull", HullNavFixtures.Floor(Nav3dBspBinding.Of(Tiny)).Write()));
        NavService nav = Loaded(files);
        Assert.Null(nav.Rooms);
        Assert.Equal("maps/dm_test.navhull", nav.Current?.File.Name);
        Assert.Contains(_log.All, l => l.Contains("room rules 999", StringComparison.Ordinal) && l.Contains("loading dm_test whole", StringComparison.Ordinal));
    }

    [Fact]
    public void ARoomsFramesAreReadFromItsJobsStartToTwoSecondsInPlay()
    {
        NavService nav = Loaded(RoomFiles(long.MaxValue / 2));
        Settle(nav);
        Assert.DoesNotContain(_log.All, l => l.Contains("room r0 frames:", StringComparison.Ordinal));
        for (int i = 0; i < NavService.WatchFramesAfter; i++)
            nav.OnPreThink(0.015f);
        string line = Assert.Single(_log.All, l => l.Contains("room r0 frames:", StringComparison.Ordinal));
        Assert.Contains($"to {NavService.WatchFramesAfter} frames in play", line, StringComparison.Ordinal);
    }

    private sealed class CountingEyes : IObstacleEyes
    {
        public int Finds { get; private set; }

        public EntityHandle Find(string name, string className, AiVector near)
        {
            Finds++;
            return new EntityHandle(7u);
        }

        public bool Alive(EntityHandle entity) => true;

        public AiVector Origin(EntityHandle entity) => new(1f, 2f, 3f);

        public AiVector Angles(EntityHandle entity) => default;
    }

    [Fact]
    public void AnObstaclesEntityIsFoundOnceForEveryRoom()
    {
        var engine = new CountingEyes();
        var eyes = new RememberingObstacleEyes(engine);
        for (int room = 0; room < 3; room++)
        {
            Assert.Equal(new EntityHandle(7u), eyes.Find("door", "func_door", new AiVector(1f, 1f, 1f)));
            Assert.Equal(new EntityHandle(7u), eyes.Find("", "prop_physics", new AiVector(5f, 5f, 5f)));
        }

        Assert.Equal((2, 2), (engine.Finds, eyes.Asked));
        Assert.Equal(new AiVector(1f, 2f, 3f), eyes.Origin(new EntityHandle(7u)));
    }

    [Fact]
    public void ARoomIsDerivedOnAtMostFourThreads()
    {
        NavService nav = Loaded(RoomFiles(long.MaxValue / 2), threads: 16);
        Settle(nav);
        Assert.Contains(_log.All, l => l.Contains("room r0 being made ready on 4 thread(s)", StringComparison.Ordinal));
        nav.OnLevelShutdown();
        _log.All.Clear();
        nav = Loaded(RoomFiles(long.MaxValue / 2), threads: 3);
        Assert.Contains(_log.All, l => l.Contains("room r0 being made ready on 3 thread(s)", StringComparison.Ordinal));
        Settle(nav);
    }

    [Fact]
    public void TheLevelsEndStopsARoomInFlight()
    {
        NavService nav = Loaded(RoomFiles(long.MaxValue / 2));
        nav.OnLevelShutdown();
        Assert.Equal(-1, nav.RoomComing);
        Assert.Equal(-1, nav.RoomInPlay);
        Assert.Null(nav.Rooms);
    }
}

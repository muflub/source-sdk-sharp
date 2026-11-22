using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.Tests.Senses;
using SourceSharp.Ai.Tests.ServerNav;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerBinding;

/// <summary>
/// The brains' world follows the level's navigation (plan_bots_teams_tf2_part_g R.2 item 6): when a
/// room of a level in rooms is installed (the navigation's version moves and its graph is another),
/// the world is made again over the new graph, the map's knowledge kept, and a recording says so with
/// a NavChange record (format 9). A version that moves with the same graph changes nothing.
/// </summary>
public sealed class NavChangeTests : IDisposable
{
    private sealed class Rooms : IAiNavigation
    {
        public LoadedNav? Current { get; set; }
        public string? Refusal => Current is null ? "coming" : null;
        public NavObstacleState? Obstacles => null;
        public NavObstacleState? ObstaclesFor(EntityHandle player) => null;
        public NavSight? Sight => null;
        public ChokeSlot? Chokes => null;
        public int Version { get; set; }
        public int RoomInPlay { get; set; } = -1;
    }

    private sealed class Quiet : ILog
    {
        public List<string> All { get; } = [];
        public void Msg(string message) => All.Add(message);
        public void Warning(string message) => All.Add(message);
    }

    private readonly string _file = Path.Combine(Path.GetTempPath(), "ai-navchange-" + Guid.NewGuid().ToString("N") + ".airec");

    public void Dispose()
    {
        if (File.Exists(_file))
            File.Delete(_file);
    }

    /// <summary>HL2MP's game with no weapons and an empty map.</summary>
    private sealed class Game : IBotGame
    {
        private readonly IBotGame _hl2mp = TestGames.Hl2mp();

        public IDecider Decider(BrainSetup setup) => _hl2mp.Decider(setup);
        public WeaponKnowledge Weapons() => new([]);
        public AbilityTable Abilities => default;
        public MapKnowledge Map(string mapName, MapEntities entities, WeaponKnowledge weapons) => new(mapName, [], [], weapons);
        public KeyValuePair<string, string>[] UserInfo(AiRandom random, int team, int teams) => [];
        public string GrenadeWeapon => "";
        public bool Thrown(in WeaponType weapon) => false;
        public IBotGameView View { get; } = new PlainBotGameView();
    }

    private sealed class NoSpace : IVoxelSpace
    {
        public bool Free(AiVector p) => true;
    }

    private static LoadedNav Room(int columns, string name)
    {
        var src = new FakeNavSource();
        for (int x = 0; x < columns; x++)
            src.Add(x, 0, 0f, 200f);
        DerivedNav d = NavGraphFromNav3d.Derive(src, SourceSharp.Ai.TF2.Nav.TFNavProfile.ModNumbers, NavEdgeCosts.Default, []);
        return new LoadedNav(name, new NavFile(NavFile.Hull, $"maps/{name}.navhull", src, new NoSpace()), d, new MapEntities([], null), "bsp", "nav", false, 0);
    }

    private static ServerAiSession Session(Rooms nav, Quiet log)
    {
        IEnginePlayers nobody = FakeEngine.Of<IEnginePlayers>((m, _) => m.Name == "GetHandle" ? EntityHandle.None : throw FakeEngine.Unasked(m));
        return new ServerAiSession(nav, new Game(), nobody, log, DeclaredConVars.Fresh<AiThreadsConVar>(), DeclaredConVars.FreshAt<AiSyncConVar>("1"));
    }

    private List<(AiRecordType Type, byte[] Payload)> Records()
    {
        using var f = File.OpenRead(_file);
        AiRecordFormat.ReadFileHeader(f);
        var all = new List<(AiRecordType, byte[])>();
        while (AiRecordFormat.TryReadRecord(f, out AiRecordType type, out byte[] payload))
            all.Add((type, payload));
        return all;
    }

    [Fact]
    public void ARoomInstalledRemakesTheWorldOverItsGraphAndTheRecordingSaysSo()
    {
        var nav = new Rooms();
        var log = new Quiet();
        ServerAiSession ai = Session(nav, log);
        ai.OnLevelInitPostEntity("jump_test");
        MapKnowledge map = ai.Session.World!.Map;
        Assert.Null(ai.Session.World.Graph);
        Assert.False(ai.FollowNav());
        ai.Recording.Start(_file, new AiRecordingHeader(1, "jump_test", 0, 0, 8, 4));

        LoadedNav hub = Room(6, "jump_test.r19");
        (nav.Current, nav.Version, nav.RoomInPlay) = (hub, 1, 19);
        Assert.True(ai.FollowNav());
        Assert.Same(hub.Nav.Graph, ai.Session.World!.Graph);
        Assert.Same(map, ai.Session.World.Map);
        Assert.Equal("maps/jump_test.r19.navhull", ai.RoomNav);
        Assert.False(ai.FollowNav());

        LoadedNav course = Room(9, "jump_test.r62");
        (nav.Current, nav.Version, nav.RoomInPlay) = (course, 2, 62);
        Assert.True(ai.FollowNav());
        Assert.Same(course.Nav.Graph, ai.Session.World!.Graph);
        ai.Recording.Stop();

        AiRecordedNavChange[] changes = [.. Records().Where(r => r.Type == AiRecordType.NavChange)
                                                      .Select(r => AiRecordFormat.ReadNavChange(new BinaryReader(new MemoryStream(r.Payload))))];
        Assert.Equal([new AiRecordedNavChange(hub.Nav.Graph.Hash, "maps/jump_test.r19.navhull"), new AiRecordedNavChange(course.Nav.Graph.Hash, "maps/jump_test.r62.navhull")], changes);
        ai.OnLevelShutdownPreEntity();
    }

    [Fact]
    public void AVersionThatMovesWithTheSameGraphChangesNothing()
    {
        LoadedNav level = Room(6, "dm_test");
        var nav = new Rooms { Current = level, Version = 1 };
        ServerAiSession ai = Session(nav, new Quiet());
        ai.OnLevelInitPostEntity("dm_test");
        Assert.Same(level.Nav.Graph, ai.Session.World!.Graph);
        nav.Version = 2;
        Assert.False(ai.FollowNav());
        Assert.Null(ai.RoomNav);
        ai.OnLevelShutdownPreEntity();
    }
}

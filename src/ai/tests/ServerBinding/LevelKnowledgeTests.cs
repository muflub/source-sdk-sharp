using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.Tests.Senses;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerBinding;

/// <summary>
/// A level's knowledge is the game's to give, navigation or none (plan_bots_teams_tf2 P6 step 5):
/// without a .nav3d the game is asked over an empty lump, so what it knows from elsewhere (TF2's
/// placed entities) is known; with neither the bots know nothing, as before.
/// </summary>
public sealed class LevelKnowledgeTests
{
    private sealed class NoNav : IAiNavigation
    {
        public LoadedNav? Current => null;
        public string? Refusal => "no navigation";
        public NavObstacleState? Obstacles => null;
        public NavObstacleState? ObstaclesFor(EntityHandle player) => null;
        public NavSight? Sight => null;
        public ChokeSlot? Chokes => null;
    }

    private sealed class Quiet : ILog
    {
        public void Msg(string message) { }
        public void Warning(string message) { }
    }

    /// <summary>HL2MP's game, whose map knowledge is told by the fact.</summary>
    private sealed class Game(Func<MapEntities, MapKnowledge> map) : IBotGame
    {
        private readonly IBotGame _hl2mp = TestGames.Hl2mp();

        public IDecider Decider(BrainSetup setup) => _hl2mp.Decider(setup);
        public WeaponKnowledge Weapons() => new([]);
        public AbilityTable Abilities => default;
        public MapKnowledge Map(string mapName, MapEntities entities, WeaponKnowledge weapons) => map(entities);
        public KeyValuePair<string, string>[] UserInfo(AiRandom random, int team, int teams) => [];
        public string GrenadeWeapon => "";
        public bool Thrown(in WeaponType weapon) => false;
        public IBotGameView View { get; } = new PlainBotGameView();
    }

    private static MapKnowledge LevelUp(IBotGame game)
    {
        IEnginePlayers nobody = FakeEngine.Of<IEnginePlayers>((m, _) => m.Name == "GetHandle" ? EntityHandle.None : throw FakeEngine.Unasked(m));
        var ai = new ServerAiSession(new NoNav(), game, nobody, new Quiet(), DeclaredConVars.Fresh<AiThreadsConVar>(), DeclaredConVars.FreshAt<AiSyncConVar>("1"));
        ai.OnLevelInitPostEntity("ctf_test");
        MapKnowledge map = ai.Session.World!.Map;
        ai.OnLevelShutdownPreEntity();
        return map;
    }

    [Fact]
    public void WithoutNavigationTheGameIsAskedOverAnEmptyLump()
    {
        int lumpSize = -1;
        MapKnowledge map = LevelUp(new Game(lump =>
        {
            lumpSize = lump.All.Count;
            return new MapKnowledge("ctf_test", [], [], new WeaponKnowledge([]), [new TeamSpawn(2, new AiVector(1, 2, 3))]);
        }));
        Assert.Equal(0, lumpSize);
        Assert.Equal([new TeamSpawn(2, new AiVector(1, 2, 3))], map.TeamSpawns.ToArray());
    }

    [Fact]
    public void Hl2mpWithoutNavigationKnowsNothingOfTheMap()
    {
        MapKnowledge map = LevelUp(new Game(lump => TestGames.Hl2mp().Map("ctf_test", lump, new WeaponKnowledge([]))));
        Assert.Equal((0, 0, 0), (map.Items.Length, map.Spawns.Length, map.TeamSpawns.Length));
    }
}

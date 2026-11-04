using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Conditions;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// TF2's round reset (tf2 ruling N2): <c>CTFGameRules::CleanUpMap</c> over the
/// core's <see cref="IMapCleanup"/> with TF's preserve list, and the list's
/// answers where they differ from the core's.
/// </summary>
public sealed class TFMapCleanupTests
{
    private sealed class RecordingCleanup(List<string> order) : IMapCleanup
    {
        public List<IMapCleanupFilter> Filters { get; } = [];

        public MapCleanupReport CleanUpMap(IMapCleanupFilter filter)
        {
            Filters.Add(filter);
            order.Add("cleanup");
            return new MapCleanupReport(0, 0, 0, 0, 0, 0, new MapRespawn(Created: 7, 7, 0, 0, 0, 0, 0, 7), 0, 0);
        }
    }

    private sealed class Conditions(List<string> order) : ITFPlayerConditions
    {
        public TFConditions? Of(int playerIndex) => null;
        public TFCondAdd Add(EntityHandle player, TFCond cond, float duration = TFConditions.Permanent, EntityHandle provider = default) => default;
        public bool Remove(EntityHandle player, TFCond cond, bool ignoreDuration = false) => false;
        public bool InCond(EntityHandle player, TFCond cond) => false;

        public TFCondMask RemoveAll(EntityHandle player)
        {
            order.Add($"conditions:{player}");
            return default;
        }
    }

    [Fact]
    public void TheRoundResetTakesEveryPlayersConditionsThenCleansWithTFsList()
    {
        var order = new List<string>();
        var engine = TestServices.Fake();
        var live = new EntityDirectory(new RecordingLog());
        var players = new[] { new EntityHandle(0x0001_0001u), new EntityHandle(0x0002_0002u) };
        foreach (var handle in players)
            Assert.True(live.Bind(handle, new SsPlayer(engine, engine, TestServices.Impacts(engine), TestServices.Fluid(), TestServices.InfiniteAux(), TestServices.PrintDamage()) { Handle = handle }));

        var services = new ServiceContainer();
        services.AddInstance<ITFPlayerConditions>(new Conditions(order));
        var cleanup = new RecordingCleanup(order);
        var entities = new ScriptedEntityRegistry(TestEngines.Server());
        var world = new CoreRoundWorld(cleanup, live, entities, engine,
                                       new RoundTimers(entities), services, new RecordingLog());

        int created = world.ResetMap();

        Assert.Equal(new[] { $"conditions:{players[0]}", $"conditions:{players[1]}", "cleanup" }, order);
        Assert.IsType<TFMapCleanupFilter>(Assert.Single(cleanup.Filters));
        Assert.Equal(7, created);
    }

    [Theory]
    [InlineData("func_brush", true)]          // HL2MP keeps it; the teamplay lists do not
    [InlineData("item_teamflag", true)]
    [InlineData("logic_relay", true)]
    [InlineData("keyframe_rope", false)]      // TF's list
    [InlineData("TF_OBJECTIVE_RESOURCE", false)]   // Q_stricmp
    [InlineData("env_sprite", false)]         // the teamplay base's list
    [InlineData("tf_player_data", false)]     // the port's: the player's m_Shared
    [InlineData("ss_weapon", false)]          // the port's: tf_weapon_*
    [InlineData("tf_building_resource", false)]   // the port's: made at level init, never by the map
    public void TFsFilterCreatesAgainEveryClassItsListsDoNotName(string className, bool created)
        => Assert.Equal(created, new TFMapCleanupFilter().ShouldCreateEntity(className));

    [Fact]
    public void ATFWeaponClassIsKeptByItsPrefixCaseAndAll()
    {
        var filter = new TFMapCleanupFilter();

        var weapon = new Probe("tf_weapon_rocketlauncher");
        var shouted = new Probe("TF_WEAPON_ROCKETLAUNCHER");   // Q_strstr is case-sensitive
        var other = new Probe("item_teamflag");

        Assert.True(filter.RoundCleanupShouldIgnore(weapon));
        Assert.False(filter.RoundCleanupShouldIgnore(shouted));
        Assert.False(filter.RoundCleanupShouldIgnore(other));
    }

    private sealed class Probe : ScriptedEntity
    {
        public Probe(string className) => ClassName = className;
    }
}

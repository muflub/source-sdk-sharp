using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Player;

using TF2.Tests.Player;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// The server half of the <c>tf_player_data</c> carrier: one per attached
/// player through the core's composite, found by player index, owning the
/// class (joinclass: TFJoinClassCommandTests).
/// </summary>
public sealed class TFPlayerCarriersTests
{
    private const int PlayerIndex = 3;
    private static readonly EntityHandle Player = new(0x0001_0000u | PlayerIndex);
    private static readonly EntityHandle CarrierHandle = new(0x0002_0041u);

    private sealed class Hooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class Directory(EntityHandle handle, ScriptedEntity entity) : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle h) => h == handle ? entity : null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => className == TFEntityNames.PlayerData;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    private sealed class World
    {
        public readonly RecordingEngine Engine = new();
        public readonly RecordingLog Log = new();
        public readonly TFPlayerDataEntity Entity;
        public readonly PlayerComposites Composites;
        public readonly TFPlayerCarriers Carriers;

        public World()
        {
            Engine.Returns("Entities.Create", CarrierHandle);
            Engine.Returns("Entities.IsValid", true);
            Engine.Returns("Entities.GetIndex", PlayerIndex);
            Engine.Returns("Entities.GetOrigin", default(Vector));
            Engine.Returns("Players.GetHandle", Player);
            Engine.Returns("Clock.Now", 0f);

            Entity = new TFPlayerDataEntity(Engine);
            Entity.State.BindLocal();

            Carriers = new TFPlayerCarriers();
            var source = new TFPlayerComponents(Engine, new Directory(CarrierHandle, Entity), Carriers, Log, new TFPlayerItems(),
                                                new TFPlayerConditions(Carriers, Engine),
                                                new TF2.Tests.Entities.Items.FakeMapPlayers(), new TF2.Tests.Rules.FakeClock(), Engine,
                                                EconTestFiles.TrimmedSchema(), new PoollessGiver());
            Composites = new PlayerComposites(Log, new Hooks<IPlayerComponentSource>(source), Engine, Engine);
        }

        public void Join() => ((IClientLifecycle)Composites).OnClientActive(PlayerIndex);
        public void Leave() => ((IClientLifecycle)Composites).OnClientDisconnected(PlayerIndex);
    }

    [Fact]
    public void An_active_player_gets_one_carrier_spawned_and_owned_by_their_index()
    {
        var world = new World();
        Assert.Null(world.Carriers.Get(PlayerIndex));

        world.Join();

        var create = Assert.Single(world.Engine.AllOf("Entities.Create"));
        Assert.Equal(TFEntityNames.PlayerData, create.ArgAs<string>(0));
        Assert.Equal(CarrierHandle, Assert.Single(world.Engine.AllOf("Entities.Spawn")).ArgAs<EntityHandle>(0));
        Assert.Same(world.Entity.State, world.Carriers.Get(PlayerIndex));
        Assert.Equal(PlayerIndex, world.Entity.State.OwnerIndex);
        Assert.Equal(CarrierHandle, world.Carriers.CarrierOf(PlayerIndex));
    }

    /// <summary>
    /// The carriers build from an empty container: the movement rules ask for
    /// them at the core's movement resolution, before the composite exists
    /// (regression: the first live load failed on PlayerComposites).
    /// </summary>
    [Fact]
    public void The_carriers_need_nothing_from_the_container()
    {
        Assert.NotNull(new ServiceContainer().Create<TFPlayerCarriers>());
    }

    [Fact]
    public void The_composite_finds_the_same_carrier()
    {
        var world = new World();
        world.Join();

        Assert.Same(world.Entity.State, world.Composites.Get<TFPlayerCarrier>(PlayerIndex)!.State);
    }

    [Fact]
    public void A_departure_removes_the_carrier()
    {
        var world = new World();
        world.Join();
        Assert.Equal(0, world.Engine.CountOf("Entities.Remove"));

        world.Leave();

        Assert.Equal(CarrierHandle, Assert.Single(world.Engine.AllOf("Entities.Remove")).ArgAs<EntityHandle>(0));
        Assert.Null(world.Carriers.Get(PlayerIndex));
        Assert.Equal(EntityHandle.None, world.Carriers.CarrierOf(PlayerIndex));
        Assert.Equal(0, world.Carriers.Count);   // the departed player's entry is gone, not only emptied
    }

    [Fact]
    public void The_carrier_is_sent_to_everyone()
    {
        var world = new World();

        world.Entity.OnSpawn();

        Assert.True(Assert.Single(world.Engine.AllOf("EntityRender.SetTransmitAlways")).ArgAs<bool>(1));
    }

    [Fact]
    public void The_shared_rules_reach_the_carrier_by_player_index_on_the_server()
    {
        var world = new World();
        world.Join();
        ITFPlayerData data = world.Carriers;

        Assert.Same(world.Entity.State, data.Predicted(PlayerIndex));
        Assert.Same(world.Entity.State, data.Networked(PlayerIndex));
        Assert.Null(data.Predicted(PlayerIndex + 1));
    }

    [Fact]
    public void Set_class_writes_the_class_and_clears_the_air_dash()
    {
        var world = new World();
        world.Join();
        world.Entity.State.AirDash = 1;

        Assert.True(world.Carriers.SetClass(PlayerIndex, TFClass.Scout));

        Assert.Equal((int)TFClass.Scout, world.Entity.State.PlayerClass);
        Assert.Equal(0, world.Entity.State.AirDash);
        Assert.Equal(TFClass.Scout, world.Carriers.ClassOf(Player));
    }

    [Fact]
    public void A_player_without_a_carrier_has_no_class_and_cannot_be_given_one()
    {
        var world = new World();

        Assert.False(world.Carriers.SetClass(PlayerIndex, TFClass.Scout));
        Assert.Equal(TFClass.Undefined, world.Carriers.ClassOf(Player));
    }
}

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// CBaseTrigger::PassesTriggerFilters' spawnflag half, asked of each toucher. dm_lockdown's
/// plug_teleporter is keyed 8 alone, physics objects, and read as naming nobody the plug
/// could never reach its socket.
/// </summary>
public class TriggerKindsTests
{
    private const int Clients = 1, Physics = 8, Everything = 64;

    private static readonly EntityHandle Volume = new(0x000C_0101u);
    private static readonly EntityHandle Toucher = new(0x0002_0001u);
    private static readonly EntityHandle Destination = new(0x000C_0102u);

    private static bool Teleported(int spawnflags, EntityState state, MoveType moveType)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(TriggerTeleport).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Entities.IsValid", true)
              .Returns("Entities.FindByName", Destination)
              .Returns("Entities.GetSpawnFlags", spawnflags)
              .Returns("Networking.EntityGetState", state)
              .Returns("Networking.EntityGetState.moveType", moveType);

        registry.KeyValue(Volume, EntityNames.TriggerTeleport, "target", "plug_destination");
        registry.Spawn(Volume, EntityNames.TriggerTeleport);
        registry.Touch(Volume, Toucher);

        return engine.Called("Entities.Teleport");
    }

    private const EntityState Player = EntityState.Player;
    private const EntityState Prop = 0;

    /// <summary>
    /// Stock hears what the solver moves after the step, so its teleport lands after it, and
    /// a player's at once: the frame's step must not act on the plug it just seated.
    /// </summary>
    [Theory]
    [InlineData(Physics, Prop, MoveType.VPhysics, false)]
    [InlineData(Clients, Player, MoveType.Walk, true)]
    public void What_the_solver_moves_is_sent_after_the_step(int spawnflags, EntityState state, MoveType moveType, bool atOnce)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(TriggerTeleport).Assembly);
        var services = Services(out var engine);
        var after = new PostSimulationQueue();
        services.AddInstance(after);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Entities.IsValid", true)
              .Returns("Entities.FindByName", Destination)
              .Returns("Entities.GetSpawnFlags", spawnflags)
              .Returns("Networking.EntityGetState", state)
              .Returns("Networking.EntityGetState.moveType", moveType);

        registry.KeyValue(Volume, EntityNames.TriggerTeleport, "target", "plug_destination");
        registry.Spawn(Volume, EntityNames.TriggerTeleport);
        registry.Touch(Volume, Toucher);
        Assert.Equal(atOnce, engine.Called("Entities.Teleport"));

        after.Run();
        Assert.True(engine.Called("Entities.Teleport"));
    }

    [Theory]
    [InlineData(Physics, Prop, MoveType.VPhysics, true)]    // the plug into its socket
    [InlineData(Physics, Player, MoveType.Walk, false)]
    [InlineData(Physics, Prop, MoveType.None, false)]       // not moved by physics
    [InlineData(Clients, Player, MoveType.Walk, true)]
    [InlineData(Clients, Prop, MoveType.VPhysics, false)]   // a crate must not fire a players' trigger
    [InlineData(Clients | Physics, Prop, MoveType.VPhysics, true)]
    [InlineData(Everything, Prop, MoveType.None, true)]
    [InlineData(0, Prop, MoveType.None, true)]              // no kind named: anybody, a recorded divergence
    public void A_toucher_passes_by_the_kinds_the_map_named(int spawnflags, EntityState state, MoveType moveType, bool passes)
    {
        Assert.Equal(passes, Teleported(spawnflags, state, moveType));
    }

    /// <summary>
    /// The routing's players-only rule stands before the kinds, and a game's only (with no
    /// engine, as here, it delivers everything): a volume must ask to hear about a physics object.
    /// </summary>
    [Theory]
    [InlineData(Physics, true)]
    [InlineData(Clients | Physics, true)]
    [InlineData(Everything, true)]
    [InlineData(Clients, false)]
    [InlineData(0, false)]
    public void A_volume_hears_about_more_than_players_when_the_map_named_more(int spawnflags, bool anything)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(TriggerTeleport).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Entities.GetSpawnFlags", spawnflags);
        registry.Spawn(Volume, EntityNames.TriggerTeleport);

        Assert.Equal(anything, registry.Get(Volume)!.TouchedByAnything);
    }
}

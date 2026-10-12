using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_physics_managed A35: trigger_teleport and point_teleport against
/// stock's CTriggerTeleport::Touch (triggers.cpp:2385-2451) and
/// CPointTeleport::InputTeleport (pointteleport.cpp:145). Both pass NULL for the
/// velocity, so the teleported entity keeps its momentum (a crate thrown
/// through a teleport keeps flying; stock's own comment calls it a BUGBUG and
/// keeps it).
/// </summary>
public class TeleportStockTests
{
    private static readonly EntityHandle It = new(0x0001_0070);
    private static readonly EntityHandle Toucher = new(0x0001_0071);
    private static readonly EntityHandle Destination = new(0x0001_0072);
    private static readonly EntityHandle Landmark = new(0x0001_0073);

    private static (ScriptedEntityRegistry, RecordingEngine) World(string className, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(TriggerTeleport).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;

        object? LastArg() => engine.Calls[^1].Args[0];
        engine.ReturnsEachCall("Entities.FindByName", () => (string?)LastArg() == "lm" ? Landmark : Destination);
        engine.ReturnsEachCall("Entities.GetOrigin", () =>
            (EntityHandle?)LastArg() == Destination ? new Vector(100f, 0f, 0f)
          : (EntityHandle?)LastArg() == Landmark ? new Vector(0f, 0f, 0f)
          : (EntityHandle?)LastArg() == Toucher ? new Vector(10f, 5f, 0f)
          : new Vector(0f, 0f, 0f));
        engine.Returns("Entities.GetAngles", new Vector(0f, 90f, 0f));
        engine.Returns("Entities.IsValid", true);
        engine.Returns("Entities.GetSpawnFlags", 0x01);              // clients
        engine.Returns("Entities.IsPlayer", true);
        engine.Returns("Networking.EntityGetState", EntityState.Player | EntityState.Solid);
        engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);

        foreach (var (key, value) in keys)
            registry.KeyValue(It, className, key, value);
        registry.Spawn(It, className);
        return (registry, engine);
    }

    private static EngineCall Teleport(RecordingEngine engine)
        => Assert.Single(engine.Calls, c => c.Member == "Entities.Teleport");

    [Fact]
    public void A_trigger_teleport_keeps_the_touchers_velocity()
    {
        var (registry, engine) = World(EntityNames.TriggerTeleport, ("target", "dest"));

        registry.Touch(It, Toucher);

        Assert.False(Teleport(engine).ArgAs<bool>(3), "stock passes pVelocity = NULL");
    }

    [Fact]
    public void A_trigger_teleport_with_no_landmark_faces_the_destination()
    {
        var (registry, engine) = World(EntityNames.TriggerTeleport, ("target", "dest"));

        registry.Touch(It, Toucher);

        var call = Teleport(engine);
        Assert.Equal(new Vector(100f, 0f, 0f), call.ArgAs<Vector>(1));
        Assert.Equal(new Vector(0f, 90f, 0f), call.ArgAs<Vector?>(2));
    }

    [Fact]
    public void A_landmark_keeps_the_touchers_offset_and_its_facing()
    {
        // vecLandmarkOffset = pOther origin - landmark origin; angles only when
        // there is no landmark.
        var (registry, engine) = World(EntityNames.TriggerTeleport, ("target", "dest"), ("landmark", "lm"));

        registry.Touch(It, Toucher);

        var call = Teleport(engine);
        Assert.Equal(new Vector(110f, 5f, 0f), call.ArgAs<Vector>(1));
        Assert.Null(call.ArgAs<Vector?>(2));
    }

    [Fact]
    public void A_point_teleport_keeps_the_targets_velocity()
    {
        var (registry, engine) = World(EntityNames.PointTeleport, ("target", "dest"));

        registry.Input(It, "Teleport", "", 0f, EntityHandle.None);

        Assert.False(Teleport(engine).ArgAs<bool>(3), "stock passes NULL for the velocity");
    }
}

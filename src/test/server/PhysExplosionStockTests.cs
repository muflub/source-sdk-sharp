using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_physics_managed A27: env_physexplosion's player arm against stock's
/// CPhysExplosion::Explode (physobj.cpp:1003-1040).
/// </summary>
public class PhysExplosionStockTests
{
    private static readonly EntityHandle Blast = new(0x0001_0060);
    private static readonly EntityHandle Player = new(0x0001_0001);

    private const int PushPlayer = 0x02;
    private const int Disorient = 0x10;

    private static RecordingEngine Exploding(int spawnFlags, params float[] draws)
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(PhysExplosion).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;

        var queue = new Queue<float>(draws);
        engine.ReturnsEachCall("Host.RandomFloat", () => queue.Dequeue());
        engine.Returns("Entities.GetSpawnFlags", spawnFlags);
        engine.Returns("Entities.GetLocalTransform.angles", new Vector(0f, 90f, 0f));
        engine.Returns("WorldQuery.EntitiesInSphere.into", new[] { Player });
        engine.Returns("Networking.EntityGetState",
                       EntityState.Player | EntityState.TakesDamage | EntityState.Physics);
        engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);
        engine.Returns("Entities.GetWorldSpaceCenter", new Vector(10f, 0f, 0f));

        registry.KeyValue(Blast, EntityNames.EnvPhysexplosion, "magnitude", "100");
        registry.KeyValue(Blast, EntityNames.EnvPhysexplosion, "radius", "200");
        registry.Spawn(Blast, EntityNames.EnvPhysexplosion);

        ((PhysExplosion)registry.Get(Blast)!).Explode();
        return engine;
    }

    [Fact]
    public void Disorienting_snaps_the_eyes_to_the_blasts_angles_plus_a_random_delta()
    {
        // vecDeltaAngles = RandomInt(-30,30), RandomInt(-30,30), 0;
        // SnapEyeAngles( GetLocalAngles() + vecDeltaAngles ). The draws are
        // 12.4 and -7.6, which RandomInt's floor makes 12 and -8.
        var engine = Exploding(PushPlayer | Disorient, 12.4f, -7.6f);

        var snap = Assert.Single(engine.Calls, c => c.Member == "PlayerMovement.SnapEyeAngles");
        Assert.Equal(Player, snap.ArgAs<EntityHandle>(0));
        Assert.Equal(new Vector(12f, 82f, 0f), snap.ArgAs<Vector>(1));
    }

    [Fact]
    public void Disorienting_punches_the_view_by_the_same_delta()
    {
        // pEntity->ViewPunch( vecDeltaAngles ): this player only, in the air too.
        var engine = Exploding(PushPlayer | Disorient, 12.4f, -7.6f);

        var punch = Assert.Single(engine.Calls, c => c.Member == "Hud.ViewPunch");
        Assert.Equal(new Vector(12f, -8f, 0f), punch.ArgAs<Vector>(1));
        Assert.True(punch.ArgAs<bool>(3), "a player thrown into the air is still punched");
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.FireInput");
    }

    [Fact]
    public void Without_the_flag_the_player_is_pushed_and_nothing_else()
    {
        var engine = Exploding(PushPlayer);

        Assert.Contains(engine.Calls, c => c.Member == "Entities.AddBaseVelocity");
        Assert.DoesNotContain(engine.Calls, c => c.Member == "PlayerMovement.SnapEyeAngles");
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Hud.ViewPunch");
        Assert.Equal(0, engine.CountOf("Host.RandomFloat"));
    }
}

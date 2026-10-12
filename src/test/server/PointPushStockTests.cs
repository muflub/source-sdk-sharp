using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_physics_managed A27: point_push against stock's CPointPush::PushEntity
/// (physobj.cpp:1992-2056).
/// </summary>
public class PointPushStockTests
{
    private static readonly EntityHandle Pusher = new(0x0001_0050);
    private static readonly EntityHandle Crate = new(0x0001_0051);

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine) Pushing(int spawnFlags, Vector crateAt)
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(PointPush).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;

        engine.Returns("Entities.GetSpawnFlags", spawnFlags);
        engine.Returns("Entities.GetAngles", new Vector(0f, 0f, 0f));          // facing +X
        engine.FillsSphere(new[] { Crate });
        engine.Returns("Networking.EntityGetState", EntityState.Solid);
        engine.Returns("Networking.EntityGetState.moveType", MoveType.VPhysics);
        engine.Returns("Entities.GetWorldSpaceCenter", crateAt);
        engine.Returns("EntityPhysics.GetMass", 50f);
        engine.Returns("Frame.GetFrameTime", 0.015f);
        engine.Returns("Clock.Now", 10f);

        registry.KeyValue(Pusher, EntityNames.PointPush, "magnitude", "10");
        registry.KeyValue(Pusher, EntityNames.PointPush, "radius", "200");
        registry.KeyValue(Pusher, EntityNames.PointPush, "enabled", "1");
        registry.Spawn(Pusher, EntityNames.PointPush);
        engine.Returns("Clock.Now", 10.05f);
        registry.Think(Pusher);
        return (registry, engine);
    }

    private static Vector Pushed(RecordingEngine engine)
        => Assert.Single(engine.Calls, c => c.Member == "EntityPhysics.ApplyForceCenter").ArgAs<Vector>(1);

    [Fact]
    public void A_physics_push_is_scaled_by_the_frame_time_not_the_think_interval()
    {
        // m_flMagnitude * flFalloff * 100 * vecPushDir * mass * gpGlobals->frametime:
        // 10 x 1 x 100 x 50 x 0.015 = 750, at the centre (full falloff).
        var (_, engine) = Pushing(spawnFlags: 0x10, crateAt: new Vector(10f, 0f, 0f));

        Assert.Equal(750f, Pushed(engine).X, 2);
    }

    [Fact]
    public void A_directional_push_is_full_strength_wherever_the_target_is()
    {
        // Directional: the push direction is the entity's forward, and the
        // distance stock remaps is VectorNormalize( forward ), which is 1, so a
        // target 150 units out (0.67 falloff outward) is pushed at full strength.
        var (_, engine) = Pushing(spawnFlags: 0x10 | 0x02, crateAt: new Vector(150f, 0f, 0f));

        Assert.Equal(750f, Pushed(engine).X, 2);
    }
}

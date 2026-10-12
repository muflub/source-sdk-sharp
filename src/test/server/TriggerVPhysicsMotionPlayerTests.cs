using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_physics_managed A27: a player in a trigger_vphysics_motion, against
/// stock's CTriggerVPhysicsMotion::StartTouch/EndTouch (triggers.cpp:4679-4731).
/// Stock sets PFLAG_VPHYSICS_MOTIONCONTROLLER and m_bSlowMovement on the player
/// before it looks for a physics object or a controller.
/// </summary>
public class TriggerVPhysicsMotionPlayerTests
{
    private static readonly EntityHandle Field = new(0x0001_0070);
    private static readonly EntityHandle Player = new(0x0001_0001);

    private static (ScriptedEntityRegistry Registry, SsPlayer Player, RecordingEngine Engine) World()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(TriggerVPhysicsMotion).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;

        engine.Returns("Entities.IsPlayer", true);
        engine.Returns("Players.IsAlive", true);
        engine.Returns("Entities.GetSolidFlags", (SolidFlags)0);
        engine.Returns("Entities.GetSpawnFlags", 1);                 // clients
        engine.Returns("Networking.EntityGetState", EntityState.Player);
        engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);

        registry.KeyValue(Player, EntityNames.SsPlayer, "targetname", "fixture_player");
        var player = (SsPlayer)registry.Get(Player)!;
        player.State.BindLocal();

        // MotionControllers.Create is unstubbed: the medium is refused (id 0),
        // and stock flags the player anyway.
        registry.KeyValue(Field, EntityNames.TriggerVphysicsMotion, "wait", "0");
        registry.Spawn(Field, EntityNames.TriggerVphysicsMotion);
        return (registry, player, engine);
    }

    [Fact]
    public void A_player_entering_is_on_the_motion_controller()
    {
        var (registry, player, _) = World();

        registry.Touch(Field, Player);

        Assert.NotEqual(0, player.PhysicsFlags & SsPlayer.PhysicsFlagVPhysicsMotionController);
    }

    [Fact]
    public void A_player_entering_is_slowed_even_without_a_medium()
    {
        var (registry, _, engine) = World();

        registry.Touch(Field, Player);

        var slow = Assert.Single(engine.Calls, c => c.Member == "PlayerMovement.SetSlowMovement");
        Assert.True(slow.ArgAs<bool>(1), "m_bSlowMovement = true");
    }

    [Fact]
    public void A_player_leaving_is_released_from_both()
    {
        var (registry, player, engine) = World();
        registry.Touch(Field, Player);

        registry.EndTouch(Field, Player);

        Assert.Equal(0, player.PhysicsFlags & SsPlayer.PhysicsFlagVPhysicsMotionController);
        Assert.False(engine.Calls.Last(c => c.Member == "PlayerMovement.SetSlowMovement").ArgAs<bool>(1));
    }
}

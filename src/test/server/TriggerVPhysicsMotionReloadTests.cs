using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A trigger_vphysics_motion across a <c>cs_sv_reload</c>. The native medium
/// and everything attached to it outlive the module; stock carries the
/// player's flags (<c>DEFINE_FIELD( m_afPhysicsFlags )</c>, player.cpp:301),
/// the controller (<c>DEFINE_PHYSPTR( m_pController )</c>, triggers.cpp:4567)
/// and its touch list through a restore. Before this, the rebuilt managed side
/// forgot all three: the player was off the motion controller while still
/// attached to it, a leaving object stayed in the medium, and switching the
/// volume off released no one.
/// </summary>
public class TriggerVPhysicsMotionReloadTests
{
    private static readonly EntityHandle Field = new(0x0001_0070);
    private static readonly EntityHandle Player = new(0x0001_0001);
    private static readonly EntityHandle Crate = new(0x0001_0090);
    private const uint MediumId = 0x0001_0007u;

    private readonly ReloadStore _reload = new(Side.Server);

    private static ScriptedEntityRegistry Registry(out RecordingEngine engine, bool player)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(TriggerVPhysicsMotion).Assembly);
        var services = Services(out engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;

        engine.Returns("Entities.IsPlayer", player);
        engine.Returns("Players.IsAlive", true);
        engine.Returns("Entities.GetSolidFlags", (SolidFlags)0);
        engine.Returns("Entities.GetSpawnFlags", 1 | 2);                  // clients, everything else
        engine.Returns("Networking.EntityGetState", player ? EntityState.Player : (EntityState)0);
        engine.Returns("Networking.EntityGetState.moveType", player ? MoveType.Walk : MoveType.VPhysics);
        engine.Returns("MotionControllers.Create", MediumId);
        return registry;
    }

    /// <summary>A volume with <paramref name="toucher"/> inside, saved and rebuilt.</summary>
    private (ScriptedEntityRegistry Registry, RecordingEngine Engine) ReloadedWithInside(EntityHandle toucher, bool player)
    {
        _reload.Clear();

        var before = Registry(out var engine, player);
        if (player)
            before.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");
        before.KeyValue(Field, EntityNames.TriggerVphysicsMotion, "wait", "0");
        before.Spawn(Field, EntityNames.TriggerVphysicsMotion);
        before.Touch(Field, toucher);

        // The control: the medium was made and the toucher attached to it.
        var attach = engine.Last("MotionControllers.Attach");
        Assert.True(attach is not null, "the toucher was never attached");
        Assert.Equal(MediumId, attach!.Value.ArgAs<uint>(0));

        before.SaveForReload(_reload);

        var after = Registry(out var afterEngine, player);
        Assert.Equal(player ? 2 : 1, after.RestoreAfterReload(_reload));
        Assert.False(afterEngine.Called("MotionControllers.Create"), "a reload adopts the medium, never makes a second");
        return (after, afterEngine);
    }

    [Fact]
    public void A_player_on_the_motion_controller_is_still_on_it_after_a_reload()
    {
        var (registry, _) = ReloadedWithInside(Player, player: true);

        var player = (SsPlayer)registry.Get(Player)!;
        Assert.NotEqual(0, player.PhysicsFlags & SsPlayer.PhysicsFlagVPhysicsMotionController);
    }

    [Fact]
    public void A_player_leaving_after_a_reload_is_released_from_the_flag_and_the_old_medium()
    {
        var (registry, engine) = ReloadedWithInside(Player, player: true);

        registry.EndTouch(Field, Player);

        Assert.Equal(0, ((SsPlayer)registry.Get(Player)!).PhysicsFlags & SsPlayer.PhysicsFlagVPhysicsMotionController);
        var detach = engine.Last("MotionControllers.Detach");
        Assert.True(detach is not null, "the player's shadow was left in the medium");
        Assert.Equal(MediumId, detach!.Value.ArgAs<uint>(0));
        Assert.Equal(Player, detach.Value.ArgAs<EntityHandle>(1));
    }

    [Fact]
    public void An_object_leaving_after_a_reload_is_taken_out_of_the_old_medium()
    {
        var (registry, engine) = ReloadedWithInside(Crate, player: false);

        registry.EndTouch(Field, Crate);

        var detach = engine.Last("MotionControllers.Detach");
        Assert.True(detach is not null, "the crate was left in the medium");
        Assert.Equal(MediumId, detach!.Value.ArgAs<uint>(0));
        Assert.Equal(Crate, detach.Value.ArgAs<EntityHandle>(1));
    }

    [Fact]
    public void Switching_the_volume_off_after_a_reload_releases_who_was_already_inside()
    {
        // The entrant set: the engine reports crossings, so only the carried set
        // knows the crate is still inside.
        var (registry, engine) = ReloadedWithInside(Crate, player: false);

        registry.Input(Field, "Disable", string.Empty, 0f, EntityHandle.None);

        var detach = engine.Last("MotionControllers.Detach");
        Assert.True(detach is not null, "nothing inside was released");
        Assert.Equal(Crate, detach!.Value.ArgAs<EntityHandle>(1));
    }
}

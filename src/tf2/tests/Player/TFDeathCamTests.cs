using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Shared;

using Xunit;
using static SourceSharp.Tests.TestServices;

namespace TF2.Tests.Player;

/// <summary>
/// The dead player's camera, as stock's <c>State_Think_DYING</c> starts it
/// (tf_player.cpp:13923-13935): once the ragdoll exists, the death cam starts
/// and the body stays drawn. Without the mode the view stays the death eye —
/// inside the corpse, whose hands and weapon fill the screen (live,
/// ctf_2fort).
/// </summary>
public sealed class TFDeathCamTests
{
    private static readonly EntityHandle Watcher = new(0x0100_0001u);

    private sealed class Rig
    {
        public readonly RecordingEngine Engine;
        public readonly Observers Observers;
        public readonly SsPlayer Player;
        public readonly TFDeathCam DeathCam;

        public Rig()
        {
            var registry = new ScriptedEntityRegistry(TestEngines.Server());
            registry.RegisterAll(typeof(SsPlayer).Assembly);
            var services = Services(out Engine);
            Engine.Returns("Entities.IsValid", true)
                  .Returns("Entities.GetIndex", 1)
                  .Returns("Players.GetTeam", (int)TFTeam.Blue)   // mp_forcecamera at its default would remap a mode above fixed
                  .Ignores("Players.SetObserverMode", "PlayerMovement.SetMoveType", "EntityRender.SetVisible",
                           "Players.HolsterActiveWeapon", "Damage.SetTakeDamageMode", "Entities.Teleport",
                           "PlayerMovement.SnapEyeAngles", "EntityPhysics.SetNotSolid", "Players.RemoveAllItems");
            services.AddInstance<IEntityDirectory>(registry);
            registry.Services = services;
            registry.KeyValue(Watcher, EntityNames.Player, "targetname", "p");
            Player = (SsPlayer)registry.Get(Watcher)!;
            Player.State.BindLocal();
            Player.State.Alive = true;

            Observers = new Observers(new RecordingLog(), registry, Engine, Engine, Engine, Engine, Engine,
                                      Engine, Engine, ConVarRegistry.For(UnattachedConsole()));
            DeathCam = new TFDeathCam(registry, Observers, new RecordingLog());
        }

        public PlayerDeath Died(EntityHandle body) => new(Watcher, 3, EntityHandle.None, default(Vector), body);
    }

    [Fact]
    public void A_death_with_a_body_starts_the_death_cam()
    {
        var r = new Rig();
        r.Player.State.Alive = false;

        r.DeathCam.OnPlayerDied(r.Died(new EntityHandle(0x0100_0002u)));

        Assert.Equal((int)ObserverMode.DeathCam, r.Player.State.ObserverMode);
        Assert.False(r.Player.Observer.EnterObserver, "the death cam is not a spectator seat");
    }

    [Fact]
    public void A_death_without_a_body_leaves_the_mode_alone()
    {
        var r = new Rig();

        r.DeathCam.OnPlayerDied(r.Died(EntityHandle.None));

        Assert.Equal((int)ObserverMode.None, r.Player.State.ObserverMode);
    }

    [Fact]
    public void A_player_already_watching_keeps_their_seat()
    {
        var r = new Rig();
        r.Player.Observer.EnterObserver = true;
        Assert.True(r.Observers.Start(r.Player, ObserverMode.Fixed, 1f));   // fixed: not remapped by forcecamera

        r.DeathCam.OnPlayerDied(r.Died(new EntityHandle(0x0100_0002u)));

        Assert.Equal((int)ObserverMode.Fixed, r.Player.State.ObserverMode);
    }

    [Fact]
    public void The_server_mod_offers_the_death_cam_as_a_death_hook()
    {
        // The composition registers IPlayerDeathHooks by [SideService]; a mod
        // assembly without one keeps the death eye inside the corpse.
        var hooks = typeof(TFDeathCam).Assembly.GetTypes()
            .Where(t => t.GetInterfaces().Contains(typeof(IPlayerDeathHook)))
            .ToList();

        Assert.Contains(typeof(TFDeathCam), hooks);
    }
}

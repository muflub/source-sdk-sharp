using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// CBreakableProp's physcannon and health inputs (props.cpp):
/// - Enable/DisablePhyscannonPickup, whose EFL_NO_PHYSCANNON_INTERACTION the
///   gravity gun reads before it picks a prop up or punts it;
/// - the puntsound key with Enable/DisablePuntSound;
/// - SetHealth, AddHealth and RemoveHealth through UpdateHealth.
/// Found in the contract diff (2026-10-28): the physics props answered none
/// of them, and our SetHealth also moved the max the ratio is measured
/// against.
/// </summary>
public class PropCannonInputsTests
{
    private static readonly EntityHandle Prop = new(71);
    private static readonly EntityHandle Player = new(0x0001_0001u);

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;

        public PropPhysics Entity => (PropPhysics)Registry.Get(Prop)!;

        public IPhysGunTarget Cannon => Entity;

        public void Input(string name, float number = 0f)
            => Registry.Input(Prop, name, number.ToString(System.Globalization.CultureInfo.InvariantCulture), number, EntityHandle.None);

        public string[] Sounds => Engine.AllOf("Sound.EntityEmitSound").Select(c => c.ArgAs<string>(1)).ToArray();
    }

    private static Rig Spawned(params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("EntityRender.GetModelKeyValueText", "mdlkeyvalue { prop_data { health 40 } }");

        foreach (var (key, value) in keys)
            registry.KeyValue(Prop, EntityNames.PropPhysics, key, value);
        registry.Spawn(Prop, EntityNames.PropPhysics);
        var rig = new Rig { Registry = registry, Engine = engine };
        rig.Entity.OnPhysicsCreated();
        return rig;
    }

    [Fact]
    public void DisablePhyscannonPickup_shuts_the_gun_out_and_Enable_lets_it_back()
    {
        var rig = Spawned();
        Assert.False(rig.Cannon.NoPhyscannonInteraction);

        rig.Input("DisablePhyscannonPickup");
        Assert.True(rig.Cannon.NoPhyscannonInteraction);

        rig.Input("EnablePhyscannonPickup");
        Assert.False(rig.Cannon.NoPhyscannonInteraction);
    }

    [Fact]
    public void A_punt_plays_its_puntsound_unless_it_is_off()
    {
        var rig = Spawned(("puntsound", "Prop.Punt"));

        rig.Cannon.OnPhysGunPickup(Player, PhysGunPickup.PuntedByCannon);
        rig.Cannon.OnPhysGunPickup(Player, PhysGunPickup.PickedUpByCannon);
        Assert.Equal(new[] { "Prop.Punt" }, rig.Sounds);

        rig.Input("DisablePuntSound");
        rig.Cannon.OnPhysGunPickup(Player, PhysGunPickup.PuntedByCannon);
        Assert.Single(rig.Sounds);

        rig.Input("EnablePuntSound");
        rig.Cannon.OnPhysGunPickup(Player, PhysGunPickup.PuntedByCannon);
        Assert.Equal(2, rig.Sounds.Length);
    }

    /// <summary>
    /// Stock's OnPhysGunDrop compares the drop reason with PUNTED_BY_CANNON,
    /// a pickup reason, and both are 1: a throw by hand plays the punt sound
    /// and a launch does not.
    /// </summary>
    [Fact]
    public void A_throw_by_hand_plays_the_punt_sound_and_a_launch_does_not()
    {
        var rig = Spawned(("puntsound", "Prop.Punt"));

        rig.Cannon.OnPhysGunDrop(Player, PhysGunDrop.LaunchedByCannon);
        Assert.Empty(rig.Sounds);

        rig.Cannon.OnPhysGunDrop(Player, PhysGunDrop.ThrownByPlayer);
        Assert.Equal(new[] { "Prop.Punt" }, rig.Sounds);
    }

    /// <summary>UpdateHealth: the ratio is of the max it spawned with, which the inputs do not move.</summary>
    [Fact]
    public void The_health_inputs_report_a_ratio_of_the_unchanged_max()
    {
        var rig = Spawned();
        int health = 40;
        rig.Engine.ReturnsEachCall("Entities.GetHealth", () => health);

        rig.Input("SetHealth", 20f);
        Assert.Equal("0.5", rig.Entity.OnHealthChanged.LastValue);
        health = 20;

        rig.Input("AddHealth", 10f);
        Assert.Equal("0.75", rig.Entity.OnHealthChanged.LastValue);
        health = 30;

        rig.Input("RemoveHealth", 10f);
        Assert.Equal("0.5", rig.Entity.OnHealthChanged.LastValue);
        Assert.Equal(0, rig.Entity.OnBreak.FireCount);
    }

    [Fact]
    public void Health_taken_to_none_by_an_input_breaks_it()
    {
        var rig = Spawned();
        rig.Engine.Returns("Entities.GetHealth", 10);

        rig.Input("RemoveHealth", 10f);

        Assert.Equal(1, rig.Entity.OnBreak.FireCount);
    }

    //-------------------------------------------------------------------------
    // The gravity gun's half: CWeaponPhysCannon's EFL_NO_PHYSCANNON_INTERACTION tests.
    //-------------------------------------------------------------------------

    private sealed class Ahead(EntityHandle hit) : ITraceQueries
    {
        public TraceAnswer Line(Vector start, Vector end, uint mask, EntityHandle pass) => new(0.5f, end, hit, false);

        public TraceAnswer Hull(Vector start, Vector end, Vector mins, Vector maxs, uint mask, EntityHandle pass) => new(0.5f, end, hit, false);
    }

    private sealed class NoControllers : ICustomMotionControllers
    {
        public uint CreateCustom(EntityHandle entity, IMotionSimulate simulate) => 0u;

        public void Destroy(uint motion) { }
    }

    private sealed class NoTempEnts : TempEntsDouble { }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_gun_dry_fires_at_a_prop_shut_out_of_it(bool shutOut)
    {
        var rig = Spawned();
        if (shutOut)
            rig.Input("DisablePhyscannonPickup");
        // Something with health in front of the gun: a punt (NonVPhysics) unless it is shut out.
        rig.Engine.Returns("Players.GetHandle", Player)
                  .Returns("Entities.GetHealth", 40)
                  .Returns("Entities.GetIndex", 71);

        var cannon = new PhysCannonWorld(new RecordingLog(), rig.Engine, rig.Engine, rig.Engine, RestingBodies, new Ahead(Prop),
                                         rig.Engine, rig.Engine, rig.Registry.Services!.Require<IScreenEffects>(), new NoTempEnts(),
                                         rig.Registry, rig.Engine, new NoControllers(), rig.Engine);

        Assert.Equal(shutOut, cannon.AimPunt(1, out _) == PhysCannonPunt.DryFire);
    }

    /// <summary>CWeaponPhysCannon::CanPickupObject: a simulated prop ahead opens the claws, unless it is shut out.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_gun_cannot_take_a_prop_shut_out_of_it(bool shutOut)
    {
        var rig = Spawned();
        if (shutOut)
            rig.Input("DisablePhyscannonPickup");
        rig.Engine.Returns("Players.GetHandle", Player)
                  .Returns("Entities.GetIndex", 71)
                  .Returns("Entities.GetEngineBlock", new byte[EngineBlock.SizeBytes])
                  .Returns("EntityPhysics.PhysicsObject", (nint)0x1234);
        EngineBlockMoveType.Write(rig.Engine, Prop, MoveType.VPhysics);

        var cannon = new PhysCannonWorld(new RecordingLog(), rig.Engine, rig.Engine, rig.Engine, RestingBodies, new Ahead(Prop),
                                         rig.Engine, rig.Engine, rig.Registry.Services!.Require<IScreenEffects>(), new NoTempEnts(),
                                         rig.Registry, rig.Engine, new NoControllers(), rig.Engine);

        Assert.Equal(!shutOut, cannon.TargetAhead(1));
    }
}

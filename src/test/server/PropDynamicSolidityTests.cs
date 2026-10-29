using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// A prop_dynamic's solidity, as CDynamicProp::Spawn and CreateVPhysics
/// decide it (props.cpp): from the map's <c>solid</c> key, SOLID_NONE when
/// absent, which makes a prop_dynamic a not-solid oriented box; a static
/// object unless it is SOLID_NONE, or not solid with SF_DYNAMICPROP_NO_VPHYSICS
/// (128); SF_DYNAMICPROP_DISABLE_COLLISION (256) after the object. TurnOff and
/// Disable only hide it. Found beside stock on ss_sandbox (2026-10-26):
/// stock's sinks are OBB and not solid (ab_movetypes 3/4) and its player
/// falls through them, where here every prop_dynamic was SOLID_BSP, which a
/// studio model is not traced as (the wall chargers' bug).
/// </summary>
public class PropDynamicSolidityTests
{
    private static readonly EntityHandle Prop = new(0x0004_0321u);

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine) Spawned(
        string className = EntityNames.PropDynamic, int spawnFlags = 0, string? solid = null, bool startDisabled = false)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropDynamic).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("Precache.PrecacheModel", 3)
              .Returns("EntityRender.GetModelKeyValueText", null)
              .Returns("Entities.GetSpawnFlags", spawnFlags);

        registry.KeyValue(Prop, className, "model", "models/props_c17/furnituresink001a.mdl");
        if (solid is not null)
            registry.KeyValue(Prop, className, "solid", solid);
        if (startDisabled)
            registry.KeyValue(Prop, className, "StartDisabled", "1");
        registry.Spawn(Prop, className);
        return (registry, engine);
    }

    private static (SolidType, SolidFlags) Solidity(RecordingEngine engine)
    {
        var set = Assert.Single(engine.AllOf("Entities.SetSolidity"));
        return (set.ArgAs<SolidType>(1), set.ArgAs<SolidFlags>(2));
    }

    private static bool[] NotSolid(RecordingEngine engine)
        => engine.AllOf("EntityPhysics.SetNotSolid").Select(c => c.ArgAs<bool>(1)).ToArray();

    [Theory]
    [InlineData(EntityNames.PropDynamic)]
    [InlineData(EntityNames.DynamicProp)]
    public void A_prop_dynamic_with_no_solid_key_is_a_not_solid_oriented_box_with_its_object(string className)
    {
        var (_, engine) = Spawned(className);

        Assert.Equal((SolidType.OrientedBox, SolidFlags.NotSolid), Solidity(engine));
        Assert.True(engine.Called("EntityPhysics.InitStaticPhysics"));
        Assert.False(engine.Called("EntityPhysics.SetBrushSolidity"), "SOLID_BSP is untraceable on a studio model");
    }

    [Fact]
    public void An_override_with_no_solid_key_has_no_solidity_and_no_object()
    {
        var (_, engine) = Spawned(EntityNames.PropDynamicOverride);

        Assert.Equal((SolidType.None, SolidFlags.None), Solidity(engine));
        Assert.False(engine.Called("EntityPhysics.InitStaticPhysics"));
    }

    [Fact]
    public void A_solid_key_of_six_is_the_models_collision_and_its_static_object()
    {
        var (_, engine) = Spawned(solid: "6");

        Assert.Equal((SolidType.VPhysics, SolidFlags.None), Solidity(engine));
        Assert.True(engine.Called("EntityPhysics.InitStaticPhysics"));
    }

    [Fact]
    public void Not_solid_and_asked_for_no_physics_has_no_object()
    {
        var (_, engine) = Spawned(spawnFlags: 128);

        Assert.False(engine.Called("EntityPhysics.InitStaticPhysics"));
    }

    [Fact]
    public void The_collision_spawnflag_comes_after_the_object_so_it_keeps_its_object()
    {
        var (_, engine) = Spawned(spawnFlags: 128 | 256, solid: "6");

        Assert.Equal((SolidType.VPhysics, SolidFlags.NotSolid), Solidity(engine));
        Assert.True(engine.Called("EntityPhysics.InitStaticPhysics"));
    }

    [Fact]
    public void Turning_it_off_hides_it_and_leaves_its_solidity()
    {
        var (registry, engine) = Spawned(solid: "6");
        engine.Clear();

        registry.Input(Prop, "TurnOff", "", 0f, EntityHandle.None);
        registry.Input(Prop, "Disable", "", 0f, EntityHandle.None);

        Assert.Equal([false, false], engine.AllOf("EntityRender.SetVisible").Select(c => c.ArgAs<bool>(1)));
        Assert.Empty(NotSolid(engine));
        Assert.False(engine.Called("Entities.SetSolidity"));
    }

    [Fact]
    public void A_prop_that_starts_disabled_is_hidden_and_keeps_its_solidity()
    {
        var (_, engine) = Spawned(solid: "6", startDisabled: true);

        Assert.Equal([false], engine.AllOf("EntityRender.SetVisible").Select(c => c.ArgAs<bool>(1)));
        Assert.Equal((SolidType.VPhysics, SolidFlags.None), Solidity(engine));
    }

    [Fact]
    public void DisableCollision_and_EnableCollision_switch_its_not_solid_flag()
    {
        var (registry, engine) = Spawned(solid: "6");

        registry.Input(Prop, "DisableCollision", "", 0f, EntityHandle.None);
        registry.Input(Prop, "EnableCollision", "", 0f, EntityHandle.None);

        Assert.Equal([true, false], NotSolid(engine));
    }

    [Fact]
    public void It_moves_as_a_pusher_and_is_never_simulated()
    {
        var (registry, _) = Spawned();
        var prop = registry.Get(Prop)!;

        Assert.True(prop.MovesAsPusher);
        Assert.False(prop.SpawnsPhysics);
    }
}

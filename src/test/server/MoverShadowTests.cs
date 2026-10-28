using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The movers stock gives a shadow (VPhysicsInitShadow( false, false ) in
/// their CreateVPhysics): doors, buttons, plats, trains, func_brush,
/// func_rotating, func_movelinear and the weight button and prop door. Found
/// by stock-ab's solidity census (2026-10-26): every one held a shadow in
/// stock and nothing here, so a prop met none of them.
/// </summary>
public class MoverShadowTests
{
    private static readonly EntityHandle Mover = new(0x0004_0341u);

    private static RecordingEngine Spawned(string className, int spawnFlags = 0)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncDoor).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("Entities.GetSpawnFlags", spawnFlags);
        registry.Spawn(Mover, className);
        return engine;
    }

    [Theory]
    [InlineData(EntityNames.FuncButton)]
    [InlineData(EntityNames.FuncRotButton)]
    [InlineData(EntityNames.MomentaryRotButton)]
    [InlineData(EntityNames.FuncDoor)]
    [InlineData(EntityNames.FuncDoorRotating)]
    [InlineData(EntityNames.FuncWater)]
    [InlineData(EntityNames.FuncPlat)]
    [InlineData(EntityNames.FuncPlatrot)]
    [InlineData(EntityNames.FuncRotating)]
    [InlineData(EntityNames.FuncMovelinear)]
    [InlineData(EntityNames.MomentaryDoor)]
    [InlineData(EntityNames.FuncWaterAnalog)]
    [InlineData(EntityNames.FuncTrain)]
    [InlineData(EntityNames.FuncTracktrain)]
    [InlineData(EntityNames.FuncBrush)]
    [InlineData(EntityNames.FuncReflectiveGlass)]
    [InlineData(EntityNames.FuncWeightButton)]
    [InlineData(EntityNames.PropDoorRotating)]
    public void A_mover_is_given_a_shadow_that_does_not_move_or_turn_by_itself(string className)
    {
        var shadow = Assert.Single(Spawned(className).AllOf("EntityPhysics.InitShadowPhysics"));

        Assert.Equal((false, false), (shadow.ArgAs<bool>(1), shadow.ArgAs<bool>(2)));
    }

    // CBaseDoor's and CFuncMoveLinear's CreateVPhysics add FSOLID_VOLUME_CONTENTS
    // for their water: the engine's point contents count only such volumes, so
    // without it a player never swam in moving water (stock-ab census, 0x20).
    [Theory]
    [InlineData(EntityNames.FuncWater)]
    [InlineData(EntityNames.FuncWaterAnalog)]
    public void Moving_water_is_volume_contents(string className)
    {
        var last = Spawned(className).Last("Entities.SetSolidity")!.Value;

        Assert.Equal((SolidType.VPhysics, SolidFlags.VolumeContents), (last.ArgAs<SolidType>(1), last.ArgAs<SolidFlags>(2)));
    }

    // The movers stock's Spawn makes SOLID_VPHYSICS, which vphysics may block
    // (VPhysicsUpdatePusher skips a SOLID_BSP one). Census: stock 6, here 1.
    [Theory]
    [InlineData(EntityNames.FuncDoor)]
    [InlineData(EntityNames.FuncDoorRotating)]
    [InlineData(EntityNames.FuncMovelinear)]
    [InlineData(EntityNames.MomentaryDoor)]
    [InlineData(EntityNames.FuncRotButton)]
    [InlineData(EntityNames.MomentaryRotButton)]
    [InlineData(EntityNames.FuncRotating)]
    [InlineData(EntityNames.FuncBrush)]
    [InlineData(EntityNames.FuncReflectiveGlass)]
    [InlineData(EntityNames.FuncTracktrain)]
    public void A_vphysics_mover_is_solid_through_its_collision_model_and_not_water(string className)
    {
        var last = Spawned(className).Last("Entities.SetSolidity")!.Value;

        Assert.Equal((SolidType.VPhysics, SolidFlags.None), (last.ArgAs<SolidType>(1), last.ArgAs<SolidFlags>(2)));
    }

    [Theory]
    [InlineData(EntityNames.FuncPlat)]
    [InlineData(EntityNames.FuncButton)]
    [InlineData(EntityNames.FuncTrain)]
    public void A_bsp_mover_stays_a_brush(string className)
    {
        var engine = Spawned(className);

        Assert.True(engine.Called("EntityPhysics.SetBrushSolidity"));
        Assert.False(engine.Called("Entities.SetSolidity"));
    }

    [Fact]
    public void A_not_solid_movelinear_has_no_shadow_but_water_always_does()
    {
        Assert.False(Spawned(EntityNames.FuncMovelinear, spawnFlags: 8).Called("EntityPhysics.InitShadowPhysics"));
        Assert.True(Spawned(EntityNames.FuncWaterAnalog, spawnFlags: 8).Called("EntityPhysics.InitShadowPhysics"));
    }

    [Theory]
    [InlineData(EntityNames.FuncWeightButton)]
    [InlineData(EntityNames.PropDoorRotating)]
    public void A_vphysics_mover_is_solid_through_its_model_and_never_static(string className)
    {
        var engine = Spawned(className);

        var solid = engine.AllOf("Entities.SetSolidity").First();
        int set = engine.Calls.ToList().IndexOf(solid);
        int shadow = engine.Calls.ToList().FindIndex(c => c.Member == "EntityPhysics.InitShadowPhysics");
        Assert.Equal(SolidType.VPhysics, solid.ArgAs<SolidType>(1));
        Assert.True(set < shadow, "the solid type before the shadow");
        Assert.False(engine.Called("EntityPhysics.SetPhysicsStatic"));
    }

    [Fact]
    public void The_movers_frame_sends_every_shadow_after_the_movers_move()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        var services = Services(out var engine);
        engine.Strict = false;
        var motion = services.Require<EntityMotion>();

        motion.RunFrame(0.015f);

        var update = Assert.Single(engine.AllOf("EntityPhysics.UpdateShadows"));
        Assert.Equal(0.015f, update.ArgAs<float>(0));
    }
}

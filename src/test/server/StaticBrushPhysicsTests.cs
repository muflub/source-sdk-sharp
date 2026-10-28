using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The brushes stock gives a static physics object (VPhysicsInitStatic):
/// func_wall and its subclasses func_wall_toggle and func_conveyor
/// (bmodels.cpp), and func_breakable (func_break.cpp). Props land on that
/// object; a trace meets the brush. Found beside stock on ss_sandbox
/// (2026-10-26): the belt's cans sank into the conveyor they stood on.
/// </summary>
public class StaticBrushPhysicsTests
{
    private static readonly EntityHandle Brush = new(0x0002_0070);

    private static RecordingEngine Spawned(string className, int spawnFlags = 0)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncWall).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("EntityPhysics.InitStaticPhysics", true)
              .Returns("Entities.GetSpawnFlags", spawnFlags);

        registry.Spawn(Brush, className);
        return engine;
    }

    private static int First(RecordingEngine engine, string member, Func<EngineCall, bool>? where = null)
    {
        for (int i = 0; i < engine.Calls.Count; i++)
        {
            if (engine.Calls[i].Member == member && (where is null || where(engine.Calls[i])))
                return i;
        }
        return -1;
    }

    [Theory]
    [InlineData(EntityNames.FuncWall)]
    [InlineData(EntityNames.FuncWallToggle)]
    [InlineData(EntityNames.FuncConveyor)]
    [InlineData(EntityNames.FuncBreakable)]
    [InlineData(EntityNames.FuncVehicleclip)]
    [InlineData(EntityNames.FuncLod)]
    [InlineData(EntityNames.FuncBreakableSurf)]
    public void A_static_brush_is_made_solid_then_given_its_static_object(string className)
    {
        var engine = Spawned(className);

        int solid = First(engine, "EntityPhysics.SetBrushSolidity", c => c.ArgAs<BrushSolidity>(1) == BrushSolidity.Solid);
        int made = First(engine, "EntityPhysics.InitStaticPhysics");
        Assert.True(solid >= 0 && made > solid, $"SOLID_BSP ({solid}) before the object ({made})");
        Assert.Single(engine.AllOf("EntityPhysics.InitStaticPhysics"));
        Assert.False(engine.Called("EntityPhysics.SetPhysicsStatic"), "a prop's create would make it SOLID_VPHYSICS");
    }

    [Fact]
    public void A_wall_that_starts_off_has_its_object_but_no_collisions()
    {
        var engine = Spawned(EntityNames.FuncWallToggle, spawnFlags: 1);

        int made = First(engine, "EntityPhysics.InitStaticPhysics");
        int off = First(engine, "EntityPhysics.EnableCollisions", c => !c.ArgAs<bool>(1));
        Assert.True(made >= 0 && off > made, $"the object ({made}) then collisions off ({off})");
        Assert.DoesNotContain(engine.AllOf("EntityPhysics.EnableCollisions"), c => c.ArgAs<bool>(1));
    }

    [Fact]
    public void Turning_a_wall_off_and_on_switches_its_collisions()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncWall).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        registry.Spawn(Brush, EntityNames.FuncWallToggle);
        engine.Clear();

        registry.Input(Brush, "TurnOff", "", 0f, EntityHandle.None);
        registry.Input(Brush, "TurnOn", "", 0f, EntityHandle.None);

        Assert.Equal([false, true], engine.AllOf("EntityPhysics.EnableCollisions").Select(c => c.ArgAs<bool>(1)));
    }

    [Fact]
    public void A_not_solid_conveyor_still_has_its_object_and_is_then_made_not_solid()
    {
        var engine = Spawned(EntityNames.FuncConveyor, spawnFlags: 2);

        int made = First(engine, "EntityPhysics.InitStaticPhysics");
        int none = First(engine, "EntityPhysics.SetBrushSolidity", c => c.ArgAs<BrushSolidity>(1) == BrushSolidity.None);
        Assert.True(made >= 0 && none > made, $"the object ({made}) then not solid ({none})");
    }
}

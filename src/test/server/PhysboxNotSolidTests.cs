using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// func_physbox's "notsolid" key (stock CPhysBox::Spawn, physobj.cpp:458:
/// AddSolidFlags( FSOLID_NOT_SOLID )), decided in managed and carried out by
/// the generic not-solid call (plan_physics_bones_native_zero 2.4:
/// shell_physicsobjects.cpp's SetSolidToWorld was the same flag write under
/// a second name).
/// </summary>
public class PhysboxNotSolidTests
{
    private static RecordingEngine Spawned(params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncPhysbox).Assembly);
        var services = Services(out var engine);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("EntityPhysics.InitPhysics", true);

        var handle = new EntityHandle(0x0001_00C1);
        foreach (var (key, value) in keys)
            registry.KeyValue(handle, EntityNames.FuncPhysbox, key, value);
        registry.Spawn(handle, EntityNames.FuncPhysbox);
        return engine;
    }

    [Fact]
    public void ANotSolidPhysboxIsMadeNotSolid()
    {
        var engine = Spawned(("notsolid", "1"));
        var call = engine.Last("EntityPhysics.SetNotSolid");
        Assert.True(call is { } c && c.ArgAs<bool>(1), "FSOLID_NOT_SOLID added");
    }

    [Fact]
    public void ASolidPhysboxIsLeftSolid()
    {
        var engine = Spawned();
        Assert.False(engine.Called("EntityPhysics.SetNotSolid"));
    }
}

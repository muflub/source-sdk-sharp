using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// "Damagetype" 1 on a prop_physics (CPhysicsProp) or func_physbox (CPhysBox)
/// makes its object FVPHYSICS_DMG_SLICE, which the impact damage reads as
/// angular speed as well as linear. Found 2026-10-28: the impact damage read
/// the flag and nothing set it, so dm_lockdown's sharp func_physbox fan cut
/// like a blunt one.
/// </summary>
public class SharpDamageTypeTests
{
    private static readonly EntityHandle Ent = new(0x000D_0101u);

    private static RecordingEngine Spawned(string className, string damageType)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("EntityPhysics.InitPhysics", true);

        registry.KeyValue(Ent, className, "Damagetype", damageType);
        registry.Spawn(Ent, className);
        registry.Get(Ent)!.OnPhysicsCreated();
        return engine;
    }

    private static uint[] Flags(RecordingEngine engine)
        => engine.AllOf("EntityPhysics.AddGameFlags").Select(c => c.ArgAs<uint>(1)).ToArray();

    [Theory]
    [InlineData(EntityNames.PropPhysics)]
    [InlineData(EntityNames.FuncPhysbox)]
    public void A_sharp_one_slices(string className)
        => Assert.Contains(PhysicsGameFlags.SliceDamage, Flags(Spawned(className, "1")));

    [Theory]
    [InlineData(EntityNames.PropPhysics)]
    [InlineData(EntityNames.FuncPhysbox)]
    public void A_blunt_one_does_not(string className)
        => Assert.DoesNotContain(PhysicsGameFlags.SliceDamage, Flags(Spawned(className, "0")));
}

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Whether a dynamic prop survives its spawn: stock's CBaseProp::Spawn
/// (props.cpp), which deletes a prop with no model, and a non-override prop
/// whose model's prop data says it must be a physics prop or is bad.
///
/// <para>
/// Found side by side with stock (tools/stock-ab, 2026-10-25): ss_sandbox's
/// prop_dynamics on crate, chair and drum models drew here and were deleted
/// in stock ("uses model ..., which has propdata which means that it be used
/// on a prop_physics. DELETED.").
/// </para>
/// </summary>
public class DynamicPropSpawnTests
{
    private const string PhysicsModel = "mdlkeyvalue { prop_data { health 40 } }";

    private static ScriptedEntityRegistry Spawned(string className, string model, string? keyValueText, uint index)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropDynamic).Assembly);
        registry.Services = Services(out var engine);
        engine.Returns("Precache.PrecacheModel", 3)
              .Returns("EntityRender.GetModelKeyValueText", keyValueText)
              .Returns("Entities.GetSpawnOrigin", new Vector(832f, -2496f, 8f))
              .Returns("Precache.BreakPieceCount", 0)
              .Returns("EntityRender.GetModelName", model)
              .Returns("Entities.GetHealth", 0)
              .Ignores("Entities.SetParent", "EntityPhysics.InitStaticPhysics", "Precache.PrecacheSound", "Entities.SetHealth");

        var handle = new EntityHandle(index);
        if (model.Length > 0)
            registry.KeyValue(handle, className, "model", model);
        registry.Spawn(handle, className);
        return registry;
    }

    private static bool Deleted(ScriptedEntityRegistry registry) => registry.Directory.PendingRemovalCount == 1;

    [Fact]
    public void ADynamicPropOnAPhysicsModelIsDeleted()
        => Assert.True(Deleted(Spawned(EntityNames.PropDynamic, "models/crate.mdl", PhysicsModel, 0x0004_0301u)),
                       "stock deletes a prop_dynamic whose model has prop data");

    [Fact]
    public void TheOverrideClassKeepsIt()
        => Assert.False(Deleted(Spawned(EntityNames.PropDynamicOverride, "models/crate.mdl", PhysicsModel, 0x0004_0302u)),
                        "prop_dynamic_override is how a map keeps one");

    [Fact]
    public void DynamicPropIsAPropDynamicByThen()
        => Assert.True(Deleted(Spawned(EntityNames.DynamicProp, "models/crate.mdl", PhysicsModel, 0x0004_0303u)),
                       "dynamic_prop is renamed prop_dynamic before the check, so it is not an override");

    [Fact]
    public void AnOrnamentOnAPhysicsModelIsDeleted()
        => Assert.True(Deleted(Spawned(EntityNames.PropDynamicOrnament, "models/crate.mdl", PhysicsModel, 0x0004_0304u)),
                       "COrnamentProp is a CDynamicProp");

    [Fact]
    public void AModelWithNoPropDataIsKept()
        => Assert.False(Deleted(Spawned(EntityNames.PropDynamic, "models/sign.mdl", null, 0x0004_0305u)),
                        "a model with no prop data is a dynamic prop's");

    [Fact]
    public void AModelThatAllowsStaticIsKept()
        => Assert.False(Deleted(Spawned(EntityNames.PropDynamic, "models/barrel.mdl",
                                        "mdlkeyvalue { prop_data { health 40 allowstatic 1 } }", 0x0004_0306u)),
                        "allowstatic lets a physics model be dynamic too");

    [Fact]
    public void ADynamicPropWithNoModelIsDeleted()
        => Assert.True(Deleted(Spawned(EntityNames.PropDynamic, "", null, 0x0004_0307u)),
                       "stock: \"prop at ... missing modelname\", removed");
}

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The old prop names condensed to one at spawn, as CDynamicProp::Spawn and
/// CPhysicsProp::Spawn do (props.cpp): a dynamic_prop reports prop_dynamic
/// and a physics_prop prop_physics from then on, and the overrides follow
/// once their spawn has read the override name. Found beside stock on
/// ss_sandbox (2026-10-27): the census counted 10 prop_dynamic_override, one
/// prop_physics_override, a dynamic_prop and a physics_prop that stock
/// reports by the two names.
/// </summary>
public class ClassnameCondensingTests
{
    private static readonly EntityHandle Prop = new(0x0004_0331u);

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine) Spawned(string className)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropDynamic).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("Precache.PrecacheModel", 3)
              .Returns("EntityRender.GetModelKeyValueText", null);

        registry.KeyValue(Prop, className, "model", "models/props_c17/oildrum001.mdl");
        registry.Spawn(Prop, className);
        return (registry, engine);
    }

    [Theory]
    [InlineData(EntityNames.DynamicProp, EntityNames.PropDynamic)]
    [InlineData(EntityNames.PhysicsProp, EntityNames.PropPhysics)]
    [InlineData(EntityNames.PropDynamicOverride, EntityNames.PropDynamic)]
    [InlineData(EntityNames.PropPhysicsOverride, EntityNames.PropPhysics)]
    public void An_old_name_or_an_override_reports_the_new_one_to_the_engine_and_the_directory(string placed, string reported)
    {
        var (registry, engine) = Spawned(placed);

        var renamed = Assert.Single(engine.AllOf("Entities.SetClassName"));
        Assert.Equal(Prop, renamed.ArgAs<EntityHandle>(0));
        Assert.Equal(reported, (string)renamed.Args[1]!);
        Assert.Equal(reported, registry.Get(Prop)!.ClassName);
    }

    [Theory]
    [InlineData(EntityNames.DynamicProp, EntityNames.PropDynamic)]
    [InlineData(EntityNames.PhysicsProp, EntityNames.PropPhysics)]
    [InlineData(EntityNames.PropDynamicOverride, EntityNames.PropDynamic)]
    public void It_is_found_by_the_new_name_and_no_longer_by_the_old(string placed, string reported)
    {
        var (registry, _) = Spawned(placed);

        var found = new List<EntityHandle>();
        registry.Directory.FindByClassName(reported, found);
        Assert.Equal([Prop], found);

        found.Clear();
        registry.Directory.FindByClassName(placed, found);
        Assert.Empty(found);
        Assert.DoesNotContain(placed, registry.Directory.ClassNames, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(EntityNames.PropDynamic)]
    [InlineData(EntityNames.PropPhysics)]
    public void The_new_names_keep_theirs(string placed)
    {
        var (registry, engine) = Spawned(placed);

        Assert.False(engine.Called("Entities.SetClassName"));
        Assert.Equal(placed, registry.Get(Prop)!.ClassName);
    }
}

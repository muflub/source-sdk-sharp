using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Whose health and explosion a physics prop has (CPropData::ParsePropFromKV).
/// A model's prop_data sets them over the map's keys wherever it names them, a
/// metal base's explicit health 0 included. prop_physics_override keeps the
/// map's health and its ExplodeDamage and ExplodeRadius. Found 2026-10-28:
/// ours let the map's health win and read the model's explosion on every
/// class, and took no ExplodeDamage or ExplodeRadius key at all.
/// </summary>
public class PropDataPrecedenceTests
{
    private static readonly EntityHandle Prop = new(61);

    private const string Metal = "mdlkeyvalue { prop_data { health 0 } }";
    private const string Barrel = "mdlkeyvalue { prop_data { health 20 explosive_damage 120 explosive_radius 256 } }";

    private static (PropPhysics Prop, RecordingEngine Engine) Spawned(string className, string text, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("EntityRender.GetModelKeyValueText", text)
              .Returns("Precache.BreakPieceCount", 4);

        registry.KeyValue(Prop, className, "model", "models/props_junk/wood_crate001a.mdl");
        foreach (var (key, value) in keys)
            registry.KeyValue(Prop, className, key, value);
        registry.Spawn(Prop, className);
        var prop = (PropPhysics)registry.Get(Prop)!;
        prop.OnPhysicsCreated();
        return (prop, engine);
    }

    private static int? SpawnHealth(RecordingEngine engine)
        => engine.AllOf("Entities.SetHealth").Select(c => (int?)c.ArgAs<int>(1)).LastOrDefault();

    [Fact]
    public void A_models_health_stands_over_the_maps()
        => Assert.Equal(20, SpawnHealth(Spawned(EntityNames.PropPhysics, Barrel, ("health", "50")).Engine));

    /// <summary>"By default, metal objects aren't breakable": an explicit 0 is a value.</summary>
    [Fact]
    public void A_metal_models_zero_health_makes_it_unbreakable_whatever_the_map_says()
    {
        var (prop, engine) = Spawned(EntityNames.PropPhysics, Metal, ("health", "50"));

        Assert.Equal(0, SpawnHealth(engine));
        Assert.Equal(0, ((ScriptedEntityRegistry)prop.Owner!).Damaged(Prop, EntityHandle.None, 10f, (int)DamageType.Bullet));
    }

    [Fact]
    public void Where_the_model_is_silent_the_maps_health_stands()
        => Assert.Equal(50, SpawnHealth(Spawned(EntityNames.PropPhysics, "mdlkeyvalue { prop_data { dmg.club 2 } }", ("health", "50")).Engine));

    [Fact]
    public void An_override_keeps_the_maps_health_and_explosion()
    {
        var (prop, engine) = Spawned(EntityNames.PropPhysicsOverride, Barrel, ("health", "50"), ("ExplodeDamage", "0"), ("ExplodeRadius", "0"));
        Assert.Equal(50, SpawnHealth(engine));

        prop.Owner!.Input(Prop, "Break", "", 0f, EntityHandle.None);

        Assert.False(engine.Called("Effects.ExplosionEffect"));
    }

    [Fact]
    public void An_override_explodes_by_its_maps_keys()
    {
        var (prop, engine) = Spawned(EntityNames.PropPhysicsOverride, "", ("health", "5"), ("ExplodeDamage", "50"), ("ExplodeRadius", "100"));

        prop.Owner!.Input(Prop, "Break", "", 0f, EntityHandle.None);

        var blast = Assert.Single(((RecordingBlastSweep)prop.Owner!.Services!.Resolve(typeof(IBlastSweep))!).Requests);
        Assert.Equal((50f, 100f), (blast.Magnitude, blast.Radius));
    }
}

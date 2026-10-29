using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A prop_dynamic is a CBreakableProp (props.cpp). Given health (the map's
/// key: an override keeps it over its model's prop_data) it takes damage
/// through its model's damage modifiers. It breaks at none, by a blow or by
/// RemoveHealth, and catches fire only when its model is flammable. Found on
/// ss_sandbox (2026-10-28): ours gave the prop_dynamic_override chair with
/// health 40 no damage at all, where stock's breaks.
/// </summary>
public class PropDynamicBreakTests
{
    private static readonly EntityHandle Prop = new(0x0004_0401u);
    private static readonly EntityHandle Shooter = new(0x0001_0001u);

    private const string ChairText = "mdlkeyvalue { prop_data { health 100 \"dmg.bullets\" 0.5 } }";

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public int Health;

        public PropDynamic Entity => (PropDynamic)Registry.Get(Prop)!;

        public int Hit(float damage, DamageType type)
        {
            int taken = Registry.Damaged(Prop, Shooter, damage, (int)type);
            Health -= taken;
            if (Health <= 0 && taken > 0)
                Registry.Killed(Prop, Shooter);
            return taken;
        }

        public int SpawnHealth => Engine.AllOf("Entities.SetHealth").Last().ArgAs<int>(1);
    }

    private static Rig Spawned(string className = EntityNames.PropDynamicOverride, string? health = "40",
                               string? text = ChairText)
    {
        var services = Services(out var engine);
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropDynamic).Assembly);
        services.AddInstance(new Flames(new RecordingLog(), registry, engine));
        services.AddInstance<IEntityRoster<EnvFire>>(new EntityRoster<EnvFire>());
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;

        var rig = new Rig { Registry = registry, Engine = engine };
        // The chair breaks into its model's six pieces.
        engine.Returns("Precache.PrecacheModel", 3)
              .Returns("Precache.BreakPieceCount", 6)
              .Returns("EntityRender.GetModelKeyValueText", text)
              .ReturnsEachCall("Entities.GetHealth", () => rig.Health);

        registry.KeyValue(Prop, className, "model", "models/props_c17/furniturechair001a.mdl");
        if (health is not null)
            registry.KeyValue(Prop, className, "health", health);
        registry.Spawn(Prop, className);
        rig.Health = rig.SpawnHealth;
        return rig;
    }

    /// <summary>ParsePropFromKV reads no health for an override: the map's 40 stands over the model's 100.</summary>
    [Fact]
    public void An_override_spawns_with_its_maps_health_over_its_models()
    {
        var rig = Spawned();

        Assert.Equal(40, rig.SpawnHealth);
    }

    /// <summary>A model with no prop_data leaves a plain prop_dynamic the map's health.</summary>
    [Fact]
    public void A_prop_dynamic_whose_model_has_no_prop_data_takes_its_maps_health()
    {
        var rig = Spawned(EntityNames.PropDynamic, health: "25", text: null);

        Assert.Equal(25, rig.SpawnHealth);
    }

    /// <summary>GetBreakableDamage: the model's dmg.bullets halves a bullet, even on an override.</summary>
    [Fact]
    public void A_blow_is_scaled_by_its_models_damage_modifiers()
    {
        var rig = Spawned();

        Assert.Equal(5, rig.Hit(10f, DamageType.Bullet));
        Assert.Equal(10, rig.Hit(10f, DamageType.Club));
        Assert.Equal(2, rig.Entity.OnTakeDamage.FireCount);
    }

    [Fact]
    public void A_blow_that_takes_its_health_breaks_it()
    {
        var rig = Spawned();

        rig.Hit(30f, DamageType.Club);
        Assert.Equal(0, rig.Entity.OnBreak.FireCount);

        rig.Hit(10f, DamageType.Club);

        Assert.Equal(1, rig.Entity.OnBreak.FireCount);
        Assert.True(rig.Entity.Gone);
        Assert.Contains(rig.Engine.AllOf("EntityPhysics.SetNotSolid"), c => c.ArgAs<bool>(1));
    }

    /// <summary>With no health it is DAMAGE_EVENTS_ONLY: a blow takes nothing.</summary>
    [Fact]
    public void One_with_no_health_takes_no_damage()
    {
        var rig = Spawned(health: null);

        Assert.Equal(0, rig.Hit(50f, DamageType.Club));
        Assert.Equal(0, rig.Entity.OnTakeDamage.FireCount);
    }

    /// <summary>
    /// UpdateHealth breaks it, and HL2MP's Break removes nothing: it stays,
    /// not solid and still drawn, as stock's chair does on ss_sandbox
    /// (2026-10-28 A/B: health 0, effects 0, still listed).
    /// </summary>
    [Fact]
    public void RemoveHealth_to_nothing_breaks_it_in_place()
    {
        var rig = Spawned();

        rig.Registry.Input(Prop, "RemoveHealth", "40", 40f, EntityHandle.None);

        Assert.Equal(1, rig.Entity.OnBreak.FireCount);
        Assert.False(rig.Entity.Gone);
        Assert.Equal(0, rig.Registry.Directory.PendingRemovalCount);
        Assert.Contains(rig.Engine.AllOf("EntityPhysics.SetNotSolid"), c => c.ArgAs<bool>(1));
    }

    /// <summary>CBreakableProp::Precache: m_iNumBreakableChunks is its model's piece count, read at spawn.</summary>
    [Fact]
    public void Its_spawn_counts_the_pieces_its_model_breaks_into()
    {
        var services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("Precache.BreakPieceCount", 3);
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropDynamic).Assembly);
        registry.Services = services;

        registry.KeyValue(Prop, EntityNames.PropDynamicOverride, "model", "models/props_c17/furniturechair001a.mdl");
        registry.Spawn(Prop, EntityNames.PropDynamicOverride);

        Assert.Equal(3, ((PropDynamic)registry.Get(Prop)!).BreakPieceCount);
    }

    /// <summary>CBreakableProp::Ignite: a chair whose model is not flammable does not catch.</summary>
    [Fact]
    public void Only_a_flammable_model_catches_fire()
    {
        var chair = Spawned();
        chair.Registry.Input(Prop, "Ignite", "", 0f, EntityHandle.None);
        Assert.Equal(0, chair.Entity.OnIgnite.FireCount);

        var flammable = Spawned(text: "mdlkeyvalue { prop_data { health 100 } fire_interactions { flammable yes } }");
        flammable.Registry.Input(Prop, "Ignite", "", 0f, EntityHandle.None);
        Assert.Equal(1, flammable.Entity.OnIgnite.FireCount);
    }
}

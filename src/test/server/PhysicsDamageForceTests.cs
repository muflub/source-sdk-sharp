using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_physics_managed A30: damage pushes a physics object. Stock's
/// CBaseEntity::OnTakeDamage calls VPhysicsTakeDamage for a MOVETYPE_VPHYSICS
/// entity (baseentity.cpp:1526-1531), which applies the damage force at the
/// damage position unless the type carries no force (Damage_NoPhysicsForce, or
/// DMG_GENERIC) (:1804-1866). Every physics class's OnTakeDamage reaches it
/// after its own refusals: CBreakableProp (props.cpp:1040-1161), CBreakable for
/// func_physbox (func_break.cpp:823-845), CPushable only when breakable (:1297).
/// </summary>
public class PhysicsDamageForceTests
{
    private static readonly Vector Where = new(10f, 20f, 30f);
    private static readonly Vector Push = new(1000f, 0f, 0f);

    private static (T Entity, RecordingEngine Engine) Spawn<T>(string className, params (string Key, string Value)[] keys)
        where T : ScriptedEntity
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;

        engine.Returns("EntityRender.GetModelName", "");
        engine.Returns("EntityRender.GetModelKeyValueText", "");

        var handle = new EntityHandle(41);
        foreach (var (key, value) in keys)
            registry.KeyValue(handle, className, key, value);
        registry.Spawn(handle, className);
        return ((T)registry.Get(handle)!, engine);
    }

    private static bool Pushed(RecordingEngine engine)
        => engine.Calls.Any(c => c.Member == "EntityPhysics.ApplyForceOffset"
                                 && Equals(c.Args[1], Push) && Equals(c.Args[2], Where));

    [Fact]
    public void A_bullet_pushes_a_prop_that_cannot_break()
    {
        // No model data: health 0, DAMAGE_EVENTS_ONLY in stock, which still
        // reaches CBaseEntity::OnTakeDamage and its push.
        var (prop, engine) = Spawn<PropPhysics>(EntityNames.PropPhysics);

        prop.OnDamagedAt(EntityHandle.None, 20f, DamageType.Bullet, Where, Push);

        Assert.True(Pushed(engine), "stock's VPhysicsTakeDamage applies the force at the position");
    }

    [Fact]
    public void A_gib_is_pushed_too()
    {
        var (prop, engine) = Spawn<PropPhysics>(EntityNames.PropPhysics, ("gibfadetime", "5"));

        prop.OnDamagedAt(EntityHandle.None, 20f, DamageType.Blast, Where, Push);

        Assert.True(Pushed(engine));
    }

    [Theory]
    [InlineData(DamageType.Generic)]
    [InlineData(DamageType.Crush)]
    [InlineData(DamageType.Burn)]
    [InlineData(DamageType.Bullet | DamageType.PreventPhysicsForce)]
    public void Damage_that_carries_no_force_does_not_push(DamageType type)
    {
        var (prop, engine) = Spawn<PropPhysics>(EntityNames.PropPhysics);

        prop.OnDamagedAt(EntityHandle.None, 20f, type, Where, Push);

        Assert.False(Pushed(engine));
    }

    [Fact]
    public void A_physbox_is_pushed_by_a_shot_it_does_not_ignore()
    {
        var (box, engine) = Spawn<FuncPhysbox>(EntityNames.FuncPhysbox, ("health", "60"));

        box.OnDamagedAt(EntityHandle.None, 20f, DamageType.Bullet, Where, Push);

        Assert.True(Pushed(engine));
    }

    [Fact]
    public void A_physbox_shot_under_its_minimum_is_not_pushed()
    {
        // CBreakable returns before the base when damage < m_iMinHealthDmg.
        var (box, engine) = Spawn<FuncPhysbox>(EntityNames.FuncPhysbox, ("health", "60"), ("minhealthdmg", "50"));

        box.OnDamagedAt(EntityHandle.None, 20f, DamageType.Bullet, Where, Push);

        Assert.False(Pushed(engine));
    }

    [Fact]
    public void A_breakable_pushable_is_pushed_and_furniture_is_not()
    {
        // Spawnflags come from the engine: 0x80 is SF_PUSH_BREAKABLE.
        var (crate, engine) = Spawn<FuncPushable>(EntityNames.FuncPushable, ("health", "50"));
        engine.Returns("Entities.GetSpawnFlags", 0x0080);
        crate.OnDamagedAt(EntityHandle.None, 20f, DamageType.Bullet, Where, Push);
        Assert.True(Pushed(engine), "SF_PUSH_BREAKABLE reaches CBreakable and the base");

        var (furniture, engine2) = Spawn<FuncPushable>(EntityNames.FuncPushable, ("health", "50"));
        engine2.Returns("Entities.GetSpawnFlags", 0);
        furniture.OnDamagedAt(EntityHandle.None, 20f, DamageType.Bullet, Where, Push);
        Assert.False(Pushed(engine2), "CPushable returns 1 without the base when not breakable");
    }

    // A32: stock sets COLLISION_GROUP_DEBRIS for the debris spawnflag before the
    // object is made: CPhysicsProp::Spawn (props.cpp:2497-2500, SF_PHYSPROP_DEBRIS
    // = 4) and CPhysBox::Spawn (physobj.cpp:448-450, SF_PHYSBOX_DEBRIS = 0x4000).
    // Ours ignored it on props and, on a physbox, set the no-pickup game flag
    // instead, so debris crates blocked players and doorways.
    private static RecordingEngine SpawnedWithFlags(string className, int spawnFlags)
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("EntityRender.GetModelName", "");
        engine.Returns("EntityRender.GetModelKeyValueText", "");
        engine.Returns("Entities.GetSpawnFlags", spawnFlags);
        registry.Spawn(new EntityHandle(42), className);
        return engine;
    }

    private static bool MadeDebris(RecordingEngine engine)
        => engine.Calls.Any(c => c.Member == "EntityPhysics.SetCollisionGroup" && Equals(c.Args[1], CollisionGroup.Debris));

    [Fact]
    public void A_debris_prop_is_in_the_debris_group()
    {
        Assert.True(MadeDebris(SpawnedWithFlags(EntityNames.PropPhysics, 0x4)));
        Assert.False(MadeDebris(SpawnedWithFlags(EntityNames.PropPhysics, 0)), "the control: no flag, no group");
    }

    [Fact]
    public void A_debris_physbox_is_in_the_debris_group()
    {
        Assert.True(MadeDebris(SpawnedWithFlags(EntityNames.FuncPhysbox, 0x4000)));
        Assert.False(MadeDebris(SpawnedWithFlags(EntityNames.FuncPhysbox, 0)), "the control: no flag, no group");
    }

    // A38: CBaseEntity's DisableDamageForces / EnableDamageForces inputs set and
    // clear EFL_NO_DAMAGE_FORCES, which OnTakeDamage reads before it calls
    // VPhysicsTakeDamage (baseentity.cpp:1526, 4554-4562).
    [Fact]
    public void DisableDamageForces_stops_the_push_and_EnableDamageForces_restores_it()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("EntityRender.GetModelName", "");
        engine.Returns("EntityRender.GetModelKeyValueText", "");
        var handle = new EntityHandle(44);
        registry.Spawn(handle, EntityNames.PropPhysics);
        var prop = (PropPhysics)registry.Get(handle)!;

        Assert.True(registry.Input(handle, "DisableDamageForces", "", 0f, EntityHandle.None), "the input is claimed");
        prop.OnDamagedAt(EntityHandle.None, 20f, DamageType.Bullet, Where, Push);
        Assert.False(Pushed(engine));

        registry.Input(handle, "EnableDamageForces", "", 0f, EntityHandle.None);
        prop.OnDamagedAt(EntityHandle.None, 20f, DamageType.Bullet, Where, Push);
        Assert.True(Pushed(engine));
    }
}

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// CBreakableProp's fire (props.cpp): Ignite only for a flammable model, and
/// OnTakeDamage's interactions. A bullet that takes an ignite-halfhealth prop to
/// half sets it alight at full health, and one into a burning prop sets it off.
/// A blast or burn that does not kill sets it alight. A deadly blast burns an
/// explosive_resist prop for a while instead. Only its own flame burns it, and
/// OnTakeDamage names the attacker. Found 2026-10-28: dm_lockdown's 21
/// explosive barrels never caught fire.
/// </summary>
public class PropFireTests
{
    private static readonly EntityHandle Barrel = new(0x000E_0101u);
    private static readonly EntityHandle FlameHandle = new(0x000E_0102u);
    private static readonly EntityHandle Shooter = new(0x0001_0001u);
    private static readonly EntityHandle Bomb = new(0x000E_0103u);

    /// <summary>oildrum001_explosive.mdl's $keyvalues, as the model holds them.</summary>
    private const string BarrelText = "mdlkeyvalue\n{\nprop_data {\n\"dmg.bullets\" \"1.0\" \"dmg.club\" \"1.0\" \"dmg.explosive\" \"1.0\" \"health\" \"20\" \"explosive_damage\" \"120\" \"explosive_radius\" \"256\"  }\nphysgun_interactions {\n\"onfirstimpact\" \"break\"  }\nfire_interactions {\n\"flammable\" \"yes\" \"explosive_resist\" \"yes\" \"ignite\" \"halfhealth\"  }\n}\n";

    private const string CrateText = "mdlkeyvalue { prop_data { health 20 } }";

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public required ServiceContainer Services;
        public int Health = 20;
        public Vector BombCentre = new(128f, 0f, 0f);

        public PropPhysics Entity => (PropPhysics)Registry.Get(Barrel)!;

        public SsEntityFlame Flame => (SsEntityFlame)Registry.Get(FlameHandle)!;

        public bool Burning => Flame.Attached == Barrel;

        /// <summary>A blow, and the engine's half of it: the health it took.</summary>
        public int Hit(float damage, DamageType type, EntityHandle inflictor = default)
        {
            using var scope = inflictor != EntityHandle.None
                ? Services.Require<DamageInflictors>().Push(inflictor, BombCentre)
                : default(DamageInflictors.Scope?);
            int taken = Registry.Damaged(Barrel, Shooter, damage, (int)type);
            Health -= taken;
            return taken;
        }
    }

    private static Rig Spawned(string text = BarrelText)
    {
        var services = Services(out var engine);
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        services.AddInstance(new Flames(new RecordingLog(), registry, engine));
        services.AddInstance<IEntityRoster<EnvFire>>(new EntityRoster<EnvFire>());
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;

        var rig = new Rig { Registry = registry, Engine = engine, Services = services };
        engine.Returns("EntityRender.GetModelKeyValueText", text)
              .Returns("Entities.IsValid", true)
              .Returns("Entities.Create", FlameHandle)
              .Returns("Clock.Now", 10f)
              .ReturnsEachCall("Entities.GetHealth", () => rig.Health)
              .ReturnsEachCall("Entities.GetWorldSpaceCenter",
                               () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Bomb ? rig.BombCentre : Vector.Zero);

        registry.KeyValue(Barrel, EntityNames.PropPhysics, "model", "models/props_c17/oildrum001_explosive.mdl");
        registry.Spawn(Barrel, EntityNames.PropPhysics);
        registry.KeyValue(FlameHandle, EntityNames.EntityFlame, "origin", "0 0 0");
        rig.Entity.OnPhysicsCreated();
        return rig;
    }

    private static int SetHealthTo(RecordingEngine engine)
        => engine.Last("Entities.SetHealth")!.Value.ArgAs<int>(1);

    [Fact]
    public void A_bullet_to_half_health_sets_it_alight_back_at_full_health()
    {
        var rig = Spawned();
        int sets = rig.Engine.AllOf("Entities.SetHealth").Count();

        int taken = rig.Hit(12f, DamageType.Bullet);

        Assert.True(rig.Burning);
        Assert.Equal(sets + 1, rig.Engine.AllOf("Entities.SetHealth").Count());
        Assert.Equal(20, SetHealthTo(rig.Engine));
        Assert.Equal(12, taken);
    }

    [Fact]
    public void A_bullet_above_half_health_sets_nothing_alight()
    {
        var rig = Spawned();

        rig.Hit(5f, DamageType.Bullet);

        Assert.NotEqual(Barrel, rig.Flame.Attached);
    }

    [Fact]
    public void A_bullet_into_a_burning_barrel_sets_it_off()
    {
        var rig = Spawned();
        rig.Entity.Ignite(10f);

        Assert.Equal(20, rig.Hit(1f, DamageType.Bullet));
    }

    [Fact]
    public void A_blast_that_does_not_kill_sets_a_flammable_prop_alight_and_not_a_crate()
    {
        var barrel = Spawned();
        barrel.Hit(5f, DamageType.Blast);
        Assert.True(barrel.Burning);

        var crate = Spawned(CrateText);
        crate.Hit(5f, DamageType.Blast);
        Assert.NotEqual(Barrel, crate.Flame.Attached);
    }

    /// <summary>
    /// A deadly blast 128 units off burns an explosive_resist barrel for 2.5 s
    /// (the random half second answering 0): it keeps FLAME_DIRECT_DAMAGE_PER_SEC
    /// times that, 12.5 health, and catches.
    /// </summary>
    [Fact]
    public void A_deadly_blast_from_afar_burns_an_explosive_resist_barrel_instead()
    {
        var rig = Spawned();

        int taken = rig.Hit(100f, DamageType.Blast, inflictor: Bomb);

        Assert.Equal(7, taken);
        Assert.True(rig.Burning);
    }

    [Fact]
    public void A_deadly_blast_close_by_sets_it_off_at_once()
    {
        var rig = Spawned();
        rig.BombCentre = new Vector(10f, 0f, 0f);

        Assert.Equal(20, rig.Hit(100f, DamageType.Blast, inflictor: Bomb));
        Assert.NotEqual(Barrel, rig.Flame.Attached);
    }

    [Fact]
    public void Only_its_own_flame_burns_it()
    {
        var rig = Spawned();
        rig.Entity.Ignite(10f);

        Assert.Equal(0, rig.Hit(5f, DamageType.Burn));
        Assert.Equal(5, rig.Hit(5f, DamageType.Burn | DamageType.Direct));
    }

    [Fact]
    public void A_crate_that_is_not_flammable_does_not_catch()
    {
        var rig = Spawned(CrateText);

        rig.Registry.Input(Barrel, "Ignite", "", 0f, EntityHandle.None);

        Assert.NotEqual(Barrel, rig.Flame.Attached);
        Assert.Equal(0, rig.Entity.OnIgnite.FireCount);
    }

    [Fact]
    public void OnTakeDamage_fires_for_a_blow()
    {
        var rig = Spawned();

        rig.Hit(3f, DamageType.Club);

        Assert.Equal(1, rig.Entity.OnTakeDamage.FireCount);
    }
}

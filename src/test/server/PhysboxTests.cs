using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// func_physbox's damage rules.
///
/// The one worth a test is not a rule at all but a convention: OnDamaged
/// returns how much damage to take, and the SHELL applies it. Subtracting
/// health here as well takes it twice — 60 health hit for 25 came out at 10 —
/// and nothing complains, because taking damage twice looks exactly like taking
/// damage.
/// </summary>
public class PhysboxTests
{

    private static FuncPhysbox Spawn(uint index, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncPhysbox).Assembly);
        registry.Services = Ignoring("Entities.GetHealth", "EntityPhysics.InitPhysics");

        var handle = new EntityHandle(index);

        foreach (var (key, value) in keys)
            registry.KeyValue(handle, EntityNames.FuncPhysbox, key, value);

        registry.Spawn(handle, EntityNames.FuncPhysbox);
        return (FuncPhysbox)registry.Get(handle)!;
    }

    [Fact]
    public void DamageIsReportedNotApplied()
    {
        var box = Spawn(1, ("health", "60"));

        // The whole amount comes back, so the shell subtracts it once.
        Assert.True(box.OnDamaged(EntityHandle.None, 25f, DamageType.Bullet) == 25, "all of the damage is reported back to the shell");

        // And again: a second hit is not scaled by what the first did, because
        // this class is not tracking health itself.
        Assert.True(box.OnDamaged(EntityHandle.None, 25f, DamageType.Bullet) == 25, "a second hit reports the same way");
    }

    [Fact]
    public void MinimumDamage()
    {
        var box = Spawn(2, ("health", "60"), ("minhealthdmg", "20"));

        // Under the threshold is ignored ENTIRELY, not reduced — a physbox with
        // minhealthdmg set is meant to shrug small arms off.
        Assert.True(box.OnDamaged(EntityHandle.None, 5f, DamageType.Bullet) == 0, "damage under the minimum is ignored");

        Assert.True(box.OnDamaged(EntityHandle.None, 20f, DamageType.Bullet) == 20, "damage at the minimum is taken");
    }

    [Fact]
    public void Indestructible()
    {
        var box = Spawn(3, ("health", "0"));

        // No health means it cannot break, so it takes nothing — but the hit is
        // still reported, because a map may wire OnDamaged on something it
        // never expects to destroy.
        Assert.True(box.OnDamaged(EntityHandle.None, 40f, DamageType.Bullet) == 0, "a box with no health takes no damage");

        Assert.True(box.Damaged.FireCount == 1, "and still reports having been hit");
    }

    // A28: CPhysBox's OnAwakened, fired the first time a start-asleep box moves
    // (physobj.cpp:716-722), and OnPlayerUse, fired from Use() by a player when
    // SF_PHYSBOX_ENABLE_PICKUP_OUTPUT (0x80000) is set (:571-580).
    private static (ScriptedEntityRegistry Registry, FuncPhysbox Box, RecordingEngine Engine) Flagged(int spawnFlags)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncPhysbox).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Entities.GetSpawnFlags", spawnFlags);
        engine.Returns("EntityPhysics.InitPhysics", true);
        engine.Returns("Entities.IsPlayer", true);
        registry.Spawn(new EntityHandle(31), EntityNames.FuncPhysbox);
        return (registry, (FuncPhysbox)registry.Get(new EntityHandle(31))!, engine);
    }

    [Fact]
    public void A_start_asleep_physbox_fires_OnAwakened_when_it_first_wakes()
    {
        var (_, box, _) = Flagged(0x1000);

        box.OnPhysicsWake();

        Assert.Equal(1, box.OnAwakened.FireCount);
    }

    [Fact]
    public void An_awake_physbox_does_not_fire_OnAwakened()
    {
        var (_, box, _) = Flagged(0);

        box.OnPhysicsWake();

        Assert.Equal(0, box.OnAwakened.FireCount);
    }

    [Fact]
    public void A_player_using_a_physbox_with_the_pickup_output_flag_fires_OnPlayerUse()
    {
        var (registry, box, _) = Flagged(0x80000);

        Assert.True(registry.Input(new EntityHandle(31), "Use", "", 0f, new EntityHandle(1)), "Use is claimed");

        Assert.Equal(1, box.OnPlayerUse.FireCount);
    }

    [Fact]
    public void Without_the_flag_a_use_fires_nothing()
    {
        var (registry, box, _) = Flagged(0);

        registry.Input(new EntityHandle(31), "Use", "", 0f, new EntityHandle(1));

        Assert.Equal(0, box.OnPlayerUse.FireCount);
    }

    // 8.7: CBreakable::UpdateHealth outputs the clamped ratio
    // clamp(health / maxhealth, 0, 1) (func_break.cpp:685-698), not raw health.
    [Fact]
    public void OnHealthChanged_carries_the_clamped_health_ratio()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncPhysbox).Assembly);
        var services = Services(out var engine);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Entities.GetHealth", 60);
        var handle = new EntityHandle(41);
        registry.KeyValue(handle, EntityNames.FuncPhysbox, "health", "60");
        registry.Spawn(handle, EntityNames.FuncPhysbox);
        var box = (FuncPhysbox)registry.Get(handle)!;

        box.OnDamaged(EntityHandle.None, 30f, DamageType.Bullet);
        Assert.Equal("0.5", box.OnHealthChanged.LastValue);

        box.OnDamaged(EntityHandle.None, 100f, DamageType.Bullet);
        Assert.Equal("0", box.OnHealthChanged.LastValue);
    }

    // 8.12: CPhysBox::OnTakeDamage fires m_OnDamaged with the attacker as
    // activator (physobj.cpp:811), and UpdateHealth/Break carry the same
    // attacker into OnHealthChanged and OnBreak (func_break.cpp:850, :703, :1112).
    [Fact]
    public void Damage_outputs_carry_the_attacker_as_activator()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncPhysbox).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Entities.GetHealth", 60);
        var handle = new EntityHandle(42);
        var attacker = new EntityHandle(0x0001_0043);
        registry.KeyValue(handle, EntityNames.FuncPhysbox, "health", "60");
        // Keyed before the class's first Spawn, in map-load order: the declared
        // name "OnDamaged" (field Damaged) must connect on the first instance.
        registry.KeyValue(handle, EntityNames.FuncPhysbox, "OnDamaged", "sink,Damaged,,0,-1");
        registry.KeyValue(handle, EntityNames.FuncPhysbox, "OnHealthChanged", "sink,Health,,0,-1");
        registry.KeyValue(handle, EntityNames.FuncPhysbox, "OnBreak", "sink,Broke,,0,-1");
        registry.Spawn(handle, EntityNames.FuncPhysbox);
        var box = (FuncPhysbox)registry.Get(handle)!;

        box.OnDamaged(attacker, 30f, DamageType.Bullet);
        box.OnKilled(attacker);

        var pending = registry.Directory.Io.Pending;
        Assert.Equal("Damaged,Health,Broke", string.Join(",", pending.Select(e => e.TargetInput)));
        foreach (var e in pending)
            Assert.True(e.Activator == attacker, $"{e.TargetInput} carries the attacker");
    }
}

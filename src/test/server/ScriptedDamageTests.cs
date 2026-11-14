using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// A victim that says whether it runs the damage rules, remembers each number
/// it was handed and answers a fixed amount (or the number itself).
/// </summary>
[ScriptedEntity("test_damage_rules_probe")]
public sealed class DamageRulesProbeEntity : ScriptedEntity
{
    /// <summary>The opt-in, flipped by a fact to show it is what matters.</summary>
    public bool Runs { get; set; } = true;

    /// <summary>What the next hits answer; negative answers the damage handed in, truncated.</summary>
    public int Answer { get; set; } = -1;

    public List<float> Handed { get; } = new();
    public List<EntityHandle> KilledBy { get; } = new();

    public override bool RunsDamageRules => Runs;

    public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                    Vector position, Vector force, HitGroup hitGroup = HitGroup.Generic)
    {
        Handed.Add(damage);
        return Answer >= 0 ? Answer : (int)damage;
    }

    public override void OnKilled(EntityHandle attacker) => KilledBy.Add(attacker);
}

/// <summary>A class that runs the rules and has no death of its own to run.</summary>
[ScriptedEntity("test_damage_rules_no_death")]
public sealed class DamageRulesNoDeathEntity : ScriptedEntity
{
    public override bool RunsDamageRules => true;

    public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                    Vector position, Vector force, HitGroup hitGroup = HitGroup.Generic)
        => (int)damage;
}

/// <summary>The same class without the opt-in: the control of the death op's arming.</summary>
[ScriptedEntity("test_damage_no_rules_no_death")]
public sealed class DamageNoRulesNoDeathEntity : ScriptedEntity
{
    public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                    Vector position, Vector force, HitGroup hitGroup = HitGroup.Generic)
        => (int)damage;
}

/// <summary>
/// The damage path of an entity that runs the damage rules
/// (<see cref="ScriptedEntity.RunsDamageRules"/>, <see cref="ScriptedDamage"/>):
/// the registry's damage route folds the hit through the mods'
/// <see cref="IDamageModifier"/> chain before the entity is handed it, and the
/// hits and the death are told to <see cref="IEntityHurtHook"/> and
/// <see cref="IEntityDeathHook"/>. Each fact drives the registry's own route,
/// the one the damage op reaches.
/// </summary>
public class ScriptedDamageTests
{
    private static readonly EntityHandle Victim = new(0x0001_0005u);
    private static readonly EntityHandle Attacker = new(0x0002_0001u);
    private static readonly Vector DiedAt = new(64f, -32f, 8f);

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class Scale(float factor) : IDamageModifier
    {
        public List<DamageContext> Seen { get; } = new();

        public float Modify(in DamageContext hit, float damage)
        {
            Seen.Add(hit);
            return damage * factor;
        }
    }

    private sealed class HurtTap : IEntityHurtHook
    {
        public List<EntityHurt> Heard { get; } = new();
        public void OnEntityHurt(in EntityHurt hurt) => Heard.Add(hurt);
    }

    private sealed class DeathTap(DamageRulesProbeEntity? probe = null) : IEntityDeathHook
    {
        public List<EntityDeath> Heard { get; } = new();

        /// <summary>How many deaths the entity itself had run when each was told.</summary>
        public List<int> OwnDeathsRunFirst { get; } = new();

        public void OnEntityDied(EntityDeath death)
        {
            Heard.Add(death);
            OwnDeathsRunFirst.Add(probe?.KilledBy.Count ?? -1);
        }
    }

    private sealed class Rig
    {
        public ScriptedEntityRegistry Registry { get; }
        public GameRules Rules { get; }
        public DamageRulesProbeEntity Probe { get; }
        public Scale Modifier { get; }
        public DamageHitGroups Hits { get; } = new();
        public HurtTap Hurt { get; } = new();
        public DeathTap Deaths { get; }

        public Rig(float factor = 3f, int health = 100, bool withService = true)
        {
            Modifier = new Scale(factor);

            var convars = ConVarRegistry.For(UnattachedConsole());
            convars.RegisterAll(typeof(GameRules).Assembly);
            var host = Fake().Returns("Host.ActiveSide", Side.Server);
            var players = Fake().Ignores("Players.FindByUserId", "Players.GetUserId", "Players.GiveItem", "Players.GiveAmmo")
                                .Returns("Players.GetTeam", 0)
                                .Returns("Players.RespawnDelay", 3f);
            Rules = new GameRules(new RecordingLog(), convars.Holder<GameRulesConVars>(), host, players,
                                  Fake().Returns("Entities.IsValid", true),
                                  new Scoreboard(host, new PlayerDisconnectEvent(), new RecordingLog()),
                                  new PlayerDeathEvent(),
                                  damageModifiers: new ListHooks<IDamageModifier>(Modifier),
                                  hits: Hits);

            Registry = new ScriptedEntityRegistry(TestEngines.Server());
            Registry.RegisterAll(typeof(ScriptedDamageTests).Assembly);
            Registry.KeyValue(Victim, "test_damage_rules_probe", "targetname", "probe");
            Probe = (DamageRulesProbeEntity)Registry.Get(Victim)!;
            Deaths = new DeathTap(Probe);

            var world = Fake().Returns("Entities.GetHealth", health).Returns("Entities.GetOrigin", DiedAt);
            var damage = new ScriptedDamage(Rules, world, Hits, new ListHooks<IEntityHurtHook>(Hurt),
                                            new ListHooks<IEntityDeathHook>(Deaths));
            Registry.Services = withService ? Services(out _, damage) : Services(out _);
        }

        public int Hit(float damage, DamageType type = DamageType.Blast, HitGroup group = HitGroup.Generic)
            => Registry.Damaged(Victim, Attacker, damage, (int)type, default, default, group);
    }

    //-------------------------------------------------------------------------
    // The fold.
    //-------------------------------------------------------------------------

    [Fact]
    public void An_entity_that_runs_the_rules_is_handed_the_folded_damage()
    {
        var rig = new Rig(factor: 3f);

        int taken = rig.Hit(10f);

        Assert.Equal([30f], rig.Probe.Handed);
        Assert.Equal(30, taken);
    }

    /// <summary>The opt-in is what matters: the same entity, the same hit, the flag flipped.</summary>
    [Fact]
    public void An_entity_that_does_not_run_the_rules_is_handed_the_damage_as_dealt()
    {
        var rig = new Rig(factor: 3f);

        int folded = rig.Hit(10f);
        rig.Probe.Runs = false;
        int asDealt = rig.Hit(10f);

        Assert.Equal((30, 10), (folded, asDealt));
        Assert.Equal([30f, 10f], rig.Probe.Handed);
        Assert.True(rig.Modifier.Seen.Count == 1,
            $"the modifiers are asked of the hit that runs the rules and not of the other (asked {rig.Modifier.Seen.Count})");
    }

    [Fact]
    public void The_modifiers_see_the_hit_as_dealt_with_its_hitgroup_and_its_crit()
    {
        var rig = new Rig();

        using (rig.Hits.Push(HitGroup.Head, critical: true))
            rig.Hit(10f, DamageType.Bullet, HitGroup.Head);

        DamageContext hit = Assert.Single(rig.Modifier.Seen);
        Assert.Equal(new DamageContext(Victim, Attacker, 10f, (int)DamageType.Bullet, HitGroup.Head, Critical: true), hit);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void A_fold_that_leaves_nothing_hands_the_entity_zero(float factor)
    {
        var rig = new Rig(factor);

        int taken = rig.Hit(10f);

        Assert.Equal([0f], rig.Probe.Handed);
        Assert.Equal(0, taken);
    }

    /// <summary>
    /// A container without the damage path leaves the hit as dealt and says so
    /// once: a composition that forgot the service must not read as a quiet
    /// entity.
    /// </summary>
    [Fact]
    public void Without_the_damage_path_the_hit_lands_as_dealt_and_the_gap_is_named_once()
    {
        var rig = new Rig(factor: 3f, withService: false);
        var warnings = new List<string>();
        rig.Registry.Lines.Tap = (text, isWarning) => { if (isWarning) warnings.Add(text); };

        int first = rig.Hit(10f);
        int second = rig.Hit(10f);

        Assert.Equal((10, 10), (first, second));
        Assert.True(warnings.Count == 1
                    && warnings[0].Contains(nameof(ScriptedDamage), StringComparison.Ordinal)
                    && warnings[0].Contains(nameof(DamageRulesProbeEntity), StringComparison.Ordinal),
            "one warning naming the path and the class: " + string.Join(" | ", warnings));
    }

    /// <summary>
    /// The composition's shape (<c>ServerModule</c>: <c>_services.Add&lt;ScriptedDamage&gt;()</c>
    /// beside the rules, the hitgroups and the two chains): the registry asks
    /// the container by type on the first such hit, and the container builds
    /// the path from what it holds.
    /// </summary>
    [Fact]
    public void A_container_that_registers_the_damage_path_by_type_builds_it_on_the_first_hit()
    {
        var rig = new Rig(factor: 3f, withService: false);
        var services = Services(out var engine);
        engine.Returns("Entities.GetHealth", 100);
        services.AddInstance<IGameRules>(rig.Rules);
        services.AddInstance(rig.Hits);
        services.AddInstance<IHooks<IEntityHurtHook>>(new ListHooks<IEntityHurtHook>(rig.Hurt));
        services.AddInstance<IHooks<IEntityDeathHook>>(new ListHooks<IEntityDeathHook>(rig.Deaths));
        services.Add<ScriptedDamage>();
        rig.Registry.Services = services;

        int taken = rig.Hit(10f);

        Assert.Equal(30, taken);
        Assert.Equal(30, Assert.Single(rig.Hurt.Heard).Taken);
    }

    //-------------------------------------------------------------------------
    // The hurt hook.
    //-------------------------------------------------------------------------

    [Fact]
    public void A_damaging_hit_is_told_to_the_hurt_hook_once()
    {
        var rig = new Rig(factor: 3f, health: 100);

        using (rig.Hits.Push(HitGroup.Generic, critical: true))
            rig.Hit(10f, DamageType.Blast);

        EntityHurt hurt = Assert.Single(rig.Hurt.Heard);
        Assert.Equal(new EntityHurt(Victim, Attacker, Taken: 30, HealthLeft: 70, DamageType.Blast, Critical: true), hurt);
    }

    [Fact]
    public void Each_damaging_hit_is_told_once()
    {
        var rig = new Rig(factor: 1f);

        rig.Hit(10f);
        rig.Hit(20f);
        rig.Hit(5f);

        Assert.Equal([10, 20, 5], rig.Hurt.Heard.Select(h => h.Taken));
    }

    [Fact]
    public void A_hit_that_did_no_damage_is_not_told()
    {
        var rig = new Rig(factor: 3f);
        rig.Probe.Answer = 0;

        rig.Hit(10f);

        Assert.Equal([30f], rig.Probe.Handed);     // the stimulus reached the entity
        Assert.Empty(rig.Hurt.Heard);
    }

    [Fact]
    public void A_hit_past_the_health_left_reports_zero_health()
    {
        var rig = new Rig(factor: 3f, health: 20);

        rig.Hit(10f);

        Assert.Equal(0, Assert.Single(rig.Hurt.Heard).HealthLeft);
    }

    [Fact]
    public void A_hit_outside_every_scope_is_not_critical()
    {
        var rig = new Rig();

        rig.Hit(10f);

        Assert.False(Assert.Single(rig.Hurt.Heard).Critical);
    }

    //-------------------------------------------------------------------------
    // The death hook.
    //-------------------------------------------------------------------------

    [Fact]
    public void A_death_is_told_to_the_death_hook_once_with_where_it_died()
    {
        var rig = new Rig();

        bool routed = rig.Registry.Killed(Victim, Attacker);

        Assert.True(routed);
        Assert.Equal(new EntityDeath(Victim, Attacker, DiedAt), Assert.Single(rig.Deaths.Heard));
    }

    [Fact]
    public void The_death_hook_runs_after_the_entitys_own_death()
    {
        var rig = new Rig();

        rig.Registry.Killed(Victim, Attacker);

        Assert.Equal([Attacker], rig.Probe.KilledBy);
        Assert.Equal([1], rig.Deaths.OwnDeathsRunFirst);
    }

    /// <summary>The opt-in again: flipped off, neither hook hears the same hit and the same death.</summary>
    [Fact]
    public void An_entity_that_does_not_run_the_rules_tells_neither_hook()
    {
        var rig = new Rig(factor: 1f);

        rig.Hit(10f);
        rig.Registry.Killed(Victim, Attacker);
        rig.Probe.Runs = false;
        rig.Hit(10f);
        rig.Registry.Killed(Victim, Attacker);

        Assert.Single(rig.Hurt.Heard);
        Assert.Single(rig.Deaths.Heard);
        Assert.Equal(2, rig.Probe.KilledBy.Count);   // its own death ran both times
    }

    /// <summary>
    /// Native elides the death op for a class with no <c>OnKilled</c>
    /// (<see cref="EntityOpMask"/>): a class that runs the rules must still be
    /// sent it, or its death is never told. The control is the same class
    /// without the opt-in.
    /// </summary>
    [Fact]
    public void A_class_that_runs_the_rules_arms_the_death_op_without_a_death_of_its_own()
    {
        uint runs = EntityOpMask.For(typeof(DamageRulesNoDeathEntity), hasBoundInputs: false);
        uint plain = EntityOpMask.For(typeof(DamageNoRulesNoDeathEntity), hasBoundInputs: false);

        Assert.NotEqual(0u, runs & EntityOpMask.Bit(EntityOp.Killed));
        Assert.Equal(0u, plain & EntityOpMask.Bit(EntityOp.Killed));
    }

    [Fact]
    public void A_class_with_no_death_of_its_own_still_has_its_death_told()
    {
        var rig = new Rig();
        var other = new EntityHandle(0x0001_0006u);
        rig.Registry.KeyValue(other, "test_damage_rules_no_death", "targetname", "quiet");

        rig.Registry.Killed(other, Attacker);

        Assert.Equal(new EntityDeath(other, Attacker, DiedAt), Assert.Single(rig.Deaths.Heard));
    }
}

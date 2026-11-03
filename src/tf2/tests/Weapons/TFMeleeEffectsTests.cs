using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Combat;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>One hit the melee arm dealt, with the force and the contact open at the call.</summary>
internal sealed record MeleeHit(EntityHandle Victim, float Damage, DamageType Type, Vector Force, Vector Position, bool Critical);

/// <summary>The damage op, recording each hit whole.</summary>
internal sealed class MeleeDamage(DamageHitGroups groups) : IEngineDamage
{
    public List<MeleeHit> Hits { get; } = new();

    public void ApplyAt(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType, Vector force, Vector position)
        => Hits.Add(new MeleeHit(victim, damage, damageType, force, position, groups.Critical));

    public void Apply(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType)
        => ApplyAt(victim, attacker, damage, damageType, default, default);
    public void SetForceServerRagdoll(EntityHandle entity, bool force) { }
    public TakeDamageMode SetTakeDamageMode(EntityHandle entity, TakeDamageMode mode) => mode;
    public void RegisterRules(DamageRuleMasks masks) { }
    public void TakeDamage(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType, Vector? direction = null,
                           Vector forceOrigin = default, bool physicsOnly = false)
        => ApplyAt(entity, attacker, damage, damageType, default, default);
    public int TakeDamageAnswered(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType, Vector? direction = null,
                                  Vector forceOrigin = default, bool physicsOnly = false)
    {
        ApplyAt(entity, attacker, damage, damageType, default, default);
        return damage > 0 ? 1 : 0;
    }
}

/// <summary>
/// One swinger (entity 3, at the origin's eye, facing +X) over the recording
/// engine: what the swing's line and hull name is a fact's to decide.
/// </summary>
internal sealed class MeleeRig
{
    public static readonly EntityHandle Owner = new(0x0001_0003u);
    public static readonly EntityHandle Victim = new(0x0001_0004u);
    public static readonly EntityHandle Wall = new(0x0002_0009u);

    public RecordingEngine Engine { get; } = RecordingEngine.Default();
    public RecordingLog Log { get; } = new();
    public RecordingArms AttackArms { get; } = new();
    public DamageHitGroups HitGroups { get; } = new();
    public MeleeDamage Damage { get; }
    public TFMeleeEffects Effects { get; }

    /// <summary>What each entity answers where, for the behind-crit's origins.</summary>
    public readonly Dictionary<EntityHandle, Vector> Origins = new();

    /// <summary>What each entity answers facing, for the behind-crit and the swing.</summary>
    public readonly Dictionary<EntityHandle, Vector> EyeAngles = new();

    /// <summary>What the swing's line names; a miss unless a fact says otherwise.</summary>
    public TraceResult Line { get; set; } = new(false, 1f, default, default, EntityHandle.None);

    /// <summary>What the swing's hull names; a miss unless a fact says otherwise.</summary>
    public SurfaceTraceResult Hull { get; set; } = new(false, 1f, default, default, EntityHandle.None, -1, 0);

    /// <summary>The contact the hull answers, when it hits.</summary>
    public Vector HullEnd { get; set; } = new(44f, 3f, 62f);

    public MeleeRig()
    {
        Engine.Returns("Players.GetHandle", Owner);
        Engine.Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f));
        Engine.ReturnsEachCall("Entities.GetEyeAngles",
                               () => Engine.Calls[^1].Args[0] is EntityHandle h && EyeAngles.TryGetValue(h, out var a) ? a : default(Vector));
        Engine.ReturnsEachCall("Entities.GetOrigin",
                               () => Engine.Calls[^1].Args[0] is EntityHandle h && Origins.TryGetValue(h, out var o) ? o : default(Vector));
        Engine.Returns("Entities.IsValid", true);
        Engine.ReturnsEachCall("WorldQuery.TraceLine", () => Line);
        Engine.ReturnsEachCall("WorldQuery.TraceBox", () => Hull.Hit ? Hull with { End = HullEnd } : Hull);

        Damage = new MeleeDamage(HitGroups);
        Effects = new TFMeleeEffects(Engine, Engine, Engine, Damage, HitGroups, Log);
    }

    /// <summary>What the engine says the hit target is.</summary>
    public bool TargetIsPlayer
    {
        set => Engine.Returns("Entities.IsPlayer", value);
    }

    public void Melee(ShotDamage? shot = null, float? range = null, float? hull = null, bool behind = false) =>
        Effects.Melee(Attack(AttackArms), WeaponFixtures.Parse("tf_weapon_bat"), shot ?? new ShotDamage(35f),
                      new SwingTrace(range ?? TFWeaponTypes.SwingRange, hull ?? TFMeleeBehaviour.SwingHull, behind));

    /// <summary>The melee attack: the bat's row, its owner, the live run.</summary>
    public static WeaponAttackContext Attack(IWeaponArms arms) => new(
        new WeaponType { Name = "tf_weapon_bat", MeleeRange = TFWeaponTypes.SwingRange, MeleeDamage = 35f },
        default!, default!, 1f, true, arms, new WeaponCommand(1, 5, 3));

    internal static SurfaceTraceResult HullOn(EntityHandle target) =>
        new(true, 0.9f, default, default, target, 0, 0);
}

/// <summary>
/// The swing's trace (<c>CTFWeaponBaseMelee::DoSwingTraceInternal</c>,
/// <c>shared/tf/tf_weaponbase_melee.cpp:392-540</c>): the line first, then
/// the 18-unit hull.
/// </summary>
public sealed class TFMeleeSwingTraceTests
{
    [Fact]
    public void A_target_off_the_line_but_inside_the_hull_is_hit()
    {
        var rig = new MeleeRig { Hull = new SurfaceTraceResult(true, 0.9f, default, default, MeleeRig.Victim, 0, 0),
                                 TargetIsPlayer = false };

        rig.Melee();

        var hit = Assert.Single(rig.Damage.Hits);
        Assert.Equal(MeleeRig.Victim, hit.Victim);
        Assert.Equal(new Vector(44f, 3f, 62f), hit.Position);
    }

    [Fact]
    public void A_target_outside_the_hull_is_missed()
    {
        var rig = new MeleeRig();

        rig.Melee();

        Assert.Empty(rig.Damage.Hits);
    }

    [Fact]
    public void A_line_hit_is_preferred_over_the_hull()
    {
        var rig = new MeleeRig
        {
            Line = new TraceResult(true, 0.75f, new Vector(30f, 1f, 64f), default, MeleeRig.Wall),
            Hull = new SurfaceTraceResult(true, 0.9f, default, default, MeleeRig.Victim, 0, 0),
            TargetIsPlayer = false,
        };

        rig.Melee();

        var hit = Assert.Single(rig.Damage.Hits);
        Assert.Equal(MeleeRig.Wall, hit.Victim);
        Assert.Equal(new Vector(30f, 1f, 64f), hit.Position);
        Assert.Equal(0, rig.Engine.CountOf("WorldQuery.TraceBox"));
    }

    [Fact]
    public void The_swing_asks_the_eye_along_the_eye_angles_over_the_reach()
    {
        var rig = new MeleeRig();

        rig.Melee();

        var line = rig.Engine.Calls.First(c => c.Member == "WorldQuery.TraceLine");
        Assert.Equal(new Vector(0f, 0f, 64f), line.Args[0]);
        Assert.Equal(new Vector(TFWeaponTypes.SwingRange, 0f, 64f), line.Args[1]);
    }

    [Fact]
    public void The_swing_hull_is_eighteen_units_to_each_side()
    {
        var rig = new MeleeRig();

        rig.Melee();

        var hull = rig.Engine.Calls.First(c => c.Member == "WorldQuery.TraceBox");
        Assert.Equal(new Vector(-TFMeleeBehaviour.SwingHull, -TFMeleeBehaviour.SwingHull, -TFMeleeBehaviour.SwingHull), hull.Args[2]);
        Assert.Equal(new Vector(TFMeleeBehaviour.SwingHull, TFMeleeBehaviour.SwingHull, TFMeleeBehaviour.SwingHull), hull.Args[3]);
    }

    [Fact]
    public void A_raised_reach_swings_the_line_to_one_and_a_half_times_the_plain_range()
    {
        var plain = new MeleeRig();
        plain.Melee();
        var raised = new MeleeRig();

        raised.Melee(range: 1.5f * TFWeaponTypes.SwingRange);

        float plainEnd = ((Vector)plain.Engine.Calls.First(c => c.Member == "WorldQuery.TraceLine").Args[1]!).X;
        float raisedEnd = ((Vector)raised.Engine.Calls.First(c => c.Member == "WorldQuery.TraceLine").Args[1]!).X;
        Assert.Equal(1.5f * plainEnd, raisedEnd, 4);
    }

    [Fact]
    public void A_raised_hull_widens_the_swing_box_to_twice_the_plain_bounds()
    {
        var plain = new MeleeRig();
        plain.Melee();
        var wide = new MeleeRig
        {
            Hull = MeleeRig.HullOn(MeleeRig.Victim),
            TargetIsPlayer = false,
        };

        wide.Melee(hull: 2f * TFMeleeBehaviour.SwingHull);

        var box = wide.Engine.Calls.First(c => c.Member == "WorldQuery.TraceBox");
        Assert.Equal(2f * -TFMeleeBehaviour.SwingHull, ((Vector)box.Args[2]!).X, 4);
        Assert.Equal(2f * TFMeleeBehaviour.SwingHull, ((Vector)box.Args[3]!).X, 4);
    }

    [Fact]
    public void A_hit_on_a_player_plays_the_scripts_hit_flesh_sound()
    {
        var rig = new MeleeRig { Hull = MeleeRig.HullOn(MeleeRig.Victim), TargetIsPlayer = true };

        rig.Melee();

        Assert.Equal(new[] { "Weapon_Bat.HitFlesh" }, rig.AttackArms.Sounds);
    }

    [Fact]
    public void A_hit_on_the_world_plays_the_hit_world_sound()
    {
        var rig = new MeleeRig { Hull = MeleeRig.HullOn(MeleeRig.Wall), TargetIsPlayer = false };

        rig.Melee();

        Assert.Equal(new[] { "Weapon_Bat.HitWorld" }, rig.AttackArms.Sounds);
    }

    [Fact]
    public void A_hit_the_world_stops_on_is_applied_even_without_an_entity_behind_it()
    {
        // Stock's OnSwingHit (:553-694): the sound plays first, then
        // DoMeleeDamage runs whatever the trace stopped on — a brush with no
        // entity behind it is still a hit, its force and contact applied.
        var rig = new MeleeRig
        {
            Line = new TraceResult(true, 0.6f, new Vector(0f, 28.8f, 64f), default, EntityHandle.None),
            TargetIsPlayer = false,
        };

        rig.Melee();

        Assert.Equal(new[] { "Weapon_Bat.HitWorld" }, rig.AttackArms.Sounds);
        var hit = Assert.Single(rig.Damage.Hits);
        Assert.Equal(EntityHandle.None, hit.Victim);
        Assert.Equal(new Vector(0f, 28.8f, 64f), hit.Position);
    }

    [Fact]
    public void A_swing_that_meets_nothing_plays_no_hit_sound()
    {
        var rig = new MeleeRig();

        rig.Melee();

        Assert.Empty(rig.AttackArms.Sounds);
    }

    [Fact]
    public void A_hit_carries_the_melee_damage_type()
    {
        var rig = new MeleeRig { Hull = MeleeRig.HullOn(MeleeRig.Victim), TargetIsPlayer = false };

        rig.Melee();

        Assert.Equal(TFDamageBits.Melee | DamageType.NeverGib | DamageType.Club,
                     Assert.Single(rig.Damage.Hits).Type);
    }

    [Fact]
    public void A_hit_pushes_along_the_swing_at_the_force_scale_over_the_damage()
    {
        // Stock's scale is 1/flDamage * 80 (tf_weaponbase_melee.cpp:869), so
        // the damage cancels: a 35-damage swing and a 7-damage one push alike.
        var heavy = new MeleeRig { Hull = MeleeRig.HullOn(MeleeRig.Victim), TargetIsPlayer = false };
        heavy.Melee(new ShotDamage(35f));
        var light = new MeleeRig { Hull = MeleeRig.HullOn(MeleeRig.Victim), TargetIsPlayer = false };
        light.Melee(new ShotDamage(7f));

        var heavyForce = Assert.Single(heavy.Damage.Hits).Force;
        var lightForce = Assert.Single(light.Damage.Hits).Force;
        Assert.Equal(heavyForce.X, lightForce.X, 2);
        Assert.Equal(heavyForce.Y, lightForce.Y, 2);
        Assert.Equal(heavyForce.Z, lightForce.Z, 2);

        // Along the swing (+X, the eye angles' forward) and a real push.
        Assert.True(heavyForce.X > 1f);
        Assert.True(MathF.Abs(heavyForce.Y) < 0.01f);
        Assert.True(MathF.Abs(heavyForce.Z) < 0.01f);
    }

    [Fact]
    public void A_hit_of_under_a_point_pushes_nothing()
    {
        var rig = new MeleeRig { Hull = MeleeRig.HullOn(MeleeRig.Victim), TargetIsPlayer = false };

        rig.Melee(new ShotDamage(0.5f));

        Assert.Equal(default, Assert.Single(rig.Damage.Hits).Force);
    }
}

/// <summary>
/// <c>crit_from_behind</c> (<c>CTFWeaponBaseMelee</c>'s hit,
/// <c>tf_weaponbase_melee.cpp:819-833</c>): the victim facing along the way
/// the swing came — the unit from the attacker to them against their eye
/// forward, over 0.7071 — crits the hit, when the swing asked the arm.
/// </summary>
public sealed class TFMeleeCritFromBehindTests
{
    /// <summary>The victim 60 units off along +X, the swing's own line, facing <paramref name="yaw"/>.</summary>
    private static MeleeRig VictimAt(float yaw, bool behind)
    {
        var rig = new MeleeRig
        {
            Hull = MeleeRig.HullOn(MeleeRig.Victim),
            TargetIsPlayer = false,
        };
        rig.EyeAngles[MeleeRig.Owner] = new Vector(0f, 0f, 0f);
        rig.EyeAngles[MeleeRig.Victim] = new Vector(0f, yaw, 0f);
        rig.Origins[MeleeRig.Owner] = new Vector(0f, 0f, 0f);
        rig.Origins[MeleeRig.Victim] = new Vector(60f, 0f, 0f);

        rig.Melee(behind: behind);
        return rig;
    }

    [Fact]
    public void A_hit_on_a_victim_facing_away_crits_when_the_arm_is_asked()
    {
        // Facing +X, the way we came: the dot is 1, past 0.7071. The shot
        // itself rolled no crit, so the flag is the arm's alone.
        var rig = VictimAt(yaw: 0f, behind: true);

        Assert.True(Assert.Single(rig.Damage.Hits).Critical);
    }

    [Fact]
    public void A_hit_on_a_victim_facing_us_is_no_crit()
    {
        // Facing -X, back towards us: the dot is -1.
        var rig = VictimAt(yaw: 180f, behind: true);

        Assert.False(Assert.Single(rig.Damage.Hits).Critical);
    }

    [Fact]
    public void A_hit_from_behind_is_no_crit_without_the_arm()
    {
        var rig = VictimAt(yaw: 0f, behind: false);

        Assert.False(Assert.Single(rig.Damage.Hits).Critical);
    }
}

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;

using TF2.Tests.Projectiles;

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

    /// <summary>Who holds a player slot; every other slot answers the owner.</summary>
    public readonly Dictionary<int, EntityHandle> Slots = new();

    /// <summary>Each player's team; red unless a fact says otherwise.</summary>
    public readonly Dictionary<EntityHandle, int> Teams = new();

    /// <summary>Where each player's middle stands, for the swing's loose reach.</summary>
    public readonly Dictionary<EntityHandle, Vector> Centres = new();

    /// <summary>Who has died since the swing started.</summary>
    public readonly HashSet<EntityHandle> Dead = new();

    /// <summary>What the swing's line names; a miss unless a fact says otherwise.</summary>
    public TraceResult Line { get; set; } = new(false, 1f, default, default, EntityHandle.None);

    /// <summary>What the swing's hull names; a miss unless a fact says otherwise.</summary>
    public SurfaceTraceResult Hull { get; set; } = new(false, 1f, default, default, EntityHandle.None, -1, 0);

    /// <summary>The contact the hull answers, when it hits.</summary>
    public Vector HullEnd { get; set; } = new(44f, 3f, 62f);

    /// <summary>The held items the tickle's defindex pair reads (slots 3 and 7).</summary>
    public FakeOwners Owners { get; } = new();

    /// <summary>The conditions the laugh arms add, recorded whole.</summary>
    public FakeConditions Conditions { get; } = new();

    public MeleeRig(ITFWeaponOwners? owners = null, AttributeHooks? hooks = null, ITFPlayerConditions? conditions = null,
                    ITFPlayerResupply? health = null)
    {
        Engine.ReturnsEachCall("Players.GetHandle",
                               () => Engine.Calls[^1].Args[0] is int slot && Slots.TryGetValue(slot, out var h) ? h : Owner);
        Engine.ReturnsEachCall("Players.GetTeam",
                               () => Engine.Calls[^1].Args[0] is EntityHandle h && Teams.TryGetValue(h, out var t) ? t : (int)TFTeam.Red);
        Engine.ReturnsEachCall("Players.IsAlive",
                               () => Engine.Calls[^1].Args[0] is EntityHandle h && !Dead.Contains(h));
        Engine.ReturnsEachCall("Entities.GetWorldSpaceCenter",
                               () => Engine.Calls[^1].Args[0] is EntityHandle h && Centres.TryGetValue(h, out var c) ? c : default);
        Engine.Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f));
        Engine.ReturnsEachCall("Entities.GetEyeAngles",
                               () => Engine.Calls[^1].Args[0] is EntityHandle h && EyeAngles.TryGetValue(h, out var a) ? a : default(Vector));
        Engine.ReturnsEachCall("Entities.GetOrigin",
                               () => Engine.Calls[^1].Args[0] is EntityHandle h && Origins.TryGetValue(h, out var o) ? o : default(Vector));
        Engine.Returns("Entities.IsValid", true);
        Engine.Returns("Entities.GetHealth", 100);
        Engine.ReturnsEachCall("Entities.GetIndex",
                               () => Engine.Calls[^1].Args[0] is EntityHandle h && h == Victim ? 7 : 3);
        Engine.ReturnsEachCall("WorldQuery.TraceLine", () => Line);
        Engine.ReturnsEachCall("WorldQuery.TraceBox", () => Hull.Hit ? Hull with { End = HullEnd } : Hull);

        Damage = new MeleeDamage(HitGroups);
        Effects = MeleeDeps.Effects(Engine, Damage, HitGroups, Log, owners: owners ?? Owners, hooks: hooks,
                                    conditions: conditions ?? Conditions, health: health);
    }

    /// <summary>What the engine says the hit target is.</summary>
    public bool TargetIsPlayer
    {
        set => Engine.Returns("Entities.IsPlayer", value);
    }

    /// <summary>A blue enemy in slot 7, its middle this far along the swing from the eye.</summary>
    public void EnemyAt(float along)
    {
        Slots[7] = Victim;
        Teams[Victim] = (int)TFTeam.Blue;
        Centres[Victim] = new Vector(along, 0f, 64f);
    }

    public void SwingStarted(float range = TFWeaponTypes.SwingRange) => Effects.MeleeSwingStarted(Attack(AttackArms), range);

    public void Melee(ShotDamage? shot = null, float? range = null, float? hull = null, bool behind = false, bool self = false,
                      bool laugh = false, bool tickle = false) =>
        Effects.Melee(Attack(AttackArms), WeaponFixtures.Parse("tf_weapon_bat"), shot ?? new ShotDamage(35f),
                      new SwingTrace(range ?? TFWeaponTypes.SwingRange, hull ?? TFMeleeBehaviour.SwingHull, behind, self,
                                     laugh, tickle));

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

    /// <summary>
    /// The Boston Basher's miss tax (tf_weaponbase_melee.cpp:755-763, :802-835):
    /// a swing that met nothing hurts its own owner for half the melee blow —
    /// seventeen and a half of thirty-five — with the melee bits and no crit.
    /// </summary>
    [Fact]
    public void A_clean_miss_with_the_self_hit_attribute_hurts_the_owner()
    {
        var rig = new MeleeRig();

        rig.Melee(self: true);

        var hurt = Assert.Single(rig.Damage.Hits);
        Assert.Equal(MeleeRig.Owner, hurt.Victim);            // stock's DoMeleeDamage: the owner hurts themselves
        Assert.Equal(17.5f, hurt.Damage, 4);
        Assert.Equal(TFDamageBits.Melee | DamageType.NeverGib | DamageType.Club, hurt.Type);
        Assert.False(hurt.Critical);
    }

    /// <summary>A critical swing's miss tax is critical too (:855-858 runs whatever the target).</summary>
    [Fact]
    public void A_critical_clean_miss_crits_its_owner()
    {
        var rig = new MeleeRig();

        rig.Melee(new ShotDamage(35f) with { Critical = true }, self: true);

        Assert.True(Assert.Single(rig.Damage.Hits).Critical);
    }

    /// <summary>
    /// An enemy inside the swing's loose reach at its start and still alive at the
    /// smack makes this no clean miss (:745-751): swinging at a dodger costs nothing.
    /// </summary>
    [Fact]
    public void A_miss_at_a_living_enemy_in_reach_hurts_nobody()
    {
        var rig = new MeleeRig();
        rig.EnemyAt(TFWeaponTypes.SwingRange);

        rig.SwingStarted();
        rig.Melee(self: true);

        Assert.Empty(rig.Damage.Hits);
    }

    /// <summary>The enemy noted at the swing's start has died by the smack: nobody spares the owner.</summary>
    [Fact]
    public void A_miss_at_an_enemy_killed_during_the_swing_hurts_the_owner()
    {
        var rig = new MeleeRig();
        rig.EnemyAt(TFWeaponTypes.SwingRange);

        rig.SwingStarted();
        rig.Dead.Add(MeleeRig.Victim);
        rig.Melee(self: true);

        Assert.Equal(MeleeRig.Owner, Assert.Single(rig.Damage.Hits).Victim);
    }

    /// <summary>Past the loose reach (:309, 1.2 of the swing's) an enemy is nobody's reason to spare the owner.</summary>
    [Fact]
    public void A_miss_with_the_enemy_past_the_loose_reach_hurts_the_owner()
    {
        var rig = new MeleeRig();
        rig.EnemyAt(TFWeaponTypes.SwingRange * TFMeleeEffects.LooseSwingScale + 1f);

        rig.SwingStarted();
        rig.Melee(self: true);

        Assert.Equal(MeleeRig.Owner, Assert.Single(rig.Damage.Hits).Victim);
    }

    /// <summary>A teammate standing close does not count (:305's enemy team only).</summary>
    [Fact]
    public void A_miss_beside_a_teammate_hurts_the_owner()
    {
        var rig = new MeleeRig();
        rig.EnemyAt(TFWeaponTypes.SwingRange);
        rig.Teams[MeleeRig.Victim] = (int)TFTeam.Red;

        rig.SwingStarted();
        rig.Melee(self: true);

        Assert.Equal(MeleeRig.Owner, Assert.Single(rig.Damage.Hits).Victim);
    }

    /// <summary>
    /// The plain stock miss: the same nothing-hit swing with the attribute off
    /// hurts nobody — the line, then the hull, then silence.
    /// </summary>
    [Fact]
    public void A_clean_miss_without_the_attribute_hurts_nobody()
    {
        var rig = new MeleeRig();

        rig.Melee();

        Assert.Empty(rig.Damage.Hits);
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

/// <summary>
/// The honourbound duel (<c>CTFKatana::GetMeleeDamage</c>,
/// tf_weapon_sword.cpp:548-571): the katana against a victim whose own held
/// weapon is honourbound floors the blow at thrice the victim's health and
/// marks it DMG_DONT_COUNT_DAMAGE_TOWARDS_CRIT_RATE.
/// </summary>
public sealed class TFKatanaDuelTests
{
    /// <summary>An item wearing the honourbound mark.</summary>
    private static IAttributeProvider HonourBound()
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(513, "attr honorbound", TFSwordHook.HonorBoundAttrib,
                                    AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                    false, false, AttributeValueType.Default, MergedKeys.Block("x")), 1f));
        return container;
    }

    /// <summary>An item wearing nothing.</summary>
    private static IAttributeProvider Plain() => new AttributeContainer(null, AttributeProviderType.Generic);

    /// <summary>A katana duellist: the swinger (slot 3, the attack's owner) honourbound
    /// when <paramref name="attacker"/>, the victim (slot 4) holding an
    /// honourbound weapon when <paramref name="victim"/>.</summary>
    private static MeleeRig Duel(bool attacker = true, bool victim = true)
    {
        var owners = new FakeOwners();
        owners.ByName[3] = new() { [TFWeaponTypes.Katana] = attacker ? HonourBound() : Plain() };
        owners.ByName[4] = new() { [TFWeaponTypes.Katana] = HonourBound() };
        owners.Held[4] = victim ? TFWeaponTypes.Katana : "tf_weapon_bat";
        var rig = new MeleeRig(owners, new AttributeHooks())
        {
            Hull = new SurfaceTraceResult(true, 0.9f, default, default, MeleeRig.Victim, 0, 0),
            TargetIsPlayer = true,
        };
        rig.EnemyAt(40);
        // The duelling victim's health, the blow's floor's base.
        rig.Engine.Returns("Entities.GetHealth", 30);
        return rig;
    }

    private static MeleeHit Swing(MeleeRig rig, float damage = 35f)
    {
        rig.Effects.Melee(MeleeRig.Attack(rig.AttackArms), WeaponFixtures.ParseExtra(TFWeaponTypes.Katana),
                          new ShotDamage(damage),
                          new SwingTrace(TFWeaponTypes.SwingRange, TFMeleeBehaviour.SwingHull, false));
        return Assert.Single(rig.Damage.Hits);
    }

    [Fact]
    public void TheDuelStrikesForNoLessThanThriceTheVictimsHealth()
    {
        // 3 * 30 past the 35 swing: the floor is the blow.
        var hit = Swing(Duel());

        Assert.Equal(90f, hit.Damage, 4);
        Assert.True((hit.Type & TFDamageBits.DontCountCritRate) != 0);
    }

    [Fact]
    public void TheDuelHoldsWhenTheHealthFloorIsUnderTheSwing()
    {
        // 3 * 20 under the 65 the swing carries: the swing is the blow, the
        // crit-rate mark still ours.
        var rig = Duel();
        rig.Engine.Returns("Entities.GetHealth", 20);
        var hit = Swing(rig, damage: 65f);

        Assert.Equal(65f, hit.Damage, 4);
        Assert.True((hit.Type & TFDamageBits.DontCountCritRate) != 0);
    }

    [Fact]
    public void TheDuelNeedsTheVictimsHeldHonourbound()
    {
        var hit = Swing(Duel(victim: false));

        Assert.Equal(35f, hit.Damage, 4);
        Assert.True((hit.Type & TFDamageBits.DontCountCritRate) == 0);
    }

    [Fact]
    public void TheDuelNeedsTheAttackersHonourbound()
    {
        var hit = Swing(Duel(attacker: false));

        Assert.Equal(35f, hit.Damage, 4);
        Assert.True((hit.Type & TFDamageBits.DontCountCritRate) == 0);
    }

    // ---- the Gunslinger's count (CTFRobotArm::Smack) ------------------------

    /// <summary>A bound weapon state for the count the smacks keep.</summary>
    private static WeaponNetState Arm()
    {
        var w = new WeaponNetState();
        w.BindLocal();
        return w;
    }

    /// <summary>The arm's attack at <paramref name="now"/>, its weapon state the caller's.</summary>
    private static WeaponAttackContext ArmAttack(WeaponNetState weapon, float now) => new(
        new WeaponType { Name = TFWeaponTypes.WrenchArm, MeleeRange = TFWeaponTypes.SwingRange, MeleeDamage = 65f },
        weapon, default!, now, true, new RecordingArms(), new WeaponCommand(1, 5, 3));

    private static readonly SwingTrace ArmSmackTrace =
        new(TFWeaponTypes.SwingRange, TFMeleeBehaviour.SwingHull, false);

    /// <summary>The arm's smack against a staged hull, the victim player or not.</summary>
    private MeleeRig ArmRig(bool player, bool enemy = true)
    {
        var rig = new MeleeRig { Hull = MeleeRig.HullOn(MeleeRig.Victim), TargetIsPlayer = player };
        rig.EnemyAt(45f);
        if (!enemy)
            rig.Teams[MeleeRig.Victim] = (int)TFTeam.Red;
        return rig;
    }

    private void Smack(MeleeRig rig, WeaponNetState weapon, float now) =>
        rig.Effects.Melee(ArmAttack(weapon, now), WeaponFixtures.ParseExtra(TFWeaponTypes.WrenchArm),
                          new ShotDamage(65f), ArmSmackTrace);

    [Fact]
    public void A_gunslinger_whiff_ends_the_combo()
    {
        var w = Arm();
        var rig = ArmRig(player: true);
        Smack(rig, w, now: 10f);
        Assert.Equal(1, TFMeleeBehaviour.ComboOf(w));

        rig.Hull = new(false, 1f, default, default, EntityHandle.None, -1, 0);
        Smack(rig, w, now: 10.5f);

        Assert.Equal(0, TFMeleeBehaviour.ComboOf(w));
    }

    [Fact]
    public void A_gunslinger_smack_into_an_enemy_counts_toward_the_third()
    {
        var w = Arm();
        var rig = ArmRig(player: true);

        Smack(rig, w, now: 10f);
        Assert.Equal(1, TFMeleeBehaviour.ComboOf(w));
        Assert.Equal(10f, w.BehaviourTimeB, 4);   // the clock the grant's timeout reads

        Smack(rig, w, now: 10.5f);
        Assert.Equal(2, TFMeleeBehaviour.ComboOf(w));

        // The third hit is the big one and folds back to none
        // (tf_weapon_wrench.cpp:508-513), its swing already gone.
        Smack(rig, w, now: 11f);
        Assert.Equal(0, TFMeleeBehaviour.ComboOf(w));
    }

    [Fact]
    public void A_smack_into_the_world_or_a_teammate_resets_the_count()
    {
        var w = Arm();
        w.BehaviourMode = 2 << TFMeleeBehaviour.ComboCountShift;

        var world = ArmRig(player: false);
        Smack(world, w, now: 10f);
        Assert.Equal(0, TFMeleeBehaviour.ComboOf(w));
        Assert.Equal(0f, w.BehaviourTimeB, 4);    // no count, no clock

        var mate = ArmRig(player: true, enemy: false);
        w.BehaviourMode = 2 << TFMeleeBehaviour.ComboCountShift;
        Smack(mate, w, now: 10f);
        Assert.Equal(0, TFMeleeBehaviour.ComboOf(w));
    }
}

/// <summary>
/// The Holiday Punch's two laugh arms after the hit (stock's non-truce block,
/// <c>tf_weaponbase_melee.cpp:882-927</c>): the crit's forced laugh and the
/// tickle by equal active-item defindexes, each only over a living enemy player.
/// </summary>
public sealed class TFMeleeHolidayPunchTests
{
    /// <summary>The fist pair the tickle compares: the schema's 15 and 23.</summary>
    private const string Schema = """
        "items_game"
        {
            "qualities"
            {
                "unique"
                {
                    "value"	"6"
                }
            }
            "attributes"
            {
            }
            "items"
            {
                "15"
                {
                    "name"	"holidays"
                    "item_class"	"tf_weapon_fists"
                    "item_quality"	"unique"
                    "used_by_classes"
                    {
                        "heavy"	"1"
                    }
                }
                "23"
                {
                    "name"	"matching"
                    "item_class"	"tf_weapon_fists"
                    "item_quality"	"unique"
                    "used_by_classes"
                    {
                        "heavy"	"1"
                    }
                }
            }
        }
        """;

    private static AttributeContainer Item(int defindex) =>
        new(SchemaParser.Parse(Schema).Items[defindex]);

    /// <summary>The swing's hull stop on the enemy player in slot 7, live.</summary>
    private static MeleeRig EnemyRig()
    {
        var rig = new MeleeRig { Hull = MeleeRig.HullOn(MeleeRig.Victim), TargetIsPlayer = true };
        rig.EnemyAt(44f);
        return rig;
    }

    [Fact]
    public void A_laughing_crit_makes_the_enemy_victim_laugh()
    {
        // crit_forces_victim_to_laugh (:884-901): the held item's one with the
        // hit a crit: stock's Taunt answers the laughing condition, over the
        // side's fallback scene window.
        var rig = EnemyRig();

        rig.Melee(new ShotDamage(35f, true), laugh: true);

        var added = Assert.Single(rig.Conditions.Added);
        Assert.Equal((MeleeRig.Victim, TFCond.Taunting), (added.Player, added.Cond));
        Assert.Equal(3f, added.Duration, 4);
    }

    [Fact]
    public void A_laughing_hit_on_a_same_team_victim_leaves_them_alone()
    {
        // The team gate (:890): same team, no laugh — the rig's default pair
        // are both red.
        var rig = new MeleeRig { Hull = MeleeRig.HullOn(MeleeRig.Victim), TargetIsPlayer = true };

        rig.Melee(new ShotDamage(35f, true), laugh: true);

        Assert.Empty(rig.Conditions.Added);
    }

    [Fact]
    public void The_laugh_needs_the_hits_crit_bit()
    {
        // The arm is the crit's (:886): a plain blow with the attribute set
        // leaves the victim's face alone.
        var rig = EnemyRig();

        rig.Melee(new ShotDamage(35f), laugh: true);

        Assert.Empty(rig.Conditions.Added);
    }

    [Fact]
    public void A_plain_tickle_on_the_same_item_makes_the_enemy_victim_laugh()
    {
        // tickle_enemies_wielding_same_weapon (:903-927): both players' active
        // items, equal defindexes, the blow no crit.
        var rig = EnemyRig();
        rig.Owners.Held[3] = "tf_weapon_bat";
        rig.Owners.Held[7] = "tf_weapon_bat";
        rig.Owners.Items[3] = Item(15);
        rig.Owners.Items[7] = Item(15);

        rig.Melee(new ShotDamage(35f), tickle: true);

        var added = Assert.Single(rig.Conditions.Added);
        Assert.Equal((MeleeRig.Victim, TFCond.Taunting), (added.Player, added.Cond));
        Assert.Equal(3f, added.Duration, 4);
    }

    [Fact]
    public void A_plain_tickle_on_different_items_leaves_them_alone()
    {
        // The match is of defindexes (:919): 15 against 23 answers no laugh.
        var rig = EnemyRig();
        rig.Owners.Held[3] = "tf_weapon_bat";
        rig.Owners.Held[7] = "tf_weapon_bat";
        rig.Owners.Items[3] = Item(15);
        rig.Owners.Items[7] = Item(23);

        rig.Melee(new ShotDamage(35f), tickle: true);

        Assert.Empty(rig.Conditions.Added);
    }
}

/// <summary>Records what the swing hands to the conditions seam.</summary>
internal sealed class BuffRecord : ITFPlayerConditions
{
    public List<(EntityHandle Who, TFCond Cond, float Seconds)> Adds { get; } = [];

    public TFConditions? Of(int playerIndex) => null;

    public TFCondAdd Add(EntityHandle player, TFCond cond, float duration = TFConditions.Permanent,
                         EntityHandle provider = default)
    {
        Adds.Add((player, cond, duration));
        return default;
    }

    public bool Remove(EntityHandle player, TFCond cond, bool ignoreDuration = false) => false;

    public TFCondMask RemoveAll(EntityHandle player) => default;

    public bool InCond(EntityHandle player, TFCond cond) => false;
}

/// <summary>
/// The disciplinary speed boost (<c>tf_weaponbase_melee.cpp:646-656</c>): a
/// melee hit landing on a same-team player stamps both with the boost, the
/// one hit for two seconds and the swinger 3.6.
/// </summary>
public sealed class TFMeleeSpeedBuffTests
{
    /// <summary>The bat's item holding one additive boost count.</summary>
    private static AttributeContainer BuffItem()
    {
        var item = new AttributeContainer(null, AttributeProviderType.Generic);
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(5401, "attr 5401", "speed_buff_ally",
            AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), 2f));
        return item;
    }

    [Fact]
    public void AHittingTheSameTeamBuffsWithTheirOwnSeconds()
    {
        var owners = new FakeOwners();
        owners.ByName[3] = new System.Collections.Generic.Dictionary<string, IAttributeProvider>
        {
            ["tf_weapon_bat"] = BuffItem(),
        };
        var conds = new BuffRecord();
        var rig = new MeleeRig(owners, new AttributeHooks(), conds)
        {
            Hull = new SurfaceTraceResult(true, 0.9f, default, default, MeleeRig.Victim, 0, 0),
            TargetIsPlayer = true,
        };

        rig.Melee();

        Assert.Equal(2, conds.Adds.Count);
        Assert.Equal(TFCond.SpeedBoost, conds.Adds[0].Cond);          // :652 — the one hit
        Assert.Equal(MeleeRig.Victim, conds.Adds[0].Who);
        Assert.Equal(2f, conds.Adds[0].Seconds, 4);
        Assert.Equal(TFCond.SpeedBoost, conds.Adds[1].Cond);          // :653 — the swinger keeps up
        Assert.Equal(MeleeRig.Owner, conds.Adds[1].Who);
        Assert.Equal(3.6f, conds.Adds[1].Seconds, 4);
    }

    [Fact]
    public void AHittingAnEnemyOrTheWorldStampsNobody()
    {
        var owners = new FakeOwners();
        owners.ByName[3] = new System.Collections.Generic.Dictionary<string, IAttributeProvider>
        {
            ["tf_weapon_bat"] = BuffItem(),
        };
        var conds = new BuffRecord();
        var rig = new MeleeRig(owners, new AttributeHooks(), conds)
        {
            Hull = new SurfaceTraceResult(true, 0.9f, default, default, MeleeRig.Victim, 0, 0),
            TargetIsPlayer = true,
        };
        rig.Teams[MeleeRig.Victim] = (int)TFTeam.Blue;                // stock's OnSameTeam gate (:646)

        rig.Melee();

        Assert.Empty(conds.Adds);

        // The world-stop, same-team-ish and all: no entity behind it, nobody buffed.
        var bare = new BuffRecord();
        var plain = new MeleeRig(owners, new AttributeHooks(), bare)
        {
            Hull = new SurfaceTraceResult(true, 0.9f, default, default, MeleeRig.Wall, 0, 0),
            TargetIsPlayer = false,
        };

        plain.Melee();

        Assert.Empty(bare.Adds);

        // Same team, but the item answers nothing: the gate (:650) asks the
        // attribute whole, and a zero answer stamps nobody.
        var noItem = new BuffRecord();
        var plainOwners = new MeleeRig(new FakeOwners(), new AttributeHooks(), noItem)
        {
            Hull = new SurfaceTraceResult(true, 0.9f, default, default, MeleeRig.Victim, 0, 0),
            TargetIsPlayer = true,
        };

        plainOwners.Melee();

        Assert.Empty(noItem.Adds);
    }
}

/// <summary>
/// The melee swing's half-health arms (<c>CTFWeaponBaseMelee::GetMeleeDamage</c>,
/// shared/tf/tf_weaponbase_melee.cpp:1004-1015): the swinger under half their
/// maximum takes the held item's <c>mult_dmg_bonus_while_half_dead</c>, at half
/// or above its <c>mult_dmg_penalty_while_half_alive</c>, each multiplied onto
/// the swing through the held item's walk.
/// </summary>
public sealed class TFMeleeHalfHealthTests
{
    /// <summary>The supply the health gate reads: a hundred maximum unless a fact says otherwise.</summary>
    private sealed class Supply : ITFPlayerResupply
    {
        public int MaxHealthAnswer { get; init; } = 100;

        public int MaxHealth(EntityHandle player) => MaxHealthAnswer;
        public void Regenerate(EntityHandle player, bool refillHealthAndAmmo = true) { }
        public int TakeHealth(EntityHandle player, float health, bool ignoreMaxHealth = false) => 0;
        public int MaxAmmo(EntityHandle player, TFAmmo ammo) => 0;
        public int GiveAmmo(EntityHandle player, int count, TFAmmo ammo) => 0;
    }

    /// <summary>An item wearing one percentage arm.</summary>
    private static IAttributeProvider Wearing(string hook, float multiplier)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(6101, "attr " + hook, hook,
                                    AttributeDescriptionFormat.Percentage, AttributeEffectType.Neutral,
                                    false, false, AttributeValueType.Default, MergedKeys.Block("h")), multiplier));
        return container;
    }

    /// <summary>A bat swinger (slot 3) at the named health over a hundred-max, their item
    /// wearing <paramref name="hook"/>; the hull's hit records the blow.</summary>
    private static MeleeRig HalfSwing(string hook, float multiplier, int health)
    {
        var owners = new FakeOwners();
        owners.Items[3] = Wearing(hook, multiplier);
        var rig = new MeleeRig(owners, new AttributeHooks(), health: new Supply())
        {
            Hull = new SurfaceTraceResult(true, 0.9f, default, default, MeleeRig.Victim, 0, 0),
            TargetIsPlayer = true,
        };
        rig.Engine.Returns("Entities.GetHealth", health);
        return rig;
    }

    private static MeleeHit Swing(MeleeRig rig)
    {
        rig.Melee();
        return Assert.Single(rig.Damage.Hits);
    }

    [Fact]
    public void ASwingUnderHalfHealthTakesTheBonusMultiplier()
    {
        // 30 under half of a hundred: the bonus percentage rides the swing.
        var hit = Swing(HalfSwing("mult_dmg_bonus_while_half_dead", 2f, health: 30));

        Assert.Equal(70f, hit.Damage, 4);
    }

    [Fact]
    public void ASwingAtHalfHealthTakesThePenaltyMultiplier()
    {
        // Exactly half is not under half (the strict less-than at :1008): the
        // penalty percentage rides instead.
        var hit = Swing(HalfSwing("mult_dmg_penalty_while_half_alive", 0.5f, health: 50));

        Assert.Equal(17.5f, hit.Damage, 4);
    }

    [Fact]
    public void ASwingWithoutTheHealthArmsStaysWhole()
    {
        var owners = new FakeOwners();
        owners.Items[3] = new AttributeContainer(null, AttributeProviderType.Generic);
        var low = new MeleeRig(owners, new AttributeHooks(), health: new Supply())
        {
            Hull = new SurfaceTraceResult(true, 0.9f, default, default, MeleeRig.Victim, 0, 0),
            TargetIsPlayer = true,
        };
        low.Engine.Returns("Entities.GetHealth", 30);
        low.Melee();

        var high = new MeleeRig(owners, new AttributeHooks(), health: new Supply())
        {
            Hull = new SurfaceTraceResult(true, 0.9f, default, default, MeleeRig.Victim, 0, 0),
            TargetIsPlayer = true,
        };
        high.Engine.Returns("Entities.GetHealth", 80);
        high.Melee();

        Assert.Equal(35f, Assert.Single(low.Damage.Hits).Damage, 4);
        Assert.Equal(35f, Assert.Single(high.Damage.Hits).Damage, 4);
    }
}

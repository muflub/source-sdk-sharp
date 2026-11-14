using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Combat;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>
/// The healing bolt's touch, both arms of stock's <c>StrikeTarget</c> split
/// (<c>tf_projectile_arrow.cpp:478-545</c>): the teammate heal and the enemy
/// shot, the life ramp both ride, the capped take and the events, the uber
/// the wound's age scales.
/// </summary>
public sealed class TFHealingBoltFacts
{
    private const int Attack = (int)InputButtons.Attack;

    /// <summary>
    /// A bolt armed with the crossbow's shot at clock 10, its victim a player.
    /// <paramref name="victimTeam"/> is the team the victim answers; the
    /// bolt's own is the owner's, 2. <paramref name="age"/> rewinds the clock
    /// forward after the arming, the seconds since the launch the ramp reads.
    /// </summary>
    private static (ProjectileRig Rig, TFHealingBolt Bolt) Flying(int victimTeam, float age = 0f)
    {
        var rig = new ProjectileRig();
        rig.Engine.Returns("Clock.Now", 10f);
        rig.Engine.Returns("Frame.GetFrameTime", 0.1f);
        // The victim is a player; nothing else this path touches is.
        rig.Engine.ReturnsEachCall("Entities.IsPlayer", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim);
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim ? victimTeam : 2);

        var bolt = rig.Bolt();
        bolt.OnSpawn();
        bolt.ArmedBy(75f, TFWeaponId.Crossbow, "tf_weapon_crossbow", critical: false, ownerIndex: 3);
        if (age != 0f)
            rig.Engine.Returns("Clock.Now", 10f + age);
        bolt.FiredBy(ProjectileRig.Player, new Vector(TFProjectileDefs.CrossbowSpeed, 0f, 0f));
        return (rig, bolt);
    }

    [Fact]
    public void ABoltHealsATeammateByDistance()
    {
        // GetDamage's life ramp (:315-324): half strength at birth, full a
        // sixth of a second in. At 2400 units a second the sixth is 1440
        // units of flight — the close heal is half the distant one, doubled
        // into health by ImpactTeamPlayer (:1218).
        var (rig, bolt) = Flying(victimTeam: 2);
        bolt.OnTouch(ProjectileRig.Victim);

        Assert.Equal((ProjectileRig.Victim, 75), Assert.Single(rig.Health.HealthTaken));
        Assert.Equal(1, rig.Engine.CountOf("Sound.EntityEmitSound"));

        var (matureRig, mature) = Flying(victimTeam: 2, age: 0.6f);
        mature.OnTouch(ProjectileRig.Victim);

        Assert.Equal((ProjectileRig.Victim, 150), Assert.Single(matureRig.Health.HealthTaken));
    }

    [Fact]
    public void ABoltDamagesAnEnemy()
    {
        // The same touch against the other colour takes the shot, the ramped
        // value under the crossbow's own damage row.
        var (rig, bolt) = Flying(victimTeam: 3);
        bolt.OnTouch(ProjectileRig.Victim);

        var hit = rig.Engine.Last("Damage.TakeDamage");
        Assert.NotNull(hit);
        Assert.Equal(ProjectileRig.Victim, hit!.ArgAs<EntityHandle>(0));
        Assert.Equal(ProjectileRig.Player, hit.ArgAs<EntityHandle>(1)); // the shooter, not the bolt
        Assert.Equal(37.5f, hit.ArgAs<float>(2), 1f);
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.Crossbow), hit.ArgAs<DamageType>(3));
        Assert.Empty(rig.Health.HealthTaken);
    }

    [Fact]
    public void ABoltDoesNotOverhealPastStocksLimit()
    {
        // DMG_GENERIC's TakeHealth (:1230): up to the class maximum, never
        // past it — not the beam's ignore-max arm, whose flag the take is
        // asked without. The mature shot's 150 ask is what the 100 max binds.
        var (rig, bolt) = Flying(victimTeam: 2, age: 0.6f);
        rig.Health.Ceiling = 100;

        bolt.OnTouch(ProjectileRig.Victim);

        Assert.Equal((ProjectileRig.Victim, 100), Assert.Single(rig.Health.HealthTaken));
        Assert.Equal(new[] { false }, rig.Health.Unbounded);
    }

    [Fact]
    public void TheHealEventsCarryTheAskNotTheTaken()
    {
        // Both events quote flHealth, the heal as computed (:1248, :1255),
        // while the health actually given stops at the ceiling: the mature
        // shot asks 150, the ceiling takes 100.
        var (rig, bolt) = Flying(victimTeam: 2, age: 0.6f);
        rig.Health.Ceiling = 100;

        bolt.OnTouch(ProjectileRig.Victim);

        Assert.Equal((ProjectileRig.Victim, ProjectileRig.Player, 150), Assert.Single(rig.Notices.Healed));
        Assert.Equal((ProjectileRig.Victim, 150), Assert.Single(rig.Notices.HealOnHit));
    }

    [Fact]
    public void TheCrossbowHealEventNamesHealerTargetAndAmount()
    {
        // The bolt's own event (:1266-1273): healer the shooter's side, target
        // the healed one, amount the same flHealth ask the other two quote —
        // the close shot's 75.
        var (rig, bolt) = Flying(victimTeam: 2);

        bolt.OnTouch(ProjectileRig.Victim);

        Assert.Equal((ProjectileRig.Player, ProjectileRig.Victim, 75), Assert.Single(rig.Notices.Crossbow));
    }

    [Fact]
    public void TheHealBooksTheShootersHealStatBeforeTheEvents()
    {
        // Event_PlayerHealedOther on the shooter (:1237), the ask under the
        // sane-value bounds of tf_gamestats.cpp:719-727.
        var (rig, bolt) = Flying(victimTeam: 2);

        bolt.OnTouch(ProjectileRig.Victim);

        Assert.Equal((ProjectileRig.Player, TFStat.Healing, 75), Assert.Single(rig.Stats.Paid));
    }

    [Fact]
    public void AWeaponThatBlocksHealingTurnTheHealAway()
    {
        // The victim's active weapon carrying weapon_blocks_healing refuses
        // the whole heal (:1208-1216) — no health, no events, no uber.
        var (rig, bolt) = Flying(victimTeam: 2);
        rig.Owners.Item = rig.AttributeItem("weapon_blocks_healing", AttributeDescriptionFormat.Additive, 1f);
        rig.Owners.Active = rig.Owners.Item;

        bolt.OnTouch(ProjectileRig.Victim);

        Assert.Empty(rig.Health.HealthTaken);
        Assert.Empty(rig.Notices.Healed);
    }

    [Fact]
    public void TheBoltChargesTheShootersUberByTheWoundAge()
    {
        // AddCharge to the shooter's medigun (:1275-1285): healed per second
        // over twenty-four, the fresher wound the slower — three at ten
        // seconds — across the frame the touch stands in.
        var (rig, bolt) = Flying(victimTeam: 2);
        rig.Records.Note(ProjectileRig.Victim.EntryIndex,
                         new TFHitRecord(ProjectileRig.Player, 10f, DamageType.Bullet, TFCritType.None,
                                         TFDamageCustom.None, TFWeaponId.Pistol, "tf_weapon_pistol", 0f),
                         damager: false);

        bolt.OnTouch(ProjectileRig.Victim);

        // 75 healed, the wound ten seconds old: 75 / (24 * 3) of a tenth.
        Assert.Equal((3, 75f / 72f * 0.1f), Assert.Single(rig.Mediguns.Writes));
    }

    [Fact]
    public void ABoltAtANeverHitPlayerChargesAtTheFullRate()
    {
        // Stock's zero last-damage stamp: a player never hit stands outside
        // every scale, so the wound's age is huge and the rate is one.
        var (rig, bolt) = Flying(victimTeam: 2);

        bolt.OnTouch(ProjectileRig.Victim);

        Assert.Equal((3, 75f / 24f * 0.1f), Assert.Single(rig.Mediguns.Writes));
    }

    [Fact]
    public void TheBoltWearsTheOverhealCondForASecondAndATenth()
    {
        var (rig, bolt) = Flying(victimTeam: 2);

        bolt.OnTouch(ProjectileRig.Victim);

        Assert.Equal((ProjectileRig.Victim, TFCond.HealthOverhealed, 1.2f), Assert.Single(rig.Conditions.Added));
    }

    // =====================================================================
    // The launch, the row the crossbow stands the bolt up with.
    // =====================================================================

    private static TFWeaponScript Script(string name)
    {
        var files = WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses());
        return new TFWeaponScripts(new RecordingLog(), files).Get(name)
            ?? throw new InvalidOperationException($"no script {name}");
    }

    private static WeaponAttackContext Firing(int owner) => new(default!, null!, null!, 1f, true,
        null!, new WeaponCommand(1, 5, owner));

    [Fact]
    public void TheCrossbowCreatesTheHealingBoltAtTheArrowsMuzzle()
    {
        // FireArrow's vecOffset (23.5, -8, -3) (tf_weaponbase_gun.cpp:784),
        // shared with the compound bow's arrow; no ducked variant.
        var rig = new ProjectileRig();
        var bolt = rig.Bolt();
        rig.Directory.Instance = bolt;

        rig.Effects.Projectile(Firing(3), Script("tf_weapon_crossbow"), new ShotDamage(75f), 0f);

        var create = rig.Engine.Last("Entities.Create");
        Assert.NotNull(create);
        Assert.Equal(TFProjectileEntityNames.HealingBolt, create!.ArgAs<string>(0));

        var muzzle = rig.Engine.Last("Entities.SetOrigin")!.ArgAs<Vector>(1);
        Assert.Equal(TFProjectileDefs.ArrowMuzzleForward, muzzle.X, 0.5f);
        Assert.Equal(-TFProjectileDefs.ArrowMuzzleRight, muzzle.Y, 0.5f); // right is -Y at yaw 0
        Assert.Equal(64f + TFProjectileDefs.ArrowMuzzleUp, muzzle.Z, 0.5f);
    }

    [Fact]
    public void TheCrossbowSendsItsBoltAtTheChargedDrawsTopSpeed()
    {
        // RemapValClamped(0.75, 0, 1, 1800, 2600) — the launcher's speed at
        // the crossbow's full charge, 2400 (tf_weapon_rocketlauncher.cpp:751).
        var rig = new ProjectileRig();
        var bolt = rig.Bolt();
        rig.Directory.Instance = bolt;

        rig.Effects.Projectile(Firing(3), Script("tf_weapon_crossbow"), new ShotDamage(75f), 1f);

        var velocity = rig.Engine.Last("Entities.SetVelocity")!.ArgAs<Vector>(1);
        Assert.Equal(TFProjectileDefs.CrossbowSpeed, velocity.X, 0.5f);

        // Armed with the shot and the team the trail wears.
        Assert.Equal(75f, bolt.ShotDamage);
        Assert.Equal(TFWeaponId.Crossbow, bolt.Weapon);
        Assert.Equal(2, bolt.State.TeamNumber);
    }

    [Fact]
    public void TheCrossbowReloadsAfterEachShot()
    {
        var world = new TFWeaponWorld(files: WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses()));
        var (w, vm) = world.Armed("tf_weapon_crossbow", clip: 1, reserve: 5);

        world.Run(w, vm, Attack, 10f, TFWeaponWorld.GunSeed(200, 10000));

        var launch = Assert.Single(world.Effects.Projectiles);
        Assert.Equal(("tf_weapon_crossbow", new ShotDamage(75f), 0f), launch);
        Assert.Equal(0, w.Clip);

        // The spent clip reloads on its own, one bolt from the reserve
        // (TimeReloadStart 0, TimeReload 1.5): the m_bReloadsSingly clock,
        // the rocket launcher's flag the crossbow inherits with its class.
        world.Run(w, vm, 0, now: 10.25f);
        Assert.True(w.InReload);
        Assert.Equal(TFWeaponBehaviour.ModeReloadingSingly, w.BehaviourMode & TFWeaponBehaviour.ModeReloadingSingly);

        for (float t = 10.3f; t < 14f; t += 0.05f)
            world.Run(w, vm, 0, now: t);

        Assert.Equal(1, w.Clip);
        Assert.Equal(4, w.Reserve);
        Assert.False(w.InReload);
    }
}

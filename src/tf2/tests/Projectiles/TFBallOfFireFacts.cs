using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Tests.Combat;
using TF2.Tests.Weapons;

using SourceSharp.Tests;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>
/// The Dragon's Fury's ball (<c>CTFProjectile_BallOfFire</c>,
/// <c>tf_projectile_dragons_fury.cpp</c>): the flat launch at the fire's speed
/// from the muzzle offsets, the enemy shot with the ignite bit and the burn
/// it leaves, three times over on a burning victim, one hit per player, and
/// the launcher's once-per-ball refill boost behind the hurt.
/// </summary>
public sealed class TFBallOfFireFacts
{
    private static TFWeaponScript BallScript()
    {
        var files = WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses());
        return new TFWeaponScripts(new RecordingLog(), files).Get("tf_weapon_rocketlauncher_fireball")
            ?? throw new InvalidOperationException("no script tf_weapon_rocketlauncher_fireball");
    }

    private static WeaponAttackContext Attack(int owner) => new(default!, null!, null!, 1f, true,
        null!, new WeaponCommand(1, 5, owner));

    /// <summary>Teams asked in the order the touch asks them: the victim's, then the shooter's.</summary>
    private static void Teams(ProjectileRig rig, int victimTeam, int shooterTeam)
    {
        var answers = new Queue<int>([victimTeam, shooterTeam]);
        rig.Engine.ReturnsEachCall("Players.GetTeam", () => answers.Dequeue());
    }

    /// <summary>The victim is a player; nothing else the touch names is.</summary>
    private static void VictimIsPlayer(ProjectileRig rig)
    {
        rig.Engine.ReturnsEachCall("Entities.IsPlayer", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == ProjectileRig.Victim);
    }

    /// <summary>A ball over the rig's doubles, armed by the Fury's press.</summary>
    private static (ProjectileRig Rig, ScopedDamage Hits, TFBallOfFire Ball) ArmedBall()
    {
        var rig = new ProjectileRig();
        var hits = new ScopedDamage(rig.Scopes);
        var ball = new TFBallOfFire(rig.Engine, rig.Motion, hits, rig.Scopes, rig.HitGroups,
                                    rig.Engine, rig.Owners, rig.Conditions, rig.Directory);
        ball.BindAreas(TestServices.Areas(rig.Engine));
        ball.ArmedBy(TFProjectileDefs.BallBaseDamage, TFWeaponId.FlameBall,
                     "tf_weapon_rocketlauncher_fireball", critical: false, ownerIndex: 3);
        ball.FiredBy(ProjectileRig.Player, new Vector(TFProjectileDefs.BallSpeed, 0f, 0f));
        // The refund's ask lands by default; the refund facts re-stub it.
        rig.Engine.Returns("Players.GetActiveWeaponEntity", EntityHandle.None);
        return (rig, hits, ball);
    }

    [Fact]
    public void The_ball_leaves_flat_at_the_fires_speed_from_the_muzzle()
    {
        // Born at the Fury's own muzzle offsets (:152-159), velocity the
        // forward times the fire's speed (:176) with no gravity to bend it.
        var rig = new ProjectileRig();
        var hits = new ScopedDamage(rig.Scopes);
        var ball = new TFBallOfFire(rig.Engine, rig.Motion, hits, rig.Scopes, rig.HitGroups,
                                    rig.Engine, rig.Owners, rig.Conditions, rig.Directory);
        ball.BindAreas(TestServices.Areas(rig.Engine));
        rig.Directory.Instance = ball;

        rig.Effects.Projectile(Attack(3), BallScript(), default, 0f);

        Assert.Equal(TFProjectileEntityNames.BallOfFire,
                     rig.Engine.Last("Entities.Create")!.Value.ArgAs<string>(0));
        var muzzle = rig.Engine.Last("Entities.SetOrigin")!.Value.ArgAs<Vector>(1);
        Assert.Equal(TFProjectileDefs.BallMuzzleForward, muzzle.X, 0.5f);
        Assert.Equal(-TFProjectileDefs.BallMuzzleRight, muzzle.Y, 0.5f); // right is -Y at yaw 0
        Assert.Equal(64f + TFProjectileDefs.BallMuzzleUp, muzzle.Z, 0.5f);
        var velocity = rig.Engine.Last("Entities.SetVelocity")!.Value.ArgAs<Vector>(1);
        Assert.Equal(TFProjectileDefs.BallSpeed, velocity.X, 0.5f);
        Assert.Equal(0f, velocity.Z, 0.5f);
    }

    /// <summary>
    /// A ball is worth <c>tf_fireball_damage</c> (tf_projectile_dragons_fury.cpp:86-88),
    /// not the shot the script's number made: live it hit an unburnt heavy for 90.
    /// </summary>
    [Fact]
    public void The_ball_is_worth_the_fireball_damage_not_the_scripts()
    {
        var rig = new ProjectileRig();
        var ball = new TFBallOfFire(rig.Engine, rig.Motion, new ScopedDamage(rig.Scopes), rig.Scopes, rig.HitGroups,
                                    rig.Engine, rig.Owners, rig.Conditions, rig.Directory);
        ball.BindAreas(TestServices.Areas(rig.Engine));
        rig.Directory.Instance = ball;

        rig.Effects.Projectile(Attack(3), BallScript(), new ShotDamage(90f), 0f);

        Assert.Equal(TFProjectileDefs.BallBaseDamage, ball.ShotDamage, 0.01f);
    }

    [Fact]
    public void An_enemy_takes_the_ball_with_the_ignite_bit_and_the_burn()
    {
        var (rig, hits, ball) = ArmedBall();
        Teams(rig, victimTeam: 3, shooterTeam: 2);
        VictimIsPlayer(rig);

        ball.OnTouch(ProjectileRig.Victim);

        var hit = Assert.Single(hits.Hits);
        Assert.Equal(TFProjectileDefs.BallBaseDamage, hit.Damage, 2f);
        Assert.Equal(TFWeaponDamageTypes.Of(TFWeaponId.FlameBall) | TFDamageBits.Ignite, hit.Type);
        Assert.Equal(TFDamageCustom.DragonsFuryIgnite, hit.Custom);

        // Burn(...) at the convar's seconds on a non-pyro (:342-351).
        var added = Assert.Single(rig.Conditions.Added);
        Assert.Equal(TFCond.Burning, added.Cond);
        Assert.Equal(TFProjectileDefs.BallBurnDuration, added.Duration, 2f);
    }

    [Fact]
    public void ABurningVictimTakesThreeTimesTheBall()
    {
        // The bonus of a victim already burning (:337-340), weighed before
        // the hurt: the shot at three times, under the bonus custom.
        var (rig, hits, ball) = ArmedBall();
        Teams(rig, victimTeam: 3, shooterTeam: 2);
        VictimIsPlayer(rig);
        rig.Conditions.EveryoneBurns = true;

        ball.OnTouch(ProjectileRig.Victim);

        var hit = Assert.Single(hits.Hits);
        Assert.Equal(TFProjectileDefs.BallBaseDamage * TFProjectileDefs.BallBurnBonus, hit.Damage, 2f);
        Assert.Equal(TFDamageCustom.DragonsFuryBonusBurning, hit.Custom);
    }

    [Fact]
    public void The_ball_hits_each_player_once()
    {
        // The hit-list dedup (:277-281): the second touch of the same entry
        // asks no hurt and leaves no burn behind.
        var (rig, hits, ball) = ArmedBall();
        Teams(rig, victimTeam: 3, shooterTeam: 2);
        VictimIsPlayer(rig);

        ball.OnTouch(ProjectileRig.Victim);
        ball.OnTouch(ProjectileRig.Victim);

        Assert.Single(hits.Hits);
        Assert.Single(rig.Conditions.Added);
    }

    [Fact]
    public void A_hit_boosts_the_launchers_refill_once()
    {
        // The refund behind the hurt (:296-310): while the bar still runs,
        // the hit puts the boost on its scale (:302).
        var (rig, _, ball) = ArmedBall();
        Teams(rig, victimTeam: 3, shooterTeam: 2);
        VictimIsPlayer(rig);

        var weaponHandle = new EntityHandle(0x0002_0000u | 7);
        rig.Engine.Returns("Players.GetActiveWeaponEntity", weaponHandle);
        var launcher = new SourceSharp.Server.SsWeapon { Handle = weaponHandle };
        launcher.State.BindLocal();
        launcher.State.ItemMeter = 0.5f;
        rig.Directory.Holds(weaponHandle, launcher);

        ball.OnTouch(ProjectileRig.Victim);

        Assert.Equal(TFItemMeter.HitRechargeBoost, launcher.State.FlameBallChargeScale);
    }

    [Fact]
    public void A_full_launcher_meter_answers_no_boost()
    {
        // Stock's first gate on the refund (:298-299): a bar already full
        // keeps its scale — the boost answers nothing on it.
        var (rig, _, ball) = ArmedBall();
        Teams(rig, victimTeam: 3, shooterTeam: 2);
        VictimIsPlayer(rig);

        var weaponHandle = new EntityHandle(0x0002_0000u | 7);
        rig.Engine.Returns("Players.GetActiveWeaponEntity", weaponHandle);
        var launcher = new SourceSharp.Server.SsWeapon { Handle = weaponHandle };
        launcher.State.BindLocal();
        launcher.State.ItemMeter = 1f;
        rig.Directory.Holds(weaponHandle, launcher);

        ball.OnTouch(ProjectileRig.Victim);

        Assert.Equal(0f, launcher.State.FlameBallChargeScale);
    }
}

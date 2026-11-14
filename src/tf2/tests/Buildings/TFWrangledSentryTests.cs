using System.Collections.Generic;
using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Buildings;
using TF2.Shared;
using TF2.Shared.Combat;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The turret under the Wrangler's control (<c>m_bPlayerControlled</c>,
/// tf_obj_sentrygun.cpp:788-925 and :1262-1454): the dot is the target, the
/// shield rides with it, the shells and rockets wait for the trigger's word,
/// and the beat runs at half while it holds.
/// </summary>
public class TFWrangledSentryTests
{
    private static string MetalName => TFAmmoNames.Name(TFAmmo.Metal);

    /// <summary>A standing sentry raised to <paramref name="level"/>, its build just finished at t=100.</summary>
    private static (BuildingsRig Rig, TFSentryGun Sentry, EntityHandle Builder) AStandingSentry(int level = 1)
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = rig.AddBuilder();
        var sentry = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);

        sentry.StartPlacement(builder);
        rig.Clock.Now = 100f;
        sentry.StartBuilding();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        sentry.OnThink();

        if (level > 1)
        {
            float duration = TFObjectInfo.Of(TFObjectType.Sentrygun)!.UpgradeDuration;
            rig.Purse.Pockets[MetalName] = 200 * level;
            for (int l = 2; l <= level; l++)
            {
                for (int swing = 0; swing < 8; swing++)   // 200 metal at 25 the swing
                    sentry.WrenchHit(builder);
                rig.Clock.Now += duration;
                sentry.OnThink();
            }
        }

        return (rig, sentry, builder);
    }

    /// <summary>The shell's line meets a blue player, as a dot on one does.</summary>
    private static EntityHandle AnEnemyOnTheLine(BuildingsRig rig)
    {
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Engine.Returns("WorldQuery.TraceLine", new TraceResult(true, 0.9f, default, Vector.Zero, enemy));
        return enemy;
    }

    /// <summary>A point the dot can sit on: straight ahead, at the turret's eye.</summary>
    private static Vector TheDot(float x, int level = 1) => new(x, 0f, TFSentryGun.EyeHeightForLevel(level));

    [Fact]
    public void TheDotIsTheTargetAndTheShieldRidesWithIt()
    {
        var (rig, sentry, _) = AStandingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = new(300f, 0f, TFSentryGun.EyeHeightForLevel(1));
        rig.FillsSphere(enemy);

        sentry.LaserAim(TheDot(600f));
        sentry.OnThink("SentryThink");

        // The controlled find (:871-918): the dot the target, no enemy seek,
        // SHIELD_NORMAL armed for the fade.
        Assert.True(sentry.IsPlayerControlled);
        Assert.Equal(EntityHandle.None, sentry.Target);
        Assert.True(sentry.IsAttacking);
        Assert.Equal(TFSentryGun.ShieldNormal, sentry.ShieldLevel);
    }

    [Fact]
    public void ADotAtTheTurretsFeetIsNoTarget()
    {
        // Nearer than the standoff the turret stands to the shield gate
        // (:907-918, :924): controlled, sweeping, no engagement.
        var (rig, sentry, _) = AStandingSentry();
        sentry.LaserAim(new(5f, 0f, 0f));

        sentry.OnThink("SentryThink");

        Assert.True(sentry.IsPlayerControlled);
        Assert.False(sentry.IsAttacking);
        Assert.Equal(TFSentryGun.ShieldNormal, sentry.ShieldLevel);
    }

    [Fact]
    public void TheShellsWaitForTheTrigger()
    {
        var (rig, sentry, _) = AStandingSentry();
        EntityHandle enemy = AnEnemyOnTheLine(rig);
        sentry.LaserAim(TheDot(600f));

        sentry.OnThink("SentryThink");           // the engage; the first shell a beat off
        rig.Clock.Now += 0.1f;
        sentry.OnThink("SentryThink");           // the beat runs, no trigger: no shell

        Assert.Empty(rig.Engine.AllOf("Damage.TakeDamage"));

        rig.Clock.Now += 0.1f;
        sentry.LaserFire();
        sentry.OnThink("SentryThink");           // the held trigger (:1264): the shell

        Assert.Equal(enemy, Assert.Single(rig.Engine.AllOf("Damage.TakeDamage")).ArgAs<EntityHandle>(0));
        Assert.Equal(TFSentryGun.MaxShellsLevel1 - 1, sentry.Shells);
    }

    [Fact]
    public void TheShellFliesPastTheDot()
    {
        var (rig, sentry, _) = AStandingSentry();
        Vector dot = TheDot(600f);
        sentry.LaserAim(dot);

        sentry.OnThink("SentryThink");
        rig.Clock.Now += 0.1f;
        sentry.LaserFire();
        sentry.OnThink("SentryThink");

        // Along the line to the dot, not any enemy's centre (GetEnemyAimPosition
        // of the non-player, :240), cone-spread under the control — the cone
        // FireBullets samples of the shot (:1531-1538 applied at
        // baseentity_shared.cpp:1719) — and a hundred units past it (:1539):
        // the dot sits on the surface it found, so a line ending there meets
        // nothing.
        EngineCall shot = rig.Engine.AllOf("WorldQuery.TraceLine").Last();
        Vector straight = new(1f, 0f, 0f);   // the eye sits on the dot's line
        (Vector right, Vector up) = Angles.RightUp(straight);
        Vector cone = ShotSpread.Apply(new Angles.Basis(straight, right, up),
                                       ShotSpread.SpreadOf(TFSentryGun.WranglerSpreadHalfAngle),
                                       0, new SeededUniformStream()).Normalized;
        Assert.Equal(dot + cone * TFSentryGun.WranglerShellOvershoot, shot.ArgAs<Vector>(1));
    }

    /// <summary>
    /// A wrangled shell that meets nothing hurts nobody, and the hopper still pays:
    /// with no enemy to fall back on, 150 live shells once "landed" on no entity.
    /// </summary>
    [Fact]
    public void AWrangledShellThatMeetsNothingHurtsNobody()
    {
        var (rig, sentry, _) = AStandingSentry();
        sentry.LaserAim(TheDot(600f));

        sentry.OnThink("SentryThink");
        rig.Clock.Now += 0.1f;
        sentry.LaserFire();
        sentry.OnThink("SentryThink");

        Assert.Empty(rig.Engine.AllOf("Damage.TakeDamage"));
        Assert.Equal(TFSentryGun.MaxShellsLevel1 - 1, sentry.Shells);
    }

    [Fact]
    public void TheWrangledBeatRunsAtHalf()
    {
        // m_flFireRate 0.5 (tf_weapon_laser_pointer.cpp's arm at :1275): the
        // 0.2 s level-1 hopper beats at 0.1 under the control.
        var (rig, sentry, _) = AStandingSentry();
        AnEnemyOnTheLine(rig);
        sentry.LaserAim(TheDot(600f));

        sentry.OnThink("SentryThink");
        rig.Clock.Now += 0.1f;
        sentry.LaserFire();
        sentry.OnThink("SentryThink");           // the first shell
        rig.Clock.Now += 0.1f;
        sentry.LaserFire();
        sentry.OnThink("SentryThink");           // the second, half a beat later

        Assert.Equal(2, rig.Engine.AllOf("Damage.TakeDamage").Count());
        Assert.Equal(TFSentryGun.MaxShellsLevel1 - 2, sentry.Shells);
    }

    [Fact]
    public void TheRocketsWaitForTheAltFireToo()
    {
        // The pod under the control fires on m_bFireRocket when the alt-fire
        // asks (:1303-1307); the auto-rocket's !m_bPlayerControlled (:1451).
        var (rig, sentry, _) = AStandingSentry(3);
        var rocket = rig.Spawn<TFSentryRocket>(TFSentryRocket.MapClass);
        rig.Engine.Returns("Entities.Create", rocket.Handle);
        Vector dot = TheDot(600f, 3);
        sentry.LaserAim(dot);

        sentry.OnThink("SentryThink");
        rig.Clock.Now += 0.1f;
        sentry.LaserFireRockets();
        sentry.OnThink("SentryThink");

        EngineCall launched = rig.Engine.AllOf("Entities.SetVelocity")
                                     .Single(c => c.ArgAs<EntityHandle>(0) == rocket.Handle);
        Assert.Equal(new Vector(TFSentryRocket.Speed, 0f, 0f), launched.ArgAs<Vector>(1));
        Assert.Equal(TFSentryGun.MaxRocketsStored - 1, sentry.Rockets);
        Assert.Empty(rig.Engine.AllOf("Damage.TakeDamage"));   // the shells still wait
    }

    [Fact]
    public void TheShieldTakesAMThirdOfTheWound()
    {
        // m_nShieldLevel's damage arm (tf_obj_sentrygun.cpp:295-299's scale).
        var (rig, sentry, _) = AStandingSentry();
        int whole = sentry.Health;
        sentry.LaserAim(TheDot(600f));
        sentry.OnThink("SentryThink");

        Assert.Equal(TFSentryGun.ShieldDamageScale * 100, BuildingsRig.Hit(sentry, rig.NewHandle(), 100, DamageType.Bullet));
        Assert.Equal(whole - 33, sentry.Health);
    }

    [Fact]
    public void TheControlDropsAndTheShieldLingersItsThreeSeconds()
    {
        var (rig, sentry, _) = AStandingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = new(300f, 0f, TFSentryGun.EyeHeightForLevel(1));
        rig.FillsSphere(enemy);
        sentry.LaserAim(TheDot(600f));

        sentry.OnThink("SentryThink");           // t=100: the shield arms for 103
        rig.Clock.Now = 102f;
        sentry.OnThink("SentryThink");           // held: the arm renews (:790)
        Assert.Equal(TFSentryGun.ShieldNormal, sentry.ShieldLevel);

        sentry.LaserReleased();
        rig.Clock.Now = 103f;
        sentry.OnThink("SentryThink");           // released: the control drops, the fade re-arms

        Assert.False(sentry.IsPlayerControlled);
        Assert.Equal(TFSentryGun.ShieldNormal, sentry.ShieldLevel);
        Assert.Equal(EntityHandle.None, sentry.Target);   // no seek under the shield (:924)

        rig.Clock.Now = 106.1f;
        sentry.OnThink("SentryThink");           // past the fade: the level drops
        Assert.Equal(TFSentryGun.ShieldNone, sentry.ShieldLevel);

        rig.Clock.Now = 106.2f;
        sentry.OnThink("SentryThink");           // and the next find seeks again

        Assert.Equal(enemy, sentry.Target);
    }

    [Fact]
    public void TheSeamCarriesTheCommandToTheTurret()
    {
        // The real use layer over the rig's own engine doubles: the command's
        // index names the builder, the eye traces along the view, and the
        // wall the trace ends on is the dot the turret holds.
        var (rig, sentry, builder) = AStandingSentry();
        var wrangle = new TFSentryWrangle(rig.Buildings(), rig.Engine, rig.Engine, rig.Engine, rig.Registry);
        Vector eye = new(0f, 0f, TFSentryGun.EyeHeightForLevel(1));
        Vector wall = new(400f, 0f, TFSentryGun.EyeHeightForLevel(1));
        rig.Engine.Returns("Players.GetHandle", builder);
        rig.Engine.Returns("Entities.GetEyePosition", eye);
        rig.Engine.Returns("Entities.GetEyeAngles", Vector.Zero);
        rig.Engine.Returns("WorldQuery.TraceLine", new TraceResult(true, 1f, wall, new Vector(0f, 1f, 0f), EntityHandle.None));

        wrangle.Aim(1);

        EngineCall? call = rig.Engine.Last("WorldQuery.TraceLine");
        Assert.NotNull(call);
        EngineCall trace = call!.Value;
        Assert.Equal(eye, trace.ArgAs<Vector>(0));
        Assert.Equal(builder, trace.ArgAs<EntityHandle>(3));   // the holder ignored

        sentry.OnThink("SentryThink");
        Assert.True(sentry.IsPlayerControlled);
        Assert.True(sentry.IsAttacking);                       // the dot, 400 out: engaged

        wrangle.Release(1);
        sentry.OnThink("SentryThink");
        Assert.False(sentry.IsPlayerControlled);
    }

    /// <summary>The brief auto-aim (:881-904): the player the holder's dot
    /// lasered holds the controlled turret's target while the window holds,
    /// and the dot's word is back once it passes.</summary>
    [Fact]
    public void TheLasedEnemyHoldsTheTargetForTheBriefWindow()
    {
        var (rig, sentry, builder) = AStandingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        Vector eye = new(0f, 0f, TFSentryGun.EyeHeightForLevel(1));
        Vector wall = new(400f, 0f, eye.Z);
        rig.Centers[enemy] = new Vector(300f, 0f, eye.Z);
        rig.Origins[enemy] = new Vector(300f, 0f, eye.Z);
        rig.Engine.Returns("Players.GetHandle", builder);
        rig.Engine.Returns("Entities.GetIndex", 3);
        rig.Engine.Returns("Entities.GetEyePosition", eye);
        rig.Engine.Returns("Entities.GetEyeAngles", Vector.Zero);
        rig.Engine.Returns("WorldQuery.TraceLine", new TraceResult(true, 1f, wall, new Vector(0f, 1f, 0f), enemy));

        var wrangle = new TFSentryWrangle(rig.Buildings(), rig.Engine, rig.Engine, rig.Engine, rig.Registry);
        wrangle.Aim(1);
        sentry.OnThink("SentryThink");

        // The lased player, not the dot, while the reach line meets them.
        Assert.Equal(enemy, sentry.Target);
        Assert.True(sentry.IsAttacking);

        // Past the window the dot's word comes back (:901-904's clear).
        rig.Clock.Now += TFSentryGun.AutoAimWindow + 0.1f;
        sentry.OnThink("SentryThink");

        Assert.Equal(EntityHandle.None, sentry.Target);
        Assert.True(sentry.IsAttacking);
    }

    /// <summary>A wall between clears the assist (:896-899): the reach line's
    /// own end has to be the player.</summary>
    [Fact]
    public void AWallBetweenClearsTheLasedTarget()
    {
        var (rig, sentry, builder) = AStandingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        Vector eye = new(0f, 0f, TFSentryGun.EyeHeightForLevel(1));
        Vector wall = new(400f, 0f, eye.Z);
        rig.Centers[enemy] = new Vector(300f, 0f, eye.Z);
        rig.Origins[enemy] = new Vector(300f, 0f, eye.Z);
        rig.Engine.Returns("Players.GetHandle", builder);
        rig.Engine.Returns("Entities.GetIndex", 3);
        rig.Engine.Returns("Entities.GetEyePosition", eye);
        rig.Engine.Returns("Entities.GetEyeAngles", Vector.Zero);
        rig.Engine.Returns("WorldQuery.TraceLine", new TraceResult(true, 1f, wall, new Vector(0f, 1f, 0f), enemy));

        var wrangle = new TFSentryWrangle(rig.Buildings(), rig.Engine, rig.Engine, rig.Engine, rig.Registry);
        wrangle.Aim(1);
        sentry.OnThink("SentryThink");
        Assert.Equal(enemy, sentry.Target);

        // The next ask stops on a world face instead: the line no longer
        // meets the player, the assist clears and the dot carries the aim.
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(true, 1f, wall, new Vector(0f, 0f, 1f), EntityHandle.None));
        wrangle.Aim(1);
        sentry.OnThink("SentryThink");

        Assert.Equal(EntityHandle.None, sentry.Target);
        Assert.True(sentry.IsAttacking);
    }

    /// <summary>The wrangled shell's custom (<c>ModifyFireBulletsDamage</c>,
    /// tf_obj_sentrygun.cpp:1651-1657): the kill feed's sentry arm reads it
    /// for the pointer's name.</summary>
    [Fact]
    public void TheWrangledShellCarriesTheSentryCustom()
    {
        var (rig, sentry, _) = AStandingSentry();
        AnEnemyOnTheLine(rig);
        var customs = new List<TFDamageCustom>();
        rig.Engine.WhenCalled("Damage.TakeDamage",
                              call => customs.Add(rig.Scopes.TryCurrent(out var custom, out _, out _) ? custom : TFDamageCustom.None));

        sentry.LaserAim(TheDot(600f));
        sentry.OnThink("SentryThink");
        rig.Clock.Now += 0.1f;
        sentry.LaserFire();
        sentry.OnThink("SentryThink");           // the held trigger: the shell

        Assert.Equal(new[] { TFDamageCustom.PlayerSentry }, customs);
    }
}

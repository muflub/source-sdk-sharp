using System.Linq;
using System.Collections.Generic;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server;
using TF2.Server.Buildings;
using TF2.Server.Player;
using TF2.Shared.Player;
using TF2.Shared;
using TF2.Shared.Combat;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>The level 1 turret's fight: the find, the aim, the shells.</summary>
public class TFSentryGunTests
{
    /// <summary>A standing sentry of <paramref name="team"/>, its build just finished at t=110.</summary>
    private static (BuildingsRig Rig, TFSentryGun Sentry, EntityHandle Builder) AWorkingSentry(TFTeam team = TFTeam.Red, float facing = 0f)
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        if (facing != 0f)
            rig.Engine.Returns("Entities.GetAngles", new Vector(0f, facing, 0f));
        EntityHandle builder = rig.AddBuilder(team);
        var sentry = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);

        sentry.StartPlacement(builder);
        rig.Clock.Now = 100f;
        sentry.StartBuilding();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        sentry.OnThink();   // the base think's last tick: constructs to full and goes active

        return (rig, sentry, builder);
    }

    /// <summary>An enemy centre the sentry can shoot: on the ground plane, at the eye height.</summary>
    private static Vector Ahead(EntityHandle enemy, float x) => new(x, 0f, TFSentryGun.EyeHeightForLevel(1));

    [Fact]
    public void TheSentrySeesThroughStocksShotMaskWithGrating()
    {
        // ValidTargetPlayer's FVisible( pPlayer, MASK_SHOT | CONTENTS_GRATE )
        // (tf_obj_sentrygun.cpp:1084): a player or a prop in the line blocks it, a
        // brushes-only line would see through both.
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(enemy, 300f);
        rig.FillsSphere(enemy);

        sentry.OnThink("SentryThink");

        var trace = rig.Engine.Last("WorldQuery.TraceLine");
        Assert.NotNull(trace);
        Assert.Equal(TraceMask.SentrySight, trace!.Value.ArgAs<TraceMask>(2));
    }

    [Fact]
    public void TheSentryAcquiresTheNearestVisibleEnemy()
    {
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle near = rig.AddBuilder(TFTeam.Blue);
        EntityHandle far = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[near] = Ahead(near, 200f);
        rig.Centers[far] = Ahead(far, 900f);
        rig.FillsSphere(far, near);

        sentry.OnThink("SentryThink");

        Assert.Equal(near, sentry.Target);
        Assert.True(sentry.IsAttacking);
    }

    [Fact]
    public void TheSentryIgnoresItsOwnTeam()
    {
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle mate = rig.AddBuilder(TFTeam.Red);
        rig.Centers[mate] = Ahead(mate, 200f);
        rig.FillsSphere(mate);

        sentry.OnThink("SentryThink");

        Assert.Equal(EntityHandle.None, sentry.Target);
        Assert.False(sentry.IsAttacking);
    }

    [Fact]
    public void TheSentryIgnoresWhatStandsBeyondItsRange()
    {
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(enemy, TFSentryGun.SentryMaxRange + 500f);
        rig.FillsSphere(enemy);

        sentry.OnThink("SentryThink");

        Assert.Equal(EntityHandle.None, sentry.Target);
        Assert.False(sentry.IsAttacking);
    }

    [Fact]
    public void TheSentryWontAcquireWhatItCannotSee()
    {
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(enemy, 200f);
        rig.BlockedEnds.Add(rig.Centers[enemy]);
        rig.FillsSphere(enemy);

        sentry.OnThink("SentryThink");

        Assert.Equal(EntityHandle.None, sentry.Target);
        Assert.False(sentry.IsAttacking);
    }

    /// <summary>A blue dispenser, placed, building and centred <paramref name="x"/> out.</summary>
    private static TFDispenser ABuilding(BuildingsRig rig, float x)
    {
        EntityHandle builder = rig.AddBuilder(TFTeam.Blue);
        var dispenser = rig.Spawn<TFDispenser>(TFDispenser.MapClass);
        dispenser.StartPlacement(builder);
        dispenser.StartBuilding();
        rig.Centers[dispenser.Handle] = Ahead(dispenser.Handle, x);
        return dispenser;
    }

    [Fact]
    public void AnEnemyBuildingStandsForTheTurret()
    {
        var (rig, sentry, _) = AWorkingSentry();
        TFDispenser dispenser = ABuilding(rig, 300f);
        rig.FillsSphere(dispenser.Handle);

        sentry.OnThink("SentryThink");           // the find: a building is a valid enemy
        rig.Clock.Now += 0.3f;
        sentry.OnThink("SentryThink");           // aimed, and the beat has run

        Assert.Equal(dispenser.Handle, sentry.Target);
        Assert.True(sentry.IsAttacking);
        EngineCall shot = Assert.Single(rig.Engine.AllOf("Damage.TakeDamage"));
        Assert.Equal(dispenser.Handle, shot.ArgAs<EntityHandle>(0));
    }

    [Fact]
    public void TheSapperAttachmentIsNotTheTargetItsParentIs()
    {
        var (rig, sentry, _) = AWorkingSentry();
        TFDispenser dispenser = ABuilding(rig, 300f);
        EntityHandle spy = rig.AddBuilder(TFTeam.Blue);
        var sapper = rig.Spawn<TFSapper>(TFSapper.MapClass);
        sapper.StartPlacement(spy);
        sapper.AttachTo(dispenser);
        sapper.StartBuilding();
        sapper.OnThink();
        rig.Centers[sapper.Handle] = Ahead(sapper.Handle, 300f);
        rig.FillsSphere(dispenser.Handle, sapper.Handle);

        sentry.OnThink("SentryThink");

        // ValidTargetObject passes the sapper attachments over
        // (tf_obj_sentrygun.cpp:1090-1109): the parent building is shot.
        Assert.Equal(dispenser.Handle, sentry.Target);
    }

    [Fact]
    public void APlayerStandsBeforeABuildingInTheFind()
    {
        var (rig, sentry, _) = AWorkingSentry();
        TFDispenser dispenser = ABuilding(rig, 200f);   // nearer than the player
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(enemy, 300f);
        rig.FillsSphere(dispenser.Handle, enemy);

        sentry.OnThink("SentryThink");

        // The objects loop runs only when no player stood
        // (tf_obj_sentrygun.cpp:998) — even the nearer building loses.
        Assert.Equal(enemy, sentry.Target);
    }

    [Fact]
    public void TheSentryFiresThroughTheDamagePathOnceAimed()
    {
        var (rig, sentry, builder) = AWorkingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(enemy, 300f);
        rig.FillsSphere(enemy);

        sentry.OnThink("SentryThink");           // the find; the first shell is a quarter beat off
        rig.Clock.Now += 0.3f;
        sentry.OnThink("SentryThink");           // aimed by then: the target sits straight ahead

        EngineCall shot = Assert.Single(rig.Engine.AllOf("Damage.TakeDamage"));
        Assert.Equal(enemy, shot.ArgAs<EntityHandle>(0));
        Assert.Equal(builder, shot.ArgAs<EntityHandle>(1));
        Assert.Equal(rig.Convars.Declared<TFSentryGunDamageConVar>().Value, shot.ArgAs<float>(2));
        Assert.Equal(DamageType.Bullet, shot.ArgAs<DamageType>(3));
        Assert.Equal(TFSentryGun.MaxShellsLevel1 - 1, sentry.Shells);
    }

    /// <summary>The turret's own shell rides no custom (ModifyFireBulletsDamage's
    /// <c>m_bPlayerControlled</c> gate, tf_obj_sentrygun.cpp:1653): the kill
    /// feed names the gun, not the pointer's arm.</summary>
    [Fact]
    public void TheTurretsOwnShellCarriesNoCustom()
    {
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(enemy, 300f);
        rig.FillsSphere(enemy);

        List<TFDamageCustom> during = new();
        rig.Engine.WhenCalled("Damage.TakeDamage",
                              call => during.Add(rig.Scopes.TryCurrent(out var custom, out _, out _) ? custom : TFDamageCustom.None));

        sentry.OnThink("SentryThink");           // the find
        rig.Clock.Now += 0.3f;
        sentry.OnThink("SentryThink");           // aimed: the shot

        Assert.Equal(new[] { TFDamageCustom.None }, during);
    }

    [Fact]
    public void TheDamageConvarScalesEveryShell()
    {
        // Stock reads tf_sentrygun_damage into the shot's damage
        // (tf_obj_sentrygun.cpp:1549).
        var (rig, sentry, _) = AWorkingSentry();
        rig.Convars.Declared<TFSentryGunDamageConVar>().Bind(-1, "tf_sentrygun_damage", "32");
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(enemy, 300f);
        rig.FillsSphere(enemy);

        sentry.OnThink("SentryThink");           // the find; the first shell is a quarter beat off
        rig.Clock.Now += 0.3f;
        sentry.OnThink("SentryThink");           // aimed by then: the target sits straight ahead

        EngineCall shot = Assert.Single(rig.Engine.AllOf("Damage.TakeDamage"));
        Assert.Equal(32f, shot.ArgAs<float>(2));
    }

    [Fact]
    public void TheShellIsDealtInTheSentrysName()
    {
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(enemy, 300f);
        rig.FillsSphere(enemy);

        // The kill rules read the open inflictor mid-hit (CTakeDamageInfo's
        // inflictor, stock's own this): recorded as each shot lands.
        List<EntityHandle> during = new();
        rig.Engine.WhenCalled("Damage.TakeDamage",
                              call => during.Add(rig.Inflictors.TryCurrent(out var inflictor, out _)
                                                     ? inflictor : EntityHandle.None));

        sentry.OnThink("SentryThink");          // the find
        rig.Clock.Now += 0.3f;
        sentry.OnThink("SentryThink");          // aimed: the shot

        Assert.Equal(new[] { sentry.Handle }, during);
    }

    [Fact]
    public void TheSentryStopsFiringWhenTheHopperIsEmpty()
    {
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(enemy, 300f);
        rig.FillsSphere(enemy);

        sentry.OnThink("SentryThink");           // the find
        for (int shell = 0; shell < 200 && sentry.Shells > 0; shell++)
        {
            rig.Clock.Now += 0.3f;
            sentry.OnThink("SentryThink");
        }
        Assert.Equal(TFSentryGun.MaxShellsLevel1, rig.Engine.AllOf("Damage.TakeDamage").Count());

        rig.Clock.Now += 0.3f;
        sentry.OnThink("SentryThink");           // the empty hopper: the beat runs, no shell

        Assert.Equal(0, sentry.Shells);
        Assert.Equal(TFSentryGun.MaxShellsLevel1, rig.Engine.AllOf("Damage.TakeDamage").Count());
    }

    [Fact]
    public void TheSentryStopsWhenTheTargetIsNoMore()
    {
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(enemy, 300f);
        rig.FillsSphere(enemy);
        sentry.OnThink("SentryThink");
        Assert.True(sentry.IsAttacking);

        rig.Players.All[enemy].Alive = false;
        sentry.OnThink("SentryThink");

        Assert.False(sentry.IsAttacking);
        Assert.Equal(EntityHandle.None, sentry.Target);
    }

    [Fact]
    public void TheSentryTurnsToAnEnemyBehindItsDeployment()
    {
        var (rig, sentry, _) = AWorkingSentry();   // stood facing yaw 0
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(enemy, -900f);  // straight behind
        rig.FillsSphere(enemy);

        // Attack angle-mods the yaw and clamps only the pitch
        // (tf_obj_sentrygun.cpp:1240-1257): the deployment bounds are the
        // idle sweep's alone, so the turret tracks a target straight behind
        // its build facing.
        for (int think = 0; think < 200; think++)
        {
            rig.Clock.Now += TFSentryGun.SentryThinkDelay;
            sentry.OnThink("SentryThink");
        }

        Assert.Equal(180f, sentry.Yaw, 2);
    }

    [Fact]
    public void TheBuiltSentryKeepsTheLevel1LoadAndTheStockBeat()
    {
        var (_, sentry, _) = AWorkingSentry();

        Assert.Equal(TFSentryGun.MaxShellsLevel1, sentry.Shells);
        Assert.False(sentry.IsAttacking);
        Assert.Equal(TFSentryGun.SentryThinkDelay, sentry.NextThinkDelay("SentryThink"), 3);
    }

    [Fact]
    public void TheTrackingTurnRampsPastTheBaseRate()
    {
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = new Vector(229.8f, 192.8f, TFSentryGun.EyeHeightForLevel(1));   // 40° off the deployment
        rig.FillsSphere(enemy);

        rig.Clock.Now += 0.05f;
        sentry.OnThink("SentryThink");           // the find, and the ramp's first step
        rig.Clock.Now += 0.05f;
        sentry.OnThink("SentryThink");

        // 18°/s then 36°/s at the 0.05 s think (tf_obj_sentrygun.cpp:1890-1897)
        Assert.Equal(2.7f, sentry.Yaw, 2);
    }

    [Fact]
    public void TheSentryTurnsOneThinkDelayAStep()
    {
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = new Vector(229.8f, 192.8f, TFSentryGun.EyeHeightForLevel(1));   // 40° off the deployment
        rig.FillsSphere(enemy);
        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");           // the find, and the ramp's first step

        rig.Clock.Now += 10f;                    // a stalled think's gap
        sentry.OnThink("SentryThink");

        // MoveTurret steps the rate over SENTRY_THINK_DELAY a think
        // (tf_obj_sentrygun.cpp:1837, :1897): the clock's gap is not the
        // step — 0.9 for the find think, 1.8 as the ramp climbs.
        Assert.Equal(2.7f, sentry.Yaw, 2);
    }

    [Fact]
    public void TheSettledTurretRestartsAtTheFloorRate()
    {
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = new Vector(229.8f, 192.8f, TFSentryGun.EyeHeightForLevel(1));   // 40° off the deployment
        rig.FillsSphere(enemy);
        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");           // the find, and the ramp's first step
        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");

        // Onto the aim: one think pegs the yaw, the next moves nothing —
        // !bMoved drops the rate back to the floor (tf_obj_sentrygun.cpp
        // :1942-1945).
        rig.Centers[enemy] = new Vector(299.7f, 14.1f, TFSentryGun.EyeHeightForLevel(1));
        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");
        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");

        // Far again: the ramp rebuilds from the floor — 48°/s this think,
        // 2.4° — not from the 36°/s it climbed to before.
        rig.Centers[enemy] = new Vector(229.8f, 192.8f, TFSentryGun.EyeHeightForLevel(1));
        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");

        Assert.Equal(5.1f, sentry.Yaw, 1);
    }

    [Fact]
    public void TheTrackingRampOvershootsTheCap()
    {
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = new Vector(229.8f, 192.8f, TFSentryGun.EyeHeightForLevel(1));   // 40° off the deployment
        rig.FillsSphere(enemy);
        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");           // the find, and the ramp's first step
        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");

        // Onto the aim: the rate floors (:1942-1945), as above.
        rig.Centers[enemy] = new Vector(299.7f, 14.1f, TFSentryGun.EyeHeightForLevel(1));
        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");
        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");

        // Far again: nine thinks of the climb from the floor. The cap is
        // tested before the add (tf_obj_sentrygun.cpp:1888-1894), so the
        // last step under it runs at 192°/s, not 180: 2.4 + 3.3 + … + 9.6
        // from the 2.7° the aim held.
        rig.Centers[enemy] = new Vector(-52.09f, 295.44f, TFSentryGun.EyeHeightForLevel(1));   // 100° off
        for (int think = 0; think < 9; think++)
        {
            rig.Clock.Now += TFSentryGun.SentryThinkDelay;
            sentry.OnThink("SentryThink");
        }

        Assert.Equal(56.7f, sentry.Yaw, 1);
    }

    [Fact]
    public void TheFireGateIsTheAimsMagnitudeNotEachAxisAlone()
    {
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = new Vector(297.1f, 41.8f, 95.8f);   // 8° off in yaw, 12° up
        rig.FillsSphere(enemy);

        rig.Clock.Now += 0.05f;
        sentry.OnThink("SentryThink");           // the find
        rig.Clock.Now += 0.05f;
        sentry.OnThink("SentryThink");           // the first beat: 6.5° and 9.0° — 11.1 as one vector

        // Stock's gate (tf_obj_sentrygun.cpp:1262) refuses what either axis
        // alone would let through.
        Assert.Equal(TFSentryGun.MaxShellsLevel1, sentry.Shells);
    }

    [Fact]
    public void TheFireGateRefusesAcrossTheYawSeam()
    {
        var (rig, sentry, _) = AWorkingSentry(facing: 358f);   // stood facing 358
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = new Vector(299.8f, 10.5f, TFSentryGun.EyeHeightForLevel(1));   // 2° off: the short way is 4°
        rig.FillsSphere(enemy);

        sentry.OnThink("SentryThink");           // the find: the goal crosses the 0/360 seam
        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");           // stood at 358 against a goal of 2

        // Stock's gate (tf_obj_sentrygun.cpp:1262) is the raw difference of
        // the angle-modded angles: across the 0/360 seam the length runs the
        // long way round, and no shell fires.
        Assert.Equal(TFSentryGun.MaxShellsLevel1, sentry.Shells);
    }

    [Fact]
    public void TheHeldEnemyMustStaySeen()
    {
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(enemy, 300f);
        rig.FillsSphere(enemy);
        sentry.OnThink("SentryThink");           // the find and the acquisition
        Assert.True(sentry.IsAttacking);

        rig.BlockedEnds.Add(rig.Centers[enemy]); // a wall up between them
        rig.Clock.Now += 0.3f;
        sentry.OnThink("SentryThink");

        // Attack's re-find runs the line of sight on the held enemy too
        // (tf_obj_sentrygun.cpp:1229, :1084): lost, the turret searches
        // instead of shelling the wall.
        Assert.False(sentry.IsAttacking);
        Assert.Equal(EntityHandle.None, sentry.Target);
        Assert.Equal(TFSentryGun.MaxShellsLevel1, sentry.Shells);
    }

    [Fact]
    public void AQuarterNearerEnemyTakesTheTurret()
    {
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle held = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[held] = Ahead(held, 300f);
        rig.FillsSphere(held);
        sentry.OnThink("SentryThink");
        Assert.Equal(held, sentry.Target);

        EntityHandle closer = rig.AddBuilder(TFTeam.Blue);   // 200 < 300 × 0.75
        rig.Centers[closer] = Ahead(closer, 200f);
        rig.FillsSphere(closer);
        rig.Clock.Now += 0.3f;
        sentry.OnThink("SentryThink");

        Assert.Equal(closer, sentry.Target);
    }

    [Fact]
    public void TheHeldEnemyStandsAgainstABarelyCloserRival()
    {
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle held = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[held] = Ahead(held, 300f);
        rig.FillsSphere(held);
        sentry.OnThink("SentryThink");
        Assert.Equal(held, sentry.Target);

        EntityHandle rival = rig.AddBuilder(TFTeam.Blue);   // 260² ≥ 300² × 0.75
        rig.Centers[rival] = Ahead(rival, 260f);
        rig.FillsSphere(held, rival);
        rig.Clock.Now += 0.3f;
        sentry.OnThink("SentryThink");

        // The 25% hysteresis (tf_obj_sentrygun.cpp:1042): not worth the turn.
        Assert.Equal(held, sentry.Target);
    }

    [Fact]
    public void ARivalAheadOfTheHeldInTheScanTakesTheTurret()
    {
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle held = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[held] = Ahead(held, 300f);
        rig.FillsSphere(held);
        sentry.OnThink("SentryThink");
        Assert.Equal(held, sentry.Target);

        EntityHandle rival = rig.AddBuilder(TFTeam.Blue);   // 290²: closer, not a quarter nearer
        rig.Centers[rival] = Ahead(rival, 290f);
        rig.FillsSphere(rival, held);                        // the rival precedes in the scan
        rig.Clock.Now += 0.3f;
        sentry.OnThink("SentryThink");

        // The held's distance is recorded only when it wins a scan step
        // (tf_obj_sentrygun.cpp:954, :964-966): a nearer rival ahead of it
        // in the order takes the attack outright.
        Assert.Equal(rival, sentry.Target);
    }

    [Fact]
    public void AnIdleSentrySweepsItsDeployment()
    {
        var (rig, sentry, _) = AWorkingSentry();   // stood facing yaw 0
        rig.FillsSphere();                         // nothing in the sphere

        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");             // nothing in the sphere: the sweep

        Assert.True(sentry.Yaw > 0f);              // swinging toward the + bound

        int guard = 0;
        while (sentry.Yaw != TFSentryGun.DeployedYawBound && guard++ < 400)
        {
            rig.Clock.Now += TFSentryGun.SentryThinkDelay;
            sentry.OnThink("SentryThink");
        }

        Assert.Equal(TFSentryGun.DeployedYawBound, sentry.Yaw, 2);

        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");             // arrived: the flip, standing still
        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");             // turn round

        Assert.True(sentry.Yaw < TFSentryGun.DeployedYawBound);
    }

    [Fact]
    public void TheFirstSweepLegTakesTheLowerBound()
    {
        var (rig, sentry, _) = AWorkingSentry(facing: 90f);   // stood facing 90
        rig.FillsSphere();                                    // nothing in the sphere

        // OnGoActive angle-mods both bounds and hands the sweep the lower
        // numeric one first (tf_obj_sentrygun.cpp:408-420): the first leg
        // walks down toward 40, not up toward 140.
        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");

        Assert.True(sentry.Yaw < 90f);

        int guard = 0;
        while (sentry.Yaw != 40f && guard++ < 400)
        {
            rig.Clock.Now += TFSentryGun.SentryThinkDelay;
            sentry.OnThink("SentryThink");
        }

        Assert.Equal(40f, sentry.Yaw, 2);

        while (sentry.Yaw != 140f && guard++ < 800)
        {
            rig.Clock.Now += TFSentryGun.SentryThinkDelay;
            sentry.OnThink("SentryThink");
        }

        Assert.Equal(140f, sentry.Yaw, 2);
    }

    [Fact]
    public void TheSweepTurnsRoundOnlyOnceItHasStoodStill()
    {
        var (rig, sentry, _) = AWorkingSentry();   // stood facing yaw 0
        rig.FillsSphere();                         // nothing in the sphere

        int guard = 0;
        while (sentry.Yaw != TFSentryGun.DeployedYawBound && guard++ < 400)
        {
            rig.Clock.Now += TFSentryGun.SentryThinkDelay;
            sentry.OnThink("SentryThink");
        }

        // SentryRotate turns round on !MoveTurret (tf_obj_sentrygun.cpp
        // :1721): the think after the arrival stands still — the flip —
        // and only the next one moves.
        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");
        Assert.Equal(TFSentryGun.DeployedYawBound, sentry.Yaw, 2);

        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");
        Assert.True(sentry.Yaw < TFSentryGun.DeployedYawBound);
    }

    [Fact]
    public void TheAimReadsADownhillTargetAsStocksPitch()
    {
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = new Vector(300f, 0f, TFSentryGun.EyeHeightForLevel(1) - 40f);   // 40 below the eye
        rig.FillsSphere(enemy);

        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");           // the find, and the first step down

        // VectorAngles' pitch is atan2(-forward.z, horizontal)
        // (mathlib_base.cpp:1020): a target below the eye pitches the
        // barrel down, and stock's pitch reads positive going down.
        Assert.Equal(1.5f, sentry.Pitch, 2);
    }

    [Fact]
    public void ThePoseMirrorsTheAimOntoTheTarget()
    {
        var (rig, sentry, _) = AWorkingSentry();

        // Stock's names, looked up against the model worn
        // (tf_obj_sentrygun.cpp:2130-2131): pitch is the third slot, yaw the fifth.
        rig.Engine.Returns("EntityRender.GetModelName", "models/buildables/sentry1_heavy.mdl");
        rig.Engine.ReturnsEachCall("EntityRender.LookupPoseParameter", () =>
        {
            string name = (rig.Engine.Last("EntityRender.LookupPoseParameter") ?? default).ArgAs<string>(1);
            return name == "aim_pitch" ? 3 : 5;
        });

        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = new Vector(300f, 300f, TFSentryGun.EyeHeightForLevel(1) - 40f);   // 45 off, below the eye
        rig.FillsSphere(enemy);

        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");

        EngineCall[] poses = rig.Engine.AllOf("EntityRender.SetPoseParameter")
                                        .Where(c => c.ArgAs<EntityHandle>(0) == sentry.Handle)
                                        .ToArray();
        Assert.Equal(2, poses.Length);

        // The pose mirrors the aim about the body (tf_obj_sentrygun.cpp:1851,
        // :1931-1935): the pitch negated, the yaw measured off the build
        // facing (0 here) and negated — and neither is a zero, or the turret
        // never turned.
        Assert.Equal(3, poses[0].ArgAs<int>(1));
        Assert.Equal(-sentry.Pitch, poses[0].ArgAs<float>(2), 3);
        Assert.NotEqual(0f, poses[0].ArgAs<float>(2));

        Assert.Equal(5, poses[1].ArgAs<int>(1));
        Assert.Equal(-sentry.Yaw, poses[1].ArgAs<float>(2), 3);
        Assert.NotEqual(0f, poses[1].ArgAs<float>(2));
    }

    /// <summary>A funded engineer whose arm item carries the Gunslinger's
    /// <c>wrench_builds_minisentry</c> — stock reads it off the wrench the
    /// builder owns (<c>tf_obj.cpp:1491</c>, <c>tf_weapon_wrench.h:42</c>).</summary>
    private static EntityHandle AGunslinger(BuildingsRig rig, int metal = 500)
    {
        EntityHandle builder = rig.AddBuilder();
        rig.Owners.Items[builder.EntryIndex] = rig.AttributeItem(
            "wrench_builds_minisentry", TF2.Shared.Econ.AttributeDescriptionFormat.Additive, 1);
        rig.Purse.Pockets[TFAmmoNames.Name(TFAmmo.Metal)] = metal;
        return builder;
    }

    /// <summary>A built mini, completed: the stock-time think after the row's build time.</summary>
    private static (BuildingsRig Rig, EntityHandle Builder, TFSentryGun Sentry) ACompletedMini(int metal = 500)
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = AGunslinger(rig, metal);
        var outcome = rig.Builder().Build(builder, TFObjectType.Sentrygun, 0, Vector.Zero, Vector.Zero);
        Assert.Equal(TFCanBuildResult.CanBuild, outcome.Result);
        var sentry = (TFSentryGun)outcome.Building!;
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        sentry.OnThink();
        return (rig, builder, sentry);
    }

    [Fact]
    public void AGunslingerWrenchBuildsAMiniSentry()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = AGunslinger(rig);

        var outcome = rig.Builder().Build(builder, TFObjectType.Sentrygun, 0, Vector.Zero, Vector.Zero);

        Assert.Equal(TFCanBuildResult.CanBuild, outcome.Result);
        var sentry = (TFSentryGun)outcome.Building!;

        // MakeMiniBuilding's flag and half-of-max start
        // (tf_obj_sentrygun.cpp:367-380): the attribute on the wrench makes
        // the build a mini, standing at half its hundred.
        Assert.True(sentry.IsMiniBuilding);
        Assert.Equal(50, sentry.Health);

        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        sentry.OnThink();                        // complete: construction's ramp reached the max (:2124)
        Assert.False(sentry.IsBuilding);
        Assert.Equal(100, sentry.Health);
    }

    [Fact]
    public void AMiniSentryNeverUpgrades()
    {
        var (rig, builder, sentry) = ACompletedMini();

        // Construction's ramp leaves the mini whole, so every swing feeds
        // the upgrade pot — and the mini's ceiling is the one it stands on
        // (GetMaxUpgradeLevel's mini arm, tf_obj_sentrygun.cpp:383-389;
        // CanBeUpgraded's refusal, tf_obj.cpp:2925-2926).
        for (int swing = 0; swing < 12; swing++)
            sentry.WrenchHit(builder);
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.UpgradeDuration;
        sentry.OnThink();

        Assert.Equal(1, sentry.Level);
    }

    [Fact]
    public void AMiniSentryCostsStocksMetal()
    {
        // The PDQ's arm takes 30 off the sentry's price
        // (tf_player_shared.cpp:11531-11536): a hundred pays for a mini.
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = AGunslinger(rig, metal: 100);

        var outcome = rig.Builder().Build(builder, TFObjectType.Sentrygun, 0, Vector.Zero, Vector.Zero);

        Assert.Equal(TFCanBuildResult.CanBuild, outcome.Result);
        var paid = Assert.Single(rig.Purse.Paid);
        Assert.Equal(100, paid.Count);

        // The plain wrench pays the row's 130, and 100 is short of it.
        var plain = new BuildingsRig();
        plain.StubWorld();
        EntityHandle engy = plain.AddBuilder();
        plain.Purse.Pockets[TFAmmoNames.Name(TFAmmo.Metal)] = 100;
        Assert.Equal(TFCanBuildResult.NeedResources,
                     plain.Builder().Build(engy, TFObjectType.Sentrygun, 0, Vector.Zero, Vector.Zero).Result);
    }

    [Fact]
    public void AMiniSentryBuildsInStocksTime()
    {
        // GetTotalTime has no mini arm (tf_obj.cpp:857-874): the row's ten
        // seconds stand for a mini as for any sentry.
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = AGunslinger(rig);
        var sentry = (TFSentryGun)rig.Builder()
                                     .Build(builder, TFObjectType.Sentrygun, 0, Vector.Zero, Vector.Zero)
                                     .Building!;

        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime / 2f;
        sentry.OnThink();
        Assert.True(sentry.IsBuilding);            // halfway: still assembling

        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime / 2f;
        sentry.OnThink();
        Assert.False(sentry.IsBuilding);           // the row's full time stands
        Assert.Equal(100, sentry.Health);          // the ramp reaches the mini's hundred (:2124)
    }

    [Fact]
    public void AStockWrenchBuildsALevelOneSentry()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = rig.AddBuilder();
        rig.Purse.Pockets[TFAmmoNames.Name(TFAmmo.Metal)] = 500;

        var outcome = rig.Builder().Build(builder, TFObjectType.Sentrygun, 0, Vector.Zero, Vector.Zero);

        Assert.Equal(TFCanBuildResult.CanBuild, outcome.Result);
        var sentry = (TFSentryGun)outcome.Building!;
        Assert.False(sentry.IsMiniBuilding);
        var paid = Assert.Single(rig.Purse.Paid);
        Assert.Equal(TFObjectInfo.Of(TFObjectType.Sentrygun)!.Cost, paid.Count);   // the row's metal, no discount

        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        sentry.OnThink();
        Assert.Equal(1, sentry.Level);
        Assert.Equal(TFBaseObject.ObjectMaxHealth, sentry.Health);                 // full hundred and fifty
    }

    [Fact]
    public void AMiniShootsMiniNumbers()
    {
        // The mini's shells: the mini's damage and the quarter-slower beat
        // (tf_obj_sentrygun.cpp:1542-1546, :1278-1281).
        var (rig, _, sentry) = ACompletedMini();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(enemy, 300f);
        rig.FillsSphere(enemy);

        sentry.OnThink("SentryThink");           // the find
        rig.Clock.Now += 0.3f;
        sentry.OnThink("SentryThink");           // aimed: the first shell
        rig.Clock.Now += 0.18f;                  // inside the plain 0.2 beat, past the mini's 0.15
        sentry.OnThink("SentryThink");

        EngineCall[] shells = rig.Engine.AllOf("Damage.TakeDamage").ToArray();
        Assert.Equal(2, shells.Length);          // the second landed inside the shorter beat
        Assert.Equal(rig.Convars.Declared<TFSentryGunMiniDamageConVar>().Value, shells[0].ArgAs<float>(2));
        Assert.NotEqual(rig.Convars.Declared<TFSentryGunDamageConVar>().Value, shells[0].ArgAs<float>(2));
    }

    [Fact]
    public void TheMiniScalesToThreeQuartersAndWearsItsBuildersTeamSkin()
    {
        // MakeMiniBuilding's scale arm (tf_obj_sentrygun.cpp:373, :2146-2150) and
        // StartPlacement's tail for a mini (tf_obj.cpp:1469-1473): red's second skin,
        // blu's third — both through the builder's real Build path.
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = AGunslinger(rig);
        var sentry = (TFSentryGun)rig.Builder()
                                     .Build(builder, TFObjectType.Sentrygun, 0, Vector.Zero, Vector.Zero)
                                     .Building!;
        Assert.True(sentry.IsMiniBuilding);

        var scale = rig.Engine.Last("EntityRender.SetModelScale");
        Assert.NotNull(scale);
        Assert.Equal(TFBaseObject.MiniSentryScale, scale!.Value.ArgAs<float>(1));   // MINI_SENTRY_SCALE

        var skin = rig.Engine.Last("EntityRender.SetSkin");
        Assert.NotNull(skin);
        Assert.Equal(2, skin!.Value.ArgAs<int>(1));                                 // the rig's builder stands red

        // A blu builder's mini wears the third skin (:1472).
        var blu = new BuildingsRig();
        blu.StubWorld();
        EntityHandle bluBuilder = blu.AddBuilder(TFTeam.Blue);
        blu.Owners.Items[bluBuilder.EntryIndex] = blu.AttributeItem(
            "wrench_builds_minisentry", TF2.Shared.Econ.AttributeDescriptionFormat.Additive, 1);
        blu.Purse.Pockets[TFAmmoNames.Name(TFAmmo.Metal)] = 500;
        blu.Builder().Build(bluBuilder, TFObjectType.Sentrygun, 0, Vector.Zero, Vector.Zero);

        var bluSkin = blu.Engine.Last("EntityRender.SetSkin");
        Assert.NotNull(bluSkin);
        Assert.Equal(3, bluSkin!.Value.ArgAs<int>(1));
    }

    [Fact]
    public void TheMiniTurnsThirtyFiveHundredthsQuicker()
    {
        // MoveTurret's mini arm (tf_obj_sentrygun.cpp:1825-1830): the pitch step of
        // one think is the base rate's five, lifted — 2.025 where the plain turret's
        // downhill fact reads 1.5 (TheAimReadsADownhillTargetAsStocksPitch).
        // Completed first, as the plain downhill fact's sentry is: the first
        // think of an unfinished build is the construction tick, not the aim.
        var (rig, _, sentry) = ACompletedMini();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = new Vector(300f, 0f, TFSentryGun.EyeHeightForLevel(1) - 40f);   // 40 below the eye
        rig.FillsSphere(enemy);

        rig.Clock.Now += TFSentryGun.SentryThinkDelay;
        sentry.OnThink("SentryThink");

        Assert.Equal(30f * TFSentryGun.MiniTurnRateMultiplier * TFSentryGun.SentryThinkDelay,
                     sentry.Pitch, 2);                                             // 2.025: the lifted step
    }

    [Fact]
    public void TheMiniSoundsItsOwnReportAtPitchesHigh()
    {
        // The mini's fire branch (:1569-1577): its own sound at the script's pitch;
        // and once the hopper runs dry, EmitSentrySound's mini arm (:2214-2219)
        // rides the empty click up to PITCH_HIGH. Through the real Fire path.
        var (rig, _, sentry) = ACompletedMini();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(enemy, 300f);
        rig.FillsSphere(enemy);

        sentry.OnThink("SentryThink");             // the find
        rig.Clock.Now += 0.3f;
        sentry.OnThink("SentryThink");             // the first shell: its report

        var report = rig.Engine.Last("Sound.EntityEmitSound");
        Assert.NotNull(report);
        Assert.Equal(TFSentryGun.MiniFireSound, report!.Value.ArgAs<string>(1));

        // The hopper runs dry at the mini's beat; one beat past the last shell
        // the empty click sounds — at PITCH_HIGH (:2214-2219).
        int guard = 0;
        while (sentry.Shells > 0 && guard++ < 400)
        {
            rig.Clock.Now += TFSentryGun.ShellIntervalLevel1 * TFSentryGun.MiniFireRateMultiplier;
            sentry.OnThink("SentryThink");
        }

        rig.Clock.Now += TFSentryGun.ShellIntervalLevel1 * TFSentryGun.MiniFireRateMultiplier;
        sentry.OnThink("SentryThink");

        var empty = rig.Engine.Last("Sound.EntityEmitSoundAs");
        Assert.NotNull(empty);
        Assert.Equal(TFSentryGun.EmptySoundName, empty!.Value.ArgAs<string>(1));
        Assert.Equal(TFSentryGun.PitchHigh, empty!.Value.ArgAs<int>(3));
    }

    [Fact]
    public void ThePlainSentryFiresItsOwnLevelReportAndPlainClick()
    {
        // The non-mini branch (:1579-1596): the level's named sound, and the
        // empty click at the script's own pitch (:2227-2242).
        var (rig, sentry, _) = AWorkingSentry();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(enemy, 300f);
        rig.FillsSphere(enemy);

        sentry.OnThink("SentryThink");             // the find
        rig.Clock.Now += 0.3f;
        sentry.OnThink("SentryThink");             // the first shell

        var report = rig.Engine.Last("Sound.EntityEmitSound");
        Assert.NotNull(report);
        Assert.Equal(TFSentryGun.FireSoundLevel1, report!.Value.ArgAs<string>(1));

        int guard = 0;
        while (sentry.Shells > 0 && guard++ < 400)
        {
            rig.Clock.Now += TFSentryGun.ShellIntervalLevel1;
            sentry.OnThink("SentryThink");
        }

        rig.Clock.Now += TFSentryGun.ShellIntervalLevel1;
        sentry.OnThink("SentryThink");

        var empty = rig.Engine.Last("Sound.EntityEmitSound");
        Assert.NotNull(empty);
        Assert.Equal(TFSentryGun.EmptySoundName, empty!.Value.ArgAs<string>(1));
    }

    /// <summary>The sentry-death pay the Justice's item carries, one defindex a fact.</summary>
    private static int _defindex = 4242;

    /// <summary>The builder's player-data carrier, the purse the pay lands in.</summary>
    private static void AttachCarrier(BuildingsRig rig, EntityHandle player)
    {
        var carrierHandle = rig.NewHandle();
        rig.Engine.Returns("Entities.Create", carrierHandle)
                  .ReturnsEachCall("Entities.GetIndex", () => player.EntryIndex);
        rig.Engine.WhenCalled("Entities.Spawn",
                              c => rig.Registry.Spawn(c.ArgAs<EntityHandle>(0), TFEntityNames.PlayerData));
        new TFPlayerCarrier(rig.Engine, rig.Registry, rig.Carriers, rig.Log).Attach(player);
    }

    private static TF2.Shared.Econ.IAttributeProvider PayingItem()
    {
        var item = new TF2.Shared.Econ.AttributeContainer(null, TF2.Shared.Econ.AttributeProviderType.Generic);
        item.Set(TF2.Shared.Econ.AttributeValue.OfFloat(
            new TF2.Shared.Econ.AttributeDefinition(_defindex++, "attr " + _defindex, "sentry_killed_revenge",
                                                    TF2.Shared.Econ.AttributeDescriptionFormat.Additive,
                                                    TF2.Shared.Econ.AttributeEffectType.Neutral,
                                                    false, false, TF2.Shared.Econ.AttributeValueType.Default,
                                                    TF2.Shared.Econ.MergedKeys.Block("x")), 1f));
        return item;
    }

    /// <summary>
    /// <c>CObjectSentrygun::Killed</c>'s tail (tf_obj_sentrygun.cpp:2055-2064):
    /// the death pays the builder's Justice holder <c>Kills * 2 + Assists</c>,
    /// through the real kill (<see cref="BuildingsRig.Hit"/>'s death call).
    /// </summary>
    [Fact]
    public void TheDeathPaysTheBuildersJusticeForItsKills()
    {
        var (rig, sentry, builder) = AWorkingSentry();
        AttachCarrier(rig, builder);
        rig.Owners.ByName[builder.EntryIndex] = new()
        {
            [TF2.Server.Player.TFRevengeCrits.ShotgunScript] = PayingItem(),
        };
        sentry.IncrementKills();
        sentry.IncrementKills();
        sentry.IncrementAssists();
        Assert.Equal(0, rig.Revenge.Of(builder.EntryIndex));   // the grant is the death's, not the kills'

        BuildingsRig.Hit(sentry, EntityHandle.None, 5000f, DamageType.Bullet);

        Assert.Equal(5, rig.Revenge.Of(builder.EntryIndex));   // 2 kills * 2 + 1 assist
    }

    /// <summary>The pay is the held Justice's: no <c>sentry_killed_revenge</c> item, no grant.</summary>
    [Fact]
    public void TheDeathPaysNothingWithoutTheJusticesItem()
    {
        var (rig, sentry, builder) = AWorkingSentry();
        AttachCarrier(rig, builder);
        sentry.IncrementKills();

        BuildingsRig.Hit(sentry, EntityHandle.None, 5000f, DamageType.Bullet);

        Assert.Equal(0, rig.Revenge.Of(builder.EntryIndex));
    }
}

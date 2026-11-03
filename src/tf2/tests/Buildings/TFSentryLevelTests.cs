using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Buildings;
using TF2.Shared;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The turret above level 1: the twin guns' beat, the pod's rockets, the eye
/// that climbs, and the wrench's refill (tf_obj_sentrygun.cpp:540-702).
/// </summary>
public class TFSentryLevelTests
{
    private static string MetalName => TFAmmoNames.Name(TFAmmo.Metal);

    /// <summary>A standing sentry raised to <paramref name="level"/>, its build just finished at t=100.</summary>
    private static (BuildingsRig Rig, TFSentryGun Sentry, EntityHandle Builder) ASentryAtLevel(int level)
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

        float duration = TFObjectInfo.Of(TFObjectType.Sentrygun)!.UpgradeDuration;
        rig.Purse.Pockets[MetalName] = 200 * level;
        for (int l = 2; l <= level; l++)
        {
            for (int swing = 0; swing < 8; swing++)   // 200 metal at 25 the swing
                sentry.WrenchHit(builder);
            rig.Clock.Now += duration;
            sentry.OnThink();
        }

        return (rig, sentry, builder);
    }

    private static Vector Ahead(float x) => new(x, 0f, TFSentryGun.EyeHeightForLevel(1));

    [Fact]
    public void TheUpgradeStocksTheLevelHopper()
    {
        var (rig, sentry, _) = ASentryAtLevel(2);

        Assert.Equal(TFSentryGun.MaxShellsLevel2, sentry.Shells);
    }

    [Fact]
    public void TheThirdLevelStocksThePod()
    {
        var (rig, sentry, _) = ASentryAtLevel(3);

        Assert.Equal(TFSentryGun.MaxRocketsStored, sentry.Rockets);
        Assert.Equal(TFSentryGun.MaxShellsLevel3, sentry.Shells);
    }

    [Fact]
    public void TheTwinGunsFireTwiceASecond()
    {
        var (rig, sentry, _) = ASentryAtLevel(2);
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(300f);
        rig.FillsSphere(enemy);

        sentry.OnThink("SentryThink");           // the find
        rig.Clock.Now += 0.3f;
        sentry.OnThink("SentryThink");           // the first shell
        rig.Clock.Now += 0.1f;
        sentry.OnThink("SentryThink");           // the twin's, a tenth later

        // Level 1's beat is 0.2 s; the second shell names the faster hopper.
        Assert.Equal(2, rig.Engine.AllOf("Damage.TakeDamage").Count());
        Assert.Equal(TFSentryGun.MaxShellsLevel2 - 2, sentry.Shells);
    }

    [Fact]
    public void TheEyeClimbsWithTheLevel()
    {
        // SENTRYGUN_EYE_OFFSET_LEVEL_1/2/3 (tf_shareddefs.h:1351-1353).
        Assert.Equal(32f, TFSentryGun.EyeHeightForLevel(1));
        Assert.Equal(40f, TFSentryGun.EyeHeightForLevel(2));
        Assert.Equal(46f, TFSentryGun.EyeHeightForLevel(3));
    }

    [Fact]
    public void TheLevelThreeSentryFiresARocketAtItsTarget()
    {
        var (rig, sentry, builder) = ASentryAtLevel(3);
        var rocket = rig.Spawn<TFSentryRocket>(TFSentryRocket.MapClass);
        rig.Engine.Returns("Entities.Create", rocket.Handle);
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(300f);
        rig.FillsSphere(enemy);

        sentry.OnThink("SentryThink");           // the find
        rig.Clock.Now += 0.3f;
        sentry.OnThink("SentryThink");           // the beat: a rocket, then a shell

        EngineCall launched = rig.Engine.AllOf("Entities.SetVelocity")
                                     .Last(c => c.ArgAs<EntityHandle>(0) == rocket.Handle);
        Vector aim = rig.Centers[enemy] - new Vector(0f, 0f, TFSentryGun.EyeHeightForLevel(3));
        Assert.Equal(aim.Normalized * TFSentryRocket.Speed, launched.ArgAs<Vector>(1));
        Assert.Equal(TFSentryGun.MaxRocketsStored - 1, sentry.Rockets);
        Assert.Equal(sentry.Handle, rocket.Launcher);   // the kill rules' owner arm asks the rocket
        Assert.Single(rig.Engine.AllOf("Damage.TakeDamage"));   // the shell follows
    }

    [Fact]
    public void ThePodWontFireThroughAWall()
    {
        var (rig, sentry, builder) = ASentryAtLevel(3);
        var rocket = rig.Spawn<TFSentryRocket>(TFSentryRocket.MapClass);
        rig.Engine.Returns("Entities.Create", rocket.Handle);
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(300f);
        rig.BlockedEnds.Add(rig.Centers[enemy]);   // the pod's line is stopped (MASK_SOLID, :1347-1349)
        rig.FillsSphere(enemy);

        sentry.OnThink("SentryThink");
        rig.Clock.Now += 0.3f;
        sentry.OnThink("SentryThink");

        Assert.DoesNotContain(rig.Engine.AllOf("Entities.SetVelocity"),
                               c => c.ArgAs<EntityHandle>(0) == rocket.Handle);
        Assert.Equal(TFSentryGun.MaxRocketsStored, sentry.Rockets);
    }

    [Fact]
    public void TheWrenchFeedsTheHopperAtAMetalTheShell()
    {
        var (rig, sentry, builder) = ASentryAtLevel(3);   // the ceiling: a swing repairs nothing to pour
        var rocket = rig.Spawn<TFSentryRocket>(TFSentryRocket.MapClass);   // the pod's shot rides along
        rig.Engine.Returns("Entities.Create", rocket.Handle);
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(300f);
        rig.FillsSphere(enemy);
        sentry.OnThink("SentryThink");
        rig.Clock.Now += 0.3f;
        sentry.OnThink("SentryThink");           // 199 shells
        rig.Purse.Pockets[MetalName] = 200;

        sentry.WrenchHit(builder);

        // 40 shells a swing at one metal the shell (SENTRYGUN_ADD_SHELLS,
        // tf_sentrygun_metal_per_shell), clipped to the level's max — one
        // shell was missing, and the pod's one more took two.
        Assert.Equal(TFSentryGun.MaxShellsLevel3, sentry.Shells);
        Assert.Equal(197, rig.Purse.Pockets[MetalName]);
    }

    [Fact]
    public void TheWrenchFeedsThePodAtTwoMetalTheRocket()
    {
        var (rig, sentry, builder) = ASentryAtLevel(3);
        var rocket = rig.Spawn<TFSentryRocket>(TFSentryRocket.MapClass);
        rig.Engine.Returns("Entities.Create", rocket.Handle);
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = Ahead(300f);
        rig.FillsSphere(enemy);
        sentry.OnThink("SentryThink");           // the find
        rig.Clock.Now += 0.3f;
        sentry.OnThink("SentryThink");           // 19 rockets and 199 shells
        Assert.Equal(TFSentryGun.MaxRocketsStored - 1, sentry.Rockets);
        rig.Purse.Pockets[MetalName] = 4;

        sentry.WrenchHit(builder);

        // Stock's order — shells first (tf_obj_sentrygun.cpp:653-675), then
        // the pod (:680-701): the missing shell takes one metal and the pod's
        // missing rocket two (tf_sentrygun_metal_per_rocket).
        Assert.Equal(TFSentryGun.MaxShellsLevel3, sentry.Shells);
        Assert.Equal(TFSentryGun.MaxRocketsStored, sentry.Rockets);
        Assert.Equal(1, rig.Purse.Pockets[MetalName]);
    }
}

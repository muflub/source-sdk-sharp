using SourceSharp.Abi;
using SourceSharp.Shared;
using TF2.Server.Buildings;
using TF2.Shared;
using TF2.Tests.Entities.Items;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The pair's recharge above level 1: g_iTeleporterRechargeTimes indexed by
/// GetUpgradeLevel() (tf_obj_teleporter.cpp:41-47, :1109, :1168) — ten, five
/// and three seconds.
/// </summary>
public class TFTeleporterLevelTests
{
    private static string MetalName => TFAmmoNames.Name(TFAmmo.Metal);

    [Fact]
    public void TheRechargeTableIsTensFiveAndThree()
    {
        Assert.Equal(10f, TFTeleporter.RechargeTimeForLevel(1));
        Assert.Equal(5f, TFTeleporter.RechargeTimeForLevel(2));
        Assert.Equal(3f, TFTeleporter.RechargeTimeForLevel(3));
    }

    [Fact]
    public void TheLevelTwoEntranceRechargesFiveSeconds()
    {
        var (rig, entrance, exit, builder) = APair();
        UpgradeTo(rig, entrance, builder, 2);
        EntityHandle rider = AStandingRider(rig);

        entrance.OnTouch(rider);
        TickBoth(rig, entrance, exit, 0.35f);   // the send's beat and the exit's fade-in

        Assert.Equal(TFTeleporterState.Recharging, entrance.State);
        TickBoth(rig, entrance, exit, 5.3f);    // a tenth short of the five and its fades
        Assert.Equal(TFTeleporterState.Recharging, entrance.State);
        TickBoth(rig, entrance, exit, 0.4f);
        Assert.Equal(TFTeleporterState.Ready, entrance.State);
    }

    [Fact]
    public void TheLevelThreeExitRechargesThreeSeconds()
    {
        var (rig, entrance, exit, builder) = APair();
        UpgradeTo(rig, exit, builder, 3);
        EntityHandle rider = AStandingRider(rig);

        entrance.OnTouch(rider);
        TickBoth(rig, entrance, exit, 0.35f);   // the send lands: the exit fades in
        TickBoth(rig, entrance, exit, 0.5f);    // the arrival and the release's fade

        Assert.Equal(TFTeleporterState.Recharging, exit.State);
        TickBoth(rig, entrance, exit, 2.8f);    // a fifth short of the three
        Assert.Equal(TFTeleporterState.Recharging, exit.State);
        TickBoth(rig, entrance, exit, 0.4f);
        Assert.Equal(TFTeleporterState.Ready, exit.State);
    }

    /// <summary>One engineer's entrance and exit, both built and thinking at t=120.</summary>
    private static (BuildingsRig Rig, TFTeleporter Entrance, TFTeleporter Exit, EntityHandle Builder) APair()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = rig.AddBuilder();
        var entrance = rig.Spawn<TFTeleporter>(TFTeleporter.MapClass);
        var exit = rig.Spawn<TFTeleporter>(TFTeleporter.MapClass);

        foreach (var (end, mode) in new[] { (entrance, 0), (exit, 1) })
        {
            end.StartPlacement(builder);
            end.SetObjectMode(mode);
            end.StartBuilding();
        }

        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Teleporter)!.BuildTime;
        entrance.OnThink();   // the base think's last tick for each end
        exit.OnThink();

        return (rig, entrance, exit, builder);
    }

    /// <summary><paramref name="end"/> wrenched up to <paramref name="level"/>, the clock run past each upgrade.</summary>
    private static void UpgradeTo(BuildingsRig rig, TFTeleporter end, EntityHandle builder, int level)
    {
        rig.Purse.Pockets[MetalName] = 400;
        float duration = TFObjectInfo.Of(TFObjectType.Teleporter)!.UpgradeDuration;
        for (int l = 2; l <= level; l++)
        {
            for (int swing = 0; swing < 4; swing++)   // 100 metal at 25 the swing
                end.WrenchHit(builder);
            rig.Clock.Now += duration;
            end.OnThink();
        }
    }

    /// <summary>The pair thinks together at its own fast beat, the clock landed exactly at the end.</summary>
    private static void TickBoth(BuildingsRig rig, TFTeleporter entrance, TFTeleporter exit, float seconds)
    {
        int steps = (int)(seconds / TFTeleporter.TeleporterThinkDelay + 0.5f);
        float start = rig.Clock.Now, end = start + seconds;
        for (int step = 1; step <= steps; step++)
        {
            rig.Clock.Now = step == steps ? end : start + step * TFTeleporter.TeleporterThinkDelay;
            entrance.OnThink("TeleporterThink");
            exit.OnThink("TeleporterThink");
        }
    }

    private static EntityHandle AStandingRider(BuildingsRig rig)
    {
        (EntityHandle handle, _) = rig.Players.Add(TFTeam.Red);
        rig.Engine.Returns("Entities.GetVelocity", Vector.Zero);
        return handle;
    }
}

using SourceSharp.Abi;
using TF2.Server.Buildings;
using TF2.Shared;
using TF2.Shared.Econ;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The sapped building's upgrade back (<c>CBaseObject::DoReverseBuild</c>,
/// tf_obj.cpp:3723-3749, its think arm :617-639 and the finish's cascade
/// :3076-3081): while a recorder sapper rides, every finished upgrade steps
/// the level down, until the first level is building again.
/// </summary>
public class TFReverseBuildTests
{
    /// <summary>A red engineer's sentry-table building raised to <paramref name="level"/>,
    /// then the blue spy's recorder sapper planted on it — attached and draining. The
    /// sapper's held item degenerates at <paramref name="speed"/>, the recorder's hook.</summary>
    private static (BuildingsRig Rig, TestBuilding Building, TFSapper Sapper) ARecordedBuilding(int level, float speed = 1f)
    {
        var rig = new BuildingsRig();
        EntityHandle engineer = rig.AddBuilder(TFTeam.Red);
        var building = rig.Spawn<TestBuilding>(TestBuilding.MapClass);
        building.StartPlacement(engineer);
        rig.Clock.Now = 100f;
        building.StartBuilding();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        building.OnThink();   // level 1, built

        for (int climbed = 1; climbed < level; climbed++)
        {
            building.StartUpgrading();
            rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.UpgradeDuration;
            building.OnThink();   // the level stands
        }

        EntityHandle spy = rig.AddBuilder(TFTeam.Blue, TFClass.Spy);
        rig.Owners.Held[0] = "tf_weapon_builder";
        rig.Owners.Items[0] = rig.AttributeItem("sapper_degenerates_buildings", AttributeDescriptionFormat.Additive, speed);

        var sapper = rig.Spawn<TFSapper>(TFSapper.MapClass);
        sapper.StartPlacement(spy);
        sapper.AttachTo(building);
        sapper.StartBuilding();
        sapper.OnThink();     // the instant build: attached

        return (rig, building, sapper);
    }

    [Fact]
    public void ASappedBuildingsThinkUnwindsInsteadOfStanding()
    {
        var (rig, building, _) = ARecordedBuilding(2);

        rig.Clock.Now += 0.1f;
        building.OnThink();

        // DoReverseBuild's climb arm: a level lower and upgrading again, the
        // level's metal pot emptied on the way.
        Assert.True(building.IsUpgrading);
        Assert.Equal(0, building.UpgradeMetal);
    }

    [Fact]
    public void TheUnwindsFinishedUpgradeLandsTheLevelBelow()
    {
        var (rig, building, _) = ARecordedBuilding(2);
        float duration = TFObjectInfo.Of(TFObjectType.Sentrygun)!.UpgradeDuration;

        rig.Clock.Now += 0.1f;
        building.OnThink();   // the unwind's climb
        rig.Clock.Now += duration;
        building.OnThink();   // finished: the cascade steps down

        // The level below's table is the cap the cascade leaves: 180 to 150.
        Assert.Equal(1, building.Level);
        Assert.Equal(150, building.Health);
        Assert.True(building.IsBuilding);
    }

    [Fact]
    public void TheUnwoundFirstLevelBuildsAgain()
    {
        var (rig, building, _) = ARecordedBuilding(1);

        rig.Clock.Now += 0.1f;
        building.OnThink();

        Assert.True(building.IsBuilding);
        Assert.False(building.IsBuilt);

        // The first level's build runs its time and stands again — the unwound
        // building is a building, not a husk.
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        building.OnThink();

        Assert.True(building.IsBuilt);
        Assert.Equal(1, building.Level);
    }

    /// <summary>The recorder's speed is a speed: the unwinding upgrade's clock is the
    /// table's divided by it (tf_obj.cpp:3044-3048), so a doubler unwinds at half time.</summary>
    [Fact]
    public void TheRecordersSpeedDividesTheUnwindsClock()
    {
        var (rig, building, _) = ARecordedBuilding(2, speed: 2f);
        float duration = TFObjectInfo.Of(TFObjectType.Sentrygun)!.UpgradeDuration;

        building.OnThink();   // the climb: the clock wound to half
        rig.Clock.Now += duration / 2f;
        building.OnThink();

        // Half the table's time and the level has stepped; at the plain clock
        // the upgrade would still be running.
        Assert.Equal(1, building.Level);
        Assert.True(building.IsBuilding);
    }
}

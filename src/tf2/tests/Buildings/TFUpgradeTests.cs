using SourceSharp.Abi;
using SourceSharp.Server;
using TF2.Server.Buildings;
using TF2.Shared;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The wrench's second trade — metal into levels (the upgrade flow of
/// tf_obj.cpp:2846-3081) and metal into wounds (Command_Repair).
/// </summary>
public class TFUpgradeTests
{
    private static string MetalName => TFAmmoNames.Name(TFAmmo.Metal);

    private static (BuildingsRig Rig, TestBuilding Building, EntityHandle Builder) AWorkingBuilding(float at = 100f)
    {
        var rig = new BuildingsRig();
        var builder = rig.AddBuilder();
        var building = rig.Spawn<TestBuilding>(TestBuilding.MapClass);
        building.StartPlacement(builder);
        rig.Clock.Now = at;
        building.StartBuilding();
        rig.Clock.Now = at + TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        building.OnThink();
        return (rig, building, builder);
    }

    private static void GiveMetal(BuildingsRig rig, int metal) => rig.Purse.Pockets[MetalName] = metal;

    // ---- the repair --------------------------------------------------------

    [Fact]
    public void AWoundedBuildingIsRepairedAtThreeHealthTheMetal()
    {
        var (rig, building, builder) = AWorkingBuilding();
        var enemy = rig.AddBuilder(TFTeam.Blue);
        BuildingsRig.Hit(building, enemy, 60f, DamageType.Bullet);   // 150 → 90
        GiveMetal(rig, 10);

        building.WrenchHit(builder);

        // The pocket holds 10 metal for 30 health at the ratio of three.
        Assert.Equal(120, building.Health);
        Assert.Equal(0, rig.Purse.Pockets[MetalName]);
    }

    [Fact]
    public void ARepairIsClippedToTheWoundNotTheSwing()
    {
        var (rig, building, builder) = AWorkingBuilding();
        var enemy = rig.AddBuilder(TFTeam.Blue);
        BuildingsRig.Hit(building, enemy, 60f, DamageType.Bullet);   // 90 of 150
        GiveMetal(rig, 100);

        building.WrenchHit(builder);

        // The wound asks 60 health (20 metal), not the swing's 100.
        Assert.Equal(150, building.Health);
        Assert.Equal(80, rig.Purse.Pockets[MetalName]);
    }

    [Fact]
    public void APocketTooLightRepairsWhatItCan()
    {
        var (rig, building, builder) = AWorkingBuilding();
        var enemy = rig.AddBuilder(TFTeam.Blue);
        BuildingsRig.Hit(building, enemy, 100f, DamageType.Bullet);  // 50 of 150
        GiveMetal(rig, 9);

        building.WrenchHit(builder);

        // 9 metal at the ratio is 27 health — what the pocket could afford.
        Assert.Equal(77, building.Health);
    }

    // ---- the order: heal first, then raise ---------------------------------

    [Fact]
    public void AWoundedBuildingIsHealedBeforeItIsRaised()
    {
        var (rig, building, builder) = AWorkingBuilding();
        var enemy = rig.AddBuilder(TFTeam.Blue);
        BuildingsRig.Hit(building, enemy, 60f, DamageType.Bullet);
        GiveMetal(rig, 200);

        building.WrenchHit(builder);

        // The swing repaired, so it asked no upgrade (tf_obj.cpp:2807-2822).
        Assert.Equal(1, building.Level);
        Assert.False(building.IsUpgrading);
    }

    [Fact]
    public void ASwingAtFullHealthPoursMetalIntoTheUpgrade()
    {
        var (rig, building, builder) = AWorkingBuilding();
        GiveMetal(rig, 200);

        building.WrenchHit(builder);

        Assert.Equal(25, building.UpgradeMetal);
        Assert.Equal(175, rig.Purse.Pockets[MetalName]);
        Assert.Equal(1, building.Level);
    }

    // ---- the upgrade -------------------------------------------------------

    [Fact]
    public void TheLevelsCostMetTheLevelClimbsAndTheMetalIsSpent()
    {
        var (rig, building, builder) = AWorkingBuilding();
        GiveMetal(rig, 200);

        for (int swing = 0; swing < 8; swing++)   // 200 metal at 25 the swing
            building.WrenchHit(builder);

        Assert.Equal(2, building.Level);
        Assert.Equal(0, rig.Purse.Pockets[MetalName]);
        Assert.True(building.IsUpgrading);
        Assert.Equal(180, building.Health);       // the new level's table (150 × 1.2)
    }

    [Fact]
    public void TheUpgradeTakesItsTimeAndThenWearsTheLevelModel()
    {
        var (rig, building, builder) = AWorkingBuilding();
        GiveMetal(rig, 200);
        for (int swing = 0; swing < 8; swing++)
            building.WrenchHit(builder);

        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.UpgradeDuration - 0.1f;
        building.OnThink();
        Assert.True(building.IsUpgrading);

        rig.Clock.Now += 0.1f;
        building.OnThink();

        Assert.False(building.IsUpgrading);
        Assert.Equal(2, building.Level);
        Assert.Equal(TFObjectModels.Finished(TFObjectType.Sentrygun, 2), LastModelWorn(rig, building));
    }

    [Fact]
    public void TheUpgradeWearsTheUpgradeBodyAndPlaysTheSequence()
    {
        var rig = new BuildingsRig();
        rig.Engine.Returns("EntityRender.LookupSequenceByActivity", 9);
        var builder = rig.AddBuilder();
        var building = rig.Spawn<TestBuilding>(TestBuilding.MapClass);
        building.StartPlacement(builder);
        rig.Clock.Now = 100f;
        building.StartBuilding();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        building.OnThink();
        GiveMetal(rig, 200);
        for (int swing = 0; swing < 8; swing++)
            building.WrenchHit(builder);

        Assert.Equal(TFObjectModels.Upgrading(TFObjectType.Sentrygun, 2), LastModelWorn(rig, building));
        EngineCall asked = rig.Engine.AllOf("EntityRender.LookupSequenceByActivity")
                                    .Last(c => c.ArgAs<EntityHandle>(0) == building.Handle);
        Assert.Equal("ACT_OBJ_UPGRADING", asked.ArgAs<string>(1));
    }

    [Fact]
    public void AShortPocketNeedsMoreSwings()
    {
        var (rig, building, builder) = AWorkingBuilding();
        GiveMetal(rig, 30);

        building.WrenchHit(builder);
        Assert.Equal(1, building.Level);          // 25 of 200 poured

        GiveMetal(rig, 200);
        for (int swing = 0; swing < 7; swing++)   // 5 left over from the first, then 150 more
            building.WrenchHit(builder);

        Assert.Equal(2, building.Level);
    }

    [Fact]
    public void OnlyTheEngineerRaisesTheLevel()
    {
        var (rig, building, builder) = AWorkingBuilding();
        GiveMetal(rig, 200);
        var spy = rig.AddBuilder(TFTeam.Red, TFClass.Spy);

        building.WrenchHit(spy);

        Assert.Equal(1, building.Level);
        Assert.Equal(200, rig.Purse.Pockets[MetalName]);
    }

    [Fact]
    public void TheThirdLevelIsTheCeiling()
    {
        var (rig, building, builder) = AWorkingBuilding();
        GiveMetal(rig, 600);
        for (int swing = 0; swing < 8; swing++)   // → 2
            building.WrenchHit(builder);
        building.OnThink();                        // play the clock out at once
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.UpgradeDuration;
        building.OnThink();

        for (int swing = 0; swing < 8; swing++)   // → 3
            building.WrenchHit(builder);
        Assert.Equal(3, building.Level);
        building.OnThink();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.UpgradeDuration;
        building.OnThink();

        Assert.False(building.WrenchHit(builder));
        Assert.Equal(3, building.Level);
        Assert.Equal(200, rig.Purse.Pockets[MetalName]);   // nothing was asked of the pocket
    }

    [Fact]
    public void TheUpgradeMetalPotIsClearedForTheNextLevel()
    {
        var (rig, building, builder) = AWorkingBuilding();
        GiveMetal(rig, 600);
        for (int swing = 0; swing < 8; swing++)
            building.WrenchHit(builder);
        building.OnThink();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.UpgradeDuration;
        building.OnThink();

        building.WrenchHit(builder);

        // The pot the last level filled went with it; this is the next one's.
        Assert.Equal(2, building.Level);
        Assert.Equal(25, building.UpgradeMetal);
    }

    private static string LastModelWorn(BuildingsRig rig, ScriptedEntity building)
        => rig.Engine.AllOf("EntityRender.SetModel")
                     .Where(c => c.ArgAs<EntityHandle>(0) == building.Handle)
                     .Last()
                     .ArgAs<string>(1);
}

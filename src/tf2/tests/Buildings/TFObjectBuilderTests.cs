using SourceSharp.Abi;
using TF2.Server.Buildings;
using TF2.Server.Entities.Items;
using TF2.Shared;
using TF2.Tests.Entities.Items;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>The gate and the birth: who may raise what, where, and what it costs.</summary>
public class TFObjectBuilderTests
{
    /// <summary>A desk for the builder: the rig's fakes funded with <paramref name="metal"/> of scrap.</summary>
    private static (BuildingsRig Rig, TFObjectBuilder Builder, EntityHandle Engineer) ADesk(
        TFClass playerClass = TFClass.Engineer, int metal = 500)
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle engineer = rig.AddBuilder(TFTeam.Red, playerClass);
        rig.Purse.Pockets[TFAmmoNames.Name(TFAmmo.Metal)] = metal;
        return (rig, rig.Builder(), engineer);
    }

    private static readonly Vector Spot = new(100f, 0f, 0f);

    [Fact]
    public void TheEngineerWithScrapCanBuild()
    {
        var (_, builder, engineer) = ADesk();

        Assert.Equal(TFCanBuildResult.CanBuild, builder.CanBuild(engineer, TFObjectType.Sentrygun, 0));
    }

    [Fact]
    public void ASoldierCannotBuild()
    {
        var (_, builder, soldier) = ADesk(TFClass.Soldier);

        Assert.Equal(TFCanBuildResult.CannotBuild, builder.CanBuild(soldier, TFObjectType.Sentrygun, 0));
    }

    [Fact]
    public void TheSapperIsNoEngineersBuilding()
    {
        var (_, builder, engineer) = ADesk();

        Assert.Equal(TFCanBuildResult.UnknownObject, builder.CanBuild(engineer, TFObjectType.AttachmentSapper, 0));
    }

    [Fact]
    public void ALevelBeyondTheStockOneIsRefused()
    {
        var (_, builder, engineer) = ADesk();

        Assert.Equal(TFCanBuildResult.CannotBuild, builder.CanBuild(engineer, TFObjectType.Sentrygun, 1));
    }

    [Fact]
    public void TheSecondSentryHitsTheLimit()
    {
        var (rig, builder, engineer) = ADesk();
        Assert.Equal(TFCanBuildResult.CanBuild, builder.Build(engineer, TFObjectType.Sentrygun, 0, Spot, Vector.Zero).Result);

        Assert.Equal(TFCanBuildResult.LimitReached, builder.CanBuild(engineer, TFObjectType.Sentrygun, 0));
    }

    [Fact]
    public void TheTeleporterTakesItsTwoModesUnderOneLimit()
    {
        var (_, builder, engineer) = ADesk();
        Assert.Equal(TFCanBuildResult.CanBuild, builder.Build(engineer, TFObjectType.Teleporter, 0, Spot, Vector.Zero).Result);

        Assert.Equal(TFCanBuildResult.CanBuild, builder.CanBuild(engineer, TFObjectType.Teleporter, 1));
    }

    [Fact]
    public void AShortPurseCannotBuild()
    {
        var (_, builder, engineer) = ADesk(metal: 129);   // the sentry costs 130

        Assert.Equal(TFCanBuildResult.NeedResources, builder.CanBuild(engineer, TFObjectType.Sentrygun, 0));
    }

    [Fact]
    public void ASentryRaisesOutOfTheEngineersScrap()
    {
        var (rig, builder, engineer) = ADesk(metal: 200);

        TFBuildOutcome outcome = builder.Build(engineer, TFObjectType.Sentrygun, 0, Spot, Vector.Zero);

        Assert.Equal(TFCanBuildResult.CanBuild, outcome.Result);
        Assert.IsType<TFSentryGun>(outcome.Building);
        Assert.Equal(70, rig.Purse.Pockets[TFAmmoNames.Name(TFAmmo.Metal)]);
        (EntityHandle payer, int count, string ammo) = Assert.Single(rig.Purse.Paid);
        Assert.Equal(engineer, payer);
        Assert.Equal(130, count);
        Assert.Equal(TFAmmoNames.Name(TFAmmo.Metal), ammo);
    }

    [Fact]
    public void TheBuildingStandsWhereTheBuilderAsked()
    {
        var (rig, builder, engineer) = ADesk();

        TFBuildOutcome outcome = builder.Build(engineer, TFObjectType.Sentrygun, 0, Spot, Vector.Zero);
        Assert.NotNull(outcome.Building);
        Vector placed = rig.Spawner.Placed[outcome.Building!.Handle];

        Assert.Equal(100f, placed.X);
        Assert.Equal(0f, placed.Y);
        Assert.Equal(0f, placed.Z);
    }

    [Fact]
    public void AShortPurseRaisesNothingAndPaysNothing()
    {
        var (rig, builder, engineer) = ADesk(metal: 50);

        TFBuildOutcome outcome = builder.Build(engineer, TFObjectType.Sentrygun, 0, Spot, Vector.Zero);

        Assert.Equal(TFCanBuildResult.NeedResources, outcome.Result);
        Assert.Null(outcome.Building);
        Assert.Empty(rig.Spawner.Placed);
        Assert.Empty(rig.Purse.Paid);
    }

    [Fact]
    public void ANoBuildSpotRefusesAndTakesNoScrap()
    {
        var (rig, builder, engineer) = ADesk();
        rig.NoBuild.Refuses = true;

        TFBuildOutcome outcome = builder.Build(engineer, TFObjectType.Sentrygun, 0, Spot, Vector.Zero);

        Assert.Equal(TFCanBuildResult.InNoBuildArea, outcome.Result);
        Assert.Null(outcome.Building);
        Assert.Single(rig.Spawner.Removed);
        Assert.Empty(rig.Purse.Paid);
    }

    [Fact]
    public void TheNoBuildRefusalSpeaksTheSpotItRefused()
    {
        var (rig, builder, engineer) = ADesk();
        rig.NoBuild.Refuses = true;
        rig.NoBuild.BlockedAt = Vector.Zero;   // the floor point is free: the centre is what the wall reaches

        TFBuildOutcome outcome = builder.Build(engineer, TFObjectType.Sentrygun, 0, Spot, Vector.Zero);

        Assert.Equal(TFCanBuildResult.InNoBuildArea, outcome.Result);
        Assert.Single(rig.Spawner.Removed);
    }

    [Fact]
    public void ARespawnRoomRefusesTheSpot()
    {
        var (rig, builder, engineer) = ADesk();
        rig.Rooms.InRoom = true;

        TFBuildOutcome outcome = builder.Build(engineer, TFObjectType.Sentrygun, 0, Spot, Vector.Zero);

        Assert.Equal(TFCanBuildResult.InRespawnRoom, outcome.Result);
        Assert.Single(rig.Spawner.Removed);
        Assert.Empty(rig.Purse.Paid);
    }

    [Fact]
    public void DestroyTakesTheEngineersOwnSentry()
    {
        var (rig, builder, engineer) = ADesk();
        builder.Build(engineer, TFObjectType.Sentrygun, 0, Spot, Vector.Zero);
        EntityHandle mate = rig.AddBuilder();
        builder.Build(mate, TFObjectType.Sentrygun, 0, new Vector(300f, 0f, 0f), Vector.Zero);

        int destroyed = builder.DestroyAll(engineer, TFObjectType.Sentrygun);

        Assert.Equal(1, destroyed);
        Assert.Empty(rig.Buildings().OwnedBy(engineer, TFObjectType.Sentrygun));
        Assert.Single(rig.Buildings().OwnedBy(mate, TFObjectType.Sentrygun));
        Assert.Contains("destroyed Sentrygun 0", rig.Announcer.Said);
    }

    [Fact]
    public void DestroyingATeleporterTakesBothEnds()
    {
        var (_, builder, engineer) = ADesk();
        builder.Build(engineer, TFObjectType.Teleporter, 0, Spot, Vector.Zero);
        builder.Build(engineer, TFObjectType.Teleporter, 1, new Vector(400f, 0f, 0f), Vector.Zero);

        Assert.Equal(2, builder.DestroyAll(engineer, TFObjectType.Teleporter));
    }
}

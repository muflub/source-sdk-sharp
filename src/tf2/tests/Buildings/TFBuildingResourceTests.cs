using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;

using TF2.Server.Buildings;
using TF2.Shared;
using TF2.Shared.Buildings;

using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The building status carrier's rows (<see cref="TFBuildingResourceRows"/>):
/// each live building fills its builder's slot row for its kind, indexed
/// <c>slot * Kinds + kind</c>, with the columns the HUD's bars draw — and a
/// dead building leaves its row empty again.
/// </summary>
public sealed class TFBuildingResourceTests
{
    private static string MetalName => TFAmmoNames.Name(TFAmmo.Metal);

    private static (BuildingsRig Rig, TFBuildingResourceRows Rows, TFBuildingResourceNetState State) ARig()
    {
        var rig = new BuildingsRig();
        var rows = new TFBuildingResourceRows(rig.Buildings(), rig.Engine);
        var state = new TFBuildingResourceNetState();
        state.BindLocal();
        return (rig, rows, state);
    }

    [Fact]
    public void APlacedBuildingFillsItsBuildersRowForItsKind()
    {
        var (rig, rows, state) = ARig();
        var builder = rig.AddBuilder(TFTeam.Blue);
        var building = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        building.StartPlacement(builder);
        rig.Engine.Returns("Players.GetHandle", builder);

        rows.Update(state, 100f);

        int row = 1 * TFBuildingResourceNetState.Kinds + TFBuildingResourceNetState.Sentry;
        Assert.Equal((int)TFBuildingResourceNetState.Present, state.Flags[row]);   // placing: not building yet
        Assert.Equal(1, state.Level[row]);
        Assert.Equal(TFBaseObject.ObjectMaxHealth, state.MaxHealth[row]);
    }

    [Fact]
    public void ConstructionClimbsTheProgressColumnAndBuiltIsFull()
    {
        var (rig, rows, state) = ARig();
        var builder = rig.AddBuilder();
        var building = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        building.StartPlacement(builder);
        rig.Engine.Returns("Players.GetHandle", builder);
        int row = 1 * TFBuildingResourceNetState.Kinds + TFBuildingResourceNetState.Sentry;

        building.StartBuilding();
        rig.Clock.Now += 5f;            // half the sentry's 10 s build
        building.OnThink();             // the tick pays the slice
        rows.Update(state, 100f);
        int underConstruction = state.BuildProgress[row];
        Assert.Equal((int)(TFBuildingResourceNetState.Present | TFBuildingResourceNetState.Building),
                     state.Flags[row]);

        rig.Clock.Now += 100f;          // far past the build time
        building.OnThink();
        rows.Update(state, 200f);
        Assert.True(underConstruction is > 0 and < 100, $"build progress {underConstruction} did not climb");
        // Built, the column turns to the upgrade climb: no metal paid yet, so
        // the fraction sits at 0 (the full built row is TheBuiltRow...).
        int required = TFObjectInfo.Of(TFObjectType.Sentrygun)!.UpgradeCost;
        Assert.Equal(0 * 100 / required, state.BuildProgress[row]);
        Assert.Equal(0, state.Flags[row] & TFBuildingResourceNetState.Building);
    }

    [Fact]
    public void ASappedBuildingRaisesTheSapperFlag()
    {
        var (rig, rows, state) = ARig();
        var builder = rig.AddBuilder();
        var building = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        building.StartPlacement(builder);
        rig.Engine.Returns("Players.GetHandle", builder);
        int row = 1 * TFBuildingResourceNetState.Kinds + TFBuildingResourceNetState.Sentry;

        building.OnAddSapper(new EntityHandle(0x0050_0001u));
        rows.Update(state, 100f);

        Assert.Equal(TFBuildingResourceNetState.Sapped, state.Flags[row] & TFBuildingResourceNetState.Sapped);
    }

    [Fact]
    public void TheSentryRowCarriesItsShellsAndTheDispenserItsMetal()
    {
        var (rig, rows, state) = ARig();
        var builder = rig.AddBuilder();
        var sentry = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        sentry.StartPlacement(builder);
        sentry.StartBuilding();
        var dispenser = rig.Spawn<TFDispenser>(TFDispenser.MapClass);
        dispenser.StartPlacement(builder);
        dispenser.StartBuilding();
        rig.Clock.Now += 100f;          // both stand: OnGoActive filled the pools
        sentry.OnThink();
        dispenser.OnThink();
        rig.Engine.Returns("Players.GetHandle", builder);

        rows.Update(state, 100f);

        int sentryRow = 1 * TFBuildingResourceNetState.Kinds + TFBuildingResourceNetState.Sentry;
        int dispenserRow = 1 * TFBuildingResourceNetState.Kinds + TFBuildingResourceNetState.Dispenser;
        Assert.Equal((TFSentryGun.MaxShellsLevel1, TFSentryGun.MaxShellsLevel1),
                     ((int)state.Ammo[sentryRow], (int)state.MaxAmmo[sentryRow]));
        Assert.Equal((TFDispenser.StartMetal, TFDispenser.MaxMetal),
                     ((int)state.Ammo[dispenserRow], (int)state.MaxAmmo[dispenserRow]));
    }

    [Fact]
    public void TheLevelThreeSentryRowCarriesItsPod()
    {
        var (rig, rows, state) = ARig();
        var builder = rig.AddBuilder();
        var sentry = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        sentry.StartPlacement(builder);
        sentry.StartBuilding();
        rig.Clock.Now += 100f;
        sentry.OnThink();
        rig.Engine.Returns("Players.GetHandle", builder);
        int row = 1 * TFBuildingResourceNetState.Kinds + TFBuildingResourceNetState.Sentry;

        rig.Purse.Pockets[MetalName] = 600;
        for (int l = 2; l <= 3; l++)
        {
            for (int swing = 0; swing < 8; swing++)   // 200 metal at 25 the swing
                sentry.WrenchHit(builder);
            rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.UpgradeDuration;
            sentry.OnThink();
        }

        rows.Update(state, 100f);

        // The pod rides the ammo pair's high byte — the row a column would
        // cost puts the state past the 2048-byte tier.
        Assert.Equal(TFSentryGun.MaxShellsLevel3, TFBuildingResourceAmmo.Count(state.Ammo[row]));
        Assert.Equal(TFSentryGun.MaxRocketsStored, TFBuildingResourceAmmo.Pod(state.Ammo[row]));
        Assert.Equal(TFSentryGun.MaxShellsLevel3, TFBuildingResourceAmmo.Count(state.MaxAmmo[row]));
        Assert.Equal(TFSentryGun.MaxRocketsStored, TFBuildingResourceAmmo.Pod(state.MaxAmmo[row]));
    }

    [Fact]
    public void ACarriedBuildingRaisesTheCarriedFlag()
    {
        var (rig, rows, state) = ARig();
        var builder = rig.AddBuilder();
        var building = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        building.StartPlacement(builder);
        building.StartBuilding();
        rig.Clock.Now += 100f;
        building.OnThink();
        rig.Engine.Returns("Players.GetHandle", builder);
        int row = 1 * TFBuildingResourceNetState.Kinds + TFBuildingResourceNetState.Sentry;

        rig.Engine.Returns("Entities.SetParent", true);   // the lift's mount
        building.MakeCarriedObject(builder);
        rows.Update(state, 100f);

        Assert.Equal(TFBuildingResourceNetState.Carried, state.Flags[row] & TFBuildingResourceNetState.Carried);
    }

    [Fact]
    public void ARemovedBuildingLeavesItsRowEmptyAgain()
    {
        var (rig, rows, state) = ARig();
        var builder = rig.AddBuilder();
        var building = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        building.StartPlacement(builder);
        rig.Engine.Returns("Players.GetHandle", builder);
        int row = 1 * TFBuildingResourceNetState.Kinds + TFBuildingResourceNetState.Sentry;

        rows.Update(state, 100f);
        Assert.NotEqual(0, state.Flags[row]);

        building.OnKilled(EntityHandle.None);
        rows.Update(state, 100f);
        Assert.Equal(0, state.Flags[row]);
    }

    [Fact]
    public void TheRowCarriesTheBuildingsKillsAndAssists()
    {
        var (rig, rows, state) = ARig();
        var builder = rig.AddBuilder();
        var sentry = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        sentry.StartPlacement(builder);
        sentry.StartBuilding();
        rig.Engine.Returns("Players.GetHandle", builder);
        int row = 1 * TFBuildingResourceNetState.Kinds + TFBuildingResourceNetState.Sentry;

        sentry.IncrementKills();
        sentry.IncrementKills();
        sentry.IncrementAssists();
        rows.Update(state, 100f);

        Assert.Equal(2, state.Kills[row]);
        Assert.Equal(1, state.Assists[row]);
    }

    [Fact]
    public void TheBuiltRowCarriesTheUpgradeProgress()
    {
        var (rig, rows, state) = ARig();
        var builder = rig.AddBuilder();
        var sentry = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        sentry.StartPlacement(builder);
        sentry.StartBuilding();
        rig.Clock.Now += 100f;
        sentry.OnThink();               // standing: the wrench's metal is taken
        rig.Engine.Returns("Players.GetHandle", builder);
        int row = 1 * TFBuildingResourceNetState.Kinds + TFBuildingResourceNetState.Sentry;

        rig.Purse.Pockets[MetalName] = 100;
        sentry.WrenchHit(builder);      // 25 of the level's 200, the clip per swing
        rows.Update(state, 100f);

        int required = TFObjectInfo.Of(TFObjectType.Sentrygun)!.UpgradeCost;
        Assert.Equal(25 * 100 / required, state.BuildProgress[row]);
        Assert.Equal(0, state.Flags[row] & TFBuildingResourceNetState.Building);

        rig.Purse.Pockets[MetalName] = 600;
        for (int l = 2; l <= 3; l++)
        {
            for (int swing = 0; swing < 8; swing++)
                sentry.WrenchHit(builder);
            rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.UpgradeDuration;
            sentry.OnThink();
        }

        rows.Update(state, 100f);
        Assert.Equal(100, state.BuildProgress[row]);    // level 3: nothing left to climb
    }

    [Fact]
    public void TheEntranceRowCarriesItsRechargeAndItsTrips()
    {
        var (rig, entrance, exit, builder) = TeleporterHarness.Pair();
        var rows = new TFBuildingResourceRows(rig.Buildings(), rig.Engine);
        var state = new TFBuildingResourceNetState();
        state.BindLocal();
        rig.Engine.Returns("Players.GetHandle", builder);
        TickBoth(rig, entrance, exit, 1f);              // ready
        int row = 1 * TFBuildingResourceNetState.Kinds + TFBuildingResourceNetState.Entrance;
        int exitRow = 1 * TFBuildingResourceNetState.Kinds + TFBuildingResourceNetState.Exit;

        rows.Update(state, rig.Clock.Now);
        Assert.Equal(0, state.Flags[row] & TFBuildingResourceNetState.Recharging);

        EntityHandle rider = TeleporterHarness.AStandingRider(rig);
        entrance.OnTouch(rider);
        TickBoth(rig, entrance, exit, 1.5f);            // sent; past the fades, the recharge runs

        rows.Update(state, rig.Clock.Now);
        Assert.Equal(TFBuildingResourceNetState.Recharging, state.Flags[row] & TFBuildingResourceNetState.Recharging);
        int packing = state.Ammo[row];
        Assert.True(TFBuildingResourceTeleporter.Progress(packing) is > 0 and < 100,
                    $"recharge {TFBuildingResourceTeleporter.Progress(packing)} did not climb");
        Assert.Equal(1, TFBuildingResourceTeleporter.TimesUsed(packing));
        Assert.Equal(100, state.MaxAmmo[row]);
        Assert.Equal(1, TFBuildingResourceTeleporter.TimesUsed(state.Ammo[exitRow]));  // the exit counts too

        TickBoth(rig, entrance, exit, 12f);             // the level 1 recharge is gone
        rows.Update(state, rig.Clock.Now);
        Assert.Equal(0, state.Flags[row] & TFBuildingResourceNetState.Recharging);
        Assert.Equal(0, TFBuildingResourceTeleporter.Progress(state.Ammo[row]));
        Assert.Equal(1, TFBuildingResourceTeleporter.TimesUsed(state.Ammo[row]));
    }

    private static void TickBoth(BuildingsRig rig, TFTeleporter entrance, TFTeleporter exit, float seconds)
        => TeleporterHarness.Tick(rig, entrance, exit, seconds);
}

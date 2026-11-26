using SourceSharp.Ai.TF2.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Engineer's nest order (<see cref="TFEngineerNest.Next"/>): stock's <c>CTFBotEngineerBuilding::Update</c> and its
/// maintenance list (tf_bot_engineer_building.cpp:154-171), rung by rung, on what his panel shows.
/// </summary>
public sealed class TFEngineerNestTests
{
    private static readonly AiVector Here = new(100f, 100f, 0f);

    private static TFOwnBuilding Building(int kind, int health = 150, int max = 150, int level = 1, bool sapped = false, bool building = false, bool mini = false)
        => new(kind, TFEngineerPanel.Present | (building ? TFEngineerPanel.Building : TFEngineerPanel.Built) | (sapped ? TFEngineerPanel.Sapped : 0)
                     | (mini ? TFEngineerPanel.Mini : 0), level, health, max, 0, 100, 0, Here);

    private static readonly TFOwnBuilding None = default;

    private static TFOwnBuilding Sentry(int health = 150, int max = 150, int level = 1, bool sapped = false, bool building = false, bool mini = false)
        => Building(TFEngineerPanel.Sentry, health, max, level, sapped, building, mini);

    private static TFOwnBuilding Dispenser(int health = 150, int max = 150, int level = 1, bool sapped = false, bool building = false)
        => Building(TFEngineerPanel.Dispenser, health, max, level, sapped, building);

    [Fact]
    public void WithNoSentryAndItsMetalHeBuildsTheSentry()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Build, TFEngineerPanel.Sentry), TFEngineerNest.Next(None, None, 200, true));

    [Fact]
    public void WithNoSentryAndTooLittleMetalHeFetchesMetal()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Metal, TFEngineerPanel.Sentry), TFEngineerNest.Next(None, None, 129, true));

    [Fact]
    public void ASappedSentryComesFirst()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Work, TFEngineerPanel.Sentry),
                        TFEngineerNest.Next(Sentry(sapped: true), Dispenser(sapped: true), 200, true));

    [Fact]
    public void ASappedDispenserComesBeforeAHurtSentry()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Work, TFEngineerPanel.Dispenser),
                        TFEngineerNest.Next(Sentry(health: 50), Dispenser(sapped: true), 200, true));

    [Fact]
    public void AHurtSentryComesBeforeADispenserUnderConstruction()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Work, TFEngineerPanel.Sentry),
                        TFEngineerNest.Next(Sentry(health: 50), Dispenser(building: true), 200, true));

    [Fact]
    public void ASentryUnderConstructionIsHitToSpeedItUp()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Work, TFEngineerPanel.Sentry),
                        TFEngineerNest.Next(Sentry(health: 40, building: true), None, 70, true));

    [Fact]
    public void ADispenserUnderConstructionComesBeforeTheUpgrade()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Work, TFEngineerPanel.Dispenser),
                        TFEngineerNest.Next(Sentry(), Dispenser(health: 40, building: true), 200, true));

    [Fact]
    public void WithTheSentryUpAndTheDispensersMetalHeBuildsTheDispenserWhenHeMay()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Build, TFEngineerPanel.Dispenser), TFEngineerNest.Next(Sentry(), None, 100, true));

    /// <summary>
    /// Ours (stock upgrades to level 3 first when a metal source is near, :368-383): live, every trip's metal went into the
    /// upgrade, no dispenser was ever built and no gun passed level 2 (6 runs, 2026-11-24).
    /// </summary>
    [Fact]
    public void TheDispenserComesBeforeTheUpgradeOnceHeCanPayForIt()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Build, TFEngineerPanel.Dispenser), TFEngineerNest.Next(Sentry(level: 2, health: 180, max: 180), None, 150, true));

    [Fact]
    public void AtLevelThreeTheDispenserComesNext()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Build, TFEngineerPanel.Dispenser),
                        TFEngineerNest.Next(Sentry(level: 3, health: 216, max: 216), None, 100, true));

    [Fact]
    public void UnderAttackOrInsideTheRetryHeUpgradesTheSentryInstead()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Work, TFEngineerPanel.Sentry), TFEngineerNest.Next(Sentry(), None, 100, false));

    /// <summary>Round 1's fix kept: a new gun's leftover metal goes into it before he walks for the dispenser's.</summary>
    [Fact]
    public void ShortOfTheDispensersMetalHeSpendsWhatHeCarriesOnTheGun()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Work, TFEngineerPanel.Sentry), TFEngineerNest.Next(Sentry(), None, 70, true));

    [Fact]
    public void WithNoMetalAndNoDispenserHeFetchesTheDispensersMetal()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Metal, TFEngineerPanel.Dispenser), TFEngineerNest.Next(Sentry(), None, 0, true));

    [Fact]
    public void TheSentryGoesToLevelThreeBeforeTheDispenserIsRepaired()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Work, TFEngineerPanel.Sentry),
                        TFEngineerNest.Next(Sentry(level: 2, health: 180, max: 180), Dispenser(health: 90), 50, true));

    [Fact]
    public void AtLevelThreeAHurtDispenserIsRepaired()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Work, TFEngineerPanel.Dispenser),
                        TFEngineerNest.Next(Sentry(level: 3, health: 216, max: 216), Dispenser(health: 90), 50, true));

    [Fact]
    public void TheDispenserIsUpgradedToTheSentrysLevel()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Work, TFEngineerPanel.Dispenser),
                        TFEngineerNest.Next(Sentry(level: 3, health: 216, max: 216), Dispenser(level: 1), 50, true));

    [Fact]
    public void AnUpgradeWithNoMetalAndNoDispenserIsATripForMetal()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Metal, TFEngineerPanel.Sentry), TFEngineerNest.Next(Sentry(), None, 0, false));

    [Fact]
    public void AnUpgradeWithNoMetalByHisDispenserIsWorkHeStaysFor()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Work, TFEngineerPanel.Sentry), TFEngineerNest.Next(Sentry(), Dispenser(), 0, true));

    [Fact]
    public void WithEverythingDoneHeKeepsHisWrenchOnTheSentry()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Work, TFEngineerPanel.Sentry),
                        TFEngineerNest.Next(Sentry(level: 3, health: 216, max: 216), Dispenser(level: 3, max: 216, health: 216), 200, true));

    [Fact]
    public void AMiniSentryIsNeverUpgraded()
        => Assert.Equal(new TFEngineerStep(TFEngineerJob.Work, TFEngineerPanel.Sentry),
                        TFEngineerNest.Next(Sentry(max: 100, health: 100, mini: true), None, 200, true, upgrades: false));
}

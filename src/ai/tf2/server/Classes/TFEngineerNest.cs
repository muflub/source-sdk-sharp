//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>What the Engineer is about (<see cref="TFEngineerNest.Next"/>): his nest's next job.</summary>
public enum TFEngineerJob : byte
{
    /// <summary>Nothing to do: no place to build.</summary>
    None = 0,

    /// <summary>Put a blueprint down (<see cref="TFEngineerStep.Build"/> names which).</summary>
    Build,

    /// <summary>Swing the wrench at a building (<see cref="TFEngineerStep.Work"/> names which): repair, construction, upgrade, sapper.</summary>
    Work,

    /// <summary>Fetch metal: what the next job costs is more than he carries, and no dispenser of his feeds him.</summary>
    Metal,
}

/// <summary>The Engineer's next job and its subject: a panel row (<see cref="TFEngineerPanel"/>) to build or to work on.</summary>
public readonly record struct TFEngineerStep(TFEngineerJob Job, int Kind);

/// <summary>
/// The Engineer's nest, stock's order (<c>CTFBotEngineerBuilding::Update</c> and <c>UpgradeAndMaintainBuildings</c>,
/// tf_bot_engineer_building.cpp:154-171, 269-465), on what his panel shows: the sentry first; then whatever is sapped,
/// the sentry hurt or under construction, the dispenser under construction; the dispenser once the sentry stands and
/// nothing hurts him (stock retries it every <see cref="DispenserRetrySeconds"/>, never under attack); then the sentry
/// to level 3, the dispenser hurt, the dispenser up to the sentry's level. A job that costs metal he has not got, with
/// no dispenser of his to stand by, is a trip for metal (stock's <c>CTFBotGetAmmo</c> on a dry wrench).
/// Pure: the play hands it the panel's rows and the bot's metal.
/// </summary>
public static class TFEngineerNest
{
    /// <summary>The metal each building costs (objects.txt, <see cref="Knowledge.TFKnowledge"/>'s table): sentry 130, dispenser 100.</summary>
    public const int SentryCost = 130, DispenserCost = 100, MiniSentryCost = 100;

    /// <summary>Stock's <c>dispenserRebuildInterval</c> (:398): a lost dispenser is not rebuilt sooner.</summary>
    public const float DispenserRetrySeconds = 10f;

    /// <summary>The most a building is upgraded to.</summary>
    public const int TopLevel = 3;

    /// <summary>
    /// The next job. <paramref name="mayBuildDispenser"/>: stock's retry clock is done and he is not under attack;
    /// <paramref name="upgrades"/>: the archetype upgrades (a Gunslinger's mini never does).
    /// </summary>
    public static TFEngineerStep Next(in TFOwnBuilding sentry, in TFOwnBuilding dispenser, int metal, bool mayBuildDispenser, bool upgrades = true,
                                      int sentryCost = SentryCost)
    {
        if (!sentry.Exists)
            return metal >= sentryCost ? new(TFEngineerJob.Build, TFEngineerPanel.Sentry) : new(TFEngineerJob.Metal, TFEngineerPanel.Sentry);
        if (sentry.Sapped)
            return new(TFEngineerJob.Work, TFEngineerPanel.Sentry);
        if (dispenser.Exists && dispenser.Sapped)
            return new(TFEngineerJob.Work, TFEngineerPanel.Dispenser);
        bool fed = dispenser.Exists && dispenser.Built;
        if (sentry.Hurt || sentry.UnderConstruction)
            return Costly(sentry.Hurt && !sentry.UnderConstruction, metal, fed, TFEngineerPanel.Sentry);
        if (dispenser.Exists && dispenser.UnderConstruction)
            return new(TFEngineerJob.Work, TFEngineerPanel.Dispenser);
        if (!dispenser.Exists && mayBuildDispenser && upgrades)
            return metal >= DispenserCost ? new(TFEngineerJob.Build, TFEngineerPanel.Dispenser) : new(TFEngineerJob.Metal, TFEngineerPanel.Dispenser);
        if (upgrades && !sentry.Mini && sentry.Level < TopLevel)
            return Costly(true, metal, fed, TFEngineerPanel.Sentry);
        if (dispenser.Exists && dispenser.Hurt)
            return Costly(true, metal, fed, TFEngineerPanel.Dispenser);
        if (upgrades && dispenser.Exists && dispenser.Level < sentry.Level)
            return Costly(true, metal, fed, TFEngineerPanel.Dispenser);
        return new(TFEngineerJob.Work, TFEngineerPanel.Sentry);
    }

    /// <summary>A swing that spends metal (a repair, an upgrade): with none and no dispenser feeding him, metal first.</summary>
    private static TFEngineerStep Costly(bool spends, int metal, bool fed, int kind)
        => spends && metal <= 0 && !fed ? new(TFEngineerJob.Metal, kind) : new(TFEngineerJob.Work, kind);
}

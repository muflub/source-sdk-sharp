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
/// The teleporter ends the play may want this decide (<see cref="TFEngineerNest.Next"/>): the entrance (none stands, and he is
/// on his way out of his spawn this life: stock's CTFBotEngineerBuildTeleportEntrance), the exit (none stands, his entrance
/// does, stock's 30 s retry is done and nothing hurts him), and whether the exit comes before the sentry (an attacking
/// Engineer's forward teleporter).
/// </summary>
public readonly record struct TFTeleporterWant(bool Entrance, bool Exit, bool ExitFirst = false);

/// <summary>
/// The Engineer's nest, stock's order (<c>CTFBotEngineerBuilding::Update</c> and <c>UpgradeAndMaintainBuildings</c>,
/// tf_bot_engineer_building.cpp:154-171, 269-465), on what his panel shows: the sentry first; then whatever is sapped,
/// the sentry hurt or under construction, the dispenser under construction; the dispenser once the sentry stands and nothing
/// hurts him (stock retries it every <see cref="DispenserRetrySeconds"/>, never under attack), before the upgrade (ours:
/// stock upgrades first when a metal source is near, tf_bot_engineer_building.cpp:380-393); the sentry to level 3; then the
/// dispenser hurt, the dispenser up to the sentry's level. A job that costs metal he has not got, with no dispenser of his to
/// stand by, is a trip for metal (stock's <c>CTFBotGetAmmo</c> on a dry wrench).
/// Pure: the play hands it the panel's rows and the bot's metal.
/// </summary>
public static class TFEngineerNest
{
    /// <summary>The metal each building costs (objects.txt, <see cref="Knowledge.TFKnowledge"/>'s table): sentry 130, dispenser 100, a teleporter end 50.</summary>
    public const int SentryCost = 130, DispenserCost = 100, MiniSentryCost = 100, TeleporterCost = 50;

    /// <summary>Stock's <c>dispenserRebuildInterval</c> (:398): a lost dispenser is not rebuilt sooner.</summary>
    public const float DispenserRetrySeconds = 10f;

    /// <summary>The most a building is upgraded to.</summary>
    public const int TopLevel = 3;

    /// <summary>
    /// The next job. <paramref name="mayBuildDispenser"/>: stock's retry clock is done and he is not under attack;
    /// <paramref name="upgrades"/>: the archetype upgrades (a Gunslinger's mini never does).
    /// </summary>
    public static TFEngineerStep Next(in TFOwnBuilding sentry, in TFOwnBuilding dispenser, int metal, bool mayBuildDispenser, bool upgrades = true,
                                      int sentryCost = SentryCost, TFTeleporterWant teleporters = default)
    {
        // Stock's first job (CTFBotEngineerBuild::InitialContainedAction): the entrance on his way out of spawn, if it is
        // affordable; then the sentry. An attacker's exit before his gun.
        if (teleporters.Entrance && metal >= TeleporterCost)
            return new(TFEngineerJob.Build, TFEngineerPanel.Entrance);
        if (teleporters.Exit && teleporters.ExitFirst && metal >= TeleporterCost)
            return new(TFEngineerJob.Build, TFEngineerPanel.Exit);
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
        // The dispenser before the upgrade, once he can pay for it; short of its price, what he carries goes into the gun before
        // he walks for more (round 1's fix: a new gun's 70 metal is not left unspent). Ours: stock goes to level 3 first when a
        // metal source is near (tf_bot_engineer_building.cpp:380-393; IsMetalSourceNearby, :181-207, an ammo area or his spawn
        // room within 2,000 u), and builds the dispenser after (:407-421). The base's stall at level 2 (no dispenser and no gun
        // past level 2 in six runs, 7e79f0290c) came with the hull trap and the stand faults, which stop upgrades in any order;
        // this order was not compared against stock's alone after those fixes. It showed no cost: at e0e7a8da8f on ctf_2fort a
        // dispenser stood in six of six runs and level 3 lasted 153-165 s of 234 s up (docs/divergences.md).
        if (!dispenser.Exists && mayBuildDispenser && upgrades)
        {
            if (metal >= DispenserCost)
                return new(TFEngineerJob.Build, TFEngineerPanel.Dispenser);
            if (metal > 0 && !sentry.Mini && sentry.Level < TopLevel)
                return new(TFEngineerJob.Work, TFEngineerPanel.Sentry);
            return new(TFEngineerJob.Metal, TFEngineerPanel.Dispenser);
        }

        // The exit after the dispenser (stock's next build, tf_bot_engineer_building.cpp:425-465), before the upgrade (ours: stock
        // upgrades first with a metal source near): his team comes back to the nest by it.
        if (teleporters.Exit && metal >= TeleporterCost)
            return new(TFEngineerJob.Build, TFEngineerPanel.Exit);

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

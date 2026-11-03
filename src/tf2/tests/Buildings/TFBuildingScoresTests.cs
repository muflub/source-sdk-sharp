using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Buildings;
using TF2.Server.Combat;
using TF2.Shared;
using TF2.Shared.Combat;

using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The buildings' kill book (<see cref="TFBuildingScores"/>): the object
/// behind the death's inflictor takes the kill — the rocket through its
/// launcher — the builder's own death does not count, and the assister's
/// sentry wound on the victim within the window takes the assist
/// (tf_gamerules.cpp:11324-11365, :11397-11412).
/// </summary>
public sealed class TFBuildingScoresTests
{
    private static (BuildingsRig Rig, TFBuildingScores Scores, TFDamageRecords Records, TFSentryDamages SentryDamages)
        ARig()
    {
        var rig = new BuildingsRig();
        // The assister the guard validates is a fake-table player, which the
        // rig's registry-backed validity answer would refuse.
        rig.Engine.Returns("Entities.IsValid", true);
        var scores = new TFBuildingScores(rig.Registry, rig.Engine, rig.Services.Resolve<TFDamageRecords>()!,
                                          rig.Services.Resolve<TFSentryDamages>()!, rig.Clock);
        return (rig, scores, rig.Services.Resolve<TFDamageRecords>()!, rig.Services.Resolve<TFSentryDamages>()!);
    }

    private static DeathNoticeFacts AKill(EntityHandle victim, EntityHandle inflictor, EntityHandle attacker) =>
        new(victim, attacker, attacker, inflictor, "");

    private static readonly EntityHandle Nobody = new(0x0090_0001u);

    [Fact]
    public void TheSentrysKillIsTheInflictors()
    {
        var (rig, scores, _, _) = ARig();
        var builder = rig.AddBuilder(TFTeam.Blue);
        var sentry = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        sentry.StartPlacement(builder);
        var victim = rig.AddBuilder();

        scores.Fill(AKill(victim, sentry.Handle, builder), new RecordingFields());

        Assert.Equal(1, sentry.Kills);
        Assert.Equal(0, sentry.Assists);
    }

    [Fact]
    public void TheRocketKillsThroughItsLauncher()
    {
        var (rig, scores, _, _) = ARig();
        var builder = rig.AddBuilder(TFTeam.Blue);
        var sentry = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        sentry.StartPlacement(builder);
        var rocket = rig.Spawn<TFSentryRocket>(TFSentryRocket.MapClass);
        rocket.FiredBy(sentry.Handle, builder, new Vector(1f, 0f, 0f));
        var victim = rig.AddBuilder();

        scores.Fill(AKill(victim, rocket.Handle, builder), new RecordingFields());

        Assert.Equal(1, sentry.Kills);
    }

    [Fact]
    public void TheBuilderHisOwnDeathIsNotAKill()
    {
        var (rig, scores, _, _) = ARig();
        var builder = rig.AddBuilder(TFTeam.Blue);
        var sentry = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        sentry.StartPlacement(builder);

        scores.Fill(AKill(builder, sentry.Handle, builder), new RecordingFields());

        Assert.Equal(0, sentry.Kills);
    }

    [Fact]
    public void AnAssistGoesToTheSentryThatWoundedTheVictim()
    {
        var (rig, scores, records, sentryDamages) = ARig();
        var builder = rig.AddBuilder(TFTeam.Blue);
        var sentry = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        sentry.StartPlacement(builder);
        var victim = rig.AddBuilder();
        var scorer = rig.AddBuilder();
        var assister = rig.AddBuilder();

        int victimIndex = victim.EntryIndex;
        records.Note(victimIndex, new TFHitRecord(assister, 5f, DamageType.Bullet, TFCritType.None,
                                                  TFDamageCustom.None, TFWeaponId.Pistol, "tf_weapon_pistol", 10f),
                     damager: true);
        records.Note(victimIndex, new TFHitRecord(scorer, 5f, DamageType.Bullet, TFCritType.None,
                                                   TFDamageCustom.None, TFWeaponId.Pistol, "tf_weapon_pistol", 11f),
                     damager: true);                             // the killer: the history's latest
        sentryDamages.Note(victimIndex, assister, sentry.Handle, 10f);

        scores.Fill(AKill(victim, EntityHandle.None, scorer), new RecordingFields());

        Assert.Equal(1, sentry.Assists);
        Assert.Equal(0, sentry.Kills);   // no building behind this one
    }

    [Fact]
    public void AnAssistNeedsARecentWound()
    {
        var (rig, scores, records, sentryDamages) = ARig();
        var builder = rig.AddBuilder(TFTeam.Blue);
        var sentry = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        sentry.StartPlacement(builder);
        var victim = rig.AddBuilder();
        var assister = rig.AddBuilder();

        int victimIndex = victim.EntryIndex;
        records.Note(victimIndex, new TFHitRecord(assister, 5f, DamageType.Bullet, TFCritType.None,
                                                  TFDamageCustom.None, TFWeaponId.Pistol, "tf_weapon_pistol", 10f),
                     damager: true);
        sentryDamages.Note(victimIndex, assister, sentry.Handle, 10f);
        rig.Clock.Now += TFSentryDamages.AssistWindow + 1f;

        scores.Fill(AKill(victim, EntityHandle.None, assister) with { Scorer = assister },
                    new RecordingFields());

        Assert.Equal(0, sentry.Assists);
    }

    /// <summary>The fields setter, recorded and thrown away: the hook writes none of its own.</summary>
    private sealed class RecordingFields : IDeathNoticeFields
    {
        public void SetInt(string key, int value) { }
        public void SetBool(string key, bool value) { }
        public void SetString(string key, string value) { }
    }
}

/// <summary>
/// The sentry-damager history (<see cref="TFSentryDamages"/>): the pair is
/// found within the window and answers the sentry, a repeat damager moves to
/// the front, the scan stops at the first stale entry, and the death clears it.
/// </summary>
public sealed class TFSentryDamagesTests
{
    private static readonly EntityHandle Damager = new(0x0091_0001u);
    private static readonly EntityHandle Other = new(0x0091_0002u);
    private static readonly EntityHandle Sentry = new(0x0091_0003u);
    private static readonly EntityHandle SecondSentry = new(0x0091_0004u);

    [Fact]
    public void TheWoundIsFoundWithinTheWindow()
    {
        var seen = new TFSentryDamages();
        seen.Note(3, Damager, Sentry, 10f);

        Assert.Equal(Sentry, seen.DamagedBy(3, Damager, 10f + TFSentryDamages.AssistWindow - 0.1f));
    }

    [Fact]
    public void TheWoundExpiresPastTheWindow()
    {
        var seen = new TFSentryDamages();
        seen.Note(3, Damager, Sentry, 10f);

        Assert.Equal(EntityHandle.None, seen.DamagedBy(3, Damager, 10f + TFSentryDamages.AssistWindow + 0.1f));
    }

    [Fact]
    public void TheNewestWoundWinsAndTheScanStopsAtTheFirstStale()
    {
        var seen = new TFSentryDamages();
        seen.Note(3, Damager, Sentry, 10f);         // oldest: the front insert pushes it back
        seen.Note(3, Damager, SecondSentry, 12f);   // newest

        // 10 + window < now: the stale front entry would stop an unsorted scan.
        Assert.Equal(SecondSentry, seen.DamagedBy(3, Damager, 12f + TFSentryDamages.AssistWindow - 0.5f));
    }

    [Fact]
    public void AnotherDamagersWoundIsNotOurs()
    {
        var seen = new TFSentryDamages();
        seen.Note(3, Damager, Sentry, 10f);

        Assert.Equal(EntityHandle.None, seen.DamagedBy(3, Other, 10f));
    }

    [Fact]
    public void TheDeathClearsTheHistory()
    {
        var seen = new TFSentryDamages();
        seen.Note(3, Damager, Sentry, 10f);
        seen.Clear(3);

        Assert.Equal(EntityHandle.None, seen.DamagedBy(3, Damager, 10f));
    }

    [Fact]
    public void TheDeathsHookClearsItWithTheRest()
    {
        var records = new TFDamageRecords();
        var seen = new TFSentryDamages();
        seen.Note(3, Damager, Sentry, 10f);

        new TFCombatDeaths(records, seen).OnPlayerDied(new PlayerDeath(
            new EntityHandle(0x0092_0003u), 3, EntityHandle.None, default, EntityHandle.None));

        Assert.Equal(EntityHandle.None, seen.DamagedBy(3, Damager, 10f));
    }
}

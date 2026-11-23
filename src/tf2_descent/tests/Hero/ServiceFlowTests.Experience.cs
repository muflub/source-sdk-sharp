using System.Collections.Immutable;

using SourceSharp.Abi;
using SourceSharp.Host.Sdk;

using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Sheets;
using TF2.Descent.Server;
using TF2.Descent.Server.Hero;
using TF2.Descent.Tests.Hero;

using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// HE5 hosted: the checkpoint that follows a level-up carries the evidence the
/// kill recorded, and the host's own rules (<c>ValidateCheckpoint</c>) accept
/// the sheet it sends against the leased one.
/// </summary>
public sealed partial class ServiceFlowTests
{
    /// <summary>The hosted hero in slot 3 in the game, and what pays him for kills.</summary>
    private static (HeroComponent Hero, HeroExperience Xp) InTheGame(Hosted h)
    {
        var engine = new RecordingEngine();
        engine.Returns("Entities.Create", EntityHandle.None);
        engine.Returns("Entities.GetOrigin", default(Vector));
        engine.Returns("Entities.GetHealth", 100);
        engine.Returns("Players.IsAlive", true);
        engine.ReturnsEachCall("Entities.GetIndex", () => (int)engine.Calls[^1].ArgAs<EntityHandle>(0).EntryIndex);

        var heroes = new Heroes();
        var hero = new HeroComponent(engine, new NoEntities(), h.Sheets, heroes,
                                     new HeroRig.ListHooks<IHeroContributionSource>([]), new HeroRig.ListHooks<IHeroStatsWatcher>([]),
                                     new HeroRig.Resupply(), h.W.Log);
        hero.Attach(HeroRig.PlayerOf(3));
        hero.Tick(0f);   // no hero entity here: the first tick reads the sheet
        Assert.NotNull(hero.Sheet);
        var xp = new HeroExperience(heroes, h.Sheets, engine, engine, new Party.OneParty(), new ClientMessages(engine), new PlayerLevelCheckpoint(h.Players), h.W.Log);
        return (hero, xp);
    }

    /// <summary>A kill of a level 30 heavy where the hero stands: worth more than one level to a level 3 hero.</summary>
    private static RobotKill HeavyKill(Hosted h) =>
        new(0, new RobotId(0, 1), "heavy", 30, 1.0, ImmutableArray<string>.Empty, false,
            h.Rules.Robots.Stats(h.Rules.Robots.Template("heavy"), 1.0, 30, []), default, 3, HeroRig.PlayerOf(3));

    [Fact]
    public void A_level_ups_checkpoint_carries_evidence_that_explains_the_sheet()
    {
        var h = HostedHero(LevelThree);
        var leased = LevelThree(h.Rules);
        var (hero, xp) = InTheGame(h);
        int checkpointsBefore = h.W.Link.Calls.Count(c => c == $"checkpoint {Steam}");

        var gain = Assert.Single(xp.Pay(HeavyKill(h)));

        Assert.True(gain.Result.LevelsGained > 0, "the stimulus: the kill is worth a level");
        Assert.Equal(checkpointsBefore + 1, h.W.Link.Calls.Count(c => c == $"checkpoint {Steam}"));
        Assert.Equal(hero.Sheet!.Level, Sent(h).Level);
        // What the host's rules make of it, first: the sheet is explained by its evidence.
        var payload = h.W.Link.Sheets[^1].Sheet!;
        var verdict = h.Rules.ValidateCheckpoint(leased, payload.Sheet, payload.Evidence);
        Assert.True(verdict.Ok, verdict.FailedRule);
        Assert.Equal(gain.Xp, Evidence(h).XpEarned);
        Assert.Equal(1, Evidence(h).KillsOf("heavy"));
    }

    [Fact]
    public void The_hosts_rules_refuse_the_same_sheet_with_the_xp_but_not_its_kills()
    {
        var h = HostedHero(LevelThree);
        var leased = LevelThree(h.Rules);
        var (_, xp) = InTheGame(h);
        xp.Pay(HeavyKill(h));
        var payload = h.W.Link.Sheets[^1].Sheet!;
        var proof = h.Rules.Evidence.Read(payload.Evidence);

        var noKills = h.Rules.Evidence.Write(new CheckpointProof(proof.XpEarned, CheckpointProof.None.Kills));
        var verdict = h.Rules.ValidateCheckpoint(leased, payload.Sheet, noKills);

        Assert.False(verdict.Ok);
        Assert.Contains(verdict.FailedRule, new[] { "xp_beyond_kills", "bestiary_unexplained" });
    }

    [Fact]
    public void An_unhosted_level_up_sends_no_checkpoint()
    {
        var w = Host(HostingMode.Unhosted);
        w.Host.OnPostInit();
        var sheets = new HeroSheets(new TF2.Descent.Rules.TF2DescentRules(), w.Host, new FakeClients(), w.Log);
        var players = new PlayerService(w.Link, w.Host, new FakeClients(), new ClientMessages(new RecordingEngine()),
                                        new NeverScheduler(), w.Log, sheets, new TF2.Descent.Tests.Hero.HeroRig.ListHooks<TF2.Descent.Server.IHeroJoinHook>([]));

        Assert.False(new PlayerLevelCheckpoint(players).Checkpoint(3));
        Assert.DoesNotContain(w.Link.Calls, c => c.StartsWith("checkpoint", StringComparison.Ordinal));
    }
}

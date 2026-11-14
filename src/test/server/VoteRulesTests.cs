using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The vote rules of plan_bots_teams_tf2 D.3: the electorate is the humans (a
/// bot never counts, so one human with five bots passes a vote on their own
/// yes), <c>sv_vote_min_voters</c> refuses a call too few can vote on, a failed
/// issue waits <c>sv_vote_failure_timer</c>, and the issues' own refusals.
/// </summary>
public class VoteRulesTests
{
    private static readonly EntityHandle Ctrl = new(0x0071_0071u);

    private static EntityHandle Slot(int slot) => new((uint)(0x0080_0000 | slot));

    /// <summary>A controller over slots 1..<paramref name="players"/>, slot 1 a human and the rest bots from <paramref name="firstBot"/>.</summary>
    private static (VoteController Vote, RecordingEngine Engine) Make(int players, int firstBot, Dictionary<string, string>? convars = null)
    {
        var services = Services(out var engine);
        engine.Ignores("Entities.FireInput", "Host.RandomFloat", "Console.FindConVarValue");
        engine.ReturnsEachCall("Players.GetHandle", () => engine.Calls[^1].Args[0] is int slot && slot <= players ? Slot(slot) : EntityHandle.None);
        engine.ReturnsEachCall("Entities.IsValid", () => engine.Calls[^1].Args[0] is EntityHandle h && h != EntityHandle.None && h != Ctrl);
        engine.Returns("Players.GetTeam", 0);
        var registry = new ConVarRegistry(new NamedConVars(convars ?? []));
        services.AddInstance(new VoteConVars(registry.Declare<AllowVotesConVar>(), registry.Declare<VoteTimerDurationConVar>(),
                                             registry.Declare<VoteCreationTimerConVar>(), registry.Declare<VoteHolderMayVoteNoConVar>(),
                                             registry.Declare<VoteEarlyFinishConVar>(), registry.Declare<VoteQuorumRatioConVar>(),
                                             registry.Declare<VoteCommandDelayConVar>(), registry.Declare<VoteAllowSpectatorsConVar>(),
                                             registry.Declare<VoteMinVotersConVar>(), registry.Declare<VoteFailureTimerConVar>()));
        services.AddInstance<IVoteIndices>(new VoteIndices());
        services.AddInstance<IBotPlayers>(new TeamDeathmatchTests.BotRoster([.. Enumerable.Range(firstBot, Math.Max(0, players - firstBot + 1)).Select(Slot)]));
        var entities = new ScriptedEntityRegistry(TestEngines.Server());
        entities.RegisterAll(typeof(VoteController).Assembly);
        entities.Services = services;
        entities.Spawn(Ctrl, EntityNames.VoteController);
        return ((VoteController)entities.Get(Ctrl)!, engine);
    }

    [Fact]
    public void ABotNeverCounts()
        => Assert.Equal(1, VoteController.CountVoters([(0, false), (0, true), (0, true)], allowSpectators: false, onlyTeam: 0));

    [Fact]
    public void OneHumanWithFiveBotsPassesAVoteOnTheirOwnYes()
    {
        var (vote, _) = Make(players: 6, firstBot: 2);

        Assert.Equal(1, vote.EligibleVoters());
        Assert.True(vote.StartVote(Slot(1), new VoteIssue("Mode", "tdm", "sv_mode tdm"), 100f));
        Assert.Equal(1, vote.PotentialVoters);
        vote.Tick(120f);

        Assert.Equal(VoteOutcome.Passed, vote.Outcome);
    }

    [Fact]
    public void MinVotersTwoRefusesTheLoneHumansCall()
    {
        var (vote, _) = Make(players: 6, firstBot: 2, new() { ["sv_vote_min_voters"] = "2" });

        Assert.True(vote.TooFewVoters(out int eligible, out int needed));
        Assert.Equal((1, 2), (eligible, needed));
    }

    [Fact]
    public void TheDefaultMinimumTakesTheLoneHumansCall()
        => Assert.False(Make(players: 6, firstBot: 2).Vote.TooFewVoters(out _, out _));

    [Fact]
    public void AFailedIssueWaitsTheFailureTimerForAnyCaller()
    {
        var (vote, _) = Make(players: 3, firstBot: 9, new() { ["sv_vote_failure_timer"] = "300" });
        vote.StartVote(Slot(1), new VoteIssue("Mode", "tdm", "sv_mode tdm"), 100f);
        vote.Cast(Slot(2), 2, 100f);
        vote.Cast(Slot(3), 2, 100f);
        vote.Tick(102f);
        Assert.Equal(VoteOutcome.Failed, vote.Outcome);

        Assert.True(vote.FailedLately("Mode", "tdm", 110f, out float wait));
        Assert.Equal(292f, wait, 0.01f);
        Assert.False(vote.FailedLately("Mode", "null", 110f, out _));
        Assert.False(vote.FailedLately("Restart", "", 110f, out _));
        Assert.False(vote.FailedLately("Mode", "tdm", 402f, out _));
    }

    [Fact]
    public void AVoteTheServerHeldRecordsNoFailure()
    {
        var (vote, _) = Make(players: 3, firstBot: 9);
        vote.StartVote(EntityHandle.None, new VoteIssue("NextMap", "nextmap", "", Options: ["a", "b"]), 100f);
        vote.Tick(120f);

        Assert.Equal(VoteOutcome.Failed, vote.Outcome);
        Assert.False(vote.FailedLately("NextMap", "nextmap", 121f, out _));
    }

    [Fact]
    public void APassedIssueDoesNotWait()
    {
        var (vote, _) = Make(players: 1, firstBot: 9);
        vote.StartVote(Slot(1), new VoteIssue("Mode", "tdm", "sv_mode tdm"), 100f);
        vote.Tick(120f);

        Assert.Equal(VoteOutcome.Passed, vote.Outcome);
        Assert.False(vote.FailedLately("Mode", "tdm", 121f, out _));
    }

    //-------------------------------------------------------------------------
    // The issues' own refusals and choices.
    //-------------------------------------------------------------------------

    private sealed class Level : ILevelInfo
    {
        public string? Map => "dm_lockdown";
        public bool IsReady => true;
    }

    private sealed class FlagMode(IRoundFlow rounds) : IGameMode
    {
        public string Id => "ctf";
        public IRoundFlow Rounds => rounds;
        public IModeSettings Settings => new ModeSettings("#CTF", [], ["item_teamflag"]);
    }

    private static T Declare<T>(string name, string value) where T : ConVarBase
        => new ConVarRegistry(new NamedConVars(new Dictionary<string, string> { [name] = value })).Declare<T>();

    [Fact]
    public void AModeTheMapCannotPlayIsRefusedAtTheCallWithTheReason()
    {
        var rig = new TeamDeathmatchTests.Rig();
        rig.Services.AddInstance(new FlagMode(rig.Rules));
        var files = new GameModeCatalogTests.Bsps();
        files.Files["maps/dm_lockdown.bsp"] = GameModeCatalogTests.Bsp("info_player_deathmatch");
        var catalog = new GameModeCatalog(rig.Modes, new BspEntityClasses(files), new Level());
        var issue = new ModeVoteIssue(rig.Modes, catalog, Declare<VoteIssueModeAllowedConVar>("sv_vote_issue_mode_allowed", "1"));
        var choices = issue.Choices().ToDictionary(c => c.Id);

        Assert.Equal("dm_lockdown has no item_teamflag", issue.Refusal(choices["ctf"]));
        Assert.Null(issue.Refusal(choices["tdm"]));
    }

    [Fact]
    public void NextMapOffersEachMapAndAChoiceOfTheFirstFive()
    {
        var options = NextLevelVoteIssue.Of(Maps("a", "b", "c", "d", "e", "f")).ToList();

        Assert.Equal(["a", "b", "c", "d", "e", "f", "nextmap"], options.Select(o => o.Id));
        Assert.Equal("nextlevel c", options[2].Command);
        Assert.Equal(["a", "b", "c", "d", "e"], options[^1].Ballot!.Select(c => c.Label));
        Assert.Equal("nextlevel e", options[^1].Ballot![4].Command);
        Assert.DoesNotContain(NextLevelVoteIssue.Of(Maps("a")), o => o.Ballot is not null);
    }

    private static MapCandidate[] Maps(params string[] names) => [.. names.Select(n => new MapCandidate(n, [], HasNav: true))];

    [Fact]
    public void NextMapIsRefusedWhileTheNextMapIsSet()
    {
        var rig = new TeamDeathmatchTests.Rig();
        var files = new GameModeCatalogTests.Bsps();
        var candidates = new MapCandidates(files, Declare<MapCycleFileConVar>("mapcyclefile", ""),
                                           new GameModeCatalog(rig.Modes, new BspEntityClasses(files), new Level()), Declare<SvModeNextConVar>("sv_mode_next", ""));
        var issue = new NextLevelVoteIssue(candidates, Declare<NextLevelConVar>("nextlevel", "dm_x"),
                                           Declare<VoteIssueNextLevelAllowedConVar>("sv_vote_issue_nextlevel_allowed", "1"),
                                           Declare<VoteIssueNextLevelChoicesModeConVar>("sv_vote_issue_nextlevel_choicesmode", "1"));

        Assert.Equal("the next map is already dm_x", issue.Refusal(new VoteOption(VoteKind.NextMap, "a", "a", "nextlevel a")));
    }

    [Fact]
    public void ExtendAddsTwentyMinutesAndOnlyToATimeLimit()
    {
        var allowed = Declare<VoteIssueExtendLevelAllowedConVar>("sv_vote_issue_extendlevel_allowed", "1");

        Assert.Equal("mp_timelimit 50", Assert.Single(new ExtendVoteIssue(Declare<MpTimeLimitConVar>("mp_timelimit", "30"), allowed).Choices()).Command);
        Assert.Empty(new ExtendVoteIssue(Declare<MpTimeLimitConVar>("mp_timelimit", "0"), allowed).Choices());
    }
}

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The general ballot (plan_bots_teams_tf2 D.3): a multiple-choice vote of any
/// two to five choices is never read as Yes/No, its winner runs its own
/// command, the state on the wire carries the choices and every count, and a
/// call names a choice by its issue's word.
/// </summary>
public class VoteBallotTests
{
    private static readonly EntityHandle Ctrl = new(0x0061_0061u);
    private static readonly EntityHandle Caller = new(0x0051_0051u);
    private static readonly EntityHandle Voter2 = new(0x0052_0052u);
    private static readonly EntityHandle Voter3 = new(0x0053_0053u);

    private static (VoteController Vote, RecordingEngine Engine) Make()
    {
        var services = Services(out var engine);
        engine.Ignores("Console.FindConVarValue", "Entities.FireInput", "Host.RandomFloat", "Players.GetHandle", "Players.GetTeam", "Entities.IsValid");
        services.AddInstance<IVoteIndices>(new VoteIndices());
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(VoteController).Assembly);
        registry.Services = services;
        registry.Spawn(Ctrl, EntityNames.VoteController);
        return ((VoteController)registry.Get(Ctrl)!, engine);
    }

    private static readonly VoteOption TwoMaps = new(VoteKind.NextMap, "nextmap", "#SS_UI_Vote_NextMapChoose", "",
                                                     [new("dm_a", "nextlevel dm_a"), new("dm_b", "nextlevel dm_b")]);

    [Fact]
    public void AChoiceOfTwoIsNotAYesNoBallot()
    {
        var (vote, _) = Make();

        Assert.True(vote.StartVote(Caller, ServerVotes.IssueOf(TwoMaps), 100f));

        Assert.False(vote.IsYesNoVote);
        Assert.Equal(["dm_a", "dm_b"], vote.Options);
        Assert.Equal(0, vote.VotesCast);   // no caller's yes on a choice
        Assert.Equal(VoteCast.BadOption, vote.Cast(Voter2, 3, 100f));
    }

    [Fact]
    public void TheWinningChoiceRunsItsOwnCommand()
    {
        var (vote, engine) = Make();
        vote.StartVote(Caller, ServerVotes.IssueOf(TwoMaps), 100f);

        vote.Cast(Caller, 2, 100f);
        vote.Cast(Voter2, 2, 100f);
        vote.Cast(Voter3, 1, 100f);
        vote.Tick(102f);
        vote.Tick(110f);

        Assert.Equal(VoteOutcome.Passed, vote.Outcome);
        Assert.Equal("nextlevel dm_b", engine.Last("Console.ExecuteServerCommand")!.Value.Args[0]);
    }

    [Fact]
    public void TheStateCarriesTheChoicesAndEveryCount()
    {
        var (vote, _) = Make();
        vote.StartVote(Caller, ServerVotes.IssueOf(TwoMaps), 100f);
        vote.Cast(Voter2, 2, 100f);

        var state = ServerVotes.State(vote, 1, 0, 100f, VoteKind.NextMap);

        Assert.Equal("dm_a\ndm_b", state.Options);
        Assert.Equal([(byte)0, (byte)1], state.Counts);
    }

    [Fact]
    public void AYesNoStateHasNoChoicesAndItsTwoCounts()
    {
        var (vote, _) = Make();
        vote.StartVote(Caller, new VoteIssue("restart", "", "mp_restartgame 1"), 100f);

        var state = ServerVotes.State(vote, 1, 0, 100f, VoteKind.Restart);

        Assert.Equal("", state.Options);
        Assert.Equal([(byte)1, (byte)0], state.Counts);
    }

    [Fact]
    public void AWordAloneNamesTheChoiceWhoseIdIsTheWord()
    {
        VoteOption[] options = [TwoMaps, new(VoteKind.NextMap, "dm_a", "dm_a", "nextlevel dm_a"), new(VoteKind.Extend, "extend", "", "mp_timelimit 40")];

        Assert.Same(TwoMaps, VoteOptionWords.Find(options, ["nextmap"]));
        Assert.Equal("dm_a", VoteOptionWords.Find(options, ["nextmap", "dm_a"])!.Id);
        Assert.Equal(VoteKind.Extend, VoteOptionWords.Find(options, ["extend"])!.Kind);
        Assert.Null(VoteOptionWords.Find(options, ["map", "dm_a"]));
        Assert.Null(VoteOptionWords.Find(options, ["nextmap", "nextmap", "x"]));
    }
}

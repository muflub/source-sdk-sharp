using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The two team-and-vote entities C# owns: the one that moves a player between
/// teams, and the one that runs a ballot.
///
/// Both are tested for the arithmetic rather than the plumbing, because that is
/// where being subtly wrong is invisible. A team key resolved to the wrong number
/// silently puts everybody on Combine; a quorum that rounds the other way passes
/// a vote nobody agreed to, and only on the servers with an awkward number of
/// players.
/// </summary>
public class TeamTests
{
    private static readonly EntityHandle Rule  = new(0x0041_0041u);
    private static readonly EntityHandle Ctrl  = new(0x0042_0042u);
    private static readonly EntityHandle Ctrl2 = new(0x0043_0043u);
    private static readonly EntityHandle Ctrl3 = new(0x0044_0044u);

    private static readonly EntityHandle Caller = new(0x0051_0051u);
    private static readonly EntityHandle Voter2 = new(0x0052_0052u);
    private static readonly EntityHandle Voter3 = new(0x0053_0053u);

    //-------------------------------------------------------------------------
    // game_player_team
    //-------------------------------------------------------------------------

    [Fact]
    public void TeamNames()
    {
        Assert.True(GamePlayerTeam.TeamNumberFor("Combine") == 2, "Combine is team 2");
        Assert.True(GamePlayerTeam.TeamNumberFor("Rebels") == 3, "Rebels is team 3");
        Assert.True(GamePlayerTeam.TeamNumberFor("Spectator") == 1, "Spectator is team 1");

        // Unassigned is a real team a player can be moved to, so it must not be
        // confused with "no team named".
        Assert.True(GamePlayerTeam.TeamNumberFor("Unassigned") == 0, "Unassigned is team 0");

        // Map data is inconsistent about case everywhere else, and this key is
        // no exception.
        Assert.True(GamePlayerTeam.TeamNumberFor("rEBELS") == 3, "a team name is matched whatever its case");

        // A map written against SetTeam rather than against the stock chain.
        Assert.True(GamePlayerTeam.TeamNumberFor("2") == 2, "a bare number is a team number");

        Assert.True(GamePlayerTeam.TeamNumberFor("") == -1, "no key names no team");
        Assert.True(GamePlayerTeam.TeamNumberFor("Aperture") == -1, "a name that is not a team names no team");

        // The number a mapper reaches for after seeing four teams and counting
        // from one. Out of range must fail rather than wrap.
        Assert.True(GamePlayerTeam.TeamNumberFor("4") == -1, "a number past the last team names no team");
        Assert.True(GamePlayerTeam.TeamNumberFor("-1") == -1, "a negative number names no team");
    }

    [Fact]
    public void PlayerTeamKeysAndInputs()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(GamePlayerTeam).Assembly);
        registry.Services = Ignoring("Console.FindConVarValue", "Entities.FireInput", "Host.RandomFloat", "Players.GetHandle", "Players.GetTeam", "Entities.IsValid");

        // The key the stock entity spends on a game_team_master lookup, holding
        // the team directly.
        Assert.True(registry.KeyValue(Rule, EntityNames.GamePlayerTeam, "target", "Combine"), "the target key binds");

        registry.Spawn(Rule, EntityNames.GamePlayerTeam);

        var vars = registry.DescribeInstances();
        string? target = null;
        foreach (var instance in vars)
        {
            if (instance.Handle != Rule.Value)
                continue;

            foreach (var v in instance.Vars)
            {
                if (string.Equals(v.Name, "target", StringComparison.OrdinalIgnoreCase))
                    target = v.Value;
            }
        }

        Assert.True(target == "Combine", "and holds what the map wrote");

        // Claiming Use is what stops the shell recursing: CBaseEntity's own Use
        // input handler calls Use(), so a class that declines it loops. See the
        // divergence on the Use key.
        Assert.True(registry.Input(Rule, "Use", "", 0f, Caller), "Use is claimed by managed code");
    }

    //-------------------------------------------------------------------------
    // vote_controller's index sequence: one per module.
    [Fact]
    public void VoteIndicesRiseAndTwoModulesDoNotShareThem()
    {
        var mine = new VoteIndices();
        var theirs = new VoteIndices();

        Assert.Equal(0, theirs.Next());
        Assert.Equal(1, theirs.Next());
        Assert.Equal(0, mine.Next());
    }

    // vote_controller, as arithmetic
    //-------------------------------------------------------------------------

    [Fact]
    public void BallotArithmetic()
    {
        // Yes/No. A tie is not agreement, and this is the case a "highest count
        // wins" implementation gets wrong.
        Assert.True(VoteController.LeadingOption(new[] { 3, 1, 0, 0, 0 }, true) == 0, "Yes ahead of No wins");
        Assert.True(VoteController.LeadingOption(new[] { 1, 3, 0, 0, 0 }, true) == 1, "No ahead of Yes wins");
        Assert.True(VoteController.LeadingOption(new[] { 2, 2, 0, 0, 0 }, true) == 1, "a tie goes to No");
        Assert.True(VoteController.LeadingOption(new int[5], true) == 1, "an empty yes/no ballot goes to No");

        // A ballot with more choices has no privileged option: the most popular
        // wins, and nothing winning is a real answer.
        Assert.True(VoteController.LeadingOption(new[] { 1, 4, 2, 0, 0 }, false) == 1, "the most popular choice wins");
        Assert.True(VoteController.LeadingOption(new int[5], false) == -1, "an empty ballot has no winner");

        // The quorum, with stock's truncation. Nine potential voters at the
        // default ratio need 5.4, which truncates to 5 — so five is enough and
        // rounding up would have made it six.
        Assert.True(VoteController.Passes(votesCast: 5, potentialVoters: 9, quorumRatio: 0.6f,
                                  leadingOption: 0, yesNo: true), "five of nine meets the default quorum");
        Assert.True(!VoteController.Passes(votesCast: 4, potentialVoters: 9, quorumRatio: 0.6f,
                                   leadingOption: 0, yesNo: true), "four of nine does not");

        // Quorum is necessary, not sufficient: the whole server turning out to
        // vote No must not pass the motion.
        Assert.True(!VoteController.Passes(votesCast: 9, potentialVoters: 9, quorumRatio: 0.6f,
                                   leadingOption: 1, yesNo: true), "a quorum voting No fails");

        // And on a multi-choice ballot, a quorum plus any winner is the test.
        Assert.True(VoteController.Passes(votesCast: 9, potentialVoters: 9, quorumRatio: 0.6f,
                                  leadingOption: 2, yesNo: false), "a quorum with a winner passes a multi-choice ballot");
        Assert.True(!VoteController.Passes(votesCast: 0, potentialVoters: 0, quorumRatio: 0.6f,
                                   leadingOption: -1, yesNo: false), "a multi-choice ballot with no winner fails");
    }

    [Fact]
    public void VoterEligibility()
    {
        Assert.True(!VoteController.IsVoterTeam(0, allowSpectators: true), "somebody who has not picked a side gets no vote");
        Assert.True(!VoteController.IsVoterTeam(1, allowSpectators: false), "a spectator gets no vote by default");
        Assert.True(VoteController.IsVoterTeam(1, allowSpectators: true), "and does when the server allows it");
        Assert.True(VoteController.IsVoterTeam(2, allowSpectators: false), "somebody on a real team gets a vote");
    }

    //-------------------------------------------------------------------------
    // vote_controller, as a sequence
    //
    // No engine, so no player occupies a slot and the ballot opens with nobody
    // able to vote. That is what makes the sequence testable: with no potential
    // voters the quorum is met by anything, so what these check is the ORDER of
    // the machine -- open, count once each, close, decide, clear -- rather than
    // the arithmetic above.
    //-------------------------------------------------------------------------

    private static VoteController Make(EntityHandle handle)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(VoteController).Assembly);
        registry.Services = Ignoring("Console.FindConVarValue", "Entities.FireInput", "Host.RandomFloat", "Players.GetHandle", "Players.GetTeam", "Entities.IsValid")
            .AddInstance<IVoteIndices>(new VoteIndices());
        registry.Spawn(handle, EntityNames.VoteController);

        return (VoteController)registry.Get(handle)!;
    }

    [Fact]
    public void AVoteThatFails()
    {
        var vote = Make(Ctrl);

        Assert.True(!vote.IsVoteActive, "an idle controller has nothing to vote on");
        Assert.True(vote.Cast(Caller, 1, 100f) == VoteCast.NoActiveIssue, "and refuses a ballot");

        Assert.True(vote.StartVote(Caller, new VoteIssue("nextlevel", "dm_lockdown", ""), 100f), "a vote can be called");
        Assert.True(vote.IsVoteActive, "and is then running");
        Assert.True(vote.VoteIndex >= 0, "and has a serial number");
        Assert.True(vote.Holder == Caller, "and remembers who called it");

        // Calling a yes/no vote is agreeing with it, which is why the caller's
        // vote is already in.
        Assert.True(vote.VotesFor(1) == 1, "calling a yes/no vote casts a Yes for the caller");

        Assert.True(!vote.StartVote(Voter2, new VoteIssue("kick", "", ""), 100f), "a second vote cannot be called over the first");

        Assert.True(vote.Cast(Caller, 2, 100.5f) == VoteCast.AlreadyVoted, "nobody votes twice");
        Assert.True(vote.Cast(Voter2, 2, 100.5f) == VoteCast.Ok, "somebody else may vote");
        Assert.True(vote.Cast(Voter3, 6, 100.5f) == VoteCast.BadOption, "a choice off the end of the ballot is refused");
        Assert.True(vote.Cast(Voter3, 0, 100.5f) == VoteCast.BadOption, "and so is one before the start");

        // Everybody has voted, so the ballot closes a second later rather than
        // running the full timer out.
        vote.Tick(101f);
        Assert.True(vote.Outcome == VoteOutcome.Undecided, "the result is not in while the ballot is open");

        vote.Tick(102f);
        Assert.True(vote.Outcome == VoteOutcome.Failed, "one Yes and one No is a tie, and a tie fails");
        Assert.True(vote.IsVoteActive, "the result stays up for a moment");

        vote.Tick(107f);
        Assert.True(!vote.IsVoteActive, "and then the controller is idle again");
        Assert.True(vote.ActiveIssue is null, "with nothing to vote on");

        // The cooldown outlives the vote it was earned by, which is the whole
        // point of it: a caller whose vote just failed must not immediately call
        // another.
        Assert.True(!vote.StartVote(Caller, new VoteIssue("nextlevel", "", ""), 108f), "the caller is on cooldown afterwards");
        Assert.True(!vote.CanCallVote(Caller, 108f, out float wait) && wait > 0f, "and is told how long is left");
        Assert.True(vote.StartVote(Voter2, new VoteIssue("nextlevel", "", ""), 108f), "but somebody who has not called one may");
    }

    [Fact]
    public void AVoteThatPasses()
    {
        var vote = Make(Ctrl2);

        vote.StartVote(Caller, new VoteIssue("nextlevel", "dm_lockdown", ""), 100f);

        // A choice past the end of a yes/no ballot is a No rather than an error:
        // a stale option number from a client must not be able to pass a motion.
        Assert.True(vote.Cast(Voter2, 4, 100f) == VoteCast.Ok, "a high choice on a yes/no ballot is a No");
        Assert.True(vote.VotesFor(2) == 1, "and lands on No");
        Assert.True(vote.VotesCast == 2, "both votes are counted");

        vote.Cast(Voter3, 1, 100f);

        vote.Tick(102f);
        Assert.True(vote.Outcome == VoteOutcome.Passed, "two Yes to one No passes");

        // The ballot is shut once the result is in, even though the controller
        // is still showing it.
        Assert.True(vote.Cast(new EntityHandle(0x54u), 1, 102.5f) == VoteCast.Closed, "a late vote is refused");
    }

    [Fact]
    public void AVoteWithMoreThanTwoChoices()
    {
        var vote = Make(Ctrl3);

        var issue = new VoteIssue("nextlevel", "dm_lockdown", "",
                                  Options: new[] { "dm_overwatch", "dm_powerhouse", "dm_runoff" });

        vote.StartVote(Caller, issue, 100f);

        // Nobody is forced to agree with a multi-choice vote: there is no "yes"
        // to be held to.
        Assert.True(vote.VotesCast == 0, "a multi-choice vote does not vote for its caller");

        vote.Cast(Caller, 2, 100f);
        vote.Cast(Voter2, 2, 100f);
        vote.Cast(Voter3, 3, 100f);

        vote.Tick(102f);
        Assert.True(vote.Outcome == VoteOutcome.Passed, "the most popular choice carries");
        Assert.True(vote.ActiveIssue?.Details == "dm_powerhouse", "and becomes what the vote was about");
    }
}

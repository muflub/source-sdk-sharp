using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's general ballot (plan_bots_teams_tf2 D.3): a multiple-choice
/// state's choices and counts, the kinds' titles, and a refusal with the
/// server's reason.
/// </summary>
public class ClientVoteBallotTests
{
    private sealed class Pointer : IRadialPointer
    {
        public void Centre()
        {
        }
    }

    private sealed class Clock : IGameClock
    {
        public float Now => 100f;
    }

    private static ClientVotes Votes()
        => new(new RadialMenus(new Pointer()), null!, null!, new ChatFeedTests.RecordingEngineClient([]), new Clock(), null!, new RecordingLog());

    [Fact]
    public void AChoiceStateShowsItsChoicesAndCounts()
    {
        var votes = Votes();

        votes.OnVoteState(new VoteState
        {
            Phase = (byte)VotePhase.Voting, Kind = (byte)VoteKind.NextMap, Detail = "", Options = "dm_a\nss_ai_tdm\ndm_c", Counts = [1, 3, 0],
        });

        Assert.Equal(["dm_a", "ss_ai_tdm", "dm_c"], votes.Options);
        Assert.Equal((1, 3, 0, 0), (votes.CountOf(1), votes.CountOf(2), votes.CountOf(3), votes.CountOf(4)));
        Assert.Equal("#SS_UI_Vote_NextMapTitle", votes.Title);
    }

    [Fact]
    public void AYesNoStateHasNoChoices()
    {
        var votes = Votes();

        votes.OnVoteState(new VoteState { Phase = (byte)VotePhase.Voting, Kind = (byte)VoteKind.Extend, Detail = "", Options = "", Counts = [1, 0] });

        Assert.Empty(votes.Options);
        Assert.Equal("#SS_UI_Vote_ExtendTitle", votes.Title);
    }

    [Fact]
    public void ARefusalCarriesTheServersReason()
    {
        var votes = Votes();

        votes.OnVoteCooldown(new VoteCooldown { Refusal = (byte)VoteRefusal.NotNow, Reason = "dm_lockdown has no item_teamflag" });

        Assert.Equal("#SS_UI_Vote_Refused_NotNow", votes.Refusal);
        Assert.Equal("dm_lockdown has no item_teamflag", votes.RefusalReason);
    }

    [Fact]
    public void AnAcceptedCallHasNoReason()
    {
        var votes = Votes();
        votes.OnVoteCooldown(new VoteCooldown { Refusal = (byte)VoteRefusal.NotNow, Reason = "no" });

        votes.OnVoteCooldown(new VoteCooldown { Refusal = (byte)VoteRefusal.None, Seconds = 90f, Reason = "" });

        Assert.Null(votes.Refusal);
        Assert.Equal("", votes.RefusalReason);
    }

    [Fact]
    public void AFailedIssuesWaitIsNotTheCallersCooldown()
    {
        var votes = Votes();

        votes.OnVoteCooldown(new VoteCooldown { Refusal = (byte)VoteRefusal.FailedLately, Seconds = 200f, Reason = "wait 3:20" });

        Assert.Equal(0f, votes.CooldownLeft);
        Assert.Equal(("#SS_UI_Vote_Refused_FailedLately", "wait 3:20"), (votes.Refusal, votes.RefusalReason));
    }

    [Fact]
    public void TheCallersOwnCooldownStillHolds()
    {
        var votes = Votes();

        votes.OnVoteCooldown(new VoteCooldown { Refusal = (byte)VoteRefusal.Cooldown, Seconds = 90f, Reason = "" });

        Assert.Equal(90f, votes.CooldownLeft);
    }
}

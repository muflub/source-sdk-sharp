using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>The client's side of a vote: what the panel shows and what the wait says.</summary>
public class ClientVotesTests
{
    [Theory]
    [InlineData(134f, "Wait 2:14")]
    [InlineData(59.2f, "Wait 1:00")]
    [InlineData(5f, "Wait 0:05")]
    public void The_wait_reads_in_minutes_and_seconds_rounded_up(float seconds, string text)
        => Assert.Equal(text, ClientVotes.Wait(seconds));

    [Fact]
    public void The_two_sides_agree_on_the_phases_wire_values()
    {
        Assert.Equal(0, (byte)VotePhase.Idle);
        Assert.Equal(1, (byte)VotePhase.Voting);
        Assert.Equal(0, (byte)VoteRefusal.None);
    }
}

/// <summary>The vote key at the main menu: a vote is a level's.</summary>
public class ClientVotesOpenTests
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

    private readonly ChatFeedTests.RecordingEngineClient _engine = new([]);
    private readonly RadialMenus _radial = new(new Pointer());

    private ClientVotes Votes() => new(_radial, null!, null!, _engine, new Clock(), null!, null!);

    [Fact]
    public void In_a_game_the_key_opens_the_ring()
    {
        Votes().Open();
        Assert.True(_radial.Showing);
    }

    [Fact]
    public void Out_of_a_game_the_key_opens_nothing()
    {
        _engine.InGame = false;
        Votes().Open();
        Assert.False(_radial.Showing);
    }

    [Fact]
    public void Leaving_the_game_closes_the_ring()
    {
        Votes().Open();
        _engine.InGame = false;
        Assert.False(_radial.Showing);
    }

    private static void Options(ClientVotes votes, params AdminMenuLine[] lines)
    {
        var pieces = AdminMenuText.Pieces(AdminMenuText.Write(lines));
        for (int i = 0; i < pieces.Count; i++)
            votes.OnVoteOptionsPiece(new VoteOptionsPiece { Index = (byte)i, Count = (byte)pieces.Count, Text = pieces[i] });
    }

    private static AdminMenuLine Map(string name) => new((int)VoteKind.Map, name, name);

    private static AdminMenuLine Mode(string id) => new((int)VoteKind.Mode, id, id);

    private static readonly AdminMenuLine Restart = new((int)VoteKind.Restart, "restart", "");

    private readonly List<string> _sent = [];

    private ClientVotes Calling()
    {
        var engine = new ChatFeedTests.RecordingEngineClient(_sent);
        return new ClientVotes(_radial, null!, null!, engine, new Clock(), null!, new RecordingLog());
    }

    [Fact]
    public void The_call_page_offers_what_the_server_listed()
    {
        var votes = Calling();
        Options(votes, Map("dm_a"), Map("dm_b"), Mode("arena"), Restart);
        votes.Open();
        Assert.Equal(["#SS_UI_Vote_KickPlayer", "#SS_UI_Vote_ChangeMap", "#SS_UI_Vote_Mode", "#SS_UI_Vote_Settings", "#SS_UI_Vote_Restart"],
                     Enumerable.Range(0, _radial.ItemCount).Select(i => _radial.Item(i).Label));
    }

    [Fact]
    public void A_map_is_called_from_its_own_ring()
    {
        var votes = Calling();
        Options(votes, Map("dm_a"), Map("dm_b"));
        votes.Open();
        _radial.Choose(1);
        Assert.Equal("#SS_UI_Vote_MapWhich", _radial.Title);
        _radial.Choose(1);
        Assert.Equal(["ss_callvote map dm_b"], _sent);
    }

    [Fact]
    public void A_mode_is_called_from_its_own_ring()
    {
        var votes = Calling();
        Options(votes, Mode("arena"));
        votes.Open();
        _radial.Choose(2);
        _radial.Choose(0);
        Assert.Equal(["ss_callvote mode arena"], _sent);
    }

    [Fact]
    public void A_restart_is_called_straight_away()
    {
        var votes = Calling();
        Options(votes, Restart);
        votes.Open();
        _radial.Choose(4);
        Assert.Equal(["ss_callvote restart"], _sent);
    }

    [Fact]
    public void Nothing_listed_of_a_kind_cannot_be_opened_and_no_restart_is_no_place()
    {
        var votes = Calling();
        votes.Open();
        Assert.Equal(4, _radial.ItemCount);
        Assert.False(_radial.Item(1).Enabled);
        Assert.False(_radial.Item(2).Enabled);
        Assert.False(_radial.Item(3).Enabled);
    }

    [Fact]
    public void A_long_list_comes_back_whole_from_its_pieces()
    {
        var votes = Calling();
        Options(votes, [.. Enumerable.Range(0, 60).Select(i => Map($"some_long_map_name_{i}"))]);
        votes.Open();
        Assert.True(_radial.Item(1).Enabled);
        _radial.Choose(1);
        Assert.True(_radial.ItemCount > 0);
    }

    [Fact]
    public void While_a_vote_is_on_or_the_wait_runs_nothing_can_be_called()
    {
        var votes = Calling();
        Options(votes, Map("dm_a"), Restart);
        votes.OnVoteCooldown(new VoteCooldown { Refusal = (byte)VoteRefusal.None, Seconds = 90f });
        votes.Open();
        Assert.All(Enumerable.Range(0, _radial.ItemCount), i => Assert.False(_radial.Item(i).Enabled));
    }

    [Theory]
    [InlineData(VoteKind.Map, "dm_a", "#SS_UI_Vote_MapTitle", "dm_a")]
    [InlineData(VoteKind.Mode, "arena", "#SS_UI_Vote_ModeTitle", "arena")]
    [InlineData(VoteKind.Restart, "", "#SS_UI_Vote_RestartTitle", "")]
    public void The_panel_says_what_the_ballot_would_do(VoteKind kind, string detail, string title, string target)
    {
        var votes = Calling();
        votes.OnVoteState(new VoteState { Phase = (byte)VotePhase.Voting, Kind = (byte)kind, Detail = detail });
        Assert.Equal(title, votes.Title);
        Assert.Equal(target, votes.Target);
    }

    [Fact]
    public void A_restart_s_ballot_says_what_it_is_voting_on()
    {
        var votes = Calling();
        votes.OnVoteState(new VoteState { Phase = (byte)VotePhase.Voting, Kind = (byte)VoteKind.Restart, Detail = "" });
        votes.Open();
        Assert.Equal("#SS_UI_Vote_RestartTitle", _radial.Item(0).Detail);
    }

    private static AdminMenuLine Setting(string id, string label) => new((int)VoteKind.Setting, id, label);

    [Fact]
    public void A_setting_is_called_through_a_ring_of_settings_and_one_of_its_values()
    {
        var votes = Calling();
        Options(votes, Setting("mp_friendlyfire=1", "#SS_UI_Vote_On"), Setting("mp_timelimit=10", "#SS_UI_Vote_Min10"), Setting("mp_timelimit=20", "#SS_UI_Vote_Min20"));
        votes.Open();
        _radial.Choose(3);
        Assert.Equal("#SS_UI_Vote_SettingsWhich", _radial.Title);
        Assert.Equal(["#SS_UI_Vote_Setting_mp_friendlyfire", "#SS_UI_Vote_Setting_mp_timelimit"], Enumerable.Range(0, _radial.ItemCount).Select(i => _radial.Item(i).Label));

        _radial.Choose(1);
        Assert.Equal("#SS_UI_Vote_Setting_mp_timelimit", _radial.Title);
        Assert.Equal("#SS_UI_Vote_Min20", _radial.Item(1).Label);

        _radial.Choose(1);
        Assert.Equal(["ss_callvote setting mp_timelimit=20"], _sent);
    }

    [Fact]
    public void The_panel_names_the_setting_and_its_value()
    {
        var votes = Calling();
        Options(votes, Setting("mp_timelimit=20", "#SS_UI_Vote_Min20"));
        votes.OnVoteState(new VoteState { Phase = (byte)VotePhase.Voting, Kind = (byte)VoteKind.Setting, Detail = "mp_timelimit=20" });
        Assert.Equal("#SS_UI_Vote_Setting_mp_timelimit", votes.Title);
        Assert.Equal("#SS_UI_Vote_Min20", votes.Target);
        Assert.Equal("#SS_UI_Vote_Setting_mp_timelimit", votes.ResultSubject);
    }

    [Fact]
    public void A_setting_s_value_the_list_no_longer_has_reads_as_itself()
    {
        var votes = Calling();
        votes.OnVoteState(new VoteState { Phase = (byte)VotePhase.Voting, Kind = (byte)VoteKind.Setting, Detail = "mp_fraglimit=20" });
        Assert.Equal("20", votes.Target);
    }

    [Fact]
    public void Only_a_setting_s_result_needs_saying_what_it_is_about()
    {
        var votes = Calling();
        votes.OnVoteState(new VoteState { Phase = (byte)VotePhase.Voting, Kind = (byte)VoteKind.Map, Detail = "dm_a" });
        Assert.Null(votes.ResultSubject);
    }
}

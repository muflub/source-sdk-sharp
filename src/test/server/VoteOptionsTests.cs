using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>What can be voted on, and that a call can only name it.</summary>
public class VoteOptionsTests
{
    private static List<VoteOption> All()
        => GameVoteOptions.Of(["dm_a", "dm_b"], ["arena", "duel"], "duel", true, true, true);

    [Fact]
    public void The_list_holds_the_maps_the_modes_but_the_one_on_and_a_restart()
    {
        Assert.Equal(["dm_a", "dm_b", "arena", "restart"], All().Select(o => o.Id));
    }

    [Fact]
    public void A_map_listed_twice_is_one_choice()
        => Assert.Single(GameVoteOptions.Of(["dm_a", "DM_A"], [], null, true, true, false));

    [Theory]
    [InlineData(false, true, true, "arena,restart")]
    [InlineData(true, false, true, "dm_a,dm_b,restart")]
    [InlineData(true, true, false, "dm_a,dm_b,arena")]
    [InlineData(false, false, false, "")]
    public void Each_kind_has_its_convar(bool maps, bool modes, bool restart, string ids)
        => Assert.Equal(ids, string.Join(',', GameVoteOptions.Of(["dm_a", "dm_b"], ["arena", "duel"], "duel", maps, modes, restart).Select(o => o.Id)));

    private static List<VoteOption> Settings(Func<string, string?> current)
        => GameVoteOptions.Of([], [], null, false, false, false, GameVoteOptions.CoreSettings(), current);

    private static readonly Func<string, string?> Stock = c => c switch { "mp_friendlyfire" => "0", "mp_timelimit" => "30", "mp_fraglimit" => "0", _ => null };

    [Fact]
    public void A_setting_offers_every_value_but_the_one_it_holds()
    {
        var ids = Settings(Stock).Select(o => o.Id).ToList();
        Assert.Contains("mp_friendlyfire=1", ids);
        Assert.DoesNotContain("mp_friendlyfire=0", ids);
        Assert.Contains("mp_timelimit=10", ids);
        Assert.DoesNotContain("mp_timelimit=30", ids);
    }

    [Fact]
    public void A_value_written_another_way_is_still_the_one_it_holds()
    {
        var ids = Settings(c => c == "mp_timelimit" ? "30.000000" : null).Select(o => o.Id).ToList();
        Assert.DoesNotContain("mp_timelimit=30", ids);
        Assert.Contains("mp_timelimit=20", ids);
    }

    [Fact]
    public void A_setting_this_game_does_not_have_is_no_choice()
        => Assert.DoesNotContain(Settings(c => c == "mp_timelimit" ? "30" : null), o => o.Id.StartsWith("mp_fraglimit", StringComparison.Ordinal));

    [Fact]
    public void With_settings_off_there_are_none()
        => Assert.DoesNotContain(GameVoteOptions.Of([], [], null, true, true, true, [], Stock), o => o.Kind == VoteKind.Setting);

    [Fact]
    public void A_setting_runs_its_convar_with_its_value()
    {
        var options = Settings(Stock);
        Assert.Equal("mp_friendlyfire 1", VoteOptionWords.Find(options, ["setting", "mp_friendlyfire=1"])!.Command);
        Assert.Equal("mp_timelimit 60", VoteOptionWords.Find(options, ["setting", "mp_timelimit=60"])!.Command);
    }

    /// <summary>The list is the whole of what can be set: not another convar, not another value.</summary>
    [Theory]
    [InlineData("rcon_password=x")]
    [InlineData("sv_cheats=1")]
    [InlineData("mp_timelimit=7")]
    [InlineData("mp_timelimit=30")]
    [InlineData("mp_friendlyfire=1;quit")]
    [InlineData("mp_friendlyfire")]
    public void A_setting_or_value_not_listed_finds_nothing(string id)
        => Assert.Null(VoteOptionWords.Find(Settings(Stock), ["setting", id]));

    private static List<AdminNode> AdminSettings(List<string> ran)
        => AdminMenuBuilder.Settings(GameVoteOptions.CoreSettings(), Stock, ran.Add);

    [Fact]
    public void The_admin_menu_has_a_folder_for_each_setting_the_game_has()
        => Assert.Equal(["#SS_UI_Vote_Setting_mp_friendlyfire", "#SS_UI_Vote_Setting_mp_timelimit", "#SS_UI_Vote_Setting_mp_fraglimit"],
                        AdminSettings([]).Select(n => n.Label));

    [Fact]
    public void An_admin_s_choice_runs_with_no_vote()
    {
        var ran = new List<string>();
        AdminNode.Find(AdminSettings(ran), "setting:mp_timelimit=20")!.Run!();
        Assert.Equal(["mp_timelimit 20"], ran);
    }

    [Theory]
    [InlineData("setting:mp_timelimit=30")]
    [InlineData("setting:mp_timelimit=7")]
    [InlineData("setting:sv_cheats=1")]
    public void An_admin_cannot_run_what_the_menu_does_not_list(string id)
        => Assert.Null(AdminNode.Find(AdminSettings([]), id));

    [Fact]
    public void The_admin_menu_s_settings_cross_the_wire_three_rings_deep()
    {
        var nodes = new List<AdminNode> { AdminNode.Folder("#SS_UI_Vote_Settings", AdminSettings([])) };
        var lines = AdminMenuText.Read(AdminMenuText.Write(AdminNode.Flatten(nodes)));
        Assert.Contains(lines, l => l.Depth == 2 && l.Id == "setting:mp_friendlyfire=1" && l.Label == "#SS_UI_Vote_On");
    }

    [Fact]
    public void Each_runs_its_own_server_command()
    {
        Assert.Equal("changelevel dm_b", VoteOptionWords.Find(All(), ["map", "dm_b"])!.Command);
        Assert.Equal("sv_mode arena", VoteOptionWords.Find(All(), ["mode", "arena"])!.Command);
        Assert.Equal("mp_restartgame 1", VoteOptionWords.Find(All(), ["restart"])!.Command);
    }

    /// <summary>The command comes from the server's list: what a client names only picks from it.</summary>
    [Theory]
    [InlineData("map", "dm_c")]
    [InlineData("map", "dm_a;quit")]
    [InlineData("map", "dm_a quit")]
    [InlineData("map", "DM_A")]
    [InlineData("mode", "duel")]
    [InlineData("mode", "dm_a")]
    [InlineData("rcon", "quit")]
    [InlineData("restart", "now")]
    public void A_call_naming_anything_else_finds_nothing(string word, string id)
        => Assert.Null(VoteOptionWords.Find(All(), [word, id]));

    [Fact]
    public void No_words_find_nothing()
        => Assert.Null(VoteOptionWords.Find(All(), []));

    [Fact]
    public void A_restart_the_server_does_not_allow_is_not_found()
        => Assert.Null(VoteOptionWords.Find(GameVoteOptions.Of([], [], null, true, true, false), ["restart"]));
}

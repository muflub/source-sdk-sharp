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

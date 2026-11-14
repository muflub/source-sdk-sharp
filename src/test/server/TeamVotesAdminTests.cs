using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TeamDeathmatchTests;

namespace SourceSharp.Tests;

/// <summary>
/// Votes and the admin menu for teams (user 2026-11-08: "make sure to add vote
/// and admin menus for teams"): team deathmatch on the mode issue under its
/// name, the team settings as vote settings, the scramble issue (refused
/// without teams, even teams with them), and the admin Teams folder's move
/// (which kills) and balance.
/// </summary>
public class TeamVotesAdminTests
{
    private static readonly EntityHandle[] Four = [CombineA, CombineB, RebelA, Fresh];

    //-------------------------------------------------------------------------
    // The mode issue and the settings.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheModeIssueListsTeamDeathmatchByItsName()
    {
        var options = GameVoteOptions.Of([], [GameModes.NullModeId, TeamDeathmatchMode.ModeId], GameModes.NullModeId,
                                         false, true, false);

        var tdm = Assert.Single(options);
        Assert.Equal((VoteKind.Mode, "tdm", "#SS_UI_Mode_TeamDeathmatch", "sv_mode tdm"), (tdm.Kind, tdm.Id, tdm.Label, tdm.Command));
        Assert.Equal("#SS_UI_Mode_Deathmatch", GameVoteOptions.ModeLabel(GameModes.NullModeId));
        Assert.Equal("ctf", GameVoteOptions.ModeLabel("ctf"));
    }

    private static readonly Func<string, string?> Current = c => c switch
    {
        "mp_tdm_teams" => "2",
        "mp_teams_unbalance_limit" => "0",
        "mp_friendlyfire" => "0",
        _ => null,
    };

    [Fact]
    public void TheTeamCountIsAVoteSettingThatRunsItsConvar()
    {
        var options = GameVoteOptions.Of([], [], null, false, false, false, GameVoteOptions.CoreSettings(), Current);

        Assert.Equal("mp_tdm_teams 4", VoteOptionWords.Find(options, ["setting", "mp_tdm_teams=4"])!.Command);
        Assert.Equal("mp_teams_unbalance_limit 1", VoteOptionWords.Find(options, ["setting", "mp_teams_unbalance_limit=1"])!.Command);
        Assert.Null(VoteOptionWords.Find(options, ["setting", "mp_tdm_teams=2"]));
    }

    /// <summary>The collide lane's convar is a row before it lands: absent, it offers nothing.</summary>
    [Fact]
    public void TeammatesCollideIsOfferedOnlyWhereTheGameHasIt()
    {
        var without = GameVoteOptions.Of([], [], null, false, false, false, GameVoteOptions.CoreSettings(), Current);
        var with = GameVoteOptions.Of([], [], null, false, false, false, GameVoteOptions.CoreSettings(),
                                      c => c == "mp_teammates_collide" ? "0" : Current(c));

        Assert.DoesNotContain(without, o => o.Id.StartsWith("mp_teammates_collide", StringComparison.Ordinal));
        Assert.Contains(with, o => o.Id == "mp_teammates_collide=1");
    }

    //-------------------------------------------------------------------------
    // The scramble.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheScrambleIssueIsOfferedOnlyWhenAllowedAndNamedByItsWord()
    {
        var with = GameVoteOptions.Of([], [], null, false, false, false, allowScramble: true);
        var without = GameVoteOptions.Of([], [], null, false, false, false);

        Assert.Equal(TeamScrambleCommand.Name, VoteOptionWords.Find(with, ["scramble"])!.Command);
        Assert.Null(VoteOptionWords.Find(without, ["scramble"]));
    }

    [Fact]
    public void ScrambleIsRefusedUnderDeathmatch()
    {
        var rig = new Rig();

        new TeamScrambleCommand(rig.Log, rig.Modes).Execute();

        Assert.Empty(rig.TeamTable.Assigned);
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("cs_sv_scramble_teams refused"));
    }

    [Fact]
    public void ScrambleEvensTheTeamsUnderTdmAndRestartsTheRound()
    {
        var rig = new Rig();
        rig.TeamCount(3);
        foreach (var p in Four)
            rig.Teams[p] = TeamDeathmatchMode.TeamCombine;
        rig.Tdm();

        new TeamScrambleCommand(rig.Log, rig.Modes).Execute();

        var sizes = Enumerable.Range(2, 3).Select(t => Four.Count(p => rig.Teams[p] == t)).ToArray();
        Assert.True(sizes.Max() - sizes.Min() <= 1, $"teams of {string.Join('/', sizes)}");
        Assert.Equal(2, rig.Log.Lines.Count(l => l.Text.Contains("switched to 'tdm'")));
    }

    //-------------------------------------------------------------------------
    // The admin Teams folder.
    //-------------------------------------------------------------------------

    [Fact]
    public void BalanceMovesTheNewestArrivalsOffTheLargestTeam()
    {
        var rig = new Rig();
        foreach (var p in Four)
            rig.Teams[p] = TeamDeathmatchMode.TeamCombine;
        rig.Host.ReturnsEachCall("Players.GetUserId", () => (int)((EntityHandle)rig.Host.Calls[^1].Args[0]!).Value & 0xFFFF);
        rig.Tdm();

        rig.Mode.Balance();

        Assert.Equal((2, 2), (Four.Count(p => rig.Teams[p] == 2), Four.Count(p => rig.Teams[p] == 3)));
        Assert.Equal([RebelA, Fresh], Four.Where(p => rig.Teams[p] == 3));
    }

    [Fact]
    public void TheTeamsFolderIsAbsentWithoutTeams()
    {
        var rig = new TeamDeathmatchJoinTests.JoinRig(GameModes.NullModeId);
        var menu = new TeamAdminMenu(rig.Modes, rig.Teams, rig.Teams, rig.Engine, new PlayerNames(rig.Engine), rig.Engine);

        Assert.Empty(menu.AdminItems(1));
    }

    [Fact]
    public void AnAdminMoveMovesThePlayerAndKillsThem()
    {
        var rig = new TeamDeathmatchJoinTests.JoinRig(TeamDeathmatchMode.ModeId);
        var menu = new TeamAdminMenu(rig.Modes, rig.Teams, rig.Teams, rig.Engine, new PlayerNames(rig.Engine), rig.Engine);
        var items = menu.AdminItems(1).ToList();
        var other = TeamDeathmatchJoinTests.JoinRig.Other;

        var move = AdminNode.Find(items, "teams:move:0:3");
        Assert.NotNull(move);
        move!.Run!();

        Assert.Equal(3, rig.Table[other]);
        Assert.Equal(other, Assert.Single(rig.Engine.AllOf("Damage.Apply")).ArgAs<EntityHandle>(0));
    }

    [Fact]
    public void TheTeamCountChoicesRunTheConvar()
    {
        var rig = new TeamDeathmatchJoinTests.JoinRig(TeamDeathmatchMode.ModeId);
        var menu = new TeamAdminMenu(rig.Modes, rig.Teams, rig.Teams, rig.Engine, new PlayerNames(rig.Engine), rig.Engine);

        AdminNode.Find(menu.AdminItems(1).ToList(), "teams:count:4")!.Run!();

        Assert.Equal("mp_tdm_teams 4\n", rig.Engine.Last("Console.ExecuteServerCommand")!.Value.Args[0]);
        Assert.Null(AdminNode.Find(menu.AdminItems(1).ToList(), "teams:count:2"));
    }
}

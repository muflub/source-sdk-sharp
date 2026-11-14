using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The scoreboard's team totals under team play (plan_bots_teams_tf2 A.3 item 8):
/// the server's team score off the player resource, as stock's header reads
/// <c>GetTeam(i)-&gt;Get_Score()</c> (hl2mpclientscoreboard.cpp:355,386), not
/// the sum of the members' frags. The two differ once a teamkill (the team +1,
/// the killer's frags -1) or a suicide (the team -1) has happened.
/// </summary>
public class ScoreboardTeamScoreTests
{
    private sealed class Standalones : IClientStandalones
    {
        public readonly List<ClientStandaloneEntity> All = new();

        public IReadOnlyList<ClientStandaloneEntity> Live() => All;

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    private sealed class NoNames : IPlayerNames
    {
        public bool TryGet(int slot, out ClientPlayerInfo info)
        {
            info = default;
            return false;
        }
    }

    /// <summary>A resource with players (slot, team, frags) connected.</summary>
    private static (Standalones Standalones, ClientPlayerResource Resource) Roster(params (int Slot, int Team, int Frags)[] players)
    {
        var resource = new ClientPlayerResource(new RecordingEngine());
        resource.State.BindLocal();
        foreach (var (slot, team, frags) in players)
        {
            resource.State.Flags[slot] = PlayerResourceNetState.FlagConnected | PlayerResourceNetState.FlagAlive;
            resource.State.UserId[slot] = (short)(100 + slot);
            resource.State.Team[slot] = (byte)team;
            resource.State.Score[slot] = (short)frags;
        }
        var standalones = new Standalones();
        standalones.All.Add(resource);
        return (standalones, resource);
    }

    private static RecordingEngine Engine()
        => Fake().Returns("Hud.LocalPlayerIndex", 1).Returns("Environment.Globals", default(Globals))
                 .Returns("Console.FindConVarValue", (string?)null);

    private static ScoreboardUi Scores(IClientStandalones standalones, RecordingEngine engine, string? tdmTeams = null)
        => new(standalones, engine, new DefaultForwarders.NoEngineClient(), new NoNames(),
               DeclaredConVars.Fresh<HostnameConVar>(), engine,
               tdmTeams: tdmTeams is null ? null : DeclaredConVars.Holding<ClTdmTeamsConVar>(tdmTeams));

    [Fact]
    public void ATeamsTotalIsTheResourcesTeamScoreNotItsMembersFrags()
    {
        // Combine: frags 5 + 3 = 8, but a teamkill and a suicide left the team on 7.
        var (standalones, resource) = Roster((1, 2, 5), (2, 2, 3), (3, 3, 4));
        resource.State.TeamScore[2] = 7;
        resource.State.TeamScore[3] = 4;
        var scores = Scores(standalones, Engine());

        scores.Refresh();

        Assert.Equal((2, 7), (scores.Team(0).Number, scores.Team(0).Score));
    }

    [Fact]
    public void TheRebelsTotalIsTheResourcesTeamScoreToo()
    {
        var (standalones, resource) = Roster((1, 2, 5), (2, 3, 6), (3, 3, 2));
        resource.State.TeamScore[2] = 5;
        resource.State.TeamScore[3] = 9;
        var scores = Scores(standalones, Engine());

        scores.Refresh();

        Assert.Equal((3, 9), (scores.Team(1).Number, scores.Team(1).Score));
    }

    [Fact]
    public void ATeamScoreChangeIsReadOnTheNextRefresh()
    {
        var (standalones, resource) = Roster((1, 2, 5), (2, 3, 6));
        resource.State.TeamScore[2] = 5;
        var scores = Scores(standalones, Engine());
        scores.Refresh();
        int before = scores.Team(0).Score;

        resource.State.TeamScore[2] = 4;    // a Combine suicide: the team loses one
        scores.Refresh();

        Assert.Equal((5, 4), (before, scores.Team(0).Score));
    }

    [Fact]
    public void TheTeamsAreTitledCombineAndRebels()
    {
        var (standalones, _) = Roster((1, 2, 0), (2, 3, 0));
        var scores = Scores(standalones, Engine());

        scores.Refresh();

        Assert.Equal(("Combine", "Rebels"), (scores.Team(0).Name, scores.Team(1).Name));
    }

    [Fact]
    public void DeathmatchsOneTableStillSumsTheFrags()
    {
        // No team play (everyone on team 0): the resource carries no team score,
        // and the one table's total is the frags, as before.
        var (standalones, _) = Roster((1, 0, 5), (2, 0, 3));
        var scores = Scores(standalones, Engine());

        scores.Refresh();

        Assert.Equal((1, 8), (scores.TeamCount, scores.Team(0).Score));
    }

    [Fact]
    public void TheTeamMenusCardsShowTheTeamScores()
    {
        var (standalones, resource) = Roster((1, 2, 5), (2, 2, 3), (3, 3, 4));
        resource.State.TeamScore[2] = 7;
        resource.State.TeamScore[3] = 3;
        var engine = Engine();
        var menu = new TeamMenuUi(Scores(standalones, engine), engine, new DefaultForwarders.NoEngineClient(), new RecordingLog());

        menu.Refresh();

        Assert.Equal((7, 3), (menu.Choice(0).Score, menu.Choice(1).Score));
    }

    // ---- mp_tdm_teams: 2, 3 or 4 teams (user 2026-11-08) ----

    [Fact]
    public void ThreeTeamsAreThreeTablesTheEmptyOneIncluded()
    {
        var (standalones, resource) = Roster((1, 2, 5), (2, 3, 6));
        resource.State.TeamScore[4] = 0;
        var scores = Scores(standalones, Engine(), tdmTeams: "3");

        scores.Refresh();

        Assert.Equal((3, 4, "Metro Police"), (scores.TeamCount, scores.Team(2).Number, scores.Team(2).Name));
    }

    [Fact]
    public void FourTeamsAreFourTablesWithTheirScores()
    {
        var (standalones, resource) = Roster((1, 2, 1), (2, 3, 2), (3, 4, 3), (4, 5, 4));
        resource.State.TeamScore[4] = 6;
        resource.State.TeamScore[5] = 9;
        var scores = Scores(standalones, Engine(), tdmTeams: "4");

        scores.Refresh();

        Assert.Equal((4, 5, 9, 6), (scores.TeamCount, scores.Team(3).Number, scores.Team(3).Score, scores.Team(2).Score));
    }

    [Fact]
    public void ACountOutOfRangeIsClampedToFour()
    {
        var (standalones, _) = Roster((1, 2, 0));
        var scores = Scores(standalones, Engine(), tdmTeams: "9");

        scores.Refresh();

        Assert.Equal(4, scores.TeamCount);
    }

    [Fact]
    public void APlayerOnATeamPastTheCountStillHasATable()
    {
        // The count was just lowered to 2; the server has not moved the Resistance player yet.
        var (standalones, _) = Roster((1, 2, 0), (2, 5, 0));
        var scores = Scores(standalones, Engine(), tdmTeams: "2");

        scores.Refresh();

        Assert.Equal((4, 5), (scores.TeamCount, scores.Team(3).Number));
    }

    [Fact]
    public void TheTeamMenuHasACardPerTeamTheServerRuns()
    {
        var (standalones, _) = Roster((1, 2, 0), (2, 3, 0), (3, 4, 0));
        var engine = Engine();
        var menu = new TeamMenuUi(Scores(standalones, engine, tdmTeams: "4"), engine, new DefaultForwarders.NoEngineClient(), new RecordingLog());

        menu.Refresh();

        Assert.Equal((4, 5, "Resistance", 0), (menu.ChoiceCount, menu.Choice(3).Number, menu.Choice(3).Name, menu.Choice(3).Players));
    }

    [Fact]
    public void BeforeAnyoneJoinsTheTeamMenuStillOffersEveryTeam()
    {
        var (standalones, _) = Roster((1, 0, 0));
        var engine = Engine();
        var menu = new TeamMenuUi(Scores(standalones, engine, tdmTeams: "3"), engine, new DefaultForwarders.NoEngineClient(), new RecordingLog());

        menu.Refresh();

        Assert.Equal((3, 4), (menu.ChoiceCount, menu.Choice(2).Number));
    }
}

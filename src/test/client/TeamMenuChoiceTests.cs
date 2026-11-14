using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// What the team menu's choices send (plan_bots_teams_tf2 A.3 item 7): AUTO ASSIGN
/// is <c>jointeam 0</c>, which the server's team modes answer by putting the player
/// on the smaller team (stock PickDefaultSpawnTeam); a team's card is
/// <c>jointeam 2</c> (Combine) or <c>jointeam 3</c> (Rebels); Spectate is
/// <c>jointeam 1</c>. Every choice closes the menu; Cancel sends nothing.
/// </summary>
public class TeamMenuChoiceTests
{
    /// <summary>An engine client that records its JoinTeam calls and answers nothing else.</summary>
    public class Joins : DispatchProxy
    {
        public readonly List<int> Teams = new();

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == nameof(IFwdEngineClient.JoinTeam))
                Teams.Add((int)args![0]!);
            return method?.ReturnType is { IsValueType: true } t && t != typeof(void) ? Activator.CreateInstance(t) : null;
        }
    }

    private static (TeamMenuUi Menu, Joins Engine, Func<int> Closes) Menu()
    {
        var engine = DispatchProxy.Create<IFwdEngineClient, Joins>();
        var hud = Fake().Returns("Hud.LocalPlayerIndex", 1).Returns("Environment.Globals", default(Globals));
        var scores = new ScoreboardUi(new UiHostKit.NoStandalones(), hud, new DefaultForwarders.NoEngineClient(),
                                      new EnginePlayerNames(new DefaultForwarders.NoEngineClient(), new IdentityNameFilter()),
                                      DeclaredConVars.Fresh<HostnameConVar>(), hud);
        int closes = 0;
        var menu = new TeamMenuUi(scores, hud, engine, new RecordingLog()) { Close = () => closes++ };
        return (menu, (Joins)(object)engine, () => closes);
    }

    [Fact]
    public void AutoAssignSendsJoinTeamZero()
    {
        var (menu, engine, _) = Menu();

        menu.Choose(0);

        Assert.Equal([0], engine.Teams);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ATeamsCardSendsItsNumber(int team)
    {
        var (menu, engine, _) = Menu();

        menu.Choose(team);

        Assert.Equal([team], engine.Teams);
    }

    [Fact]
    public void SpectateSendsJoinTeamOne()
    {
        var (menu, engine, _) = Menu();

        menu.Choose(1);

        Assert.Equal([1], engine.Teams);
    }

    [Fact]
    public void AChoiceClosesTheMenu()
    {
        var (menu, _, closes) = Menu();

        menu.Choose(0);

        Assert.Equal(1, closes());
    }

    [Fact]
    public void CancelClosesAndSendsNothing()
    {
        var (menu, engine, closes) = Menu();

        menu.Cancel();

        Assert.Equal((0, 1), (engine.Teams.Count, closes()));
    }
}

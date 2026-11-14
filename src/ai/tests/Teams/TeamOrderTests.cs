using SourceSharp.Ai.Tests.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Teams;

/// <summary>
/// A human teammate's orders in team chat (plan_bots_teams_tf2 B.5, Q5): "follow me",
/// "help", "go", "hold", two phrasings each, Teamwork-weighted, only from a teammate.
/// </summary>
public sealed class TeamOrderTests
{
    private static readonly ActorId Human = Duel.Other;

    [Theory]
    [InlineData("follow me", OrderKind.Follow)]
    [InlineData("On me!", OrderKind.Follow)]
    [InlineData("help", OrderKind.Help)]
    [InlineData("cover me", OrderKind.Help)]
    [InlineData("go", OrderKind.Go)]
    [InlineData("HOLD.", OrderKind.Hold)]
    public void TheFourOrdersAreRead(string line, OrderKind kind) => Assert.Equal(kind, TeamOrders.Parse(line));

    /// <summary>A line that says more than an order is talk.</summary>
    [Theory]
    [InlineData("follow me to the armor")]
    [InlineData("help yourself")]
    [InlineData("")]
    public void TalkIsNoOrder(string line) => Assert.Equal(OrderKind.None, TeamOrders.Parse(line));

    private static SkillProfile Teamwork(int level)
    {
        SkillPresets.TryGet("normal", out SkillProfile? p);
        return new SkillProfile("tw", StandardSkills.Eleven, [.. p!.Levels, level]);
    }

    /// <summary>The bot on team 2 sees the human on <paramref name="humanTeam"/> at 900 u, then the order arrives.</summary>
    private static Duel Ordered(OrderKind order, int teamwork = 8, int humanTeam = 2, float x = 900f)
    {
        var d = new Duel(Teamwork(teamwork));
        var at = new AiVector(x, 0, 0);
        SelfState self = d.Self() with { Team = 2 };
        d.StepTeams(self, [(Human, at, humanTeam)], [AiEvent.ActorTeam(Duel.Bot, new Tick(d.Tick + 6), Human, humanTeam, 0), .. d.SeeAt(Human, at)]);
        d.StepTeams(self, [(Human, at, humanTeam)], AiEvent.Order(Duel.Bot, new Tick(d.Tick + 3), Human, order));
        return d;
    }

    [Fact]
    public void FollowMeIsFollowed()
    {
        Duel d = Ordered(OrderKind.Follow);
        IntentCommand i = d.StepTeams(d.Self() with { Team = 2 }, [(Human, new AiVector(900, 0, 0), 2)]);
        Assert.Equal("follow", d.Decider.TeamRole);
        Assert.InRange(i.Move.Corner0.X, 600f, 800f);
    }

    [Fact]
    public void HoldKeepsTheBotWhereItWasTold()
    {
        Duel d = Ordered(OrderKind.Hold);
        IntentCommand i = d.StepTeams(d.Self() with { Team = 2 }, [(Human, new AiVector(900, 0, 0), 2)]);
        Assert.Equal(("hold", AiVector.Zero), (d.Decider.TeamRole, i.Move.Corner0));
    }

    [Fact]
    public void GoReleasesTheBot()
    {
        Duel d = Ordered(OrderKind.Go);
        d.StepTeams(d.Self() with { Team = 2 }, [(Human, new AiVector(900, 0, 0), 2)]);
        Assert.DoesNotContain(d.Decider.TeamRole, new[] { "follow", "hold", "help" });
    }

    /// <summary>The control: an order from the other team is not listened to.</summary>
    [Fact]
    public void AnEnemysOrderIsIgnored()
    {
        Duel d = Ordered(OrderKind.Follow, humanTeam: 3);
        Assert.Equal(OrderKind.None, d.Memory.LastOrder.Kind);
    }

    /// <summary>A lone wolf (Teamwork 2) ignores orders, as it ignores a rally.</summary>
    [Fact]
    public void ALoneWolfIgnoresOrders()
    {
        Duel d = Ordered(OrderKind.Follow, teamwork: 2);
        d.StepTeams(d.Self() with { Team = 2 }, [(Human, new AiVector(900, 0, 0), 2)]);
        Assert.NotEqual("follow", d.Decider.TeamRole);
    }

    /// <summary>Within 200-400 u of the one it follows the bot stays by them, not on them.</summary>
    [Fact]
    public void AFollowerAlreadyCloseStandsBy()
    {
        Duel d = Ordered(OrderKind.Follow, x: 300f);
        IntentCommand i = d.StepTeams(d.Self() with { Team = 2 }, [(Human, new AiVector(300, 0, 0), 2)]);
        Assert.Equal("follow", d.Decider.TeamRole);
        Assert.InRange(i.Move.Corner0.X, 0f, 120f);
    }
}

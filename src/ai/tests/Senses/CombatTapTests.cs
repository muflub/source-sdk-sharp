using Xunit;
using SourceSharp.Ai.Server.Senses;

namespace SourceSharp.Ai.Tests.Senses;

/// <summary>
/// The damage, death and scoreboard taps (plan_hl2mp_ai §4.3, Phase 4 step 4):
/// what each bot's own HUD shows of a hit, a death and the scoreboard.
/// </summary>
public sealed class CombatTapTests
{
    private static readonly ActorId A = new(2, 1), B = new(3, 2), C = new(4, 3);
    private static readonly ActorId Human = new(1, 0x4000_0005);

    [Fact]
    public void AHitIsTheVictimsDamageAndTheAttackersHitMarkerAndNobodyElses()
    {
        var into = new List<AiEvent>();
        CombatTap.Hurt(A, B, 20, 80, new AiVector(1, 0, 0), [A, B, C], new Tick(4), into);
        Assert.Equal(2, into.Count);
        AiEvent hurt = into.Single(e => e.Bot == A);
        Assert.Equal((AiEventKind.Damaged, B, 20f, new AiVector(1, 0, 0)), (hurt.Kind, hurt.Other, hurt.Amount, hurt.Position));
        AiEvent marker = into.Single(e => e.Bot == B);
        Assert.Equal((AiEventKind.DealtDamage, A, 20f, 80), (marker.Kind, marker.Other, marker.Amount, marker.Code));
        Assert.DoesNotContain(into, e => e.Bot == C);
    }

    /// <summary>A bot's own blast (a rocket jump) is its damage with itself named as the attacker, and no hit marker: the memory tells it from an attack by that.</summary>
    [Fact]
    public void ABotsOwnBlastIsItsDamageNamingItself()
    {
        var into = new List<AiEvent>();
        CombatTap.Hurt(A, A, 45, 155, AiVector.Zero, [A, B, C], new Tick(4), into);
        AiEvent hurt = Assert.Single(into);
        Assert.Equal((AiEventKind.Damaged, A, A, 45f), (hurt.Kind, hurt.Bot, hurt.Other, hurt.Amount));
    }

    [Fact]
    public void AHumanHurtByTheWorldTellsNoBot()
    {
        var into = new List<AiEvent>();
        CombatTap.Hurt(Human, ActorId.None, 20, 80, AiVector.Zero, [A, B, C], new Tick(4), into);
        Assert.Empty(into);
    }

    [Fact]
    public void EveryBotsKillFeedShowsADeathAndTheVictimIsKilled()
    {
        var into = new List<AiEvent>();
        CombatTap.Died(A, Human, [A, B, C], new Tick(7), into);
        Assert.All([A, B, C], b => Assert.Contains(into, e => e.Bot == b && e.Kind == AiEventKind.KillFeed && e.Other == A && e.Killer == Human));
        Assert.Equal(A, Assert.Single(into, e => e.Kind == AiEventKind.Killed).Bot);
        Assert.Equal(Human, into.Single(e => e.Kind == AiEventKind.Killed).Other);
    }

    [Fact]
    public void TheLeaderRanksFirstAndATieGoesToFewerDeaths()
    {
        ScoreRow[] rows = [new(A, 3, 5), new(B, 7, 1), new(Human, 3, 2), new(C, 0, 0)];
        Assert.Equal([3, 1, 2, 4], CombatTap.Ranks(rows));
    }

    /// <summary>Each bot is told every player's team once, its own included (plan_bots_teams_tf2 B.2).</summary>
    [Fact]
    public void EveryBotIsToldEachPlayersTeamOnce()
    {
        var told = new Dictionary<(ActorId, ActorId), int>();
        var into = new List<AiEvent>();
        (ActorId, int)[] rows = [(A, 2), (Human, 3)];
        CombatTap.TellTeams(rows, [A, B], told, new Tick(6), into);
        Assert.Equal(4, into.Count);
        Assert.All(into, e => Assert.Equal(AiEventKind.ActorTeam, e.Kind));
        Assert.Contains(into, e => e.Bot == A && e.Other == A && e.Code == 2);
        into.Clear();
        CombatTap.TellTeams(rows, [A, B], told, new Tick(12), into);
        Assert.Empty(into);
    }

    /// <summary>A swap is a <c>player_team</c> event with the old team, as every client gets it (B.9).</summary>
    [Fact]
    public void ASwapIsToldWithItsOldTeam()
    {
        var told = new Dictionary<(ActorId, ActorId), int>();
        var into = new List<AiEvent>();
        CombatTap.TellTeams([(Human, 3)], [A], told, new Tick(6), into);
        into.Clear();
        CombatTap.TellTeams([(Human, 2)], [A], told, new Tick(12), into);
        AiEvent e = Assert.Single(into);
        Assert.Equal((Human, 2, 3), (e.Other, e.Code, e.Code2));
    }

    /// <summary>No teams, nothing to tell: a deathmatch row at team 0 is never an event.</summary>
    [Fact]
    public void ADeathmatchRowTellsNoTeam()
    {
        var into = new List<AiEvent>();
        CombatTap.TellTeams([(Human, 0), (A, 0)], [A, B], new Dictionary<(ActorId, ActorId), int>(), new Tick(6), into);
        Assert.Empty(into);
    }

    /// <summary>The team scores reach each bot when they change, and not again.</summary>
    [Fact]
    public void TeamScoresAreToldWhenTheyChange()
    {
        var told = new Dictionary<(ActorId, int), int>();
        var into = new List<AiEvent>();
        CombatTap.TellScore(5, 3, [A, B], told, new Tick(6), into);
        Assert.Equal(2, into.Count);
        Assert.All(into, e => Assert.Equal((AiEventKind.MatchState, 5, 3), (e.Kind, e.Code, e.Code2)));
        into.Clear();
        CombatTap.TellScore(5, 3, [A, B], told, new Tick(12), into);
        CombatTap.TellScore(4, 0, [A, B], told, new Tick(12), into);
        Assert.Empty(into);
    }
}

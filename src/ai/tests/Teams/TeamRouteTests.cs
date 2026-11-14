using SourceSharp.Ai.Hl2mp.Trees;
using SourceSharp.Ai.Tests.Nav;
using SourceSharp.Ai.Tests.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Teams;

/// <summary>
/// The team roles over the level's routes (plan_bots_teams_tf2 B.5, P2 step 8): Crossfire,
/// where the teammate behind on one way to a known enemy takes the other door, and Chase with
/// a partner, where the nearer of two hunting teammates leads and the other holds the far exit.
/// Each fact's control is the same scene with the condition taken away.
/// </summary>
public sealed class TeamRouteTests
{
    private static readonly ActorId Mate = Duel.Other, Foe = Duel.Opponent;

    private static SkillProfile Teamwork(int level)
    {
        SkillPresets.TryGet("normal", out SkillProfile? p);
        return new SkillProfile("tw", StandardSkills.Eleven, [.. p!.Levels, level]);
    }

    /// <summary>
    /// Two corridors 256 u apart, a ring: row 0 the north route, row 8 the south, joined
    /// down columns 0 and 39. The bot stands at (0,2), the enemy was last seen at (39,2):
    /// the north way is the shorter (43 cells to 51).
    /// </summary>
    private static readonly string[] Ring =
    [
        new string('.', 40),
        .. Enumerable.Repeat("." + new string('#', 38) + ".", 7),
        new string('.', 40),
    ];

    private static AiVector Cell(int x, int y) => new(x * Grids.Size + 16f, (Ring.Length - 1 - y) * Grids.Size + 16f, 0f);

    private static readonly AiVector BotAt = Cell(0, 2), EnemyAt = Cell(39, 2);

    private static SelfState Self(Duel d) => d.Self() with { Origin = BotAt, Team = 2 };

    private static AiEvent OnTeam(Duel d, ActorId who, int team) => AiEvent.ActorTeam(Duel.Bot, new Tick(d.Tick + 6), who, team, 0);

    /// <summary>The enemy's footsteps where it was lost: the bot's belief stays there, so a fact's geometry holds while it hunts.</summary>
    private static AiEvent Steps(Duel d, AiVector at) => AiEvent.SoundHeard(Duel.Bot, new Tick(d.Tick + 3), SoundKind.Footstep, at, 0.6f, Foe, -1);

    /// <summary>
    /// The bot sees its teammate at <paramref name="mate"/> (none if null) and the enemy, then
    /// loses both, and hunts for <paramref name="decides"/> decides, hearing the enemy where it
    /// was; what it did each one.
    /// </summary>
    private static (Duel D, List<(string Role, IntentCommand I)> Decides) Hunt(int teamwork, ulong seed, AiVector? mate, int decides = 40,
                                                                                   Func<Duel, int, AiEvent[]>? extra = null)
    {
        var (g, _) = Grids.Parse(Ring);
        var d = new Duel(Teamwork(teamwork), Duel.Bot, seed, g, null, null);
        var sighted = new List<(ActorId, AiVector, int)> { (Foe, EnemyAt, 3) };
        var events = new List<AiEvent> { OnTeam(d, Foe, 3) };
        events.AddRange(d.SeeAt(Foe, EnemyAt));
        if (mate is { } m)
        {
            sighted.Add((Mate, m, 2));
            events.Add(OnTeam(d, Mate, 2));
            events.AddRange(d.SeeAt(Mate, m));
        }

        d.StepTeams(Self(d), [.. sighted], [.. events]);
        var lost = new List<AiEvent> { AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Foe, EnemyAt, default) };
        if (mate is { } m2)
            lost.Add(AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Mate, m2, default));
        d.StepTeams(Self(d), [], [.. lost]);
        var seen = new List<(string, IntentCommand)>();
        for (int k = 0; k < decides; k++)
        {
            IntentCommand i = d.StepTeams(Self(d), [], [Steps(d, EnemyAt), .. extra?.Invoke(d, k) ?? []]);
            seen.Add((d.Decider.TeamRole, i));
        }

        return (d, seen);
    }

    /// <summary>The teammate ahead on the north route, ten cells along it.</summary>
    private static readonly AiVector Ahead = Cell(10, 0);

    [Fact]
    public void TheTeammateBehindOnOneWayGoesRoundByTheOther()
    {
        int crossed = 0;
        for (ulong seed = 1; seed <= 8; seed++)
        {
            (Duel d, var decides) = Hunt(10, seed, Ahead);
            int first = decides.FindIndex(x => x.Role == "crossfire");
            if (first < 0)
                continue;
            crossed++;
            Assert.True(d.Decider.CrossVia.Y < Cell(0, 5).Y, $"by the south route: {d.Decider.CrossVia}");
            Assert.Equal(CalloutKind.GoingAround, decides[first].I.Say.Kind);
            Assert.Equal(1, d.Decider.Crossfires);
        }

        Assert.True(crossed >= 4, $"Teamwork 10 (crossfireWeight 0.8) goes round most times: {crossed} of 8");
    }

    /// <summary>The control: alone, the bot takes the short way.</summary>
    [Fact]
    public void WithNoPartnerNobodyGoesRound()
    {
        for (ulong seed = 1; seed <= 8; seed++)
            Assert.DoesNotContain(Hunt(10, seed, null).Decides, x => x.Role == "crossfire");
    }

    /// <summary>The control: Teamwork 0 (crossfireWeight 0) never goes round.</summary>
    [Fact]
    public void ALoneWolfNeverGoesRound()
    {
        for (ulong seed = 1; seed <= 8; seed++)
            Assert.DoesNotContain(Hunt(0, seed, Ahead).Decides, x => x.Role == "crossfire");
    }

    /// <summary>The control: the teammate on the way but behind the bot (two cells south, 45 cells from the enemy to its 43) leaves this bot in front.</summary>
    [Fact]
    public void TheBotInFrontKeepsTheShortWay()
    {
        for (ulong seed = 1; seed <= 8; seed++)
            Assert.DoesNotContain(Hunt(10, seed, Cell(0, 4)).Decides, x => x.Role == "crossfire");
    }

    /// <summary>B.9: the partner moved to the enemy's team ends the crossfire at the next decide.</summary>
    [Fact]
    public void APartnerWhoTurnsFoeEndsTheCrossfire()
    {
        int checkedSwaps = 0;
        for (ulong seed = 1; seed <= 8; seed++)
        {
            int swapAt = -1;
            (_, var decides) = Hunt(10, seed, Ahead, extra: (d, k) =>
            {
                if (swapAt < 0 && d.Decider.TeamRole == "crossfire")
                {
                    swapAt = k;
                    return [OnTeam(d, Mate, 3)];
                }

                return [];
            });
            if (swapAt < 0)
                continue;
            checkedSwaps++;
            Assert.All(decides.Skip(swapAt), x => Assert.NotEqual("crossfire", x.Role));
        }

        Assert.True(checkedSwaps > 0, "some seed began a crossfire to end");
    }

    /// <summary>An open floor, 40 by 40 cells, for the chase.</summary>
    private static (Duel D, string Role) Chase(AiVector bot, AiVector leader, AiVector enemy)
    {
        var (g, _) = Grids.Parse([.. Enumerable.Repeat(new string('.', 40), 40)]);
        var d = new Duel(Teamwork(10), Duel.Bot, 7, g, null, null);
        SelfState self = d.Self() with { Origin = bot, Team = 2 };
        d.StepTeams(self, [(Foe, enemy, 3), (Mate, leader, 2)], [OnTeam(d, Foe, 3), OnTeam(d, Mate, 2), .. d.SeeAt(Foe, enemy), .. d.SeeAt(Mate, leader)]);
        d.StepTeams(self, [], AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Foe, enemy, default),
                    AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Mate, leader, default));
        string role = "";
        for (int k = 0; k < 150 && role != "chase exit"; k++)
        {
            d.StepTeams(self, [], Steps(d, enemy));
            role = d.Decider.TeamRole;
        }

        return (d, role);
    }

    private static readonly AiVector ChaseEnemy = new(656, 656, 0), ChaseLeader = new(1136, 656, 0);

    /// <summary>The teammate nearer the enemy leads; the bot holds a way out on the enemy's far side from it.</summary>
    [Fact]
    public void TheFartherPartnerHoldsTheFarExit()
    {
        (Duel d, string role) = Chase(new AiVector(656, 48, 0), ChaseLeader, ChaseEnemy);
        Assert.Equal("chase exit", role);
        AiVector exit = d.Decider.Doing.Goal;
        Assert.True(exit.X < ChaseEnemy.X - Hl2mpDecider.ChaseExitNear * 0.5f, $"west of the enemy, away from the leader in the east: {exit}");
        Assert.True(d.Decider.ChaseCovers > 0);
    }

    /// <summary>The control: the bot nearer the enemy than its teammate leads, by the tree's own hunt.</summary>
    [Fact]
    public void TheNearerPartnerLeads()
    {
        (_, string role) = Chase(new AiVector(656, 400, 0), ChaseLeader, ChaseEnemy);
        Assert.NotEqual("chase exit", role);
    }
}

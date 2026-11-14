using SourceSharp.Ai.Tests.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Teams;

/// <summary>
/// The team roles over the HL2MP tree (plan_bots_teams_tf2 B.5): Stick, Cover, Trade,
/// Regroup and Push on no progress. Each fact's control is the same scene with the
/// condition taken away or the Teamwork that drives it at the other end.
/// </summary>
public sealed class TeamRoleTests
{
    private static readonly ActorId Mate = Duel.Other, Mate2 = new(5, 0x4000_0005), Foe = Duel.Opponent;

    private static SkillProfile Teamwork(int level)
    {
        SkillPresets.TryGet("normal", out SkillProfile? p);
        return new SkillProfile("tw", StandardSkills.Eleven, [.. p!.Levels, level]);
    }

    private static SelfState Self(Duel d) => d.Self() with { Team = 2 };

    private static AiEvent OnTeam(Duel d, ActorId who, int team) => AiEvent.ActorTeam(Duel.Bot, new Tick(d.Tick + 6), who, team, 0);

    /// <summary>A teammate seen at <paramref name="x"/> along x, then out of sight; nobody else about.</summary>
    private static Duel SawMateAt(int teamwork, float x)
    {
        var d = new Duel(Teamwork(teamwork));
        var at = new AiVector(x, 0, 0);
        d.StepTeams(Self(d), [(Mate, at, 2)], [OnTeam(d, Mate, 2), .. d.SeeAt(Mate, at)]);
        d.StepTeams(Self(d), [], AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Mate, at, default));
        d.StepTeams(Self(d), []);
        return d;
    }

    [Fact]
    public void BeyondItsLeashABotSticksToItsTeammate()
    {
        Duel d = SawMateAt(10, 900f);
        IntentCommand i = d.StepTeams(Self(d), []);
        Assert.Equal(("Team", "stick"), (d.Decider.Doing.Branch, d.Decider.TeamRole));
        Assert.True(i.Move.Corner0.X > 400f && i.Move.Corner0.X < 900f, $"toward the teammate, short of them: {i.Move.Corner0}");
        Assert.Equal(MoveSpeed.Walk, i.Move.Speed);
    }

    /// <summary>The control: a lone wolf's leash (1,500 u) holds the same teammate, so it does not stick.</summary>
    [Fact]
    public void ALoneWolfLetsTheSameTeammateGo()
    {
        Duel d = SawMateAt(0, 900f);
        d.StepTeams(Self(d), []);
        Assert.NotEqual("stick", d.Decider.TeamRole);
    }

    /// <summary>In a deathmatch nobody is a teammate: the same scene on team 0 takes no role.</summary>
    [Fact]
    public void WithNoTeamsNoRoleIsTaken()
    {
        var d = new Duel(Teamwork(10));
        var at = new AiVector(900, 0, 0);
        d.StepTeams(d.Self(), [(Mate, at, 0)], d.SeeAt(Mate, at));
        d.StepTeams(d.Self(), [], AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Mate, at, default));
        d.StepTeams(d.Self(), []);
        Assert.Equal(0, d.Decider.TeamRoleDecides);
    }

    /// <summary>A teammate killed in sight by an enemy in sight: the survivor's branch is Trade within one decide, at that enemy.</summary>
    [Fact]
    public void ATeammateKilledInSightIsTraded()
    {
        int traded = 0;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var d = new Duel(Teamwork(10), Duel.Bot, seed, null, null, null);
            var mate = new AiVector(300, 200, 0);
            var foe = new AiVector(900, 200, 0);
            d.StepTeams(Self(d), [(Mate, mate, 2), (Foe, foe, 3)], [OnTeam(d, Mate, 2), OnTeam(d, Foe, 3), .. d.SeeAt(Mate, mate), .. d.SeeAt(Foe, foe)]);
            d.StepTeams(Self(d), [(Foe, foe, 3)], AiEvent.KillFeed(Duel.Bot, new Tick(d.Tick + 3), Mate, Foe));
            if (d.Decider.TeamRole == "trade")
            {
                traded++;
                Assert.Equal(Foe, d.Decider.Doing.Target);
            }
        }

        Assert.True(traded >= 4, $"Teamwork 10 trades most chances: {traded} of 6");
    }

    /// <summary>
    /// The game drops a body from sight the tick it dies, before the kill feed (ss_ai_tdm's trade
    /// scene): a teammate lost from sight with its death is still a death seen, and is traded.
    /// </summary>
    [Fact]
    public void ATeammateWhoLeavesSightAsItDiesIsTraded()
    {
        int traded = 0;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var d = new Duel(Teamwork(10), Duel.Bot, seed, null, null, null);
            var mate = new AiVector(300, 200, 0);
            var foe = new AiVector(900, 200, 0);
            d.StepTeams(Self(d), [(Mate, mate, 2), (Foe, foe, 3)], [OnTeam(d, Mate, 2), OnTeam(d, Foe, 3), .. d.SeeAt(Mate, mate), .. d.SeeAt(Foe, foe)]);
            d.StepTeams(Self(d), [(Foe, foe, 3)], AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 3), Mate, mate, default),
                        AiEvent.KillFeed(Duel.Bot, new Tick(d.Tick + 3), Mate, Foe));
            traded += d.Decider.TeamRole == "trade" ? 1 : 0;
        }

        Assert.True(traded >= 4, $"Teamwork 10 trades most chances: {traded} of 6");
    }

    /// <summary>The control: a teammate out of sight for seconds before the kill feed died unseen, and is no trade.</summary>
    [Fact]
    public void ATeammateLongOutOfSightIsNotTraded()
    {
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var d = new Duel(Teamwork(10), Duel.Bot, seed, null, null, null);
            var mate = new AiVector(300, 200, 0);
            var foe = new AiVector(900, 200, 0);
            d.StepTeams(Self(d), [(Mate, mate, 2), (Foe, foe, 3)], [OnTeam(d, Mate, 2), OnTeam(d, Foe, 3), .. d.SeeAt(Mate, mate), .. d.SeeAt(Foe, foe)]);
            d.StepTeams(Self(d), [(Foe, foe, 3)], AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 3), Mate, mate, default));
            d.Skip(200);
            d.StepTeams(Self(d), [(Foe, foe, 3)], AiEvent.KillFeed(Duel.Bot, new Tick(d.Tick + 3), Mate, Foe));
            Assert.NotEqual("trade", d.Decider.TeamRole);
        }
    }

    /// <summary>The control: the same enemy in sight with no teammate's death is no trade.</summary>
    [Fact]
    public void NoDeathNoTrade()
    {
        var d = new Duel(Teamwork(10));
        var foe = new AiVector(900, 200, 0);
        d.StepTeams(Self(d), [(Foe, foe, 3)], [OnTeam(d, Foe, 3), .. d.SeeAt(Foe, foe)]);
        d.StepTeams(Self(d), [(Foe, foe, 3)]);
        Assert.NotEqual("trade", d.Decider.TeamRole);
    }

    private static readonly SayIntent Called = new(CalloutKind.Enemy, new AiVector(1200, 0, 0), 1, 0, 30, 2, true);

    private static Duel HeardCall(int teamwork)
    {
        var d = new Duel(Teamwork(teamwork));
        var mate = new AiVector(-200, 0, 0);
        d.StepTeams(Self(d), [(Mate, mate, 2)], [OnTeam(d, Mate, 2), .. d.SeeAt(Mate, mate)]);
        d.StepTeams(Self(d), [(Mate, mate, 2)], AiEvent.Callout(Duel.Bot, new Tick(d.Tick + 3), Mate, Called));
        return d;
    }

    [Fact]
    public void ACalledFightIsCoveredFromAnotherAngle()
    {
        Duel d = HeardCall(10);
        IntentCommand i = d.StepTeams(Self(d), [(Mate, new AiVector(-200, 0, 0), 2)]);
        Assert.Equal("cover", d.Decider.TeamRole);
        Assert.True(MathF.Abs(i.Move.Corner0.Y) > 300f, $"off the line to the called place: {i.Move.Corner0}");
    }

    /// <summary>The control: Teamwork 0 covers nobody (teamCoverWeight 0).</summary>
    [Fact]
    public void ALoneWolfCoversNobody()
    {
        Duel d = HeardCall(0);
        d.StepTeams(Self(d), [(Mate, new AiVector(-200, 0, 0), 2)]);
        Assert.NotEqual("cover", d.Decider.TeamRole);
    }

    /// <summary>Losing on the team scores, no team kill for 90 s: the lowest slot of a Teamwork 4+ bot's team calls a rally.</summary>
    private static (Duel D, IntentCommand I) Losing(int teamwork, int ours, int theirs)
    {
        var d = new Duel(Teamwork(teamwork));
        d.StepTeams(Self(d), [], OnTeam(d, Mate, 2), AiEvent.MatchState(Duel.Bot, new Tick(d.Tick + 6), 2, ours), AiEvent.MatchState(Duel.Bot, new Tick(d.Tick + 6), 3, theirs));
        d.Skip((int)(SourceSharp.Ai.Hl2mp.Trees.Hl2mpDecider.NoProgressTeamSeconds / 0.015f));
        return (d, d.StepTeams(Self(d), []));
    }

    [Fact]
    public void NoProgressCallsARally()
    {
        (_, IntentCommand i) = Losing(8, 1, 5);
        Assert.Equal((CalloutKind.Rally, true), (i.Say.Kind, i.Say.Text));
    }

    /// <summary>The control: winning is progress, and nobody calls.</summary>
    [Fact]
    public void AWinningTeamCallsNoRally()
    {
        (_, IntentCommand i) = Losing(8, 5, 1);
        Assert.NotEqual(CalloutKind.Rally, i.Say.Kind);
    }

    /// <summary>A bot under Teamwork 4 never calls one.</summary>
    [Fact]
    public void ALowTeamworkBotCallsNoRally()
    {
        (_, IntentCommand i) = Losing(3, 1, 5);
        Assert.NotEqual(CalloutKind.Rally, i.Say.Kind);
    }

    /// <summary>A teammate's rally heard: a Teamwork 10 bot goes to the called place.</summary>
    [Fact]
    public void ARallyHeardIsJoined()
    {
        var d = new Duel(Teamwork(10));
        var place = new AiVector(-800, 300, 0);
        d.StepTeams(Self(d), [], OnTeam(d, Mate, 2));
        d.StepTeams(Self(d), [], AiEvent.Callout(Duel.Bot, new Tick(d.Tick + 3), Mate, new SayIntent(CalloutKind.Rally, place, 0, 0, 30, 2, true)));
        IntentCommand i = d.StepTeams(Self(d), []);
        Assert.Equal(("regroup", place), (d.Decider.TeamRole, i.Move.Corner0));
    }

    /// <summary>The control: Teamwork 2 ignores the call (B.5).</summary>
    [Fact]
    public void ALoneWolfIgnoresARally()
    {
        var d = new Duel(Teamwork(2));
        d.StepTeams(Self(d), [], OnTeam(d, Mate, 2));
        d.StepTeams(Self(d), [], AiEvent.Callout(Duel.Bot, new Tick(d.Tick + 3), Mate, new SayIntent(CalloutKind.Rally, new AiVector(-800, 300, 0), 0, 0, 30, 2, true)));
        // Past when even a late bot would come (8 s at Teamwork 2), inside the rally's time.
        var roles = new List<string>();
        for (int k = 0; k < 100; k++)
        {
            d.StepTeams(Self(d), []);
            roles.Add(d.Decider.TeamRole);
        }

        Assert.DoesNotContain("regroup", roles);
    }

    /// <summary>The caller at its rally with two teammates in sight there calls the push.</summary>
    [Fact]
    public void ThreeGatheredCallThePush()
    {
        (Duel d, IntentCommand rally) = Losing(8, 1, 5);
        Assert.Equal(CalloutKind.Rally, rally.Say.Kind);
        AiVector place = rally.Say.Place;
        IntentCommand i = d.StepTeams(Self(d) with { Origin = place }, [(Mate, place + new AiVector(64, 0, 0), 2), (Mate2, place + new AiVector(0, 64, 0), 2)],
                                      OnTeam(d, Mate2, 2));
        Assert.Equal(CalloutKind.Push, i.Say.Kind);
    }

    /// <summary>The control: alone at the rally and inside its time, no push yet.</summary>
    [Fact]
    public void AloneAtTheRallyNoPushYet()
    {
        (Duel d, IntentCommand rally) = Losing(8, 1, 5);
        IntentCommand i = d.StepTeams(Self(d) with { Origin = rally.Say.Place }, []);
        Assert.NotEqual(CalloutKind.Push, i.Say.Kind);
    }
}

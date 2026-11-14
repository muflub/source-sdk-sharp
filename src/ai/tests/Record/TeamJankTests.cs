using SourceSharp.Ai.Replay;
using Xunit;

namespace SourceSharp.Ai.Tests.Record;

/// <summary>
/// <c>ai-replay jank</c>'s team block (plan_bots_teams_tf2 B.8) over hand-built recordings: team
/// kills, fire held for a teammate on the line, time near a teammate, callouts, and engagements
/// begun with a teammate near. Each with the case that looks like it and must not count.
/// </summary>
public sealed class TeamJankTests
{
    private static readonly ActorId Bot = new(3, 7), Mate = new(4, 8), Foe = new(5, 9);

    private sealed class Rec
    {
        private readonly MemoryStream _bytes = new();
        private readonly AiRecorder _r;

        public Rec()
        {
            _r = new AiRecorder(_bytes, Recordings.Header);
            _r.World(new AiRecordedWorld(new MapKnowledge("ss_test", [], [], new WeaponKnowledge([])), AiMetrics.TickSeconds, "", 0));
            _r.BotAdded(Bot, 1, []);
        }

        public void Frame(ActorId a, int t, AiVector at) => _r.Frame(new AiActorFrame(a, new Tick(t), at, at + new AiVector(0, 0, 64), default));

        /// <summary>The bot's view at <paramref name="t"/>: itself on team 2 facing east at the origin, and who it sees on which team.</summary>
        public void View(int t, int team = 2, params (ActorId Id, AiVector At, int Team)[] seen)
        {
            AiSnapshot snap = new SnapshotRing(1, 8, 4).BeginWrite(new Tick(t));
            PerceivedView v = snap.Add(Bot, new SelfState(default, default, new AiAngles(0, 0, 0), 100, 0, 1, 1, 1, SelfFlags.Alive) with { Team = team });
            foreach ((ActorId id, AiVector at, int tm) in seen)
                v.AddSighted(new ActorFacts(id, at, default, 1, ActorActivity.None, 1f, tm));
            _r.Snapshot(snap);
        }

        /// <summary>The bot decides at <paramref name="t"/> to look at the foe's chest, firing or not, saying an enemy callout or not.</summary>
        public void Decide(int t, AiVector foe, bool fire, bool callout = false)
        {
            var look = new LookIntent(LookKind.Actor, foe + new AiVector(0, 0, 48), Foe, AimPoint.Body, 0f, 0);
            var action = fire ? new ActionIntent(ActionKind.Fire, default, 0, 0) : ActionIntent.None;
            SayIntent say = callout ? new SayIntent(CalloutKind.Enemy, foe, 1, Foe.Slot + 1, 30, 2, true) : default;
            _r.Intent(new IntentCommand(Bot, new Tick(t), t, new Tick(t + IntentCommand.LifetimeTicks), default, look, action, default, say));
        }

        public void Event(AiEvent e) => _r.Event(e);

        public JankReport Report() => AiJank.Read(new MemoryStream(_bytes.ToArray()), JankTool.WeaponRange, null);

        public JankActor Of(ActorId a) => Report().Actors.Single(x => x.Actor == a);
    }

    private static readonly AiVector FoeAt = new(600, 0, 0);

    /// <summary>The bot at the origin aiming east at the foe for 100 ticks, deciding every 6, with the teammate where given (null: none seen).</summary>
    private static Rec Aiming(AiVector? mate, bool fire)
    {
        var r = new Rec();
        for (int t = 0; t < 100; t++)
        {
            r.Frame(Bot, t, default);
            r.Frame(Foe, t, FoeAt);
            if (t % 6 == 0)
                r.Decide(t, FoeAt, fire);
            if (mate is { } m)
            {
                r.Frame(Mate, t, m);
                r.View(t, 2, (Foe, FoeAt, 3), (Mate, m, 2));
            }
            else
            {
                r.View(t, 2, (Foe, FoeAt, 3));
            }
        }

        return r;
    }

    [Fact]
    public void FireHeldWithATeammateOnTheLineIsCounted()
    {
        JankActor a = Aiming(new AiVector(300, 10, 0), fire: false).Of(Bot);
        Assert.Equal(100 * (double)AiMetrics.TickSeconds, a.Teamplay.HeldFireSeconds, 6);
    }

    /// <summary>The control: the same teammate well off the line is not in the way.</summary>
    [Fact]
    public void ATeammateOffTheLineHoldsNothing()
        => Assert.Equal(0, Aiming(new AiVector(300, 200, 0), fire: false).Of(Bot).Teamplay.HeldFireSeconds);

    /// <summary>The control: a bot firing held nothing, whoever stands there.</summary>
    [Fact]
    public void FiringIsNotHeldFire()
        => Assert.Equal(0, Aiming(new AiVector(300, 10, 0), fire: true).Of(Bot).Teamplay.HeldFireSeconds);

    /// <summary>Held fire is a part of the score (B.8): it reads the held fire, and raises the score.</summary>
    [Fact]
    public void HeldFireIsAJankPart()
    {
        JankScore.Part part = Assert.Single(JankScore.Parts, p => p.Name == "held fire for a teammate");
        JankActor held = Aiming(new AiVector(300, 10, 0), fire: false).Of(Bot), clear = Aiming(new AiVector(300, 200, 0), fire: false).Of(Bot);
        Assert.True(part.Of(held) > 0, "the part reads the held fire");
        Assert.True(JankScore.Of(held) > JankScore.Of(clear), $"held {JankScore.Of(held)}, clear {JankScore.Of(clear)}");
    }

    [Fact]
    public void ATeammateWithinReachForHalfTheLifeIsNearHalfTheTime()
    {
        var r = new Rec();
        for (int t = 0; t < 100; t++)
        {
            r.Frame(Bot, t, default);
            r.Frame(Mate, t, new AiVector(t < 50 ? 400 : 700, 0, 0));
            r.View(t, 2);
        }

        r.Event(AiEvent.ActorTeam(Bot, new Tick(1), Mate, 2, 0));
        // The teammate's team: the bot's view of it, once.
        r.View(100, 2, (Mate, new AiVector(700, 0, 0), 2));
        Assert.Equal(0.5, r.Of(Bot).Teamplay.NearShare, 2);
    }

    [Fact]
    public void ATeammateKilledIsATeamKillAndAnEnemyIsNot()
    {
        var r = new Rec();
        for (int t = 0; t < 10; t++)
        {
            r.Frame(Bot, t, default);
            r.View(t, 2, (Mate, new AiVector(100, 0, 0), 2), (Foe, FoeAt, 3));
        }

        r.Event(AiEvent.KillFeed(Bot, new Tick(5), Mate, Bot));
        r.Event(AiEvent.KillFeed(Bot, new Tick(6), Foe, Bot));
        Assert.Equal(1, r.Of(Bot).Teamplay.TeamKills);
    }

    [Fact]
    public void EnemyCalloutsAreCounted()
    {
        var r = new Rec();
        for (int t = 0; t < 30; t++)
        {
            r.Frame(Bot, t, default);
            r.View(t, 2, (Foe, FoeAt, 3));
            if (t % 6 == 0)
                r.Decide(t, FoeAt, fire: false, callout: t == 12);
        }

        Assert.Equal(1, r.Of(Bot).Teamplay.Callouts);
    }

    /// <summary>An enemy sighted with none in sight begins an engagement; with the teammate 300 u away it is begun with a teammate near, at 900 u it is not.</summary>
    [Theory]
    [InlineData(300f, 1)]
    [InlineData(900f, 0)]
    public void AnEngagementIsBegunWithATeammateNearOrNot(float mateX, int withMate)
    {
        var r = new Rec();
        for (int t = 0; t < 30; t++)
        {
            r.Frame(Bot, t, default);
            r.Frame(Mate, t, new AiVector(mateX, 0, 0));
            r.View(t, 2, (Foe, FoeAt, 3), (Mate, new AiVector(mateX, 0, 0), 2));
        }

        r.Event(AiEvent.SightGained(Bot, new Tick(10), Foe, FoeAt, default, 1f));
        JankTeam team = r.Of(Bot).Teamplay;
        Assert.Equal((1, withMate), (team.Engagements, team.EngagementsWithTeammate));
    }

    /// <summary>In a deathmatch there is no team block: nobody is on a game team.</summary>
    [Fact]
    public void ADeathmatchActorHasNoTeamplay()
    {
        var r = new Rec();
        for (int t = 0; t < 10; t++)
        {
            r.Frame(Bot, t, default);
            r.View(t, 0, (Foe, FoeAt, 0));
        }

        r.Event(AiEvent.KillFeed(Bot, new Tick(5), Foe, Bot));
        Assert.Equal(default, r.Of(Bot).Teamplay);
        Assert.DoesNotContain("team play", AiJank.Table(r.Report()), StringComparison.Ordinal);
    }

    /// <summary>The table prints the team block under teams, with friendly fire as the recording says.</summary>
    [Fact]
    public void UnderTeamsTheTablePrintsTheTeamBlock()
    {
        Rec r = Aiming(new AiVector(300, 10, 0), fire: false);
        r.Event(AiEvent.FriendlyFireSet(Bot, new Tick(1), true));
        Assert.Contains("team play (B.8, reported; friendly fire on)", AiJank.Table(r.Report()), StringComparison.Ordinal);
    }
}

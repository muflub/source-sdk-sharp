using SourceSharp.Ai.Hl2mp.Teams;
using SourceSharp.Ai.Hl2mp.Trees;
using SourceSharp.Ai.Tests.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Teams;

/// <summary>
/// The team layer (plan_bots_teams_tf2 B.4): a callout is part of an intent, re-published
/// by the session to the speaker's teammates' lanes after its delay, never to the other
/// team or back to the speaker; heard as a teammate's words; and said in team chat for
/// the humans. Each fact's control is the same callout with the receiver on the other side.
/// </summary>
public sealed class TeamCalloutTests
{
    private const int Slots = 8, Sighted = 4;

    private static readonly ActorId A = new(1, 1), B = new(2, 2), C = new(3, 3);

    private static readonly SayIntent Spotted = new(CalloutKind.Enemy, new AiVector(256, 256, 0), 2, 5, 40, 2, true);

    /// <summary>Says <see cref="Spotted"/> at its decide on tick <paramref name="at"/>, holds otherwise.</summary>
    private sealed class Speaker(ActorId bot, int at, SayIntent say) : IDecider
    {
        public IntentCommand Decide(Tick tick, PerceivedView view, int seq)
            => IntentCommand.Hold(bot, tick, seq) with { Say = tick.Value == at && bot == A ? say : default };

        public BtTrace Trace { get; } = new();

        public DoingRecord Doing => default;

        public IReadOnlyList<string> NodeNames => [];

        public IReadOnlyList<int> NodeBranches => [];
    }

    private sealed class Events : IAiRecordSink
    {
        public readonly List<AiEvent> Callouts = new();

        public void BotAdded(ActorId bot, ulong seed, ReadOnlySpan<byte> skillLevels) { }

        public void BotRemoved(ActorId bot) { }

        public void DecisionTree(IReadOnlyList<string> names, IReadOnlyList<int> branches) { }

        public void Event(in AiEvent e)
        {
            if (e.Kind == AiEventKind.Callout)
                Callouts.Add(e);
        }

        public void Snapshot(AiSnapshot snapshot) { }

        public void Intent(in IntentCommand intent) { }

        public void UserCmd(in AiUserCmd cmd) { }

        public void Frame(in AiActorFrame frame) { }

        public void Observed(in AiEvent e) { }
    }

    /// <summary>A, B and C on the given teams, A saying <paramref name="say"/> at its decide on tick 6, run for 120 ticks.</summary>
    private static (Events Sink, AiSession Session) Run((int A, int B, int C) teams, SayIntent? say = null)
    {
        var sink = new Events();
        var s = new AiSession(new InlineTransport(), sink, Slots, Sighted)
        {
            World = MemoryWorld.Empty(),
            Curves = SourceSharp.Ai.Tests.Skills.Curves.Game(),
            Deciders = setup => new Speaker(setup.Bot, 5, say ?? Spotted),
        };
        SkillPresets.TryGet("normal", out SkillProfile? p);
        byte[] levels = [.. p!.Levels.Select(l => (byte)l)];
        foreach (ActorId id in new[] { A, B, C })
            s.AddBot(id, (ulong)id.Slot, levels);
        for (int t = 1; t <= 120; t++)
        {
            AiSnapshot snap = s.Snapshots.BeginWrite(new Tick(t));
            snap.Add(A, new SelfState(default, default, default, 100, 0, 3, 30, 90, SelfFlags.Alive, Team: teams.A));
            snap.Add(B, new SelfState(default, default, default, 100, 0, 3, 30, 90, SelfFlags.Alive, Team: teams.B));
            snap.Add(C, new SelfState(default, default, default, 100, 0, 3, 30, 90, SelfFlags.Alive, Team: teams.C));
            s.Publish(snap);
        }

        s.Drain(wait: true);
        return (sink, s);
    }

    [Fact]
    public void ACalloutReachesTheSpeakersTeammates()
    {
        (Events sink, _) = Run((2, 2, 3));
        AiEvent e = Assert.Single(sink.Callouts);
        Assert.Equal((B, A, CalloutKind.Enemy, 2), (e.Bot, e.Other, e.CalloutKind, e.CalloutCount));
    }

    /// <summary>Neither the other team nor the speaker hears it.</summary>
    [Fact]
    public void NoEnemyAndNotTheSpeakerHearsIt()
    {
        (Events sink, _) = Run((2, 2, 3));
        Assert.DoesNotContain(sink.Callouts, e => e.Bot == C || e.Bot == A);
    }

    /// <summary>The control for the facts above: on one team, both others hear it.</summary>
    [Fact]
    public void OnOneTeamEveryOtherBotHearsIt()
    {
        (Events sink, _) = Run((2, 2, 2));
        Assert.Equal([B.Slot, C.Slot], sink.Callouts.Select(e => e.Bot.Slot).Order());
    }

    /// <summary>It arrives its delay after the tick of the step that said it, the step's tick carried with it.</summary>
    [Fact]
    public void ACalloutArrivesAfterItsDelay()
    {
        (Events sink, _) = Run((2, 2, 3));
        AiEvent e = Assert.Single(sink.Callouts);
        Assert.Equal((5 + Spotted.DelayTicks, 5f), (e.Tick.Value, e.Velocity.X));
    }

    /// <summary>A delay asked shorter than 400 ms is held to it: the bound the session's determinism rests on.</summary>
    [Fact]
    public void ACalloutIsNeverSoonerThanItsShortestDelay()
    {
        (Events sink, _) = Run((2, 2, 3), Spotted with { DelayTicks = 1 });
        Assert.Equal(5 + AiSession.CalloutMinDelayTicks, Assert.Single(sink.Callouts).Tick.Value);
    }

    /// <summary>A deathmatch has no teammates: nobody hears anything.</summary>
    [Fact]
    public void WithNoTeamsNobodyHearsACallout()
    {
        (Events sink, _) = Run((0, 0, 0), Spotted with { Team = 0 });
        Assert.Empty(sink.Callouts);
    }

    /// <summary>A line asked for goes to the binding's chat once.</summary>
    [Fact]
    public void ACalloutAskingForALineIsHandedToChat()
    {
        (_, AiSession s) = Run((2, 2, 3));
        SpokenCallout c = Assert.Single(s.TakeSpoken());
        Assert.Equal((A, CalloutKind.Enemy), (c.Speaker, c.Say.Kind));
        Assert.Empty(s.TakeSpoken());
    }

    [Fact]
    public void AnIntentsSayRoundTripsAndOneWithoutIsFormatFivesBytes()
    {
        IntentCommand plain = IntentCommand.Hold(A, new Tick(6), 1);
        byte[] Bytes(in IntentCommand i)
        {
            var m = new MemoryStream();
            using (var w = new BinaryWriter(m, System.Text.Encoding.UTF8, leaveOpen: true))
                AiRecordFormat.Write(w, i);
            return m.ToArray();
        }

        byte[] without = Bytes(plain), with = Bytes(plain with { Say = Spotted });
        Assert.Equal(without, with[..without.Length]);
        Assert.Equal(Spotted, AiRecordFormat.ReadIntent(new BinaryReader(new MemoryStream(with))).Say);
        Assert.Equal(default, AiRecordFormat.ReadIntent(new BinaryReader(new MemoryStream(without))).Say);
    }

    // ---------------------------------------------------------------- hearing one

    private static BotMemory Hearing(int speakerTeam, int subjectTeam)
    {
        BotMemory m = TeamMemoryTests.Memory();
        m.Apply(AiEvent.ActorTeam(TeamMemoryTests.Bot, new Tick(1), TeamMemoryTests.Bot, 2, 0));
        m.Apply(AiEvent.ActorTeam(TeamMemoryTests.Bot, new Tick(1), TeamMemoryTests.Mate, speakerTeam, 0));
        m.Apply(AiEvent.ActorTeam(TeamMemoryTests.Bot, new Tick(1), TeamMemoryTests.Foe, subjectTeam, 0));
        m.Apply(AiEvent.Callout(TeamMemoryTests.Bot, new Tick(40), TeamMemoryTests.Mate, Spotted with { Subject = TeamMemoryTests.Foe.Slot + 1 }));
        return m;
    }

    [Fact]
    public void AnEnemyCalledOutIsSomewhereToLook()
    {
        BotMemory m = Hearing(2, 3);
        Assert.Equal((1, 1), (m.CalloutsHeard, m.Board.InvestigationCount));
        Assert.Equal(Spotted.Place, m.LastEnemyCall.Place);
    }

    /// <summary>B.9: a speaker who is no longer a friend is not listened to.</summary>
    [Fact]
    public void ACalloutFromAPlayerWhoSwappedIsIgnored()
    {
        BotMemory m = Hearing(3, 3);
        Assert.Equal((0, 1, 0), (m.CalloutsHeard, m.CalloutsDropped, m.Board.InvestigationCount));
    }

    /// <summary>B.9: a callout about someone who has since become a friend is dropped on arrival.</summary>
    [Fact]
    public void ACalloutAboutANewTeammateIsDropped()
    {
        BotMemory m = Hearing(2, 2);
        Assert.Equal((0, 1), (m.CalloutsHeard, m.CalloutsDropped));
    }

    // ---------------------------------------------------------------- saying one

    private static readonly AiVector Seen = new(600, 0, 0);

    [Fact]
    public void ABotCallsOutAnEnemyNewlySeenWithATeammateAbout()
    {
        int said = 0;
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var d = new Duel(Duel.Preset("normal"), Duel.Bot, seed, null, null, null);
            d.StepTeams(d.Self() with { Team = 2 }, [(Duel.Other, new AiVector(-300, 0, 0), 2)]);
            IntentCommand i = d.StepTeams(d.Self() with { Team = 2 }, [(Duel.Other, new AiVector(-300, 0, 0), 2), (Duel.Opponent, Seen, 3)],
                                          d.SeeAt(Duel.Opponent, Seen));
            if (i.Say.Says)
            {
                said++;
                Assert.Equal((CalloutKind.Enemy, Duel.Opponent.Slot + 1, 2, true), (i.Say.Kind, i.Say.Subject, i.Say.Team, i.Say.Text));
                Assert.InRange(i.Say.DelayTicks, AiSession.CalloutMinDelayTicks, 81);
            }
        }

        // One sighting in three goes unsaid: over eight seeds some are said and some are not.
        Assert.InRange(said, 1, 7);
    }

    /// <summary>Alone, nobody to tell: no callout whatever the draw.</summary>
    [Fact]
    public void ABotWithNoTeammateSaysNothing()
    {
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var d = new Duel(Duel.Preset("normal"), Duel.Bot, seed, null, null, null);
            d.StepTeams(d.Self() with { Team = 2 }, []);
            IntentCommand i = d.StepTeams(d.Self() with { Team = 2 }, [(Duel.Opponent, Seen, 3)], d.SeeAt(Duel.Opponent, Seen));
            Assert.False(i.Say.Says);
        }
    }

    /// <summary>The place is the pickup by the enemy, not where it stands.</summary>
    [Fact]
    public void TheCalledPlaceIsTheNearestPickup()
    {
        var kit = new ItemSpawn(0, "item_healthkit", ItemKind.Health, new AiVector(700, 100, 0), 30f);
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var d = new Duel(Duel.Preset("normal"), Duel.Bot, seed, null, null, null, kit);
            d.StepTeams(d.Self() with { Team = 2 }, [(Duel.Other, new AiVector(-300, 0, 0), 2)]);
            IntentCommand i = d.StepTeams(d.Self() with { Team = 2 }, [(Duel.Other, new AiVector(-300, 0, 0), 2), (Duel.Opponent, Seen, 3)],
                                          d.SeeAt(Duel.Opponent, Seen));
            if (i.Say.Says)
            {
                Assert.Equal(kit.Position, i.Say.Place);
                return;
            }
        }

        Assert.Fail("no seed called the sighting out");
    }

    // ---------------------------------------------------------------- in words

    [Fact]
    public void AnEnemyCalloutReadsAsTeamChat()
    {
        var kit = new ItemSpawn(0, "item_healthkit", ItemKind.Health, new AiVector(256, 256, 0), 30f);
        var map = new MapKnowledge("m", [kit], [], new WeaponKnowledge([]));
        Assert.Equal("2 enemies by the health kit", CalloutWords.Line(Spotted, new ChatPlaces(map, null), map));
    }

    [Fact]
    public void ABotSaysOneTeamLineInTwentySeconds()
    {
        var last = new Dictionary<int, int>();
        Assert.True(TeamCalloutChat.Due(last, 3, 100, 0.015f));
        Assert.False(TeamCalloutChat.Due(last, 3, 100 + 1000, 0.015f));
        Assert.True(TeamCalloutChat.Due(last, 3, 100 + 1400, 0.015f));
    }
}

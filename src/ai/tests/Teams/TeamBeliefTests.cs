using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Teams;

/// <summary>
/// A teammate has no belief of where it is (plan_bots_teams_tf2 B.3: an enemy's memory is built,
/// a teammate's never is): losing sight of one, or hearing one, starts no spread over the graph,
/// so it costs no hop, where an enemy's does. On ctf_2fort with twelve bots the teammates' beliefs
/// were five hops in six (tools/ai-replay cost), and nothing reads one: a target is an enemy.
/// Where a teammate was last seen and heard is still remembered.
/// </summary>
public sealed class TeamBeliefTests
{
    private static readonly ActorId Bot = new(3, 1), Mate = new(4, 2), Foe = new(2, 0x4000_0001);
    private const int Ours = 2, Theirs = 3;
    private static readonly AiVector Seen = new(240f, 48f, 0f);

    private static readonly NavGraph Floor = Grids.Parse([.. Enumerable.Repeat(new string('.', 32), 4)]).Graph;

    private static BotMemory Memory(StepCost? cost = null)
        => new(Bot, 8, new MemoryParams(4f, 120f), new MemoryWorld(MapKnowledge.Empty(new WeaponKnowledge([])), Floor, 0.015f, id => id.ToString()), new HabitStore())
        {
            Cost = cost,
        };

    private static PerceivedView View(int tick, params (ActorId Id, int Team)[] seen)
    {
        var v = new PerceivedView(8);
        v.Reset(Bot, new Tick(tick), new SelfState(new AiVector(48f, 48f, 0f), default, default, 100, 0, 3, 45, 90, SelfFlags.Alive, Team: Ours));
        foreach ((ActorId id, int team) in seen)
            v.AddSighted(new ActorFacts(id, Seen, default, 3, ActorActivity.None, 1f, team));
        return v;
    }

    /// <summary>The bot sees <paramref name="who"/> on <paramref name="team"/>, then loses it from sight.</summary>
    private static BotMemory SeenThenLost(ActorId who, int team, StepCost? cost = null)
    {
        BotMemory m = Memory(cost);
        m.Step(new Tick(6), View(6, (who, team)), [AiEvent.SightGained(Bot, new Tick(6), who, Seen, default, 1f)]);
        m.Step(new Tick(12), View(12), [AiEvent.SightLost(Bot, new Tick(12), who, Seen, new AiVector(190f, 0f, 0f))]);
        return m;
    }

    [Fact]
    public void AnEnemyLostFromSightIsBelievedSomewhere()
        => Assert.True(SeenThenLost(Foe, Theirs).Board.Of(Foe, make: false)!.Where!.Active, "the control: an enemy's belief begins where it was lost");

    [Fact]
    public void ATeammateLostFromSightIsGivenNoBelief()
        => Assert.False(SeenThenLost(Mate, Ours).Board.Of(Mate, make: false)!.Where is { Active: true });

    /// <summary>Where it was lost is still what the bot remembers of it: its last place, its velocity, its cell.</summary>
    [Fact]
    public void ATeammateLostFromSightIsRememberedWhereItWas()
    {
        KnownActor mate = SeenThenLost(Mate, Ours).Board.Of(Mate, make: false)!;
        Assert.Equal((Seen, new AiVector(190f, 0f, 0f), Floor.FindCell(Seen + new AiVector(0f, 0f, 1f)), false),
                     (mate.LastSeenPos, mate.LastSeenVel, mate.LastKnownCell, mate.InSight));
    }

    private static BotMemory Heard(ActorId who, int team)
    {
        BotMemory m = SeenThenLost(who, team);
        m.Board.Of(who, make: false)!.Where?.Clear();
        m.Step(new Tick(18), View(18), [AiEvent.SoundHeard(Bot, new Tick(18), SoundKind.Gunfire, Seen + new AiVector(64f, 0f, 0f), 1f, who, -1)]);
        return m;
    }

    [Fact]
    public void AnEnemyHeardIsBelievedRoundTheSound()
        => Assert.True(Heard(Foe, Theirs).Board.Of(Foe, make: false)!.Where!.Active, "the control: hearing an enemy gathers its belief round the sound");

    [Fact]
    public void ATeammateHeardIsGivenNoBelief()
    {
        KnownActor mate = Heard(Mate, Ours).Board.Of(Mate, make: false)!;
        Assert.False(mate.Where is { Active: true });
        Assert.Equal(Seen + new AiVector(64f, 0f, 0f), mate.LastHeardPos);
    }

    /// <summary>Two seconds out of sight: an enemy's belief hopped across the floor, a teammate's made no hop.</summary>
    private static long HopsOutOfSight(ActorId who, int team)
    {
        double now = 0;
        var cost = new StepCost(() => now++);
        BotMemory m = SeenThenLost(who, team, cost);
        for (int tick = 18; tick <= 144; tick += DecideSchedule.Period)
            m.Step(new Tick(tick), View(tick), []);
        return cost.Units(StepPart.Predict);
    }

    [Fact]
    public void AnEnemyOutOfSightCostsHops()
    {
        long hops = HopsOutOfSight(Foe, Theirs);
        Assert.True(hops >= 10, $"the control: {hops} hops in two seconds at 190 u/s over 32 u cells");
    }

    [Fact]
    public void ATeammateOutOfSightCostsNoHop() => Assert.Equal(0L, HopsOutOfSight(Mate, Ours));

    /// <summary>A teammate who turns enemy out of sight is believed from its next sign: until then it is where it was last seen.</summary>
    [Fact]
    public void ATeammateTurnedEnemyIsBelievedFromItsNextLoss()
    {
        BotMemory m = SeenThenLost(Mate, Ours);
        m.Step(new Tick(18), View(18), [AiEvent.ActorTeam(Bot, new Tick(18), Mate, Theirs, Ours)]);
        KnownActor turned = m.Board.Of(Mate, make: false)!;
        Assert.Equal((false, Seen), (turned.Where is { Active: true }, turned.BelievedAt(Floor)));
        m.Step(new Tick(24), View(24, (Mate, Theirs)), [AiEvent.SightGained(Bot, new Tick(24), Mate, Seen, default, 1f)]);
        m.Step(new Tick(30), View(30), [AiEvent.SightLost(Bot, new Tick(30), Mate, Seen, default)]);
        Assert.True(turned.Where is { Active: true });
    }
}

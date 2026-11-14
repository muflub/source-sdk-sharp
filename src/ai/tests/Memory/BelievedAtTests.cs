using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Memory;

/// <summary>
/// Where a bot believes someone out of sight is (plan_hl2mp_ai §5.3.1, plan_bots_teams_tf2_part_g
/// G.2.1): the likeliest cell of the belief it spreads over the graph, which moves on from where
/// they were lost; with no belief (no graph, or nothing lost yet) where they were last seen or
/// heard, whichever is the later.
/// </summary>
public sealed class BelievedAtTests
{
    private const float Dt = 0.015f;
    private static readonly ActorId Bot = new(3, 1), Them = new(2, 0x4000_0001);
    private static readonly AiVector SeenAt = new(16f, 16f, 0f), HeardAt = new(400f, 16f, 0f), East = new(200f, 0f, 0f), Stand = new(1500f, 16f, 0f);

    private static PerceivedView View(int tick, bool sees)
    {
        var v = new PerceivedView(4);
        v.Reset(Bot, new Tick(tick), new SelfState(Stand, default, default, 100, 0, -1, 0, 0, SelfFlags.Alive));
        if (sees)
            v.AddSighted(new ActorFacts(Them, SeenAt, East, -1, ActorActivity.None, 1f));
        return v;
    }

    private static (BotMemory Memory, NavGraph? Graph) SeenThenLost(bool graph)
    {
        NavGraph? g = graph ? Grids.Parse(new string('.', 60)).Graph : null;
        var m = new BotMemory(Bot, 8, new MemoryParams(60f, 120f), new MemoryWorld(MapKnowledge.Empty(new WeaponKnowledge([])), g, Dt, id => id.ToString()), new HabitStore());
        m.Step(new Tick(1), View(1, true), [AiEvent.SightGained(Bot, new Tick(1), Them, SeenAt, East, 1f)]);
        m.Step(new Tick(2), View(2, false), [AiEvent.SightLost(Bot, new Tick(2), Them, SeenAt, East)]);
        return (m, g);
    }

    [Fact]
    public void WithNoGraphItIsWhereTheyWereLastSeen()
    {
        (BotMemory m, NavGraph? g) = SeenThenLost(graph: false);
        Assert.Equal(SeenAt, m.Board.Actor(Them.Slot).BelievedAt(g));
    }

    [Fact]
    public void HeardSinceItIsWhereTheyWereHeard()
    {
        (BotMemory m, NavGraph? g) = SeenThenLost(graph: false);
        m.Step(new Tick(40), View(40, false), [AiEvent.SoundHeard(Bot, new Tick(40), SoundKind.Footstep, HeardAt, 1f, Them, -1)]);
        Assert.Equal(HeardAt, m.Board.Actor(Them.Slot).BelievedAt(g));
    }

    [Fact]
    public void TheBeliefMovesOnFromWhereTheyWereLost()
    {
        (BotMemory m, NavGraph? g) = SeenThenLost(graph: true);
        KnownActor them = m.Board.Actor(Them.Slot);
        float atLoss = them.BelievedAt(g).X;
        for (int t = 12; t <= 2 + (int)(2f / Dt); t += 10)
            m.Step(new Tick(t), View(t, false), []);
        Assert.True(them.Where!.Active, "the control: a belief is held");
        Assert.Equal(g!.Cell(them.Where.ArgMax).Centre, them.BelievedAt(g));
        Assert.True(them.BelievedAt(g).X > atLoss + 128f, $"believed at {them.BelievedAt(g).X:F0}, lost at {atLoss:F0}");
        Assert.Equal(SeenAt, them.LastSeenPos);
    }

    /// <summary>A belief is over one graph: asked with none, the answer is the last perception.</summary>
    [Fact]
    public void AskedWithNoGraphABeliefHeldIsNotRead()
    {
        (BotMemory m, _) = SeenThenLost(graph: true);
        for (int t = 12; t <= 140; t += 10)
            m.Step(new Tick(t), View(t, false), []);
        Assert.Equal(SeenAt, m.Board.Actor(Them.Slot).BelievedAt(null));
    }
}

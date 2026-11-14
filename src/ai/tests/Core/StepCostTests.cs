using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Core;

/// <summary>
/// What a brain step costs by part (<see cref="StepCost"/>): a part is charged the time since
/// the mark before it on the clock it is given, a brain's step marks its memory's parts and its
/// decide, the beliefs' hops are the predict part's units, and a session costs its brains only
/// when asked. The clocks here count their own readings, so every number is exact.
/// </summary>
public sealed class StepCostTests
{
    private static readonly ActorId Bot = new(2, 1), Foe = new(5, 9);

    /// <summary>A clock that reads each of <paramref name="times"/> in turn.</summary>
    private static Func<double> Reads(params double[] times)
    {
        int i = 0;
        return () => times[i++];
    }

    /// <summary>A clock one microsecond later at every reading: a part's time is the readings taken in it.</summary>
    private static Func<double> Counting()
    {
        double now = 0;
        return () => now++;
    }

    private static PerceivedView View(int tick, params ActorId[] sighted)
    {
        var v = new PerceivedView(4);
        v.Reset(Bot, new Tick(tick), new SelfState(default, default, default, 100, 0, 1, 18, 150, SelfFlags.Alive));
        foreach (ActorId id in sighted)
            v.AddSighted(new ActorFacts(id, new AiVector(48f, 48f, 0f), default, 1, ActorActivity.None, 1f));
        return v;
    }

    private static BotMemory Memory(StepCost cost, NavGraph? graph = null)
        => new(Bot, 8, new MemoryParams(5f, MemoryParams.HabitHalfLife), new MemoryWorld(MapKnowledge.Empty(new WeaponKnowledge([])), graph, 0.015f, id => id.ToString()),
               new HabitStore()) { Cost = cost };

    [Fact]
    public void APartIsChargedTheTimeSinceTheMarkBefore()
    {
        var cost = new StepCost(Reads(100, 130, 190));
        cost.Begin();
        cost.Mark(StepPart.Events);
        cost.Mark(StepPart.Decide);
        Assert.Equal((30d, 60d, 90d), (cost.Micros(StepPart.Events), cost.Micros(StepPart.Decide), cost.Total));
    }

    /// <summary>A part marked twice in a step is charged both stretches, and its worst is a step's whole, not a stretch.</summary>
    [Fact]
    public void APartsWorstIsItsMostInOneStep()
    {
        var cost = new StepCost(Reads(0, 10, 15, 35, 100, 105));
        cost.Begin();
        cost.Mark(StepPart.Predict, 2);
        cost.Mark(StepPart.Decay);
        cost.Mark(StepPart.Predict, 1);
        cost.Begin();
        cost.Mark(StepPart.Predict);
        Assert.Equal((35d, 30d, 3L, 2L), (cost.Micros(StepPart.Predict), cost.Max(StepPart.Predict), cost.Units(StepPart.Predict), cost.Steps));
    }

    /// <summary>
    /// A brain's step begins its cost and marks every part of the memory and then the decide. On a
    /// counting clock each mark is one microsecond: two steps mark the events, the view, the ammo,
    /// the fading and the decide twice, and the beliefs' spread once (time has passed only by the second).
    /// </summary>
    [Fact]
    public void ABrainsStepMarksTheMemorysPartsAndItsDecide()
    {
        var cost = new StepCost(Counting());
        var brain = new Brain(Bot, 1, Memory(cost)) { Cost = cost };
        brain.Step(new Tick(6), View(6), []);
        brain.Step(new Tick(12), View(12), []);
        Assert.Equal((2L, 2d, 2d, 2d, 1d, 2d, 2d),
                     (cost.Steps, cost.Micros(StepPart.Events), cost.Micros(StepPart.Observe), cost.Micros(StepPart.Ammo), cost.Micros(StepPart.Predict),
                      cost.Micros(StepPart.Decay), cost.Micros(StepPart.Decide)));
    }

    /// <summary>The events taken in and the actors in sight are their parts' units.</summary>
    [Fact]
    public void TheEventsAndTheSightedAreTheirPartsUnits()
    {
        var cost = new StepCost(Counting());
        BotMemory m = Memory(cost);
        m.Step(new Tick(6), View(6, Foe), [AiEvent.SightGained(Bot, new Tick(6), Foe, new AiVector(48f, 48f, 0f), default, 1f), AiEvent.Holdings(Bot, new Tick(6), 2)]);
        Assert.Equal((2L, 1L), (cost.Units(StepPart.Events), cost.Units(StepPart.Observe)));
    }

    /// <summary>A belief's step answers the hops it made (one a cell's width travelled), and the memory counts them as the predict part's units.</summary>
    [Fact]
    public void TheBeliefsHopsAreThePredictPartsUnits()
    {
        (NavGraph g, _) = Grids.Parse([.. Enumerable.Repeat(new string('.', 16), 16)]);
        var alone = new PositionPredictor(g);
        alone.LostAt(g.FindCell(new AiVector(48f, 48f, 1f)), default);
        // 190 u/s over cells 32 u wide: 0.45 s is 2.67 cells, two whole hops.
        Assert.Equal(2, alone.Step(0.45f, BodyKnowledge.DefaultRunSpeed, []));

        var cost = new StepCost(Counting());
        BotMemory m = Memory(cost, g);
        m.Step(new Tick(6), View(6, Foe), [AiEvent.SightGained(Bot, new Tick(6), Foe, new AiVector(48f, 48f, 0f), default, 1f)]);
        m.Step(new Tick(12), View(12), [AiEvent.SightLost(Bot, new Tick(12), Foe, new AiVector(48f, 48f, 0f), default)]);
        // Lost at tick 12, six ticks after the step before: 0.09 s is half a cell, no hop yet.
        Assert.Equal(0L, cost.Units(StepPart.Predict));
        // Thirty ticks on, 0.45 s more: 3.2 cells in all, three hops.
        m.Step(new Tick(42), View(42), []);
        Assert.Equal(3L, cost.Units(StepPart.Predict));
    }

    private static AiSession Session(Func<ActorId, StepCost?>? costs)
    {
        var s = new AiSession(new InlineTransport(), new NoRecording(), 8, 4) { World = MemoryWorld.Empty(), Costs = costs };
        s.AddBot(Bot, 1, []);
        AiSnapshot snap = s.Snapshots.BeginWrite(new Tick(DecideSchedule.Period - Bot.Slot));
        snap.Add(Bot, new SelfState(default, default, default, 100, 0, 1, 18, 150, SelfFlags.Alive));
        s.Publish(snap);
        s.Drain(wait: true);
        return s;
    }

    [Fact]
    public void ASessionAskedCostsEachBrainItMakes()
    {
        var made = new List<(ActorId Bot, StepCost Cost)>();
        AiSession s = Session(id =>
        {
            var cost = new StepCost(Counting());
            made.Add((id, cost));
            return cost;
        });
        Assert.Equal((1L, Bot, 1L), (s.Counters.StepsRun, made.Single().Bot, made.Single().Cost.Steps));
        Assert.Same(made.Single().Cost, s.BrainIn(Bot.Slot)!.Memory!.Cost);
    }

    /// <summary>A game's session asks for none: its brains are timed by no part.</summary>
    [Fact]
    public void ASessionNotAskedCostsNoBrain()
    {
        AiSession s = Session(null);
        Assert.Equal(1L, s.Counters.StepsRun);
        Assert.True(s.BrainIn(Bot.Slot)!.Cost is null && s.BrainIn(Bot.Slot)!.Memory!.Cost is null);
    }
}

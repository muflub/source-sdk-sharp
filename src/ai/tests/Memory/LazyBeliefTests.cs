using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Memory;

/// <summary>
/// A memory makes the belief of where someone is (<see cref="KnownActor.Where"/>, a
/// <see cref="PositionPredictor"/>: some 40 KB of cells, mass and directions) when it first has
/// one to hold, an enemy lost from sight or heard, and for nobody before that: a session of many
/// slots pays for the opponents it has met, not for its capacity. What such a belief then holds is
/// what one made with the memory would: each fact's reference is a predictor made up front and
/// told the same things.
/// </summary>
public sealed class LazyBeliefTests
{
    private const int Slots = 54, Ours = 2, Theirs = 3;
    private const float Dt = 0.015f;
    private static readonly ActorId Bot = new(0, 1);
    private static readonly NavGraph Floor = Grids.Parse([.. Enumerable.Repeat(new string('.', 48), 12)]).Graph;
    private static readonly AiVector Stand = new(48f, 48f, 0f);

    private static ActorId Id(int slot) => new(slot, 0x4000_0000 | slot);

    private static AiVector SeenAt(int slot) => new(80f + 24f * slot, 48f + 32f * (slot % 9), 0f);

    private static AiVector Going(int slot) => new(190f, 30f * (slot % 5 - 2), 0f);

    private static BotMemory Memory(NavGraph? graph)
        => new(Bot, Slots, new MemoryParams(60f, 120f), new MemoryWorld(MapKnowledge.Empty(new WeaponKnowledge([])), graph, Dt, id => id.ToString()), new HabitStore());

    private static PerceivedView View(int tick, params (int Slot, int Team)[] seen)
    {
        var v = new PerceivedView(Slots);
        v.Reset(Bot, new Tick(tick), new SelfState(Stand, default, default, 100, 0, 3, 45, 90, SelfFlags.Alive, Team: Ours));
        foreach ((int slot, int team) in seen)
            v.AddSighted(new ActorFacts(Id(slot), SeenAt(slot), default, 3, ActorActivity.None, 1f, team));
        return v;
    }

    private static int Predictors(BotMemory m) => Enumerable.Range(0, m.Board.Slots).Count(s => m.Board.Actor(s).Where is not null);

    private static BotMemory SeenThenLost(int slot, int team, NavGraph? graph)
    {
        BotMemory m = Memory(graph);
        m.Step(new Tick(6), View(6, (slot, team)), [AiEvent.SightGained(Bot, new Tick(6), Id(slot), SeenAt(slot), default, 1f)]);
        m.Step(new Tick(12), View(12), [AiEvent.SightLost(Bot, new Tick(12), Id(slot), SeenAt(slot), Going(slot))]);
        return m;
    }

    [Fact]
    public void AFreshMemoryHoldsNoPredictor() => Assert.Equal(0, Predictors(Memory(Floor)));

    /// <summary>In sight there is nothing to believe: the enemy is where it is seen.</summary>
    [Fact]
    public void AnEnemyInSightHasNoneYet()
    {
        BotMemory m = Memory(Floor);
        m.Step(new Tick(6), View(6, (7, Theirs)), [AiEvent.SightGained(Bot, new Tick(6), Id(7), SeenAt(7), default, 1f)]);
        Assert.Equal((true, 0), (m.Board.Actor(7).InSight, Predictors(m)));
    }

    [Fact]
    public void AnEnemyLostFromSightIsGivenItsOwnAndNobodyElseIs()
    {
        BotMemory m = SeenThenLost(7, Theirs, Floor);
        Assert.Equal((true, 1), (m.Board.Actor(7).Where is { Active: true }, Predictors(m)));
    }

    [Fact]
    public void AnEnemyHeardAndNeverSeenIsGivenItsOwn()
    {
        BotMemory m = Memory(Floor);
        m.Step(new Tick(6), View(6), [AiEvent.ActorTeam(Bot, new Tick(6), Id(9), Theirs, 0),
                                       AiEvent.SoundHeard(Bot, new Tick(6), SoundKind.Gunfire, SeenAt(9), 1f, Id(9), -1)]);
        Assert.Equal((true, 1), (m.Board.Actor(9).Where is { Active: true }, Predictors(m)));
    }

    /// <summary>Known from the scoreboard and never seen: a teammate's callout of it is the first place it is believed.</summary>
    [Fact]
    public void AnEnemyATeammateCallsOutIsGivenItsOwn()
    {
        BotMemory m = Memory(Floor);
        var said = new SayIntent(CalloutKind.Enemy, SeenAt(7), 1, 7 + 1, 40, Ours, false);
        m.Step(new Tick(18), View(18, (4, Ours)), [AiEvent.ActorTeam(Bot, new Tick(18), Id(7), Theirs, 0),
                                                    AiEvent.Callout(Bot, new Tick(18), Id(4), said)]);
        Assert.Equal((1, true, 1), (m.CalloutsHeard, m.Board.Actor(7).Where is { Active: true }, Predictors(m)));
    }

    /// <summary>A teammate has no belief of where it is (TeamBeliefTests): none is made for one, lost or heard.</summary>
    [Fact]
    public void ATeammateLostAndHeardIsGivenNone()
    {
        BotMemory m = SeenThenLost(7, Ours, Floor);
        m.Step(new Tick(18), View(18), [AiEvent.SoundHeard(Bot, new Tick(18), SoundKind.Gunfire, SeenAt(7), 1f, Id(7), -1)]);
        Assert.Equal((true, 0), (m.Board.Actor(7).EverHeard, Predictors(m)));
    }

    [Fact]
    public void WithNoGraphNobodyIsGivenOne()
    {
        BotMemory m = SeenThenLost(7, Theirs, null);
        Assert.Equal((false, 0), (m.Board.Actor(7).InSight, Predictors(m)));
    }

    /// <summary>Lost where the graph has no cell, there is no place to believe it at: none is made.</summary>
    [Fact]
    public void AnEnemyLostOffTheGraphIsGivenNone()
    {
        BotMemory m = Memory(Floor);
        AiVector off = new(-500f, -500f, 0f);
        m.Step(new Tick(6), View(6), [AiEvent.SightGained(Bot, new Tick(6), Id(7), off, default, 1f)]);
        m.Step(new Tick(12), View(12), [AiEvent.ActorTeam(Bot, new Tick(12), Id(7), Theirs, 0), AiEvent.SightLost(Bot, new Tick(12), Id(7), off, Going(7))]);
        Assert.Equal((false, off, 0), (m.Board.Actor(7).InSight, m.Board.Actor(7).LastSeenPos, Predictors(m)));
    }

    /// <summary>An enemy lost again keeps the predictor it has: one is made at its first loss, not at each.</summary>
    [Fact]
    public void AnEnemyLostAgainKeepsItsPredictor()
    {
        BotMemory m = SeenThenLost(7, Theirs, Floor);
        PositionPredictor first = m.Board.Actor(7).Where!;
        m.Step(new Tick(18), View(18, (7, Theirs)), [AiEvent.SightGained(Bot, new Tick(18), Id(7), SeenAt(7), default, 1f)]);
        m.Step(new Tick(24), View(24), [AiEvent.SightLost(Bot, new Tick(24), Id(7), SeenAt(7), Going(7))]);
        m.Step(new Tick(30), View(30), [AiEvent.SoundHeard(Bot, new Tick(30), SoundKind.Footstep, SeenAt(7), 1f, Id(7), -1)]);
        Assert.Same(first, m.Board.Actor(7).Where);
    }

    // ---------------------------------------------------------------- the same beliefs

    /// <summary>
    /// Every other slot an enemy: all seen, all lost from sight (the odd ones moving), the even ones
    /// then heard from out of it, over two seconds of decides. Beside the memory, a predictor a slot
    /// made up front over one scratch (what the memory's constructor made before) is told the same
    /// things in the same order.
    /// </summary>
    private static (BotMemory Memory, PositionPredictor[] Reference) EverySlotLostOrHeard()
    {
        BotMemory m = Memory(Floor);
        var scratch = new PredictorScratch(Floor.CellCount);
        PositionPredictor[] reference = [.. Enumerable.Range(0, Slots).Select(_ => new PositionPredictor(Floor, scratch))];
        (int Slot, int Team)[] all = [.. Enumerable.Range(1, Slots - 1).Select(s => (s, Theirs))];
        CellId Cell(AiVector p) => Floor.FindCell(p + new AiVector(0f, 0f, 1f));
        int last = 6;

        // What the memory's own step does after the tick's events: each belief held spreads for the time since the
        // last step, by the speed and the route prior the memory reads then (asked before the step, whose end decays
        // the habits).
        void Spread(int tick)
        {
            for (int s = 1; s < Slots; s++)
            {
                KnownActor a = m.Board.Actor(s);
                if (reference[s].Active)
                    reference[s].Step((tick - last) * Dt, m.OpponentSpeed(a), m.TryHabits(a.Id, out OpponentHabits? h) ? h!.Route : []);
            }

            last = tick;
        }

        m.Step(new Tick(6), View(6, all), [.. all.Select(a => AiEvent.SightGained(Bot, new Tick(6), Id(a.Slot), SeenAt(a.Slot), default, 1f))]);

        // Out of sight: the odd slots moving, the even ones standing.
        var lost = new List<AiEvent>();
        for (int s = 1; s < Slots; s++)
        {
            AiVector going = s % 2 == 1 ? Going(s) : default;
            lost.Add(AiEvent.SightLost(Bot, new Tick(12), Id(s), SeenAt(s), going));
            reference[s].LostAt(Cell(SeenAt(s)), going);
        }

        Spread(12);
        m.Step(new Tick(12), View(12), [.. lost]);
        for (int tick = 18; tick <= 150; tick += DecideSchedule.Period)
        {
            // The even slots are heard once, a way off from where they were lost.
            var heard = new List<AiEvent>();
            for (int s = 2; s < Slots && tick == 60; s += 2)
            {
                AiVector at = SeenAt(s) + new AiVector(96f, 0f, 0f);
                heard.Add(AiEvent.SoundHeard(Bot, new Tick(tick), SoundKind.Footstep, at, 0.6f, Id(s), -1));
                reference[s].Heard(Cell(at), MathF.Max(64f, Stand.DistanceTo(at) * 0.15f));
            }

            Spread(tick);
            m.Step(new Tick(tick), View(tick), [.. heard]);
        }

        return (m, reference);
    }

    [Fact]
    public void EveryEnemyLostOrHeardHoldsAPredictorAndNoOtherSlotDoes()
    {
        (BotMemory m, _) = EverySlotLostOrHeard();
        Assert.Equal(Enumerable.Range(1, Slots - 1), Enumerable.Range(0, Slots).Where(s => m.Board.Actor(s).Where is { Active: true }));
        Assert.Equal(Slots - 1, Predictors(m));
    }

    [Fact]
    public void EveryEnemysBeliefIsWhatAPredictorMadeUpFrontHolds()
    {
        (BotMemory m, PositionPredictor[] reference) = EverySlotLostOrHeard();
        long cells = 0;
        for (int s = 1; s < Slots; s++)
        {
            PositionPredictor where = m.Board.Actor(s).Where!;
            Assert.Equal(reference[s].MassCells.ToArray(), where.MassCells.ToArray());
            Assert.Equal(reference[s].Masses.ToArray(), where.Masses.ToArray());
            Assert.Equal(Floor.Cell(reference[s].ArgMax).Centre, m.Board.Actor(s).BelievedAt(Floor));
            cells += where.CellsWithMass;
        }

        Assert.True(cells > 4 * (Slots - 1), $"the stimulus: the beliefs spread ({cells} cells with mass over {Slots - 1} enemies)");
    }

    /// <summary>The predictors of one memory step one after another over one scratch, as they did when the constructor made them all.</summary>
    [Fact]
    public void AMemorysBeliefsAreEachWhatTheyAreAlone()
    {
        (BotMemory all, _) = EverySlotLostOrHeard();
        BotMemory alone = SeenThenLost(7, Theirs, Floor);
        // Slot 7 is lost at tick 12 in both, with the same velocity; the same decides follow.
        for (int tick = 18; tick <= 150; tick += DecideSchedule.Period)
            alone.Step(new Tick(tick), View(tick), []);
        Assert.Equal(alone.Board.Actor(7).Where!.MassCells.ToArray(), all.Board.Actor(7).Where!.MassCells.ToArray());
        Assert.Equal(alone.Board.Actor(7).Where!.Masses.ToArray(), all.Board.Actor(7).Where!.Masses.ToArray());
    }
}

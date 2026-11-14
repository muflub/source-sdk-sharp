using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Knowledge;
using SourceSharp.Shared;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Knowledge;

/// <summary>
/// The game's bodies (plan_bots_teams_tf2_part_g G.2.1): a class's row answers for it, a class
/// with none and class 0 answer the unknown body's; a knowledge that names no bodies believes the
/// 190 u/s every recording was made with, and so does HL2MP's, to the bit; and an actor out of
/// sight is believed to move at the run of the class it was last drawn as.
/// </summary>
public sealed class BodyKnowledgeTests
{
    private const int Fast = 1, Slow = 6;
    private static readonly ActorId Bot = new(3, 1);
    private static readonly ActorId Them = new(2, 0x4000_0001);

    private static BodyKnowledge TwoClasses() => new(new BodyFacts(300f), [(Fast, new BodyFacts(400f)), (Slow, new BodyFacts(230f))]);

    [Fact]
    public void AClassWithARowAnswersItsOwn()
        => Assert.Equal((400f, 230f), (TwoClasses().Of(Fast).RunSpeed, TwoClasses().Of(Slow).RunSpeed));

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(99)]
    [InlineData(-1)]
    public void AClassWithoutARowAnswersTheUnknownBodys(int playerClass)
        => Assert.Equal(300f, TwoClasses().Of(playerClass).RunSpeed);

    [Fact]
    public void ClassZeroIsNoRow()
        => Assert.Throws<ArgumentException>(() => new BodyKnowledge(new BodyFacts(300f), [(0, new BodyFacts(1f))]));

    [Fact]
    public void AKnowledgeThatNamesNoBodiesBelievesTheRecordingsSpeedOfEveryone()
    {
        MapKnowledge map = MapKnowledge.Empty(new WeaponKnowledge([]));
        for (int c = 0; c < 12; c++)
            Assert.Equal(190f, map.Bodies.Of(c).RunSpeed);
    }

    /// <summary>
    /// A recording's world carries no bodies, so a replay believes the default's speed: HL2MP's own
    /// answer must be that float exactly, or a live session and its replay drift apart.
    /// </summary>
    [Fact]
    public void Hl2mpsKnowledgeSaysTheSameSpeedARecordingsWorldDoesForEveryone()
    {
        WeaponCatalog catalog = WeaponCatalog.Stock();
        MapKnowledge map = MapKnowledgeBuilder.Build("m", new MapEntities([], null), catalog, WeaponKnowledgeBuilder.Build(catalog, _ => 1.5f), 20f, 30f);
        for (int c = 0; c < 12; c++)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(190f), BitConverter.SingleToInt32Bits(map.Bodies.Of(c).RunSpeed));
            Assert.Equal(BodyKnowledge.DefaultRunSpeed, map.Bodies.Of(c).RunSpeed);
        }
    }

    // A corridor of 32 u cells running east; the actor is lost at its west end, going east.
    private const int Cells = 60;

    private static (BotMemory Memory, NavGraph Graph) Lost(BodyKnowledge? bodies, int drawnAs)
    {
        (NavGraph graph, int[,] _) = Grids.Parse(new string('.', Cells));
        var map = new MapKnowledge("m", [], [], new WeaponKnowledge([]), bodies: bodies);
        var m = new BotMemory(Bot, 8, new MemoryParams(60f, 120f), new MemoryWorld(map, graph, 0.015f, id => id.ToString()), new HabitStore());
        var at = new AiVector(16f, 16f, 0f);
        var east = new AiVector(200f, 0f, 0f);
        var seen = new PerceivedView(4);
        seen.Reset(Bot, new Tick(1), new SelfState(new AiVector(1900f, 16f, 0f), default, default, 100, 0, -1, 0, 0, SelfFlags.Alive));
        seen.AddSighted(new ActorFacts(Them, at, east, -1, ActorActivity.None, 1f, Class: drawnAs));
        m.Step(new Tick(1), seen, [AiEvent.SightGained(Bot, new Tick(1), Them, at, east, 1f)]);
        var gone = new PerceivedView(4);
        gone.Reset(Bot, new Tick(2), new SelfState(new AiVector(1900f, 16f, 0f), default, default, 100, 0, -1, 0, 0, SelfFlags.Alive));
        m.Step(new Tick(2), gone, [AiEvent.SightLost(Bot, new Tick(2), Them, at, east)]);
        return (m, graph);
    }

    /// <summary>How far east the belief's likeliest cell is, <paramref name="seconds"/> after the loss, a decide at a time.</summary>
    private static float BelievedEast(BodyKnowledge? bodies, int drawnAs, float seconds)
    {
        (BotMemory m, NavGraph graph) = Lost(bodies, drawnAs);
        var view = new PerceivedView(4);
        int last = 2 + (int)(seconds / 0.015f);
        for (int t = 12; t <= last; t += 10)
        {
            view.Reset(Bot, new Tick(t), new SelfState(new AiVector(1900f, 16f, 0f), default, default, 100, 0, -1, 0, 0, SelfFlags.Alive));
            m.Step(new Tick(t), view, []);
        }

        PositionPredictor where = m.Board.Actor(Them.Slot).Where!;
        Assert.True(where.Active, "the control: a belief is held");
        return graph.Cell(where.ArgMax).Centre.X;
    }

    [Fact]
    public void AnActorOutOfSightIsBelievedToMoveAtTheRunOfTheClassItWasDrawnAs()
    {
        (BotMemory m, _) = Lost(TwoClasses(), Fast);
        Assert.Equal(400f, m.OpponentSpeed(m.Board.Actor(Them.Slot)));
    }

    [Fact]
    public void AnActorNeverDrawnAsAClassIsBelievedToMoveAtTheUnknownBodysRun()
    {
        (BotMemory m, _) = Lost(TwoClasses(), 0);
        Assert.Equal(300f, m.OpponentSpeed(m.Board.Actor(Them.Slot)));
    }

    [Fact]
    public void TheFasterClassIsBelievedFurtherAlongAfterTheSameTime()
    {
        float fast = BelievedEast(TwoClasses(), Fast, 2f), slow = BelievedEast(TwoClasses(), Slow, 2f);
        Assert.True(fast > slow + 128f, $"fast {fast:F0}, slow {slow:F0}");
    }

    /// <summary>With no bodies named the class drawn changes nothing: every actor is believed where 190 u/s puts it.</summary>
    [Fact]
    public void WithNoBodiesNamedEveryClassIsBelievedInTheSamePlace()
        => Assert.Equal(BelievedEast(null, 0, 2f), BelievedEast(null, Fast, 2f));
}

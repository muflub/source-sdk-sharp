using SourceSharp.Ai.Hl2mp.Senses;
using SourceSharp.Ai.Tests.Senses;
using Xunit;

namespace SourceSharp.Ai.Tests.Tactics;

/// <summary>
/// What a prefire asks of the world (plan_hl2mp_ai §5.4): a line from the bot's own eye
/// through what it can see of the world and props, with the vision pass's trace seam,
/// within its budget. No player stops it, so its answer says nothing of who is there.
/// </summary>
public sealed class LineAnswerTests
{
    private const float Tick = 0.015f;
    private static readonly VisionParams Eyes = new(6000f, 90f, 0.3f, 0.5f);
    private static readonly ActorId Bot = new(1, 1);

    private sealed class Rig
    {
        public readonly SenseWorld World = new();
        public readonly VisionPass Pass;
        public readonly SenseWorld.Player Me;
        public readonly List<AiEvent> Events = new();
        private int _tick = 1;

        public Rig()
        {
            Me = World.Add(1, new AiVector(0, 0, 0));
            Pass = new VisionPass(World.Engine(), World.Entities(), World.Traces());
            Pass.Add(Bot);
            Sense();
        }

        public SensedBot Sensed => new(Bot, Me.Handle, Eyes);

        public void Sense() => Pass.Run(new Tick(++_tick), Tick, [Sensed], World.Living(Sensed), Events);

        public AiEvent? Ask(int ask, AiVector point)
        {
            Events.Clear();
            Pass.AnswerLine(new Tick(_tick), Sensed, ask, point, Events);
            return Events.Count > 0 ? Events[^1] : null;
        }
    }

    private static readonly AiVector Far = new(500, 0, 48);

    [Fact]
    public void AnOpenLineIsClear()
    {
        AiEvent? e = new Rig().Ask(1, Far);
        Assert.NotNull(e);
        Assert.Equal(AiEventKind.LineAnswer, e.Value.Kind);
        Assert.Equal((Far, 1, 1f), (e.Value.Position, e.Value.Code, e.Value.Amount));
    }

    [Fact]
    public void AWallIsNot()
    {
        var r = new Rig();
        r.World.Occluder(new AiVector(200, -100, 0), new AiVector(220, 100, 200), 0x1);
        Assert.Equal(0f, r.Ask(1, Far)!.Value.Amount);
    }

    /// <summary>A shut door or a stack of crates stops it as a wall does.</summary>
    [Fact]
    public void AProp()
    {
        var r = new Rig();
        r.World.Occluder(new AiVector(200, -100, 0), new AiVector(220, 100, 200), 0x4000, entity: 7);
        Assert.Equal(0f, r.Ask(1, Far)!.Value.Amount);
    }

    /// <summary>
    /// Truth does not reach it: someone stood on the line, or behind the wall at the
    /// end of it, changes nothing about the answer. The mask holds no bodies.
    /// </summary>
    [Fact]
    public void ABodyDoesNotStopIt()
    {
        var alone = new Rig();
        var crowded = new Rig();
        crowded.World.Add(2, new AiVector(250, 0, 0));
        crowded.Sense();
        Assert.Equal(alone.Ask(1, Far)!.Value.Amount, crowded.Ask(1, Far)!.Value.Amount);
        Assert.Equal(0u, VisionPass.LineMask & 0x2000000u);
        Assert.NotEqual(0u, VisionPass.SightMask & 0x2000000u);
    }

    /// <summary>One answer an ask: asked again it stays quiet, and the next ask gets its own.</summary>
    [Fact]
    public void AnAskIsAnsweredOnce()
    {
        var r = new Rig();
        Assert.NotNull(r.Ask(1, Far));
        Assert.Null(r.Ask(1, Far));
        Assert.NotNull(r.Ask(2, Far));
    }

    /// <summary>Over its budget it waits a tick; the ask is not lost.</summary>
    [Fact]
    public void TheBudgetHoldsAndNothingIsLost()
    {
        var r = new Rig();
        var lanes = new (SensedBot Bot, int Slot)[VisionPass.LineTracesPerTick + 2];
        for (int i = 0; i < lanes.Length; i++)
        {
            SenseWorld.Player p = i == 0 ? r.Me : r.World.Add(i + 1, new AiVector(0, 64 * i, 0));
            var id = new ActorId(i + 1, 1);
            if (i > 0)
                r.Pass.Add(id);
            lanes[i] = (new SensedBot(id, p.Handle, Eyes), i + 1);
        }

        SensedBot[] bots = [.. lanes.Select(l => l.Bot)];
        r.Events.Clear();
        r.Pass.Run(new Tick(100), Tick, bots, r.World.Living(bots), r.Events);
        r.Events.Clear();
        foreach (SensedBot b in bots)
            r.Pass.AnswerLine(new Tick(100), b, 1, Far, r.Events);
        Assert.Equal(VisionPass.LineTracesPerTick, r.Events.Count);
        long traces = r.Pass.Traces;
        r.Pass.Run(new Tick(101), Tick, bots, r.World.Living(bots), r.Events);
        r.Events.Clear();
        foreach (SensedBot b in bots)
            r.Pass.AnswerLine(new Tick(101), b, 1, Far, r.Events);
        Assert.Equal(2, r.Events.Count);
        Assert.True(r.Pass.Traces > traces);
    }
}

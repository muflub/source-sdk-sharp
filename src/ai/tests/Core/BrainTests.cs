using Xunit;

namespace SourceSharp.Ai.Tests.Core;

public class BrainTests
{
    private static readonly ActorId Bot = new(2, 1);

    private static PerceivedView View(int tick, ActorId? bot = null)
    {
        var v = new PerceivedView(4);
        v.Reset(bot ?? Bot, new Tick(tick), default);
        return v;
    }

    [Fact]
    public void AStepHoldsFromItsTick()
    {
        IntentCommand i = new Brain(Bot, 1).Step(new Tick(12), View(12), []);
        Assert.Equal((MoveKind.Hold, new Tick(12), new Tick(28)), (i.Move.Kind, i.FromTick, i.ExpiresTick));
    }

    [Fact]
    public void TheIntentIsTheBrainsBots()
        => Assert.Equal(Bot, new Brain(Bot, 1).Step(new Tick(12), View(12), []).Bot);

    [Fact]
    public void EachStepTakesTheNextSequence()
    {
        var b = new Brain(Bot, 1);
        int first = b.Step(new Tick(6), View(6), []).Seq;
        Assert.Equal(first + 1, b.Step(new Tick(12), View(12), []).Seq);
    }

    [Fact]
    public void ABrainNeverStepsBack()
    {
        var b = new Brain(Bot, 1);
        b.Step(new Tick(12), View(12), []);
        Assert.Throws<ArgumentOutOfRangeException>(() => b.Step(new Tick(6), View(6), []));
    }

    [Fact]
    public void ABrainRefusesAnotherBotsView()
        => Assert.Throws<ArgumentException>(() => new Brain(Bot, 1).Step(new Tick(6), View(6, new ActorId(3, 1)), []));

    [Fact]
    public void ABrainRefusesAViewOfAnotherTick()
        => Assert.Throws<ArgumentException>(() => new Brain(Bot, 1).Step(new Tick(6), View(12), []));

    [Fact]
    public void AnIntentExpiresSixteenTicksOn()
    {
        IntentCommand i = IntentCommand.Hold(Bot, new Tick(100), 1);
        Assert.Equal((false, true), (i.ExpiredAt(new Tick(115)), i.ExpiredAt(new Tick(116))));
    }
}

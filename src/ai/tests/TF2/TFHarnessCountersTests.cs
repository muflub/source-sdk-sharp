using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// What the per-class A/B harness reads off a decider (plan_bots_teams_tf2 P7 step 5, <c>/ai/classes</c>): each ability
/// row's uses, counted when the one-shot is sent, and a class's own counters through its play's hook.
/// </summary>
public sealed class TFHarnessCountersTests
{
    /// <summary>A Soldier's rocket jump is counted against its row (0); a decide with no ability counts nothing.</summary>
    [Fact]
    public void AnAbilityOneShotIsCountedByItsRow()
    {
        var f = new TFField(jumps: new TFJumpAdvice());
        for (int i = 0; i < 20; i++)
            f.Step(TFField.Self(new AiVector(300f, 256f, 0f), velocity: new AiVector(240f, 0f, 0f)), TFField.Ctf());
        int row = TFAbilities.Table().Rows.IndexOf(TFAbilities.Table().Rows.Single(r => r.Name == TFAbilities.RocketJump));
        Assert.Equal((1, 1), (f.Decider.AbilityUses[row], f.Decider.AbilityUses.ToArray().Sum()));
    }

    private sealed class CountingPlay(TFDecider d) : TFDecider.ClassPlay(d, TFClass.Spy)
    {
        public int Decides { get; private set; }

        public override void OneShots() => Decides++;

        public override void Count(List<KeyValuePair<string, long>> into) => into.Add(new("spy.decides", Decides));
    }

    /// <summary>A class's own counter reaches the harness through its play: the Spy's test play counts its decides.</summary>
    [Fact]
    public void AClassesOwnCounterReachesTheHarness()
    {
        var f = new TFField(plays: (d, c) => c == TFClass.Spy ? new CountingPlay(d) : null);
        for (int i = 0; i < 3; i++)
            f.Step(TFField.Self(new AiVector(300f, 256f, 0f), playerClass: TFClass.Spy), TFField.Ctf());
        Assert.Equal([new KeyValuePair<string, long>("spy.decides", 3)], f.Decider.ClassCounters());
    }
}

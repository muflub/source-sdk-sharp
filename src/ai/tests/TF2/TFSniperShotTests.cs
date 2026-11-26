using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Server.Interop;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Sniper's shot is a shot when it went (the code review's C1): asked for while the hands may still be withholding
/// it (the reaction and the flick of first sight, BotCommandApplier.HandsWaitForTheEye), it is asked again until the
/// bot's own ammunition counter falls, and only that starts the wait for the rifle's rezoom.
/// </summary>
public sealed class TFSniperShotTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001);
    private static readonly AiVector Mid = new(2048f, 256f, 0f);
    private static readonly EntityHandle Player = new(0x0001_0002u);

    private static IntentCommand Step(TFField f, int reserve, ActorFacts enemy)
    {
        f.Hud = TFSniperPlayTests.Kit(zoomed: true, charge: 150f);
        return f.StepDrawn(TFSniperPlayTests.Me(Mid) with { Reserve = reserve }, TFField.Ctf(), enemy);
    }

    /// <summary>A shot asked for and not gone (the ammunition counter did not fall) is asked again at the next decide, not 1.6 s later.</summary>
    [Fact]
    public void AShotThatDidNotGoIsAskedAgain()
    {
        TFField f = new(seed: TFSniperPlayTests.SeedOf(TFSniperClass.Marksman), weapons: TFSniperPlayTests.Weapons);
        ActorFacts enemy = TFSniperPlayTests.Drawn(Enemy, Mid + new AiVector(900f, 0f, 0f), TFClass.Soldier);
        IntentCommand[] decides = [.. Enumerable.Range(0, 8).Select(_ => Step(f, 25, enemy))];
        int first = Array.FindIndex(decides, i => i.Action.Kind == ActionKind.Fire);
        Assert.True(first >= 0 && first + 1 < decides.Length, $"first fire at {first}");
        Assert.Equal(ActionKind.Fire, decides[first + 1].Action.Kind);
    }

    /// <summary>Once the counter falls, the next shot waits for the rifle's unzoom and rezoom (1.6 s): no fire the decide after.</summary>
    [Fact]
    public void AShotThatWentWaitsForTheRezoom()
    {
        TFField f = new(seed: TFSniperPlayTests.SeedOf(TFSniperClass.Marksman), weapons: TFSniperPlayTests.Weapons);
        ActorFacts enemy = TFSniperPlayTests.Drawn(Enemy, Mid + new AiVector(900f, 0f, 0f), TFClass.Soldier);
        int reserve = 25, fired = -1;
        var after = new List<IntentCommand>();
        for (int n = 0; n < 16; n++)
        {
            IntentCommand i = Step(f, reserve, enemy);
            if (fired >= 0)
                after.Add(i);
            if (fired < 0 && i.Action.Kind == ActionKind.Fire)
                (fired, reserve) = (n, reserve - 1);
        }

        Assert.True(fired >= 0, "never fired");
        Assert.DoesNotContain(after.Take(10), i => i.Action.Kind == ActionKind.Fire);
    }

    private sealed class Runner : IPlayerCommandRunner
    {
        public UserCmd Last;

        public void Queue(EntityHandle player, in UserCmd cmd) => Last = cmd;

        public int RunQueued(EntityHandle player) => 1;
    }

    private sealed class Self : IBotSelf
    {
        public BotSelfFacts Facts = new(true, false, Mid, Mid + new AiVector(0f, 0f, 64f), default);

        public BotSelfFacts Read(EntityHandle player) => Facts;

        public int WeaponSelectFor(EntityHandle player, int slot) => 0;
    }

    /// <summary>
    /// Through the applier: a Marksman holding its spot, scoped and charged, and a Soldier steps into sight 900 u off. It asks for the
    /// shot at once (the scope is past its gate, the charge past the 67 a Soldier needs); the hands withhold the trigger through the
    /// reaction (200 ms) and the flick; the rifle (the counter falls on the first press) fires within 1.2 s of first sight. Asked
    /// once and stamped at the asking, the first shot never went and the next waited 1.6 s.
    /// </summary>
    [Fact]
    public void TheFirstScopedShotGoesSoonAfterFirstSightThroughTheApplier()
    {
        TFField f = new(seed: TFSniperPlayTests.SeedOf(TFSniperClass.Marksman), weapons: TFSniperPlayTests.Weapons);
        AiVector at = Mid;
        for (int n = 0; n < 8; n++)
        {
            f.Hud = TFSniperPlayTests.Kit(zoomed: true, charge: 150f);
            f.StepDrawn(TFSniperPlayTests.Me(at), TFField.Ctf());
            if (f.Decider.Doing.HasGoal && f.Decider.Doing.Verdict == "to a sniper spot")
                at = f.Decider.Doing.Goal;
        }

        Assert.Equal("holding a sight line", f.Decider.Doing.Verdict);
        var runner = new Runner();
        var self = new Self { Facts = new(true, false, at, at + new AiVector(0f, 0f, 64f), default) };
        var applier = new BotCommandApplier(runner, self);
        applier.Add(f.Bot, Player, new MotionParams(200f, 487.5f, 16.2f, 174f), 42);
        ActorFacts enemy = TFSniperPlayTests.Drawn(Enemy, at + new AiVector(900f, 200f, 0f), TFClass.Soldier);
        int reserve = 25, pressedAt = -1, start = f.Tick + DecideSchedule.Period;
        for (int n = 0; n < 40 && pressedAt < 0; n++)
        {
            f.Hud = TFSniperPlayTests.Kit(zoomed: true, charge: 150f);
            IntentCommand intent = f.StepDrawn(TFSniperPlayTests.Me(at) with { Reserve = reserve }, TFField.Ctf(), enemy);
            applier.Submit(intent);
            for (int k = 0; k < DecideSchedule.Period && pressedAt < 0; k++)
            {
                applier.ApplyTick(new Tick(f.Tick + k), 0.015f);
                self.Facts = self.Facts with { View = new AiAngles(runner.Last.Pitch, runner.Last.Yaw, 0f) };
                if ((runner.Last.Buttons & (int)InputButtons.Attack) != 0)
                    (pressedAt, reserve) = (f.Tick + k, reserve - 1);
            }
        }

        Assert.True(pressedAt >= 0, "the trigger never went down in 3.6 s");
        Assert.True((pressedAt - start) * 0.015f < 1.2f, $"first press {(pressedAt - start) * 0.015f:F2} s after first sight");
    }
}

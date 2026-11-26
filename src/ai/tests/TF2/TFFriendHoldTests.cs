using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Server.Interop;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// Round 2's S9 through the decider: a class play's Fire held at a teammate on purpose (<c>FireAtFriend</c>, the Medic's
/// beam) is an intent that says so (<see cref="IntentCommand.HoldsAtFriend"/>), on a decide that carries a one-shot too (a
/// voice line taking the decide's action), and the applier keeps the Fire down through that decide while the play still
/// asks; a play that stopped asking lets go (the review's S9-2). The general fire at an enemy never asks, and is let go.
/// </summary>
public sealed class TFFriendHoldTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001), Mate = new(4, 0x4000_0001);
    private static readonly AiVector Mid = new(2048f, 256f, 0f);
    private static readonly EntityHandle Player = new(0x0001_0002u);

    /// <summary>A play that holds the trigger at the teammate 400 u off, as a beam is held, while <see cref="Asks"/>.</summary>
    private sealed class BeamPlay(TFDecider d) : TFDecider.ClassPlay(d, TFClass.Soldier)
    {
        public bool Asks { get; set; } = true;

        public override void Engage()
        {
            if (Asks)
                FireAtFriend(Mate, Mid + new AiVector(400f, 0f, 40f));
        }
    }

    private sealed class Runner : IPlayerCommandRunner
    {
        public readonly List<UserCmd> Queued = [];

        public void Queue(EntityHandle player, in UserCmd cmd) => Queued.Add(cmd);

        public int RunQueued(EntityHandle player) => 1;
    }

    private sealed class Self(AiVector eye) : IBotSelf
    {
        public BotSelfFacts Facts = new(true, false, eye - new AiVector(0f, 0f, 64f), eye, default);

        public BotSelfFacts Read(EntityHandle player) => Facts;

        public int WeaponSelectFor(EntityHandle player, int slot) => 0;
    }

    private static readonly ActorFacts Patient = new(Mate, Mid + new AiVector(400f, 0f, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Red);

    /// <summary>
    /// The commands of the decide <paramref name="next"/>, once <paramref name="held"/> has been applied for 60 ticks (long
    /// past the reaction and the flick to its look), and whether the trigger was down at the end of them.
    /// </summary>
    private static (bool HeldBefore, List<UserCmd> Next) Applied(IntentCommand held, IntentCommand next)
    {
        var runner = new Runner();
        var self = new Self(Mid + new AiVector(0f, 0f, 64f));
        var applier = new BotCommandApplier(runner, self);
        applier.Add(held.Bot, Player, new MotionParams(500f, 487.5f, 16.2f, 174f), 42);
        int tick = 100, seq = 0;
        for (int i = 0; i < 60; i++)
        {
            applier.Submit(held with { FromTick = new Tick(tick), ExpiresTick = new Tick(tick + IntentCommand.LifetimeTicks), Seq = ++seq });
            applier.ApplyTick(new Tick(tick++), 0.015f);
            self.Facts = self.Facts with { View = new AiAngles(runner.Queued[^1].Pitch, runner.Queued[^1].Yaw, 0f) };
        }

        bool before = (runner.Queued[^1].Buttons & (int)InputButtons.Attack) != 0;
        applier.Submit(next with { FromTick = new Tick(tick), ExpiresTick = new Tick(tick + IntentCommand.LifetimeTicks), Seq = ++seq });
        for (int i = 0; i < 8; i++)
            applier.ApplyTick(new Tick(tick++), 0.015f);
        return (before, runner.Queued[^8..]);
    }

    /// <summary>
    /// The beam's decide, then a decide on which the bot, hurt, calls for a Medic (the voice line is that decide's one-shot)
    /// with the play still asking or not: (the beam's intent, the call's intent).
    /// </summary>
    private static (IntentCommand Beam, IntentCommand Call) BeamThenCall(bool stillAsks)
    {
        BeamPlay? play = null;
        var f = new TFField(tune: p => p with { VoiceEverySeconds = 2f }, plays: (d, c) => c == TFClass.Soldier ? play = new BeamPlay(d) : null);
        IntentCommand beam = f.StepDrawn(TFField.Self(Mid, health: 200), TFField.Ctf(), Patient);
        play!.Asks = stillAsks;
        IntentCommand call = f.StepDrawn(TFField.Self(Mid, health: 80), TFField.Ctf(), Patient);
        return (beam, call);
    }

    /// <summary>The beam's intent holds at a friend; the call's decide carries the voice one-shot and still asks; the beam stays down through it.</summary>
    [Fact]
    public void AScriptedBeamIsHeldThroughTheDecidersOneShot()
    {
        (IntentCommand beam, IntentCommand call) = BeamThenCall(stillAsks: true);
        Assert.Equal((ActionKind.Fire, Mate, true), (beam.Action.Kind, beam.Look.Actor, beam.HoldsAtFriend));
        Assert.Equal((ActionKind.Ability, true, true), (call.Action.Kind, call.Action.OneShotSeq != 0, call.HoldsAtFriend));

        (bool held, List<UserCmd> called) = Applied(beam, call);
        Assert.True(held, "the control: the beam was down before the call");
        Assert.All(called, c => Assert.True((c.Buttons & (int)InputButtons.Attack) != 0, "the beam was let go for the call"));
    }

    /// <summary>A play that stopped asking on the call's decide: its intent does not hold at a friend, and the beam is let go through it.</summary>
    [Fact]
    public void ABeamThePlayStoppedAskingForIsLetGoThroughTheDecidersOneShot()
    {
        (IntentCommand beam, IntentCommand call) = BeamThenCall(stillAsks: false);
        Assert.Equal((ActionKind.Ability, true, false), (call.Action.Kind, call.Action.OneShotSeq != 0, call.HoldsAtFriend));

        (bool held, List<UserCmd> called) = Applied(beam, call);
        Assert.True(held, "the control: the beam was down before the call");
        Assert.DoesNotContain(called, c => (c.Buttons & (int)InputButtons.Attack) != 0);
    }

    /// <summary>The general fire at an enemy does not hold at a friend, and is let go through a one-shot decide, as before S9.</summary>
    [Fact]
    public void TheGeneralFireAtAnEnemyIsLetGoThroughThePop()
    {
        var soldier = new TFField();
        IntentCommand fire = soldier.Step(TFField.Self(Mid), TFField.Ctf(), (Enemy, Mid + new AiVector(400f, 0f, 0f), AiVector.Zero, TFField.Blue));
        Assert.Equal((ActionKind.Fire, Enemy, false), (fire.Action.Kind, fire.Look.Actor, fire.HoldsAtFriend));

        var pop = new ActionIntent(ActionKind.AltFire, FireStyle.Tap, 0, OneShotSeq: 1);
        (bool held, List<UserCmd> popped) = Applied(fire, fire with { Action = pop });
        Assert.True(held, "the control: the Fire was down before the pop");
        Assert.DoesNotContain(popped, c => (c.Buttons & (int)InputButtons.Attack) != 0);
    }
}

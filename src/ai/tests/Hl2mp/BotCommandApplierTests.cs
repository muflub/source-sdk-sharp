using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mp;

/// <summary>
/// The applier (plan_hl2mp_ai §2.4, §3.3, Phase 3 step 4): one usercmd per bot
/// per tick through the human path, the move in the view's frame, buttons, the
/// weapon by slot, the one-shot channel, the checks, and the envelope.
/// </summary>
public class BotCommandApplierTests
{
    private const float Dt = 0.015f;

    private sealed class Runner : IPlayerCommandRunner
    {
        public readonly List<string> Calls = new();
        public readonly List<(EntityHandle Player, UserCmd Cmd)> Queued = new();

        public void Queue(EntityHandle player, in UserCmd cmd)
        {
            Calls.Add($"queue {player.Value:x}");
            Queued.Add((player, cmd));
        }

        public int RunQueued(EntityHandle player)
        {
            Calls.Add($"run {player.Value:x}");
            return 1;
        }
    }

    private sealed class Self : IBotSelf
    {
        public BotSelfFacts Facts = new(true, false, new AiVector(0f, 0f, 0f), new AiVector(0f, 0f, 64f), default);
        public readonly Dictionary<int, int> Slots = new() { [2] = 7 };

        public BotSelfFacts Read(EntityHandle player) => Facts;

        public int WeaponSelectFor(EntityHandle player, int slot) => Slots.TryGetValue(slot, out int w) ? w : 0;
    }

    private static readonly ActorId Bot = new(3, 1);
    private static readonly EntityHandle Player = new(0x0001_0003u);
    private static readonly MotionParams Normal = new(500f, 487.5f, 16.2f, 174f);

    private readonly Runner _runner = new();
    private readonly Self _self = new();
    private readonly BotCommandApplier _applier;
    private int _seq;
    private int _tick = 100;

    public BotCommandApplierTests()
    {
        _applier = new BotCommandApplier(_runner, _self);
        _applier.Add(Bot, Player, Normal, 42);
    }

    private UserCmd Tick()
    {
        _applier.ApplyTick(new Tick(_tick++), Dt);
        return _runner.Queued[^1].Cmd;
    }

    private static MoveIntent Follow(AiVector corner, MoveSpeed speed = MoveSpeed.Run)
        => new(MoveKind.Follow, speed, 1, corner, default, default, default, default, false, 0);

    private static MoveIntent Kind(MoveKind kind, bool crouch = false)
        => new(kind, MoveSpeed.Run, 0, default, default, default, default, default, crouch, 0);

    /// <summary>The look straight ahead along +x from the eye, so the view stays at yaw 0.</summary>
    private static LookIntent Ahead => new(LookKind.Point, new AiVector(1000f, 0f, 64f), ActorId.None, AimPoint.Body, 0f, 0);

    private IntentRefusal Submit(MoveIntent move, LookIntent? look = null, ActionIntent action = default, ActorId? bot = null, int? seq = null)
    {
        var from = new Tick(_tick);
        var intent = new IntentCommand(bot ?? Bot, from, seq ?? ++_seq, from + IntentCommand.LifetimeTicks, move, look ?? Ahead, action, default);
        return _applier.Submit(in intent);
    }

    [Fact]
    public void EachBotRunsOneCommandPerTickThroughTheHumanPathInSlotOrder()
    {
        var second = new EntityHandle(0x0002_0002u);
        _applier.Add(new ActorId(2, 1), second, Normal, 1);

        _applier.ApplyTick(new Tick(100), Dt);

        Assert.Equal(["queue 20002", "run 20002", "queue 10003", "run 10003"], _runner.Calls);
    }

    [Fact]
    public void TheCommandNumberRisesByOneAndTheTickCountIsTheTick()
    {
        UserCmd a = Tick(), b = Tick();

        Assert.Equal((1, 100), (a.CommandNumber, a.TickCount));
        Assert.Equal((2, 101), (b.CommandNumber, b.TickCount));
    }

    [Fact]
    public void ACornerAheadIsAForwardMove()
    {
        Submit(Follow(new AiVector(500f, 0f, 0f)));

        UserCmd cmd = Tick();

        Assert.Equal(BotCommandApplier.FullMove, cmd.ForwardMove, 2);
        Assert.Equal(0f, cmd.SideMove, 2);
        Assert.True(cmd.IsPressed(InputButtons.Forward));
    }

    [Fact]
    public void ACornerToTheRightIsARightMove()
    {
        // Facing +x, the right is -y (AngleVectors' right at yaw 0).
        Submit(Follow(new AiVector(0f, -500f, 0f)));

        UserCmd cmd = Tick();

        Assert.Equal(0f, cmd.ForwardMove, 2);
        Assert.Equal(BotCommandApplier.FullMove, cmd.SideMove, 2);
        Assert.True(cmd.IsPressed(InputButtons.MoveRight));
    }

    [Fact]
    public void AReachedCornerGivesWayToTheNext()
    {
        var move = new MoveIntent(MoveKind.Follow, MoveSpeed.Run, 2, new AiVector(4f, 0f, 0f), new AiVector(-500f, 0f, 0f),
                                  default, default, default, false, 0);
        Submit(move);

        Assert.Equal(-BotCommandApplier.FullMove, Tick().ForwardMove, 2);
    }

    [Fact]
    public void SprintAndWalkHoldTheirKeys()
    {
        Submit(Follow(new AiVector(500f, 0f, 0f), MoveSpeed.Sprint));
        Assert.True(Tick().IsPressed(InputButtons.Speed));

        Submit(Follow(new AiVector(500f, 0f, 0f), MoveSpeed.Walk));
        UserCmd walk = Tick();
        Assert.True(walk.IsPressed(InputButtons.Walk));
        Assert.False(walk.IsPressed(InputButtons.Speed));
    }

    [Fact]
    public void JumpCrouchJumpAndCrouchPressTheirButtons()
    {
        Submit(Kind(MoveKind.Jump));
        Assert.Equal((int)InputButtons.Jump, Tick().Buttons);
        Submit(Kind(MoveKind.CrouchJump));
        Assert.Equal((int)(InputButtons.Jump | InputButtons.Duck), Tick().Buttons);
        Submit(Kind(MoveKind.Crouch, crouch: true));
        Assert.Equal((int)InputButtons.Duck, Tick().Buttons);
    }

    [Fact]
    public void AJumpWithACornerRunsTowardItPressingJump()
    {
        Submit(new MoveIntent(MoveKind.Jump, MoveSpeed.Run, 1, new AiVector(500f, 0f, 0f), default, default, default, default, false, 0));

        UserCmd cmd = Tick();

        Assert.True(cmd.IsPressed(InputButtons.Jump));
        Assert.Equal(BotCommandApplier.FullMove, cmd.ForwardMove, 2);
    }

    [Fact]
    public void ACrouchedFollowDucksWhileItRuns()
    {
        Submit(new MoveIntent(MoveKind.Follow, MoveSpeed.Run, 1, new AiVector(500f, 0f, 0f), default, default, default, default, true, 0));

        UserCmd cmd = Tick();

        Assert.True(cmd.IsPressed(InputButtons.Duck));
        Assert.Equal(BotCommandApplier.FullMove, cmd.ForwardMove, 2);
    }

    [Fact]
    public void ASprayHoldsFireAndATapLetsGoBetweenPresses()
    {
        Submit(MoveIntent.Hold, action: new ActionIntent(ActionKind.Fire, FireStyle.Spray, 0, 0));
        Assert.True(Tick().IsPressed(InputButtons.Attack));
        Assert.True(Tick().IsPressed(InputButtons.Attack));

        Submit(MoveIntent.Hold, action: new ActionIntent(ActionKind.Fire, FireStyle.Tap, 0, 0));
        bool[] taps = [Tick().IsPressed(InputButtons.Attack), Tick().IsPressed(InputButtons.Attack), Tick().IsPressed(InputButtons.Attack)];
        Assert.Equal([true, false, true], taps);
    }

    [Fact]
    public void AltFireReloadAndUsePressTheirButtons()
    {
        Submit(MoveIntent.Hold, action: new ActionIntent(ActionKind.AltFire, FireStyle.Tap, 0, 0));
        Assert.True(Tick().IsPressed(InputButtons.Attack2));
        Submit(MoveIntent.Hold, action: new ActionIntent(ActionKind.Reload, FireStyle.Tap, 0, 0));
        Assert.True(Tick().IsPressed(InputButtons.Reload));
        Submit(MoveIntent.Hold, action: new ActionIntent(ActionKind.Use, FireStyle.Tap, 0, 0));
        Assert.True(Tick().IsPressed(InputButtons.Use));
    }

    [Fact]
    public void AWeaponIsSelectedBySlotExactlyOnce()
    {
        Submit(MoveIntent.Hold, action: new ActionIntent(ActionKind.SelectWeapon, FireStyle.Tap, 2, OneShotSeq: 1));

        Assert.Equal(7, Tick().WeaponSelect);
        Assert.Equal(0, Tick().WeaponSelect);

        // The same one-shot again, in a newer intent: not applied twice.
        Submit(MoveIntent.Hold, action: new ActionIntent(ActionKind.SelectWeapon, FireStyle.Tap, 2, OneShotSeq: 1));
        Assert.Equal(0, Tick().WeaponSelect);
    }

    [Fact]
    public void AnEmptySlotSelectsNothing()
    {
        Submit(MoveIntent.Hold, action: new ActionIntent(ActionKind.SelectWeapon, FireStyle.Tap, 5, OneShotSeq: 1));

        Assert.Equal(0, Tick().WeaponSelect);
    }

    [Fact]
    public void AnIntentForAnotherLifeOfTheSlotIsRefused()
    {
        Assert.Equal(IntentRefusal.StaleSerial, Submit(MoveIntent.Hold, bot: new ActorId(3, 2)));
        Assert.Equal(1, _applier.Refused(IntentRefusal.StaleSerial));
    }

    [Fact]
    public void AnIntentForNoBotIsRefused()
        => Assert.Equal(IntentRefusal.UnknownBot, Submit(MoveIntent.Hold, bot: new ActorId(9, 1)));

    [Fact]
    public void AnIntentNoNewerThanTheLastIsRefused()
    {
        Assert.Equal(IntentRefusal.None, Submit(MoveIntent.Hold, seq: 5));
        Assert.Equal(IntentRefusal.OldSeq, Submit(MoveIntent.Hold, seq: 5));
        Assert.Equal(IntentRefusal.OldSeq, Submit(MoveIntent.Hold, seq: 4));
    }

    [Fact]
    public void AnExpiredIntentIsDroppedAndTheBotHolds()
    {
        Submit(Follow(new AiVector(500f, 0f, 0f)));
        Assert.NotEqual(0f, Tick().ForwardMove);

        _tick += IntentCommand.LifetimeTicks;
        UserCmd late = Tick();

        Assert.Equal((0f, 0f), (late.ForwardMove, late.SideMove));
        Assert.Equal(1, _applier.Refused(IntentRefusal.Expired));
    }

    [Fact]
    public void ADeadBotDropsItsIntentAndPressesFireToRespawn()
    {
        Submit(Follow(new AiVector(500f, 0f, 0f)));
        _self.Facts = _self.Facts with { Alive = false };

        var cmds = Enumerable.Range(0, BotCommandApplier.RespawnPressTicks).Select(_ => Tick()).ToArray();

        Assert.All(cmds, c => Assert.Equal(0f, c.ForwardMove));
        Assert.Single(cmds, c => c.IsPressed(InputButtons.Attack));
        Assert.Equal(1, _applier.Refused(IntentRefusal.Dead));
    }

    [Fact]
    public void AWatchingBotRefusesItsIntent()
    {
        Submit(Follow(new AiVector(500f, 0f, 0f)));
        _self.Facts = _self.Facts with { Observer = true };

        Assert.Equal(0f, Tick().ForwardMove);
        Assert.Equal(1, _applier.Refused(IntentRefusal.Observer));
    }

    [Fact]
    public void TheViewStartsWhereTheServerSaysItIs()
    {
        _self.Facts = _self.Facts with { View = new AiAngles(5f, 135f, 0f) };

        UserCmd cmd = Tick();

        Assert.Equal((5f, 135f), (cmd.Pitch, cmd.Yaw));
    }

    [Fact]
    public void NoCommandTurnsTheViewFasterThanTheEnvelopeWhateverTheCurve()
    {
        // A curve far outside the envelope: 5,000°/s and a 40 % overshoot,
        // on eight bots with their own draws. The applier's view is the
        // motion's, which clamps it to 900°/s, 13.5° a tick, in the flick and
        // in the correction.
        var behind = new LookIntent(LookKind.Point, new AiVector(-1000f, 1f, 64f), ActorId.None, AimPoint.Body, 0f, 0);

        // Each bot's view, as the server echoes it back, starts at 0.
        var views = new Dictionary<uint, AiAngles>();
        var echo = new EchoSelf(views);
        var applier = new BotCommandApplier(_runner, echo);
        for (int slot = 1; slot <= 8; slot++)
        {
            var bot = new ActorId(slot, 1);
            applier.Add(bot, new EntityHandle((uint)(0x0001_0000 * slot + slot)), new MotionParams(100f, 5000f, 40f, 30f), (ulong)slot);
            var intent = new IntentCommand(bot, new Tick(0), 1, new Tick(1000), MoveIntent.Hold, behind, ActionIntent.None, default);
            applier.Submit(in intent);
        }

        float turned = 0f;
        for (int t = 0; t < 120; t++)
        {
            int before = _runner.Queued.Count;
            applier.ApplyTick(new Tick(t), Dt);
            foreach ((EntityHandle player, UserCmd cmd) in _runner.Queued.Skip(before))
            {
                AiAngles last = views.GetValueOrDefault(player.Value);
                float step = MathF.Abs(HumanMotion.AngleDelta(last.Yaw, cmd.Yaw));
                Assert.True(step <= MotionEnvelope.MaxPeakDegPerSec * Dt + 1e-3f, $"tick {t}, {player.Value:x}: {step}°");
                turned += step;
                views[player.Value] = new AiAngles(cmd.Pitch, cmd.Yaw, 0f);
            }
        }

        // The stimulus reached the subject: every view went round.
        Assert.True(turned >= 8 * 179f, $"turned {turned}°");
    }

    /// <summary>The bot's own facts, with the view the server holds from its last command.</summary>
    private sealed class EchoSelf(Dictionary<uint, AiAngles> views) : IBotSelf
    {
        public BotSelfFacts Read(EntityHandle player)
            => new(true, false, default, new AiVector(0f, 0f, 64f), views.GetValueOrDefault(player.Value));

        public int WeaponSelectFor(EntityHandle player, int slot) => 0;
    }

    [Fact]
    public void AnglesToLooksDownAtAPointBelow()
    {
        AiAngles a = BotCommandApplier.AnglesTo(new AiVector(0f, 0f, 64f), new AiVector(100f, 100f, -36f));

        Assert.Equal(45f, a.Yaw, 3);
        Assert.True(a.Pitch > 0f, "pitch down is positive");
    }
}

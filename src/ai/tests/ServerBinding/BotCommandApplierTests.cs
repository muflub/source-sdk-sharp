using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp;
using SourceSharp.Server.Interop;
using Xunit;
using SourceSharp.Ai.Server;

namespace SourceSharp.Ai.Tests.ServerBinding;

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

    /// <summary>
    /// A held jump lets go now and then, and the duck stays: stock jumps only on
    /// the press, and a bot that held the key fell back from a rail and stood
    /// at it ducked with the key down, for good.
    /// </summary>
    [Fact]
    public void ACrouchJumpLetsGoOfTheJumpNowAndThenHoldingTheDuck()
    {
        var pressed = new List<bool>();
        for (int i = 0; i < BotCommandApplier.JumpCycle * 2; i++)
        {
            Submit(Kind(MoveKind.CrouchJump));
            UserCmd cmd = Tick();
            pressed.Add(cmd.IsPressed(InputButtons.Jump));
            Assert.True(cmd.IsPressed(InputButtons.Duck), $"the duck let go at {i}");
        }

        Assert.True(pressed[0], "the first tick presses");
        Assert.Contains(false, pressed);
        Assert.True(pressed[BotCommandApplier.JumpCycle], "pressed again after the let-go");
    }

    /// <summary>A landing near across on another floor is still ahead: a bot at a drop's edge stopped with it "reached" under its feet.</summary>
    [Fact]
    public void ACornerOnAnotherFloorIsNotReachedByBeingUnderIt()
    {
        Submit(Follow(new AiVector(10f, 0f, -128f)));
        Assert.True(Tick().ForwardMove != 0f || _runner.Queued[^1].Cmd.SideMove != 0f, "it stopped at the edge");
    }

    /// <summary>The last corner it was told of (a link's take-off) is walked right up to; one with another after it is cut, as a path is.</summary>
    [Fact]
    public void TheLastCornerIsWalkedRightUpToAndAnEarlierOneIsCut()
    {
        Submit(Follow(new AiVector(10f, 0f, 0f)));
        UserCmd last = Tick();
        Assert.True(last.ForwardMove != 0f || last.SideMove != 0f, "it stopped short of its last corner");
        Submit(new MoveIntent(MoveKind.Follow, MoveSpeed.Run, 2, new AiVector(10f, 0f, 0f), new AiVector(0f, 500f, 0f), default, default, default, false, 0));
        Tick();
        Assert.Equal(new AiVector(0f, 500f, 0f), _applier.LastOf(Bot).Corner);
    }

    /// <summary>Two dead bots press to respawn on ticks of their own: together, they came back in one tick and stood in each other on one spot.</summary>
    [Fact]
    public void DeadBotsPressToRespawnOnTicksOfTheirOwn()
    {
        var other = new ActorId(4, 2);
        var otherPlayer = new EntityHandle(0x0001_0004u);
        _applier.Add(other, otherPlayer, Normal, 43);
        _self.Facts = _self.Facts with { Alive = false };
        var pressedBy = new HashSet<EntityHandle>();
        var ticks = new Dictionary<int, EntityHandle>();
        for (int i = 0; i < BotCommandApplier.RespawnPressTicks * 2; i++)
        {
            int before = _runner.Queued.Count;
            _applier.ApplyTick(new Tick(_tick++), Dt);
            foreach ((EntityHandle p, UserCmd c) in _runner.Queued.Skip(before))
            {
                if (c.IsPressed(InputButtons.Attack))
                {
                    Assert.False(ticks.TryGetValue(_tick, out EntityHandle same) && same != p, "both pressed on one tick");
                    ticks[_tick] = p;
                    pressedBy.Add(p);
                }
            }
        }

        Assert.Equal(2, pressedBy.Count);
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

    /// <summary>A crouched strafe (§5.4): the fight's sidestep goes on, with the duck held.</summary>
    [Fact]
    public void ACrouchedStrafeDucksAndStillSteps()
    {
        Submit(new MoveIntent(MoveKind.Strafe, MoveSpeed.Run, 0, default, default, default, default, new AiVector(1f, 0f, 0f), true, 0));
        UserCmd cmd = Tick();
        Assert.True(cmd.IsPressed(InputButtons.Duck));
        Assert.Equal(BotCommandApplier.FullMove, cmd.ForwardMove, 2);
    }

    /// <summary>The control: an ordinary sidestep stands up.</summary>
    [Fact]
    public void AStrafeDoesNotDuckUnlessItIsAskedTo()
    {
        Submit(new MoveIntent(MoveKind.Strafe, MoveSpeed.Run, 0, default, default, default, default, new AiVector(1f, 0f, 0f), false, 0));
        Assert.False(Tick().IsPressed(InputButtons.Duck));
    }

    /// <summary>On a ladder a duck lets go of the rungs: the fight's crouch waits.</summary>
    [Fact]
    public void OnALadderTheFightsCrouchDoesNotDuck()
    {
        _self.Facts = _self.Facts with { OnLadder = true };
        Submit(Kind(MoveKind.Crouch, crouch: true));
        Assert.False(Tick().IsPressed(InputButtons.Duck));
        Submit(new MoveIntent(MoveKind.Strafe, MoveSpeed.Run, 0, default, default, default, default, new AiVector(1f, 0f, 0f), true, 0));
        Assert.False(Tick().IsPressed(InputButtons.Duck));
    }

    /// <summary>
    /// Crouching lowers the eye, and the aim is from the eye: after the settle the view goes
    /// through the same point, at the angle from the lower eye.
    /// </summary>
    [Fact]
    public void ACrouchedBotsAimStaysOnItsTarget()
    {
        var target = new AiVector(500f, 0f, 64f);
        for (int i = 0; i < 400; i++)
        {
            if (i % 6 == 0)
                SubmitAim(target, ActionIntent.None);
            if (i == 200)
                _self.Facts = _self.Facts with { Eye = new AiVector(0f, 0f, 28f) };
            TickFollowingTheView();
        }

        // Up by atan(36 / 500): about 4.1 degrees, and pitch runs negative upward.
        Assert.InRange(_self.Facts.View.Pitch, -6f, -2.5f);
    }

    // ---------------------------------------------------------------- recoil (2026-11-02)

    private static readonly MotionParams Hands = Normal with { RecoilControl = 0.5f, BurstSeconds = 0.3f, RecoilSettleDeg = 2f };

    private static readonly ActionIntent Burst = new(ActionKind.Fire, FireStyle.Burst, 0, 0);

    private static readonly ActionIntent Tap = new(ActionKind.Fire, FireStyle.Tap, 0, 0);

    /// <summary>Each tick's trigger over <paramref name="ticks"/> firing at a point, past the first flick.</summary>
    private bool[] Trigger(AiVector point, ActionIntent action, int ticks = 800, Action<int>? each = null)
    {
        for (int i = 0; i < 150; i++)
        {
            if (i % 6 == 0)
                SubmitAim(point, action);
            TickFollowingTheView();
        }

        var pressed = new bool[ticks];
        for (int i = 0; i < ticks; i++)
        {
            each?.Invoke(i);
            if (i % 6 == 0)
                SubmitAim(point, action);
            pressed[i] = TickFollowingTheView().IsPressed(InputButtons.Attack);
        }

        return pressed;
    }

    /// <summary>The whole runs of the trigger down (or up) in seconds, leaving out the first and the last, which were cut.</summary>
    private static List<float> Runs(bool[] pressed, bool down)
    {
        var runs = new List<float>();
        int at = 0;
        while (at < pressed.Length && pressed[at] == pressed[0])
            at++;
        while (at < pressed.Length)
        {
            int from = at;
            while (at < pressed.Length && pressed[at] == pressed[from])
                at++;
            if (at < pressed.Length && pressed[from] == down)
                runs.Add((at - from) * Dt);
        }

        return runs;
    }

    private static readonly AiVector AtRange = new(600f, 0f, 64f), AtClose = new(300f, 0f, 64f);

    /// <summary>A machine gun at range: down for a burst around its length, off between, and not all the same: not a hose.</summary>
    [Fact]
    public void AMachineGunFiresInBursts()
    {
        _applier.Add(Bot, Player, Hands, 42);
        bool[] pressed = Trigger(AtRange, Burst);
        List<float> bursts = Runs(pressed, down: true), gaps = Runs(pressed, down: false);
        Assert.True(bursts.Count >= 5, $"{bursts.Count} bursts");
        Assert.All(bursts, b => Assert.InRange(b, Hands.BurstSeconds * (1f - BotCommandApplier.BurstVaries) - Dt, Hands.BurstSeconds * (1f + BotCommandApplier.BurstVaries) + Dt));
        Assert.All(gaps, g => Assert.True(g >= BotCommandApplier.BurstGapMinSeconds - Dt, $"a gap of {g:F3} s"));
        Assert.True(bursts.Distinct().Count() > 1, "every burst the same");
    }

    /// <summary>Close, a body is wide, and a burst runs on.</summary>
    [Fact]
    public void CloseABurstRunsLonger()
    {
        _applier.Add(Bot, Player, Hands, 42);
        List<float> bursts = Runs(Trigger(AtClose, Burst), down: true);
        Assert.True(bursts.Count >= 3, $"{bursts.Count} bursts");
        Assert.All(bursts, b => Assert.True(b >= Hands.BurstSeconds * (1f - BotCommandApplier.BurstVaries) * BotCommandApplier.BurstCloseScale - Dt, $"a burst of {b:F3} s"));
    }

    /// <summary>The gap waits for the crosshair to come back, up to a limit.</summary>
    [Fact]
    public void ABurstsGapWaitsForTheKick()
    {
        _applier.Add(Bot, Player, Hands, 42);
        float settled = Runs(Trigger(AtRange, Burst), down: false).Max();
        _applier.Add(Bot, Player, Hands, 42);
        _self.Facts = _self.Facts with { Punch = new AiAngles(-6f, 0f, 0f) };
        List<float> kicked = Runs(Trigger(AtRange, Burst), down: false);
        Assert.True(settled < BotCommandApplier.BurstGapMaxSeconds / 2f, $"settled {settled:F3}");
        Assert.True(kicked.Count >= 3, $"{kicked.Count} gaps: it stopped firing");
        Assert.All(kicked, g => Assert.InRange(g, BotCommandApplier.BurstGapMaxSeconds - Dt - 0.001f, BotCommandApplier.BurstGapMaxSeconds + Dt));
    }

    /// <summary>A bot with no burst of its own holds the key, as a scripted one does: the burst is the hands', not the weapon's.</summary>
    [Fact]
    public void WithNoBurstLengthTheTriggerIsHeld()
    {
        Assert.All(Trigger(AtRange, Burst), Assert.True);
    }

    /// <summary>A semi-automatic's next press waits while the kick is up, then fires again: no shot over the top of a .357's climb.</summary>
    [Fact]
    public void ASemiAutomaticWaitsForTheKick()
    {
        _applier.Add(Bot, Player, Hands, 42);
        // Up at 10 degrees for 100 ticks, then down at a quarter of a degree a tick: under the settle at tick 132.
        bool[] kicked = Trigger(AtRange, Tap, 260, i => _self.Facts = _self.Facts with { Punch = new AiAngles(-MathF.Max(0f, 10f - MathF.Max(0, i - 100) * 0.25f), 0f, 0f) });
        const int Settled = 132;
        Assert.DoesNotContain(true, kicked[..Settled]);
        // Then (10 - 2) x 0.05: about 0.4 s past it, by the peak, not by where it is now.
        int pause = (int)((10f - Hands.RecoilSettleDeg) * BotCommandApplier.TapPausePerDeg / Dt);
        Assert.DoesNotContain(true, kicked[Settled..(Settled + pause - 1)]);
        Assert.Contains(true, kicked[(Settled + pause)..]);
    }

    /// <summary>The kick at the end of the last fight is no reason to wait in this one: the pause runs on the clock, firing or not.</summary>
    [Fact]
    public void AnOldKickDoesNotHoldTheNextFight()
    {
        _applier.Add(Bot, Player, Hands, 42);
        // A shot's kick seen while firing; then the fight ends, the kick falls, and time passes.
        Trigger(AtRange, Tap, 20, _ => _self.Facts = _self.Facts with { Punch = new AiAngles(-10f, 0f, 0f) });
        for (int i = 0; i < 200; i++)
        {
            _self.Facts = _self.Facts with { Punch = new AiAngles(-MathF.Max(0f, 10f - i * 0.25f), 0f, 0f) };
            if (i % 6 == 0)
                SubmitAim(AtRange, ActionIntent.None);
            TickFollowingTheView();
        }

        SubmitAim(AtRange, Tap);
        Assert.True(TickFollowingTheView().IsPressed(InputButtons.Attack), "it waited for a kick long gone");
    }

    /// <summary>A single shot's kick is waited for, not pulled against: it comes back by itself.</summary>
    [Fact]
    public void ASemiAutomaticsKickIsNotPulledAgainst()
    {
        _applier.Add(Bot, Player, Hands, 42);
        Trigger(AtRange, Tap, 300, _ => _self.Facts = _self.Facts with { Punch = new AiAngles(-6f, 0f, 0f) });
        Assert.InRange(_self.Facts.View.Pitch, -0.5f, 0.5f);
    }

    /// <summary>A small kick asks no pause: the pistol's half a degree fires as it did.</summary>
    [Fact]
    public void ASmallKickDoesNotHoldASemiAutomatic()
    {
        _applier.Add(Bot, Player, Hands, 42);
        bool[] small = Trigger(AtRange, Tap, 40, i => _self.Facts = _self.Facts with { Punch = new AiAngles(-0.5f, 0f, 0f) });
        Assert.Equal(20, small.Count(p => p));
    }

    /// <summary>The view pulls down against a climb by its share while it fires, and the rest of the climb stays: no perfect hand.</summary>
    [Fact]
    public void TheViewPullsDownAgainstAClimbByItsShare()
    {
        _applier.Add(Bot, Player, Hands, 42);
        Trigger(AtRange, Burst, 300, _ => _self.Facts = _self.Facts with { Punch = new AiAngles(-6f, 2f, 0f) });
        // Pitch runs positive down: half of -6 undone is 3 down; half of +2 of yaw is 1 the other way.
        Assert.InRange(_self.Facts.View.Pitch, 2.5f, 3.5f);
        Assert.InRange(HumanMotion.AngleDelta(0f, _self.Facts.View.Yaw), -1.5f, -0.5f);
    }

    /// <summary>With nothing to fire at there is nothing to pull against: the kick does not move a bot's view.</summary>
    [Fact]
    public void WithoutFiringTheViewDoesNotPull()
    {
        _applier.Add(Bot, Player, Hands, 42);
        Trigger(AtRange, ActionIntent.None, 300, _ => _self.Facts = _self.Facts with { Punch = new AiAngles(-6f, 0f, 0f) });
        Assert.InRange(_self.Facts.View.Pitch, -0.5f, 0.5f);
    }

    /// <summary>However big the climb, the pull is one more thing the view does at human speed.</summary>
    [Fact]
    public void ThePullStaysInsideTheTurnLimit()
    {
        _applier.Add(Bot, Player, Hands, 42);
        for (int i = 0; i < 150; i++)
        {
            if (i % 6 == 0)
                SubmitAim(AtRange, Burst);
            TickFollowingTheView();
        }

        _self.Facts = _self.Facts with { Punch = new AiAngles(-60f, 0f, 0f) };
        float largest = 0f;
        for (int i = 0; i < 60; i++)
        {
            float was = _self.Facts.View.Pitch;
            TickFollowingTheView();
            largest = MathF.Max(largest, MathF.Abs(_self.Facts.View.Pitch - was));
        }

        Assert.True(largest > 0f, "it did not pull at all");
        Assert.True(largest <= Normal.TurnPeakDegPerSec * Dt + 0.01f, $"{largest} a tick");
    }

    /// <summary>The peak a bot's crosshair showed is told: the live read that a bot's shots kick at all.</summary>
    [Fact]
    public void ThePeakOfAKickIsNoted()
    {
        Assert.Equal(0f, _applier.PunchPeakDeg);
        _self.Facts = _self.Facts with { Punch = new AiAngles(-3f, 4f, 0f) };
        Tick();
        Assert.Equal(5f, _applier.PunchPeakDeg, 3);
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

    /// <summary>
    /// A bot's brain made again (a level in rooms installing another room, plan_bots_teams_tf2_part_g R.2 item 6; a new
    /// level) numbers its one-shots from 1 again, under the old brain's last: each is a one-shot the applier has not
    /// taken. Live on jump_academy2_rc9a, a Soldier carried into easywalls stood at a blast link's start, its tree began
    /// the link (the doing said "rocket jump") and the ability never reached the program (ai.jump.begun 0, the
    /// applier's programs 0): the one-shot's number was under the last one the old brain's had reached.
    /// </summary>
    [Fact]
    public void ABrainMadeAgainHasItsOneShotsTakenThoughNumberedFromOneAgain()
    {
        Submit(MoveIntent.Hold, action: new ActionIntent(ActionKind.SelectWeapon, FireStyle.Tap, 2, OneShotSeq: 40));
        Assert.Equal(7, Tick().WeaponSelect);

        // The new brain's first one-shot: number 1.
        Submit(MoveIntent.Hold, action: new ActionIntent(ActionKind.SelectWeapon, FireStyle.Tap, 2, OneShotSeq: 1));
        Assert.Equal(7, Tick().WeaponSelect);

        // Its own repeat in a newer intent is still not taken twice.
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

    /// <summary>
    /// A tick as in the game, where the server's view is the last command's;
    /// <see cref="Self"/> otherwise stays at yaw 0 and the applier would
    /// take every turn for a server snap.
    /// </summary>
    private UserCmd TickFollowingTheView()
    {
        UserCmd cmd = Tick();
        _self.Facts = _self.Facts with { View = new AiAngles(cmd.Pitch, cmd.Yaw, 0f) };
        return cmd;
    }

    private static readonly ActorId Foe = new(5, 0x4000_0001);

    private void SubmitAim(AiVector point, ActionIntent action)
        => _applier.Submit(new IntentCommand(Bot, new Tick(_tick), ++_seq, new Tick(_tick + IntentCommand.LifetimeTicks), MoveIntent.Hold,
                                             new LookIntent(LookKind.Actor, point, Foe, AimPoint.Body, 0f, 0), action, default));

    /// <summary>The second trigger is IN_ATTACK2 in the usercmd, a person's press, and only with the view on its point (§5.4).</summary>
    [Fact]
    public void AnAltFireIsAttack2OnceTheViewIsThere()
    {
        var point = new AiVector(500f, 200f, 64f);
        var alt = new ActionIntent(ActionKind.AltFire, FireStyle.Tap, 0, 0);
        bool early = false, pressed = false;
        for (int i = 0; i < 200 && !pressed; i++)
        {
            SubmitAim(point, alt);
            UserCmd cmd = TickFollowingTheView();
            AiAngles at = BotCommandApplier.AnglesTo(new AiVector(0f, 0f, 64f), point);
            float off = MathF.Sqrt(MathF.Pow(at.Pitch - cmd.Pitch, 2f) + MathF.Pow(HumanMotion.AngleDelta(cmd.Yaw, at.Yaw), 2f));
            bool attack2 = (cmd.Buttons & (int)InputButtons.Attack2) != 0;
            early |= attack2 && off > BotCommandApplier.AltFireOnAimDeg + 0.5f;
            pressed = attack2;
            Assert.Equal(0, cmd.Buttons & (int)InputButtons.Attack);
        }

        Assert.True(pressed, "the view got there and nothing was pressed");
        Assert.False(early, "pressed with the view off its point");
    }

    private static readonly ActionIntent SpinHeld = new(ActionKind.AltFire, FireStyle.Hold, 0, 0), FireHeld = new(ActionKind.Fire, FireStyle.Spray, 0, 0),
                                         BothBarrels = new(ActionKind.AltFire, FireStyle.Spray, 0, 0);
    private static readonly AiVector OffToTheSide = new(400f, 450f, 64f);
    private static readonly LookIntent AtTheSide = new(LookKind.Actor, OffToTheSide, Foe, AimPoint.Body, 0f, 0);

    /// <summary>The commands of <paramref name="ticks"/> ticks at <paramref name="action"/> with <paramref name="look"/>, the view following.</summary>
    private List<UserCmd> Hold(int ticks, ActionIntent action, LookIntent? look = null)
    {
        var cmds = new List<UserCmd>();
        for (int i = 0; i < ticks; i++)
        {
            Submit(MoveIntent.Hold, look, action);
            cmds.Add(TickFollowingTheView());
        }

        return cmds;
    }

    private static bool Down(UserCmd cmd, InputButtons button) => (cmd.Buttons & (int)button) != 0;

    /// <summary>A fresh bot, its view straight ahead, for a second arm of a fact.</summary>
    private void Fresh()
    {
        _applier.Add(Bot, Player, Normal, 42);
        _self.Facts = _self.Facts with { View = default, Alive = true };
    }

    /// <summary>
    /// The Fire's wait at <paramref name="look"/> after <paramref name="before"/> ticks of <paramref name="lead"/>: the commands
    /// before the first trigger went, and whether it went at all within <paramref name="ticks"/> (the command after the wait).
    /// </summary>
    private (List<UserCmd> Wait, UserCmd? Went) Waited(ActionIntent lead, int before, LookIntent look, int ticks = 60)
    {
        Hold(before, lead);
        List<UserCmd> all = Hold(ticks, FireHeld, look);
        int n = all.TakeWhile(c => !Down(c, InputButtons.Attack)).Count();
        return ([.. all.Take(n)], n < all.Count ? all[n] : null);
    }

    /// <summary>
    /// A held spin (Hold, no shot) stays down while the first trigger waits for the eye: a new target 600 u off and 48° round,
    /// so the hands wait out the reaction and the flick. The wait ends (the Fire goes: Attack down and the spin's Attack2 up),
    /// and it is no shorter than the same wait from cold, where nothing at all is down: the held spin buys no faster shot.
    /// </summary>
    [Fact]
    public void AHeldSpinStaysDownWhileTheFirstWaitsForTheEye()
    {
        (List<UserCmd> cold, UserCmd? coldWent) = Waited(ActionIntent.None, 1, AtTheSide);
        Assert.True(cold.Count >= 3, $"the hands waited {cold.Count} commands from cold");
        Assert.NotNull(coldWent);
        Assert.DoesNotContain(cold, c => Down(c, InputButtons.Attack2));

        Fresh();
        (List<UserCmd> spun, UserCmd? went) = Waited(SpinHeld, 5, AtTheSide);
        Assert.NotNull(went);
        Assert.True((Down(went!.Value, InputButtons.Attack), Down(went.Value, InputButtons.Attack2)) == (true, false), "the Fire went and the spin's key let go");
        Assert.All(spun, c => Assert.True(Down(c, InputButtons.Attack2), "the spin was let go while the eye got there"));
        Assert.True(spun.Count >= cold.Count - 1, $"the spun wait {spun.Count} beat the cold {cold.Count}");
    }

    /// <summary>
    /// Close (300 u, inside the distance where the hands fire before the flick ends) and 60° round: past the reaction the first
    /// trigger still waits for the view to come within a body, and the held spin stays down through it; the wait ends.
    /// </summary>
    [Fact]
    public void AHeldSpinStaysDownThroughACloseFlick()
    {
        (List<UserCmd> spun, UserCmd? went) = Waited(SpinHeld, 5, new LookIntent(LookKind.Actor, new AiVector(150f, 260f, 64f), Foe, AimPoint.Body, 0f, 0));
        Assert.True(spun.Count >= 3, $"the hands waited {spun.Count} commands");
        Assert.NotNull(went);
        Assert.All(spun, c => Assert.True(Down(c, InputButtons.Attack2), "the spin was let go in the flick"));
    }

    /// <summary>A spin (Hold) at an actor is pressed whatever the view, from cold and when its look turns to a new target: it is no shot.</summary>
    [Fact]
    public void ASpinIsPressedWhateverTheView()
    {
        Assert.True(Down(Hold(1, SpinHeld, AtTheSide)[0], InputButtons.Attack2));
        Fresh();
        Hold(5, SpinHeld);
        Assert.All(Hold(10, SpinHeld, AtTheSide), c => Assert.True(Down(c, InputButtons.Attack2)));
    }

    /// <summary>
    /// HL2MP's both barrels (a Spray AltFire) are a shot: held, and its look turned to a new target, they wait for the eye
    /// again (the reaction) as before the spin's rule; and a Fire after them keeps nothing down.
    /// </summary>
    [Fact]
    public void BothBarrelsStillWaitForTheEye()
    {
        Assert.All(Hold(5, BothBarrels), c => Assert.True(Down(c, InputButtons.Attack2), "both barrels at the point ahead"));
        List<UserCmd> turned = Hold(3, BothBarrels, AtTheSide);
        Assert.DoesNotContain(turned, c => Down(c, InputButtons.Attack2));

        Fresh();
        (List<UserCmd> wait, _) = Waited(BothBarrels, 5, AtTheSide);
        Assert.True(wait.Count >= 3, $"the hands waited {wait.Count} commands");
        Assert.DoesNotContain(wait, c => Down(c, InputButtons.Attack2));
    }

    /// <summary>
    /// A tapped second trigger (a frag, a ball) is no spin: after it, whether its last command had the key down or up, the
    /// first trigger's wait holds nothing down.
    /// </summary>
    [Fact]
    public void ATappedSecondTriggerIsNotKeptDown()
    {
        var tap = new ActionIntent(ActionKind.AltFire, FireStyle.Tap, 0, 0);
        foreach (int taps in new[] { 5, 6 })
        {
            Fresh();
            Assert.True(Down(Hold(taps, tap)[^1], InputButtons.Attack2) == (taps % 2 == 1), "the tap's parity");
            List<UserCmd> wait = Hold(3, FireHeld, AtTheSide);
            Assert.DoesNotContain(wait, c => Down(c, InputButtons.Attack2) || Down(c, InputButtons.Attack));
        }
    }

    /// <summary>The spin let go on purpose (an intent with no second trigger) is not brought back by the first trigger's wait.</summary>
    [Fact]
    public void ASpinLetGoIsNotKeptDown()
    {
        Hold(5, SpinHeld);
        List<UserCmd> letGo = Hold(2, ActionIntent.None);
        List<UserCmd> wait = Hold(3, FireHeld, AtTheSide);
        Assert.DoesNotContain([.. letGo, .. wait], c => Down(c, InputButtons.Attack2) || Down(c, InputButtons.Attack));
    }

    /// <summary>
    /// A spin held at death does not come back with the next life (which may be another class: a Pyro's Attack2 airblasts): dead
    /// for a command, then alive with a Fire at an actor as its first intent, the wait holds nothing down.
    /// </summary>
    [Fact]
    public void ASpinHeldAtDeathIsNotHeldInTheNextLife()
    {
        Hold(5, SpinHeld);
        _self.Facts = _self.Facts with { Alive = false };
        Tick();
        _self.Facts = _self.Facts with { Alive = true };
        List<UserCmd> wait = Hold(3, FireHeld, AtTheSide);
        Assert.DoesNotContain(wait, c => Down(c, InputButtons.Attack2) || Down(c, InputButtons.Attack));
    }

    /// <summary>A spin held before a level change does not come back on the new player's first wait.</summary>
    [Fact]
    public void ASpinHeldBeforeALevelChangeIsNotHeldAfterIt()
    {
        Hold(5, SpinHeld);
        _applier.Rebind(Bot.Slot, new EntityHandle(0x0002_0003u));
        List<UserCmd> wait = Hold(3, FireHeld, AtTheSide);
        Assert.DoesNotContain(wait, c => Down(c, InputButtons.Attack2) || Down(c, InputButtons.Attack));
    }

    // ---- round 2's S9: a Fire held at a teammate on purpose (a Medic's beam) is kept down through a one-shot

    /// <summary>The pop: a one-shot second trigger, as a Medic's Über goes.</summary>
    private static readonly ActionIntent Pop = new(ActionKind.AltFire, FireStyle.Tap, 0, OneShotSeq: 1);

    /// <summary>Holds Fire at the point ahead for <paramref name="ticks"/> decides, at a teammate on purpose or not.</summary>
    private void FireAhead(int ticks, bool atFriend)
    {
        for (int i = 0; i < ticks; i++)
        {
            var from = new Tick(_tick);
            _applier.Submit(new IntentCommand(Bot, from, ++_seq, from + IntentCommand.LifetimeTicks, MoveIntent.Hold, Ahead, FireHeld, default,
                                              HoldsAtFriend: atFriend));
            TickFollowingTheView();
        }
    }

    /// <summary>
    /// One decide carrying <paramref name="once"/>, and the commands of the <paramref name="ticks"/> ticks it lives;
    /// <paramref name="asks"/>: the decide still asks to hold at the friend (<see cref="IntentCommand.HoldsAtFriend"/>).
    /// </summary>
    private List<UserCmd> OneShotDecide(ActionIntent once, int ticks = 8, bool asks = false)
    {
        var from = new Tick(_tick);
        _applier.Submit(new IntentCommand(Bot, from, ++_seq, from + IntentCommand.LifetimeTicks, MoveIntent.Hold, Ahead, once, default, HoldsAtFriend: asks));
        return [.. Enumerable.Range(0, ticks).Select(_ => TickFollowingTheView())];
    }

    /// <summary>A beam held at a teammate stays down through a decide carrying the pop: every command of it has Attack, and the pop goes.</summary>
    [Fact]
    public void ABeamHeldAtATeammateStaysDownThroughAOneShot()
    {
        FireAhead(5, atFriend: true);
        List<UserCmd> popped = OneShotDecide(Pop, asks: true);
        Assert.All(popped, c => Assert.True(Down(c, InputButtons.Attack), "the beam was let go for the one-shot"));
        Assert.Single(popped, c => Down(c, InputButtons.Attack2));
    }

    /// <summary>
    /// A Fire at a teammate still waiting for the eye (a new look 48° round, inside the reaction) held nothing down, so the
    /// one-shot decide after it presses no Attack (the review's S9-1): before, the flag followed the intent and the pop's
    /// decide pressed Attack for the first time, off aim. The control: the hands did wait (no Attack in the friend-fire).
    /// </summary>
    [Fact]
    public void AFriendFireStillWaitingForTheEyeHoldsNothingThroughAOneShot()
    {
        var waiting = new List<UserCmd>();
        for (int i = 0; i < 3; i++)
        {
            var from = new Tick(_tick);
            _applier.Submit(new IntentCommand(Bot, from, ++_seq, from + IntentCommand.LifetimeTicks, MoveIntent.Hold, AtTheSide, FireHeld, default,
                                              HoldsAtFriend: true));
            waiting.Add(TickFollowingTheView());
        }

        Assert.DoesNotContain(waiting, c => Down(c, InputButtons.Attack));
        Assert.DoesNotContain(OneShotDecide(Pop, ticks: 2, asks: true), c => Down(c, InputButtons.Attack));
    }

    /// <summary>A Fire at an enemy is let go through a one-shot, as before S9: no command of the one-shot's decide has Attack.</summary>
    [Fact]
    public void AFireAtAnEnemyIsLetGoThroughAOneShot()
    {
        FireAhead(5, atFriend: false);
        Assert.True(Down(_runner.Queued[^1].Cmd, InputButtons.Attack), "the control: the Fire was down before the one-shot");
        Assert.DoesNotContain(OneShotDecide(Pop), c => Down(c, InputButtons.Attack));
    }

    /// <summary>
    /// A one-shot decide that no longer asks to hold at the friend (the beam let go to retarget, a weapon taken out) lets the
    /// beam go and ends the hold (the review's S9-2): no command of it, nor of a later one-shot decide that asks, has Attack.
    /// The control: the same one-shot decide asking keeps it (<see cref="ABeamHeldAtATeammateStaysDownThroughAOneShot"/>).
    /// </summary>
    [Fact]
    public void AOneShotDecideThatStopsAskingLetsTheBeamGo()
    {
        FireAhead(5, atFriend: true);
        Assert.DoesNotContain(OneShotDecide(Pop), c => Down(c, InputButtons.Attack));
        Assert.DoesNotContain(OneShotDecide(Pop with { OneShotSeq = 2 }, asks: true), c => Down(c, InputButtons.Attack));
    }

    /// <summary>
    /// The hold needs both halves (S9-2's rule): a one-shot decide that asks to hold at a friend after a decide whose Fire was
    /// at an enemy (no friend held) presses no Attack. The control: the Fire was down before it.
    /// </summary>
    [Fact]
    public void AnAskingOneShotAfterAFireAtAnEnemyHoldsNothing()
    {
        FireAhead(5, atFriend: false);
        Assert.True(Down(_runner.Queued[^1].Cmd, InputButtons.Attack), "the control: the Fire was down before the one-shot");
        Assert.DoesNotContain(OneShotDecide(Pop, asks: true), c => Down(c, InputButtons.Attack));
    }

    /// <summary>A beam held at death is not held through the next life's first one-shot.</summary>
    [Fact]
    public void ABeamHeldAtDeathIsNotHeldThroughTheNextLifesOneShot()
    {
        FireAhead(5, atFriend: true);
        _self.Facts = _self.Facts with { Alive = false };
        Tick();
        _self.Facts = _self.Facts with { Alive = true };
        Assert.DoesNotContain(OneShotDecide(Pop, asks: true), c => Down(c, InputButtons.Attack));
    }

    /// <summary>A beam held before a level change is not held through the new player's first one-shot.</summary>
    [Fact]
    public void ABeamHeldBeforeALevelChangeIsNotHeldThroughAOneShotAfterIt()
    {
        FireAhead(5, atFriend: true);
        _applier.Rebind(Bot.Slot, new EntityHandle(0x0002_0003u));
        Assert.DoesNotContain(OneShotDecide(Pop, asks: true), c => Down(c, InputButtons.Attack));
    }

    /// <summary>A decide that holds no Fire lets the beam go for good: the one-shot after it holds nothing.</summary>
    [Fact]
    public void ADecideWithoutFireEndsTheBeamsHold()
    {
        FireAhead(5, atFriend: true);
        Hold(2, ActionIntent.None);
        Assert.DoesNotContain(OneShotDecide(Pop, asks: true), c => Down(c, InputButtons.Attack));
    }

    /// <summary>
    /// The aim's point moves at every decide while the target stays the same:
    /// one stimulus (§3.4.1). As a new stimulus each move restarted the
    /// reaction, and the view never moved for as long as the fight lasted.
    /// </summary>
    [Fact]
    public void AnActorWhoseAimPointMovesIsOneStimulusAndTheViewGetsThere()
    {
        var rng = new Random(5);
        for (int i = 0; i < 200; i++)
        {
            if (i % 6 == 0)
                SubmitAim(new AiVector(rng.NextSingle() * 80f - 40f, 500f + rng.NextSingle() * 80f - 40f, 64f), ActionIntent.None);
            TickFollowingTheView();
        }

        Assert.InRange(_self.Facts.View.Yaw, 80f, 100f);
    }

    /// <summary>The control: another actor is a new stimulus, and the view waits its reaction again.</summary>
    [Fact]
    public void AnotherActorIsANewStimulus()
    {
        for (int i = 0; i < 150; i++)
        {
            if (i % 6 == 0)
                SubmitAim(new AiVector(0f, 500f, 64f), ActionIntent.None);
            TickFollowingTheView();
        }

        float yaw = _self.Facts.View.Yaw;
        var other = new LookIntent(LookKind.Actor, new AiVector(-500f, 0f, 64f), new ActorId(6, 0x4000_0001), AimPoint.Body, 0f, 0);
        _applier.Submit(new IntentCommand(Bot, new Tick(_tick), ++_seq, new Tick(_tick + IntentCommand.LifetimeTicks), MoveIntent.Hold, other, ActionIntent.None, default));
        for (int i = 0; i < (int)(Normal.ReactionMs / 1000f / Dt) - 1; i++)
            TickFollowingTheView();
        Assert.Equal(yaw, _self.Facts.View.Yaw);
    }

    /// <summary>Someone it was chasing, seen again, is a new stimulus: it waits its reaction and flicks.</summary>
    [Fact]
    public void SeeingAgainSomeoneItChasedIsANewStimulus()
    {
        for (int i = 0; i < 150; i++)
        {
            if (i % 6 == 0)
                _applier.Submit(new IntentCommand(Bot, new Tick(_tick), ++_seq, new Tick(_tick + IntentCommand.LifetimeTicks), MoveIntent.Hold,
                                                  new LookIntent(LookKind.Point, new AiVector(500f, 0f, 64f), Foe, AimPoint.Body, 0f, 0), ActionIntent.None, default));
            TickFollowingTheView();
        }

        SubmitAim(new AiVector(0f, 500f, 64f), ActionIntent.None);
        float yaw = TickFollowingTheView().Yaw;
        Assert.Equal(AimPhase.Reacting, _applier.StateOf(Bot.Slot)!.Motion.Phase);
        Assert.InRange(yaw, -1f, 1f);
    }

    /// <summary>
    /// Walking almost where it looks, the side move is none at all: the view's
    /// small lag either way made a side move that flipped sign every few ticks.
    /// </summary>
    [Theory]
    [InlineData(3f)]
    [InlineData(-3f)]
    public void ASliverOfASidestepIsNone(float degreesOff)
    {
        float rad = degreesOff * (MathF.PI / 180f);
        var corner = new AiVector(MathF.Cos(rad) * 500f, MathF.Sin(rad) * 500f, 0f);
        _applier.Submit(new IntentCommand(Bot, new Tick(_tick), ++_seq, new Tick(_tick + IntentCommand.LifetimeTicks), Follow(corner), Ahead, ActionIntent.None, default));
        UserCmd c = Tick();
        Assert.Equal(0f, c.SideMove);
        Assert.True(c.ForwardMove > 400f, $"forward {c.ForwardMove}");
    }

    /// <summary>The control: a real sidestep is sent.</summary>
    [Fact]
    public void ARealSidestepIsSent()
    {
        var corner = new AiVector(MathF.Cos(0.6f) * 500f, -MathF.Sin(0.6f) * 500f, 0f);
        _applier.Submit(new IntentCommand(Bot, new Tick(_tick), ++_seq, new Tick(_tick + IntentCommand.LifetimeTicks), Follow(corner), Ahead, ActionIntent.None, default));
        Assert.True(Tick().SideMove > 100f);
    }

    private static ActionIntent HoldFire => new(ActionKind.Fire, FireStyle.Spray, 0, 0);

    [Fact]
    public void TheHandsWaitForTheReactionAndTheFlick()
    {
        var fired = new List<int>();
        int reaction = (int)(Normal.ReactionMs / 1000f / Dt);
        for (int i = 0; i < 200; i++)
        {
            if (i % 6 == 0)
                SubmitAim(new AiVector(0f, 500f, 64f), HoldFire);
            if ((TickFollowingTheView().Buttons & (int)InputButtons.Attack) != 0)
                fired.Add(i);
        }

        Assert.NotEmpty(fired);
        Assert.True(fired[0] > reaction, $"first press at tick {fired[0]}, the reaction lasts {reaction}");
        Assert.InRange(MathF.Abs(HumanMotion.AngleDelta(_self.Facts.View.Yaw, 90f)), 0f, 25f);
    }

    /// <summary>A bot holding a place sways its view across it: each decide's new point is no new stimulus, or the view never moves (it stood frozen, 166 s of idle in one run).</summary>
    [Fact]
    public void AHeldViewFollowsItsSway()
    {
        float first = _self.Facts.View.Yaw, most = 0f;
        for (int i = 0; i < 200; i++)
        {
            if (i % 6 == 0)
            {
                float yaw = 90f + 20f * MathF.Sin(i * 0.015f * 2f * MathF.PI / 5f);
                var point = new AiVector(MathF.Cos(yaw * MathF.PI / 180f) * 300f, MathF.Sin(yaw * MathF.PI / 180f) * 300f, 64f);
                _applier.Submit(new IntentCommand(Bot, new Tick(_tick), ++_seq, new Tick(_tick + IntentCommand.LifetimeTicks), MoveIntent.Hold,
                                                  new LookIntent(LookKind.Watch, point, ActorId.None, AimPoint.Body, 0f, 1), ActionIntent.None, default));
            }

            TickFollowingTheView();
            most = MathF.Max(most, MathF.Abs(HumanMotion.AngleDelta(first, _self.Facts.View.Yaw)));
        }

        Assert.True(most > 20f, $"the view moved {most} degrees");
    }

    /// <summary>A prefire looks at a corner, and the hands wait for the eye there as they do for a person (§5.4).</summary>
    [Fact]
    public void AtACornerTheHandsWaitForTheEye()
    {
        var fired = new List<int>();
        int reaction = (int)(Normal.ReactionMs / 1000f / Dt);
        for (int i = 0; i < 200; i++)
        {
            if (i % 6 == 0)
                _applier.Submit(new IntentCommand(Bot, new Tick(_tick), ++_seq, new Tick(_tick + IntentCommand.LifetimeTicks), MoveIntent.Hold,
                                                  new LookIntent(LookKind.CornerCheck, new AiVector(0f, 500f, 64f), Foe, AimPoint.Body, 0f, 0), HoldFire, default));
            if ((TickFollowingTheView().Buttons & (int)InputButtons.Attack) != 0)
                fired.Add(i);
        }

        Assert.NotEmpty(fired);
        Assert.True(fired[0] > reaction, $"first press at tick {fired[0]}, the reaction lasts {reaction}");
    }

    private int FirstPressAiming(AiVector point)
    {
        for (int i = 0; i < 300; i++)
        {
            if (i % 6 == 0)
                SubmitAim(point, HoldFire);
            if ((TickFollowingTheView().Buttons & (int)InputButtons.Attack) != 0)
                return i;
        }

        return int.MaxValue;
    }

    /// <summary>
    /// Close, a body is wide: the hands fire once the view is within it, not at a
    /// flick's 70 %. The same 40 degrees' flick at 1,000 u presses later, and
    /// neither presses in the reaction.
    /// </summary>
    [Fact]
    public void CloseTheHandsDoNotWaitForTheFlicksEnd()
    {
        float rad = 40f * (MathF.PI / 180f);
        int close = FirstPressAiming(new AiVector(MathF.Cos(rad) * 150f, MathF.Sin(rad) * 150f, 64f));
        var other = new BotCommandApplierTests();
        int far = other.FirstPressAiming(new AiVector(MathF.Cos(rad) * 1000f, MathF.Sin(rad) * 1000f, 64f));
        int reaction = (int)(Normal.ReactionMs / 1000f / Dt);
        Assert.True(close > reaction, $"close pressed at {close}, the reaction lasts {reaction}");
        Assert.True(close < far, $"close pressed at {close}, far at {far}");
    }

    /// <summary>The control: a scripted look at a point fires when told, whatever the view is doing.</summary>
    [Fact]
    public void AScriptedLookFiresAtOnce()
    {
        var look = new LookIntent(LookKind.Point, new AiVector(0f, 500f, 64f), ActorId.None, AimPoint.Body, 0f, 0);
        _applier.Submit(new IntentCommand(Bot, new Tick(_tick), ++_seq, new Tick(_tick + IntentCommand.LifetimeTicks), MoveIntent.Hold, look, HoldFire, default));
        Assert.NotEqual(0, TickFollowingTheView().Buttons & (int)InputButtons.Attack);
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

    private static int Rises(IEnumerable<UserCmd> cmds, int key)
    {
        int rises = 0, was = 0;
        foreach (UserCmd c in cmds)
        {
            int now = c.Buttons & key;
            rises += now != 0 && was == 0 ? 1 : 0;
            was = now;
        }

        return rises;
    }

    private IntentCommand Go(MoveKind kind, ActionIntent action)
        => new(Bot, new Tick(_tick), ++_seq, new Tick(_tick + IntentCommand.LifetimeTicks),
               new MoveIntent(kind, MoveSpeed.Run, 1, new AiVector(4000f, 0f, 0f), default, default, default, default, false, 0),
               LookIntent.AlongPath, action, default);

    private UserCmd LateTick()
    {
        _applier.Late(Bot);
        return Tick();
    }

    /// <summary>
    /// A late drive step: a new usercmd with its own number and tick, as stock
    /// has a bot send one a tick, its move still steered at the corner; nothing
    /// pressed anew, and the one-shot it held not pressed again.
    /// </summary>
    [Fact]
    public void ALateTickSendsANewCommandThatMovesAndPressesNothingAnew()
    {
        IntentCommand reload = Go(MoveKind.Follow, new ActionIntent(ActionKind.Reload, FireStyle.Tap, 0, 1));
        _applier.Submit(in reload);
        UserCmd first = Tick();
        Assert.True((first.Buttons & (int)InputButtons.Reload) != 0, "the one-shot pressed");
        UserCmd late = LateTick();
        Assert.Equal(first.CommandNumber + 1, late.CommandNumber);
        Assert.Equal(first.TickCount + 1, late.TickCount);
        Assert.NotEqual(0f, late.ForwardMove);
        UserCmd after = Tick();
        Assert.Equal(1, Rises([first, late, after], (int)InputButtons.Reload));
        Assert.Equal(1L, _applier.Repeats);
    }

    /// <summary>A one-shot queued and not yet pressed is not spent by a late tick: it goes on the next that is not.</summary>
    [Fact]
    public void ALateTickKeepsAQueuedOneShotForTheNextTick()
    {
        IntentCommand reload = Go(MoveKind.Follow, new ActionIntent(ActionKind.Reload, FireStyle.Tap, 0, 1));
        _applier.Submit(in reload);
        UserCmd late = LateTick();
        Assert.Equal(0, late.Buttons & (int)InputButtons.Reload);
        UserCmd next = Tick();
        Assert.NotEqual(0, next.Buttons & (int)InputButtons.Reload);
        Assert.Equal(0, Tick().Buttons & (int)InputButtons.Reload);
    }

    /// <summary>
    /// The let-go that lets a held jump press again falls due on a late tick:
    /// held instead, so the repeat makes no second press. A jump over a run of
    /// repeats presses once.
    /// </summary>
    [Fact]
    public void ALateTickNeverPressesTheJumpAgain()
    {
        IntentCommand jump = Go(MoveKind.Jump, ActionIntent.None);
        _applier.Submit(in jump);
        var cmds = new List<UserCmd>();
        for (int i = 0; i < BotCommandApplier.JumpCycle - 1; i++)
            cmds.Add(Tick());
        // The JumpCycle-th would let go and the next press again: late, neither comes.
        for (int i = 0; i < 4; i++)
            cmds.Add(LateTick());
        Assert.Equal(1, Rises(cmds, (int)InputButtons.Jump));
        Assert.All(cmds, c => Assert.NotEqual(0, c.Buttons & (int)InputButtons.Jump));
        // The control: not late, the same run lets go and presses again.
        cmds.Add(Tick());
        cmds.Add(Tick());
        Assert.Equal(2, Rises(cmds, (int)InputButtons.Jump));
    }

    /// <summary>A use held for a mount stays held through a late tick, never released and pressed: that press on the ladder is what gets a bot off it.</summary>
    [Fact]
    public void ALateTickHoldsAUseWithoutPressingItAgain()
    {
        IntentCommand use = Go(MoveKind.Follow, new ActionIntent(ActionKind.Use, FireStyle.Tap, 0, 0));
        _applier.Submit(in use);
        UserCmd[] cmds = [Tick(), LateTick(), LateTick(), Tick()];
        Assert.All(cmds, c => Assert.NotEqual(0, c.Buttons & (int)InputButtons.Use));
        Assert.Equal(1, Rises(cmds, (int)InputButtons.Use));
    }

    /// <summary>A tap holds where it was on a late tick: a press every other tick, and a tick's wait, never one extra shot.</summary>
    [Fact]
    public void ALateTickFiresNoExtraTap()
    {
        IntentCommand fire = Go(MoveKind.Follow, new ActionIntent(ActionKind.Fire, FireStyle.Tap, 0, 0));
        _applier.Submit(in fire);
        var cmds = new List<UserCmd>();
        for (int i = 0; i < 6; i++)
            cmds.Add(Tick());
        int plain = Rises(cmds, (int)InputButtons.Attack);
        Assert.True(plain >= 2, $"{plain} presses in six ticks, the control");
        cmds.Clear();
        _applier.Submit(fire with { Seq = ++_seq });
        for (int i = 0; i < 6; i++)
            cmds.Add(i % 2 == 0 ? Tick() : LateTick());
        int withLate = Rises(cmds, (int)InputButtons.Attack);
        Assert.True(withLate < plain, $"every other tick late, its presses wait: {withLate} against {plain}");
    }
}

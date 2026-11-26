using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Tests.Senses;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerBinding;

/// <summary>
/// The applier runs an <see cref="AbilityRun.Program"/> row (plan_bots_teams_tf2_part_g G.5 step 2):
/// the game's program is begun with the one-shot's argument and stepped once a command with the
/// bot's own state; its view goes through the bot's motion, its move is sent in the frame of the
/// view the command has, its buttons are the command's and the intent's hands press nothing; it
/// ends when it says so, when the bot dies or leaves, or when its row's commands run out, and is
/// told which.
/// </summary>
public class AbilityProgramTests
{
    private const float Dt = 0.015f;
    private static readonly ActorId Bot = new(3, 1);
    private static readonly EntityHandle Player = new(0x0001_0003u);
    private const int Fire = (int)InputButtons.Attack, Duck = (int)InputButtons.Duck;
    private const int Glide = 0, Short = 1, Bare = 2, Spin = 3;
    private const int ShortCommands = 5;

    private sealed class Runner : IPlayerCommandRunner
    {
        public readonly List<UserCmd> Queued = new();

        public void Queue(EntityHandle player, in UserCmd cmd) => Queued.Add(cmd);

        public int RunQueued(EntityHandle player) => 1;
    }

    private sealed class Self(AbilityProgramTests fact) : IBotSelf
    {
        // The server's view is the last command's, as a client's own is.
        public BotSelfFacts Read(EntityHandle player)
            => new(fact._alive, false, fact._origin, fact._origin + new AiVector(0f, 0f, 64f),
                   fact._runner.Queued.Count == 0 ? default : new AiAngles(fact._runner.Queued[^1].Pitch, fact._runner.Queued[^1].Yaw, 0f),
                   Velocity: fact._velocity, OnGround: fact._onGround);

        public int WeaponSelectFor(EntityHandle player, int slot) => 40 + slot;
    }

    /// <summary>A program that asks for one view, one move and one set of buttons until told to stop, and keeps what it was told.</summary>
    private sealed class Scripted : IAbilityProgram, IAbilityRun
    {
        public readonly List<AbilityBody> Bodies = new();
        public readonly List<AbilityEnd> Ends = new();
        public readonly List<int> Arguments = new();
        public MotionParams Motion;
        public float TickSeconds;
        public bool Refuses;
        public AbilityCommand Answer = new(default, 1, default, 0);
        public int OverAfter = int.MaxValue;

        public IAbilityRun? Begin(int argument, in AbilityBody body, in MotionParams motion, float tickSeconds)
        {
            Arguments.Add(argument);
            (Motion, TickSeconds) = (motion, tickSeconds);
            return Refuses ? null : this;
        }

        public AbilityCommand Step(in AbilityBody body)
        {
            Bodies.Add(body);
            return Bodies.Count > OverAfter ? AbilityCommand.Done : Answer;
        }

        public void Ended(AbilityEnd why, in AbilityBody body) => Ends.Add(why);
    }

    private sealed class Programs(Scripted program) : IAbilityPrograms
    {
        public IAbilityProgram? Named(string ability) => ability is "glide" or "short" ? program : null;
    }

    private static readonly AbilityTable Table = new([
        new AbilityRow("glide", AbilityRun.Program, HoldTicks: 400),
        new AbilityRow("short", AbilityRun.Program, HoldTicks: ShortCommands),
        new AbilityRow("bare", AbilityRun.Program, HoldTicks: 400),
        new AbilityRow("spin_up", AbilityRun.Buttons, Buttons: (int)InputButtons.Attack2, HoldTicks: 3),
    ]);

    private readonly Runner _runner = new();
    private readonly Scripted _program = new();
    private readonly BotCommandApplier _applier;
    private bool _alive = true, _onGround = true;
    private AiVector _origin = new(10f, 20f, 30f), _velocity = new(240f, 0f, 0f);
    private int _tick = 100, _seq;

    // The motion: a 500 ms reaction (33 commands), a turn of at most 487.5 degrees a second.
    private static readonly MotionParams Motion = new(500f, 487.5f, 16.2f, 174f);

    public AbilityProgramTests()
    {
        _applier = new BotCommandApplier(_runner, new Self(this), null, Table, null, new Programs(_program));
        _applier.Add(Bot, Player, Motion, 42);
    }

    private void Submit(ActionIntent action)
    {
        var from = new Tick(_tick);
        var intent = new IntentCommand(Bot, from, ++_seq, from + IntentCommand.LifetimeTicks, MoveIntent.Hold,
                                       new LookIntent(LookKind.Point, new AiVector(1000f, 20f, 94f), ActorId.None, AimPoint.Body, 0f, 0), action, default);
        Assert.Equal(IntentRefusal.None, _applier.Submit(in intent));
    }

    /// <summary>Asks for the row with <paramref name="argument"/>, and runs the command that takes the one-shot.</summary>
    private void Begin(int row, int argument = 0)
    {
        Submit(new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(row, argument), OneShotSeq: _seq + 1));
        Command();
    }

    private UserCmd Command()
    {
        _applier.ApplyTick(new Tick(_tick++), Dt);
        return _runner.Queued[^1];
    }

    /// <summary><paramref name="count"/> commands, each run; the last one answered.</summary>
    private UserCmd Commands(int count)
    {
        for (int i = 1; i < count; i++)
            Command();
        return Command();
    }

    private sealed class Stance : IPlayerStances
    {
        public bool Standing;

        public bool Ducked(EntityHandle player) => false;

        public bool OnGround(EntityHandle player) => Standing;
    }

    private sealed class NoInventory : IBotInventory
    {
        public IReadOnlyList<string> Held(EntityHandle player) => [];
    }

    /// <summary>What a program is told comes from the bot's own facts, read of its own player: how it moves, and whether it stands on something.</summary>
    [Fact]
    public void ABotsFactsHoldHowItMovesAndWhetherItStands()
    {
        var w = new SenseWorld();
        SenseWorld.Player me = w.Add(1, new AiVector(0f, 0f, 0f));
        me.Velocity = new AiVector(10f, -20f, 300f);
        var stance = new Stance();
        var self = new EngineBotSelf(w.Engine(), w.Entities(), WeaponCatalog.Stock(), new NoInventory(), null, stance);
        Assert.Equal((new AiVector(10f, -20f, 300f), false), (self.Read(me.Handle).Velocity, self.Read(me.Handle).OnGround));
        stance.Standing = true;
        Assert.True(self.Read(me.Handle).OnGround);
    }

    [Fact]
    public void AOneShotNamesItsRowAndCarriesAnArgument()
    {
        int slot = AbilityCall.Pack(5, 1234);
        Assert.Equal((5, 1234), (AbilityCall.Row(slot), AbilityCall.Argument(slot)));
        // A row alone is the slot it always was, and a slot that names nothing names no row.
        Assert.Equal(7, AbilityCall.Pack(7, 0));
        Assert.Equal((-1, 0), (AbilityCall.Row(-3), AbilityCall.Argument(-3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => AbilityCall.Pack(AbilityCall.MaxRow + 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AbilityCall.Pack(0, AbilityCall.MaxArgument + 1));
        Assert.Equal(AbilityCall.MaxArgument, AbilityCall.Argument(AbilityCall.Pack(AbilityCall.MaxRow, AbilityCall.MaxArgument)));
    }

    [Fact]
    public void AProgramIsBegunWithTheOneShotsArgumentTheBotsMotionAndTheCommandsLength()
    {
        Begin(Glide, argument: 77);
        Assert.Equal([77], _program.Arguments);
        Assert.Equal((MotionEnvelope.Clamp(Motion), Dt), (_program.Motion, _program.TickSeconds));
        Assert.Equal(1, _applier.AbilityPrograms);
        Assert.True(_applier.ProgramRunning(Bot));
    }

    [Fact]
    public void AProgramIsSteppedOnceACommandWithTheBotsOwnState()
    {
        Begin(Glide);
        Assert.Empty(_program.Bodies);
        Command();
        _origin = new AiVector(50f, 60f, 70f);
        _velocity = new AiVector(0f, -100f, 300f);
        _onGround = false;
        Command();
        Assert.Equal(2, _program.Bodies.Count);
        Assert.Equal((new AiVector(10f, 20f, 30f), new AiVector(240f, 0f, 0f), true), (_program.Bodies[0].Origin, _program.Bodies[0].Velocity, _program.Bodies[0].OnGround));
        Assert.Equal((new AiVector(50f, 60f, 70f), new AiVector(0f, -100f, 300f), false), (_program.Bodies[1].Origin, _program.Bodies[1].Velocity, _program.Bodies[1].OnGround));
        Assert.Equal(2, _applier.AbilityProgramCommands);
    }

    [Fact]
    public void AProgramIsToldWhichBotItRunsFor()
    {
        Begin(Glide);
        Command();
        Assert.Equal(Bot, Assert.Single(_program.Bodies).Bot);
    }

    [Fact]
    public void AWeaponAProgramNamesIsSelectedWithItsCommand()
    {
        _program.Answer = new AbilityCommand(default, 1, default, 0, Weapon: 5);
        Begin(Glide);
        Assert.Equal(45, Command().WeaponSelect);
    }

    [Fact]
    public void ADefaultCommandSelectsNoWeapon()
    {
        // Round 2's S1, the review's item 8: a command built as default names no weapon, not slot 0.
        _program.Answer = default;
        Begin(Glide);
        Assert.Equal(0, Command().WeaponSelect);
        // The control: slot 0 named on purpose is selected.
        _program.Answer = default(AbilityCommand) with { Weapon = 0 };
        Assert.Equal(40, Command().WeaponSelect);
    }

    [Fact]
    public void AProgramIsToldTheViewItsLastCommandSent()
    {
        _program.Answer = new AbilityCommand(new AiAngles(30f, 120f, 0f), 1, default, 0);
        Begin(Glide);
        UserCmd[] sent = [.. Enumerable.Range(0, 80).Select(_ => Command())];
        Assert.True(MathF.Abs(sent[^1].Yaw) > 60f, $"the control: the view turned to {sent[^1].Yaw}");
        for (int i = 1; i < sent.Length; i++)
            Assert.Equal(new AiAngles(sent[i - 1].Pitch, sent[i - 1].Yaw, 0f), _program.Bodies[i].View);
    }

    [Fact]
    public void AProgramsViewIsReactedToThenTurnedToNoFasterThanTheBotTurns()
    {
        _program.Answer = new AbilityCommand(new AiAngles(40f, 170f, 0f), 1, default, 0);
        Begin(Glide);
        UserCmd[] sent = [.. Enumerable.Range(0, 200).Select(_ => Command())];
        // The reaction: 500 ms, 33 commands in which the view stays where the intent had it.
        Assert.All(sent.Take(30), c => Assert.True(MathF.Abs(c.Yaw) < 2f && MathF.Abs(c.Pitch - sent[0].Pitch) < 0.01f, $"the view moved in the reaction: {c.Pitch} {c.Yaw}"));
        // Then there, and never a step past the bot's turn.
        Assert.True(MathF.Abs(HumanMotion.AngleDelta(sent[^1].Yaw, 170f)) < 1f && MathF.Abs(sent[^1].Pitch - 40f) < 1f, $"the view came to {sent[^1].Pitch} {sent[^1].Yaw}");
        float step = MotionEnvelope.Clamp(Motion).TurnPeakDegPerSec * Dt + 1e-3f;
        for (int i = 1; i < sent.Length; i++)
        {
            float dp = sent[i].Pitch - sent[i - 1].Pitch, dy = HumanMotion.AngleDelta(sent[i - 1].Yaw, sent[i].Yaw);
            Assert.True(MathF.Sqrt(dp * dp + dy * dy) <= step, $"command {i} turned {MathF.Sqrt(dp * dp + dy * dy)} degrees");
        }
    }

    [Fact]
    public void ANewLookOfAProgramIsReactedToAfresh()
    {
        _program.Answer = new AbilityCommand(new AiAngles(0f, 90f, 0f), 1, default, 0);
        Begin(Glide);
        for (int i = 0; i < 150; i++)
            Command();
        float held = Command().Yaw;
        Assert.True(MathF.Abs(held - 90f) < 1f, $"the control: the first look was reached, {held}");
        // The same look moved a little is followed at once; another look waits the reaction out.
        _program.Answer = new AbilityCommand(new AiAngles(0f, 100f, 0f), 1, default, 0);
        float followed = Commands(10).Yaw;
        Assert.True(followed > held + 1f, $"the same look was not followed: {followed}");
        _program.Answer = new AbilityCommand(new AiAngles(0f, -60f, 0f), 2, default, 0);
        float before = Command().Yaw;
        float[] waiting = [.. Enumerable.Range(0, 30).Select(_ => Command().Yaw)];
        Assert.All(waiting, y => Assert.Equal(before, y));
        // And then it goes.
        Assert.True(Commands(120).Yaw < -55f, "the view never went to the new look");
    }

    [Fact]
    public void AProgramsMoveIsSentInTheFrameOfTheViewTheCommandHas()
    {
        // The bot's intent looks east; the program wants to go north, and its look stays east.
        _program.Answer = new AbilityCommand(default, 1, new AiVector(0f, 450f, 0f), 0);
        Begin(Glide);
        UserCmd east = Commands(80);
        Assert.True(MathF.Abs(east.Yaw) < 2f, $"the control: the view is east, {east.Yaw}");
        // North is to the left of a view east: no forward move, a full side move left.
        Assert.Equal((0f, -450f), (MathF.Round(east.ForwardMove), MathF.Round(east.SideMove / 10f) * 10f));

        // Asked to look north too: while the view has not yet left the east (the reaction), the move is still to its left.
        _program.Answer = new AbilityCommand(new AiAngles(0f, 90f, 0f), 2, new AiVector(0f, 450f, 0f), 0);
        UserCmd reacting = Commands(5);
        Assert.True(MathF.Abs(reacting.Yaw) < 2f && MathF.Abs(reacting.ForwardMove) < 16f && reacting.SideMove < -440f,
                    $"yaw {reacting.Yaw} forward {reacting.ForwardMove} side {reacting.SideMove}");

        // Turned to look north, the same wish is straight ahead.
        UserCmd north = Commands(200);
        Assert.True(MathF.Abs(north.Yaw - 90f) < 1f, $"the control: the view is north, {north.Yaw}");
        Assert.True(north.ForwardMove > 449f && MathF.Abs(north.SideMove) < 8f, $"forward {north.ForwardMove} side {north.SideMove}");
    }

    [Fact]
    public void AProgramsButtonsAreTheCommandsAndTheIntentsHandsPressNothing()
    {
        _program.Answer = new AbilityCommand(default, 1, default, Duck);
        // The control: the intent's held fire is pressed before the program begins.
        Submit(new ActionIntent(ActionKind.Fire, FireStyle.Spray, 0, 0));
        Assert.Equal(Fire, Command().Buttons & Fire);
        Begin(Glide);
        for (int i = 0; i < 20; i++)
        {
            Submit(new ActionIntent(ActionKind.Fire, FireStyle.Spray, 0, 0));
            Assert.Equal(Duck, Command().Buttons);
        }
    }

    [Fact]
    public void AProgramThatSaysItIsOverHandsThatCommandBackToTheIntent()
    {
        _program.Answer = new AbilityCommand(default, 1, default, Duck);
        _program.OverAfter = 3;
        Begin(Glide);
        int[] pressed = new int[6];
        for (int i = 0; i < pressed.Length; i++)
        {
            Submit(new ActionIntent(ActionKind.Fire, FireStyle.Spray, 0, 0));
            pressed[i] = Command().Buttons & (Duck | Fire);
        }

        Assert.Equal([Duck, Duck, Duck, Fire, Fire, Fire], pressed);
        Assert.Equal([AbilityEnd.Over], _program.Ends);
        Assert.False(_applier.ProgramRunning(Bot));
        Assert.Equal(4, _program.Bodies.Count);
    }

    [Fact]
    public void DeathEndsAProgramAndItIsTold()
    {
        _program.Answer = new AbilityCommand(default, 1, default, Duck);
        Begin(Glide);
        Command();
        _alive = false;
        Command();
        _alive = true;
        Assert.Equal([AbilityEnd.Died], _program.Ends);
        int stepped = _program.Bodies.Count;
        int[] after = [.. Enumerable.Range(0, 5).Select(_ => Command().Buttons & Duck)];
        Assert.Equal([0, 0, 0, 0, 0], after);
        Assert.Equal(stepped, _program.Bodies.Count);
    }

    [Fact]
    public void ABotThatLeavesEndsItsProgram()
    {
        Begin(Glide);
        Command();
        _applier.Remove(Bot.Slot);
        Assert.Equal([AbilityEnd.Died], _program.Ends);
    }

    [Fact]
    public void AProgramHasAtMostItsRowsCommands()
    {
        _program.Answer = new AbilityCommand(default, 1, default, Duck);
        Begin(Short);
        int[] pressed = [.. Enumerable.Range(0, ShortCommands + 3).Select(_ => Command().Buttons & Duck)];
        Assert.Equal([.. Enumerable.Repeat(Duck, ShortCommands), 0, 0, 0], pressed);
        Assert.Equal([AbilityEnd.TimedOut], _program.Ends);
        Assert.Equal(ShortCommands, _program.Bodies.Count);
    }

    [Fact]
    public void AProgramAskedForWhileOneRunsBeginsWhenTheFirstIsOver()
    {
        _program.OverAfter = 4;
        Begin(Glide, argument: 1);
        Command();
        Submit(new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(Glide, 2), OneShotSeq: _seq + 1));
        Command();
        Command();
        Command();
        Assert.Equal([1], _program.Arguments);
        Assert.Empty(_program.Ends);
        // The fifth step says it is over; that command is the intent's and takes the waiting one-shot.
        Command();
        Assert.Equal([AbilityEnd.Over], _program.Ends);
        Assert.Equal([1, 2], _program.Arguments);
        Assert.Equal(2, _applier.AbilityPrograms);
    }

    [Fact]
    public void WhatTheIntentQueuedWaitsForTheProgramToEnd()
    {
        _program.OverAfter = 4;
        Begin(Glide);
        Command();
        // A weapon select asked for while the program runs goes when it is over, not before and not lost.
        Submit(new ActionIntent(ActionKind.SelectWeapon, FireStyle.Tap, 2, OneShotSeq: _seq + 1));
        int[] selects = [.. Enumerable.Range(0, 6).Select(_ => Command().WeaponSelect)];
        Assert.Equal([0, 0, 0, 42, 0, 0], selects);
    }

    [Fact]
    public void AProgramThatRefusesToBeginRunsNothingAndIsCounted()
    {
        _program.Refuses = true;
        Begin(Glide, argument: 9);
        Command();
        Assert.Equal([9], _program.Arguments);
        Assert.Empty(_program.Bodies);
        Assert.Equal((0, 1), (_applier.AbilityPrograms, _applier.AbilityProgramsRefused));
        Assert.False(_applier.ProgramRunning(Bot));
    }

    [Fact]
    public void AProgramRowTheGameSuppliesNoProgramForDoesNothing()
    {
        Begin(Bare);
        Command();
        Assert.Empty(_program.Arguments);
        Assert.Equal(0, _applier.AbilityPrograms);
    }

    [Fact]
    public void AButtonRowBesideAProgramRowIsHeldAsItWas()
    {
        Begin(Spin);
        int[] held = [.. Enumerable.Range(0, 4).Select(_ => Command().Buttons & (int)InputButtons.Attack2)];
        // The command that took the one-shot held it first: two more, then let go.
        Assert.Equal([(int)InputButtons.Attack2, (int)InputButtons.Attack2, 0, 0], held);
        Assert.Empty(_program.Arguments);
    }
}

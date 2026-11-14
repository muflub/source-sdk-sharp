using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerBinding;

/// <summary>
/// The applier runs an <see cref="ActionKind.Ability"/> one-shot from the game's
/// <see cref="AbilityTable"/> (plan_bots_teams_tf2 C.3): a button row is held for
/// its ticks; a command row's word is sent once, as the bot's, and presses nothing;
/// a sequence row's steps run in order, each its buttons for its ticks and the view
/// to its pitch; a row the table lacks presses nothing, and death ends a sequence.
/// </summary>
public class AbilityApplyTests
{
    private const float Dt = 0.015f;
    private static readonly ActorId Bot = new(3, 1);
    private static readonly EntityHandle Player = new(0x0001_0003u);
    private const int Spin = (int)InputButtons.Attack2;

    private sealed class Runner : IPlayerCommandRunner
    {
        public readonly List<UserCmd> Queued = new();

        public void Queue(EntityHandle player, in UserCmd cmd) => Queued.Add(cmd);

        public int RunQueued(EntityHandle player) => 1;
    }

    private sealed class Self(AbilityApplyTests fact) : IBotSelf
    {
        // The server's view is the last command's, as a client's own is.
        public BotSelfFacts Read(EntityHandle player)
            => new(fact._alive, false, new AiVector(0f, 0f, 0f), new AiVector(0f, 0f, 64f),
                   fact._runner.Queued.Count == 0 ? default : new AiAngles(fact._runner.Queued[^1].Pitch, fact._runner.Queued[^1].Yaw, 0f));

        public int WeaponSelectFor(EntityHandle player, int slot) => 0;
    }

    private sealed class Commands : IBotClientCommand
    {
        public readonly List<(EntityHandle Bot, string Command)> Sent = new();

        public void Run(EntityHandle bot, string command) => Sent.Add((bot, command));
    }

    private const int Jump = (int)InputButtons.Jump, Duck = (int)InputButtons.Duck, Fire = (int)InputButtons.Attack;

    private static readonly AbilityTable Table = new([
        new AbilityRow("spin_up", AbilityRun.Buttons, Buttons: Spin, HoldTicks: 3),
        new AbilityRow("build_sentry", AbilityRun.Command, Command: "build 2 0", Buttons: Spin),
        new AbilityRow("blast_jump", AbilityRun.Sequence, Steps: [new AbilityStep(0, 2, 89f), new AbilityStep(Jump | Duck, 1, 89f), new AbilityStep(Jump | Duck | Fire, 2, 89f), new AbilityStep(Duck, 1)]),
        new AbilityRow("look_down", AbilityRun.Sequence, Steps: [new AbilityStep(0, 120, 89f)]),
    ]);

    private readonly Runner _runner = new();
    private readonly Commands _commands = new();
    private readonly BotCommandApplier _applier;
    private bool _alive = true;
    private int _tick = 100;

    public AbilityApplyTests()
    {
        _applier = new BotCommandApplier(_runner, new Self(this), null, Table, _commands);
        _applier.Add(Bot, Player, new MotionParams(500f, 487.5f, 16.2f, 174f), 42);
    }

    private int _seq;

    private void Ask(int row)
    {
        var from = new Tick(_tick);
        _seq++;
        var intent = new IntentCommand(Bot, from, _seq, from + IntentCommand.LifetimeTicks, MoveIntent.Hold,
                                       new LookIntent(LookKind.Point, new AiVector(1000f, 0f, 64f), ActorId.None, AimPoint.Body, 0f, 0),
                                       new ActionIntent(ActionKind.Ability, FireStyle.Tap, row, OneShotSeq: _seq), default);
        _applier.Submit(in intent);
    }

    private int Buttons()
    {
        _applier.ApplyTick(new Tick(_tick++), Dt);
        return _runner.Queued[^1].Buttons;
    }

    [Fact]
    public void AButtonRowIsHeldForItsTicksThenLetGo()
    {
        Ask(0);
        int[] held = [Buttons(), Buttons(), Buttons(), Buttons()];
        Assert.Equal([Spin, Spin, Spin, 0], held.Select(b => b & Spin));
    }

    [Fact]
    public void ARowTheTableLacksPressesNothing()
    {
        Ask(7);
        Assert.Equal(0, Buttons() & Spin);
    }

    [Fact]
    public void ACommandRowSendsItsWordOnceAsTheBotsAndPressesNoButton()
    {
        Ask(1);
        int[] pressed = [Buttons(), Buttons(), Buttons()];
        Assert.Equal([0, 0, 0], pressed);
        Assert.Equal([(Player, "build 2 0")], _commands.Sent);
        Assert.Equal(1, _applier.AbilityCommands);
    }

    [Fact]
    public void ASequenceRowRunsItsStepsInOrderEachForItsTicks()
    {
        Ask(2);
        int[] pressed = [.. Enumerable.Range(0, 8).Select(_ => Buttons() & (Jump | Duck | Fire))];
        Assert.Equal([0, 0, Jump | Duck, Jump | Duck | Fire, Jump | Duck | Fire, Duck, 0, 0], pressed);
        Assert.Empty(_commands.Sent);
    }

    [Fact]
    public void ASequenceStepTurnsTheViewToItsPitch()
    {
        // The control: held, the view stays on the intent's point, level.
        float[] level = [.. Enumerable.Range(0, 40).Select(_ => Command().Pitch)];
        Assert.True(MathF.Abs(level[^1]) < 2f, $"the view went to {level[^1]}");
        Ask(3);
        // The fake's reaction is 500 ms (33 commands); a flick follows.
        float[] pitches = [.. Enumerable.Range(0, 90).Select(_ => Command().Pitch)];
        Assert.True(pitches[^1] > 45f, $"the view went from {level[^1]} to {pitches[^1]}");
    }

    [Fact]
    public void DeathEndsARunningSequence()
    {
        Ask(2);
        Buttons();
        Buttons();
        _alive = false;
        Buttons();
        _alive = true;
        int[] after = [.. Enumerable.Range(0, 5).Select(_ => Buttons() & (Jump | Duck | Fire))];
        Assert.Equal([0, 0, 0, 0, 0], after);
    }

    private UserCmd Command()
    {
        _applier.ApplyTick(new Tick(_tick++), Dt);
        return _runner.Queued[^1];
    }
}

using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerBinding;

/// <summary>
/// The applier runs an <see cref="ActionKind.Ability"/> one-shot from the game's
/// <see cref="AbilityTable"/> (plan_bots_teams_tf2 C.3): a button row is held for
/// its ticks; a row the table lacks, and a row not yet run, press nothing.
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

    private sealed class Self : IBotSelf
    {
        public BotSelfFacts Read(EntityHandle player) => new(true, false, new AiVector(0f, 0f, 0f), new AiVector(0f, 0f, 64f), default);

        public int WeaponSelectFor(EntityHandle player, int slot) => 0;
    }

    private static readonly AbilityTable Table = new([
        new AbilityRow("spin_up", AbilityRun.Buttons, Buttons: Spin, HoldTicks: 3),
        new AbilityRow("build_sentry", AbilityRun.Command, Command: "build 2 0", Buttons: Spin),
    ]);

    private readonly Runner _runner = new();
    private readonly BotCommandApplier _applier;
    private int _tick = 100;

    public AbilityApplyTests()
    {
        _applier = new BotCommandApplier(_runner, new Self(), null, Table);
        _applier.Add(Bot, Player, new MotionParams(500f, 487.5f, 16.2f, 174f), 42);
    }

    private void Ask(int row)
    {
        var from = new Tick(_tick);
        var intent = new IntentCommand(Bot, from, 1, from + IntentCommand.LifetimeTicks, MoveIntent.Hold,
                                       new LookIntent(LookKind.Point, new AiVector(1000f, 0f, 64f), ActorId.None, AimPoint.Body, 0f, 0),
                                       new ActionIntent(ActionKind.Ability, FireStyle.Tap, row, OneShotSeq: 1), default);
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
    public void ACommandRowPressesNoButton()
    {
        Ask(1);
        Assert.Equal(0, Buttons());
    }
}

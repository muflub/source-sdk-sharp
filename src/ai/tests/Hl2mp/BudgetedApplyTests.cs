using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp;
using SourceSharp.Ai.Hl2mp.Senses;
using SourceSharp.Ai.Tests.Senses;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mp;

/// <summary>
/// The bots' commands under the main-thread budget (user ruling 2026-11-03),
/// with a fake clock each command costs on: a bot whose turn does not fit runs
/// later, as a client whose packet came late, with each of its commands' own
/// tick, one-shots once, none dropped and none sent twice; the first bot goes
/// round; the bound forces a run; off, and at <c>ai_sync 1</c>, there is no cap.
/// </summary>
public sealed class BudgetedApplyTests
{
    private const float Dt = 0.015f;
    private const double Cost = 100d;

    private sealed class Clock
    {
        public double Wall;
    }

    /// <summary>A command queue as the core's: a run takes all that is queued, oldest first, each command at its cost.</summary>
    private sealed class Runner(Clock clock) : IPlayerCommandRunner
    {
        private readonly Dictionary<EntityHandle, List<UserCmd>> _queues = new();
        public readonly List<(EntityHandle Player, int Tick, UserCmd Cmd)> Ran = new();
        public readonly List<(EntityHandle Player, int Commands, int Tick)> Batches = new();
        public readonly Dictionary<EntityHandle, AiAngles> Views = new();
        public int Now;

        public void Queue(EntityHandle player, in UserCmd cmd)
        {
            if (!_queues.TryGetValue(player, out List<UserCmd>? q))
                _queues[player] = q = new List<UserCmd>();
            q.Add(cmd);
        }

        public int RunQueued(EntityHandle player)
        {
            List<UserCmd> q = _queues[player];
            Batches.Add((player, q.Count, Now));
            foreach (UserCmd c in q)
            {
                Ran.Add((player, Now, c));
                clock.Wall += Cost;
                Views[player] = new AiAngles(c.Pitch, c.Yaw, 0f);
            }

            int n = q.Count;
            q.Clear();
            return n;
        }

        public int QueuedFor(EntityHandle player) => _queues.TryGetValue(player, out List<UserCmd>? q) ? q.Count : 0;
    }

    /// <summary>A bot's own facts: its view is what its last command to run said, as the server's is.</summary>
    private sealed class Self(Runner runner) : IBotSelf
    {
        public int Reads;

        public BotSelfFacts Read(EntityHandle player)
        {
            Reads++;
            return new BotSelfFacts(true, false, default, new AiVector(0f, 0f, 64f), runner.Views.GetValueOrDefault(player));
        }

        public int WeaponSelectFor(EntityHandle player, int slot) => 0;
    }

    private static readonly MotionParams Normal = new(500f, 487.5f, 16.2f, 174f);
    private readonly Clock _clock = new();
    private readonly Runner _runner;
    private readonly Self _self;
    private readonly BotCommandApplier _applier;
    private readonly MainThreadBudget _budget;
    private readonly ActorId[] _ids = [new(2, 1), new(3, 2), new(4, 3)];
    private readonly EntityHandle[] _players = [new(0x0001_0002u), new(0x0001_0003u), new(0x0001_0004u)];
    private int _tick = 100, _frame, _seq;

    public BudgetedApplyTests()
    {
        _runner = new Runner(_clock);
        _self = new Self(_runner);
        _applier = new BotCommandApplier(_runner, _self);
        _budget = new MainThreadBudget(() => _clock.Wall, null);
        for (int i = 0; i < _ids.Length; i++)
            _applier.Add(_ids[i], _players[i], Normal, (ulong)(40 + i));
    }

    /// <summary>One frame of one tick, with room for <paramref name="floor"/> of the bots' time and no unused time.</summary>
    private void Frame(double floor, int bound = 3, MainThreadBudget? budget = null)
    {
        _runner.Now = _tick;
        _budget.Begin(_frame++, new BudgetFrame(0d, 0d, floor, bound, true));
        _applier.ApplyTick(new Tick(_tick++), Dt, budget ?? _budget);
    }

    private void Submit(int bot, ActionIntent action = default, MoveKind kind = MoveKind.Follow)
    {
        var from = new Tick(_tick);
        var intent = new IntentCommand(_ids[bot], from, ++_seq, from + IntentCommand.LifetimeTicks,
                                       new MoveIntent(kind, MoveSpeed.Run, 1, new AiVector(4000f, 0f, 0f), default, default, default, default, false, 0),
                                       LookIntent.AlongPath, action, default);
        Assert.Equal(IntentRefusal.None, _applier.Submit(in intent));
    }

    [Fact]
    public void WithRoomEveryBotRunsItsCommandEachTick()
    {
        Frame(100_000d);
        Frame(100_000d);
        Assert.Equal(6, _runner.Ran.Count);
        Assert.All(_runner.Batches, b => Assert.Equal(1, b.Commands));
        Assert.Equal(0L, _budget.Deferrals);
    }

    /// <summary>The floor holds one bot's command: the others wait, and each runs in a later frame.</summary>
    [Fact]
    public void ABotThatDoesNotFitWaitsAndRunsLaterWithItsOwnTick()
    {
        Frame(Cost);   // one fits (the floor is used up by its cost), two wait
        Assert.Single(_runner.Ran);
        Assert.Equal(2L, _budget.Deferrals);
        EntityHandle waiting = _players.First(p => _runner.QueuedFor(p) == 1);

        // Nothing costs now, with room: what waited runs, with the new tick's after it.
        Frame(100_000d);
        (EntityHandle Player, int Tick, UserCmd Cmd)[] its = [.. _runner.Ran.Where(r => r.Player == waiting)];
        Assert.Equal(2, its.Length);
        Assert.Equal(100, its[0].Cmd.TickCount);
        Assert.Equal(101, its[0].Tick);          // ran a tick late
        Assert.Equal(101, its[1].Cmd.TickCount);
        Assert.Equal(its[0].Cmd.CommandNumber + 1, its[1].Cmd.CommandNumber);
        Assert.Contains(_runner.Batches, b => b.Player == waiting && b.Commands == 2);
    }

    /// <summary>However a run of frames falls, every tick's command of every bot runs once, in order: none dropped, none sent twice.</summary>
    [Fact]
    public void NoCommandIsDroppedOrSentTwice()
    {
        const int Ticks = 200;
        for (int t = 0; t < Ticks; t++)
            Frame(t % 7 == 0 ? 100_000d : Cost, bound: 3);
        Assert.True(_budget.Deferrals > 100, $"the stimulus: {_budget.Deferrals} deferrals");
        Frame(100_000d);    // what is left waiting
        foreach (EntityHandle p in _players)
        {
            int[] ticks = [.. _runner.Ran.Where(r => r.Player == p).Select(r => r.Cmd.TickCount)];
            Assert.Equal(Enumerable.Range(100, Ticks + 1), ticks);
            int[] numbers = [.. _runner.Ran.Where(r => r.Player == p).Select(r => r.Cmd.CommandNumber)];
            Assert.Equal(Enumerable.Range(numbers[0], Ticks + 1), numbers);
        }
    }

    [Fact]
    public void ADeferredOneShotRunsOnceWithItsCommand()
    {
        for (int b = 0; b < 3; b++)
            Submit(b, new ActionIntent(ActionKind.Reload, FireStyle.Tap, 0, 1));
        Frame(Cost);
        for (int t = 0; t < 3; t++)
            Frame(Cost);
        Frame(100_000d);
        Assert.True(_budget.Deferrals > 0, "some waited");
        foreach (EntityHandle p in _players)
        {
            UserCmd[] presses = [.. _runner.Ran.Where(r => r.Player == p && (r.Cmd.Buttons & (int)InputButtons.Reload) != 0).Select(r => r.Cmd)];
            UserCmd only = Assert.Single(presses);
            Assert.Equal(100, only.TickCount);
        }
    }

    /// <summary>The first bot goes round: with room for one a frame each gets its turn, not the lowest slot every time.</summary>
    [Fact]
    public void TheFirstBotGoesRound()
    {
        var first = new List<EntityHandle>();
        for (int t = 0; t < 3; t++)
        {
            int before = _runner.Batches.Count;
            Frame(Cost, bound: 32);
            first.Add(_runner.Batches[before].Player);
        }

        Assert.Equal(3, first.Distinct().Count());
    }

    /// <summary>The bots go as a wheel: the first put off goes first next time, so a bot is never passed over while others run twice.</summary>
    [Fact]
    public void TheBotPutOffGoesFirstNextTick()
    {
        Frame(Cost * 2d, bound: 32);
        Assert.Equal(2, _runner.Batches.Count);
        EntityHandle putOff = _players.Single(p => _runner.QueuedFor(p) == 1);
        Frame(Cost * 2d, bound: 32);
        Assert.Equal(putOff, _runner.Batches[2].Player);
    }

    [Fact]
    public void TheBoundForcesARunPastTheCap()
    {
        const int Bound = 2;
        for (int t = 0; t < 40; t++)
            Frame(0d, Bound);
        Assert.True(_budget.Forced > 0, "the cap gave way");
        Assert.True(_budget.Deferrals > 0, "and they did wait");
        int[] commandsInBatch = [.. _runner.Batches.Select(b => b.Commands)];
        Assert.Equal(Bound + 1, commandsInBatch.Max());
        // No bot's commands ever waited past the bound.
        foreach (EntityHandle p in _players)
        {
            foreach ((_, int ranAt, UserCmd cmd) in _runner.Ran.Where(r => r.Player == p))
                Assert.InRange(ranAt - cmd.TickCount, 0, Bound);
        }
    }

    /// <summary>A bot whose commands wait is not read: the saving the wait is for, and it has not moved. Its view's motion is not reset by a server view that is behind it.</summary>
    [Fact]
    public void AWaitingBotIsNotReadAndItsViewIsNotReset()
    {
        var far = new LookIntent(LookKind.Point, new AiVector(0f, 4000f, 64f), ActorId.None, AimPoint.Body, 0f, 0);
        for (int b = 0; b < 3; b++)
        {
            var from = new Tick(_tick);
            var intent = new IntentCommand(_ids[b], from, ++_seq, from + 200, MoveIntent.Hold, far, ActionIntent.None, default);
            _applier.Submit(in intent);
        }

        for (int t = 0; t < 40; t++)
            Frame(100_000d);   // settled, the reaction past, the view moving
        // The server keeps up with the motion when commands run (a run is what turns it).
        int waited = 0;
        for (int t = 0; t < 12; t++)
        {
            int reads = _self.Reads;
            int before = _ids.Count(id => _applier.WaitingOf(id) > 0);
            Frame(0d, bound: 3);
            waited += before;
            Assert.Equal(3 - before, _self.Reads - reads);
        }

        Assert.True(waited > 0, "some waited");
        Assert.All(_ids, id => Assert.Equal(0L, _applier.StateOf(id.Slot)!.ViewResets));
    }

    [Fact]
    public void NoBudgetRunsEveryBotInSlotOrderEveryTick()
    {
        _runner.Now = _tick;
        _applier.ApplyTick(new Tick(_tick++), Dt);
        _applier.ApplyTick(new Tick(_tick++), Dt);
        Assert.Equal([.. _players, .. _players], _runner.Batches.Select(b => b.Player));
    }

    // ---- The binding's settings ---------------------------------------------------------------

    private static AiMainBudget Binding(TestGlobals env, string? fpsMax = "60", bool dedicated = false)
    {
        IEngineConsole console = FakeEngine.Of<IEngineConsole>((m, a) => m.Name == "FindConVarValue" && (string)a[0]! == "fps_max" ? fpsMax : throw FakeEngine.Unasked(m));
        IEngineHost host = FakeEngine.Of<IEngineHost>((m, _) => m.Name == "get_IsDedicatedServer" ? dedicated : throw FakeEngine.Unasked(m));
        return new AiMainBudget(env, console, host, DeclaredConVars.FreshAt<AiBudgetConVar>("1"), DeclaredConVars.Fresh<AiBudgetFloorConVar>(), DeclaredConVars.Fresh<AiBudgetWaitConVar>());
    }

    [Fact]
    public void TheCapIsOnWhenAskedForAndOffAtAiSync()
    {
        using var env = new TestGlobals();
        AiMainBudget b = Binding(env);
        b.Begin(sync: false);
        Assert.True(b.Current!.Capped);
        Assert.Equal(1e6 / 60d, b.Measure.TargetUs, 1);

        env.Frame++;
        b.Begin(sync: true);
        Assert.False(b.Current!.Capped);
    }

    [Fact]
    public void TheMasterSwitchTurnsTheCapOff()
    {
        using var env = new TestGlobals();
        IEngineConsole console = FakeEngine.Of<IEngineConsole>((m, _) => m.Name == "FindConVarValue" ? "60" : throw FakeEngine.Unasked(m));
        IEngineHost host = FakeEngine.Of<IEngineHost>((m, _) => m.Name == "get_IsDedicatedServer" ? false : throw FakeEngine.Unasked(m));
        var b = new AiMainBudget(env, console, host, DeclaredConVars.FreshAt<AiBudgetConVar>("0"), DeclaredConVars.Fresh<AiBudgetFloorConVar>(), DeclaredConVars.Fresh<AiBudgetWaitConVar>());
        b.Begin(sync: false);
        Assert.False(b.Current!.Capped);
    }

    /// <summary>Uncapped the bots' time is still measured, and they run as with no budget: every one, in slot order. So a run with it off can be compared.</summary>
    [Fact]
    public void UncappedEveryBotRunsInSlotOrderAndItsTimeIsCharged()
    {
        _runner.Now = _tick;
        _budget.Begin(_frame++, new BudgetFrame(0d, 0d, 0d, 0, false));
        _applier.ApplyTick(new Tick(_tick++), Dt, _budget);
        _applier.ApplyTick(new Tick(_tick++), Dt, _budget);
        Assert.Equal([.. _players, .. _players], _runner.Batches.Select(b => b.Player));
        Assert.Equal(6 * Cost, _budget.UsedUs);
        Assert.Equal(0L, _budget.Deferrals);
    }

    [Fact]
    public void TheConVarsHaveTheirDefaultsAndTheFloorAndBoundReachTheBudget()
    {
        Assert.False(DeclaredConVars.Fresh<AiBudgetConVar>().Value);
        Assert.Equal(1000, DeclaredConVars.Fresh<AiBudgetFloorConVar>().Value);
        Assert.Equal(3, DeclaredConVars.Fresh<AiBudgetWaitConVar>().Value);
        using var env = new TestGlobals();
        AiMainBudget b = Binding(env);
        b.Begin(sync: false);
        Assert.Equal(3, b.Measure.BoundTicks);
        Assert.True(b.Measure.AllowanceUs >= 1000d);
    }

    [Theory]
    [InlineData("0", 0d)]
    [InlineData("60", 1e6 / 60d)]
    [InlineData("300", 1e6 / 300d)]
    [InlineData(null, 0d)]
    [InlineData("not a number", 0d)]
    public void AFrameTargetIsFpsMaxsPeriod(string? fpsMax, double expected) => Assert.Equal(expected, AiMainBudget.TargetOf(fpsMax), 3);

    /// <summary>A dedicated server has no render to protect: its frame is the tick, whatever fps_max says, and its wait does not spin.</summary>
    [Fact]
    public void ADedicatedServersFrameIsTheTick()
    {
        using var env = new TestGlobals();
        AiMainBudget b = Binding(env, fpsMax: "300", dedicated: true);
        b.Begin(sync: false);
        Assert.Equal(15_000d, b.Measure.TargetUs, 1);
    }

    [Fact]
    public void WithNoEngineGlobalsThereIsNoBudget()
    {
        IEngineEnvironment none = FakeEngine.Of<IEngineEnvironment>((m, _) => m.Name == "get_Globals" ? default(Globals) : throw FakeEngine.Unasked(m));
        var b = new AiMainBudget(none, null!, null!, DeclaredConVars.FreshAt<AiBudgetConVar>("1"), DeclaredConVars.Fresh<AiBudgetFloorConVar>(), DeclaredConVars.Fresh<AiBudgetWaitConVar>());
        b.Begin(sync: false);
        Assert.Null(b.Current);
    }
}

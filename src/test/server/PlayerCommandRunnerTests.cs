using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A bot's commands run from managed code (plan_hl2mp_ai §3.2, Phase 3 step
/// 3): <see cref="PlayerCommandRunner"/> is RunQueuedCommands' gate, tick base
/// and clock restore around the loop a human's commands take, and every
/// command, a human's or a bot's, reaches the <see cref="IPlayerCommandObserver"/>
/// hooks (§3.4.4).
/// </summary>
public sealed unsafe class PlayerCommandRunnerTests : IDisposable
{
    private static readonly EntityHandle Player = new(0x0003_0002u);

    private sealed class Env : IEngineEnvironment, IDisposable
    {
        public readonly CGlobalVarsRaw* Raw = (CGlobalVarsRaw*)NativeMemory.AllocZeroed((nuint)sizeof(CGlobalVarsRaw));
        public nint GetInterface(string name) => 0;
        public Globals Globals => new(Raw);
        public void Dispose() => NativeMemory.Free(Raw);
    }

    private readonly Env _env = new();

    public PlayerCommandRunnerTests()
    {
        _env.Raw->interval_per_tick = 0.015f;
        _env.Raw->frametime = 0.015f;
        _env.Raw->curtime = 10f;
        _env.Raw->tickcount = 667;
        _env.Raw->maxClients = 2;
    }

    public void Dispose()
    {
        _root.Dispose();
        _env.Dispose();
    }

    private PlayerCommandRunner Runner(PlayerCommandQueue queue, PlayerCommandStage stage, Action<EntityHandle, int>? tickBase = null)
        => new(queue, stage, tickBase, _env, null, null, null, () => false);

    private static UserCmd Cmd(int number) => new() { CommandNumber = number, TickCount = 600 + number, Buttons = 2, ForwardMove = 400f };

    //-------------------------------------------------------------------------
    // The gate, the tick base and the clock.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheFramesClockIsPutBackAfterTheBatch()
    {
        // The stages set each command's clock (ServerPlayerCommands.Prepare's
        // SetClock); the frame's curtime and frametime come back after, as
        // RunQueuedCommands' flSaveCurtime/flSaveFrametime put them back.
        var queue = new PlayerCommandQueue(new RecordingLog());
        float seen = 0f;
        var runner = Runner(queue, (_, _, stage, in _) =>
        {
            if (stage == 0)
                _env.Globals.SetClock(999f, 0.5f);
            else
                seen = _env.Globals.CurTime;
            return 1;
        });

        runner.Queue(Player, Cmd(1));
        Assert.Equal(1, runner.RunQueued(Player));

        Assert.Equal(999f, seen);
        Assert.Equal((10f, 0.015f), (_env.Raw->curtime, _env.Raw->frametime));
    }

    [Fact]
    public void ASecondRunInOneTickRunsNothing()
    {
        // m_nSimulationTick: once per server tick per player.
        var queue = new PlayerCommandQueue(new RecordingLog());
        var runner = Runner(queue, (_, _, _, in _) => 1);

        runner.Queue(Player, Cmd(1));
        Assert.Equal(1, runner.RunQueued(Player));
        runner.Queue(Player, Cmd(2));
        Assert.Equal(0, runner.RunQueued(Player));

        _env.Raw->tickcount++;
        Assert.Equal(1, runner.RunQueued(Player));
    }

    [Fact]
    public void TheTickBaseIsPlacedForTheBatchItRuns()
    {
        var queue = new PlayerCommandQueue(new RecordingLog());
        var placed = new List<(EntityHandle, int)>();
        var runner = Runner(queue, (_, _, _, in _) => 1, (p, n) => placed.Add((p, n)));

        runner.Queue(Player, Cmd(1));
        runner.Queue(Player, Cmd(2));
        runner.RunQueued(Player);

        Assert.Equal([(Player, 2)], placed);
    }

    /// <summary>
    /// A bot held back by the AI's main-thread budget runs its waiting commands as
    /// one batch, as a client whose packet came late: all of them, oldest first, each
    /// with its own tick, the tick base told how many, the tick's gate untouched by
    /// the ticks it did not run on.
    /// </summary>
    [Fact]
    public void ALateBatchRunsEveryCommandOldestFirstWithItsOwnTick()
    {
        var queue = new PlayerCommandQueue(new RecordingLog());
        var seen = new List<int>();
        int placed = -1;
        PlayerCommandRunner runner = Runner(queue, (EntityHandle _, int _, int stage, in UserCmd cmd) =>
        {
            if (stage == 0)
                seen.Add(cmd.TickCount);
            return 1;
        }, (_, commands) => placed = commands);

        Assert.Equal(1, Queue(runner, 1));
        Assert.Equal(1, Queue(runner, 2));
        Assert.Equal(1, Queue(runner, 3));
        _env.Raw->tickcount += 2;
        Assert.Equal(3, runner.RunQueued(Player));
        Assert.Equal([601, 602, 603], seen);
        Assert.Equal(3, placed);
        Assert.Equal(0, queue.Depth(Player));

        // The next tick's own command runs in its own tick: the batch took only its gate.
        _env.Raw->tickcount++;
        Queue(runner, 4);
        Assert.Equal(1, runner.RunQueued(Player));
        Assert.Equal([601, 602, 603, 604], seen);
    }

    private int Queue(PlayerCommandRunner runner, int number)
    {
        UserCmd cmd = Cmd(number);
        runner.Queue(Player, in cmd);
        return 1;
    }

    /// <summary>
    /// Why deferring a bot is safe (AdjustPlayerTimeBase, player.cpp:3116): a batch
    /// of late commands starts its count from the same tick base, so each runs on
    /// the clock it would have had, and the batch stays inside the correction window.
    /// Compared with on-time commands, tick by tick, with the real rule.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    public void ALateBatchsCommandsRunOnTheClockTheyWouldHaveHad(int batch)
    {
        const int Correction = 4, Start = 1000, Ticks = 200;
        int onTimeBase = 0, lateBase = 0;
        var onTime = new List<int>();
        var late = new List<int>();
        int firstCorrected = ServerCommandRules.AdjustTickBase(0, Start, 1, 1, Correction, singlePlayer: false);
        onTimeBase = firstCorrected;
        lateBase = firstCorrected;
        int corrections = 0;
        for (int t = Start; t < Start + Ticks; t++)
        {
            int placed = ServerCommandRules.AdjustTickBase(onTimeBase, t, 1, 1, Correction, singlePlayer: false);
            onTimeBase = placed;
            onTime.Add(onTimeBase++);
            if ((t - Start + 1) % batch != 0)
                continue;
            int was = lateBase;
            lateBase = ServerCommandRules.AdjustTickBase(lateBase, t, batch, 1, Correction, singlePlayer: false);
            corrections += lateBase != was ? 1 : 0;
            for (int c = 0; c < batch; c++)
                late.Add(lateBase++);
        }

        Assert.Equal(0, corrections);
        Assert.True(late.Count >= Ticks - batch);
        Assert.Equal(onTime.Take(late.Count), late);
    }

    [Fact]
    public void AnEmptyQueueRunsNothingAndPlacesNoTickBase()
    {
        var queue = new PlayerCommandQueue(new RecordingLog());
        int placed = 0;
        var runner = Runner(queue, (_, _, _, in _) => 1, (_, _) => placed++);

        Assert.Equal(0, runner.RunQueued(Player));
        Assert.Equal(0, placed);
    }

    //-------------------------------------------------------------------------
    // The observers: one tap, humans and bots.
    //-------------------------------------------------------------------------

    private sealed class Observer : IPlayerCommandObserver
    {
        public readonly List<(EntityHandle Player, int Number)> Seen = new();
        public void OnCommand(EntityHandle player, in UserCmd cmd) => Seen.Add((player, cmd.CommandNumber));
    }

    private sealed class Hooks(params IPlayerCommandObserver[] all) : IHooks<IPlayerCommandObserver>
    {
        public IReadOnlyList<IPlayerCommandObserver> All => all;
        public void Each(string what, Action<IPlayerCommandObserver> call) => throw new InvalidOperationException("the sink walks All");
    }

    [Fact]
    public void AHumansAndABotsCommandsBothReachTheObservers()
    {
        var observer = new Observer();
        var queue = new PlayerCommandQueue(new RecordingLog()) { Observers = new Hooks(observer) };
        var human = new EntityHandle(0x0001_0001u);
        var bot = new EntityHandle(0x0002_0002u);

        // The human: a decoded packet queued, run through the loop the
        // player-command binding runs it through (the same overload, the
        // shipped sink), with stages that accept it.
        UserCmd h = Cmd(7);
        int order;
        queue.Queue(human, &h, 1, 1, 0, &order, 1);
        queue.Run(human, 1, (_, _, _, in _) => 1, null, null, null, false);

        // The bot: the runner.
        var runner = Runner(queue, (_, _, _, in _) => 1);
        runner.Queue(bot, Cmd(9));
        runner.RunQueued(bot);

        Assert.Equal([(human, 7), (bot, 9)], observer.Seen);
    }

    [Fact]
    public void AThrowingObserverDoesNotStopTheNext()
    {
        var after = new Observer();
        var log = new RecordingLog();
        var queue = new PlayerCommandQueue(log) { Observers = new Hooks(new Throwing(), after) };
        var runner = Runner(queue, (_, _, _, in _) => 1);

        runner.Queue(Player, Cmd(3));
        Assert.Equal(1, runner.RunQueued(Player));

        Assert.Equal([(Player, 3)], after.Seen);
    }

    private sealed class Throwing : IPlayerCommandObserver
    {
        public void OnCommand(EntityHandle player, in UserCmd cmd) => throw new InvalidOperationException("observer");
    }

    //-------------------------------------------------------------------------
    // Same command, same outcome: the real stages over a fake engine.
    //-------------------------------------------------------------------------

    private readonly FakeShimRoot _root = new();

    private const string MovementServer = "SourceSharpMovementServer001";

    private sealed class NoPublish : IPlayerMovePublisher
    {
        public void Publish(EntityHandle player, ServerPlayerMove move) { }
    }

    private sealed class NoSounds : IMoveSoundSink
    {
        public void MoveSound(int entIndex, Vector origin, int channel, string sample, float volume, int soundLevel,
                              int flags, int pitch) { }

        public void MoveSoundScript(int entIndex, Vector origin, string soundName) { }

        public bool Footsteps => false;

        public int MaxClients => 1;

        public void StepSound(int entIndex, Vector origin, float volume, bool left) { }
    }

    [UnmanagedCallersOnly]
    private static int Begin(void* self, uint player, void* facts)
    {
        var f = (PlayerCommandFacts*)facts;
        f->Health = 100;
        f->EntIndex = 2;
        f->ModelScale = 1f;
        f->Origin = new Vector(64f, 32f, 0f);
        return 1;
    }

    private (ServerPlayerCommands Commands, ServerPlayerMoves Moves) Stack(ServerModuleEngine engine)
    {
        var moves = new ServerPlayerMoves();
        var commands = ServerPlayerCommands.For(engine, EngineBound.Trace(engine.Api), EngineBound.SurfaceProps(engine.Api), moves,
                                                new ServerMoveData(), new NoSounds(), null, TestSides.Movement(engine),
                                                Frame, new NoPublish(), _env);
        return (commands, moves);
    }

    private static MoveFrameConstants Frame()
    {
        MovementParams p = Movement.Current;
        var c = new MoveConstants
        {
            MaxVelocity = 3500f, MaxSpeed = 320f, Friction = 4f, StopSpeed = 100f, Accelerate = 10f,
            AirAccelerate = 10f, AirSpeedCap = 30f, DuckSpeedScale = 0.33333333f, Optimizations = true,
            MaxClients = 2, TickInterval = 0.015f, View = p.View, HullMin = p.HullMin, HullMax = p.HullMax,
            DuckHullMin = p.DuckHullMin, DuckHullMax = p.DuckHullMax, DuckView = p.DuckView,
            ObsHullMin = p.ObsHullMin, ObsHullMax = p.ObsHullMax, DeadViewHeight = p.DeadViewHeight,
        };
        return new MoveFrameConstants(c, 21f, 0f, 1f, 600f);
    }

    // What PlayerCommandView was last handed, and how often (the fixangle facts).
    private static int s_viewCalls;
    private static float s_viewYaw;
    private static int s_viewFix;

    [UnmanagedCallersOnly]
    private static int BeginSnapped(void* self, uint player, void* facts)
    {
        var f = (PlayerCommandFacts*)facts;
        f->Health = 100;
        f->EntIndex = 2;
        f->ModelScale = 1f;
        f->VAngle = new Vector(0f, 90f, 0f);
        f->FixAngle = ServerCommandRules.FixAngleAbsolute;
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void View(void* self, uint player, void* angles, int fixAngle)
    {
        s_viewCalls++;
        s_viewYaw = ((float*)angles)[1];
        s_viewFix = fixAngle;
    }

    private ServerPlayerCommands SnappedStages()
    {
        _root.Live(MovementServer, nameof(ShimMovementServerFwd.PlayerCommandBegin),
                   (nint)(delegate* unmanaged<void*, uint, void*, int>)&BeginSnapped);
        _root.Live(MovementServer, nameof(ShimMovementServerFwd.PlayerCommandView),
                   (nint)(delegate* unmanaged<void*, uint, void*, int, void>)&View);
        var side = TestSides.Create(Side.Server);
        side.Attachment.Initialize();
        Assert.True(side.Attachment.AttachShims(_root.Bind(), out string why), why);
        s_viewCalls = 0;
        return Stack(new ServerModuleEngine(side)).Commands;
    }

    [Fact]
    public void AHumansAbsoluteFixAngleHoldsUntilTheEngineSendsIt()
    {
        ServerPlayerCommands stages = SnappedStages();
        var cmd = new UserCmd { CommandNumber = 1, Yaw = 30f };

        Assert.Equal(1, stages.Stage(Player, 0, 0, in cmd));

        Assert.Equal(0, s_viewCalls);
    }

    [Fact]
    public void ABotsAbsoluteFixAngleCountsAsDeliveredAndItsCommandTurnsIt()
    {
        // The engine never sends a fake client its fixangle
        // (it returns first), so without this
        // a snapped bot could never turn again.
        ServerPlayerCommands stages = SnappedStages();
        var cmd = new UserCmd { CommandNumber = 1, Yaw = 30f };

        Assert.Equal(1, stages.FakeClientStage(Player, 0, 0, in cmd));

        Assert.Equal((1, 30f, ServerCommandRules.FixAngleNone), (s_viewCalls, s_viewYaw, s_viewFix));
    }

    [Fact]
    public void ABotsCommandEndsAsTheSameHumanCommandDoes()
    {
        // One recorded command, run once as a human's (a packet queued, then
        // the player-command binding's tick base and loop) and once as a bot's
        // (the runner), each over its own stages and movement record on one
        // engine: the loop's counters and the movement it leaves agree.
        _root.Live(MovementServer, nameof(ShimMovementServerFwd.PlayerCommandBegin),
                   (nint)(delegate* unmanaged<void*, uint, void*, int>)&Begin);
        var side = TestSides.Create(Side.Server);
        side.Attachment.Initialize();
        Assert.True(side.Attachment.AttachShims(_root.Bind(), out string why), why);
        var engine = new ServerModuleEngine(side);

        var cmd = new UserCmd
        {
            CommandNumber = 41, TickCount = 660, Yaw = 90f, Pitch = 10f, ForwardMove = 400f, SideMove = -200f,
            Buttons = 1 << 1 /* IN_JUMP */,
        };

        (ServerPlayerCommands humanStages, ServerPlayerMoves humanMoves) = Stack(engine);
        var humanQueue = new PlayerCommandQueue(new RecordingLog());
        UserCmd h = cmd;
        int order;
        humanQueue.Queue(Player, &h, 1, 1, 0, &order, 1);
        // PlayerCommandsBinding.PlayerRunCommands' two calls (its For needs a
        // whole server module for the movement rules; the rules here are
        // stock's on both arms).
        humanStages.AdjustTickBase(Player, 1);
        humanQueue.Run(Player, 1, humanStages.Stage, null, null, null, false);
        float humanClock = _env.Raw->curtime;

        _env.Raw->curtime = 10f;
        _env.Raw->frametime = 0.015f;
        (ServerPlayerCommands botStages, ServerPlayerMoves botMoves) = Stack(engine);
        var botQueue = new PlayerCommandQueue(new RecordingLog());
        var runner = new PlayerCommandRunner(botQueue, botStages.Stage, botStages.AdjustTickBase, _env, null, null, null, () => false);
        runner.Queue(Player, in cmd);
        runner.RunQueued(Player);

        // The stimulus reached the subject: the command ran and moved.
        Assert.Equal(1, humanQueue.Counters().Ran);
        MoveState human = humanMoves.Find(Player)!.Move, bot = botMoves.Find(Player)!.Move;
        Assert.NotEqual(Vector.Zero, human.Velocity);

        Assert.Equal(humanQueue.Counters(), botQueue.Counters());
        Assert.Equal((human.Velocity, human.Flags, human.OldButtons, human.ViewAngles),
                     (bot.Velocity, bot.Flags, bot.OldButtons, bot.ViewAngles));
        Assert.Equal(humanMoves.Find(Player)!.TickBase, botMoves.Find(Player)!.TickBase);

        // And the runner put the frame's clock back where the human batch's
        // native caller would have; the binding alone does not.
        Assert.NotEqual(10f, humanClock);
        Assert.Equal(10f, _env.Raw->curtime);
    }
}

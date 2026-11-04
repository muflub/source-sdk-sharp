using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Ai.Hl2mp.Senses;
using SourceSharp.Ai.Hl2mp.Trees;
using SourceSharp.Ai.Tests.Hl2mpNav;
using SourceSharp.Ai.Tests.Nav;
using SourceSharp.Ai.Tests.Senses;
using SourceSharp.Ai.Tests.Trees;
using SourceSharp.MapFormats.Nav;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mp;

/// <summary>
/// The bots' drive steps on the AI's workers (plan_hl2mp_ai §2.7, user ruling
/// 2026-11-03): off the main thread when pooled, on it at <c>ai_sync 1</c>,
/// one at a time a bot, what comes of a late one, and a level ending with one
/// on its way. Real pools throughout: a fake one could not say which thread ran.
/// </summary>
public sealed class BotDriveLaneTests
{
    private static readonly ActorId Bot = new(2, 1);
    private static readonly (NavGraph Graph, int[,] Ids) Grid = Grids.Parse([.. Enumerable.Repeat(new string('.', 32), 32)]);
    private static readonly AiVector From = new(48f, 48f, 0f), Goal = new(900f, 900f, 0f);

    /// <summary>The players a step reads, recording the thread that read them and able to hold it there.</summary>
    private sealed class PlayersProbe : IReadOnlyList<NearbyPlayer>
    {
        private int _thread;
        public ManualResetEventSlim? Gate { get; set; }
        public int Thread => Volatile.Read(ref _thread);
        public int Reads { get; private set; }

        public int Count
        {
            get
            {
                Volatile.Write(ref _thread, Environment.CurrentManagedThreadId);
                Reads++;
                Gate?.Wait(TimeSpan.FromSeconds(20));
                return 0;
            }
        }

        public NearbyPlayer this[int index] => throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<NearbyPlayer> GetEnumerator() => Enumerable.Empty<NearbyPlayer>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static IntentCommand Brain(ActorId bot, Tick from)
        => new(bot, from, 1, from + IntentCommand.LifetimeTicks, new MoveIntent(MoveKind.Follow, MoveSpeed.Run, 1, Goal, default, default, default, default, false, 0),
               LookIntent.AlongPath, ActionIntent.None, default);

    private static DriveInput Input(int tick, IReadOnlyList<NearbyPlayer> players, AiVector? at = null, ActorId? bot = null)
        => new(Brain(bot ?? Bot, new Tick(tick)), at ?? From, new Tick(tick), Grid.Graph, new NavObstacleState(0), false, default, EntityHandle.None, [], players, false, BreakableSight.None);

    private static readonly BotSelfFacts Self = new(true, false, From, From + new AiVector(0f, 0f, 64f), default);

    private static BotDriveLanes Pooled(AiWorkerPool pool, int threads)
    {
        pool.Start(threads);
        var transport = new PooledTransport(pool);
        return new BotDriveLanes(() => transport, 8);
    }

    [Fact]
    public void PooledTheStepRunsOffTheCallingThread()
    {
        using var pool = new AiWorkerPool();
        BotDriveLanes lanes = Pooled(pool, 2);
        var probe = new PlayersProbe();
        Assert.True(lanes.Hand(Input(1, probe), Self));
        lanes.Settle();
        Assert.Equal(DriveTaken.Fresh, lanes.Take(Bot, out DriveOutput output, out _));
        Assert.NotEqual(0, probe.Thread);
        Assert.NotEqual(Environment.CurrentManagedThreadId, probe.Thread);
        Assert.Equal(MoveKind.Follow, output.Command.Move.Kind);
        Assert.Equal((0L, 0L), (lanes.StepsOnMain, output.Counters.BuildsOnMain));
        Assert.True(output.Counters.Builds > 0, "the first step built its field");
    }

    [Fact]
    public void InlineTheStepRunsOnTheCallerAndIsDoneAtOnce()
    {
        var transport = new InlineTransport();
        var lanes = new BotDriveLanes(() => transport, 8);
        var probe = new PlayersProbe();
        Assert.True(lanes.Hand(Input(1, probe), Self));
        Assert.False(lanes.Busy(Bot.Slot));
        Assert.Equal(Environment.CurrentManagedThreadId, probe.Thread);
        Assert.Equal(DriveTaken.Fresh, lanes.Take(Bot, out DriveOutput output, out _));
        Assert.Equal(1L, lanes.StepsOnMain);
        Assert.True(output.Counters.BuildsOnMain > 0, "built where it ran");
    }

    [Fact]
    public void ALaneHoldsOneStepAndALateOneIsNotQueuedBehindIt()
    {
        using var pool = new AiWorkerPool();
        BotDriveLanes lanes = Pooled(pool, 2);
        using var gate = new ManualResetEventSlim(false);
        var probe = new PlayersProbe { Gate = gate };
        Assert.True(lanes.Hand(Input(1, probe), Self));
        SpinWait.SpinUntil(() => probe.Thread != 0, TimeSpan.FromSeconds(10));

        Assert.True(lanes.Busy(Bot.Slot));
        Assert.False(lanes.Hand(Input(2, probe), Self));
        Assert.Equal(1L, lanes.Steps);
        Assert.Equal(DriveTaken.Late, lanes.Take(Bot, out _, out _));
        Assert.Equal(DriveTaken.Late, lanes.Take(Bot, out _, out _));
        Assert.Equal(2L, lanes.Late);

        gate.Set();
        lanes.Settle();
        Assert.Equal(DriveTaken.Fresh, lanes.Take(Bot, out DriveOutput output, out _));
        Assert.Equal(1, output.Command.FromTick.Value);
        Assert.Equal(DriveTaken.None, lanes.Take(Bot, out _, out _));
        Assert.Equal(1, probe.Reads);
    }

    /// <summary>A tick's steps wake a worker once for each few lanes, not once for each lane: a wake is the main thread's cost.</summary>
    [Fact]
    public void ATicksStepsWakeAWorkerForEachFewLanes()
    {
        using var pool = new AiWorkerPool();
        BotDriveLanes lanes = Pooled(pool, 2);
        lanes.Hold();
        var bots = new List<ActorId>();
        for (int b = 1; b <= 6; b++)
        {
            bots.Add(new ActorId(b, b));
            Assert.True(lanes.Hand(Input(1, new PlayersProbe(), bot: bots[^1]), Self));
        }

        Assert.Equal(0L, pool.Ran);
        Assert.All(bots, b => Assert.True(lanes.Busy(b.Slot)));
        lanes.Flush();
        lanes.Settle();
        Assert.True(SpinWait.SpinUntil(() => pool.Ran == 2, TimeSpan.FromSeconds(10)), $"{pool.Ran} wakes for six lanes");
        Assert.All(bots, b => Assert.Equal(DriveTaken.Fresh, lanes.Take(b, out _, out _)));
    }

    /// <summary>Another bot's lane goes on while one is held: one bot's late step is that bot's.</summary>
    [Fact]
    public void OneLateLaneHoldsUpNoOther()
    {
        using var pool = new AiWorkerPool();
        BotDriveLanes lanes = Pooled(pool, 2);
        using var gate = new ManualResetEventSlim(false);
        var held = new PlayersProbe { Gate = gate };
        Assert.True(lanes.Hand(Input(1, held), Self));
        SpinWait.SpinUntil(() => held.Thread != 0, TimeSpan.FromSeconds(10));
        var other = new ActorId(3, 2);
        Assert.True(lanes.Hand(Input(1, new PlayersProbe(), bot: other), Self));
        Assert.True(SpinWait.SpinUntil(() => !lanes.Busy(other.Slot), TimeSpan.FromSeconds(10)), "the other lane finished");
        Assert.Equal(DriveTaken.Fresh, lanes.Take(other, out _, out _));
        gate.Set();
        lanes.Settle();
    }

    [Fact]
    public void AStepThatThrowsIsCountedAndTheLaneGoesOn()
    {
        var transport = new InlineTransport();
        var lanes = new BotDriveLanes(() => transport, 8);
        Assert.True(lanes.Hand(Input(1, new Throwing()), Self));
        Assert.Equal(DriveTaken.Faulted, lanes.Take(Bot, out _, out _));
        Assert.Equal(1L, lanes.Faults);
        Assert.Contains("the step's fault", lanes.LastFault);
        Assert.True(lanes.Hand(Input(2, new PlayersProbe()), Self));
        Assert.Equal(DriveTaken.Fresh, lanes.Take(Bot, out _, out _));
    }

    private sealed class Throwing : IReadOnlyList<NearbyPlayer>
    {
        public int Count => throw new InvalidOperationException("the step's fault");
        public NearbyPlayer this[int index] => default;
        public IEnumerator<NearbyPlayer> GetEnumerator() => Enumerable.Empty<NearbyPlayer>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// §2.5: a level ending with a step running. The pool's stop waits for it,
    /// a step only queued is dropped and never runs, every lane is idle after,
    /// and nothing of the level is held.
    /// </summary>
    [Fact]
    public void ALevelEndingWithAStepInFlightJoinsItDropsTheQueuedAndLeavesTheLanesIdle()
    {
        var pool = new AiWorkerPool();
        BotDriveLanes lanes = Pooled(pool, 1);
        using var gate = new ManualResetEventSlim(false);
        var running = new PlayersProbe { Gate = gate };
        var other = new ActorId(3, 2);
        var queued = new PlayersProbe();
        Assert.True(lanes.Hand(Input(1, running), Self));
        SpinWait.SpinUntil(() => running.Thread != 0, TimeSpan.FromSeconds(10));
        Assert.True(lanes.Hand(Input(1, queued, bot: other), Self));

        // Released only after the stop has begun: Stop must wait for it.
        var release = new Thread(() =>
        {
            Thread.Sleep(50);
            gate.Set();
        });
        release.Start();
        int discarded = pool.Stop();
        release.Join();
        Assert.Equal(1, discarded);
        Assert.Equal(0, pool.Alive);
        Assert.Equal(0, queued.Reads);

        Assert.True(lanes.Busy(other.Slot), "the control: the dropped step's lane still says so until told");
        Assert.Equal(1, lanes.Abandon());
        Assert.False(lanes.Busy(Bot.Slot));
        Assert.False(lanes.Busy(other.Slot));
        Assert.Equal(DriveTaken.None, lanes.Take(other, out _, out _));
        Assert.Null(lanes.RouteOf(Bot.Slot));
        Assert.Null(lanes.DriverOf(Bot.Slot)!.FollowerOf(Bot.Slot));
        pool.Dispose();
    }

    /// <summary>A pool stopped between levels is no reason to leave a step waiting for good: it runs where it was handed.</summary>
    [Fact]
    public void AStoppedPoolsStepRunsOnTheCaller()
    {
        var pool = new AiWorkerPool();
        var transport = new PooledTransport(pool);
        var lanes = new BotDriveLanes(() => transport, 8);
        var probe = new PlayersProbe();
        Assert.True(lanes.Hand(Input(1, probe), Self));
        Assert.Equal(Environment.CurrentManagedThreadId, probe.Thread);
        Assert.Equal(1L, lanes.Refused);
        Assert.Equal(DriveTaken.Fresh, lanes.Take(Bot, out _, out _));
        pool.Dispose();
    }

    /// <summary>
    /// A body stopped dead reports stuck, and the rebuild that comes of it runs
    /// on the worker with the step (measured: about 2 ms a frame when it was on
    /// the main thread through ReportStuck); the line comes back in the result.
    /// </summary>
    [Fact]
    public void AStuckRebuildRunsOnTheWorkerAndItsLineComesBackInTheResult()
    {
        using var pool = new AiWorkerPool();
        BotDriveLanes lanes = Pooled(pool, 2);
        var probe = new PlayersProbe();
        string? line = null;
        long builds = 0, onMain = 0;
        for (int t = 1; t <= BrainDriver.BodyBlockTicks + 3 && line is null; t++)
        {
            Assert.True(lanes.Hand(Input(t, probe), Self));
            lanes.Settle();
            Assert.Equal(DriveTaken.Fresh, lanes.Take(Bot, out DriveOutput output, out _));
            if (t == 1)
                builds = output.Counters.Builds;
            line = output.StuckLine;
            onMain = output.Counters.BuildsOnMain;
            if (line is not null)
                Assert.True(output.Counters.Builds > builds, $"the report rebuilt: {output.Counters.Builds} against {builds}");
        }

        Assert.NotNull(line);
        Assert.Contains("is stuck at", line);
        Assert.Equal(0L, onMain);
        Assert.Equal(1L, lanes.Totals.Stucks);
        Assert.NotEqual(Environment.CurrentManagedThreadId, probe.Thread);
    }

    /// <summary>What the main thread learns after a step is handed over is the next step's: the lane's copy stays as it was handed.</summary>
    [Fact]
    public void AStepReadsTheObstaclesAsTheyWereHanded()
    {
        var (g, _) = Grids.Parse(".D.");
        var live = new NavObstacleState(g.ObstacleCount) { OpenDoors = false };
        var copy = new NavObstacleState(g.ObstacleCount);
        copy.CopyFrom(live);
        live.Set(0, true);
        Assert.False(copy.Blocking(0));
        copy.CopyFrom(live);
        Assert.True(copy.Blocking(0));
        byte[] taken = new byte[g.CellCount];
        live.Cells = new NavCellBlocks(taken);
        Assert.NotSame(live.Cells, copy.Cells);
        copy.CopyFrom(live);
        Assert.Same(live.Cells, copy.Cells);
    }

    /// <summary>The way a brain's bot is going, for the web UI: a step on its way is never read through; its copy is what is answered.</summary>
    [Fact]
    public void AnInspectorReadsAPublishedRouteWhileAStepRuns()
    {
        using var pool = new AiWorkerPool();
        BotDriveLanes lanes = Pooled(pool, 2);
        var probe = new PlayersProbe();
        Assert.True(lanes.Hand(Input(1, probe), Self));
        lanes.Settle();
        Assert.Equal(DriveTaken.Fresh, lanes.Take(Bot, out _, out _));
        (AiVector Target, AiVector[] Way) idle = lanes.RouteOf(Bot.Slot)!.Value;
        Assert.True(idle.Way.Length >= 2, "the follower's way, with no step on its way");
        Assert.Equal(Goal.X, idle.Target.X);

        using var gate = new ManualResetEventSlim(false);
        var held = new PlayersProbe { Gate = gate };
        Assert.True(lanes.Hand(Input(2, held, From + new AiVector(32f, 0f, 0f)), Self));
        SpinWait.SpinUntil(() => held.Thread != 0, TimeSpan.FromSeconds(10));
        Assert.True(lanes.Busy(Bot.Slot));
        Assert.Same(idle.Way, lanes.RouteOf(Bot.Slot)!.Value.Way);

        gate.Set();
        lanes.Settle();
        // Asked while it ran, the step published a new copy as it ended.
        Assert.NotSame(idle.Way, lanes.RouteOf(Bot.Slot)!.Value.Way);
    }

    // ---- The whole binding: Hl2mpBots with brains, a fake engine and a real pool -------------------

    private sealed class Quiet : ILog
    {
        public readonly List<string> Lines = new();
        public void Msg(string message) => Lines.Add(message);
        public void Warning(string message) => Lines.Add(message);
    }

    private sealed class Navigation(LoadedNav nav) : IHl2mpNavigation
    {
        public LoadedNav? Current => nav;
        public string? Refusal => null;
        public NavObstacleState? Obstacles { get; } = new(0);
        public NavObstacleState? ObstaclesFor(EntityHandle player) => Obstacles;
        public NavSight? Sight => null;
        public ChokeSlot? Chokes => null;
    }

    private static readonly Lazy<LoadedNav> GridNav = new(() =>
    {
        byte[] bsp = TinyBsp.Make(TinyBsp.Stock);
        LoadedNav course = NavLoader.Load("course", p => p.EndsWith(".bsp", StringComparison.Ordinal) ? bsp
                                                         : NavFixtures.Course(Nav3dBspBinding.Of(bsp)).Write(), null).Nav!;
        return course with { Nav = new DerivedNav(Grid.Graph, [], [], [], 0) };
    });

    private sealed class Names : IBotNameSource
    {
        public BotNamePools? Pools => null;
        public string Refusal => "no names";
    }

    public class Defaults : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.ReturnType is { IsValueType: true } t && t != typeof(void) ? Activator.CreateInstance(t) : null;
    }

    /// <summary>The bots as bodies on the floor: each command moves its player as the movement code would at run speed.</summary>
    private sealed class Bodies : IBotPlayers, IPlayerCommandRunner
    {
        public sealed class Body
        {
            public EntityHandle Handle;
            public int Slot;
            public AiVector Origin;
            public AiVector View;
        }

        public readonly List<BotPlayer> Made = new();
        public readonly Dictionary<EntityHandle, Body> ByHandle = new();
        public readonly List<(int Slot, UserCmd Cmd)> Commands = new();
        private readonly Dictionary<EntityHandle, List<UserCmd>> _queued = new();

        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal)
        {
            int slot = Made.Count + 2;
            var handle = new EntityHandle((uint)(slot | (3 << ActorDirectory.EntryBits)));
            ByHandle[handle] = new Body { Handle = handle, Slot = slot, Origin = new AiVector(48f + 64f * slot, 48f + 32f * slot, 0f) };
            Made.Add(new BotPlayer(handle, slot, name));
            refusal = "";
            return handle;
        }

        public bool IsBot(EntityHandle player) => ByHandle.ContainsKey(player);
        public bool Kick(EntityHandle bot, string reason) => Made.RemoveAll(b => b.Player == bot) > 0;
        public IReadOnlyList<BotPlayer> Bots => Made;

        public void Queue(EntityHandle player, in UserCmd cmd)
        {
            if (!_queued.TryGetValue(player, out List<UserCmd>? q))
                _queued[player] = q = new List<UserCmd>();
            q.Add(cmd);
        }

        public int RunQueued(EntityHandle player)
        {
            if (!_queued.TryGetValue(player, out List<UserCmd>? q))
                return 0;
            Body b = ByHandle[player];
            foreach (UserCmd cmd in q)
            {
                Commands.Add((b.Slot, cmd));
                float yaw = cmd.Yaw * (MathF.PI / 180f);
                float fx = MathF.Cos(yaw), fy = MathF.Sin(yaw);
                float step = 3f / BotCommandApplier.FullMove;
                b.Origin += new AiVector((cmd.ForwardMove * fx + cmd.SideMove * fy) * step, (cmd.ForwardMove * fy - cmd.SideMove * fx) * step, 0f);
                b.View = new AiVector(cmd.Pitch, cmd.Yaw, 0f);
            }

            int ran = q.Count;
            q.Clear();
            return ran;
        }

        private static Vector V(AiVector v) => new(v.X, v.Y, v.Z);

        public IEngineEntities Entities() => FakeEngine.Of<IEngineEntities>((m, a) => m.Name switch
        {
            "GetOrigin" => V(ByHandle[(EntityHandle)a[0]!].Origin),
            "GetEyePosition" => V(ByHandle[(EntityHandle)a[0]!].Origin + new AiVector(0f, 0f, 64f)),
            "GetEyeAngles" => V(ByHandle[(EntityHandle)a[0]!].View),
            _ => throw FakeEngine.Unasked(m),
        });

        public IEnginePlayers Players() => FakeEngine.Of<IEnginePlayers>((m, a) => m.Name switch
        {
            "IsAlive" => true,
            "GetObserverMode" => ObserverMode.None,
            "GetPunch" => default(Vector),
            _ => throw FakeEngine.Unasked(m),
        });
    }

    /// <summary>A match of a few bots over the grid: brains with their trees, drive steps, commands run as bodies.</summary>
    private sealed class Match : IDisposable
    {
        private static readonly string[] Presets = ["easy", "normal", "hard", "expert", "normal", "hard"];
        public readonly Bodies World = new();
        public readonly Hl2mpAiSession Ai;
        public readonly Hl2mpBots Bots;
        public readonly Quiet Log = new();
        private readonly Navigation _nav = new(GridNav.Value);
        private readonly bool _pooled;
        private int _tick;

        public Match(int threads, AiMainBudget? budget = null)
        {
            _pooled = threads > 0;
            Ai = new Hl2mpAiSession(_nav, WeaponCatalog.Stock(), null!, null!, Log,
                                    DeclaredConVars.Fresh<AiThreadsConVar>(), DeclaredConVars.Fresh<AiSyncConVar>(),
                                    DeclaredConVars.Fresh<WeaponRespawnTimeConVar>(), DeclaredConVars.Fresh<ItemRespawnTimeConVar>());
            IEngineFrame frame = FakeEngine.Of<IEngineFrame>((m, _) => m.Name == "TickCount" ? _tick : throw FakeEngine.Unasked(m));
            Bots = new Hl2mpBots(World, World, World.Players(), World.Entities(), WeaponCatalog.Stock(), frame, _nav, DispatchProxy.Create<IBotInventory, Defaults>(),
                                 new Names(), new ScoreboardTags(), Log, Ai, null, budget);
            var map = new MapKnowledge("grid", [new ItemSpawn(0, "item_healthkit", ItemKind.Health, new AiVector(880f, 880f, 0f), 30f)], [], Duel.Weapons);
            Ai.Session.World = new MemoryWorld(map, Grid.Graph, Hl2mpAiSession.TickSeconds, id => id.ToString());
            Ai.Session.Curves = SourceSharp.Ai.Tests.Skills.Curves.Game();
            Ai.Session.Deciders = setup => new Hl2mpDecider(setup);
            if (_pooled)
            {
                Ai.Pool.Start(threads);
                Ai.Session.Transport = new PooledTransport(Ai.Pool);
            }

            for (int i = 0; i < Presets.Length; i++)
            {
                SkillPresets.TryGet(Presets[i], out SkillProfile? p);
                Assert.Null(Bots.Add($"B{i}", p!, i + 1, out ActorId id));
                Ai.Session.AddBot(id, (ulong)(1000 + i), [.. p!.Levels.Select(l => (byte)l)]);
            }
        }

        private static ActorId Opponent(int bot) => new(40 + bot, 0x4000_0000 | (40 + bot));

        /// <summary>One tick, as the game runs it; each step done before the next tick needs it, whichever thread ran it.</summary>
        public void Tick()
        {
            _tick++;
            Bots.OnPreThink(Hl2mpAiSession.TickSeconds);
            Ai.Drives.Settle();
            Publish();
            Ai.Session.Drain(wait: true);
        }

        /// <summary>The main thread's part of a tick alone: what a late step is late for.</summary>
        public void PreThink()
        {
            _tick++;
            Bots.OnPreThink(Hl2mpAiSession.TickSeconds);
        }

        /// <summary>The tick's senses: each bot sees an opponent ahead of it for a while, and loses it.</summary>
        private void Publish()
        {
            var tick = new Tick(_tick);
            AiSnapshot snap = Ai.Session.Snapshots.BeginWrite(tick);
            int phase = _tick % 400;
            foreach (Hl2mpBot bot in Bots.Bots)
            {
                Bodies.Body b = World.ByHandle[Bots.Applier.PlayerOf(bot.Id)];
                var pos = new AiVector(b.Origin.X + 300f, b.Origin.Y + 200f, 0f);
                if (phase == 60)
                    Ai.Session.Push(AiEvent.SightGained(bot.Id, tick, Opponent(bot.Id.Slot), pos, AiVector.Zero, 1f));
                else if (phase == 200)
                    Ai.Session.Push(AiEvent.SightLost(bot.Id, tick, Opponent(bot.Id.Slot), pos, new AiVector(100f, 40f, 0f)));
                PerceivedView v = snap.Add(bot.Id, new SelfState(b.Origin, default, new AiAngles(b.View.X, b.View.Y, 0f), 100, 0, Duel.Smg1, 30, 90, SelfFlags.Alive | SelfFlags.Grounded));
                if (phase >= 60 && phase < 200)
                    v.AddSighted(new ActorFacts(Opponent(bot.Id.Slot), pos, AiVector.Zero, Duel.Smg1, ActorActivity.None, 1f));
            }

            Ai.Session.Publish(snap);
        }

        public void Dispose()
        {
            Ai.Pool.Stop();
            Ai.Pool.Dispose();
        }
    }

    private static (List<(int Slot, UserCmd Cmd)> Commands, BotDriveLanes Lanes) Played(int threads, int ticks)
    {
        using var match = new Match(threads);
        for (int t = 0; t < ticks; t++)
            match.Tick();
        return (match.World.Commands, match.Ai.Drives);
    }

    private static string Text(in UserCmd c)
        => $"#{c.CommandNumber} tick {c.TickCount} view {c.Pitch},{c.Yaw} move {c.ForwardMove},{c.SideMove} buttons {c.Buttons} weapon {c.WeaponSelect}";

    private static bool Same(in UserCmd a, in UserCmd b) => Text(in a) == Text(in b);

    /// <summary>
    /// §2.6 for the commands themselves: with every step done in its time, the
    /// usercmds of a run are the same, field for field, inline and on a pool of
    /// 1, 2 and 4 threads; so what a thread count can change is when a step is
    /// done, which is the late tick's concern, never what it says.
    /// </summary>
    [Fact]
    public void ARunsCommandsAreTheSameInlineAndAtOneTwoAndFourThreads()
    {
        const int Ticks = 500;
        (List<(int Slot, UserCmd Cmd)> inline, BotDriveLanes inlineLanes) = Played(0, Ticks);
        // The control: it walked, turned and pressed fire, all of which a difference could be in.
        Assert.True(inline.Count >= 6 * (Ticks - 2), $"{inline.Count} commands");
        Assert.Contains(inline, c => c.Cmd.ForwardMove != 0f);
        Assert.True(inline.Select(c => c.Cmd.Yaw).Distinct().Count() > 50, "the view moved");
        Assert.Contains(inline, c => (c.Cmd.Buttons & (int)InputButtons.Attack) != 0);
        Assert.True(inlineLanes.StepsOnMain > 0, "inline, the steps ran on the caller");

        foreach (int threads in new[] { 1, 2, 4 })
        {
            (List<(int Slot, UserCmd Cmd)> pooled, BotDriveLanes lanes) = Played(threads, Ticks);
            Assert.Equal(inline.Count, pooled.Count);
            for (int i = 0; i < inline.Count; i++)
                Assert.True(inline[i].Slot == pooled[i].Slot && Same(inline[i].Cmd, pooled[i].Cmd), $"{threads} thread(s), command {i}: inline {Text(inline[i].Cmd)}, pooled {Text(pooled[i].Cmd)}");
            Assert.Equal(0L, lanes.Late);
        }
    }

    /// <summary>
    /// What keeps bot logic off the main thread, measured: a pooled run of the
    /// whole binding ran every step and every flow field on a worker. Moved back
    /// to the main thread, a step or a build turns this red.
    /// </summary>
    [Fact]
    public void APooledRunBuildsItsFieldsAndDrivesOffTheMainThread()
    {
        (_, BotDriveLanes lanes) = Played(2, 200);
        Assert.True(lanes.Steps > 500, $"{lanes.Steps} steps");
        Assert.True(lanes.Totals.Builds >= 6, $"{lanes.Totals.Builds} builds");
        Assert.Equal(0L, lanes.StepsOnMain);
        Assert.Equal(0L, lanes.Totals.BuildsOnMain);

        (_, BotDriveLanes inline) = Played(0, 200);
        Assert.InRange(inline.Steps - inline.StepsOnMain, 0, 6);   // the last tick's, handed and not yet taken
        Assert.True(inline.Totals.BuildsOnMain > 0, "the control: inline they count");
    }

    /// <summary>A command every tick for every bot, whatever the lanes are doing: one late step costs no bot a tick, and no command number repeats.</summary>
    [Fact]
    public void ALateStepStillSendsOneCommandATick()
    {
        using var match = new Match(2);
        for (int t = 0; t < 30; t++)
            match.Tick();

        // Every step that walks reads the players: held there, it will not end.
        using var gate = new ManualResetEventSlim(false);
        var held = new PlayersProbe { Gate = gate };
        match.Bots.Players = held;
        Hl2mpBot? stuck = null;
        for (int t = 0; t < 400 && stuck is null; t++)
        {
            match.PreThink();
            SpinWait.SpinUntil(() => held.Thread != 0 && match.Bots.Bots.Any(b => match.Ai.Drives.Busy(b.Id.Slot)), TimeSpan.FromMilliseconds(5));
            stuck = match.Bots.Bots.Where(b => match.Ai.Drives.Busy(b.Id.Slot)).Cast<Hl2mpBot?>().FirstOrDefault();
        }

        Assert.NotNull(stuck);
        int slot = stuck.Value.Id.Slot, before = match.World.Commands.Count;
        long late = match.Ai.Drives.Late, repeats = match.Bots.Applier.Repeats;
        for (int t = 0; t < 5; t++)
            match.PreThink();
        Assert.True(match.Ai.Drives.Late >= late + 5, "its step was late every tick");
        Assert.True(match.Bots.Applier.Repeats >= repeats + 5, "and the bot went on");
        gate.Set();
        match.Ai.Drives.Settle();

        (int Number, int Tick)[] commands = [.. match.World.Commands.Skip(before).Where(c => c.Slot == slot).Select(c => (c.Cmd.CommandNumber, c.Cmd.TickCount))];
        Assert.Equal(5, commands.Length);
        Assert.Equal(Enumerable.Range(commands[0].Number, 5), commands.Select(c => c.Number));
        Assert.Equal(Enumerable.Range(commands[0].Tick, 5), commands.Select(c => c.Tick));
    }

    /// <summary>
    /// The whole binding under a starved budget, on a real pool: the floor gone,
    /// the bound holding, each bot still sends one command for every tick, in
    /// order, within the bound, and no step goes without its bot's facts.
    /// </summary>
    [Fact]
    public void UnderAStarvedBudgetEveryBotsCommandsRunInOrderWithinTheBound()
    {
        using var globals = new TestGlobals();
        AiBudgetFloorConVar floor = DeclaredConVars.FreshAt<AiBudgetFloorConVar>("0");
        var budget = new AiMainBudget(globals, DeclaredConVars.FreshAt<FpsMaxConVar>("0"),
                                      FakeEngine.Of<IEngineHost>((m, _) => m.Name == "get_IsDedicatedServer" ? false : throw FakeEngine.Unasked(m)),
                                      DeclaredConVars.FreshAt<AiBudgetConVar>("1"), floor, DeclaredConVars.Fresh<AiBudgetWaitConVar>());
        using var match = new Match(2, budget);
        for (int t = 0; t < 300; t++)
        {
            globals.Frame++;
            match.Tick();
        }

        Assert.True(budget.Measure.Deferrals > 100, $"the stimulus: {budget.Measure.Deferrals} deferrals");
        Assert.True(budget.Measure.Forced > 0, "the bound forced runs");
        foreach (Hl2mpBot bot in match.Bots.Bots)
        {
            (int Number, int Tick)[] its = [.. match.World.Commands.Where(c => c.Slot == bot.Id.Slot).Select(c => (c.Cmd.CommandNumber, c.Cmd.TickCount))];
            Assert.InRange(its.Length, 300 - 3, 300);
            Assert.Equal(Enumerable.Range(its[0].Number, its.Length), its.Select(c => c.Number));
            Assert.Equal(Enumerable.Range(its[0].Tick, its.Length), its.Select(c => c.Tick));
        }
    }

    /// <summary>
    /// <c>ai_sync 1</c> ignores the budget: the same starved budget changes no
    /// command of an inline run, whatever it would have put off.
    /// </summary>
    [Fact]
    public void AtAiSyncABudgetChangesNoCommand()
    {
        const int Ticks = 300;
        List<(int Slot, UserCmd Cmd)> plain = Played(0, Ticks).Commands;
        using var globals = new TestGlobals();
        AiBudgetFloorConVar floor = DeclaredConVars.FreshAt<AiBudgetFloorConVar>("0");
        var budget = new AiMainBudget(globals, DeclaredConVars.FreshAt<FpsMaxConVar>("0"),
                                      FakeEngine.Of<IEngineHost>((m, _) => m.Name == "get_IsDedicatedServer" ? false : throw FakeEngine.Unasked(m)),
                                      DeclaredConVars.FreshAt<AiBudgetConVar>("1"), floor, DeclaredConVars.Fresh<AiBudgetWaitConVar>());
        using var match = new Match(0, budget);
        for (int t = 0; t < Ticks; t++)
        {
            globals.Frame++;
            match.Tick();
        }

        Assert.Equal((0L, 0L), (budget.Measure.Deferrals, budget.Measure.Forced));
        Assert.Equal(plain.Count, match.World.Commands.Count);
        for (int i = 0; i < plain.Count; i++)
            Assert.True(plain[i].Slot == match.World.Commands[i].Slot && Same(plain[i].Cmd, match.World.Commands[i].Cmd), $"command {i}");
    }

    /// <summary>A bot gone while its step runs: the lane is dropped, the step finishes on its own, and nothing it says reaches anyone.</summary>
    [Fact]
    public void ABotGoneWhileItsStepRunsLeavesNoLane()
    {
        using var pool = new AiWorkerPool();
        BotDriveLanes lanes = Pooled(pool, 2);
        using var gate = new ManualResetEventSlim(false);
        var held = new PlayersProbe { Gate = gate };
        Assert.True(lanes.Hand(Input(1, held), Self));
        SpinWait.SpinUntil(() => held.Thread != 0, TimeSpan.FromSeconds(10));
        lanes.Forget(Bot.Slot);
        Assert.False(lanes.Busy(Bot.Slot));
        Assert.Equal(DriveTaken.None, lanes.Take(Bot, out _, out _));
        gate.Set();
        Assert.Equal(0, pool.Stop() - 0);
        Assert.Equal(0L, pool.Faults);
    }
}

using System.Diagnostics;
using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.MapFormats.Bsp;
using SourceSharp.MapFormats.Bsp.Structs;

using TF2.Descent.Ai.Level;
using TF2.Descent.Server.Horde.Motion;

namespace TF2.Descent.Server.Horde.Nav;

/// <summary><c>tf2_descent_motion_sync</c>: 1 steps the robot bodies on the game thread against the engine; 0, the default, on a worker against the level's voxels once they are ready.</summary>
[ConVar("tf2_descent_motion_sync", "0",
        Help = "1: the robot bodies' movement steps run on the game thread against the engine's world (InlineMotionStepper); 0: on a worker against the level's nav voxels once they are ready (WorkerMotionStepper), the engine's world until then. Read each tick.")]
public sealed class HordeMotionSyncConVar : BoolConVar { }

/// <summary>Where a level's navigation stands.</summary>
public enum HordeNavState : byte
{
    /// <summary>No level, or the level has gone.</summary>
    None = 0,

    /// <summary>The graph is being derived off the game thread.</summary>
    Deriving,

    /// <summary>The graph is derived; the game thread is finding the static props the file does not hold, a few cells a tick.</summary>
    Probing,

    /// <summary>The graph is the brains' (<see cref="HordePaths.Epoch"/> non-zero).</summary>
    Ready,

    /// <summary>The level ships no <c>.nav3d</c>: the room route and the engine's steps for the whole level.</summary>
    Missing,

    /// <summary>The file could not be derived: as missing.</summary>
    Failed,
}

/// <summary>
/// A level's navigation for the horde (plan_descent_logic 3.6, 3.7). At the level's start
/// (<see cref="Begin"/>, on the game thread) the level's <c>.nav3d</c> and <c>.bsp</c> are read,
/// and a thread of its own derives the graph and the voxel columns
/// (<see cref="HordeNavData.Derive"/>) and lists the cells near the bsp's solid static props; it
/// ends when that is done. The game thread never waits for it: each <see cref="Tick"/> it looks
/// whether the work is done, and then tests those cells against the engine's world, a few a tick
/// (<see cref="ProbesPerTick"/>), blocking each a robot standing on it would be inside a prop
/// (the file holds the room's brushes, not its props). Then the graph is handed to the brains'
/// paths (<see cref="HordePaths.Ready"/>). Until then robots walk the room route.
///
/// <para>
/// <b>The motion's stepper</b> is chosen each tick: the engine's world on the game thread
/// (<see cref="InlineMotionStepper"/>) under <c>tf2_descent_motion_sync 1</c> or while the
/// voxels are not ready, else a worker over the voxel columns (<see cref="WorkerMotionStepper"/>
/// over <see cref="VoxelMotionWorld"/>). <see cref="End"/> lets the level's work go: a derivation
/// still running finishes and its result is dropped (its generation is old); the worker stepper
/// is stopped.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class HordeNav(IEngineFiles files, RobotMotion motion, IMotionWorld world, HordeMotionSyncConVar sync, IHordeClock clock, ILog log)
    : IDisposable
{
    /// <summary>Static-prop cells tested against the engine's world a tick: about 2 us each in a debug build (11,067 on depth 1 seed 1 took 14 to 27 ms in all), so a level is probed in under a second.</summary>
    public const int ProbesPerTick = 256;

    /// <summary>How far round a static prop's origin its cells are tested.</summary>
    public const float PropReach = 112f;

    /// <summary>The box a cell is tested with: a robot's hull a little narrower, from above a step to its shoulders (only flat cells are tested).</summary>
    public static Vector ProbeMins => new(-20f, -20f, 19f);

    public static Vector ProbeMaxs => new(20f, 20f, 72f);

    private readonly object _gate = new();
    private int _generation;
    private int _alive;
    private HordeNavData? _derived;
    private int[]? _candidates;
    private string? _failure;
    private HordeNavData? _data;
    private HordeLevel? _level;
    private byte[]? _blocks;
    private int _probed;
    private long _begunTick, _readyTick;
    private double _probeMicros;
    private WorkerMotionStepper? _worker;
    private string _map = "";

    /// <summary>The brains' paths: one object for the process, given each level's graph when it is ready.</summary>
    public HordePaths Paths { get; } = new();

    public HordeNavState State { get; private set; }

    /// <summary>The level's navigation once derived (the voxels are ready then; the graph is the brains' only at <see cref="HordeNavState.Ready"/>).</summary>
    public HordeNavData? Data => _data;

    /// <summary>
    /// What derives a level's navigation from its <c>.nav3d</c> and lists its prop cells: the
    /// thread's whole work. A fact replaces it to hold the work at a gate.
    /// </summary>
    public Func<byte[], byte[]?, (HordeNavData Nav, int[] Candidates)> Work { get; set; } = DeriveAndList;

    /// <summary>Levels begun, derivations finished (all levels), and those whose level had gone by then.</summary>
    public int Levels { get; private set; }

    public int Derived { get; private set; }

    public int DroppedStale { get; private set; }

    /// <summary>Threads started for derivations that have not ended (one still running after its level went counts).</summary>
    public int ThreadsAlive => Volatile.Read(ref _alive);

    /// <summary>Server ticks from the level's start to the graph's hand-over.</summary>
    public long TicksToReady => State == HordeNavState.Ready ? _readyTick - _begunTick : -1;

    public int Candidates => _candidates?.Length ?? 0;

    public int Probed => _probed;

    public int Blocked => _data?.BlockedCells ?? 0;

    public double ProbeMicros => _probeMicros;

    public string Map => _map;

    public string? Failure => _failure;

    /// <summary>The stepper the motion runs now, by name.</summary>
    public string StepperName => motion.Stepper is WorkerMotionStepper ? "worker (voxels)" : "inline (engine)";

    /// <summary>The level <paramref name="map"/> begins: its files are read here, the work starts on its own thread.</summary>
    public void Begin(string map, HordeLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);
        End();
        Levels++;
        _map = map;
        _level = level;
        _begunTick = clock.Tick;
        byte[]? nav = files.ReadGameFileBytes($"maps/{map}.nav3d");
        if (nav is null)
        {
            State = HordeNavState.Missing;
            log.Msg($"[TF2Descent/Horde] nav: maps/{map}.nav3d is missing: robots walk the room route and step on the game thread\n");
            return;
        }

        byte[]? bsp = files.ReadGameFileBytes($"maps/{map}.bsp");
        int generation;
        lock (_gate)
        {
            generation = ++_generation;
            _derived = null;
            _candidates = null;
            _failure = null;
        }

        State = HordeNavState.Deriving;
        var thread = new Thread(() => Run(generation, nav, bsp)) { IsBackground = true, Name = "tf2_descent nav" };
        Interlocked.Increment(ref _alive);
        thread.Start();
    }

    private void Run(int generation, byte[] nav, byte[]? bsp)
    {
        HordeNavData? derived = null;
        int[]? candidates = null;
        string? failure = null;
        try
        {
            try
            {
                (derived, candidates) = Work(nav, bsp);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // Anything the file makes the derivation throw is the level's navigation failing, never the server's thread dying.
                failure = $"{e.GetType().Name}: {e.Message}";
            }

            lock (_gate)
            {
                if (generation != _generation)
                {
                    DroppedStale++;
                    return;
                }

                _derived = derived;
                _candidates = candidates;
                _failure = failure;
            }
        }
        finally
        {
            Interlocked.Decrement(ref _alive);
        }
    }

    /// <summary>One server tick: takes a finished derivation, probes props, hands the graph over, and picks the motion's stepper.</summary>
    public void Tick()
    {
        if (State == HordeNavState.Deriving)
            TakeDerived();
        if (State == HordeNavState.Probing)
            Probe();
        PickStepper();
    }

    private void TakeDerived()
    {
        HordeNavData? derived;
        string? failure;
        lock (_gate)
        {
            derived = _derived;
            failure = _failure;
            if (derived is null && failure is null)
                return;
            _derived = null;
        }

        Derived++;
        if (derived is null)
        {
            State = HordeNavState.Failed;
            log.Warning($"[TF2Descent/Horde] nav: maps/{_map}.nav3d cannot be derived ({failure}): robots walk the room route and step on the game thread\n");
            return;
        }

        _data = derived;
        _blocks = new byte[derived.Graph.CellCount];
        _probed = 0;
        _probeMicros = 0;
        State = HordeNavState.Probing;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Horde] nav: {_map} derived in {derived.DeriveMs:0} ms (+{derived.ColumnsMs:0} ms voxels) on its own thread, {clock.Tick - _begunTick} ticks after the level began: {derived.Graph.CellCount} cells, {derived.Graph.EdgeCount} edges, {derived.Graph.ObstacleCount} obstacles; voxels ready, {Candidates} static-prop cells to test\n"));
    }

    private void Probe()
    {
        var data = _data!;
        int[] candidates = _candidates ?? [];
        long t0 = Stopwatch.GetTimestamp();
        int end = Math.Min(candidates.Length, _probed + ProbesPerTick);
        for (; _probed < end; _probed++)
        {
            int cell = candidates[_probed];
            NavCell c = data.Graph.Cell(cell);
            var at = new Vector((c.MinX + c.MaxX) * 0.5f, (c.MinY + c.MaxY) * 0.5f, c.FloorZ);
            var t = world.Sweep(at, at, ProbeMins, ProbeMaxs, EntityHandle.None);
            if (t.StartSolid && t.EntityIndex == 0)
                _blocks![cell] = NavCellBlocks.Blocked;
        }

        _probeMicros += Stopwatch.GetElapsedTime(t0).TotalMicroseconds;
        if (_probed < candidates.Length)
            return;

        _data = data.WithBlocks(_blocks!);
        Paths.Ready(_data, _level!);
        State = HordeNavState.Ready;
        _readyTick = clock.Tick;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Horde] nav ready: {_map}, {_readyTick - _begunTick} ticks after the level began; {_data.BlockedCells} of {candidates.Length} static-prop cells blocked ({_probeMicros:0} us of probes on the game thread), epoch {Paths.Epoch:x8}; robots plan on the graph\n"));
    }

    private void PickStepper()
    {
        if (!motion.Begun)
            return;
        bool voxels = !sync.Value && _data is { } data;
        if (voxels && _worker is null)
        {
            _worker = new WorkerMotionStepper(GameMovementStep.Create(new VoxelMotionWorld(_data!.World), motion.TickSeconds), motion.Capacity, new ThreadCpuClock());
        }

        IMotionStepper? want = voxels ? _worker : null;
        if (!ReferenceEquals(motion.Chosen, want))
            motion.Use(want);
    }

    /// <summary>The level is going: the graph is let go, a derivation still running will be dropped, the worker stepper stops.</summary>
    public void End()
    {
        lock (_gate)
        {
            _generation++;
            _derived = null;
            _candidates = null;
        }

        Paths.Clear();
        if (motion.Begun && motion.Chosen is not null)
            motion.Use(null);
        _worker?.Dispose();
        _worker = null;
        _data = null;
        _level = null;
        _blocks = null;
        State = HordeNavState.None;
    }

    public void Dispose()
    {
        End();
        Paths.Dispose();
    }

    /// <summary>
    /// The thread's work: the graph and voxels from the <c>.nav3d</c>, and the cells within
    /// <see cref="PropReach"/> of a solid static prop of the <c>.bsp</c> (none without one), in
    /// cell order, leaving out cells on a slope or a stair (a prop stands on a flat floor, and a
    /// stair's riser would answer the probe as a prop).
    /// </summary>
    public static (HordeNavData Nav, int[] Candidates) DeriveAndList(byte[] nav, byte[]? bsp)
    {
        HordeNavData data = HordeNavData.Derive(nav);
        return (data, bsp is null ? [] : PropCells(data, bsp));
    }

    /// <summary>The cells near the bsp's solid static props (see <see cref="DeriveAndList"/>).</summary>
    public static int[] PropCells(HordeNavData data, byte[] bsp)
    {
        BspData map;
        try
        {
            using var stream = new MemoryStream(bsp, writable: false);
            map = BspFile.LoadAsync(stream).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is SourceSharp.MapFormats.InvalidBspException or InvalidDataException or ArgumentException or IOException)
        {
            return [];
        }

        var cells = new SortedSet<int>();
        var near = new List<int>();
        int sprp = GameLumpEntry.MakeId(GameLumpId.StaticProps);
        foreach (GameLumpEntry entry in map.GameLumps)
        {
            if (entry.Id != sprp)
                continue;
            StaticPropLump lump = StaticPropLump.Read(entry);
            foreach (StaticProp prop in lump.Props)
            {
                if (prop.Solid == 0)
                    continue;
                float x = prop.Origin.X, y = prop.Origin.Y, z = prop.Origin.Z;
                data.Graph.CellsNear(x - PropReach, y - PropReach, x + PropReach, y + PropReach, near);
                foreach (int c in near)
                {
                    NavCell cell = data.Graph.Cell(c);
                    if (MathF.Abs(cell.FloorZ - z) <= 96f && Flat(data.Graph, c))
                        cells.Add(c);
                }
            }
        }

        return [.. cells];
    }

    /// <summary>Whether every walk out of a cell stays on its floor: no stair or slope.</summary>
    private static bool Flat(NavGraph graph, int cell)
    {
        float floor = graph.Cell(cell).FloorZ;
        foreach (NavEdge e in graph.EdgesFrom(cell))
        {
            if (MathF.Abs(graph.Cell(e.To).FloorZ - floor) > 1f)
                return false;
        }

        return true;
    }
}

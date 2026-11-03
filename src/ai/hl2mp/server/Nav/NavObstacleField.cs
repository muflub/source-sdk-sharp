using System.Diagnostics;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp.Nav;

/// <summary>What a solid entity is to a walking player.</summary>
public enum ObstacleBodyKind : byte
{
    /// <summary>It stands where it is: a brush, a breakable, a frozen or dynamic prop.</summary>
    Fixed = 0,

    /// <summary>A physics object a player's body shoves aside if it is small enough to ignore or costs a detour if not.</summary>
    Shovable,

    /// <summary>A door: shut where it spawned it is the graph's own Door edge, a way through; moved, it takes the cells it moved to.</summary>
    Door,
}

/// <summary>The field's reads of the world: its solid entities and where each is. Main thread only.</summary>
public interface IObstacleWorld
{
    /// <summary>How many rosters there are (a class each, asked in turn to keep a tick's cost down).</summary>
    int RosterCount { get; }

    /// <summary>The entities of roster <paramref name="roster"/>, with what each is to a player, into <paramref name="into"/>.</summary>
    void Roster(int roster, List<(EntityHandle Entity, ObstacleBodyKind Kind)> into);

    /// <summary>Where it is, false when it is gone or no longer solid to a player.</summary>
    bool Pose(EntityHandle entity, out AiVector origin, out AiVector angles);

    /// <summary>Its box in the world at that pose.</summary>
    (AiVector Mins, AiVector Maxs) Box(EntityHandle entity, AiVector origin, AiVector angles);

    /// <summary>Whether a player's gun can break it soon enough to be worth it: asked once, when it is enrolled.</summary>
    bool Breakable(EntityHandle entity);
}

/// <summary>What a bot's hands ask of the breakable on its route (§6.2): on the main thread.</summary>
public interface IBreakableAim
{
    /// <summary>The middle of what takes <paramref name="cell"/>, if a shot from <paramref name="eye"/> would reach it.</summary>
    bool TryAim(int cell, AiVector eye, EntityHandle shooter, out AiVector point);
}

/// <summary>Whether a box is in a player's PVS: what decides whether a client would be sent an entity there.</summary>
public interface IObstacleSight
{
    bool InView(EntityHandle player, AiVector mins, AiVector maxs);
}

/// <summary>
/// The solid entities as routing cells (plan_hl2mp_ai §6.2, user ruling 2026-11-02: "the
/// nav should know about static props and entities the client would know about"). Static
/// props are the file's. This field is what the file cannot hold: props, gates, func_brushes,
/// breakables and physboxes where they are now, and each bot's knowledge of them.
/// <list type="bullet">
/// <item>Main thread, a slice of the entities a tick. A body's cells are worked out only
/// when it moved; a worker only ever reads the finished <see cref="NavCellBlocks"/>.</item>
/// <item>A cell is taken when a body's centre over it would overlap the entity
/// (<see cref="BodyReach"/> round its box) from the floor up: not an entity it steps over
/// (<see cref="StepHeight"/>), and not one a crouched body goes under
/// (<see cref="CrouchHeight"/>), which only costs a standing route.</item>
/// <item>Shovable bodies no wider than <see cref="SmallSide"/> and no taller than
/// <see cref="SmallHeight"/> (a can, a bottle, a bucket, a brick) take nothing: a player's
/// body shoves them. Bigger ones cost (<see cref="NavCellBlocks.ShoveCost"/>) and do not
/// block, since a player can push through as a last resort.</item>
/// <item>One a gun can break (<see cref="IObstacleWorld.Breakable"/>) is
/// <see cref="NavCellBlocks.Breakable"/> instead, of any kind: a route may go through it for
/// <see cref="NavCellBlocks.BreakCost"/> a cell, and the bot shoots it on the way
/// (<c>BrainDriver</c>). User report 2026-11-02: "bots should shoot creates if blocking path".</item>
/// <item>Who knows what: <see cref="OnLevelStart"/>'s layout is every bot's from the start
/// (ruling 2026-11-01). After that a bot learns where a body went when that place is in its
/// PVS, and that it left when its old place is: the rule by which a client is sent an entity.
/// Players, weapons and items are not in the rosters.</item>
/// </list>
/// </summary>
public sealed class NavObstacleField
{
    /// <summary>A player's half width, and a little for the corner a walk cuts.</summary>
    public const float BodyReach = 20f;

    public const float StepHeight = 18f, CrouchHeight = 36f, StandHeight = 72f;

    /// <summary>A shovable body at most this wide and tall takes no cells.</summary>
    public const float SmallSide = 24f, SmallHeight = 32f;

    /// <summary>How far a body moves, or turns in degrees, before its cells are worked out again.</summary>
    public const float MovedBy = 8f;

    /// <summary>How many ticks a walk of every body takes: a quarter of a second behind a thing that moved.</summary>
    public const int ScanTicks = 16;

    /// <summary>How many ticks between one bot's knowledge and the next's: each asks after every thing it is behind on.</summary>
    public const int ViewTicks = 4;

    /// <summary>How many ticks between one roster and the next: with 15 classes, a new entity is enrolled within about 8 s.</summary>
    public const int RosterTicks = 33;

    private readonly NavGraph _graph;
    private readonly IObstacleWorld _world;
    private readonly IObstacleSight _sight;
    private readonly NavObstacleInfo[] _infos;
    private readonly NavObstacleState _truth;
    private readonly List<Body> _bodies = [];
    private readonly Dictionary<EntityHandle, int> _indexOf = [];
    private readonly List<int> _moved = [];
    private readonly Dictionary<EntityHandle, View> _views = [];
    private readonly List<View> _viewList = [];
    private readonly List<(EntityHandle Entity, ObstacleBodyKind Kind)> _roster = [];
    private readonly List<int> _near = [];
    private readonly bool[] _baseBlocking;
    private NavCellBlocks _baseCells;
    private int _scanAt, _rosterAt, _viewAt, _tick;
    private long _spentTicks, _ticks;

    public NavObstacleField(NavGraph graph, IObstacleWorld world, IObstacleSight sight, NavObstacleInfo[] infos, NavObstacleState truth)
    {
        _graph = graph;
        _world = world;
        _sight = sight;
        _infos = infos;
        _truth = truth;
        _baseBlocking = new bool[infos.Length];
        _baseCells = new NavCellBlocks(new byte[graph.CellCount]);
    }

    /// <summary>The entities the field follows, gone ones included.</summary>
    public int Bodies => _bodies.Count;

    /// <summary>Those a gun can break, still there.</summary>
    public int BreakableBodies => _bodies.Count(b => b.Breakable && !b.Gone);

    /// <summary>The cells the entities truly take now: what a bot that had seen everything would route by. Counted on asking.</summary>
    public (int Blocked, int Costly) TruthCells
    {
        get
        {
            byte[] truth = new byte[_graph.CellCount];
            foreach (Body b in _bodies)
                Mark(truth, b.Now);
            return new NavCellBlocks(truth).Count();
        }
    }

    /// <summary>What the entities truly do to <paramref name="cell"/>: a debug command's control.</summary>
    public byte TruthAt(int cell)
    {
        byte[] truth = new byte[_graph.CellCount];
        foreach (Body b in _bodies)
            Mark(truth, b.Now);
        return truth[cell];
    }

    /// <summary>How many entities have moved since the level began.</summary>
    public int Moved => _moved.Count;

    /// <summary>How many times a bot learned something by seeing it.</summary>
    public long Learned { get; private set; }

    /// <summary>How many times a bot's cells were rebuilt, for the cost.</summary>
    public long Published { get; private set; }

    /// <summary>The mean main-thread cost of a tick, in microseconds.</summary>
    public double MeanUs => _ticks == 0 ? 0 : _spentTicks * 1e6 / Stopwatch.Frequency / _ticks;

    /// <summary>The level's layout: what every bot knows from the start.</summary>
    public void OnLevelStart()
    {
        for (int r = 0; r < _world.RosterCount; r++)
            Enrol(r);
        _moved.Clear();
        byte[] cells = new byte[_graph.CellCount];
        foreach (Body b in _bodies)
        {
            b.Layout = b.Now;
            b.InLayout = true;
            b.Seq = 1;
            b.Moved = false;
            Mark(cells, b.Now);
        }

        _baseCells = new NavCellBlocks(cells);
        for (int o = 0; o < _infos.Length; o++)
            _baseBlocking[o] = _truth.Blocking(o);
    }

    /// <summary>What <paramref name="player"/> routes by: the layout until it has seen otherwise.</summary>
    public NavObstacleState StateFor(EntityHandle player)
    {
        if (_views.TryGetValue(player, out View? view))
            return view.State;

        var state = _truth.Clone();
        state.Cells = _baseCells;
        for (int o = 0; o < _infos.Length; o++)
            state.Set(o, _baseBlocking[o]);
        view = new View(player, state);
        _views[player] = view;
        _viewList.Add(view);
        return state;
    }

    /// <summary>A slice of the bodies, one roster in turn, and one bot's knowledge.</summary>
    public void Tick()
    {
        long start = Stopwatch.GetTimestamp();
        _tick++;
        if (_tick % RosterTicks == 0 && _world.RosterCount > 0)
        {
            Enrol(_rosterAt);
            _rosterAt = (_rosterAt + 1) % _world.RosterCount;
        }

        int slice = Math.Max(1, _bodies.Count / ScanTicks);
        for (int i = 0; i < slice && _bodies.Count > 0; i++)
        {
            _scanAt = (_scanAt + 1) % _bodies.Count;
            Scan(_scanAt);
        }

        if (_tick % ViewTicks == 0 && _viewList.Count > 0)
        {
            _viewAt = (_viewAt + 1) % _viewList.Count;
            Sync(_viewList[_viewAt]);
        }

        _spentTicks += Stopwatch.GetTimestamp() - start;
        _ticks++;
    }

    private void Enrol(int roster)
    {
        _roster.Clear();
        _world.Roster(roster, _roster);
        foreach ((EntityHandle entity, ObstacleBodyKind kind) in _roster)
        {
            if (_indexOf.ContainsKey(entity) || !_world.Pose(entity, out AiVector origin, out AiVector angles))
                continue;
            var body = new Body(entity, kind, origin, angles) { Breakable = _world.Breakable(entity) };
            body.Now = Footprint(body, origin, angles);
            _indexOf[entity] = _bodies.Count;
            _bodies.Add(body);
            NoteMoved(_bodies.Count - 1, body);
        }
    }

    private void Scan(int index)
    {
        Body b = _bodies[index];
        if (b.Gone)
            return;
        if (!_world.Pose(b.Entity, out AiVector origin, out AiVector angles))
        {
            b.Gone = true;
            b.Now = NavFootprint.None;
            b.Seq++;
            _indexOf.Remove(b.Entity);
            NoteMoved(index, b);
            return;
        }

        if (!PoseDiffers(origin, angles, b.Origin, b.Angles))
            return;
        b.Origin = origin;
        b.Angles = angles;
        b.Now = Footprint(b, origin, angles);
        b.Seq++;
        NoteMoved(index, b);
    }

    private void NoteMoved(int index, Body body)
    {
        if (!body.Moved)
        {
            body.Moved = true;
            _moved.Add(index);
        }
    }

    private void Sync(View view)
    {
        bool learned = false;
        foreach (int index in _moved)
        {
            Body body = _bodies[index];
            int knownSeq = view.Seq(index, body.InLayout ? 1 : 0);
            if (knownSeq == body.Seq)
                continue;
            NavFootprint known = view.Print(index) ?? body.Layout;
            if (!body.Gone && _sight.InView(view.Player, body.Now.Mins, body.Now.Maxs))
            {
                view.Know(index, body.Seq, body.Now);
                learned = true;
                Learned++;
            }
            else if (!known.IsEmpty && _sight.InView(view.Player, known.Mins, known.Maxs))
            {
                // It is not where the bot thought, and where it went is out of sight.
                view.Know(index, body.Gone ? body.Seq : knownSeq, NavFootprint.None);
                learned = true;
                Learned++;
            }
        }

        for (int o = 0; o < _infos.Length; o++)
        {
            if (view.State.Blocking(o) != _truth.Blocking(o) && _sight.InView(view.Player, _infos[o].Mins, _infos[o].Maxs))
                view.State.Set(o, _truth.Blocking(o));
        }

        if (!learned)
            return;
        byte[] cells = new byte[_graph.CellCount];
        for (int i = 0; i < _bodies.Count; i++)
            Mark(cells, view.Print(i) ?? _bodies[i].Layout);
        view.State.Cells = new NavCellBlocks(cells);
        Published++;
    }

    private static void Mark(byte[] cells, NavFootprint print)
    {
        foreach (int c in print.Costly)
        {
            if (cells[c] == NavCellBlocks.Free)
                cells[c] = NavCellBlocks.Costly;
        }

        foreach (int c in print.Breakable)
        {
            if (cells[c] == NavCellBlocks.Free)
                cells[c] = NavCellBlocks.Breakable;
        }

        foreach (int c in print.Blocked)
            cells[c] = NavCellBlocks.Blocked;
    }

    /// <summary>The breakable that takes <paramref name="cell"/> now, and the middle of it: where a bot's shot goes.</summary>
    public bool BreakableAt(int cell, out EntityHandle entity, out AiVector centre)
    {
        foreach (Body b in _bodies)
        {
            if (!b.Gone && b.Breakable && Array.IndexOf(b.Now.Breakable, cell) >= 0)
            {
                entity = b.Entity;
                centre = (b.Now.Mins + b.Now.Maxs) * 0.5f;
                return true;
            }
        }

        entity = EntityHandle.None;
        centre = default;
        return false;
    }

    /// <summary>Whether a pose differs, the angles the short way round: a solver's -90 is a map's 270.</summary>
    private static bool PoseDiffers(AiVector origin, AiVector angles, AiVector wasOrigin, AiVector wasAngles)
        => origin.DistanceTo(wasOrigin) >= MovedBy || Arc(angles.X, wasAngles.X) >= MovedBy
           || Arc(angles.Y, wasAngles.Y) >= MovedBy || Arc(angles.Z, wasAngles.Z) >= MovedBy;

    private static float Arc(float a, float b)
    {
        float d = MathF.Abs(a - b) % 360f;
        return d > 180f ? 360f - d : d;
    }

    private NavFootprint Footprint(Body body, AiVector origin, AiVector angles)
    {
        (AiVector mins, AiVector maxs) = _world.Box(body.Entity, origin, angles);
        float sideX = maxs.X - mins.X, sideY = maxs.Y - mins.Y, height = maxs.Z - mins.Z;
        if (body.Kind == ObstacleBodyKind.Shovable && MathF.Max(sideX, sideY) <= SmallSide && height <= SmallHeight)
            return new NavFootprint([], [], mins, maxs);

        // A door where it spawned is the graph's Door edge.
        if (body.Kind == ObstacleBodyKind.Door && !PoseDiffers(origin, angles, body.SpawnOrigin, body.SpawnAngles))
            return new NavFootprint([], [], mins, maxs);

        var blocked = new List<int>();
        var costly = new List<int>();
        var breakable = new List<int>();
        _graph.CellsNear(mins.X - BodyReach, mins.Y - BodyReach, maxs.X + BodyReach, maxs.Y + BodyReach, _near);
        foreach (int c in _near)
        {
            NavCell cell = _graph.Cell(c);
            AiVector centre = cell.Centre;
            if (centre.X < mins.X - BodyReach || centre.X > maxs.X + BodyReach
                || centre.Y < mins.Y - BodyReach || centre.Y > maxs.Y + BodyReach
                || (cell.Flags & NavCellFlags.Ladder) != 0)
                continue;
            float floor = cell.FloorZ;
            if (maxs.Z <= floor + StepHeight || mins.Z >= floor + StandHeight)
                continue;
            if (mins.Z >= floor + CrouchHeight)
                costly.Add(c);
            else if (body.Breakable)
                breakable.Add(c);
            else if (body.Kind == ObstacleBodyKind.Shovable)
                costly.Add(c);
            else
                blocked.Add(c);
        }

        return new NavFootprint([.. blocked], [.. costly], mins, maxs) { Breakable = [.. breakable] };
    }

    private sealed class Body(EntityHandle entity, ObstacleBodyKind kind, AiVector origin, AiVector angles)
    {
        public EntityHandle Entity { get; } = entity;
        public ObstacleBodyKind Kind { get; } = kind;
        public AiVector SpawnOrigin { get; } = origin;
        public AiVector SpawnAngles { get; } = angles;
        public AiVector Origin { get; set; } = origin;
        public AiVector Angles { get; set; } = angles;
        public NavFootprint Now { get; set; } = NavFootprint.None;
        public NavFootprint Layout { get; set; } = NavFootprint.None;
        public bool InLayout { get; set; }
        public int Seq { get; set; } = 1;
        public bool Moved { get; set; }
        public bool Gone { get; set; }
        public bool Breakable { get; init; }
    }

    private sealed class View(EntityHandle player, NavObstacleState state)
    {
        private readonly Dictionary<int, (int Seq, NavFootprint Print)> _known = [];

        public EntityHandle Player { get; } = player;
        public NavObstacleState State { get; } = state;
        public int Seq(int index, int layoutSeq) => _known.TryGetValue(index, out var k) ? k.Seq : layoutSeq;
        public NavFootprint? Print(int index) => _known.TryGetValue(index, out var k) ? k.Print : null;
        public void Know(int index, int seq, NavFootprint print) => _known[index] = (seq, print);
    }
}

/// <summary>The cells one position of a body takes, and its box.</summary>
public sealed record NavFootprint(int[] Blocked, int[] Costly, AiVector Mins, AiVector Maxs)
{
    public static NavFootprint None => new([], [], default, default);

    /// <summary>The cells it takes that a gun can clear.</summary>
    public int[] Breakable { get; init; } = [];

    public bool IsEmpty => Blocked.Length == 0 && Costly.Length == 0 && Breakable.Length == 0;
}

/// <summary>
/// <see cref="IObstacleWorld"/> over the engine: the classes that stop a player, not a
/// player, a weapon or an item. Reads entities and never a player's state (§4.4).
/// </summary>
public sealed class EngineObstacleWorld(IEngineEntities entities, Func<EntityHandle, string>? modelOf = null) : IObstacleWorld
{
    /// <summary>SF_BREAK_TRIGGER_ONLY: a func_breakable only the map can break.</summary>
    private const int TriggerOnly = 1;

    /// <summary>The most hit points worth shooting through: about two SMG clips' worth is no crate.</summary>
    public const int BreakableMostHealth = 150;

    private readonly HashSet<EntityHandle> _breakable = [];

    /// <summary>A class a gun breaks, its health saying how soon: a prop or physbox with none is not breakable; a brush may ask the map to do it.</summary>
    private static bool BreakableRoster(int roster) => roster is 0 or 1 or 2 or 3 or 4 or 9 or 10;

    public bool Breakable(EntityHandle entity) => _breakable.Contains(entity);

    /// <summary>SF_PHYSPROP_MOTIONDISABLED: a prop_physics that starts frozen stands where it is.</summary>
    private const int MotionDisabled = 8;

    private static ObstacleBodyKind RosterKind(int roster) => roster switch
    {
        0 or 1 or 2 or 3 or 4 => ObstacleBodyKind.Shovable,
        12 or 13 or 14 => ObstacleBodyKind.Door,
        _ => ObstacleBodyKind.Fixed,
    };

    /// <summary>The class of a roster.</summary>
    public static string RosterClass(int roster) => roster switch
    {
        0 => "prop_physics",
        1 => "prop_physics_respawnable",
        2 => "prop_physics_multiplayer",
        3 => "func_physbox",
        4 => "func_physbox_multiplayer",
        5 => "prop_dynamic",
        6 => "func_brush",
        7 => "func_wall",
        8 => "func_wall_toggle",
        9 => "func_breakable",
        10 => "func_breakable_surf",
        11 => "func_pushable",
        12 => "func_door",
        13 => "func_door_rotating",
        _ => "prop_door_rotating",
    };

    public int RosterCount => 15;

    public void Roster(int roster, List<(EntityHandle Entity, ObstacleBodyKind Kind)> into)
    {
        ObstacleBodyKind kind = RosterKind(roster);
        foreach (EntityHandle h in entities.FindAllByClassname(RosterClass(roster)))
        {
            // Solid none: the map's decoration on a prop_dynamic. A toggled brush is solid
            // with its flag off (Pose asks), and is enrolled again when it is back.
            if (entities.GetSolidType(h) == SolidType.None)
                continue;
            int health = BreakableRoster(roster) ? entities.GetHealth(h) : 0;
            // Not an explosive drum: nobody shoots one they are walking up to.
            if (health is > 0 and <= BreakableMostHealth && !(roster == 9 && (entities.GetSpawnFlags(h) & TriggerOnly) != 0)
                && modelOf?.Invoke(h).Contains("explosive", StringComparison.OrdinalIgnoreCase) != true)
                _breakable.Add(h);
            bool frozen = kind == ObstacleBodyKind.Shovable && (entities.GetSpawnFlags(h) & MotionDisabled) != 0;
            into.Add((h, frozen ? ObstacleBodyKind.Fixed : kind));
        }
    }

    public bool Pose(EntityHandle entity, out AiVector origin, out AiVector angles)
    {
        origin = default;
        angles = default;
        if (!entities.IsValid(entity) || (entities.GetSolidFlags(entity) & SolidFlags.NotSolid) != 0)
            return false;
        Vector o = entities.GetOrigin(entity), a = entities.GetAngles(entity);
        origin = new AiVector(o.X, o.Y, o.Z);
        angles = new AiVector(a.X, a.Y, a.Z);
        return true;
    }

    public (AiVector Mins, AiVector Maxs) Box(EntityHandle entity, AiVector origin, AiVector angles)
    {
        (Vector mins, Vector maxs) = entities.GetBounds(entity);
        Angles.Basis axes = Angles.Vectors(new Vector(angles.X, angles.Y, angles.Z));
        float loX = float.MaxValue, loY = float.MaxValue, loZ = float.MaxValue, hiX = float.MinValue, hiY = float.MinValue, hiZ = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            float x = (i & 1) != 0 ? maxs.X : mins.X, y = (i & 2) != 0 ? maxs.Y : mins.Y, z = (i & 4) != 0 ? maxs.Z : mins.Z;
            Vector w = axes.Forward * x - axes.Right * y + axes.Up * z;
            loX = MathF.Min(loX, w.X);
            loY = MathF.Min(loY, w.Y);
            loZ = MathF.Min(loZ, w.Z);
            hiX = MathF.Max(hiX, w.X);
            hiY = MathF.Max(hiY, w.Y);
            hiZ = MathF.Max(hiZ, w.Z);
        }

        return (origin + new AiVector(loX, loY, loZ), origin + new AiVector(hiX, hiY, hiZ));
    }
}

/// <summary><see cref="IBreakableAim"/> over the field and the engine's traces: the line is a shot's, so a crate behind a wall is no target.</summary>
public sealed class EngineBreakableAim(NavObstacleField field, ITraceQueries traces) : IBreakableAim
{
    public bool TryAim(int cell, AiVector eye, EntityHandle shooter, out AiVector point)
    {
        if (!field.BreakableAt(cell, out EntityHandle entity, out point))
            return false;
        TraceAnswer t = traces.Line(new Vector(eye.X, eye.Y, eye.Z), new Vector(point.X, point.Y, point.Z), Senses.AiWorldRoutes.ShotMask, shooter);
        return t.Fraction >= 1f || t.Entity == entity;
    }
}

/// <summary><see cref="IObstacleSight"/> over the engine's PVS: where a client would be sent an entity.</summary>
public sealed class EnginePvsSight(IEnginePlayers players) : IObstacleSight
{
    public bool InView(EntityHandle player, AiVector mins, AiVector maxs)
        => players.BoxInPvs(player, new Vector(mins.X, mins.Y, mins.Z), new Vector(maxs.X, maxs.Y, maxs.Z));
}

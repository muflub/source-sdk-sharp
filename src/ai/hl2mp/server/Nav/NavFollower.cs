namespace SourceSharp.Ai.Hl2mp.Nav;

/// <summary>Where a follower stands relative to its objective.</summary>
public enum FollowState : byte
{
    Moving = 0,
    Arrived,
    NoRoute,
    OffGraph,
}

/// <summary>
/// A bot holding <c>Follow</c> along a flow field (plan_hl2mp_ai §2.3,
/// Phase 3 step 5): one field to the objective, and each tick the corridor
/// the field gives from the bot's cell, funnelled and shortcut into the
/// corners its move intent carries. A jump or crouch-jump edge out of the
/// bot's cell turns the move into that jump toward the landing; a
/// crouch-only cell within the next few holds the bot ducked.
/// </summary>
public sealed class NavFollower
{
    public const int CorridorCells = 32;
    /// <summary>How near the target a bot has arrived: its box over the item's place.</summary>
    public const float ArriveRadius = 16f;
    private const int CrouchLookahead = 4;

    private readonly NavGraph _graph;
    private readonly NavObstacleState _obstacles;
    private readonly FlowField _field = new();
    private readonly List<CellId> _corridor = new();
    private readonly List<PathCorner> _corners = new();
    private readonly HashSet<int> _avoided = new();
    private int _avoidLeft;

    /// <summary>How far ahead of a stuck bot the way is avoided, and how far to each side.</summary>
    public const float AvoidAhead = 64f, StripHalfWidth = 24f;

    /// <summary>How many moves a stuck spot stays avoided (8 s at a move a tick).</summary>
    public const int AvoidMoves = 533;

    /// <summary>The cells avoided while the bot routes round where it stuck.</summary>
    public IReadOnlyCollection<int> Avoided => _avoided;

    public NavFollower(NavGraph graph, CellId objective, AiVector target, ClearanceClass cls, NavObstacleState obstacles)
    {
        _graph = graph;
        _obstacles = obstacles;
        Objective = objective;
        Target = target;
        Class = cls;
    }

    public CellId Objective { get; }

    /// <summary>The point the bot is sent to (an item's place): arrival is reaching it.</summary>
    public AiVector Target { get; }

    public ClearanceClass Class { get; }

    /// <summary>The cell the bot was last found in, or None.</summary>
    public CellId At { get; private set; } = CellId.None;

    /// <summary>The field's cost from <see cref="At"/>, +∞ off the graph or with no route.</summary>
    public float CostLeft => At.IsValid ? _field.CostAt(At.Index) : float.PositiveInfinity;

    /// <summary>The edge the route takes out of the bot's cell (a Ladder means the bot mounts here), Walk off the field.</summary>
    public NavEdgeType NextVia => At.IsValid ? _field.ViaFrom(At.Index) : NavEdgeType.Walk;

    /// <summary>The cell the route steps into from the bot's cell, or None.</summary>
    public CellId NextCell => At.IsValid ? _field.NextFrom(At.Index) : CellId.None;

    /// <summary>The next step from where the bot was last found, for a stuck line: the cell, its floor point, and the edge.</summary>
    public string NextStep()
    {
        if (!At.IsValid || !_field.NextFrom(At.Index).IsValid)
            return "none";
        CellId next = _field.NextFrom(At.Index);
        AiVector c = _graph.Cell(next).Centre;
        return FormattableString.Invariant($"{_field.ViaFrom(At.Index)} to cell {next.Index} at ({c.X:F0} {c.Y:F0} {c.Z:F0})");
    }

    /// <summary>(Re)builds the field: at the start, and when the obstacles change.</summary>
    public void Rebuild()
    {
        _builds++;
        _field.Build(_graph, Objective, Class, _obstacles, _avoided.Count > 0 ? _avoided : null, At);
    }

    /// <summary>Field builds so far, for the cost counters.</summary>
    public int Builds => _builds;

    private int _builds;

    /// <summary>
    /// The bot made no progress (something the nav does not know is in the
    /// way: a physics prop the file's box under-states, a player): the
    /// corridor's cells within <see cref="AvoidAhead"/> of the bot are
    /// avoided for <see cref="AvoidMoves"/> moves and the field rebuilt, so it
    /// routes round. With no route round, nothing is avoided and the bot
    /// jumps at the blockage instead. Answers whether a way round was found.
    /// </summary>
    public bool ReportStuck(AiVector origin)
    {
        if (!At.IsValid)
            return false;
        AiVector here = _graph.Cell(At).Centre;
        CellId next = _field.NextFrom(At.Index);
        if (!next.IsValid)
            return false;

        // Every cell in a strip ahead, along the way the bot was going, a
        // player's width across: the field's moves skip columns (a step two
        // columns along), so the corridor's own cells leave gaps beside them.
        AiVector way = _graph.Cell(next).Centre - here;
        var dir = new AiVector(way.X, way.Y, 0f).Normalised();
        for (int c = 0; c < _graph.CellCount; c++)
        {
            AiVector d = _graph.Cell(c).Centre - here;
            float along = d.X * dir.X + d.Y * dir.Y, across = MathF.Abs(d.X * dir.Y - d.Y * dir.X);
            if (c != At.Index && c != Objective.Index && along > 0f && along <= AvoidAhead && across <= StripHalfWidth && MathF.Abs(d.Z) <= 40f)
                _avoided.Add(c);
        }

        Rebuild();
        if (float.IsFinite(_field.CostAt(At.Index)) && _avoided.Count > 0)
        {
            _avoidLeft = AvoidMoves;
            return true;
        }

        _avoided.Clear();
        Rebuild();
        _jumpLeft = JumpMoves;
        return false;
    }

    /// <summary>How many moves a bot with no way round jumps at its blockage.</summary>
    public const int JumpMoves = 40;

    private int _jumpLeft;

    /// <summary>The move for a bot at <paramref name="origin"/>, and where it stands.</summary>
    public MoveIntent Next(AiVector origin, out FollowState state)
    {
        if (_field.Objective != Objective || _builds == 0)
        {
            At = Locate(origin);
            Rebuild();
        }
        if (_avoidLeft > 0 && --_avoidLeft == 0)
        {
            _avoided.Clear();
            Rebuild();
        }

        At = Locate(origin);
        if (!At.IsValid)
        {
            state = FollowState.OffGraph;
            return MoveIntent.Hold;
        }

        if (new AiVector(Target.X - origin.X, Target.Y - origin.Y, 0f).Length < ArriveRadius && MathF.Abs(Target.Z - origin.Z) < 72f)
        {
            state = FollowState.Arrived;
            return MoveIntent.Hold;
        }

        // In the cell that touches the target: the last steps straight onto it.
        if (At == Objective)
        {
            state = FollowState.Moving;
            return new MoveIntent(MoveKind.Follow, MoveSpeed.Walk, 1, Target, default, default, default, default, _graph.Cell(At).CrouchOnly, 0);
        }

        // Off the cells a partial field settled: ask again from here.
        if (!float.IsFinite(_field.CostAt(At.Index)) && _field.IsPartial)
            Rebuild();
        if (!float.IsFinite(_field.CostAt(At.Index)))
        {
            state = FollowState.NoRoute;
            return MoveIntent.Hold;
        }

        _corridor.Clear();
        _corridor.Add(At);
        bool crouch = _graph.Cell(At).CrouchOnly;
        for (int at = At.Index, i = 0; i < CorridorCells && _field.NextFrom(at).IsValid; i++)
        {
            at = _field.NextFrom(at).Index;
            _corridor.Add(new CellId(at));
            if (i < CrouchLookahead && _graph.Cell(at).CrouchOnly)
                crouch = true;
        }

        AiVector end = _corridor[^1] == Objective ? Target with { Z = _graph.Cell(Objective).FloorZ } : _graph.Cell(_corridor[^1]).Centre;
        PathSmoother.Funnel(_graph, _corridor, origin, end, _corners);
        // Routing round a stuck spot, the corners keep to the corridor: a
        // shortcut's straight line knows nothing of the avoided cells.
        if (_avoided.Count == 0)
            PathSmoother.Shortcut(_graph, Class, _obstacles, _corners);

        // A corner the bot already stands by (its own cell's, behind it at a
        // drop's edge) is passed: steering back to it holds the bot at the
        // edge (live, ss_ai_nav's step top: "next Drop ... steering to" a
        // corner 18 u behind it, for ever).
        int skip = 0;
        while (skip < _corners.Count - 2 && Flat(_corners[skip + 1].Point, origin) < PassedRadius)
            skip++;
        int count = Math.Min(MoveIntent.MaxCorners, _corners.Count - 1 - skip);
        AiVector Corner(int i) => i < count ? _corners[i + 1 + skip].Point : default;
        NavEdgeType via = _field.ViaFrom(At.Index);
        MoveKind kind = via switch
        {
            // A ledge the derivation counts a plain jump's (rise to 39 u, a
            // jump's 21 plus a step's 18) is taken ducked: the plain jump
            // tops out near 20 u and the step does not come in the air (live,
            // ss_ai_nav's 32 u ledge: the bot rose to 35 and fell back, for
            // ever), and the duck tucks the feet up as a player's does.
            NavEdgeType.Jump or NavEdgeType.CrouchJump => MoveKind.CrouchJump,
            _ => MoveKind.Follow,
        };
        if (_jumpLeft > 0 && kind == MoveKind.Follow)
        {
            _jumpLeft--;
            kind = MoveKind.CrouchJump;
        }
        state = FollowState.Moving;
        return new MoveIntent(kind, MoveSpeed.Run, (byte)Math.Max(count, 0), Corner(0), Corner(1), Corner(2), Corner(3), default, crouch, 0);
    }

    /// <summary>A corner nearer the bot than this, flat, is one it has passed.</summary>
    public const float PassedRadius = 24f;

    private static float Flat(AiVector a, AiVector b) => new AiVector(a.X - b.X, a.Y - b.Y, 0f).Length;

    private CellId Locate(AiVector origin)
    {
        CellId c = _graph.FindCell(origin + new AiVector(0f, 0f, 1f));
        if (c.IsValid && _graph.Cell(c).Admits(Class))
            return c;
        foreach (CellId near in NavPoints.CellsNear(_graph, origin, Class))
        {
            if (float.IsFinite(_field.CostAt(near.Index)))
                return near;
        }

        return c;
    }
}

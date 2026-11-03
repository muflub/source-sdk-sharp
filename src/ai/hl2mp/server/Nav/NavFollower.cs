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

    /// <summary>
    /// How far under the target's height a bot has arrived (an item on a shelf,
    /// a player's height up), and over it: a bot on a table does not touch the
    /// kit under the table (live, dm_lockdown: held over it for good).
    /// </summary>
    public const float ArriveBelow = 72f, ArriveAbove = 24f;
    private const int CrouchLookahead = 4;

    private readonly NavGraph _graph;
    private NavObstacleState _obstacles;
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

    /// <summary>Whether this field goes through the props a player can shove, there being no other way.</summary>
    public bool Shoving { get; private set; }

    /// <summary>The cell the bot was last found in, or None.</summary>
    public CellId At { get; private set; } = CellId.None;

    /// <summary>The field's cost from <see cref="At"/>, +∞ off the graph or with no route.</summary>
    public float CostLeft => At.IsValid ? _field.CostAt(At.Index) : float.PositiveInfinity;

    /// <summary>The edge the route takes out of the bot's cell (a Ladder means the bot mounts here), Walk off the field.</summary>
    public NavEdgeType NextVia => At.IsValid ? _field.ViaFrom(At.Index) : NavEdgeType.Walk;

    /// <summary>The cell the route steps into from the bot's cell, or None.</summary>
    public CellId NextCell => At.IsValid ? _field.NextFrom(At.Index) : CellId.None;

    /// <summary>
    /// Where the route's ladder ends, when it climbs one from the bot's cell: the
    /// cell past the run of Ladder edges, on another floor. The first step can be
    /// a cell on the same floor (a ladder's foot beside the bot), and a climb
    /// aimed at that looked at its floor and got off at the foot. None when the
    /// run stays on this floor: a ladder's on-and-off link along the floor is a
    /// walk, not a climb.
    /// </summary>
    public CellId LadderEnd()
    {
        if (!At.IsValid)
            return CellId.None;
        int at = At.Index;
        CellId end = CellId.None;
        for (int i = 0; i < CorridorCells && _field.NextFrom(at).IsValid && _field.ViaFrom(at) == NavEdgeType.Ladder; i++)
        {
            end = _field.NextFrom(at);
            at = end.Index;
        }

        return end.IsValid && MathF.Abs(_graph.Cell(end).FloorZ - _graph.Cell(At).FloorZ) > OnFloorReach ? end : CellId.None;
    }

    /// <summary>The first cell a breakable takes on the route within <paramref name="hops"/> steps, the bot's own first, or -1.</summary>
    public int BreakableAhead(NavCellBlocks cells, int hops)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (!At.IsValid)
            return -1;
        int at = At.Index;
        if (cells.State(at) == NavCellBlocks.Breakable)
            return at;
        for (int i = 0; i < hops; i++)
        {
            CellId next = _field.NextFrom(at);
            if (!next.IsValid)
                return -1;
            if (cells.State(next.Index) == NavCellBlocks.Breakable)
                return next.Index;
            at = next.Index;
        }

        return -1;
    }

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
        _builtOver = _obstacles.Cells;
        _field.Build(_graph, Objective, Class, _obstacles, _avoided.Count > 0 ? _avoided : null, At);
    }

    // What the last build knew of the entities.
    private NavCellBlocks? _builtOver;

    /// <summary>
    /// What the bot knows of the entities changed since its field was built: a build, if
    /// it now knows of something on the rest of its corridor. Only then: a way that
    /// opened waits for the next goal, or a bot round a corner from a prop that was shoved
    /// aside and back would turn on every shove (the corner it holds stays, _held).
    /// </summary>
    private void ReplanIfTheWayIsTaken()
    {
        NavCellBlocks? now = _obstacles.Cells;
        if (ReferenceEquals(now, _builtOver))
            return;
        _builtOver = now;
        int at = At.Index;
        for (int i = 0; i < CorridorCells && _field.NextFrom(at).IsValid; i++)
        {
            at = _field.NextFrom(at).Index;
            if (at != Objective.Index && now?.State(at) is NavCellBlocks.Blocked or NavCellBlocks.Costly)
            {
                Replans++;
                Rebuild();
                return;
            }
        }
    }

    /// <summary>How many times a field was built again for a thing learned to be on its way.</summary>
    public long Replans { get; private set; }

    /// <summary>
    /// The first build, on a worker (§2.7's flow-field job): from
    /// <paramref name="from"/> over <paramref name="obstacles"/> as they stood
    /// when it was asked. The main thread does not touch the follower until it
    /// is done.
    /// </summary>
    public void BuildFrom(CellId from, NavObstacleState obstacles)
    {
        _builds++;
        At = from;
        _builtOver = obstacles.Cells;
        _field.Build(_graph, Objective, Class, obstacles, null, from);
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
        _stuckReports++;
        _held = null;
        // Off the graph, or with nothing to step into: the way back to a cell
        // (Next's) goes on, and the bot jumps at whatever holds it.
        if (!At.IsValid || !_field.NextFrom(At.Index).IsValid)
        {
            _jumpLeft = JumpMoves;
            _away = -1;
            return false;
        }

        AiVector here = _graph.Cell(At).Centre;
        CellId next = _field.NextFrom(At.Index);

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

        // The step itself, on whatever floor: a drop through standing glass from
        // a window's sill is not a strip of the bot's own floor (live, dm_lockdown:
        // 25 s on the sill, the same drop each time).
        if (next != Objective)
            _avoided.Add(next.Index);

        Rebuild();
        if (float.IsFinite(_field.CostAt(At.Index)) && _avoided.Count > 0)
        {
            _avoidLeft = AvoidMoves;
            // The way round did not free it last time either: jump on it too.
            if (_stuckReports >= 2)
                _jumpLeft = JumpMoves;
            return true;
        }

        _avoided.Clear();
        Rebuild();
        _jumpLeft = JumpMoves;
        return false;
    }

    /// <summary>How far round a player's place the cells are avoided: two half-hulls and a margin, as a corridor's corners hug what they avoid.</summary>
    public const float PlayerReach = 40f;

    /// <summary>How many moves a player's place stays avoided (2 s at a move a tick).</summary>
    public const int AvoidPlayerMoves = 133;

    /// <summary>
    /// A player stands in the way and is not moving: the cells their body takes
    /// are avoided for a while and the field rebuilt, so the bot walks round.
    /// Answers whether there is a way round.
    /// </summary>
    public bool RouteRound(AiVector player)
    {
        if (!At.IsValid)
            return false;
        for (int c = 0; c < _graph.CellCount; c++)
        {
            AiVector d = _graph.Cell(c).Centre - player;
            if (c != At.Index && c != Objective.Index && new AiVector(d.X, d.Y, 0f).Length <= PlayerReach && MathF.Abs(d.Z) <= 40f)
                _avoided.Add(c);
        }

        Rebuild();
        _held = null;
        if (_avoided.Count > 0 && float.IsFinite(_field.CostAt(At.Index)))
        {
            _avoidLeft = AvoidPlayerMoves;
            return true;
        }

        _avoided.Clear();
        Rebuild();
        return false;
    }

    /// <summary>
    /// A new goal's follower goes on with what the bot was doing: the corner it
    /// was making for, while that still leads the new way (a hunt re-goals every
    /// half second), and about being stuck, the cells it is avoiding, its
    /// jumping, and how many times it has reported.
    /// </summary>
    public void GoOnFrom(NavFollower old)
    {
        ArgumentNullException.ThrowIfNull(old);
        _held = old._held;
        if (old.Shoving && !Shoving)
        {
            _obstacles = _obstacles.Shoved();
            Shoving = true;
        }

        _stuckReports = old._stuckReports;
        _jumpLeft = old._jumpLeft;
        if (old._avoidLeft > 0)
        {
            _avoidLeft = old._avoidLeft;
            _avoided.UnionWith(old._avoided);
            _avoided.Remove(Objective.Index);
            _rebuildCarried = true;
        }
    }

    private int _stuckReports;
    private bool _rebuildCarried;

    /// <summary>How many moves a bot with no way round jumps at its blockage.</summary>
    public const int JumpMoves = 40;

    private int _jumpLeft;

    /// <summary>The move for a bot at <paramref name="origin"/>, and where it stands.</summary>
    public MoveIntent Next(AiVector origin, out FollowState state)
    {
        if (_field.Objective != Objective || _builds == 0 || _rebuildCarried)
        {
            _rebuildCarried = false;
            At = Locate(origin);
            Rebuild();
        }
        if (_avoidLeft > 0 && --_avoidLeft == 0)
        {
            _avoided.Clear();
            Rebuild();
        }

        At = Locate(origin);
        _corners.Clear();
        _corridor.Clear();
        _passed = 0;
        if (!At.IsValid)
        {
            state = FollowState.OffGraph;
            return BackToAField(origin);
        }

        // Falling to a floor well below: its route starts from there. Steered by
        // it in the air, a bot off a rail's top caught the lip it had come over.
        if (_falling)
        {
            state = FollowState.Moving;
            return MoveIntent.Hold;
        }

        if (new AiVector(Target.X - origin.X, Target.Y - origin.Y, 0f).Length < ArriveRadius
            && Target.Z - origin.Z < ArriveBelow && origin.Z - Target.Z < ArriveAbove)
        {
            state = FollowState.Arrived;
            return MoveIntent.Hold;
        }

        // In the cell that touches the target: the last steps straight onto it.
        if (At == Objective)
        {
            _corners.Add(new PathCorner(origin, NavEdgeType.Walk));
            _corners.Add(new PathCorner(Target, NavEdgeType.Walk));
            state = FollowState.Moving;
            // Told it is stuck this near (the target under a shelf the bot stands
            // on): down to it with a jump's way off, as for any other blockage.
            MoveKind last = MoveKind.Follow;
            if (_jumpLeft > 0)
            {
                _jumpLeft--;
                last = MoveKind.CrouchJump;
            }

            return new MoveIntent(last, MoveSpeed.Walk, 1, Target, default, default, default, default, _graph.Cell(At).CrouchOnly, 0);
        }

        ReplanIfTheWayIsTaken();

        // Off the cells a partial field settled: ask again from here.
        if (!float.IsFinite(_field.CostAt(At.Index)) && _field.IsPartial)
            Rebuild();
        // A prop in the way of the only way out: a player shoves through it
        // (live, dm_lockdown: sinks and benches cut 1,756 cells off a spawn's
        // field, and a bot in there stood at "NoRoute" for good).
        if (!float.IsFinite(_field.CostAt(At.Index)) && !Shoving)
        {
            _obstacles = _obstacles.Shoved();
            Shoving = true;
            Rebuild();
        }

        if (!float.IsFinite(_field.CostAt(At.Index)))
        {
            state = FollowState.NoRoute;
            return BackToAField(origin);
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
        // corner 18 u behind it, for ever). A corner on another floor is not
        // stood by, and neither is the take-off of a link from another cell:
        // passed by the flat alone, the bot beside a ladder's foot and under
        // its top held there for good (live, dm_lockdown).
        int skip = 0;
        while (skip < _corners.Count - 2 && Passed(skip + 1, origin))
            skip++;
        _passed = skip;
        int count = Math.Min(MoveIntent.MaxCorners, _corners.Count - 1 - skip);
        // A link's take-off walked to is where the corners stop until the bot is
        // in its cell: the corner past it is another floor's, and steered to from
        // short of the take-off it pulled the bot back and away (live,
        // dm_lockdown's ladder at (-2720 5060): to and fro, never in the foot's
        // cell). A landing that is also a take-off (a rail's top) is flown over:
        // the corners run on, or the jump stops on the rail.
        for (int i = 0; i < count; i++)
        {
            int corner = i + 1 + skip;
            if (_corners[corner].Arrive == NavEdgeType.Walk && corner + 1 < _corners.Count && _corners[corner + 1].Arrive != NavEdgeType.Walk
                && _corners[corner].Point != _graph.Cell(At).Centre)
            {
                count = i + 1;
                break;
            }
        }

        NavEdgeType via = _field.ViaFrom(At.Index);
        AiVector overTheEdge = via == NavEdgeType.Drop ? PastTheLanding() : default;
        AiVector Corner(int i)
        {
            if (i >= count)
                return default;
            AiVector point = _corners[i + 1 + skip].Point;
            return i == 0 && via == NavEdgeType.Drop && point == _graph.Cell(_field.NextFrom(At.Index)).Centre ? overTheEdge : point;
        }

        AiVector? held = HoldCorner(via, count > 0 ? _corners[1 + skip] : default, origin);
        if (held is not null)
            count = 1;

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

        // A crouch-jump stays ducked until it lands, as a player's: past its
        // take-off the route is a walk again, and letting go in the air dropped
        // the feet 36 u, into the rail (live, ss_ai_nav).
        if (kind == MoveKind.CrouchJump)
            _tuckLeft = TuckMoves;
        else if (_tuckLeft > 0)
        {
            _tuckLeft--;
            if (origin.Z - _graph.Cell(At).FloorZ < 1f && _tuckLeft < TuckMoves - TuckRise)
                _tuckLeft = 0;
            else
                crouch = true;
        }

        state = FollowState.Moving;
        return new MoveIntent(kind, MoveSpeed.Run, (byte)Math.Max(count, 0), held ?? Corner(0), Corner(1), Corner(2), Corner(3), default, crouch, 0);
    }

    /// <summary>How much further from the objective than the bot a held corner may stand and still be on its way.</summary>
    public const float HoldCostSlack = 32f;

    private AiVector? _held;

    /// <summary>
    /// The corner a walking bot holds to, when it is not this tick's first: the
    /// one it was making for, until it has passed it or the route has really
    /// changed. Two routes round a pillar cost the same from where they part, so
    /// a field's next step differs from one cell to the next along that ridge,
    /// and a bot steering by the cell it is in turned back and forth on it (live:
    /// corners -2984 and -3176 in turn, tick by tick; 260 changes in 2,744 ticks,
    /// the strafe's jitter). Null: go by this tick's corners.
    /// </summary>
    private AiVector? HoldCorner(NavEdgeType via, PathCorner first, AiVector origin)
    {
        bool walking = via is NavEdgeType.Walk or NavEdgeType.Door && _avoided.Count == 0 && first.Arrive == NavEdgeType.Walk && first.Point != default;
        if (!walking)
        {
            _held = null;
            return null;
        }

        if (_held is { } h && h != first.Point && StillOnItsWay(h, origin))
            return h;
        _held = first.Point;
        return null;
    }

    private bool StillOnItsWay(AiVector corner, AiVector origin)
        => Flat(corner, origin) >= PassedRadius
           && CostAtCorner(corner) <= _field.CostAt(At.Index) + HoldCostSlack
           && ClearTo(corner, origin);

    /// <summary>Whether the straight line from where the bot is to <paramref name="to"/> is a walk, on its floor.</summary>
    public bool Clear(AiVector origin, AiVector to)
        => At.IsValid && _graph.WalkableLine(At.Index, origin, to with { Z = _graph.Cell(At).FloorZ }, Class, _obstacles);

    /// <summary>Whether the straight line to a corner is walkable up to it: a corner stands on a cell's side, and the cell beyond it may be no cell.</summary>
    private bool ClearTo(AiVector corner, AiVector origin)
    {
        var back = new AiVector(origin.X - corner.X, origin.Y - corner.Y, 0f).Normalised();
        return _graph.WalkableLine(At.Index, origin, corner + back, Class, _obstacles);
    }

    /// <summary>The field's cost at a corner: the least of the cells that meet there, since a corner stands on a cell's side.</summary>
    private float CostAtCorner(AiVector corner)
    {
        float best = float.PositiveInfinity;
        for (int dy = -1; dy <= 1; dy += 2)
        {
            for (int dx = -1; dx <= 1; dx += 2)
            {
                CellId c = _graph.FindCell(new AiVector(corner.X + dx, corner.Y + dy, corner.Z + 1f));
                if (c.IsValid)
                    best = MathF.Min(best, _field.CostAt(c.Index));
            }
        }

        return best;
    }

    /// <summary>The most cells <see cref="Route"/> walks on past the corridor, whatever the map.</summary>
    public const int RouteCells = 1024;

    private int _passed;

    /// <summary>
    /// The way the bot is going, as its last <see cref="Next"/> worked it out
    /// (the web UI's 3D view, plan_hl2mp_ai §7.1): where it stood, the corners
    /// it steers by, then the field's cells from the corridor's end on to the
    /// objective, a straight run of them as its two ends. Empty when that move
    /// had no way: arrived, off the graph, no route. Read on the main thread,
    /// which is the only one that moves this follower once it is driving.
    /// </summary>
    public void Route(List<AiVector> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        if (_corners.Count == 0)
            return;
        into.Add(_corners[0].Point);
        for (int i = 1 + _passed; i < _corners.Count; i++)
            into.Add(_corners[i].Point);
        if (_corridor.Count == 0 || _corridor[^1] == Objective)
            return;

        int at = _corridor[^1].Index;
        for (int i = 0; i < RouteCells && _field.NextFrom(at).IsValid; i++)
        {
            at = _field.NextFrom(at).Index;
            AiVector next = at == Objective.Index ? Target with { Z = _graph.Cell(Objective).FloorZ } : _graph.Cell(at).Centre;
            if (into.Count >= 2 && Straight(into[^2], into[^1], next))
                into[^1] = next;
            else
                into.Add(next);
            if (at == Objective.Index)
                break;
        }
    }

    /// <summary>Whether <paramref name="b"/> lies on the way from <paramref name="a"/> to <paramref name="c"/>.</summary>
    private static bool Straight(AiVector a, AiVector b, AiVector c)
    {
        AiVector first = (b - a).Normalised(), second = (c - b).Normalised();
        return first.X * second.X + first.Y * second.Y + first.Z * second.Z > 0.9995f;
    }

    /// <summary>How many moves a crouch-jump keeps the duck at most (0.8 s, its air time), and how many of them are the rise, when being on a floor is the take-off's.</summary>
    public const int TuckMoves = 53, TuckRise = 10;

    private int _tuckLeft;

    /// <summary>How far past a drop's landing a bot still on the edge is steered.</summary>
    public const float PastLanding = 24f;

    /// <summary>
    /// The way off a drop's edge: past the landing, so the body clears the lip.
    /// A box over the landing's column can still stand on the edge it came off
    /// (live, dm_lockdown's rail: stood on the walkway's lip at z 128, its centre
    /// on the landing, and turned back).
    /// </summary>
    private AiVector PastTheLanding()
    {
        AiVector landing = _graph.Cell(_field.NextFrom(At.Index)).Centre, here = _graph.Cell(At).Centre;
        AiVector way = new AiVector(landing.X - here.X, landing.Y - here.Y, 0f).Normalised();
        return landing + way * PastLanding;
    }

    /// <summary>A corner nearer the bot than this, flat, is one it has passed.</summary>
    public const float PassedRadius = 24f;

    private static float Flat(AiVector a, AiVector b) => new AiVector(a.X - b.X, a.Y - b.Y, 0f).Length;

    private bool Passed(int corner, AiVector origin)
    {
        AiVector point = _corners[corner].Point;
        if (Flat(point, origin) >= PassedRadius || MathF.Abs(point.Z - origin.Z) > OnFloorReach)
            return false;
        bool takeOff = corner + 1 < _corners.Count && _corners[corner + 1].Arrive != NavEdgeType.Walk;
        if (takeOff)
            return point == _graph.Cell(At).Centre;
        // A corner round a wall is not passed for being near through it (live,
        // dm_lockdown's balcony at (-3226 4438 128): steered past it, into the
        // wall, until the stuck detector).
        return corner + 1 >= _corners.Count || ClearTo(_corners[corner + 1].Point, origin);
    }

    /// <summary>How far above its floor a bot coming down is falling, not at the end of a jump: over a crouch-jump's reach.</summary>
    public const float FallReach = 64f;

    /// <summary>How far above a cell's floor a bot may stand and still be taken to be on it.</summary>
    public const float OnFloorReach = 24f;

    /// <summary>
    /// The cell a bot is in. A column's only cell can be a floor far below the
    /// bot: the column at a walkway's edge holds no body at the walkway's
    /// height (its box overlaps the rail) but a bot's centre crosses it, and
    /// taken to be on the floor under it the bot walked the floor's route, into
    /// the rail, for good (live, dm_lockdown: at z 128 "next Walk to" a cell at
    /// z 0). So a cell at the bot's feet near by comes first; the floor under
    /// it is for a bot falling to it: taken to be back on the walkway it had
    /// jumped from, a bot falling past the rail turned back to jump it again.
    /// </summary>
    private CellId Locate(AiVector origin)
    {
        CellId c = _graph.FindCell(origin + new AiVector(0f, 0f, 1f));
        bool admits = c.IsValid && _graph.Cell(c).Admits(Class);
        _falling = origin.Z < _lastZ - 1f && admits && origin.Z - _graph.Cell(c).FloorZ > FallReach;
        _lastZ = origin.Z;
        if (admits && (_falling || origin.Z - _graph.Cell(c).FloorZ <= OnFloorReach))
            return c;
        List<CellId> near = NavPoints.CellsNear(_graph, origin, Class);
        foreach (CellId n in near)
        {
            if (MathF.Abs(_graph.Cell(n).FloorZ - origin.Z) <= OnFloorReach && float.IsFinite(_field.CostAt(n.Index)))
                return n;
        }

        if (admits)
            return c;
        foreach (CellId n in near)
        {
            if (float.IsFinite(_field.CostAt(n.Index)))
                return n;
        }

        return c;
    }

    /// <summary>How far across a bot with no way from where it stands looks for a cell that has one.</summary>
    public const float AwayReach = 128f;

    /// <summary>How many moves the cell it heads back to is kept before it is looked for again.</summary>
    public const int AwayMoves = 33;

    private float _lastZ = float.NegativeInfinity;
    private bool _falling;
    private int _away = -1;
    private int _awayLeft;

    /// <summary>
    /// A bot where the graph has no way on (on top of a prop, a mid-air ledge
    /// the file does not stand on): the move to the nearest cell near by that
    /// has a way, jumping when it has been told it is stuck; Hold when none is near.
    /// </summary>
    private MoveIntent BackToAField(AiVector origin)
    {
        if (_awayLeft-- <= 0 || _away < 0)
        {
            _away = NearestWithAWay(origin);
            _awayLeft = AwayMoves;
        }

        if (_away < 0)
            return MoveIntent.Hold;
        AiVector to = _graph.Cell(_away).Centre;
        MoveKind kind = MoveKind.Follow;
        if (_jumpLeft > 0)
        {
            _jumpLeft--;
            kind = MoveKind.CrouchJump;
        }

        return new MoveIntent(kind, MoveSpeed.Run, 1, to, default, default, default, default, false, 0);
    }

    private int NearestWithAWay(AiVector origin)
    {
        int best = -1;
        float bestDistance = float.MaxValue;
        const float Step = 16f;
        for (float dy = -AwayReach; dy <= AwayReach; dy += Step)
        {
            for (float dx = -AwayReach; dx <= AwayReach; dx += Step)
            {
                CellId c = _graph.FindCell(new AiVector(origin.X + dx, origin.Y + dy, origin.Z + 18f));
                if (!c.IsValid || !_graph.Cell(c).Admits(Class) || !float.IsFinite(_field.CostAt(c.Index)))
                    continue;
                float distance = _graph.Cell(c).Centre.DistanceTo(origin);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = c.Index;
                }
            }
        }

        return best;
    }
}

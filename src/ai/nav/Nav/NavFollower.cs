namespace SourceSharp.Ai.Server.Nav;

/// <summary>Where a follower stands relative to its objective.</summary>
public enum FollowState : byte
{
    Moving = 0,
    Arrived,
    NoRoute,
    OffGraph,
}

/// <summary>
/// A lane's count of the flow fields its followers built, and how many of them on
/// the main thread: none while the AI's workers drive the bots (§2.7).
/// </summary>
public sealed class NavBuildTally
{
    /// <summary>The main thread, as the lane last saw it: the one handing its steps over.</summary>
    public int MainThread { get; set; }

    public long Builds { get; private set; }

    public long OnMain { get; private set; }

    public void Note()
    {
        Builds++;
        if (Environment.CurrentManagedThreadId == MainThread)
            OnMain++;
    }
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

    /// <summary>Where its field builds are counted, or null.</summary>
    public NavBuildTally? Tally { get; set; }

    /// <summary>(Re)builds the field: at the start, and when the obstacles change.</summary>
    public void Rebuild()
    {
        _builds++;
        Tally?.Note();
        _builtOver = _obstacles.Cells;
        _linksSeen = _obstacles.Links;
        _field.Build(_graph, Objective, Class, _obstacles, _avoided.Count > 0 ? _avoided : null, At, _avoidedLinks.Count > 0 ? _avoidedLinks : null);
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
        _lastOnFloor = old._lastOnFloor;
        _lastZ = old._lastZ;
        if (old._avoidLeft > 0)
        {
            _avoidLeft = old._avoidLeft;
            _avoided.UnionWith(old._avoided);
            _avoided.Remove(Objective.Index);
            _rebuildCarried = true;
        }

        // The links refused or failed, and a link under way or waited for: a hunt re-goals every half second, and
        // a wait that started over with each new goal would never end, nor a flight's landing be judged.
        if (ReferenceEquals(old._linksSeen, _obstacles.Links))
        {
            if (old._linkAvoidLeft > 0)
            {
                _linkAvoidLeft = old._linkAvoidLeft;
                _avoidedLinks.UnionWith(old._avoidedLinks);
                _healthAvoided.UnionWith(old._healthAvoided);
                _healthNeed = old._healthNeed;
                _rebuildCarried = true;
            }

            (_linkPhase, _link, _linkLeft, _linkFloor, _linkStill, _linkLastZ) = (old._linkPhase, old._link, old._linkLeft, old._linkFloor, old._linkStill, old._linkLastZ);
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

        LinksArrived();
        if (_linkAvoidLeft > 0 && --_linkAvoidLeft == 0)
        {
            _avoidedLinks.Clear();
            _healthAvoided.Clear();
            Rebuild();
        }

        HealthBack();

        At = Locate(origin);
        _corners.Clear();
        _corridor.Clear();
        _passed = 0;
        LinkStep = NavLinkStep.None;
        // A link begun: the program has the bot's commands; the follower only sees where it comes down.
        if (_linkPhase is LinkPhase.Begun or LinkPhase.Airborne)
        {
            FlyLink(origin);
            state = FollowState.Moving;
            return MoveIntent.Hold;
        }

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

        // No way for want of health that the health now covers (a cabinet): built again (lane G7a).
        if (!float.IsFinite(_field.CostAt(At.Index)) && _field.HealthShort <= _obstacles.LinkHealth)
            Rebuild();
        if (!float.IsFinite(_field.CostAt(At.Index)))
        {
            state = FollowState.NoRoute;
            LinkStep = NoWayStep();
            return BackToAField(origin);
        }

        _corridor.Clear();
        _corridor.Add(At);
        bool crouch = _graph.Cell(At).CrouchOnly;
        int link = _field.LinkFrom(At.Index);
        for (int at = At.Index, i = 0; link < 0 && i < CorridorCells && _field.NextFrom(at).IsValid; i++)
        {
            at = _field.NextFrom(at).Index;
            _corridor.Add(new CellId(at));
            if (i < CrouchLookahead && _graph.Cell(at).CrouchOnly)
                crouch = true;
            link = _field.LinkFrom(at);
        }

        if (link >= 0 && LeadToLink(link, origin) is { } leading)
        {
            state = FollowState.Moving;
            return leading;
        }

        if (link < 0)
            _linkPhase = LinkPhase.None;
        AiVector end = link >= 0 ? LinkStart(link)
                     : _corridor[^1] == Objective ? Target with { Z = _graph.Cell(Objective).FloorZ } : _graph.Cell(_corridor[^1]).Centre;
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
            return Afloat(i == 0 && via == NavEdgeType.Drop && point == _graph.Cell(_field.NextFrom(At.Index)).Centre ? overTheEdge : point);
        }

        // Afloat, a corner on the water's floor under the bot is at its own depth: the applier
        // passes a corner on another floor only once on top of it, and a swimmer 40 u over the
        // moat's floor circled the corner beside it (live, ctf_2fort). A corner above it stays.
        AiVector Afloat(AiVector point) => _swimming && point != default && point.Z < origin.Z ? point with { Z = origin.Z } : point;

        AiVector? held = link < 0 && HoldCorner(via, count > 0 ? _corners[1 + skip] : default, origin) is { } h ? Afloat(h) : null;
        if (held is not null)
            count = 1;

        MoveKind kind = via switch
        {
            // A ledge the derivation counts a plain jump's (rise to 39 u, a
            // jump's 21 plus a step's 18) is taken ducked: the plain jump
            // tops out near 20 u and the step does not come in the air (live,
            // ss_ai_nav's 32 u ledge: the bot rose to 35 and fell back, for
            // ever), and the duck tucks the feet up as a player's does.
            NavEdgeType.Jump or NavEdgeType.CrouchJump or NavEdgeType.DoubleJump => MoveKind.CrouchJump,
            _ => MoveKind.Follow,
        };
        kind = DoubleJump(via, kind, origin);
        // In water a player holds jump to rise: it swims up to the surface and climbs out at a bank
        // (the movement's water jump). Walking, a bot sank to the bottom of ctf_2fort's moat and
        // stood against its bank (live: 400 stuck reports under the bridge in eight minutes). But
        // held all the way it bobs at the surface, its feet out of the water as often as not and
        // the air's control its only one (live, 9acebc65f: swimmers drifted on the spot by the
        // moat's channels): only while the way on is higher than it can swim up to unaided.
        if (kind == MoveKind.Follow && (_graph.Cell(At).Flags & NavCellFlags.Water) != 0 && SwimsUp(origin, held ?? Corner(0)))
            kind = MoveKind.Jump;
        if (_jumpLeft > 0 && kind == MoveKind.Follow && _dash == DashPhase.None)
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

    // ------------------------------------------------------------------ links (lane G5c-2b)

    private enum LinkPhase : byte
    {
        None = 0,

        /// <summary>The bot stands at the link's start: the brain is told, and has <see cref="LinkHoldMoves"/> to begin it.</summary>
        Holding,

        /// <summary>The brain began it: the program runs up, aims and launches.</summary>
        Begun,

        /// <summary>Off the ground on it, until it comes down.</summary>
        Airborne,
    }

    private readonly HashSet<int> _avoidedLinks = new();
    private int _linkAvoidLeft;
    private NavLinks? _linksSeen;
    private LinkPhase _linkPhase;
    private int _link = -1, _linkLeft, _linkStill;
    private float _linkFloor;

    /// <summary>How near a link's start, flat, the bot stops and waits for the brain: the program walks the rest (its own side slack is 48 u).</summary>
    public const float LinkLeadRadius = 48f;

    /// <summary>How long the bot stands at a link's start for the brain to begin it (1 s: a decide comes every 6 ticks), before the link is left out for a while.</summary>
    public const int LinkHoldMoves = 66;

    /// <summary>How long a link refused or failed is left out of the route (8 s at a move a tick, as a stuck spot is).</summary>
    public const int LinkAvoidMoves = 533;

    /// <summary>How long a link begun may take to leave the ground (4 s: a program's approach is 3 s at most), and then to come down (5 s).</summary>
    public const int LinkLaunchMoves = 266, LinkFlightMoves = 333;

    /// <summary>How far over the start's floor the body is off the ground on a link, and how near the landing's cell, flat, it has come down on it.</summary>
    public const float LinkAirborneRise = 16f, LinkLandReach = 96f;

    /// <summary>How many moves the body's height must hold still on a floor for a flight to be over.</summary>
    public const int LinkStillMoves = 2;

    /// <summary>What the route's next link is, as the last <see cref="Next"/> found it: what the brain is told.</summary>
    public NavLinkStep LinkStep { get; private set; } = NavLinkStep.None;

    /// <summary>Whether the bot is at a link: standing at its start for the brain, or on it. The driver's watches leave it alone then.</summary>
    public bool InLink => _linkPhase != LinkPhase.None;

    /// <summary>The links left out of the route for now (refused or failed), by index.</summary>
    public IReadOnlyCollection<int> AvoidedLinks => _avoidedLinks;

    /// <summary>Links begun, landed on and failed (called off, fizzled, down elsewhere), and waits that ran out, for the counters.</summary>
    public int LinksBegun { get; private set; }

    public int LinksLanded { get; private set; }

    public int LinksFailed { get; private set; }

    public int LinkWaitsOut { get; private set; }

    /// <summary>
    /// The brain began link <paramref name="link"/> (its one-shot names it, <see cref="NavLinks.Argument"/>): if it
    /// is the link the bot stands at, the wait is over and the hands are off until it comes down.
    /// </summary>
    public void BeginLink(int link)
    {
        if (_linkPhase != LinkPhase.Holding || link != _link)
            return;
        _linkPhase = LinkPhase.Begun;
        _linkLeft = LinkLaunchMoves;
        LinksBegun++;
    }

    /// <summary>A new set of links (or none) since the field was built: built again, for an agent with a reach either set needs.</summary>
    private void LinksArrived()
    {
        NavLinks? now = _obstacles.Links;
        if (ReferenceEquals(now, _linksSeen))
            return;
        JumpReach needs = (now?.Reaches ?? JumpReach.None) | (_linksSeen?.Reaches ?? JumpReach.None);
        _linksSeen = now;
        _avoidedLinks.Clear();
        _healthAvoided.Clear();
        _linkAvoidLeft = 0;
        _linkPhase = LinkPhase.None;
        if ((_obstacles.Reach & needs) != 0 && _builds > 0)
            Rebuild();
    }

    /// <summary>Where a bot stands to take link <paramref name="link"/>: its start, or its takeoff cell's centre.</summary>
    private AiVector LinkStart(int link)
    {
        NavLink l = _field.Links![link];
        return l.Start != default ? l.Start : _graph.Cell(l.From).Centre;
    }

    /// <summary>
    /// The route's next step is <paramref name="link"/>: told to the brain, and the bot led to its start. Null while it
    /// is on its way there (the corners lead it, the corridor ending at the start); the hold at the start; or, when the
    /// link is no longer the bot's to take (its health has fallen under it) or the wait has run out, the link left out
    /// and the field built again, the bot holding this one move.
    /// </summary>
    private MoveIntent? LeadToLink(int link, AiVector origin)
    {
        _held = null;
        NavLinks links = _field.Links!;
        if (!links.Admits(_graph, link, Class, _obstacles))
        {
            AvoidLink(link, links.ShortOfHealth(_graph, link, Class, _obstacles));
            return MoveIntent.Hold;
        }

        AiVector start = LinkStart(link);
        bool there = Flat(start, origin) <= LinkLeadRadius && MathF.Abs(start.Z - origin.Z) <= OnFloorReach;
        if (!there)
        {
            _linkPhase = LinkPhase.None;
            LinkStep = new NavLinkStep(link, links.Hash, Target, false);
            return null;
        }

        if (_linkPhase != LinkPhase.Holding || _link != link)
            (_linkPhase, _link, _linkLeft, _linkFloor) = (LinkPhase.Holding, link, LinkHoldMoves, origin.Z);
        if (--_linkLeft < 0)
        {
            LinkWaitsOut++;
            AvoidLink(link);
            return MoveIntent.Hold;
        }

        LinkStep = new NavLinkStep(link, links.Hash, Target, true);
        return MoveIntent.Hold;
    }

    /// <summary>
    /// A link refused, failed or out of the bot's reach: left out for <see cref="LinkAvoidMoves"/>, and the field built again
    /// so the route goes round. One out of reach for the health alone (<paramref name="forHealth"/>) is let back sooner, as
    /// soon as the health covers it (<see cref="HealthBack"/>: a cabinet), and is what the bot needs (<see cref="NoWayStep"/>).
    /// </summary>
    private void AvoidLink(int link, bool forHealth = false)
    {
        _linkPhase = LinkPhase.None;
        _link = -1;
        _avoidedLinks.Add(link);
        _linkAvoidLeft = LinkAvoidMoves;
        if (forHealth && _field.Links is { } links && (uint)link < (uint)links.Count)
        {
            _healthAvoided.Add(link);
            _healthNeed = MathF.Min(_healthNeed, links[link].Health);
        }

        Rebuild();
    }

    // The links left out for the health alone (a subset of _avoidedLinks), and the least of their costs.
    private readonly HashSet<int> _healthAvoided = new();
    private float _healthNeed = float.PositiveInfinity;

    /// <summary>
    /// The links left out for want of health that the health now covers are let back, and the field built again (lane G7a):
    /// a bot refilled by a cabinet goes on at once, not after <see cref="LinkAvoidMoves"/>.
    /// </summary>
    private void HealthBack()
    {
        if (_healthAvoided.Count == 0)
        {
            _healthNeed = float.PositiveInfinity;
            return;
        }

        if (_obstacles.LinkHealth < _healthNeed || _field.Links is not { } links)
            return;
        _healthNeed = float.PositiveInfinity;
        foreach (int link in _healthAvoided.ToArray())
        {
            float cost = (uint)link < (uint)links.Count ? links[link].Health : 0f;
            if (cost <= _obstacles.LinkHealth)
            {
                _healthAvoided.Remove(link);
                _avoidedLinks.Remove(link);
            }
            else
            {
                _healthNeed = MathF.Min(_healthNeed, cost);
            }
        }

        if (_avoidedLinks.Count == 0)
            _linkAvoidLeft = 0;
        Rebuild();
    }

    /// <summary>
    /// What the bot is told when its route finds no way (lane G7a): no link, no way, and the least health a link costs that
    /// its route left out for the health alone (the follower's own, or the field's), 0 when none.
    /// </summary>
    private NavLinkStep NoWayStep()
    {
        float need = _healthAvoided.Count > 0 ? MathF.Min(_healthNeed, _field.HealthShort) : _field.HealthShort;
        return new NavLinkStep(-1, _field.Links?.Hash ?? 0UL, Target, false) { NoWay = true, Need = float.IsFinite(need) ? need : 0f };
    }

    /// <summary>
    /// A link under way: off the ground over its start's floor, then still on a floor. Down near its landing, on the
    /// landing's floor: done, and the route goes on from there. Down anywhere else, never off the ground in
    /// <see cref="LinkLaunchMoves"/>, or not down in <see cref="LinkFlightMoves"/>: failed, and left out for a while.
    /// </summary>
    private void FlyLink(AiVector origin)
    {
        if (_field.Links is not { } links || (uint)_link >= (uint)links.Count)
        {
            _linkPhase = LinkPhase.None;
            return;
        }

        NavLink l = links[_link];

        if (_linkPhase == LinkPhase.Begun)
        {
            if (origin.Z - _linkFloor > LinkAirborneRise)
                (_linkPhase, _linkLeft, _linkStill) = (LinkPhase.Airborne, LinkFlightMoves, 0);
            else if (--_linkLeft < 0)
                FailLink();
            return;
        }

        float floor = At.IsValid ? _graph.Cell(At).FloorZ : float.NegativeInfinity;
        bool onFloor = At.IsValid && origin.Z - floor <= OnFloorReach && origin.Z - floor >= -1f;
        _linkStill = onFloor && MathF.Abs(origin.Z - _linkLastZ) < 0.5f ? _linkStill + 1 : 0;
        _linkLastZ = origin.Z;
        if (_linkStill >= LinkStillMoves)
        {
            NavCell landing = _graph.Cell(l.To);
            bool landed = MathF.Abs(floor - landing.FloorZ) <= OnFloorReach && Flat(landing.Centre, origin) <= LinkLandReach;
            if (landed)
            {
                LinksLanded++;
                _linkPhase = LinkPhase.None;
                _link = -1;
            }
            else
            {
                FailLink();
            }

            return;
        }

        if (--_linkLeft < 0)
            FailLink();
    }

    private float _linkLastZ;

    private void FailLink()
    {
        LinksFailed++;
        AvoidLink(_link);
    }

    /// <summary>How far over a swimmer's feet the way on may rise before it holds jump to swim up to it: a step's height.</summary>
    public const float SwimUpRise = 18f;

    /// <summary>Whether a swimmer at <paramref name="origin"/> swims up: its steering corner, or the next cell's floor, is over its feet by more than <see cref="SwimUpRise"/>.</summary>
    private bool SwimsUp(AiVector origin, AiVector corner)
    {
        float way = corner == default ? float.NegativeInfinity : corner.Z;
        if (_field.NextFrom(At.Index) is { IsValid: true } next)
            way = MathF.Max(way, _graph.Cell(next).FloorZ);
        return way - origin.Z > SwimUpRise;
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
    /// or a lane's step, which moves this follower once it is driving and never both.
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

    /// <summary>How far over the take-off's floor a bot is in the air, for a double jump's second press.</summary>
    public const float AirborneRise = 1f;

    private enum DashPhase : byte
    {
        None = 0,
        Rising,
    }

    private DashPhase _dash;
    private float _dashFloor, _dashLastZ;
    private int _dashMoves;

    /// <summary>
    /// A <see cref="NavEdgeType.DoubleJump"/> (plan_bots_teams_tf2 C.7): the take-off is a
    /// crouch-jump as any other, pressed for one move; while the bot rises the jump is let go
    /// (the duck stays, <see cref="TuckMoves"/>), and once it stops rising it is pressed again,
    /// which a game that has the second jump answers (TF's Scout dashes on a fresh press in the
    /// air). Held through the rise, the applier's own let-go (every eighth tick of a held jump)
    /// pressed it afresh 0.12 s up: the dash went off 29 u over the floor instead of at the top's
    /// 52, and the jump fell 23 u short of the 117 the edge was built for. A bot that lands first, or
    /// has been in the air a crouch-jump's whole time, drops the second press.
    /// </summary>
    private MoveKind DoubleJump(NavEdgeType via, MoveKind kind, AiVector origin)
    {
        if (_dash == DashPhase.None)
        {
            if (via == NavEdgeType.DoubleJump && kind == MoveKind.CrouchJump)
            {
                _dash = DashPhase.Rising;
                _dashFloor = _graph.Cell(At).FloorZ;
                _dashLastZ = origin.Z;
                _dashMoves = 0;
            }

            return kind;
        }

        bool airborne = origin.Z - _dashFloor > AirborneRise;
        bool topped = airborne && origin.Z <= _dashLastZ;
        _dashLastZ = origin.Z;
        if (++_dashMoves > TuckMoves || (!airborne && _dashMoves > TuckRise))
        {
            _dash = DashPhase.None;
            return kind;
        }

        if (!topped)
            return kind == MoveKind.CrouchJump ? MoveKind.Follow : kind;
        _dash = DashPhase.None;
        return MoveKind.CrouchJump;
    }

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

    /// <summary>Whether <paramref name="origin"/> is past <paramref name="from"/> on the flat, on the side toward <paramref name="to"/>.</summary>
    private static bool Beyond(AiVector from, AiVector to, AiVector origin)
        => (origin.X - from.X) * (to.X - from.X) + (origin.Y - from.Y) * (to.Y - from.Y) > 0f;

    private bool Passed(int corner, AiVector origin)
    {
        AiVector point = _corners[corner].Point;
        if (MathF.Abs(point.Z - origin.Z) > (_swimming ? SwimReach : OnFloorReach))
            return false;
        bool takeOff = corner + 1 < _corners.Count && _corners[corner + 1].Arrive != NavEdgeType.Walk;
        // The bot's own cell's centre before a drop, the bot already beyond it toward the
        // landing: passed, however far (live, ctf_2fort's courtyard lip at (169 864 264): a drop
        // two columns on, the bot out of reach of the centre turned back to it, set off again
        // once inside reach, and so on, on the lip, for good).
        if (takeOff && _corners[corner + 1].Arrive == NavEdgeType.Drop && point == _graph.Cell(At).Centre
            && Beyond(point, _corners[corner + 1].Point, origin))
            return true;
        if (Flat(point, origin) >= PassedRadius)
            return false;
        if (takeOff)
            return point == _graph.Cell(At).Centre;
        // Afloat there is no wall to see round at the water's floor, and a moat's cells leave
        // columns out where its slope holds no body at the centre, so no line on its floor is
        // clear: live on ctf_2fort a swimmer steered at the corner under it, and drifted.
        if (_swimming)
            return true;
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
    /// How far above a water cell's floor a swimmer may float and still be in it: a body held up
    /// at the surface (ctf_2fort's moat, 136 u deep at its deepest, floats a TF2 body's feet 95 u
    /// over its floor).
    /// </summary>
    public const float SwimReach = 112f;

    private bool _swimming;

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
        _swimming = false;
        if (admits && (_falling || origin.Z - _graph.Cell(c).FloorZ <= OnFloorReach))
            return OnFloor(c, origin);
        // Afloat over a water cell's floor, a bot is in that cell: taken to be on a shallower
        // water cell beside it at its feet' height (a ramp's), a swimmer at the surface over
        // ctf_2fort's moat was sent back to the deep cell it floated over, and never reached it
        // (live: some 150 stuck reports at the moat's ramps in ten minutes).
        if (admits && (_graph.Cell(c).Flags & NavCellFlags.Water) != 0 && origin.Z - _graph.Cell(c).FloorZ <= SwimReach)
        {
            _swimming = true;
            return c;
        }

        List<CellId> near = NavPoints.CellsNear(_graph, origin, Class);
        foreach (CellId n in near)
        {
            if (MathF.Abs(_graph.Cell(n).FloorZ - origin.Z) <= OnFloorReach && float.IsFinite(_field.CostAt(n.Index)))
                return OnFloor(n, origin);
        }

        // Standing (a falling bot was answered above) on something the file has no place on, high over the only floor
        // under it: a sill or a parapet's top. Taken to be on that floor, the bot walked the
        // floor's route into the wall, for good (live, ctf_2fort: a battlement's crenel over the
        // moat, 480 u up, "next Walk to" a water cell). It is where it last stood on a floor, still
        // near its height: the way on, and a stuck report's way round, start there.
        if (admits && _lastOnFloor.IsValid && MathF.Abs(_graph.Cell(_lastOnFloor).FloorZ - origin.Z) <= FallReach)
            return _lastOnFloor;
        if (admits)
            return c;
        foreach (CellId n in near)
        {
            if (float.IsFinite(_field.CostAt(n.Index)))
                return n;
        }

        return c;
    }

    /// <summary><paramref name="c"/>, the cell the bot stands on at its feet' height: remembered as where it last stood on a floor.</summary>
    private CellId OnFloor(CellId c, AiVector origin)
    {
        if (MathF.Abs(_graph.Cell(c).FloorZ - origin.Z) <= OnFloorReach)
            _lastOnFloor = c;
        return c;
    }

    private CellId _lastOnFloor;

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

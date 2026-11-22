using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using TF2.Shared.Movement;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// A stretch a bot asks about (plan_bots_teams_tf2_part_g G.5 step 6): where it stands, the way it
/// is going over the ground (a unit vector), where it is going to, and the tick it asked on.
/// </summary>
public readonly record struct JumpStretchAsk(AiVector Origin, AiVector Heading, AiVector Goal, Tick At);

/// <summary>Why a stretch has no jump.</summary>
public enum JumpRefusal : byte
{
    None = 0,

    /// <summary>No way on foot from the bot to its goal, or the bot or the goal is off the graph.</summary>
    NoRoute,

    /// <summary>The bot is not going the way its route goes from here: it is turning onto it.</summary>
    Turning,

    /// <summary>The route does not go straight and level for long enough from here.</summary>
    Short,

    /// <summary>The search found no recipe that lands there.</summary>
    NoRecipe,

    /// <summary>The recipe it found lands in too few of its perturbed trials.</summary>
    Fragile,

    /// <summary>The flight comes too near a ceiling.</summary>
    Ceiling,

    /// <summary>The simulator's best is no faster than the walk over the same ground, or not by enough to be worth a rocket.</summary>
    NotFaster,

    /// <summary>The planner threw on the stretch (the desk's answer, so the stretch is not asked about over and over).</summary>
    Faulted,
}

/// <summary>
/// A jump forward along a bot's way, as the simulator found it for one stretch
/// (plan_bots_teams_tf2_part_g G.5 steps 2 and 6). <paramref name="Recipe"/> is the one the search
/// answered for that stretch, its takeoff ahead of where the bot asked from and its landing further
/// along the same line. <paramref name="Slide"/> is how far past the takeoff the jump may still be
/// made and land (a view that comes round late). <paramref name="Seconds"/> is the simulator's time
/// from standing at the recipe's start to the touch, <paramref name="AirSeconds"/> the part of it
/// in the air, and <paramref name="WalkSeconds"/> the walk's over the same ground at the class's
/// run. <paramref name="Health"/> is what the blast costs, <paramref name="Touch"/> where the
/// simulator comes down and <paramref name="Apex"/> how high it rises. <paramref name="Id"/> is the
/// book's number for it: what an ability one-shot carries to the program that flies it.
/// </summary>
public sealed record JumpPlan(JumpRecipe Recipe, AiVector Goal, float Slide, float Seconds, float AirSeconds, float WalkSeconds, float Health,
                              float Robustness, Vector Touch, float Apex)
{
    public int Id { get; init; }

    /// <summary>
    /// The jump from a standstill: a bot whose view was not round in time may stand and wait for
    /// it, then run <see cref="HoldRun"/> and jump, no later than the end of the slide. The
    /// simulator flew that from the slide's end and it landed; <see cref="HoldSeconds"/> is what it
    /// saved over the walk from where the bot stood, which is the longest the wait may be. Both 0
    /// when the simulator says there is no such jump: a late view then calls the jump off.
    /// </summary>
    public float HoldRun { get; init; }

    public float HoldSeconds { get; init; }

    /// <summary>
    /// Whether the jump is flown as the simulator flew it, from standing at the recipe's start (a blast link's, lane
    /// G5c-2b): the program walks the bot there, stands it still and waits for its view before it moves on to the
    /// jump. A stretch's plan runs at its takeoff from the bot's run.
    /// </summary>
    public bool StartsStanding { get; init; }

    /// <summary>
    /// The heal places within a walk of where the jump comes down, nearest first (lane G9, <see cref="JumpHeals"/>): what
    /// lets the tree begin it under the health floor (<see cref="JumpHealthRule"/>). None when the level's places were not
    /// known yet, or none is near.
    /// </summary>
    public System.Collections.Immutable.ImmutableArray<JumpHeal> Heals { get; init; } = [];

    /// <summary>
    /// Of <see cref="Heals"/>, those the jump is the quicker way to from where it starts (lane G9b): no walk reaches the place
    /// in the jump's seconds and the walk from its landing (<see cref="JumpHeals.WalkTo"/>). What a bot going for a pack by
    /// way of a jump looks for (<see cref="IJumpAdvisor.HealLinks"/>); a place a walk gets to sooner is walked to. Set for a
    /// level's links when they are published.
    /// </summary>
    public System.Collections.Immutable.ImmutableArray<JumpHeal> JumpWays { get; init; } = [];

    /// <summary>The direction of travel over the ground, a unit vector.</summary>
    public AiVector Way
    {
        get
        {
            float yaw = Recipe.TravelYaw * (MathF.PI / 180f);
            return new AiVector(MathF.Cos(yaw), MathF.Sin(yaw), 0f);
        }
    }

    /// <summary>How far before the takeoff's line <paramref name="origin"/> is along the way (negative past it), and how far off the line to a side.</summary>
    public (float Along, float Aside) Before(AiVector origin)
    {
        AiVector way = Way;
        float dx = Recipe.Takeoff.X - origin.X, dy = Recipe.Takeoff.Y - origin.Y;
        return (dx * way.X + dy * way.Y, MathF.Abs(dx * way.Y - dy * way.X));
    }
}

/// <summary>What a planner answered for a stretch: the plan, or why there is none, and the simulated flights it spent.</summary>
public readonly record struct JumpPlanned(JumpPlan? Plan, JumpRefusal Refusal, int Flights);

/// <summary>What the desk's worker asks of a level's planner.</summary>
public interface IJumpPlanner
{
    /// <summary>The plan for <paramref name="ask"/>, or why there is none.</summary>
    JumpPlanned Plan(in JumpStretchAsk ask);

    /// <summary>The simulator's flight of <paramref name="recipe"/> from <paramref name="start"/>.</summary>
    JumpOutcome Predict(in JumpRecipe recipe, in JumpStart start);

    /// <summary>The simulated flights flown and the floods of the graph made, so far.</summary>
    long Flights { get; }

    int Floods { get; }
}

/// <summary>A planner's numbers.</summary>
public sealed record JumpPlanSettings
{
    /// <summary>How far ahead of the asker, along its route, the takeoff is put: the plan is back, and the view round, before the bot is there.</summary>
    public float Lead { get; init; } = 224f;

    /// <summary>The shortest jump (takeoff to landing) asked of the search.</summary>
    public float MinJump { get; init; } = 512f;

    /// <summary>How far short of the straight's end (a corner, a wall, the goal) the landing is put.</summary>
    public float EndMargin { get; init; } = 96f;

    /// <summary>The share of the simulator's range a jump is planned within: well inside it, where a shot a little off still lands.</summary>
    public float ReachShare { get; init; } = 0.7f;

    /// <summary>A jump pays when its seconds are at most this share of the walk's over the same ground, and save at least <see cref="MinSavedSeconds"/>.</summary>
    public float PaysShare { get; init; } = 0.8f;

    public float MinSavedSeconds { get; init; } = 0.75f;

    /// <summary>The air a flight must keep over the body's head, all the way.</summary>
    public float Headroom { get; init; } = 12f;

    /// <summary>How far past the takeoff a late jump may still be made; halved once when the simulator says a jump from there does not land or pay.</summary>
    public float Slide { get; init; } = 192f;

    /// <summary>The run a bot that stood waiting for its view takes at the jump: far enough to be at its speed again. 0: no plan allows a wait.</summary>
    public float HoldRun { get; init; } = 64f;

    /// <summary>How far from the landing a jump may come to rest.</summary>
    public float Tolerance { get; init; } = 32f;

    /// <summary>How far apart in height the floor along the stretch may be: a step.</summary>
    public float FlatStep { get; init; } = 18f;

    /// <summary>The bot is going the route's way when its heading is within this of it (the cosine).</summary>
    public float HeadingDot { get; init; } = 0.97f;

    /// <summary>Route cells in a row a straight line from the bot may not reach before the route is taken to have turned.</summary>
    public int MissCells { get; init; } = 6;

    /// <summary>The route's cells looked through for the straight, at most.</summary>
    public int MaxLegCells { get; init; } = 512;

    /// <summary>Goals whose routes are kept (a flood of the graph each): the oldest goes.</summary>
    public int Fields { get; init; } = 6;
}

/// <summary>
/// Finds the jump for a stretch of a bot's route (plan_bots_teams_tf2_part_g G.5 step 6), or says why
/// there is none. Everything costly about a jump is here, off the brains and off the game's thread:
///
/// <list type="number">
/// <item><b>The route.</b> The bot's way to its goal over the graph (a field flooded back from the
/// goal's cell, kept for the next asks about that goal), followed from the bot's cell while it stays
/// level and a straight line from the bot to it is a walk: that is the straight ahead, measured along
/// the route and not to the goal. A bot not heading along it is turning; a straight too short for the
/// lead, a jump and the room to stop has no jump.</item>
/// <item><b>The recipe.</b> The takeoff <see cref="JumpPlanSettings.Lead"/> ahead of the bot, the
/// landing as far on as the straight and the simulator's range let it be; the search
/// (<see cref="JumpSearch"/>, forward, at most its 160 flights) answers the recipe.</item>
/// <item><b>Worth it.</b> Flown again: nothing nearer over its head than
/// <see cref="JumpPlanSettings.Headroom"/> anywhere on the flight, and its seconds under the walk's
/// over the same ground by <see cref="JumpPlanSettings.PaysShare"/> and
/// <see cref="JumpPlanSettings.MinSavedSeconds"/>.</item>
/// <item><b>The slide.</b> The same flight from further on, where a bot whose view came round late
/// would make it: it must land and pay from there too, or the plan allows less.</item>
/// <item><b>The hold.</b> The same flight from a standstill <see cref="JumpPlanSettings.HoldRun"/>
/// before the slide's end, the view round already: what a bot makes that stood and waited for its
/// view. It must land and keep its headroom; what it saves over the walk from there is how long the
/// plan lets a bot wait (<see cref="JumpPlan.HoldSeconds"/>).</item>
/// </list>
///
/// <para>
/// It holds one simulator, one search and its fields, so it serves one thread at a time. Nothing in
/// it is a number of the game's blast: the range and every time are the simulator's when asked.
/// </para>
/// </summary>
public sealed class JumpPlanner : IJumpPlanner
{
    private readonly NavGraph _graph;
    private readonly JumpSearch _search;
    private readonly JumpFlier _flier;
    private readonly JumpFlier _open;
    private readonly NavObstacleState _obstacles;
    private readonly float _runSpeed;
    private readonly JumpPlanSettings _settings;
    private readonly List<(int Goal, FlowField Field)> _fields = [];
    private readonly List<JumpPoint> _track = [];
    private JumpEnvelope? _envelope;

    public JumpPlanner(NavGraph graph, JumpSearch search, JumpFlier flier, JumpFlier open, NavObstacleState obstacles, float runSpeed, JumpPlanSettings settings)
    {
        _graph = graph;
        _search = search;
        _flier = flier;
        _open = open;
        _obstacles = obstacles;
        _runSpeed = runSpeed;
        _settings = settings;
    }

    /// <summary>
    /// A planner over <paramref name="graph"/>, flying <paramref name="sim"/> (the map's free space)
    /// and, for its reach, <paramref name="open"/> (a simulator of <see cref="JumpEnvelope.OpenAir"/>),
    /// with every door open and nothing known of walking but the class's run.
    /// </summary>
    public static JumpPlanner Of(NavGraph graph, BlastJumpSim sim, BlastJumpSim open, in JumpPilotLimits limits, JumpSearchSettings search, JumpPlanSettings settings)
    {
        var flier = new JumpFlier(sim, limits);
        return new JumpPlanner(graph, new JumpSearch(flier, new NoJumpWalk(), search), flier, new JumpFlier(open, limits),
                               new NavObstacleState(graph.ObstacleCount) { OpenDoors = true }, limits.RunSpeed, settings);
    }

    public JumpPlanSettings Settings => _settings;

    /// <summary>The simulated flights it has flown, its envelope's among them.</summary>
    public long Flights { get; private set; }

    /// <summary>The floods of the graph it has made: one a goal it had no field for.</summary>
    public int Floods { get; private set; }

    /// <summary>The simulator's reach in open air, measured at the first ask.</summary>
    public JumpEnvelope Envelope
    {
        get
        {
            if (_envelope is not { } known)
            {
                known = JumpEnvelope.Measure(_open, _search.Settings);
                Flights += known.Flights;
                _envelope = known;
            }

            return known;
        }
    }

    /// <summary>The plan for <paramref name="ask"/>, or why there is none: see the class.</summary>
    public JumpPlanned Plan(in JumpStretchAsk ask)
    {
        CellId from = _graph.FindCell(ask.Origin + new AiVector(0f, 0f, 1f));
        if (!from.IsValid || !_graph.Cell(from).Admits(ClearanceClass.Stand) || FieldTo(ask.Goal) is not { } field || !float.IsFinite(field.CostAt(from.Index)))
            return new JumpPlanned(null, JumpRefusal.NoRoute, 0);
        if (!Straight(ask.Origin, from, field, out AiVector way, out float length))
            return new JumpPlanned(null, JumpRefusal.Short, 0);
        if (way.Dot(ask.Heading) < _settings.HeadingDot)
            return new JumpPlanned(null, JumpRefusal.Turning, 0);

        // The straight first: one too short for the lead, a jump and the room to stop costs no flight.
        long before = Flights;
        float jump = length - _settings.Lead - _settings.EndMargin;
        if (jump < _settings.MinJump)
            return new JumpPlanned(null, JumpRefusal.Short, 0);
        jump = MathF.Min(jump, Envelope.Range * _settings.ReachShare);
        if (jump < _settings.MinJump)
            return new JumpPlanned(null, JumpRefusal.Short, (int)(Flights - before));

        float floor = _graph.Cell(from).FloorZ + VoxelMoveWorld.DistEpsilon;
        var takeoff = new Vector(ask.Origin.X + way.X * _settings.Lead, ask.Origin.Y + way.Y * _settings.Lead, floor);
        var landing = new Vector(takeoff.X + way.X * jump, takeoff.Y + way.Y * jump, floor);
        var wanted = new JumpAsk(takeoff, landing, _settings.Tolerance, JumpFamilies.Forward);
        JumpSearchResult result = _search.Find(wanted);
        Flights += result.Flights;
        if (result.Found is not { } found)
            return new JumpPlanned(null, JumpRefusal.NoRecipe, (int)(Flights - before));
        if (Fly(wanted, found.Recipe, out JumpOutcome flown, out float walk) is var why and not JumpRefusal.None)
            return new JumpPlanned(null, why, (int)(Flights - before));
        if (found.Robustness < _search.Settings.Keep)
            return new JumpPlanned(null, JumpRefusal.Fragile, (int)(Flights - before));

        // A jump made late, from further along the line: it must do as well, or the plan allows less.
        float slide = _settings.Slide;
        for (int halved = 0; halved < 2 && slide > 0f; halved++)
        {
            JumpRecipe late = found.Recipe with { Takeoff = new Vector(takeoff.X + way.X * slide, takeoff.Y + way.Y * slide, floor) };
            if (Fly(wanted, late, out _, out _) == JumpRefusal.None)
                break;
            slide = halved == 0 ? slide * 0.5f : 0f;
        }

        // A jump from a standstill at the end of the slide, the view round: it must land clear; what it saves is the wait it is worth.
        float holdRun = 0f, holdSeconds = 0f;
        if (_settings.HoldRun > 0f)
        {
            JumpRecipe held = found.Recipe with { Takeoff = new Vector(takeoff.X + way.X * slide, takeoff.Y + way.Y * slide, floor), RunUp = _settings.HoldRun };
            JumpRefusal says = Fly(wanted, held, held.Start() with { View = held.ShotView }, out JumpOutcome stood, out float afoot);
            if (says is JumpRefusal.None or JumpRefusal.NotFaster && afoot > stood.Seconds)
                (holdRun, holdSeconds) = (_settings.HoldRun, afoot - stood.Seconds);
        }

        var plan = new JumpPlan(found.Recipe, ask.Goal, slide, flown.Seconds, flown.AirSeconds, walk, flown.BlastDamage, found.Robustness, flown.Touch, flown.Apex)
        {
            HoldRun = holdRun, HoldSeconds = holdSeconds,
        };
        return new JumpPlanned(plan, JumpRefusal.None, (int)(Flights - before));
    }

    /// <summary>
    /// Flies <paramref name="recipe"/> from <paramref name="start"/> (a bot's own state when it
    /// launched) in the map's simulator: what the simulator says of the flight a bot made, to set
    /// beside where the game put it.
    /// </summary>
    public JumpOutcome Predict(in JumpRecipe recipe, in JumpStart start)
    {
        Flights++;
        return _flier.Fly(recipe, start, _search.Settings.MaxCommands);
    }

    /// <summary>
    /// One recipe flown from its own start and judged: it lands on the ask's landing, keeps its
    /// headroom, and beats the walk from its start to where it comes down.
    /// </summary>
    private JumpRefusal Fly(in JumpAsk ask, in JumpRecipe recipe, out JumpOutcome flown, out float walk) => Fly(ask, recipe, recipe.Start(), out flown, out walk);

    /// <summary>The same from <paramref name="start"/>, a body standing at the recipe's start.</summary>
    private JumpRefusal Fly(in JumpAsk ask, in JumpRecipe recipe, in JumpStart start, out JumpOutcome flown, out float walk)
    {
        _track.Clear();
        flown = _flier.Fly(recipe, start, _search.Settings.MaxCommands, _track);
        Flights++;
        walk = (recipe.RunUp + Flat(flown.Touch, recipe.Takeoff)) / _runSpeed;
        if (!_search.Made(ask, flown))
            return JumpRefusal.NoRecipe;
        if (!Clear(_track))
            return JumpRefusal.Ceiling;
        return flown.Seconds <= walk * _settings.PaysShare && walk - flown.Seconds >= _settings.MinSavedSeconds ? JumpRefusal.None : JumpRefusal.NotFaster;
    }

    /// <summary>Whether a tracked flight kept <see cref="JumpPlanSettings.Headroom"/> over its head while it was off the ground.</summary>
    private bool Clear(List<JumpPoint> track)
    {
        MovementParams body = TFViewVectors.Params;
        VoxelMoveWorld world = _flier.Sim.World;
        var rise = new Vector(0f, 0f, _settings.Headroom);
        foreach (JumpPoint p in track)
        {
            if (p.OnGround)
                continue;
            Vector maxs = p.Ducking ? body.DuckHullMax : body.HullMax, mins = p.Ducking ? body.DuckHullMin : body.HullMin;
            if (world.Trace(p.Origin, p.Origin + rise, mins, maxs, 0u, 0).Fraction < 1f)
                return false;
        }

        return true;
    }

    /// <summary>
    /// The straight of the route from <paramref name="origin"/>: the furthest of the route's cells,
    /// that a straight walk over level floor from the bot reaches (<see cref="LevelLine"/>), before
    /// the route turns away or leaves the level (<see cref="JumpPlanSettings.MissCells"/> cells in a
    /// row out of such a line's reach) or leaves the plain floor.
    /// </summary>
    private bool Straight(AiVector origin, CellId from, FlowField field, out AiVector way, out float length)
    {
        float floor = _graph.Cell(from).FloorZ;
        var feet = new AiVector(origin.X, origin.Y, floor);
        AiVector furthest = feet;
        length = 0f;
        int at = from.Index, misses = 0;
        for (int step = 0; step < _settings.MaxLegCells && misses <= _settings.MissCells; step++)
        {
            CellId next = field.NextFrom(at);
            if (!next.IsValid || field.ViaFrom(at) is not (NavEdgeType.Walk or NavEdgeType.Door))
                break;
            NavCell cell = _graph.Cell(next);
            at = next.Index;
            var centre = new AiVector(cell.Centre.X, cell.Centre.Y, cell.FloorZ);
            float far = (centre - feet).Length2D;
            if (far <= length)
                continue;
            if (LevelLine(from.Index, feet, centre, floor))
            {
                (furthest, length, misses) = (centre, far, 0);
            }
            else
            {
                misses++;
            }
        }

        way = length > 1f ? new AiVector((furthest.X - feet.X) / length, (furthest.Y - feet.Y) / length, 0f) : default;
        return length > 1f;
    }

    /// <summary>
    /// Whether the straight line over the ground from <paramref name="a"/> (in cell
    /// <paramref name="from"/>) to <paramref name="b"/> stays on level floor a standing body walks:
    /// every cell it crosses is level with <paramref name="floor"/> and admits it, and each is
    /// reached from the one before by a plain walk (or an open door), or, where the line crosses the
    /// corner four cells meet at, through one of the two cells beside that corner. The graph's own
    /// <see cref="NavGraph.WalkableLine"/> has no way across such a corner, which a long line a
    /// little off the grid's axes meets every few hundred units.
    /// </summary>
    private bool LevelLine(int from, AiVector a, AiVector b, float floor)
    {
        int at = from;
        float length = (b - a).Length2D;
        int samples = Math.Max(1, (int)MathF.Ceiling(length / 4f));
        for (int s = 1; s <= samples; s++)
        {
            float t = (float)s / samples, x = a.X + (b.X - a.X) * t, y = a.Y + (b.Y - a.Y) * t;
            if (Holds(at, x, y))
                continue;
            int next = Beside(at, x, y);
            if (next < 0)
            {
                // Across a corner: by way of a cell beside it.
                foreach (NavEdge e in _graph.EdgesFrom(at))
                {
                    if (Walked(e, floor) && (next = Beside(e.To, x, y)) >= 0)
                        break;
                }
            }

            if (next < 0 || MathF.Abs(_graph.Cell(next).FloorZ - floor) > _settings.FlatStep || !_graph.Cell(next).Admits(ClearanceClass.Stand))
                return false;
            at = next;
        }

        return true;
    }

    private bool Holds(int cell, float x, float y)
    {
        NavCell c = _graph.Cell(cell);
        return x >= c.MinX && x < c.MaxX && y >= c.MinY && y < c.MaxY;
    }

    /// <summary>The cell a plain walk from <paramref name="cell"/> reaches that holds the point, or -1.</summary>
    private int Beside(int cell, float x, float y)
    {
        foreach (NavEdge e in _graph.EdgesFrom(cell))
        {
            if (e.Type is NavEdgeType.Walk or NavEdgeType.Door && Holds(e.To, x, y) && _graph.Passable(e, ClearanceClass.Stand, _obstacles))
                return e.To;
        }

        return -1;
    }

    private bool Walked(in NavEdge e, float floor)
        => e.Type is NavEdgeType.Walk or NavEdgeType.Door && _graph.Passable(e, ClearanceClass.Stand, _obstacles)
           && MathF.Abs(_graph.Cell(e.To).FloorZ - floor) <= _settings.FlatStep;

    /// <summary>The field to the cell <paramref name="goal"/> is reached from, kept for the next asks about it; null when the goal is on no cell.</summary>
    private FlowField? FieldTo(AiVector goal)
    {
        CellId cell = NavPoints.CellNear(_graph, goal, ClearanceClass.Stand);
        if (!cell.IsValid)
            cell = _graph.FindCell(goal + new AiVector(0f, 0f, 1f));
        if (!cell.IsValid)
            return null;
        for (int i = 0; i < _fields.Count; i++)
        {
            if (_fields[i].Goal != cell.Index)
                continue;
            (int Goal, FlowField Field) held = _fields[i];
            _fields.RemoveAt(i);
            _fields.Add(held);
            return held.Field;
        }

        // The oldest field is filled again in place: its arrays are the graph's size already.
        FlowField field;
        if (_fields.Count >= _settings.Fields)
        {
            field = _fields[0].Field;
            _fields.RemoveAt(0);
        }
        else
        {
            field = new FlowField();
        }

        field.Build(_graph, cell, ClearanceClass.Stand, _obstacles);
        Floods++;
        _fields.Add((cell.Index, field));
        return field;
    }

    private static float Flat(Vector a, Vector b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}

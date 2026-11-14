using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>What a proposed jump is for.</summary>
public enum JumpPlaceKind : byte
{
    /// <summary>Onto a ledge from the lower ground before it.</summary>
    Up = 0,

    /// <summary>Onto a ledge from ground about as high, with lower ground between.</summary>
    Gap = 1,
}

/// <summary>
/// A jump worth searching for: from the cell and place it would take off at
/// to the cell and place it would land on, how much higher the landing is
/// and how far off over the ground, and what the walk between them costs in
/// units (null when there is no way on foot).
/// </summary>
public readonly record struct JumpCandidate(int From, int To, Vector Takeoff, Vector Landing, JumpPlaceKind Kind, float Rise, float Distance, float? WalkCost);

/// <summary>
/// A jump found: from which cell to which (the cell the recipe's flight comes
/// to rest in), the recipe and its family, the share of its perturbed trials
/// that landed, the seconds from its first command to the touch and the part
/// of them in the air, the health its blast costs, and the seconds the walk
/// between the two takes (null when there is none).
/// </summary>
public readonly record struct JumpLink(int From, int To, JumpRecipe Recipe, JumpFamily Family, float Robustness, float Seconds, float AirSeconds, float Health,
                                       float? WalkSeconds);

/// <summary>
/// What proposing looked through, for sizing its cost: the columns' edges, the
/// runs of them long enough to land on, and the landings whose way on foot
/// was asked (each a flood of the graph back from it, <see cref="GraphJumpWalk.Reaches"/>);
/// and the takeoffs passed over because no player gets there (<see cref="JumpPlaces"/>' seeds).
/// </summary>
public readonly record struct JumpPlacesWork(int Edges, int Runs, int Landings, int Unplayed = 0);

/// <summary>What the pass answered: what it proposed, the links the search found among them, the simulated flights it spent, and what proposing looked through.</summary>
public sealed record JumpPlacesResult(IReadOnlyList<JumpCandidate> Candidates, IReadOnlyList<JumpLink> Links, int Flights, JumpPlacesWork Work);

/// <summary>The pass's numbers.</summary>
public sealed record JumpPlacesSettings
{
    /// <summary>A drop a body cannot get back up on foot: past the highest rise the graph's own jumps take.</summary>
    public required float MinRise { get; init; }

    /// <summary>A crouched body's height: the air a takeoff needs over it past the landing's height.</summary>
    public required float CrouchHeight { get; init; }

    /// <summary>The shortest edge of a ledge that is a landing.</summary>
    public float MinRun { get; init; } = 64f;

    /// <summary>The shortest stretch of ground along the way out from an edge that is a takeoff.</summary>
    public float MinGround { get; init; } = 48f;

    /// <summary>How far in from its edge a landing is.</summary>
    public float Inset { get; init; } = 40f;

    /// <summary>How far out from the edge a jump up takes off, where the ground goes that far.</summary>
    public float Standoff { get; init; } = 160f;

    /// <summary>How far in from its own edge a jump over a gap takes off.</summary>
    public float GapInset { get; init; } = 32f;

    /// <summary>The share of the simulator's reach (its rise and its range) a jump is proposed within.</summary>
    public float ReachShare { get; init; } = 0.9f;

    /// <summary>A walk that costs no more than this many times the straight line is walked, not jumped.</summary>
    public float WalkFactor { get; init; } = 2.5f;

    /// <summary>How far from the landing a jump may come to rest (the search's tolerance).</summary>
    public float Tolerance { get; init; } = 24f;

    /// <summary>How far apart two floors may be and still be one level stretch.</summary>
    public float FlatStep { get; init; } = 1f;

    /// <summary>The most candidates searched, in the proposals' order; 0 searches every one.</summary>
    public int MaxCandidates { get; init; }

    /// <summary>The most rounds a pass with seeds goes on from where its links land (<see cref="JumpPlaces.Find"/>).</summary>
    public int MaxRounds { get; init; } = 4;

    /// <summary>
    /// How many takeoffs of a pair of level stretches are searched, best first, until one gives a
    /// link: the best, and alternates when it gives none (another edge of the ledge, another place
    /// on the ground). 1 searches only the best.
    /// </summary>
    public int PerPair { get; init; } = 3;

    /// <summary>
    /// The settings for a body that moves by <paramref name="numbers"/> (the
    /// graph's own derivation numbers): a drop past its highest crouched jump
    /// is one it cannot get back up, and its crouched height is the body's.
    /// </summary>
    public static JumpPlacesSettings Of(in NavNumbers numbers)
        => new() { MinRise = numbers.MaxCrouchJumpRise, CrouchHeight = numbers.CrouchHeight };
}

/// <summary>
/// Finds the places on a map worth a blast jump, and the recipe for each
/// (plan_bots_teams_tf2_part_g G.5 step 4): a pass over the bot's graph and
/// the nav's free space, per ledge and never per pair of cells.
///
/// <list type="number">
/// <item><b>Edges.</b> A column's floor is an edge toward a side when the
/// air beside it at that level goes down further than a body gets back up
/// on foot (<see cref="JumpPlacesSettings.MinRise"/>). Edges in a row, at
/// one height and facing one way, are a run; a run of
/// <see cref="JumpPlacesSettings.MinRun"/> or more is somewhere to land.</item>
/// <item><b>The landing.</b> One a run: its middle column,
/// <see cref="JumpPlacesSettings.Inset"/> in from the edge (the edge column
/// itself when no cell is that far in), where the graph has a cell at the
/// edge's height that a standing body fits in.</item>
/// <item><b>The takeoff.</b> Out from the middle of the run, straight away
/// from the ledge, a column at a time as far as the simulator's range and
/// until something stands in the way at the ledge's level: the cells under
/// that line, split into stretches of one level
/// (<see cref="JumpPlacesSettings.MinGround"/> or longer). A stretch lower
/// than the ledge is a jump <see cref="JumpPlaceKind.Up"/>, taking off
/// <see cref="JumpPlacesSettings.Standoff"/> out from the edge (or as near
/// that as the stretch goes); a stretch about as high as the ledge, which
/// the edge's own drop lies before, is a jump over a
/// <see cref="JumpPlaceKind.Gap"/>, taking off
/// <see cref="JumpPlacesSettings.GapInset"/> in from its own near end.</item>
/// <item><b>Worth it.</b> The rise within
/// <see cref="JumpPlacesSettings.ReachShare"/> of the simulator's
/// (<see cref="JumpEnvelope"/>; the way out from the edge is followed that
/// share of its range and no further); air over the takeoff up to the
/// landing's height and a crouched body more; and no way there on foot, or
/// one that costs over <see cref="JumpPlacesSettings.WalkFactor"/> times the
/// straight line.</item>
/// <item><b>One a pair.</b> Of the proposals from one level stretch of the
/// graph to another, one is kept: the one whose takeoff is nearest where it
/// was wanted (ground that stops short of the standoff has a wall close
/// behind the jump), then the one off the longest run, then the first
/// found. The next ones of other cells, up to
/// <see cref="JumpPlacesSettings.PerPair"/> in all, are its alternates: the
/// pass searches them in that order while none has given a link.</item>
/// <item><b>The search</b> on each (<see cref="JumpSearch"/>, both families,
/// the ask's own seed). A recipe that lands in
/// <see cref="JumpSearchSettings.Keep"/> of its trials is a link, to the
/// cell its unperturbed flight comes to rest in, when that cell's floor is
/// within the search's <see cref="JumpSearchSettings.HeightTolerance"/> of
/// where it rests.</item>
/// </list>
///
/// <para>
/// <b>What it does not find.</b> A landing along a ramp's or a stair's side
/// (no two columns of it are at one height), a ledge narrower than
/// <see cref="JumpPlacesSettings.MinRun"/>, a takeoff that is not straight
/// out from the middle of the ledge's edge, a jump down across a gap from
/// higher ground, and a jump along the flat for speed (nothing about the
/// ground says where one pays: JumpForwardSpeedTests times one).
/// </para>
///
/// <para>
/// The same graph, free space and settings give the same list in the same
/// order. It reads the graph and the columns, which are immutable, and holds
/// a search and a walk of its own, so one pass runs on any one thread, and
/// any number of passes run at once over one graph.
/// </para>
/// </summary>
public sealed class JumpPlaces
{
    private readonly NavGraph _graph;
    private readonly VoxelColumns _columns;
    private readonly JumpSearch _search;
    private readonly GraphJumpWalk _walk;
    private readonly JumpEnvelope _envelope;
    private readonly JumpPlacesSettings _settings;
    private readonly IReadOnlyList<int>? _seeds;
    private bool[]? _played;
    private int _unplayed;
    private readonly Dictionary<(int From, int To), List<JumpCandidate>> _alternates = [];

    /// <param name="seeds">
    /// Cells players start in (a map's spawns), or null or none for none known: then a takeoff
    /// is proposed only in a cell the graph's edges lead to from one of them (any edge, every door
    /// open). A map's navigation has free space a player never stands in (the tops of the clip
    /// brushes and cliffs round a TF2 map, the river a fall the graph takes no edge down to): a
    /// jump from there is on nobody's route.
    /// </param>
    public JumpPlaces(NavGraph graph, VoxelColumns columns, JumpSearch search, GraphJumpWalk walk, in JumpEnvelope envelope, JumpPlacesSettings settings,
                      IReadOnlyList<int>? seeds = null)
    {
        _graph = graph;
        _columns = columns;
        _search = search;
        _walk = walk;
        _envelope = envelope;
        _settings = settings;
        _seeds = seeds;
    }

    /// <summary>One column's edge: which way it faces (0 east, 1 north, 2 west, 3 south), the column across the run and along it, and its floor.</summary>
    private readonly record struct Rim(int Facing, int Across, int Along, float Floor);

    /// <summary>
    /// A proposal with what it is ranked by among its pair's: how far its
    /// takeoff is from where one was wanted, the run it lands off, and the
    /// two level stretches it joins.
    /// </summary>
    private readonly record struct Proposed(JumpCandidate Candidate, float Off, int Run, int FromLevel, int ToLevel);

    /// <summary>A cell under the line out from an edge, and how far out.</summary>
    private readonly record struct Ground(float Out, int Cell);

    /// <summary>
    /// The whole pass: the proposals, then the search on each. <paramref name="cancel"/> is
    /// looked at before each search, so a level that changes stops the pass within one
    /// candidate's flights (at most <see cref="JumpSearchSettings.MaxFlights"/>).
    ///
    /// <para>
    /// With seeds it goes in rounds: the takeoffs the graph leads to from the seeds first; then,
    /// while the links found land where the graph leads on to cells no round has played yet,
    /// those too (a ledge only a jump reaches is somewhere to jump on from), each candidate
    /// searched once, at most <see cref="JumpPlacesSettings.MaxRounds"/> rounds. The candidates
    /// answered are every one proposed, in the order of their landing cell and then their takeoff's.
    /// </para>
    /// </summary>
    public JumpPlacesResult Find(CancellationToken cancel = default)
    {
        _played = _seeds is { Count: > 0 } ? Played(_seeds) : null;
        var proposed = new List<JumpCandidate>();
        var asked = new HashSet<(int From, int To)>();
        var links = new List<JumpLink>();
        var linkOf = new List<(int To, int From)>();
        int flights = 0, searched = 0;
        JumpPlacesWork work = default;
        for (int round = 0; round < Math.Max(1, _settings.MaxRounds); round++)
        {
            IReadOnlyList<JumpCandidate> candidates = ProposeNow(out work);
            int linked = links.Count;
            bool any = false;
            foreach (JumpCandidate best in candidates)
            {
                // A pair an earlier round searched is done with. Else its best, then its alternates while none has given a link.
                if (asked.Contains((best.From, best.To)))
                    continue;
                IEnumerable<JumpCandidate> tries = _alternates.TryGetValue((best.From, best.To), out List<JumpCandidate>? others) ? [best, .. others] : [best];
                foreach (JumpCandidate c in tries)
                {
                    if (!asked.Add((c.From, c.To)))
                        continue;
                    any = true;
                    proposed.Add(c);
                    if (_settings.MaxCandidates > 0 && searched >= _settings.MaxCandidates)
                        continue;
                    searched++;
                    cancel.ThrowIfCancellationRequested();
                    if (Search(c, ref flights) is { } link)
                    {
                        links.Add(link);
                        linkOf.Add((c.To, c.From));
                        break;
                    }
                }
            }

            // On from where the new links land, when that plays a cell no round has.
            if (_played is null || !any || !Spread(_played, links.Skip(linked).Select(l => l.To)))
                break;
        }

        // Both in the order of the candidate's landing cell and then its takeoff's, whichever round found them.
        proposed.Sort(static (a, b) => a.To != b.To ? a.To.CompareTo(b.To) : a.From.CompareTo(b.From));
        JumpLink[] ordered = [.. Enumerable.Range(0, links.Count).OrderBy(i => linkOf[i].To).ThenBy(i => linkOf[i].From).Select(i => links[i])];
        return new JumpPlacesResult(proposed, ordered, flights, work);
    }

    /// <summary>
    /// The search on <paramref name="c"/>: the link, or null. Where its flight comes to rest is the cell the link goes to:
    /// one at the height it rests at. Where the graph has only a floor further down (a ledge's rim no cell of the graph
    /// reaches), the bot would stand nowhere it knows, and there is no link.
    /// </summary>
    private JumpLink? Search(in JumpCandidate c, ref int flights)
    {
        JumpSearchResult result = _search.Find(new JumpAsk(c.Takeoff, c.Landing, _settings.Tolerance));
        flights += result.Flights;
        if (result.Found is not { } found || found.Robustness < _search.Settings.Keep)
            return null;
        JumpOutcome flown = _search.Fly(found.Recipe);
        flights++;
        CellId to = _graph.FindCell(new AiVector(flown.Rest.X, flown.Rest.Y, flown.Rest.Z));
        return to.IsValid && MathF.Abs(_graph.Cell(to).FloorZ - flown.Rest.Z) <= _search.Settings.HeightTolerance
            ? new JumpLink(c.From, to.Index, found.Recipe, found.Family, found.Robustness, found.Seconds, found.AirSeconds, found.Health, found.WalkSeconds)
            : null;
    }

    /// <summary>The jumps worth searching for, one a pair of level stretches, in the order of their landing cell and then their takeoff cell. No flight is flown.</summary>
    public IReadOnlyList<JumpCandidate> Propose() => Propose(out _);

    /// <summary><see cref="Propose()"/>, with what it looked through: the first round's (<see cref="Find"/>), from the seeds alone.</summary>
    public IReadOnlyList<JumpCandidate> Propose(out JumpPlacesWork work)
    {
        _played = _seeds is { Count: > 0 } ? Played(_seeds) : null;
        return ProposeNow(out work);
    }

    /// <summary>The proposals from the cells played so far.</summary>
    private IReadOnlyList<JumpCandidate> ProposeNow(out JumpPlacesWork work)
    {
        _unplayed = 0;
        int[] level = Levels();
        var rims = new List<Rim>();
        for (int y = 0; y < _columns.Depth; y++)
        {
            for (int x = 0; x < _columns.Width; x++)
            {
                foreach (FreeSpan span in _columns.Spans(x, y))
                {
                    for (int facing = 0; facing < 4; facing++)
                    {
                        (int dx, int dy) = Step(facing);
                        if (Below(x + dx, y + dy, span.Bottom) is { } beside && span.Bottom - beside > _settings.MinRise)
                            rims.Add(new Rim(facing, facing % 2 == 0 ? x : y, facing % 2 == 0 ? y : x, span.Bottom));
                    }
                }
            }
        }

        rims.Sort(static (a, b) => a.Facing != b.Facing ? a.Facing.CompareTo(b.Facing)
            : a.Across != b.Across ? a.Across.CompareTo(b.Across)
            : a.Floor != b.Floor ? a.Floor.CompareTo(b.Floor) : a.Along.CompareTo(b.Along));
        var kept = new List<Proposed>();
        var ground = new List<Ground>();
        int runs = 0, landings = 0;
        for (int i = 0; i < rims.Count;)
        {
            int end = i;
            while (end + 1 < rims.Count && rims[end + 1].Facing == rims[i].Facing && rims[end + 1].Across == rims[i].Across && rims[end + 1].Floor == rims[i].Floor
                   && rims[end + 1].Along == rims[end].Along + 1)
            {
                end++;
            }

            int run = end - i + 1;
            if (run * _columns.VoxelSize >= _settings.MinRun)
            {
                runs++;
                landings += ProposeFrom(rims[i + run / 2], run, level, kept, ground) ? 1 : 0;
            }

            i = end + 1;
        }

        work = new JumpPlacesWork(rims.Count, runs, landings, _unplayed);

        // One a pair: the takeoff nearest where one was wanted (ground that stops short of the standoff is a wall close
        // behind the jump), then the longer run; the first found among equals. The next ones (up to PerPair in all, of
        // other cells) are the pair's alternates, searched by Find when the first gives no link.
        _alternates.Clear();
        var candidates = new List<JumpCandidate>();
        foreach (IGrouping<(int, int), Proposed> pair in kept.GroupBy(static p => (p.FromLevel, p.ToLevel)))
        {
            Proposed[] ranked = [.. pair.OrderBy(static p => p.Off).ThenByDescending(static p => p.Run)];
            JumpCandidate first = ranked[0].Candidate;
            candidates.Add(first);
            var others = new List<JumpCandidate>();
            foreach (Proposed p in ranked.Skip(1))
            {
                if (others.Count + 1 >= _settings.PerPair)
                    break;
                if ((p.Candidate.From, p.Candidate.To) != (first.From, first.To) && !others.Any(o => (o.From, o.To) == (p.Candidate.From, p.Candidate.To)))
                    others.Add(p.Candidate);
            }

            if (others.Count > 0)
                _alternates[(first.From, first.To)] = others;
        }

        candidates.Sort(static (a, b) => a.To != b.To ? a.To.CompareTo(b.To) : a.From.CompareTo(b.From));
        return candidates;
    }

    /// <summary>The proposals that land off the run whose middle edge is <paramref name="rim"/>; true when the way on foot to its landing was asked.</summary>
    private bool ProposeFrom(in Rim rim, int run, int[] level, List<Proposed> kept, List<Ground> ground)
    {
        float voxel = _columns.VoxelSize;
        (int dx, int dy) = Step(rim.Facing);
        int column = rim.Facing % 2 == 0 ? rim.Across : rim.Along, row = rim.Facing % 2 == 0 ? rim.Along : rim.Across;
        float edgeX = _columns.OriginX + (column + 0.5f + dx * 0.5f) * voxel, edgeY = _columns.OriginY + (row + 0.5f + dy * 0.5f) * voxel;

        // The landing: in from the edge, or the edge column itself when no cell is that far in.
        CellId landingCell = CellId.None;
        float inset = _settings.Inset;
        for (int attempt = 0; attempt < 2 && !landingCell.IsValid; attempt++)
        {
            inset = attempt == 0 ? _settings.Inset : voxel * 0.5f;
            CellId cell = _graph.FindCell(new AiVector(edgeX - dx * inset, edgeY - dy * inset, rim.Floor + 1f));
            if (cell.IsValid && MathF.Abs(_graph.Cell(cell).FloorZ - rim.Floor) <= _settings.FlatStep && _graph.Cell(cell).Admits(ClearanceClass.Stand))
                landingCell = cell;
        }

        if (!landingCell.IsValid)
            return false;
        var landing = new Vector(edgeX - dx * inset, edgeY - dy * inset, _graph.Cell(landingCell).FloorZ + VoxelMoveWorld.DistEpsilon);

        // Out from the edge a column at a time: the cells under the line, in stretches of one level.
        float reach = _envelope.Range * _settings.ReachShare;
        int stretch = -1;
        bool asked = false;
        ground.Clear();
        for (int k = 1; ; k++)
        {
            float outward = (k - 0.5f) * voxel;
            if (outward > reach || Below(column + k * dx, row + k * dy, rim.Floor) is null)
                break;
            CellId cell = _graph.FindCell(new AiVector(edgeX + dx * outward, edgeY + dy * outward, rim.Floor + 1f));
            int here = cell.IsValid ? level[cell.Index] : -1;
            if (here != stretch)
            {
                asked |= Consider(rim, run, landing, landingCell, level, kept, ground, edgeX, edgeY, dx, dy);
                ground.Clear();
                stretch = here;
            }

            if (cell.IsValid)
                ground.Add(new Ground(outward, cell.Index));
        }

        return asked | Consider(rim, run, landing, landingCell, level, kept, ground, edgeX, edgeY, dx, dy);
    }

    /// <summary>One level stretch of ground out from an edge: the jump from it, when it is worth one. True when the way on foot from it was asked.</summary>
    private bool Consider(in Rim rim, int run, Vector landing, CellId landingCell, int[] level, List<Proposed> kept, List<Ground> ground,
                          float edgeX, float edgeY, int dx, int dy)
    {
        if (ground.Count == 0 || ground.Count * _columns.VoxelSize < _settings.MinGround)
            return false;

        // Lower than the ledge by more than a body jumps: a jump up. Else it is ground about as high, and the way out to
        // it began over the edge's own drop: a gap.
        float floor = _graph.Cell(ground[0].Cell).FloorZ, rise = rim.Floor - floor;
        bool up = rise > _settings.MinRise;
        if (rise > _envelope.Rise * _settings.ReachShare)
            return false;

        // Where on the stretch: a standoff out from the edge for a jump up, a little in from its own near end for a gap.
        float wanted = up ? _settings.Standoff : ground[0].Out + _settings.GapInset;
        Ground from = ground[0];
        foreach (Ground g in ground)
        {
            if (MathF.Abs(g.Out - wanted) < MathF.Abs(from.Out - wanted))
                from = g;
        }

        // Nobody stands there: no edge of the graph leads there from a seed.
        if (_played is not null && !_played[from.Cell])
        {
            _unplayed++;
            return false;
        }

        var takeoff = new Vector(edgeX + dx * from.Out, edgeY + dy * from.Out, _graph.Cell(from.Cell).FloorZ + VoxelMoveWorld.DistEpsilon);
        float flat = MathF.Sqrt((landing.X - takeoff.X) * (landing.X - takeoff.X) + (landing.Y - takeoff.Y) * (landing.Y - takeoff.Y));

        // Air over the takeoff up to the landing's height and a crouched body more.
        if (!(Above(_columns.ColumnX(takeoff.X), _columns.ColumnY(takeoff.Y), takeoff.Z) >= MathF.Max(landing.Z, takeoff.Z) + _settings.CrouchHeight))
            return false;

        // On foot: no way, or one that costs much more than the straight line.
        float? walk = _walk.Cost(new CellId(from.Cell), landingCell);
        float straight = MathF.Sqrt(flat * flat + (landing.Z - takeoff.Z) * (landing.Z - takeoff.Z));
        if (walk is { } cost && cost <= _settings.WalkFactor * straight)
            return true;

        kept.Add(new Proposed(new JumpCandidate(from.Cell, landingCell.Index, takeoff, landing, up ? JumpPlaceKind.Up : JumpPlaceKind.Gap, rise, flat, walk),
                              MathF.Abs(from.Out - wanted), run, level[from.Cell], level[landingCell.Index]));
        return true;
    }

    /// <summary>The cells the graph's edges lead to from <paramref name="seeds"/>, the seeds among them.</summary>
    private bool[] Played(IReadOnlyList<int> seeds)
    {
        var played = new bool[_graph.CellCount];
        Spread(played, seeds);
        return played;
    }

    /// <summary>Plays <paramref name="from"/> and every cell the graph's edges lead to from them; true when a cell was not played before.</summary>
    private bool Spread(bool[] played, IEnumerable<int> from)
    {
        var open = new Stack<int>();
        foreach (int seed in from)
        {
            if ((uint)seed < (uint)played.Length && !played[seed])
            {
                played[seed] = true;
                open.Push(seed);
            }
        }

        bool any = open.Count > 0;
        while (open.Count > 0)
        {
            foreach (NavEdge e in _graph.EdgesFrom(open.Pop()))
            {
                if (!played[e.To])
                {
                    played[e.To] = true;
                    open.Push(e.To);
                }
            }
        }

        return any;
    }

    /// <summary>Which level stretch each cell is in: cells joined by walks between floors no more than <see cref="JumpPlacesSettings.FlatStep"/> apart.</summary>
    private int[] Levels()
    {
        var level = new int[_graph.CellCount];
        Array.Fill(level, -1);
        var open = new Stack<int>();
        int next = 0;
        for (int start = 0; start < level.Length; start++)
        {
            if (level[start] >= 0)
                continue;
            level[start] = next;
            open.Push(start);
            while (open.Count > 0)
            {
                int cell = open.Pop();
                float floor = _graph.Cell(cell).FloorZ;
                foreach (NavEdge e in _graph.EdgesFrom(cell))
                {
                    if (e.Type == NavEdgeType.Walk && level[e.To] < 0 && MathF.Abs(_graph.Cell(e.To).FloorZ - floor) <= _settings.FlatStep)
                    {
                        level[e.To] = next;
                        open.Push(e.To);
                    }
                }
            }

            next++;
        }

        return level;
    }

    private static (int X, int Y) Step(int facing) => facing switch
    {
        0 => (1, 0),
        1 => (0, 1),
        2 => (-1, 0),
        _ => (0, -1),
    };

    /// <summary>The floor of the air in column (<paramref name="x"/>, <paramref name="y"/>) at just over <paramref name="height"/>, or null when it is solid there (or outside the grid).</summary>
    private float? Below(int x, int y, float height)
    {
        foreach (FreeSpan span in _columns.Spans(x, y))
        {
            if (span.Bottom <= height + 1f && height + 1f < span.Top)
                return span.Bottom;
        }

        return null;
    }

    /// <summary>The top of the air in column (<paramref name="x"/>, <paramref name="y"/>) at just over <paramref name="height"/>, or NaN when it is solid there.</summary>
    private float Above(int x, int y, float height)
    {
        foreach (FreeSpan span in _columns.Spans(x, y))
        {
            if (span.Bottom <= height + 1f && height + 1f < span.Top)
                return span.Top;
        }

        return float.NaN;
    }
}

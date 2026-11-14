using SourceSharp.Host.Contracts;

namespace TF2.Descent.Rules.Layout;

/// <summary>A cell of a level grid: row 0 is the south row, column 0 the west column.</summary>
public readonly record struct GridCell(int Row, int Column);

/// <summary>
/// What a generated level is, beyond the plan the host links: the facts the
/// property tests and the pool's logs read.
/// </summary>
/// <param name="CriticalPath">The arrival to the exit (or, at 15, the arena), through joined doors; a shortest walk.</param>
/// <param name="Rooms">Rooms placed.</param>
/// <param name="Joints">Pairs of doors joined across a shared wall.</param>
/// <param name="Loops">Independent cycles of the door graph (joints - rooms + 1).</param>
/// <param name="BranchRooms">Rooms off the critical path, the treasure included.</param>
/// <param name="TreasurePlaced">A treasure room stands off the critical path.</param>
/// <param name="STierSpots">Scout-tier spots the placed rooms offer.</param>
/// <param name="BTierSpots">Blast-tier spots the placed rooms offer.</param>
/// <param name="JumpFallback">The depth's jump minimums are not met; <paramref name="JumpFallbackReason"/> says why.</param>
/// <param name="JumpFallbackReason">Null when the minimums are met.</param>
/// <param name="CappedDoors">Doors facing the grid's edge or an empty cell (the link caps them).</param>
/// <param name="WastedDoors">Doors facing a neighbour's blank wall (capped too, but placed only when nothing else fits).</param>
/// <param name="Attempts">Grids tried before this one.</param>
/// <param name="AntechamberPlaced">A boss level holds its antechamber (false when the pool has none).</param>
/// <param name="DeadEnds">Rooms with exactly one joined door, but the arrival and the exit (M18).</param>
/// <param name="DeadEndConnectors">Dead ends whose room is a connector, not a destination; zero unless <paramref name="DeadEndFallbackReason"/> says why.</param>
/// <param name="DeadEndFallbackReason">Null when every dead end is a destination and their count is within the depth's cap.</param>
/// <param name="RoomTarget">The rooms the depth asks for (M19's budget); <paramref name="Rooms"/> falls short only when no attempt reached it.</param>
/// <param name="FillRatio">Rooms over the cells of the plan's bounding box.</param>
/// <param name="MeanNeighbours">Placed rooms beside each room (joined or not), on average: 2 is a tunnel, 4 a filled block.</param>
/// <param name="LongestStraightRun">The most rooms in one straight line along the critical path.</param>
/// <param name="TallClusterMean">The mean size of the groups of joined halls and shafts (worked-out areas); 0 with none.</param>
/// <remarks>
/// <see cref="MultiCellRooms"/> counts the rooms larger than one cell (each once, as every room is
/// counted), and <see cref="MultiCellCaps"/> their sockets the layout leaves unjoined: a larger room
/// has a socket on every cell edge of its perimeter and the link caps the ones it does not join, so
/// those caps are the room's walls, not doors the layout failed to join (they are not in
/// <see cref="CappedDoors"/> or <see cref="WastedDoors"/>).
/// </remarks>
public sealed record LayoutReport(
    IReadOnlyList<GridCell> CriticalPath,
    int Rooms,
    int Joints,
    int Loops,
    int BranchRooms,
    bool TreasurePlaced,
    int STierSpots,
    int BTierSpots,
    bool JumpFallback,
    string? JumpFallbackReason,
    int CappedDoors,
    int WastedDoors,
    int Attempts,
    bool AntechamberPlaced,
    int DeadEnds,
    int DeadEndConnectors,
    string? DeadEndFallbackReason,
    int RoomTarget,
    double FillRatio,
    double MeanNeighbours,
    int LongestStraightRun,
    double TallClusterMean)
{
    /// <summary>Rooms larger than one cell placed.</summary>
    public int MultiCellRooms { get; init; }

    /// <summary>The unjoined sockets of the rooms larger than one cell, capped by the link as walls.</summary>
    public int MultiCellCaps { get; init; }
}

/// <summary>A generated level, or why there is none.</summary>
public sealed record LayoutResult(LevelPlan? Plan, LayoutReport? Report, string? Error)
{
    public bool Ok => Plan is not null;
}

/// <summary>
/// The level layout generator (task C1, C3; plan_tf2_rpg 6b, plan_descent_mine_levels
/// sections 7 and 10; M16, M18, M19). Pure and deterministic: the same depth, seed and
/// rooms give the same plan, with no clock, no I/O and its own seeded generator. The pool
/// is read by name order, so the order the host lists rooms in does not matter.
/// <list type="number">
/// <item>A normal level grows from the middle of a virtual grid to exactly its room
/// budget (<see cref="DepthProfile.Rooms"/>), in any shape whose bounding box stays within
/// <see cref="DepthProfile.MaxSpan"/> cells a side (M19); the plan is that box, cropped
/// to start at row 0, column 0.</item>
/// <item>A critical path of <see cref="DepthProfile.PathMin"/> to <see cref="DepthProfile.PathMax"/>
/// rooms: a self-avoiding walk that carries straight on with the depth's
/// <see cref="DepthProfile.Straightness"/>, shuns cells beside its own earlier rooms (it
/// wanders rather than folds back), and whose ends stand at least half its steps apart.</item>
/// <item>A treasure room off the path, one or two cells out along a branch.</item>
/// <item>Branches until the budget. Each step closes a loop (a new room joined to two placed
/// ones, while the level is under its <see cref="DepthProfile.Loops"/>), extends a side
/// tunnel (favoured until it is <see cref="DepthProfile.BranchLength"/> rooms long), or starts
/// one where it can end in a destination, steered towards the depth's
/// <see cref="DepthProfile.EndSharePercent"/> of tunnel ends (M20: a tunnel that ends in a room
/// is no problem; only a hallway that stops is, and none does).
/// Every candidate cell is weighted by its placed neighbours as the depth's
/// <see cref="DepthProfile.Sprawl"/> says: thin tunnels reaching out, or compact workings.
/// A two-door arrival or exit joins its spare door to a branch rather than capping it.</item>
/// <item>Loops: joints between neighbouring rooms, the branch ends first, kept only
/// when they leave the critical path the shortest way through (no loop is a shortcut).
/// Over the dead-end cap, branch ends are joined to a neighbour, and only then pruned and
/// their rooms grown again elsewhere.</item>
/// <item>Rooms: each cell takes a room and a quarter turn whose doors are exactly
/// the cell's joints when the pool has one; else a room whose extra doors face the
/// edge or an empty cell (capped); else, last, one whose extra door faces a
/// neighbour's blank wall (capped, wasted). A door never faces a neighbour's door
/// it is not meant to join, so the joints are the ones the layout chose and the
/// level never relies on a capped door. On the critical path no third low combat
/// room follows two (R19). A dead end takes a destination (<see cref="LayoutRoom.IsDestination"/>),
/// never a hallway; a pool with no destination that fits is a fallback the report names.
/// A hall or shaft is drawn beside another, up to four together, as the depth's
/// <see cref="DepthProfile.Clusters"/> says. The draw adapts to what the level already holds
/// (M21): a kind or family under its target share is raised and one past it lowered, a hard
/// most is never passed, a family beside itself or a variant already placed is drawn less
/// (<see cref="DepthProfile.Variety"/>), a run of hallways raises the rooms that end it, and a
/// level short of its jump spots raises the rooms that offer them. Families with a fewest are
/// swapped in by footprint at the end if the draw missed them. Every weight is a function of
/// the depth, the seed, the pool in name order and the rooms placed so far, in placement order.</item>
/// <item>Jump spots: when the placed rooms offer fewer than the depth's S-tier or
/// B-tier minimum, rooms are swapped for ones of the same footprint that offer
/// them. A pool with no such room is a fallback the report names
/// (<see cref="LayoutReport.JumpFallback"/>); so is one where no attempt met them.</item>
/// </list>
/// The critical path never needs a jump (M8): a room's doors stand on its floor
/// (map tools D27) and jump spots are features inside rooms, so every walk
/// through joined doors is a walk a Heavy can make. Nothing in a room's name can
/// put a jump between two doors, and this type does not try to.
/// </summary>
public sealed class LevelLayout
{
    /// <summary>Grids tried before the generator reports that the pool cannot make a level.</summary>
    public const int MaxAttempts = 48;

    /// <summary>The largest tall-room group the cluster control builds towards.</summary>
    public const int MaxCluster = 4;

    private const int KindCount = 6;

    /// <summary>The level for a depth and seed from a library's rooms, or why there is none.</summary>
    public LayoutResult Generate(int depth, ulong seed, IReadOnlyList<RoomInfo> rooms)
    {
        if (depth is < 1 or > DepthTable.Depths)
            return Fail($"depth {depth} is not a level of the shaft (1 to {DepthTable.Depths})");
        return Generate(DepthTable.For(depth), seed, rooms);
    }

    /// <summary>The level for a depth's row of the table, or one changed from it (the shape controls' facts).</summary>
    public LayoutResult Generate(DepthProfile profile, ulong seed, IReadOnlyList<RoomInfo> rooms)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var depth = profile.Depth;
        var parsed = new List<LayoutRoom>();
        foreach (var info in rooms.Where(r => depth >= r.MinDepth && depth <= r.MaxDepth).OrderBy(r => r.Name, StringComparer.Ordinal))
        {
            if (!RoomName.TryParse(info.Name, out var shape, out var error)) return Fail(error!);
            parsed.Add(new LayoutRoom(info, shape!));
        }
        if (parsed.Count == 0) return Fail($"the pool holds no room for depth {depth}");

        var pool = new Pool(parsed, profile);
        if (pool.Of(RoomKind.Arrival).Count == 0)
            return Fail($"the pool has no arrival at depth {depth}: no room tagged '{RoomTags.Start}' and no camp");
        if (profile.HasDownExit && pool.Of(RoomKind.Exit).Count == 0)
            return Fail($"the pool has no exit at depth {depth}: no room tagged '{RoomTags.Stairs}'");

        var rng = new LayoutRandom(seed ^ ((ulong)depth * 0xD1B54A32D192ED03UL));
        return profile.IsBoss ? BossRow(profile, pool, rng) : Grid(profile, pool, rng);
    }

    private static LayoutResult Fail(string error) => new(null, null, error);

    // ---------------------------------------------------------------------
    // Boss levels (C3): arrival, antechamber, arena, exit, in one row.
    // ---------------------------------------------------------------------

    private static LayoutResult BossRow(DepthProfile profile, Pool pool, LayoutRandom rng)
    {
        if (pool.Of(RoomKind.Arena).Count == 0)
            return Fail($"depth {profile.Depth} is a boss level and the pool has no room tagged '{RoomTags.BossArena}'");
        var kinds = new List<RoomKind> { RoomKind.Arrival };
        var antechamber = pool.Of(RoomKind.Antechamber).Count > 0;
        if (antechamber) kinds.Add(RoomKind.Antechamber);
        kinds.Add(RoomKind.Arena);
        if (profile.HasDownExit) kinds.Add(RoomKind.Exit);

        var a = new Attempt(1, kinds.Count, kinds.Count, profile, pool);
        for (var i = 0; i < kinds.Count; i++)
        {
            a.Occupy(i, kinds[i]);
            a.PathIndex[i] = i;
            a.Path.Add(i);
            if (i > 0) a.Join(i - 1, Doors.East);
        }
        foreach (var cell in a.Path)
            if (a.Place(cell, rng) is { } why)
                return Fail($"depth {profile.Depth}: {why}");
        return new LayoutResult(a.ToPlan(), a.ToReport(0, null, antechamber), null);
    }

    // ---------------------------------------------------------------------
    // Normal levels (C1).
    // ---------------------------------------------------------------------

    private static LayoutResult Grid(DepthProfile profile, Pool pool, LayoutRandom rng)
    {
        // A virtual grid twice the span, grown from its middle: any box of the span holding the
        // middle fits inside it.
        var side = 2 * profile.MaxSpan - 1;
        Attempt? best = null;
        var bestAttempt = 0;
        var bestScore = int.MaxValue;
        string? lastFailure = null;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var a = new Attempt(side, side, profile.MaxSpan, profile, pool);
            var clean = attempt < MaxAttempts / 2;
            lastFailure = a.Build(rng, cleanRoles: clean);
            if (lastFailure is not null) continue;

            // Short of rooms is a failure while clean attempts remain; after that, the best one stands, reported.
            var shortRooms = profile.Rooms - a.Count;
            if (shortRooms > 0 && clean)
            {
                lastFailure = $"{a.Count} of {profile.Rooms} rooms";
                continue;
            }
            var score = shortRooms * 100 + a.Shortfall;
            if (score == 0)
                return new LayoutResult(a.ToPlan(), a.ToReport(attempt, null, false), null);
            if (shortRooms == 0 && a.ShortfallOnlyWhereThePoolCannot)
                return new LayoutResult(a.ToPlan(), a.ToReport(attempt, a.FallbackReason(), false), null);
            if (score < bestScore)
            {
                best = a;
                bestAttempt = attempt;
                bestScore = score;
            }
        }
        if (best is not null)
            return new LayoutResult(best.ToPlan(), best.ToReport(bestAttempt, best.Shortfall > 0 ? best.FallbackReason() : null, false), null);
        return Fail($"no layout in {MaxAttempts} attempts at depth {profile.Depth}: {lastFailure}");
    }

    /// <summary>The pool sorted into kinds, with each room's door set at each quarter turn.</summary>
    private sealed class Pool
    {
        private readonly List<LayoutRoom>[] _byKind = new List<LayoutRoom>[KindCount];

        // Index KindCount holds the fill rooms that are destinations (a branch may end in them).
        private readonly bool[,] _exact = new bool[KindCount + 1, 16];
        private readonly bool[,] _super = new bool[KindCount + 1, 16];

        public Pool(List<LayoutRoom> rooms, DepthProfile profile)
        {
            for (var k = 0; k < KindCount; k++) _byKind[k] = [];
            foreach (var r in rooms)
            {
                // A larger fill room is placed whole by its own step (a square with a socket on every
                // cell edge); the one-cell matching below never sees it.
                if (r.Shape.IsMultiCell && r.Kind == RoomKind.Fill)
                {
                    if (r.Shape.CellsX == r.Shape.CellsY && r.Shape.Doors == Doors.All) Big.Add(r);
                    continue;
                }
                _byKind[(int)r.Kind].Add(r);
            }

            // The camps (M10): a depth that opens on one draws its arrival from them when it has any.
            if (profile.ArrivalFamily is { } family && _byKind[(int)RoomKind.Arrival].Any(r => r.Shape.Family == family))
                _byKind[(int)RoomKind.Arrival] = _byKind[(int)RoomKind.Arrival].Where(r => r.Shape.Family == family).ToList();

            for (var k = 0; k < KindCount; k++)
                foreach (var r in _byKind[k].Where(r => !r.Shape.IsMultiCell))
                    for (var turn = 0; turn < 4; turn++)
                    {
                        var d = (int)DoorSets.Rotate(r.Shape.Doors, turn * 90);
                        var destination = k == (int)RoomKind.Fill && r.IsDestination;
                        _exact[k, d] = true;
                        if (destination) _exact[KindCount, d] = true;
                        for (var q = 0; q < 16; q++)
                            if ((d & q) == q)
                            {
                                _super[k, q] = true;
                                if (destination) _super[KindCount, q] = true;
                            }
                    }
            HasDestination = _byKind[(int)RoomKind.Fill].Any(r => r.IsDestination);
            HasOneDoorEnd = _byKind[(int)RoomKind.Fill].Any(r => r.IsDestination && !r.IsShrine && DoorSets.Count(r.Shape.Doors) == 1);
            HasShrineEnd = _byKind[(int)RoomKind.Fill].Any(r => r.IsShrine && DoorSets.Count(r.Shape.Doors) == 1);

            FillOffersS = _byKind[(int)RoomKind.Fill].Any(r => r.Shape.STierSpots > 0);
            FillOffersB = _byKind[(int)RoomKind.Fill].Any(r => r.Shape.BTierSpots > 0);
        }

        public bool FillOffersS { get; }

        /// <summary>The fill rooms larger than one cell: square, with a socket on every cell edge.</summary>
        public List<LayoutRoom> Big { get; } = [];

        public bool FillOffersB { get; }

        /// <summary>Some fill room may end a branch.</summary>
        public bool HasDestination { get; }

        /// <summary>A one-door destination that is not a counted shrine: a tunnel may end anywhere in it.</summary>
        public bool HasOneDoorEnd { get; }

        /// <summary>A one-door shrine stub: a tunnel may end in one while the level's shrines allow.</summary>
        public bool HasShrineEnd { get; }

        /// <summary>A destination fill room has at least these doors at some quarter turn (exactly, with <paramref name="exact"/>).</summary>
        public bool Destination(Doors doors, bool exact) => exact ? _exact[KindCount, (int)doors] : _super[KindCount, (int)doors];

        public List<LayoutRoom> Of(RoomKind kind) => _byKind[(int)kind];

        /// <summary>A room of the kind has exactly these doors at some quarter turn.</summary>
        public bool Exact(RoomKind kind, Doors doors) => _exact[(int)kind, (int)doors];

        /// <summary>A room of the kind has at least these doors at some quarter turn.</summary>
        public bool Super(RoomKind kind, Doors doors) => _super[(int)kind, (int)doors];
    }

    /// <summary>One try at a level: the door graph first, then a room in every cell.</summary>
    private sealed class Attempt
    {
        private readonly int _rows;
        private readonly int _columns;
        private readonly int _span;
        private readonly DepthProfile _profile;
        private readonly Pool _pool;

        private readonly bool[] _occupied;
        private readonly Doors[] _joints;
        private readonly RoomKind[] _kind;
        private readonly LayoutRoom?[] _room;
        private readonly int[] _turn;
        private readonly Doors[] _forbidden;
        private readonly bool[] _reserved;
        private readonly int[] _block;
        private int _extraCells;
        private int _bigRooms;
        private readonly List<int> _cells = [];

        public readonly int[] PathIndex;
        public readonly List<int> Path = [];

        private int _minRow = int.MaxValue, _maxRow = int.MinValue, _minColumn = int.MaxValue, _maxColumn = int.MinValue;
        private int _jointCount;
        private int _combat;
        private int _jumpRooms;
        private int _sSpots;
        private int _bSpots;
        private bool _treasure;
        private string? _deadEndFallback;
        private int[] _fromArrival = [];
        private int[] _toExit = [];

        // The level's mix so far (M21): fill rooms by kind, family and variant, and the shrines.
        private readonly int[] _classCount = new int[4];
        private readonly Dictionary<string, int> _familyCount = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _nameCount = new(StringComparer.Ordinal);
        private int _fillPlaced;
        private int _shrines;
        private readonly KindTarget?[] _kindTargets = new KindTarget?[4];
        private readonly Dictionary<string, FamilyTarget> _familyTargets = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RoomClass> _familyClass = new(StringComparer.Ordinal);
        private bool[] _seen = [];

        public Attempt(int rows, int columns, int span, DepthProfile profile, Pool pool)
        {
            _rows = rows;
            _columns = columns;
            _span = span;
            _profile = profile;
            _pool = pool;
            var cells = rows * columns;
            _occupied = new bool[cells];
            _joints = new Doors[cells];
            _kind = new RoomKind[cells];
            _room = new LayoutRoom?[cells];
            _turn = new int[cells];
            _forbidden = new Doors[cells];
            _reserved = new bool[cells];
            _block = new int[cells];
            Array.Fill(_block, -1);
            PathIndex = new int[cells];
            Array.Fill(PathIndex, -1);
            foreach (var kind in profile.Kinds) _kindTargets[(int)kind.Kind] = kind;
            foreach (var family in profile.FamilyTargets) _familyTargets[family.Family] = family;
            // A family's kind, as its first variant in name order is counted.
            foreach (var room in pool.Of(RoomKind.Fill)) _familyClass.TryAdd(room.Shape.Family, room.Class);
        }

        private KindTarget? KindTargetOf(RoomClass kind) => _kindTargets[(int)kind];

        private FamilyTarget? FamilyTargetOf(string family) => _familyTargets.TryGetValue(family, out var f) ? f : null;

        private int Cells => _rows * _columns;

        /// <summary>Rooms placed: a room larger than one cell counts once (M19's budget).</summary>
        public int Count => _cells.Count - _extraCells;

        public int Shortfall => Math.Max(0, _profile.MinSTierSpots - _sSpots) + Math.Max(0, _profile.MinBTierSpots - _bSpots);

        /// <summary>Why the jump minimums are not met, one clause per tier short.</summary>
        public string FallbackReason()
        {
            var why = new List<string>(2);
            foreach (var (tier, have, min, offered) in new[] { ("S", _sSpots, _profile.MinSTierSpots, _pool.FillOffersS), ("B", _bSpots, _profile.MinBTierSpots, _pool.FillOffersB) })
                if (have < min)
                    why.Add(offered
                        ? $"{have} of {min} {tier}-tier spots at depth {_profile.Depth}: no attempt in {MaxAttempts} placed more"
                        : $"{have} of {min} {tier}-tier spots at depth {_profile.Depth}: the pool offers no {tier}-tier room");
            return string.Join("; ", why);
        }

        /// <summary>Every unmet minimum is one the pool has no room for.</summary>
        public bool ShortfallOnlyWhereThePoolCannot =>
            (_sSpots >= _profile.MinSTierSpots || !_pool.FillOffersS) && (_bSpots >= _profile.MinBTierSpots || !_pool.FillOffersB);

        /// <summary>The cell across a side, or -1 off the grid.</summary>
        private int Neighbour(int cell, Doors side)
        {
            int row = cell / _columns, column = cell % _columns;
            switch (side)
            {
                case Doors.North: row++; break;
                case Doors.South: row--; break;
                case Doors.East: column++; break;
                case Doors.West: column--; break;
            }
            return row < 0 || row >= _rows || column < 0 || column >= _columns ? -1 : row * _columns + column;
        }

        private static Doors Side(int i) => (Doors)(1 << i);

        /// <summary>The cell keeps the level's bounding box within the span (M19).</summary>
        private bool InBox(int cell)
        {
            int row = cell / _columns, column = cell % _columns;
            return Math.Max(_maxRow, row) - Math.Min(_minRow, row) < _span && Math.Max(_maxColumn, column) - Math.Min(_minColumn, column) < _span;
        }

        /// <summary>A cell a new room may take: on the grid, empty, not kept empty, inside the box.</summary>
        private bool Free(int cell) => cell >= 0 && !_occupied[cell] && !_reserved[cell] && InBox(cell);

        public void Occupy(int cell, RoomKind kind)
        {
            _occupied[cell] = true;
            _kind[cell] = kind;
            _cells.Add(cell);
            int row = cell / _columns, column = cell % _columns;
            _minRow = Math.Min(_minRow, row);
            _maxRow = Math.Max(_maxRow, row);
            _minColumn = Math.Min(_minColumn, column);
            _maxColumn = Math.Max(_maxColumn, column);
        }

        public void Join(int cell, Doors side)
        {
            _joints[cell] |= side;
            _joints[Neighbour(cell, side)] |= DoorSets.Opposite(side);
            _jointCount++;
        }

        private void Unjoin(int cell, Doors side)
        {
            _joints[cell] &= ~side;
            _joints[Neighbour(cell, side)] &= ~DoorSets.Opposite(side);
            _jointCount--;
        }

        private int LoopCount => _jointCount - Count + 1;

        /// <summary>A cell of a room larger than one cell.</summary>
        private bool InBlock(int cell) => _block[cell] >= 0;

        /// <summary>The cells of the room larger than one cell that holds <paramref name="cell"/>.</summary>
        private IEnumerable<int> Mates(int cell)
        {
            var anchor = _block[cell];
            var size = _room[anchor]!.Shape.CellsX;
            for (var r = 0; r < size; r++)
                for (var c = 0; c < size; c++)
                    yield return anchor + r * _columns + c;
        }

        /// <summary>A room's joined doors: a larger room's are its cells' joints together.</summary>
        private int RoomDegree(int cell) => InBlock(cell) ? Mates(cell).Sum(Degree) : Degree(cell);

        /// <summary>The door graph and the rooms; null, or why this try failed.</summary>
        /// <param name="rng">The level's generator.</param>
        /// <param name="cleanRoles">Refuse a role room whose extra door would face a blank wall (the early attempts).</param>
        public string? Build(LayoutRandom rng, bool cleanRoles)
        {
            var length = rng.Range(_profile.PathMin, Math.Min(_profile.PathMax, _profile.Rooms - 1));
            if (!CriticalPath(length, rng, cleanRoles)) return $"no critical path of {length} rooms fits the span of {_span} and the pool's doors";

            // The role rooms' joints are final once placed: place them as soon as they are, and
            // keep empty the cells their extra doors face, so nothing grows against a door it
            // cannot join.
            var treasures = _pool.Of(RoomKind.Treasure).Count > 0 ? 1 : 0;
            if (PlaceRole(Path[0], rng, cleanRoles, spare: 4) is { } up) return up;
            if (PlaceRole(Path[^1], rng, cleanRoles, spare: 4) is { } down) return down;
            if (treasures > 0)
            {
                if (Treasure(rng) is not { } treasure) return "no cell off the critical path takes the treasure room";
                if (PlaceRole(treasure, rng, cleanRoles, spare: 0) is { } why) return why;
            }

            PlaceBlocks(rng);
            Grow(rng);
            CloseLoops(rng);

            foreach (var cell in PlacementOrder())
                if (_room[cell] is null && Place(cell, rng) is { } why) return why;
            RepairFamilies(rng);
            RepairJumps(rng);
            return null;
        }

        /// <summary>
        /// A role room, placed as soon as its joints are known. Up to <paramref name="spare"/> of its
        /// extra doors that face an empty cell open onto a new branch room there (a new dead end
        /// each, M18); the cells the others face stay empty, so nothing grows against them.
        /// </summary>
        private string? PlaceRole(int cell, LayoutRandom rng, bool clean, int spare)
        {
            if (Place(cell, rng, allowWasted: !clean, joinable: spare) is { } why) return why;
            var extra = DoorsOf(cell, _room[cell]!) & ~_joints[cell];
            for (var s = 0; s < 4; s++)
            {
                if ((extra & Side(s)) == 0 || Neighbour(cell, Side(s)) is not (>= 0 and var next) || _occupied[next]) continue;
                if (spare > 0 && Free(next) && LeafFits(DoorSets.Opposite(Side(s)), DeadEnds()))
                {
                    Occupy(next, RoomKind.Fill);
                    Join(cell, Side(s));
                    spare--;
                }
                else _reserved[next] = true;
            }
            return null;
        }

        private int Degree(int cell) => DoorSets.Count(_joints[cell]);

        /// <summary>A dead end (M18): a room with one joined door, but the arrival and the exit.</summary>
        private bool IsDeadEnd(int cell) =>
            _occupied[cell] && (!InBlock(cell) || _block[cell] == cell) && RoomDegree(cell) == 1 && _kind[cell] is not (RoomKind.Arrival or RoomKind.Exit);

        private int DeadEnds()
        {
            var n = 0;
            foreach (var cell in _cells)
                if (IsDeadEnd(cell)) n++;
            return n;
        }

        /// <summary>
        /// A branch may end with these doors (M20): a one-door destination takes it, or a shrine
        /// while the level's shrines (and the treasure) allow; a room that would cap a door only
        /// when the pool has no one-door destination at all; anything when it has no destination
        /// (the reported fallback).
        /// </summary>
        private bool LeafFits(Doors doors, int deadEnds)
        {
            if (!_pool.HasDestination) return true;
            if (DoorSets.Count(doors) != 1) return _pool.Destination(doors, exact: false);
            if (_pool.HasOneDoorEnd) return true;
            if (_pool.HasShrineEnd) return deadEnds < _profile.MaxShrines + (_treasure ? 1 : 0);
            return _pool.Destination(doors, exact: false);
        }

        // A self-avoiding walk from the middle of the grid, as straight as the depth says,
        // shunning its own earlier rooms, whose ends stand at least half its steps apart.
        private bool CriticalPath(int length, LayoutRandom rng, bool clean)
        {
            var need = length / 2;
            var cells = new List<int>(length);
            var visited = new bool[Cells];
            var start = _rows / 2 * _columns + _columns / 2;
            for (var tries = 0; tries < 24; tries++)
            {
                cells.Clear();
                Array.Clear(visited);
                var budget = 800;
                int row = start / _columns, column = start % _columns;
                if (!Walk(start, start, -1, length, need, cells, visited, rng, ref budget, row, row, column, column)) continue;
                if (!PathFits(cells, clean)) continue;

                for (var i = 0; i < length; i++)
                {
                    Occupy(cells[i], i == 0 ? RoomKind.Arrival : i == length - 1 ? RoomKind.Exit : RoomKind.Fill);
                    PathIndex[cells[i]] = i;
                    Path.Add(cells[i]);
                    if (i > 0) Join(cells[i - 1], SideTowards(cells[i - 1], cells[i]));
                }
                return true;
            }
            return false;
        }

        private bool Walk(int start, int cell, int heading, int length, int need, List<int> cells, bool[] visited, LayoutRandom rng, ref int budget,
            int minRow, int maxRow, int minColumn, int maxColumn)
        {
            if (--budget < 0) return false;
            cells.Add(cell);
            visited[cell] = true;
            if (cells.Count == length)
            {
                if (Manhattan(start, cell) >= need) return true;
            }
            else if (Manhattan(start, cell) + (length - cells.Count) >= need)
            {
                // Straight on with the depth's straightness, else a quarter turn either way; a cell
                // beside the walk's own earlier rooms is a tenth as likely.
                Span<double> weight = stackalloc double[4];
                for (var s = 0; s < 4; s++)
                {
                    var next = Neighbour(cell, Side(s));
                    if (next < 0 || visited[next]) continue;
                    int r = next / _columns, c = next % _columns;
                    if (Math.Max(maxRow, r) - Math.Min(minRow, r) >= _span || Math.Max(maxColumn, c) - Math.Min(minColumn, c) >= _span) continue;
                    var w = heading < 0 ? 1.0 : s == heading ? _profile.Straightness : (1 - _profile.Straightness) / 2;
                    for (var t = 0; t < 4; t++)
                        if (Neighbour(next, Side(t)) is >= 0 and var beside && beside != cell && visited[beside])
                        {
                            w *= 0.1;
                            break;
                        }
                    weight[s] = Math.Max(w, 1e-4);
                }
                for (var tried = 0; tried < 4; tried++)
                {
                    var total = 0.0;
                    foreach (var w in weight) total += w;
                    if (total <= 0) break;
                    var pick = (rng.Next() >> 11) * (1.0 / (1UL << 53)) * total;
                    var s = -1;
                    for (var i = 0; i < 4; i++)
                    {
                        if (weight[i] <= 0) continue;
                        s = i;
                        pick -= weight[i];
                        if (pick < 0) break;
                    }
                    weight[s] = 0;
                    var next = Neighbour(cell, Side(s));
                    int r = next / _columns, c = next % _columns;
                    if (Walk(start, next, s, length, need, cells, visited, rng, ref budget,
                            Math.Min(minRow, r), Math.Max(maxRow, r), Math.Min(minColumn, c), Math.Max(maxColumn, c)))
                        return true;
                }
            }
            cells.RemoveAt(cells.Count - 1);
            visited[cell] = false;
            return false;
        }

        /// <param name="cells">The walk.</param>
        /// <param name="clean">Ask that an arrival or exit with more doors than it joins can face its spare doors into the grid, off the path (no capped door).</param>
        private bool PathFits(List<int> cells, bool clean)
        {
            var box = (MinRow: cells.Min(c => c / _columns), MaxRow: cells.Max(c => c / _columns),
                MinColumn: cells.Min(c => c % _columns), MaxColumn: cells.Max(c => c % _columns));
            for (var i = 0; i < cells.Count; i++)
            {
                var doors = Doors.None;
                if (i > 0) doors |= SideTowards(cells[i], cells[i - 1]);
                if (i < cells.Count - 1) doors |= SideTowards(cells[i], cells[i + 1]);
                var kind = i == 0 ? RoomKind.Arrival : i == cells.Count - 1 ? RoomKind.Exit : RoomKind.Fill;
                if (!_pool.Super(kind, doors)) return false;
                if (clean && kind != RoomKind.Fill && !_pool.Exact(kind, doors) && !SparesInside(kind, cells[i], doors, cells, box)) return false;
            }
            return true;
        }

        /// <summary>Some room of the kind turns so its doors beyond <paramref name="need"/> face cells inside the grid and the span, off the path.</summary>
        private bool SparesInside(RoomKind kind, int cell, Doors need, List<int> path, (int MinRow, int MaxRow, int MinColumn, int MaxColumn) box)
        {
            bool Inside(int next)
            {
                int r = next / _columns, c = next % _columns;
                return Math.Max(box.MaxRow, r) - Math.Min(box.MinRow, r) < _span && Math.Max(box.MaxColumn, c) - Math.Min(box.MinColumn, c) < _span;
            }

            foreach (var room in _pool.Of(kind))
                for (var turn = 0; turn < 4; turn++)
                {
                    var doors = DoorSets.Rotate(room.Shape.Doors, turn * 90);
                    if ((doors & need) != need) continue;
                    var inside = true;
                    for (var s = 0; s < 4 && inside; s++)
                        if ((doors & ~need & Side(s)) != 0 && (Neighbour(cell, Side(s)) is not (>= 0 and var next) || path.Contains(next) || !Inside(next))) inside = false;
                    if (inside) return true;
                }
            return false;
        }

        private int Manhattan(int a, int b) => Math.Abs(a / _columns - b / _columns) + Math.Abs(a % _columns - b % _columns);

        private Doors SideTowards(int from, int to) =>
            to == from + _columns ? Doors.North
            : to == from - _columns ? Doors.South
            : to == from + 1 ? Doors.East
            : Doors.West;

        // A treasure one or two cells out from an inner room of the path.
        private int? Treasure(LayoutRandom rng)
        {
            var anchors = Path.Skip(1).Take(Path.Count - 2).ToList();
            rng.Shuffle(anchors);
            var extend = rng.Below(2) == 1;
            foreach (var exactOnly in new[] { true, false })
                foreach (var anchor in anchors)
                    for (var s = 0; s < 4; s++)
                    {
                        var side = Side(s);
                        var first = Neighbour(anchor, side);
                        if (!Free(first) || !Fits(RoomKind.Fill, _joints[anchor] | side, exactOnly)) continue;
                        var back = DoorSets.Opposite(side);
                        if (extend)
                            for (var t = 0; t < 4; t++)
                            {
                                var on = Side(t);
                                var second = Neighbour(first, on);
                                if (on == back || !Free(second)) continue;
                                if (!Fits(RoomKind.Fill, back | on, exactOnly) || !_pool.Super(RoomKind.Treasure, DoorSets.Opposite(on))) continue;
                                Occupy(first, RoomKind.Fill);
                                Join(anchor, side);
                                Occupy(second, RoomKind.Treasure);
                                Join(first, on);
                                _treasure = true;
                                return second;
                            }
                        if (!_pool.Super(RoomKind.Treasure, back)) continue;
                        Occupy(first, RoomKind.Treasure);
                        Join(anchor, side);
                        _treasure = true;
                        return first;
                    }
            return null;
        }

        private bool Fits(RoomKind kind, Doors doors, bool exactOnly) => exactOnly ? _pool.Exact(kind, doors) : _pool.Super(kind, doors);

        /// <summary>Placed rooms beside a cell, joined or not.</summary>
        private int Beside(int cell)
        {
            var n = 0;
            for (var s = 0; s < 4; s++)
                if (Neighbour(cell, Side(s)) is >= 0 and var next && _occupied[next]) n++;
            return n;
        }

        /// <summary>
        /// The rooms of the side tunnel ending at a branch end: back along rooms of two joined doors
        /// until a fork, the critical path or a role room.
        /// </summary>
        private int TunnelLength(int end)
        {
            var length = 1;
            int previous = -1, cell = end;
            while (length < 64)
            {
                var next = -1;
                for (var s = 0; s < 4; s++)
                    if ((_joints[cell] & Side(s)) != 0 && Neighbour(cell, Side(s)) is var n && n != previous) next = n;
                if (next < 0 || PathIndex[next] >= 0 || _kind[next] != RoomKind.Fill || InBlock(next) || Degree(next) != 2) return length;
                length++;
                previous = cell;
                cell = next;
            }
            return length;
        }

        /// <summary>
        /// The level's rooms larger than one cell (plan section 3.2, M24): as many as the depth's
        /// draw (<see cref="DepthTable.BigRoomsAt"/>), each a square of cells off a fill room of the
        /// path or a branch, joined to it through one cell edge, counted once against the budget.
        /// Every cell edge of its perimeter is a socket: growth may branch from any of them, and the
        /// link caps the rest. The room is chosen as the block is laid, so no room placed beside it
        /// later opens a door onto one of its sockets that the layout did not join.
        /// </summary>
        private void PlaceBlocks(LayoutRandom rng)
        {
            if (_pool.Big.Count == 0 || _profile.MaxBigRooms <= 0) return;
            var want = rng.Range((_profile.MaxBigRooms + 1) / 2, _profile.MaxBigRooms);
            var sizes = _pool.Big.Select(r => r.Shape.CellsX).Distinct().Order().ToList();
            var candidates = new List<(int From, Doors Side, int Anchor)>();
            for (var placed = 0; placed < want && Count < _profile.Rooms; placed++)
            {
                var size = sizes[rng.Below(sizes.Count)];
                candidates.Clear();
                foreach (var cell in _cells)
                {
                    if (_kind[cell] != RoomKind.Fill || InBlock(cell)) continue;
                    for (var s = 0; s < 4; s++)
                    {
                        var side = Side(s);
                        if (!_pool.Exact(RoomKind.Fill, _joints[cell] | side)) continue;
                        var next = Neighbour(cell, side);
                        if (next < 0) continue;
                        for (var offset = 0; offset < size; offset++)
                            if (BlockAt(next, side, offset, size) is { } anchor) candidates.Add((cell, side, anchor));
                    }
                }
                if (candidates.Count == 0) return;
                var (from, towards, at) = candidates[rng.Below(candidates.Count)];
                var rooms = _pool.Big.Where(r => r.Shape.CellsX == size).ToList();
                var room = rooms[rng.Weighted(rooms.Select(r => (long)Math.Max(1, r.Info.Weight)).ToList())];
                var turn = rng.Below(4);
                for (var r = 0; r < size; r++)
                    for (var c = 0; c < size; c++)
                    {
                        var cell = at + r * _columns + c;
                        Occupy(cell, RoomKind.Fill);
                        _block[cell] = at;
                        _room[cell] = room;
                        _turn[cell] = turn;
                    }
                _extraCells += size * size - 1;
                _bigRooms++;
                Tally(room, +1);
                Join(from, towards);
                // A socket against a placed room's wall: that room never opens back onto it.
                foreach (var cell in Mates(at))
                    for (var s = 0; s < 4; s++)
                        if (Neighbour(cell, Side(s)) is >= 0 and var next && _occupied[next] && !InBlock(next) && (_joints[cell] & Side(s)) == 0)
                            _forbidden[next] |= DoorSets.Opposite(Side(s));
            }
        }

        /// <summary>
        /// The south-west cell of a square of <paramref name="size"/> cells whose edge
        /// <paramref name="offset"/> cells along lies at <paramref name="first"/>, entered across
        /// <paramref name="side"/>, when every cell of it is free and the level's box still holds it.
        /// </summary>
        private int? BlockAt(int first, Doors side, int offset, int size)
        {
            int row = first / _columns, column = first % _columns;
            // The square grows away from the room it is entered from, and slides along the edge.
            int r0 = side switch { Doors.North => row, Doors.South => row - size + 1, _ => row - offset };
            int c0 = side switch { Doors.East => column, Doors.West => column - size + 1, _ => column - offset };
            if (r0 < 0 || c0 < 0 || r0 + size > _rows || c0 + size > _columns) return null;
            int minRow = Math.Min(_minRow, r0), maxRow = Math.Max(_maxRow, r0 + size - 1);
            int minColumn = Math.Min(_minColumn, c0), maxColumn = Math.Max(_maxColumn, c0 + size - 1);
            if (maxRow - minRow >= _span || maxColumn - minColumn >= _span) return null;
            for (var r = r0; r < r0 + size; r++)
                for (var c = c0; c < c0 + size; c++)
                {
                    var cell = r * _columns + c;
                    if (_occupied[cell] || _reserved[cell]) return null;
                }
            return r0 * _columns + c0;
        }

        // Branches, to the room budget. A tunnel may always start where it can end in a
        // destination; the tunnel-end share steers how often (M20).
        private void Grow(LayoutRandom rng)
        {
            var endsWanted = _profile.Rooms * _profile.EndSharePercent / 100.0;
            var target = _profile.Rooms;
            var candidates = new List<(int From, Doors Side, int To, Doors ToSide, long Weight)>();
            var loose = new List<(int Cell, Doors Side)>();
            var shortest = Path.Count - 1;
            // Sprawl: a cell with n placed neighbours weighs 16^((1 - 2 sprawl)(n - 1)).
            var sprawl = 1 - 2 * _profile.Sprawl;
            while (Count < target)
            {
                candidates.Clear();
                loose.Clear();
                var closing = LoopCount < _profile.Loops;
                if (closing) Distances();
                var deadEnds = DeadEnds();
                foreach (var cell in _cells)
                {
                    if (_kind[cell] != RoomKind.Fill) continue;
                    var block = InBlock(cell);
                    var leaf = !block && Degree(cell) == 1;
                    var tunnel = leaf ? TunnelLength(cell) : 0;
                    for (var s = 0; s < 4; s++)
                    {
                        var side = Side(s);
                        var next = Neighbour(cell, side);
                        if (!Free(next)) continue;
                        var grown = _joints[cell] | side;
                        var back = DoorSets.Opposite(side);
                        // A larger room has a socket on every cell edge: any free side of it may join.
                        if (!block && !_pool.Exact(RoomKind.Fill, grown))
                        {
                            if (_pool.Super(RoomKind.Fill, grown) && _pool.Super(RoomKind.Fill, back)) loose.Add((cell, side));
                            continue;
                        }
                        var spread = Math.Pow(16, sprawl * (Beside(next) - 1));
                        if (closing && !block)
                            for (var t = 0; t < 4; t++)
                            {
                                var on = Side(t);
                                var other = Neighbour(next, on);
                                if (on == back || other < 0 || !_occupied[other] || _kind[other] != RoomKind.Fill || InBlock(other)) continue;
                                if (!_pool.Exact(RoomKind.Fill, back | on) || !_pool.Exact(RoomKind.Fill, _joints[other] | DoorSets.Opposite(on))) continue;
                                if (Shortcut(cell, other, 2, shortest)) continue;
                                candidates.Add((cell, side, other, DoorSets.Opposite(on), Scaled(2 * spread)));
                            }
                        if (!LeafFits(back, deadEnds)) continue;
                        // A side tunnel carries straight on as the depth's straightness says.
                        var ahead = !leaf ? 1 : side == DoorSets.Opposite(_joints[cell]) ? _profile.Straightness : (1 - _profile.Straightness) / 2;
                        if (leaf) candidates.Add((cell, side, -1, Doors.None, Scaled((tunnel < _profile.BranchLength ? 4 : 0.5) * spread * ahead)));
                        else candidates.Add((cell, side, -1, Doors.None, Scaled(spread * (deadEnds < endsWanted ? 2 : 0.2))));
                    }
                }
                if (candidates.Count == 0)
                {
                    if (loose.Count == 0) return;
                    var (at, towards) = loose[rng.Below(loose.Count)];
                    Occupy(Neighbour(at, towards), RoomKind.Fill);
                    Join(at, towards);
                    continue;
                }
                var (from, fromSide, to, toSide, _) = candidates[rng.Weighted(candidates.Select(c => c.Weight).ToList())];
                Occupy(Neighbour(from, fromSide), RoomKind.Fill);
                Join(from, fromSide);
                // A closing step joins the other placed room to the new one.
                if (to >= 0) Join(to, toSide);
            }
        }

        private static long Scaled(double weight) => Math.Max(1L, (long)Math.Round(weight * 1000));

        /// <summary>Doors from the arrival and to the exit for every room, for the shortcut test.</summary>
        private void Distances()
        {
            _fromArrival = DistancesFrom(Path[0]);
            _toExit = DistancesFrom(Path[^1]);
        }

        /// <summary>Joining <paramref name="a"/> and <paramref name="b"/> by a walk of <paramref name="doors"/> would shorten the critical path.</summary>
        private bool Shortcut(int a, int b, int doors, int shortest) =>
            (_fromArrival[a] >= 0 && _toExit[b] >= 0 && _fromArrival[a] + doors + _toExit[b] < shortest)
            || (_fromArrival[b] >= 0 && _toExit[a] >= 0 && _fromArrival[b] + doors + _toExit[a] < shortest);

        // Loops: a joint between two neighbouring fill rooms that is not a shortcut, while the level is under its loops.
        private void CloseLoops(LayoutRandom rng)
        {
            if (LoopCount >= _profile.Loops) return;
            var candidates = new List<(int Cell, Doors Side)>();
            foreach (var cell in _cells)
            {
                if (_kind[cell] != RoomKind.Fill) continue;
                foreach (var side in new[] { Doors.North, Doors.East })
                {
                    var other = Neighbour(cell, side);
                    if (other < 0 || !_occupied[other] || _kind[other] != RoomKind.Fill || (_joints[cell] & side) != 0 || InBlock(cell) || InBlock(other)) continue;
                    candidates.Add((cell, side));
                }
            }
            // Branch ends first: a loop through a dead end removes it (M18).
            rng.Shuffle(candidates);
            candidates = candidates.OrderBy(c => Degree(c.Cell) == 1 || Degree(Neighbour(c.Cell, c.Side)) == 1 ? 0 : 1).ToList();
            var shortest = Path.Count - 1;
            foreach (var (cell, side) in candidates)
            {
                if (LoopCount >= _profile.Loops) return;
                var other = Neighbour(cell, side);
                if (!_pool.Exact(RoomKind.Fill, _joints[cell] | side) || !_pool.Exact(RoomKind.Fill, _joints[other] | DoorSets.Opposite(side))) continue;
                Join(cell, side);
                if (Distance(Path[0], Path[^1]) < shortest) Unjoin(cell, side);
            }
        }

        /// <summary>Doors walked from one room to every room (breadth first), -1 where unreachable.</summary>
        private int[] DistancesFrom(int from)
        {
            // A room larger than one cell is one room: crossing it costs no door (0-1 breadth first).
            var seen = new int[Cells];
            Array.Fill(seen, -1);
            var queue = new LinkedList<int>();
            seen[from] = 0;
            queue.AddLast(from);
            while (queue.Count > 0)
            {
                var cell = queue.First!.Value;
                queue.RemoveFirst();
                if (InBlock(cell))
                    foreach (var mate in Mates(cell))
                        if (seen[mate] < 0 || seen[mate] > seen[cell])
                        {
                            seen[mate] = seen[cell];
                            queue.AddFirst(mate);
                        }
                for (var s = 0; s < 4; s++)
                {
                    if ((_joints[cell] & Side(s)) == 0) continue;
                    var next = Neighbour(cell, Side(s));
                    if (seen[next] >= 0 && seen[next] <= seen[cell] + 1) continue;
                    seen[next] = seen[cell] + 1;
                    queue.AddLast(next);
                }
            }
            return seen;
        }

        /// <summary>Doors walked from one room to another (breadth first), or -1.</summary>
        private int Distance(int from, int to) => DistancesFrom(from)[to];

        /// <summary>The path in order, then the rest breadth first from the arrival.</summary>
        private List<int> PlacementOrder()
        {
            var order = new List<int>(Count);
            var seen = new bool[Cells];
            foreach (var cell in Path)
            {
                order.Add(cell);
                seen[cell] = true;
            }
            for (var i = 0; i < order.Count; i++)
            {
                if (InBlock(order[i]))
                    foreach (var mate in Mates(order[i]))
                        if (!seen[mate])
                        {
                            seen[mate] = true;
                            order.Add(mate);
                        }
                for (var s = 0; s < 4; s++)
                {
                    if ((_joints[order[i]] & Side(s)) == 0) continue;
                    var next = Neighbour(order[i], Side(s));
                    if (seen[next]) continue;
                    seen[next] = true;
                    order.Add(next);
                }
            }
            return order;
        }

        /// <summary>A room and quarter turn for a cell; null, or why none fits.</summary>
        /// <param name="cell">The cell.</param>
        /// <param name="rng">The level's generator.</param>
        /// <param name="allowWasted">Allow an extra door against a neighbour's blank wall.</param>
        /// <param name="joinable">A role room's extra doors, up to this many, that face an empty cell
        /// count as fitting (they open onto a branch) rather than as capped.</param>
        public string? Place(int cell, LayoutRandom rng, bool allowWasted = true, int joinable = 0)
        {
            var need = _joints[cell];
            var kind = _kind[cell];
            var lowRun = !LowRunAllows(cell);
            var deadEnd = kind == RoomKind.Fill && Degree(cell) == 1;

            var candidates = Candidates(cell, kind, need, lowRun, allowWasted, joinable, destinationsOnly: deadEnd && _pool.HasDestination);
            if (candidates.Count == 0 && deadEnd && _pool.HasDestination)
            {
                candidates = Candidates(cell, kind, need, lowRun, allowWasted, joinable, destinationsOnly: false);
                if (candidates.Count > 0)
                    _deadEndFallback ??= $"no destination room fits the dead end at row {cell / _columns}, column {cell % _columns} (doors {need}) at depth {_profile.Depth}";
            }
            if (deadEnd && !_pool.HasDestination)
                _deadEndFallback ??= $"the pool offers no destination room (a combat room, a shrine stub or the treasure) to end a branch at depth {_profile.Depth}";
            // A most of the mix gives way only when nothing within it fits the cell.
            if (candidates.Count == 0)
                candidates = Candidates(cell, kind, need, lowRun, allowWasted, joinable, destinationsOnly: deadEnd && _pool.HasDestination, withinMost: false);
            if (candidates.Count == 0)
                return $"no {kind} room fits doors {need} at row {cell / _columns}, column {cell % _columns}"
                       + (lowRun ? " where a low combat room would make three in a row on the critical path" : "");

            // A zero weight (a height or family the depth leaves out) is drawn only when nothing else fits.
            var rooms = candidates.Select(c => c.Room).Distinct().ToList();
            var hallways = HallwayRun(cell);
            var weights = rooms.Select(r => Weight(cell, r, hallways)).ToList();
            if (weights.All(w => w == 0)) weights = rooms.Select(_ => 1L).ToList();
            var positive = Enumerable.Range(0, rooms.Count).Where(i => weights[i] > 0).ToList();
            var chosen = rooms[positive[rng.Weighted(positive.Select(i => weights[i]).ToList())]];
            var turns = candidates.Where(c => c.Room == chosen).Select(c => c.Turn).ToList();
            Set(cell, chosen, turns[rng.Below(turns.Count)]);
            return null;
        }

        /// <summary>The rooms and turns of the best tier for a cell.</summary>
        private List<(LayoutRoom Room, int Turn, int Tier)> Candidates(int cell, RoomKind kind, Doors need, bool lowRun, bool allowWasted, int joinable, bool destinationsOnly, bool withinMost = true)
        {
            var bestTier = int.MaxValue;
            var candidates = new List<(LayoutRoom Room, int Turn, int Tier)>();
            foreach (var room in _pool.Of(kind))
            {
                if (lowRun && room.IsLowCombat) continue;
                if (destinationsOnly && !room.IsDestination) continue;
                if (withinMost && !WithinMost(room)) continue;
                for (var turn = 0; turn < 4; turn++)
                {
                    var doors = DoorSets.Rotate(room.Shape.Doors, turn * 90);
                    if ((doors & need) != need || (doors & _forbidden[cell]) != 0) continue;
                    var tier = Tier(cell, doors & ~need, joinable);
                    if (tier < 0 || tier > bestTier || (tier == 2 && !allowWasted)) continue;
                    if (tier < bestTier)
                    {
                        bestTier = tier;
                        candidates.Clear();
                    }
                    candidates.Add((room, turn, tier));
                }
            }
            return candidates;
        }

        /// <summary>0 when there are no extra doors (or each opens onto a branch), 1 when each faces the edge or an empty cell, 2 when one faces a blank wall, -1 when one would join a neighbour's door.</summary>
        private int Tier(int cell, Doors extra, int joinable = 0)
        {
            var tier = 0;
            for (var s = 0; s < 4; s++)
            {
                var side = Side(s);
                if ((extra & side) == 0) continue;
                var next = Neighbour(cell, side);
                if (joinable > 0 && Free(next) && LeafFits(DoorSets.Opposite(side), DeadEnds()) && joinable-- > 0) continue;
                if (next < 0 || !_occupied[next]) tier = Math.Max(tier, 1);
                else if (_room[next] is { } placed && (DoorsOf(next, placed) & DoorSets.Opposite(side)) != 0) return -1;
                else tier = 2;
            }
            return tier;
        }

        private Doors DoorsOf(int cell, LayoutRoom room)
        {
            if (!InBlock(cell)) return DoorSets.Rotate(room.Shape.Doors, _turn[cell] * 90);
            // A larger room's socket on each cell edge of its perimeter.
            var doors = Doors.None;
            for (var s = 0; s < 4; s++)
                if (Neighbour(cell, Side(s)) is var next && (next < 0 || _block[next] != _block[cell])) doors |= Side(s);
            return doors;
        }

        private static bool IsTall(LayoutRoom room) => room.Shape.Height is HeightClass.Hall or HeightClass.Shaft;

        /// <summary>The joined, placed halls and shafts reached from a tall room at a cell.</summary>
        private int ClusterAt(int cell, bool[] seen)
        {
            var size = 0;
            var stack = new Stack<int>();
            stack.Push(cell);
            seen[cell] = true;
            while (stack.Count > 0)
            {
                var at = stack.Pop();
                if (!InBlock(at) || _block[at] == at) size++;
                if (InBlock(at))
                    foreach (var mate in Mates(at))
                        if (!seen[mate])
                        {
                            seen[mate] = true;
                            stack.Push(mate);
                        }
                for (var s = 0; s < 4; s++)
                    if ((_joints[at] & Side(s)) != 0 && Neighbour(at, Side(s)) is var next && !seen[next] && _room[next] is { } r && IsTall(r))
                    {
                        seen[next] = true;
                        stack.Push(next);
                    }
            }
            return size;
        }

        private long Weight(int cell, LayoutRoom room, int hallways)
        {
            double w = Math.Max(1, room.Info.Weight) * _profile.Heights.Of(room.Shape.Height) * _profile.FamilyWeight(room.Shape.Family);
            if (room.Kind != RoomKind.Fill || w == 0) return (long)w;
            // Steer the share of jump rooms among combat rooms towards the depth's target, both ways.
            if (room.IsCombat)
            {
                var under = _jumpRooms * 100 < _profile.JumpSharePercent * _combat;
                if (room.Shape.IsJumpRoom == under) w *= 3;
            }
            // A level short of its jump spots raises the rooms that offer them (the swap pass is the last resort).
            if (_sSpots < _profile.MinSTierSpots && room.Shape.STierSpots > 0) w *= 3;
            if (_bSpots < _profile.MinBTierSpots && room.Shape.BTierSpots > 0) w *= 3;

            // M21: the mix so far. A kind or family under its share is raised, one over it lowered;
            // a family short of its fewest is raised by how short it is.
            if (KindTargetOf(room.Class) is { } kind) w *= ShareFactor(kind.SharePercent, _classCount[(int)room.Class]);
            if (FamilyTargetOf(room.Shape.Family) is { } family)
            {
                if (family.SharePercent > 0) w *= ShareFactor(family.SharePercent, FamilyCount(room.Shape.Family));
                if (FamilyCount(room.Shape.Family) < family.Min) w *= 1 + 2 * (family.Min - FamilyCount(room.Shape.Family));
            }

            // Variety: the more often a variant already stands the less it is drawn, and a room
            // joined to its own family (more so its own variant) is drawn less; hallways run in
            // lines of one family, so they are held to it gently.
            if (_profile.Variety > 0)
            {
                w *= Math.Pow(room.IsConnector ? 0.9 : 0.6, NameCount(room.Name) * _profile.Variety);
                for (var s = 0; s < 4; s++)
                    if ((_joints[cell] & Side(s)) != 0 && Neighbour(cell, Side(s)) is var next && _room[next] is { Kind: RoomKind.Fill } beside)
                    {
                        if (beside.Shape.Family == room.Shape.Family) w *= Math.Pow(room.IsConnector ? 0.8 : 0.25, _profile.Variety);
                        if (beside.Name == room.Name) w *= Math.Pow(0.4, _profile.Variety);
                    }
            }

            // A run of hallways raises the rooms that end it.
            if (hallways >= 3) w = room.IsConnector ? w / (1 + 0.4 * (hallways - 2)) : w * (1 + 0.7 * (hallways - 2));

            // Worked-out areas (M19): a tall room joined to a tall group under four grows it; one
            // standing alone, or beside a full group, is drawn less.
            if (IsTall(room) && _profile.Clusters > 0)
            {
                // The joined tall group this room would make, and whether it would pass the largest.
                if (_seen.Length != Cells) _seen = new bool[Cells];
                else Array.Clear(_seen);
                var seen = _seen;
                seen[cell] = true;
                var joined = 1;
                for (var s = 0; s < 4; s++)
                    if ((_joints[cell] & Side(s)) != 0 && Neighbour(cell, Side(s)) is var next && !seen[next] && _room[next] is { } r && IsTall(r))
                        joined += ClusterAt(next, seen);
                w *= joined > MaxCluster ? 0.05 : joined > 1 ? 1 + 6 * _profile.Clusters : 1 - 0.7 * _profile.Clusters;
            }
            return Math.Max(1L, (long)Math.Round(w * 1000));
        }

        private void Set(int cell, LayoutRoom room, int turn)
        {
            if (_room[cell] is { } old) Tally(old, -1);
            _room[cell] = room;
            _turn[cell] = turn;
            Tally(room, +1);
            var doors = DoorsOf(cell, room);
            var extra = doors & ~_joints[cell];
            for (var s = 0; s < 4; s++)
            {
                var side = Side(s);
                var next = Neighbour(cell, side);
                // A door against a neighbour's blank wall: that neighbour must never open back onto it.
                if ((extra & side) != 0 && next >= 0 && _occupied[next]) _forbidden[next] |= DoorSets.Opposite(side);
            }
        }

        private void Tally(LayoutRoom room, int by)
        {
            if (room.IsCombat) _combat += by;
            if (room.Shape.IsJumpRoom) _jumpRooms += by;
            _sSpots += by * room.Shape.STierSpots;
            _bSpots += by * room.Shape.BTierSpots;
            if (room.Kind != RoomKind.Fill) return;
            _fillPlaced += by;
            _classCount[(int)room.Class] += by;
            _familyCount[room.Shape.Family] = FamilyCount(room.Shape.Family) + by;
            _nameCount[room.Name] = NameCount(room.Name) + by;
            if (room.IsShrine) _shrines += by;
        }

        private int FamilyCount(string family) => _familyCount.TryGetValue(family, out var n) ? n : 0;

        private int NameCount(string name) => _nameCount.TryGetValue(name, out var n) ? n : 0;

        /// <summary>
        /// Placing <paramref name="room"/> (in place of <paramref name="replacing"/>, if any) keeps
        /// every hard most of the level's mix: its kind's, its family's and the shrines' (M21).
        /// </summary>
        private bool WithinMost(LayoutRoom room, LayoutRoom? replacing = null)
        {
            if (room.Kind != RoomKind.Fill) return true;
            int Less(Func<LayoutRoom, bool> same) => replacing is { } r && same(r) ? 1 : 0;
            if (KindTargetOf(room.Class) is { Max: < int.MaxValue } kind)
            {
                // Room held back for the families of this kind still short of their fewest.
                var held = 0;
                foreach (var target in _profile.FamilyTargets)
                    if (target.Min > 0 && target.Family != room.Shape.Family && _familyClass.TryGetValue(target.Family, out var c) && c == room.Class)
                        held += Math.Max(0, target.Min - FamilyCount(target.Family));
                if (_classCount[(int)room.Class] - Less(r => r.Class == room.Class) + held >= kind.Max) return false;
            }
            if (FamilyTargetOf(room.Shape.Family) is { } family && FamilyCount(room.Shape.Family) - Less(r => r.Shape.Family == room.Shape.Family) >= family.Max) return false;
            if (room.IsShrine && _shrines - Less(r => r.IsShrine) >= _profile.MaxShrines) return false;
            return true;
        }

        /// <summary>Taking <paramref name="room"/> out keeps its family at its fewest.</summary>
        private bool KeepsFewest(LayoutRoom room) =>
            room.Kind != RoomKind.Fill || FamilyTargetOf(room.Shape.Family) is not { Min: > 0 } family || FamilyCount(room.Shape.Family) > family.Min;

        /// <summary>
        /// How far the level is under (positive) or over (negative) a target share of its fill rooms,
        /// counting the room about to be placed, as a weight factor: e^(1.2 (deficit - 1/2)), within 0.1 to 6.
        /// </summary>
        private double ShareFactor(int sharePercent, int count)
        {
            var deficit = sharePercent / 100.0 * (_fillPlaced + 1) - count;
            return Math.Clamp(Math.Exp(1.2 * (deficit - 0.5)), 0.1, 6);
        }

        /// <summary>The hallways in a row behind a cell, along its joined placed neighbours (at most 6).</summary>
        private int HallwayRun(int cell)
        {
            var longest = 0;
            for (var s = 0; s < 4; s++)
            {
                if ((_joints[cell] & Side(s)) == 0) continue;
                int previous = cell, at = Neighbour(cell, Side(s)), run = 0;
                while (run < 6 && _room[at] is { IsConnector: true })
                {
                    run++;
                    var next = -1;
                    for (var t = 0; t < 4 && next < 0; t++)
                        if ((_joints[at] & Side(t)) != 0 && Neighbour(at, Side(t)) is var n && n != previous && _room[n] is not null) next = n;
                    if (next < 0) break;
                    previous = at;
                    at = next;
                }
                longest = Math.Max(longest, run);
            }
            return longest;
        }

        // M21: a family short of its fewest is swapped in, by footprint, where the draw missed it.
        private void RepairFamilies(LayoutRandom rng)
        {
            foreach (var family in _profile.FamilyTargets)
            {
                if (family.Min <= 0 || FamilyCount(family.Family) >= family.Min) continue;
                var variants = _pool.Of(RoomKind.Fill).Where(r => r.Shape.Family == family.Family).ToList();
                if (variants.Count == 0) continue;
                var cells = _cells.Where(c => !InBlock(c) && _room[c] is { Kind: RoomKind.Fill } r && r.Shape.Family != family.Family).ToList();
                rng.Shuffle(cells);
                foreach (var cell in cells)
                {
                    if (FamilyCount(family.Family) >= family.Min) break;
                    var current = _room[cell]!;
                    if (!KeepsFewest(current)) continue;
                    var footprint = DoorsOf(cell, current);
                    foreach (var room in variants)
                    {
                        if (Degree(cell) == 1 && !room.IsDestination) continue;
                        if (room.IsLowCombat && !LowRunAllows(cell)) continue;
                        if (!WithinMost(room, current)) continue;
                        var loseS = _sSpots - current.Shape.STierSpots + room.Shape.STierSpots < Math.Min(_sSpots, _profile.MinSTierSpots);
                        var loseB = _bSpots - current.Shape.BTierSpots + room.Shape.BTierSpots < Math.Min(_bSpots, _profile.MinBTierSpots);
                        if (loseS || loseB) continue;
                        var turn = Enumerable.Range(0, 4).FirstOrDefault(t => DoorSets.Rotate(room.Shape.Doors, t * 90) == footprint, -1);
                        if (turn < 0) continue;
                        Set(cell, room, turn);
                        break;
                    }
                }
            }
        }

        // Swap fill rooms for ones of the same footprint that offer the missing tiers.
        private void RepairJumps(LayoutRandom rng)
        {
            if (Shortfall == 0) return;
            var cells = _cells.Where(c => !InBlock(c) && _room[c] is not null && _kind[c] == RoomKind.Fill).ToList();
            rng.Shuffle(cells);
            foreach (var cell in cells)
            {
                var needS = _sSpots < _profile.MinSTierSpots;
                var needB = _bSpots < _profile.MinBTierSpots;
                if (!needS && !needB) return;
                var current = _room[cell]!;
                var footprint = DoorsOf(cell, current);
                var options = new List<(LayoutRoom Room, int Turn)>();
                foreach (var room in _pool.Of(RoomKind.Fill))
                {
                    if (room == current) continue;
                    var gainS = needS && room.Shape.STierSpots > current.Shape.STierSpots;
                    var gainB = needB && room.Shape.BTierSpots > current.Shape.BTierSpots;
                    if (!gainS && !gainB) continue;
                    var loseS = !needS && _sSpots - current.Shape.STierSpots + room.Shape.STierSpots < _profile.MinSTierSpots;
                    var loseB = !needB && _bSpots - current.Shape.BTierSpots + room.Shape.BTierSpots < _profile.MinBTierSpots;
                    if (loseS || loseB) continue;
                    if (room.IsLowCombat && !LowRunAllows(cell)) continue;
                    if (Degree(cell) == 1 && current.IsDestination && !room.IsDestination) continue;
                    if (!KeepsFewest(current) || !WithinMost(room, current)) continue;
                    for (var turn = 0; turn < 4; turn++)
                        if (DoorSets.Rotate(room.Shape.Doors, turn * 90) == footprint)
                        {
                            options.Add((room, turn));
                            break;
                        }
                }
                if (options.Count == 0) continue;
                var weights = options.Select(o => Math.Max(1L, Math.Max(1, o.Room.Info.Weight) * (long)Math.Max(1, _profile.Heights.Of(o.Room.Shape.Height)))).ToList();
                var (pick, pickTurn) = options[rng.Weighted(weights)];
                Set(cell, pick, pickTurn);
            }
        }

        /// <summary>A low combat room at this path cell leaves no run of three among the path's placed rooms.</summary>
        private bool LowRunAllows(int cell)
        {
            var at = PathIndex[cell];
            if (at < 0) return true;
            var run = 1;
            for (var i = at - 1; i >= 0 && _room[Path[i]] is { IsLowCombat: true }; i--) run++;
            for (var i = at + 1; i < Path.Count && _room[Path[i]] is { IsLowCombat: true }; i++) run++;
            return run <= 2;
        }

        /// <summary>The placed rooms' bounding box: its south-west cell and its size.</summary>
        private (int Row, int Column, int Rows, int Columns) Box()
        {
            int minRow = int.MaxValue, maxRow = int.MinValue, minColumn = int.MaxValue, maxColumn = int.MinValue;
            foreach (var cell in _cells)
            {
                int row = cell / _columns, column = cell % _columns;
                minRow = Math.Min(minRow, row);
                maxRow = Math.Max(maxRow, row);
                minColumn = Math.Min(minColumn, column);
                maxColumn = Math.Max(maxColumn, column);
            }
            return (minRow, minColumn, maxRow - minRow + 1, maxColumn - minColumn + 1);
        }

        public LevelPlan ToPlan()
        {
            var box = Box();
            var placements = new List<Placement>(Count);
            string up = "", down = "";
            foreach (var cell in _cells.Order())
            {
                if (_room[cell] is not { } room || (InBlock(cell) && _block[cell] != cell)) continue;
                var role = _kind[cell] switch
                {
                    RoomKind.Arrival => "up",
                    RoomKind.Exit => "down",
                    RoomKind.Treasure => "treasure",
                    RoomKind.Antechamber => "antechamber",
                    RoomKind.Arena => "arena",
                    _ => PathIndex[cell] >= 0 ? "path" : "branch",
                };
                if (_kind[cell] == RoomKind.Arrival) up = room.Name;
                if (_kind[cell] == RoomKind.Exit) down = room.Name;
                placements.Add(new Placement(room.Name, cell / _columns - box.Row, cell % _columns - box.Column, _turn[cell] * 90, role));
            }
            return new LevelPlan(box.Rows, box.Columns, placements, up, down);
        }

        public LayoutReport ToReport(int attempts, string? fallback, bool antechamber)
        {
            var box = Box();
            int joints = 0, capped = 0, wasted = 0, beside = 0, bigCaps = 0;
            foreach (var cell in _cells)
            {
                joints += DoorSets.Count(_joints[cell]);
                beside += Beside(cell);
                if (_room[cell] is not { } room) continue;
                var extra = DoorsOf(cell, room) & ~_joints[cell];
                for (var s = 0; s < 4; s++)
                {
                    if ((extra & Side(s)) == 0) continue;
                    var next = Neighbour(cell, Side(s));
                    if (InBlock(cell)) bigCaps++;
                    else if (next < 0 || !_occupied[next]) capped++;
                    else wasted++;
                }
            }
            joints /= 2;
            int deadEnds = 0, connectors = 0;
            foreach (var cell in _cells)
            {
                if (!IsDeadEnd(cell) || _room[cell] is not { } room) continue;
                deadEnds++;
                if (!room.IsDestination) connectors++;
            }
            var deadEndWhy = connectors == 0 ? null : _deadEndFallback;

            // The longest straight run of the critical path, in rooms.
            var longest = Math.Min(Path.Count, 2);
            for (int i = 2, run = 2; i < Path.Count; i++)
            {
                run = SideTowards(Path[i - 2], Path[i - 1]) == SideTowards(Path[i - 1], Path[i]) ? run + 1 : 2;
                longest = Math.Max(longest, run);
            }

            var seen = new bool[Cells];
            var clusters = new List<int>();
            foreach (var cell in _cells)
                if (!seen[cell] && _room[cell] is { } r && IsTall(r))
                    clusters.Add(ClusterAt(cell, seen));

            return new LayoutReport(
                Path.Select(c => new GridCell(c / _columns - box.Row, c % _columns - box.Column)).ToList(),
                Count, joints, joints - Count + 1, Count - Path.Count, _treasure, _sSpots, _bSpots,
                fallback is not null, fallback, capped, wasted, attempts, antechamber,
                deadEnds, connectors, _profile.IsBoss ? null : deadEndWhy,
                _profile.Rooms, Count == 0 ? 0 : (double)Count / (box.Rows * box.Columns), Count == 0 ? 0 : (double)beside / Count, longest,
                clusters.Count == 0 ? 0 : clusters.Average())
            {
                MultiCellRooms = _bigRooms,
                MultiCellCaps = bigCaps,
            };
        }
    }
}

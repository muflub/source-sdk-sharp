namespace TF2.Descent.Rules.Layout;

/// <summary>
/// The walls a room opens on, in a frame where east is +x and north is +y (the
/// map tools' frame). A set of sides, so a room's doors are one value.
/// </summary>
[Flags]
public enum Doors : byte
{
    None = 0,
    North = 1,
    East = 2,
    South = 4,
    West = 8,
    All = North | East | South | West,
}

/// <summary>The four height classes of the kit (plan_descent_mine_levels section 3.1).</summary>
public enum HeightClass
{
    /// <summary><c>l</c>: 192 to 256, drifts and crawls; no jumping.</summary>
    Low,
    /// <summary><c>c</c>: 512, ordinary rooms; the Scout tiers fit.</summary>
    Chamber,
    /// <summary><c>h</c>: 1024, the standard jump room (B1, B2).</summary>
    Hall,
    /// <summary><c>s</c>: 1536 to 2048, ore passes and caverns (B3).</summary>
    Shaft,
}

/// <summary>
/// What a room offers the jumping classes: <see cref="Jump"/> marks a jump room
/// (R19), the others the jump tiers of plan section 4.1 it holds a spot of.
/// </summary>
[Flags]
public enum JumpFlags : byte
{
    None = 0,
    Jump = 1,
    S2 = 2,
    S3 = 4,
    B1 = 8,
    B2 = 16,
    B3 = 32,
}

/// <summary>
/// A room's layout facts, as its name carries them (decision E8): the shape
/// family and its variant, the doors in the room's unrotated frame, its height
/// class and its jump flags.
/// </summary>
public sealed record RoomShape(string Family, string Variant, Doors Doors, HeightClass Height, JumpFlags Jumps)
{
    /// <summary>A jump room (R19's <c>jump</c>).</summary>
    public bool IsJumpRoom => (Jumps & JumpFlags.Jump) != 0;

    /// <summary>The Scout-tier spots the room offers: one each for S2 and S3.</summary>
    public int STierSpots => ((Jumps & JumpFlags.S2) != 0 ? 1 : 0) + ((Jumps & JumpFlags.S3) != 0 ? 1 : 0);

    /// <summary>The blast-tier spots the room offers: one each for B1, B2 and B3.</summary>
    public int BTierSpots =>
        ((Jumps & JumpFlags.B1) != 0 ? 1 : 0) + ((Jumps & JumpFlags.B2) != 0 ? 1 : 0) + ((Jumps & JumpFlags.B3) != 0 ? 1 : 0);

    /// <summary>The cells the room covers along its own x (east), 1 for a room of one cell (map tools' <c>room_footprint</c>).</summary>
    public int CellsX { get; init; } = 1;

    /// <summary>The cells the room covers along its own y (north).</summary>
    public int CellsY { get; init; } = 1;

    /// <summary>A room larger than one cell: its sockets are one per cell edge of its perimeter, and its doors name the faces that have one on every edge.</summary>
    public bool IsMultiCell => CellsX > 1 || CellsY > 1;

    /// <summary>
    /// A larger room that declares only some of its cell edges as sockets (a boss arena): 16 bits a side,
    /// north's lowest, then east, south, west; bit k is the edge k cells along the side (from the west on
    /// north and south, from the south on east and west). 0 means a socket on every edge of every door side.
    /// </summary>
    public ulong SocketMask { get; init; }

    /// <summary>Whether the room has a socket on the edge <paramref name="offset"/> cells along <paramref name="side"/> (unturned).</summary>
    public bool HasSocket(Doors side, int offset)
    {
        if ((Doors & side) == 0) return false;
        if (SocketMask == 0) return true;
        var i = side switch { Doors.North => 0, Doors.East => 1, Doors.South => 2, _ => 3 };
        return ((SocketMask >> (16 * i + offset)) & 1) != 0;
    }

    /// <summary>The mask bit of one socket (<see cref="SocketMask"/>).</summary>
    public static ulong SocketBit(Doors side, int offset) =>
        1UL << (16 * (side switch { Doors.North => 0, Doors.East => 1, Doors.South => 2, _ => 3 }) + offset);

    /// <summary>
    /// A quiet room (flag <c>q</c>, brief p9): one with no pack spawn, which the layout places only
    /// beside the way in, so a party arriving has no pack in sight.
    /// </summary>
    public bool Quiet { get; init; }

    /// <summary>The name these facts are spelled as (<see cref="RoomName.Format"/>).</summary>
    public string Name => RoomName.Format(this);
}

/// <summary>
/// The room-name codec (decision E8, 2026-11-10). <see cref="SourceSharp.Host.Contracts.RoomInfo"/>
/// carries no doors, so a room's layout facts ride in its name:
/// <c>&lt;family&gt;_&lt;variant&gt;-&lt;doors&gt;-&lt;height&gt;[-&lt;W&gt;x&lt;D&gt;][-&lt;flags&gt;]</c>, for example
/// <c>drift_a-ew-l</c>, <c>gallery_b-nesw-h-j</c>, <c>arrival_cage-e-c</c>, <c>hall2_a-nesw-h-2x2-j</c>.
/// <list type="bullet">
/// <item><c>family</c>: a lower-case letter then lower-case letters and digits;
/// <c>variant</c>: lower-case letters and digits.</item>
/// <item><c>doors</c>: the walls the room opens on unrotated, from <c>n e s w</c>
/// in that order, at least one. A larger room that has sockets on only some cell edges of a
/// side spells that side's letter followed by the edges' numbers in order (<c>e1w1</c>, a 3x3
/// arena opening only in the middle of its east and west walls); a side with a socket on every
/// edge is spelled bare.</item>
/// <item><c>height</c>: <c>l</c> low, <c>c</c> chamber, <c>h</c> hall, <c>s</c> shaft.</item>
/// <item><c>W</c>x<c>D</c>: the footprint of a room larger than one cell (map tools'
/// <c>room_footprint</c>, 1 to 16 cells a side), never spelled for one cell. Its doors are the
/// faces with a socket on every cell edge, since the host's <c>RoomInfo</c> carries no footprint
/// and no sockets either.</item>
/// <item><c>flags</c>: any of <c>j s2 s3 b1 b2 b3 q</c> in that order, joined by <c>.</c>; <c>q</c>
/// is a quiet room (<see cref="RoomShape.Quiet"/>).</item>
/// </list>
/// Each set of facts has exactly one spelling, so <see cref="Parse"/> refuses a
/// name whose doors or flags are out of order or repeated, and
/// <c>Format(Parse(name)) == name</c> for every name it accepts. The codec has no
/// dependency beyond the base library, so the room generators can share it.
/// </summary>
public static class RoomName
{
    private const string DoorLetters = "nesw";
    private const string HeightLetters = "lchs";

    /// <summary>The most cells a room covers along a side (map tools' <c>room_footprint</c> guard).</summary>
    public const int MaxCells = 16;

    /// <summary>The facts a name spells; throws <see cref="FormatException"/> naming what is wrong.</summary>
    public static RoomShape Parse(string name) =>
        TryParse(name, out var shape, out var error) ? shape! : throw new FormatException(error);

    /// <summary>The facts a name spells, or false and why not.</summary>
    public static bool TryParse(string? name, out RoomShape? shape, out string? error)
    {
        shape = null;
        error = Check(name, out var family, out var variant, out var doors, out var height, out var jumps, out var cellsX, out var cellsY, out var mask, out var quiet);
        if (error is not null)
        {
            error = $"room name '{name}': {error}; a layout room is named <family>_<variant>-<doors>-<height>[-<flags>], e.g. drift_a-ew-l";
            return false;
        }
        shape = new RoomShape(family, variant, doors, height, jumps) { CellsX = cellsX, CellsY = cellsY, SocketMask = mask, Quiet = quiet };
        return true;
    }

    /// <summary>The one spelling of a room's facts; throws <see cref="ArgumentException"/> for facts no name can hold.</summary>
    public static string Format(RoomShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (!IsWord(shape.Family, letterFirst: true))
            throw new ArgumentException($"family '{shape.Family}' is not a lower-case letter followed by lower-case letters and digits", nameof(shape));
        if (!IsWord(shape.Variant, letterFirst: false))
            throw new ArgumentException($"variant '{shape.Variant}' is not lower-case letters and digits", nameof(shape));
        if (shape.Doors == Doors.None || (shape.Doors & ~Doors.All) != 0)
            throw new ArgumentException($"doors {shape.Doors} is not a non-empty set of n, e, s, w", nameof(shape));
        if ((int)shape.Height is < 0 or > 3)
            throw new ArgumentException($"height {shape.Height} is not a height class", nameof(shape));
        const JumpFlags allFlags = JumpFlags.Jump | JumpFlags.S2 | JumpFlags.S3 | JumpFlags.B1 | JumpFlags.B2 | JumpFlags.B3;
        if ((shape.Jumps & ~allFlags) != 0)
            throw new ArgumentException($"jump flags {shape.Jumps} hold an unknown flag", nameof(shape));

        if (shape.CellsX is < 1 or > MaxCells || shape.CellsY is < 1 or > MaxCells)
            throw new ArgumentException($"footprint {shape.CellsX}x{shape.CellsY} is not 1 to {MaxCells} cells a side", nameof(shape));
        if (shape.SocketMask != 0 && (!shape.IsMultiCell || Math.Max(shape.CellsX, shape.CellsY) > 10))
            throw new ArgumentException("only a larger room of at most 10 cells a side chooses its sockets", nameof(shape));
        var doors = "";
        for (var i = 0; i < 4; i++)
        {
            if ((shape.Doors & DoorAt(i)) == 0)
            {
                if (shape.SocketMask != 0 && ((shape.SocketMask >> (16 * i)) & 0xFFFF) != 0)
                    throw new ArgumentException($"a socket on side {DoorLetters[i]}, which is not a door", nameof(shape));
                continue;
            }
            doors += DoorLetters[i];
            if (shape.SocketMask == 0) continue;
            var length = i is 0 or 2 ? shape.CellsX : shape.CellsY;
            var bits = (shape.SocketMask >> (16 * i)) & 0xFFFF;
            if (bits == 0)
                throw new ArgumentException($"door side {DoorLetters[i]} has no socket", nameof(shape));
            if (bits >> length != 0)
                throw new ArgumentException($"a socket on side {DoorLetters[i]} past its {length} cells", nameof(shape));
            if (bits != (1UL << length) - 1)
                for (var k = 0; k < length; k++)
                    if (((bits >> k) & 1) != 0) doors += (char)('0' + k);
        }
        var name = $"{shape.Family}_{shape.Variant}-{doors}-{HeightLetters[(int)shape.Height]}";
        if (shape.IsMultiCell) name += $"-{shape.CellsX}x{shape.CellsY}";
        if (shape.Jumps == JumpFlags.None && !shape.Quiet) return name;
        var flags = new List<string>(7);
        for (var i = 0; i < 6; i++)
            if ((shape.Jumps & FlagAt(i)) != 0) flags.Add(FlagWord(i));
        if (shape.Quiet) flags.Add(QuietWord);
        return name + "-" + string.Join('.', flags);
    }

    private static string? Check(string? name, out string family, out string variant, out Doors doors, out HeightClass height, out JumpFlags jumps,
        out int cellsX, out int cellsY, out ulong mask, out bool quiet)
    {
        mask = 0;
        quiet = false;
        cellsX = cellsY = 1;
        family = variant = "";
        doors = Doors.None;
        height = HeightClass.Low;
        jumps = JumpFlags.None;
        if (string.IsNullOrEmpty(name)) return "the name is empty";

        var parts = name.Split('-');
        if (parts.Length is < 3 or > 5) return $"it has {parts.Length} '-'-separated part(s), not 3 to 5";

        var stem = parts[0].Split('_');
        if (stem.Length != 2) return $"'{parts[0]}' is not <family>_<variant> with exactly one '_'";
        if (!IsWord(stem[0], letterFirst: true)) return $"family '{stem[0]}' is not a lower-case letter followed by lower-case letters and digits";
        if (!IsWord(stem[1], letterFirst: false)) return $"variant '{stem[1]}' is not lower-case letters and digits";
        family = stem[0];
        variant = stem[1];

        var last = -1;
        if (parts[1].Length == 0) return "the doors are empty: a room opens on at least one of n, e, s, w";
        var edges = new List<(int Side, int Offset)>();
        var lastEdge = -1;
        foreach (var c in parts[1])
        {
            if (c is >= '0' and <= '9')
            {
                if (last < 0) return $"doors '{parts[1]}' start with a socket number, not a side";
                if (c - '0' <= lastEdge) return $"doors '{parts[1]}': side {DoorLetters[last]}'s socket numbers are not rising";
                lastEdge = c - '0';
                edges.Add((last, lastEdge));
                continue;
            }
            var i = DoorLetters.IndexOf(c);
            if (i < 0) return $"door '{c}' is not one of n, e, s, w";
            if (i <= last) return $"doors '{parts[1]}' are not in the order n, e, s, w without repeats";
            last = i;
            lastEdge = -1;
            doors |= DoorAt(i);
        }

        if (parts[2].Length != 1 || HeightLetters.IndexOf(parts[2][0]) < 0)
            return $"height '{parts[2]}' is not one of l (low), c (chamber), h (hall), s (shaft)";
        height = (HeightClass)HeightLetters.IndexOf(parts[2][0]);

        var next = 3;
        if (parts.Length > next && IsFootprint(parts[next]))
        {
            var wd = parts[next].Split('x');
            if (wd.Length != 2 || !int.TryParse(wd[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out cellsX)
                || !int.TryParse(wd[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out cellsY)
                || cellsX is < 1 or > MaxCells || cellsY is < 1 or > MaxCells || wd[0][0] == '0' || wd[1][0] == '0')
                return $"footprint '{parts[next]}' is not <W>x<D> of 1 to {MaxCells} cells a side";
            if (cellsX == 1 && cellsY == 1) return "footprint '1x1' is a room of one cell, which spells no footprint";
            next++;
        }
        if (edges.Count > 0)
        {
            if (cellsX == 1 && cellsY == 1) return $"doors '{parts[1]}' number sockets, which only a larger room does";
            if (Math.Max(cellsX, cellsY) > 10) return "a room choosing its sockets is at most 10 cells a side";
            for (var i = 0; i < 4; i++)
            {
                if ((doors & DoorAt(i)) == 0) continue;
                var length = i is 0 or 2 ? cellsX : cellsY;
                var mine = edges.Where(e => e.Side == i).Select(e => e.Offset).ToList();
                if (mine.Any(o => o >= length)) return $"side {DoorLetters[i]} numbers a socket past its {length} cells";
                if (mine.Count == length) return $"side {DoorLetters[i]} numbers every one of its sockets; spell it bare";
                var bits = mine.Count == 0 ? (1UL << length) - 1 : mine.Aggregate(0UL, (m, o) => m | (1UL << o));
                mask |= bits << (16 * i);
            }
        }
        if (parts.Length > next + 1) return $"'{parts[next]}' is followed by another part, but only a footprint <W>x<D> comes before the flags";
        if (parts.Length == next + 1)
        {
            last = -1;
            foreach (var word in parts[next].Split('.'))
            {
                var i = word == QuietWord ? 6 : FlagIndex(word);
                if (i < 0) return $"flag '{word}' is not one of j, s2, s3, b1, b2, b3, q";
                if (i <= last) return $"flags '{parts[next]}' are not in the order j, s2, s3, b1, b2, b3, q without repeats";
                last = i;
                if (i == 6) quiet = true;
                else jumps |= FlagAt(i);
            }
        }
        return null;
    }

    private static bool IsFootprint(string s) => s.Length >= 3 && s.Contains('x') && s.All(c => c is 'x' or (>= '0' and <= '9'));

    private static bool IsWord(string? s, bool letterFirst)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (letterFirst && s[0] is < 'a' or > 'z') return false;
        foreach (var c in s)
            if (c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9')) return false;
        return true;
    }

    /// <summary>The flag a quiet room's name carries last.</summary>
    private const string QuietWord = "q";

    private static Doors DoorAt(int i) => (Doors)(1 << i);
    private static JumpFlags FlagAt(int i) => (JumpFlags)(1 << i);

    private static string FlagWord(int i) => i switch
    {
        0 => "j",
        1 => "s2",
        2 => "s3",
        3 => "b1",
        4 => "b2",
        _ => "b3",
    };

    private static int FlagIndex(string word) => word switch
    {
        "j" => 0,
        "s2" => 1,
        "s3" => 2,
        "b1" => 3,
        "b2" => 4,
        "b3" => 5,
        _ => -1,
    };
}

/// <summary>Turning and counting door sets.</summary>
public static class DoorSets
{
    /// <summary>
    /// The doors of a room turned <paramref name="degreesCcw"/> counter-clockwise
    /// seen from above, as the map tools turn a placement: a quarter turn takes
    /// east to north, north to west, west to south and south to east.
    /// </summary>
    public static Doors Rotate(Doors doors, int degreesCcw)
    {
        if (degreesCcw % 90 != 0) throw new ArgumentException($"{degreesCcw} degrees is not a quarter turn", nameof(degreesCcw));
        var turns = ((degreesCcw / 90) % 4 + 4) % 4;
        var bits = (int)doors & 0xF;
        // Bits run N, E, S, W (clockwise), so a counter-clockwise turn is a right shift.
        for (var i = 0; i < turns; i++)
            bits = (bits >> 1) | ((bits & 1) << 3);
        return (Doors)bits;
    }

    /// <summary>The side across from one side.</summary>
    public static Doors Opposite(Doors side) => side switch
    {
        Doors.North => Doors.South,
        Doors.South => Doors.North,
        Doors.East => Doors.West,
        Doors.West => Doors.East,
        _ => throw new ArgumentException($"{side} is not one side", nameof(side)),
    };

    /// <summary>How many doors a set holds.</summary>
    public static int Count(Doors doors) => System.Numerics.BitOperations.PopCount((uint)doors & 0xF);
}

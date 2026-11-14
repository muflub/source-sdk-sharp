using System.Globalization;

namespace SourceSharp.MapGen.Descent;

/// <summary>A room's level-transition role (<c>room_role</c>).</summary>
public enum RoomRole
{
    None,
    Up,
    Down,
}

/// <summary>A point or brush entity of a room, room-local.</summary>
/// <param name="Origin">Its origin, room-local, or null for a brush entity placed by its brushes.</param>
public sealed record RoomEntity(
    string ClassName,
    Point? Origin,
    IReadOnlyList<KeyValuePair<string, string>> Keys,
    IReadOnlyList<VmfConnection> Wires,
    IReadOnlyList<Brush> Brushes)
{
    /// <summary>The value of a key, or null.</summary>
    public string? Key(string key) => Keys.Where(kv => kv.Key == key).Select(kv => kv.Value).FirstOrDefault();
}

/// <summary>
/// One room, room-local: its cell is <c>[0, cell]²</c> by <c>[0, height]</c>,
/// and nothing of it leaves that box.
/// </summary>
public sealed record Room(
    string Name,
    RoomKit Kit,
    int Height,
    int Width,
    IReadOnlyList<CellFace> Doors,
    RoomRole Role,
    IReadOnlyList<Brush> World,
    IReadOnlyList<RoomEntity> Entities)
{
    /// <summary>The room's name on the level map (the <c>info_room</c>'s <c>map_label</c>), or null.</summary>
    public string? MapLabel { get; init; }

    /// <summary>What <c>library.json</c> says of the room beyond its markers and doors.</summary>
    public RoomEntry Entry { get; init; } = RoomEntry.Default;

    /// <summary>Every Descent marker of the room, with where it stands.</summary>
    public IEnumerable<(MarkerKind Kind, Point At)> Markers =>
        Entities.Where(e => e.ClassName == "info_poi" && e.Origin is not null)
            .Select(e => (Kind: Descent.Markers.KindOf(e.Key("poi_type") ?? ""), At: e.Origin!.Value))
            .Where(m => m.Kind is not null)
            .Select(m => (m.Kind!.Value, m.At));

    /// <summary>Whether the room is taller or lower than its cell, and so writes <c>room_height</c>.</summary>
    public bool IsShaped => Height != Kit.Cell;
}

/// <summary>
/// Builds one Descent room in room-local coordinates (plan task A2): the
/// sealed shell with a door on any of its four walls, a height of its own,
/// boxes, lamps, markers, and the pieces of a transition room.
/// </summary>
/// <remarks>
/// <para>
/// <b>The shell.</b> A room's floor and ceiling are slabs over the whole cell
/// (one brush each). Its walls enclose a centre square of
/// <see cref="Room.Width"/> and one arm of the same width from the square to
/// each door's wall, so the interior is a plus, a tee, an L or a bar. At
/// <see cref="RoomKit.Interior"/> the square is the whole cell and the arms
/// are only the doorways: the ordinary cube shell. A door's wall slab at the
/// cell face holds the plug (exactly the kit's opening), a jamb each side
/// when the arm is wider than the door, and a lintel above when the room is
/// taller than the door.
/// </para>
/// <para>
/// <b>A narrow corridor in a 1024 cell</b> is that plus shape at 256 to 384
/// wide: the walls hug the corridor and the rest of the cell is left empty,
/// outside the sealed shell. vbsp's outside fill then culls every face that
/// looks into that void, the slabs' included, so the unused cell costs no
/// lightmap and no visible face; filling it with solid blocks instead would
/// add brushes and gain nothing, since a face against the void is culled
/// either way. The cell stays the room's box (the pipeline's rule: the plug
/// sits on the cell face, and nothing crosses it), so corridors and halls
/// still meet door to door. Pieces in line are joined (<see cref="Merge"/>),
/// so a drift is six brushes: two slabs, two side walls and two plugs.
/// </para>
/// </remarks>
public sealed class RoomBuilder
{
    private readonly string name;
    private readonly RoomKit kit;
    private readonly RoomTheme theme;
    private readonly List<CellFace> doors = [];
    private readonly List<Brush> features = [];
    private readonly List<RoomEntity> entities = [];
    private int height;
    private int width;
    private RoomRole role;
    private Point arrival;
    private int arrivalYaw;
    private bool frames = true;
    private string? mapLabel;
    private RoomEntry entry = RoomEntry.Default;

    public RoomBuilder(string name, RoomKit kit, RoomTheme theme)
    {
        this.name = name;
        this.kit = kit;
        this.theme = theme;
        height = kit.Cell;
        width = kit.Interior;
    }

    /// <summary>The volume a transition room's <c>cxry_transition</c> fills: 64 square and 64 tall, on the floor at the cell's centre.</summary>
    public static (Point Mins, Point Maxs) TransitionVolume(RoomKit kit)
    {
        float m = kit.Cell / 2f, t = kit.WallDepth;
        return (new(m - 32, m - 32, t), new(m + 32, m + 32, t + 64));
    }

    /// <summary>The down room's hallway trigger: 128 square and 128 tall round the volume.</summary>
    public static (Point Mins, Point Maxs) HallwayTrigger(RoomKit kit)
    {
        float m = kit.Cell / 2f, t = kit.WallDepth;
        return (new(m - 64, m - 64, t), new(m + 64, m + 64, t + 128));
    }

    /// <summary>The transition volume's room-local name, which the link renames per placement.</summary>
    public const string VolumeName = "cxry_transition";

    /// <summary>The room's own height (<c>room_height</c>); the cell size by default.</summary>
    public RoomBuilder WithHeight(int value)
    {
        height = value;
        return this;
    }

    /// <summary>The interior's width, the corridor's; the whole cell (<see cref="RoomKit.Interior"/>) by default.</summary>
    public RoomBuilder WithWidth(int value)
    {
        width = value;
        return this;
    }

    public RoomBuilder WithDoors(params CellFace[] faces)
    {
        foreach (CellFace face in faces)
        {
            if (doors.Contains(face))
                throw new ArgumentException($"room {name}: two doors on its {face} wall", nameof(faces));
            doors.Add(face);
        }

        return this;
    }

    /// <summary>A box inside the room, in the theme's block surface unless another is named.</summary>
    public RoomBuilder WithBox(Point mins, Point maxs, Surface? surface = null)
    {
        features.Add(Brush.Box(mins, maxs, surface ?? theme.Block));
        return this;
    }

    /// <summary>Any brush inside the room (a ramp, a pillar, a displacement), room-local.</summary>
    public RoomBuilder WithBrush(Brush brush)
    {
        features.Add(brush);
        return this;
    }

    /// <summary>Several brushes (a flight of stairs, a ring wall).</summary>
    public RoomBuilder WithBrushes(IEnumerable<Brush> brushes)
    {
        features.AddRange(brushes);
        return this;
    }

    /// <summary>Leaves the doors unframed (a test fixture; every real room frames its sockets).</summary>
    public RoomBuilder WithoutDoorFrames()
    {
        frames = false;
        return this;
    }

    /// <summary>The kit this room is built on.</summary>
    public RoomKit Kit => kit;

    /// <summary>The theme it is drawn in.</summary>
    public RoomTheme Theme => theme;

    /// <summary>The room's height as set so far.</summary>
    public int Height => height;

    /// <summary>The interior's width as set so far.</summary>
    public int Width => width;

    /// <summary>The walls the room has doors on so far.</summary>
    public IReadOnlyList<CellFace> DoorFaces => doors;

    /// <summary>The floor's top, where a player stands and the doors' sills are.</summary>
    public float Floor => kit.Sill;

    /// <summary>The ceiling's underside.</summary>
    public float CeilingUnderside => height - kit.WallDepth;

    /// <summary>The low edge of the interior's centre square (x and y alike).</summary>
    public float InteriorLow => (kit.Cell - width) / 2f;

    /// <summary>The high edge of the interior's centre square.</summary>
    public float InteriorHigh => (kit.Cell + width) / 2f;

    /// <summary>How far in front of a door nothing but the shell may stand, so the doorway stays walkable.</summary>
    public const int DoorApproach = 96;

    /// <summary>The box in front of the door on <paramref name="face"/> that features must keep clear, room-local.</summary>
    public (Point Mins, Point Maxs) ApproachBox(CellFace face)
    {
        var box = Face(face, kit.Cell - kit.WallDepth - DoorApproach, kit.Cell, kit.DoorLow, kit.DoorHigh, kit.Sill, kit.Head, theme.Block);
        return (box.Mins, box.Maxs);
    }

    /// <summary>A <c>light</c> entity: <paramref name="brightness"/> is the fourth number of <c>_light</c>; the falloff is <see cref="Lamp"/>'s default.</summary>
    public RoomBuilder WithLight(Point at, int brightness, string colour = "255 214 160")
        => WithLamp(at, new Lamp(colour, brightness));

    /// <summary>A lamp's <c>light</c> entity at <paramref name="at"/>, room-local; <see cref="Lamp.Default"/> unless another is given.</summary>
    public RoomBuilder WithLamp(Point at, Lamp? lamp = null)
    {
        Lamp l = lamp ?? Lamp.Default;
        if (l.Brightness <= 0 || l.FiftyPercent <= 0 || l.ZeroPercent <= l.FiftyPercent)
            throw new ArgumentException($"room {name}: a lamp needs a brightness and 0 < half distance < zero distance ({l.Brightness}, {l.FiftyPercent}, {l.ZeroPercent})");
        entities.Add(new RoomEntity("light", at, l.Keys, [], []));
        return this;
    }

    /// <summary>An <c>info_poi</c> marker (a pack spawn, a shrine, a player spawn), facing <paramref name="yaw"/>.</summary>
    public RoomBuilder WithPoi(string type, Point at, int yaw = 0)
    {
        entities.Add(Point("info_poi", at, ("poi_type", type), ("angles", Angles(yaw))));
        return this;
    }

    /// <summary>
    /// A Descent marker (<see cref="MarkerKind"/>) standing at
    /// <paramref name="at"/>, room-local, on the floor or ledge below it,
    /// facing <paramref name="yaw"/>. <paramref name="radius"/> is the
    /// <c>poi_radius</c> (a pack's spread), <paramref name="tags"/> the
    /// <c>poi_tags</c>.
    /// </summary>
    public RoomBuilder WithMarker(MarkerKind kind, Point at, int yaw = 0, int? radius = null, string? tags = null)
    {
        if (kind == MarkerKind.MapMarker)
            throw new ArgumentException($"room {name}: a map marker has an icon; use WithMapMarker", nameof(kind));
        var keys = new List<(string, string)> { ("poi_type", Descent.Markers.PoiType(kind)), ("angles", Angles(yaw)) };
        if (radius is { } r)
            keys.Add(("poi_radius", RoomLibrary.Num(r)));
        if (tags is not null)
            keys.Add(("poi_tags", tags));
        entities.Add(Point("info_poi", at, [.. keys]));
        return this;
    }

    /// <summary>A point on the level map: <c>map_marker</c> names its icon, <c>map_label</c> its text.</summary>
    public RoomBuilder WithMapMarker(Point at, string icon, string? label = null)
    {
        var keys = new List<(string, string)> { ("poi_type", Descent.Markers.PoiType(MarkerKind.MapMarker)), ("map_marker", icon) };
        if (label is not null)
            keys.Add(("map_label", label));
        entities.Add(Point("info_poi", at, [.. keys]));
        return this;
    }

    /// <summary>The room's <c>library.json</c> entry: its tags, weight, depths, and whether it is a jump room or a low one.</summary>
    public RoomBuilder WithEntry(RoomEntry value)
    {
        entry = value;
        return this;
    }

    /// <summary>The room's name on the level map.</summary>
    public RoomBuilder WithMapLabel(string label)
    {
        mapLabel = label;
        return this;
    }

    /// <summary>
    /// Makes this a transition room: <c>room_role</c>, the transition volume,
    /// what fires it (a button for the way up, a hallway trigger for the way
    /// down) and the arrival point.
    /// </summary>
    public RoomBuilder WithRole(RoomRole value, Point arrivalAt, int yaw)
    {
        role = value;
        arrival = arrivalAt;
        arrivalYaw = yaw;
        return this;
    }

    public Room Build()
    {
        Validate();
        var shell = new List<RoomBox>();
        Shell(shell);
        Merge(shell);
        var world = shell.Select(b => b.ToBrush()).ToList();
        foreach (Brush brush in features)
        {
            if (!Inside(brush.Mins, brush.Maxs))
                throw new ArgumentException($"room {name}: a brush ({brush.Mins})-({brush.Maxs}) leaves the room's cell");
            foreach (CellFace face in doors)
            {
                var (mins, maxs) = ApproachBox(face);
                if (brush.Overlaps(mins, maxs))
                    throw new ArgumentException($"room {name}: a brush ({brush.Mins})-({brush.Maxs}) stands in front of its {WallName(face)} door");
            }

            world.Add(brush);
        }

        var all = new List<RoomEntity>(entities);
        if (frames)
            foreach (CellFace face in doors)
            {
                all.Add(Frame(face));
                all.Add(Cap(face));
            }
        if (role != RoomRole.None)
            RolePieces(all);
        return new Room(name, kit, height, width, [.. doors], role, world, all) { MapLabel = mapLabel, Entry = entry };
    }

    private void Validate()
    {
        if (height < kit.LeastHeight)
            throw new ArgumentOutOfRangeException(nameof(height), height,
                $"room {name}: {height} is lower than the door kit allows ({kit.LeastHeight}: door_height + 2 x wall_depth)");
        if (height % kit.NavVoxel != 0)
            throw new ArgumentOutOfRangeException(nameof(height), height,
                $"room {name}: {height} is not a whole number of the kit's {kit.NavVoxel}-unit navigation voxels");
        if (width < kit.DoorWidth || width > kit.Interior)
            throw new ArgumentOutOfRangeException(nameof(width), width,
                $"room {name}: width {width} is outside the door's {kit.DoorWidth} to the cell's {kit.Interior}");
        if (frames && (kit.ClearWidth < RoomKit.GiantWidth || kit.ClearHeight < RoomKit.GiantHeight))
            throw new ArgumentException(
                $"room {name}: the {kit.Name} kit's door frame leaves {kit.ClearWidth} by {kit.ClearHeight}, less than a giant's {RoomKit.GiantWidth} by {RoomKit.GiantHeight}");
        if ((kit.Cell - width) % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, $"room {name}: width {width} does not centre on whole units");
        if (role != RoomRole.None && !Inside(arrival, arrival + new Point(0, 0, 72)))
            throw new ArgumentException($"room {name}: its arrival ({arrival}) is outside the room");
    }

    private bool Inside(Point mins, Point maxs)
        => mins.X >= 0 && mins.Y >= 0 && mins.Z >= 0 && maxs.X <= kit.Cell && maxs.Y <= kit.Cell && maxs.Z <= height;

    private void Shell(List<RoomBox> world)
    {
        float c = kit.Cell, t = kit.WallDepth, h = height;
        float a = (c - width) / 2f, b = a + width;
        float z0 = t, z1 = h - t;

        world.Add(new RoomBox(new(0, 0, 0), new(c, c, t), theme.Floor));
        world.Add(new RoomBox(new(0, 0, h - t), new(c, c, h), theme.Ceiling));

        // The square's four corner posts, so the arms' side walls and the caps
        // meet without overlapping. At full width they are the cell's corners.
        foreach (float x in (float[])[a - t, b])
            foreach (float y in (float[])[a - t, b])
                world.Add(new RoomBox(new(x, y, z0), new(x + t, y + t, z1), theme.Wall));

        foreach (CellFace face in (CellFace[])[CellFace.East, CellFace.West, CellFace.North, CellFace.South])
        {
            if (!doors.Contains(face))
            {
                // A cap across the square's side.
                world.Add(Face(face, b, b + t, a, b, z0, z1, theme.Wall));
                continue;
            }

            // The arm's side walls, from the post to the cell face (none at full width).
            if (b + t < c)
            {
                world.Add(Face(face, b + t, c, a - t, a, z0, z1, theme.Wall));
                world.Add(Face(face, b + t, c, b, b + t, z0, z1, theme.Wall));
            }

            // The door's wall slab on the cell face: jambs, lintel, plug.
            if (a < kit.DoorLow)
            {
                world.Add(Face(face, c - t, c, a, kit.DoorLow, z0, z1, theme.Wall));
                world.Add(Face(face, c - t, c, kit.DoorHigh, b, z0, z1, theme.Wall));
            }

            if (kit.Head < z1)
                world.Add(Face(face, c - t, c, kit.DoorLow, kit.DoorHigh, kit.Head, z1, theme.Wall));

            var plug = kit.PlugBox(face);
            world.Add(new RoomBox(plug.Mins, plug.Maxs, theme.Plug));
        }
    }

    /// <summary>
    /// Joins boxes of one material that are one box in two pieces: the same
    /// extent on two axes, touching on the third. The shell is written as
    /// posts, caps, side walls and jambs so that they never overlap; most of
    /// them line up, and a corridor's whole side is then one brush. The union
    /// of two such boxes is exactly the two, so no overlap is made.
    /// </summary>
    private static void Merge(List<RoomBox> boxes)
    {
        bool merged = true;
        while (merged)
        {
            merged = false;
            for (int i = 0; i < boxes.Count && !merged; i++)
                for (int j = i + 1; j < boxes.Count && !merged; j++)
                    if (Join(boxes[i], boxes[j]) is { } union)
                    {
                        boxes[i] = union;
                        boxes.RemoveAt(j);
                        merged = true;
                    }
        }
    }

    private static RoomBox? Join(RoomBox a, RoomBox b)
    {
        if (a.Surface != b.Surface)
            return null;
        bool sx = a.Mins.X == b.Mins.X && a.Maxs.X == b.Maxs.X;
        bool sy = a.Mins.Y == b.Mins.Y && a.Maxs.Y == b.Maxs.Y;
        bool sz = a.Mins.Z == b.Mins.Z && a.Maxs.Z == b.Maxs.Z;
        bool tx = a.Maxs.X == b.Mins.X || b.Maxs.X == a.Mins.X;
        bool ty = a.Maxs.Y == b.Mins.Y || b.Maxs.Y == a.Mins.Y;
        bool tz = a.Maxs.Z == b.Mins.Z || b.Maxs.Z == a.Mins.Z;
        if (!((sy && sz && tx) || (sx && sz && ty) || (sx && sy && tz)))
            return null;
        return new RoomBox(
            new(Math.Min(a.Mins.X, b.Mins.X), Math.Min(a.Mins.Y, b.Mins.Y), Math.Min(a.Mins.Z, b.Mins.Z)),
            new(Math.Max(a.Maxs.X, b.Maxs.X), Math.Max(a.Maxs.Y, b.Maxs.Y), Math.Max(a.Maxs.Z, b.Maxs.Z)),
            a.Surface);
    }

    /// <summary>
    /// A box given along the face's outward axis (east's x, from the cell's
    /// centre side) and across it; west and south mirror east and north. Every
    /// across range this class asks for is symmetric about the cell's centre
    /// line in pairs, so the mirror needs no across flip.
    /// </summary>
    private RoomBox Face(CellFace face, float out0, float out1, float across0, float across1, float z0, float z1, Surface material)
    {
        float c = kit.Cell;
        return face switch
        {
            CellFace.East => new RoomBox(new(out0, across0, z0), new(out1, across1, z1), material),
            CellFace.West => new RoomBox(new(c - out1, across0, z0), new(c - out0, across1, z1), material),
            CellFace.North => new RoomBox(new(across0, out0, z0), new(across1, out1, z1), material),
            CellFace.South => new RoomBox(new(across0, c - out1, z0), new(across1, c - out0, z1), material),
            _ => throw new ArgumentOutOfRangeException(nameof(face), face, "a cell has four faces"),
        };
    }

    /// <summary>The name of a wall as <c>room_needs</c> and the sockets spell it.</summary>
    public static string WallName(CellFace face) => face switch
    {
        CellFace.East => "east",
        CellFace.West => "west",
        CellFace.North => "north",
        _ => "south",
    };

    /// <summary>
    /// The frame of the door on <paramref name="face"/> (<see cref="DoorFrame"/>):
    /// two posts, a head beam and a sill plate lining this room's half of the
    /// opening, from the cell face to <see cref="DoorFrame.Reveal"/> proud of
    /// the wall, kept by the link only where the socket is joined.
    /// </summary>
    private RoomEntity Frame(CellFace face)
    {
        DoorFrame f = kit.Frame;
        float c = kit.Cell, t = kit.WallDepth, lo = kit.DoorLow, hi = kit.DoorHigh, s = kit.Sill, h = kit.Head;
        float in0 = c - t - f.Reveal;
        Surface timber = theme.Frame;
        Brush[] pieces =
        [
            Face(face, in0, c, lo, lo + f.Post, s, h, timber).ToBrush(),
            Face(face, in0, c, hi - f.Post, hi, s, h, timber).ToBrush(),
            Face(face, in0, c, lo + f.Post, hi - f.Post, h - f.Head, h, timber).ToBrush(),
            Face(face, in0, c, lo + f.Post, hi - f.Post, s, s + f.Sill, timber).ToBrush(),
        ];
        return new RoomEntity("func_brush", null,
            [new("room_needs", "joined_" + WallName(face)), new("Solidity", "2"), new("vrad_brush_cast_shadows", "0"),
             new("_minlight", RoomLibrary.Num(f.MinLight))],
            [], pieces);
    }

    /// <summary>
    /// The cap of the door on <paramref name="face"/>: a panel of the wall's
    /// surface over the opening, <see cref="DoorFrame.Cap"/> in front of the
    /// plug, kept by the link only where the socket is capped.
    /// </summary>
    private RoomEntity Cap(CellFace face)
    {
        float c = kit.Cell, t = kit.WallDepth;
        Brush panel = Face(face, c - t - kit.Frame.Cap, c - t, kit.DoorLow, kit.DoorHigh, kit.Sill, kit.Head, theme.Wall).ToBrush();
        return new RoomEntity("func_brush", null,
            [new("room_needs", "!joined_" + WallName(face)), new("Solidity", "2"), new("vrad_brush_cast_shadows", "0")],
            [], [panel]);
    }

    private void RolePieces(List<RoomEntity> all)
    {
        var volume = TransitionVolume(kit);
        all.Add(new RoomEntity("trigger_room_transition", null, [new("targetname", VolumeName)], [],
            [Brush.Box(volume.Mins, volume.Maxs, theme.Trigger)]));

        if (role == RoomRole.Up)
        {
            // A button on a post in the square's north-east corner, out of
            // every door's line but the arm it shares the corner with.
            float b = (kit.Cell + width) / 2f, t = kit.WallDepth;
            all.Add(new RoomEntity("func_button", null, [new("spawnflags", "1025"), new("wait", "-1")],
                [new VmfConnection("OnPressed", VolumeName, "Transition", "", 0f, -1)],
                [Brush.Box(new(b - 16, b - 16, t), new(b, b, t + 48), theme.Block)]));
        }
        else
        {
            var hall = HallwayTrigger(kit);
            all.Add(new RoomEntity("trigger_once", null, [new("spawnflags", "1")],
                [new VmfConnection("OnStartTouch", VolumeName, "Transition", "", 0f, -1)],
                [Brush.Box(hall.Mins, hall.Maxs, theme.Trigger)]));
        }

        all.Add(Point("info_poi", arrival, ("poi_type", "arrival"), ("angles", Angles(arrivalYaw))));
    }

    private static RoomEntity Point(string className, Point at, params (string Key, string Value)[] keys)
        => new(className, at, [.. keys.Select(k => new KeyValuePair<string, string>(k.Key, k.Value))], [], []);

    private static string Angles(int yaw) => string.Create(CultureInfo.InvariantCulture, $"0 {yaw} 0");
}

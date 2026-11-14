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

    /// <summary>The jump tiers above T1 whose features the room holds (<see cref="JumpFeatures"/>), which its name's flags spell.</summary>
    public IReadOnlySet<JumpTier> JumpTiers { get; init; } = new HashSet<JumpTier>();

    /// <summary>Water volumes (world brushes of a water material), room-local; not solid to a player.</summary>
    public IReadOnlyList<Brush> Water { get; init; } = [];

    /// <summary>Whether the room has a sky face, and so needs the library's sun (<see cref="RoomCatalogue.Library"/>).</summary>
    public bool HasSky { get; init; }

    /// <summary>The open floor plan of an outlined room (<see cref="RoomBuilder.WithOutline"/>), or empty for the centred plus.</summary>
    public IReadOnlyList<Rect> Outline { get; init; } = [];

    /// <summary>Where a visitor's eye goes in the room: the middle of its main space, on its floor.</summary>
    public Point Focus { get; init; }

    /// <summary>The room's main space (<see cref="RoomBuilder.WithBody"/>), the interior square by default.</summary>
    public Rect Body { get; init; }

    /// <summary>
    /// The cells the room covers along each side (map tools' <c>room_footprint</c>, M24): 1 for a room
    /// of one cell. A larger room is square, has a socket on every cell edge of its perimeter, and its
    /// <see cref="Doors"/> are the four faces.
    /// </summary>
    public int Cells { get; init; } = 1;

    /// <summary>The room's box along x and y: its cells times the kit's cell.</summary>
    public int Extent => Cells * Kit.Cell;

    /// <summary>
    /// The sockets of a larger room that declares only some of its cell edges
    /// (<see cref="RoomBuilder.WithSockets"/>: a boss arena joined only where its
    /// row meets it), or null for every edge of its door faces.
    /// </summary>
    public IReadOnlyList<(CellFace Face, int Offset)>? SocketEdges { get; init; }

    /// <summary>Every socket: a wall and the cell edge along it (0 for a room of one cell).</summary>
    public IEnumerable<(CellFace Face, int Offset)> Sockets =>
        SocketEdges ?? (Cells == 1 ? Doors.Select(f => (f, 0)) : Doors.SelectMany(f => Enumerable.Range(0, Cells).Select(k => (f, k))));

    /// <summary>A socket's plug box, room-local (<see cref="RoomKit.PlugBox(CellFace, int, int)"/>).</summary>
    public (Point Mins, Point Maxs) PlugBox(CellFace face, int offset) => Kit.PlugBox(face, offset, Cells);

    /// <summary>A socket's name as the tools spell it: the wall's, and in a larger room its cell edge's number after it.</summary>
    public string SocketName(CellFace face, int offset) =>
        Cells == 1 ? RoomBuilder.WallName(face) : RoomBuilder.WallName(face) + offset.ToString(CultureInfo.InvariantCulture);

    /// <summary>A point <paramref name="depth"/> inside the room from a socket's wall, at the socket's middle, on the floor.</summary>
    public Point DoorInside(CellFace face, int offset, float depth)
    {
        float c = Extent, m = Kit.Cell / 2f + offset * Kit.Cell, d = Kit.WallDepth + depth;
        return face switch
        {
            CellFace.East => new(c - d, m, Kit.Sill),
            CellFace.West => new(d, m, Kit.Sill),
            CellFace.North => new(m, c - d, Kit.Sill),
            _ => new(m, d, Kit.Sill),
        };
    }
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
    private int cells = 1;
    private string? mapLabel;
    private RoomEntry entry = RoomEntry.Default;
    private readonly HashSet<JumpTier> tiers = [];
    private readonly List<Rect> outline = [];
    private readonly List<Brush> water = [];
    private readonly List<Rect> sky = [];
    private Rect? body;
    private Point? focus;
    private (Point Mins, Point Maxs)? transitionBox;
    private (Point Mins, Point Maxs)? button;
    private (Point Mins, Point Maxs)? hallTrigger;

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

    /// <summary>
    /// Makes the room's open floor the union of <paramref name="rects"/>
    /// (room-local, inside the cell's walls) instead of the centred plus: the
    /// rest of the cell is solid wall, cut into boxes (<see cref="Rects.Subtract"/>).
    /// A room need not fill its cell (owner ruling 2026-11-10); only its doors
    /// are fixed, so each door's approach must be open floor. Calls add up.
    /// </summary>
    public RoomBuilder WithOutline(params Rect[] rects)
    {
        outline.AddRange(rects);
        width = kit.Interior;
        return this;
    }

    /// <summary>
    /// The room's main space, which the wall features (<see cref="JumpFeatures"/>)
    /// stand against: its walls are the rectangle's sides. The interior square
    /// by default.
    /// </summary>
    public RoomBuilder WithBody(Rect value)
    {
        body = value;
        return this;
    }

    /// <summary>Where a visitor's eye goes (<see cref="Room.Focus"/>); the body's middle by default.</summary>
    public RoomBuilder WithFocus(Point value)
    {
        focus = value;
        return this;
    }

    /// <summary>A water volume over <paramref name="area"/> from <paramref name="z0"/> to <paramref name="z1"/> (room-local), in <paramref name="material"/>.</summary>
    public RoomBuilder WithWater(Rect area, float z0, float z1, string material)
    {
        water.Add(Brush.Box(new(area.X0, area.Y0, z0), new(area.X1, area.Y1, z1), material));
        return this;
    }

    /// <summary>The sky material a sky ceiling is cut in (the tools' <c>%compileSky</c>).</summary>
    public const string SkyMaterial = "TOOLS/TOOLSSKYBOX";

    /// <summary>Opens the ceiling over <paramref name="area"/> to the sky: the slab there is <see cref="SkyMaterial"/>, and the library gets the sun.</summary>
    public RoomBuilder WithSkyCeiling(Rect area)
    {
        sky.Add(area);
        return this;
    }

    /// <summary>Stands a transition room's volume (64 square, 64 tall) at <paramref name="at"/> (its floor's middle) instead of the cell's centre.</summary>
    public RoomBuilder WithTransitionAt(Point at) =>
        WithTransitionBox(new Point(at.X - 32, at.Y - 32, at.Z), new Point(at.X + 32, at.Y + 32, at.Z + 64));

    /// <summary>Makes a transition room's volume the box from <paramref name="mins"/> to <paramref name="maxs"/> (a cage's floor, a hole's bottom).</summary>
    public RoomBuilder WithTransitionBox(Point mins, Point maxs)
    {
        transitionBox = (mins, maxs);
        return this;
    }

    /// <summary>A button (a box, room-local) that fires the transition, for an up room or a down one.</summary>
    public RoomBuilder WithButton(Point mins, Point maxs)
    {
        button = (mins, maxs);
        return this;
    }

    /// <summary>A trigger (a box, room-local) a player walks into to fire the transition, for an up room or a down one; a down room's default is the 128 cube round its volume.</summary>
    public RoomBuilder WithHallTrigger(Point mins, Point maxs)
    {
        hallTrigger = (mins, maxs);
        return this;
    }

    /// <summary>
    /// Makes the room <paramref name="n"/> by <paramref name="n"/> cells (M24, map tools'
    /// <c>room_footprint</c>): its box is <c>[0, n x cell]</c> along x and y, it has a door on every
    /// cell edge of its perimeter (sockets <c>east0</c> to <c>east{n-1}</c> and so on, each framed and
    /// capped as any door), and its open floor is the whole interior. No surface may lie on one of
    /// its internal cell faces (the tools refuse it); <see cref="Build"/> checks.
    /// </summary>
    public RoomBuilder WithFootprint(int n)
    {
        if (n is < 2 or > 4)
            throw new ArgumentOutOfRangeException(nameof(n), n, $"room {name}: a larger room is 2 to 4 cells a side");
        cells = n;
        doors.Clear();
        doors.AddRange([CellFace.East, CellFace.West, CellFace.North, CellFace.South]);
        return this;
    }

    /// <summary>
    /// Makes a larger room's sockets only <paramref name="sockets"/> (a wall and the cell edge along
    /// it), not one on every cell edge of its perimeter: the rest of the perimeter is plain wall with
    /// no brush the shape of a door's box (the tools count one as a socket). Its doors are then the
    /// walls with a socket. A boss arena opens only where its row joins it, which packs it in a
    /// fraction of the time and space (plan §3.2).
    /// </summary>
    public RoomBuilder WithSockets(params (CellFace Face, int Offset)[] sockets)
    {
        if (cells == 1)
            throw new InvalidOperationException($"room {name}: only a larger room (WithFootprint) chooses its sockets");
        if (sockets.Length == 0 || sockets.Distinct().Count() != sockets.Length || sockets.Any(s => s.Offset < 0 || s.Offset >= cells))
            throw new ArgumentException($"room {name}: sockets must be distinct cell edges 0 to {cells - 1}", nameof(sockets));
        socketEdges = [.. sockets.OrderBy(s => Array.IndexOf(FaceOrder, s.Face)).ThenBy(s => s.Offset)];
        doors.Clear();
        doors.AddRange(FaceOrder.Where(f => sockets.Any(s => s.Face == f)));
        return this;
    }

    private static readonly CellFace[] FaceOrder = [CellFace.East, CellFace.West, CellFace.North, CellFace.South];

    private List<(CellFace Face, int Offset)>? socketEdges;

    /// <summary>The cells a side (1 unless <see cref="WithFootprint"/>).</summary>
    public int Cells => cells;

    /// <summary>The room's box along x and y.</summary>
    public float Extent => cells * kit.Cell;

    /// <summary>Every socket: a wall and the cell edge along it.</summary>
    public IEnumerable<(CellFace Face, int Offset)> Sockets =>
        socketEdges ?? (cells == 1 ? doors.Select(f => (f, 0)) : doors.SelectMany(f => Enumerable.Range(0, cells).Select(k => (f, k))));

    /// <summary>The walls the room has doors on, in the order given.</summary>
    public IReadOnlyList<CellFace> Doors => doors;

    /// <summary>The room's main space as features see it (<see cref="WithBody"/>).</summary>
    public Rect Body => body ?? (cells > 1
        ? new Rect(kit.WallDepth, kit.WallDepth, Extent - kit.WallDepth, Extent - kit.WallDepth)
        : new Rect(InteriorLow, InteriorLow, InteriorHigh, InteriorHigh));

    /// <summary>The open floor plan so far (empty for the centred plus).</summary>
    public IReadOnlyList<Rect> Outline => outline;

    public RoomBuilder WithDoors(params CellFace[] faces)
    {
        if (cells > 1)
            throw new InvalidOperationException($"room {name}: a larger room has a door on every cell edge already");
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
    public (Point Mins, Point Maxs) ApproachBox(CellFace face, int offset = 0)
    {
        var box = Face(face, Extent - kit.WallDepth - DoorApproach, Extent, kit.DoorLow, kit.DoorHigh, kit.Sill, kit.Head, theme.Block, offset);
        return (box.Mins, box.Maxs);
    }

    /// <summary>A socket's plug box, room-local: the kit's opening on the cell edge <paramref name="offset"/> along the wall.</summary>
    public (Point Mins, Point Maxs) PlugBox(CellFace face, int offset = 0)
    {
        var box = Face(face, Extent - kit.WallDepth, Extent, kit.DoorLow, kit.DoorHigh, kit.Sill, kit.Head, theme.Plug, offset);
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

    /// <summary>Records that the room holds a jump spot of <paramref name="tier"/>; the features of <see cref="JumpFeatures"/> call it.</summary>
    public RoomBuilder Holds(JumpTier tier)
    {
        if (tier > JumpTier.T1)
            tiers.Add(tier);
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
        if (cells > 1)
            MultiShell(shell);
        else if (outline.Count > 0)
            OutlineShell(shell);
        else
            Shell(shell);
        if (cells == 1)
            Merge(shell);
        var world = shell.Select(b => b.ToBrush()).ToList();
        world.AddRange(SkyCeiling());
        foreach (Brush brush in features)
        {
            if (!Inside(brush.Mins, brush.Maxs))
                throw new ArgumentException($"room {name}: a brush ({brush.Mins})-({brush.Maxs}) leaves the room's cell");
            foreach (var (face, offset) in Sockets)
            {
                var (mins, maxs) = ApproachBox(face, offset);
                if (brush.Overlaps(mins, maxs))
                    throw new ArgumentException($"room {name}: a brush ({brush.Mins})-({brush.Maxs}) stands in front of its {SocketName(face, offset)} door");
            }

            world.Add(brush);
        }

        if (cells > 1)
            CheckCellFaces(world);
        var all = new List<RoomEntity>(entities);
        if (frames)
            foreach (var (face, offset) in Sockets)
            {
                all.AddRange(FramePieces(face, offset));
                all.Add(Cap(face, offset));
            }
        if (role != RoomRole.None)
            RolePieces(all);
        Rect main = Body;
        return new Room(name, kit, height, width, [.. doors], role, world, all)
        {
            MapLabel = mapLabel,
            Entry = entry,
            JumpTiers = new HashSet<JumpTier>(tiers),
            Water = [.. water],
            HasSky = sky.Count > 0,
            Outline = [.. outline],
            Focus = focus ?? new Point(main.CentreX, main.CentreY, kit.Sill),
            Body = main,
            Cells = cells,
            SocketEdges = socketEdges is null ? null : [.. socketEdges],
        };
    }

    private void Validate()
    {
        if (height < kit.LeastHeight)
            throw new ArgumentOutOfRangeException(nameof(height), height,
                $"room {name}: {height} is lower than the door kit allows ({kit.LeastHeight}: door_height + 2 x wall_depth)");
        if (height % kit.NavVoxel != 0)
            throw new ArgumentOutOfRangeException(nameof(height), height,
                $"room {name}: {height} is not a whole number of the kit's {kit.NavVoxel}-unit navigation voxels");
        if (cells > 1 && outline.Count > 0)
            throw new ArgumentException($"room {name}: a larger room's open floor is its whole interior");
        if (width < kit.DoorWidth || width > kit.Interior)
            throw new ArgumentOutOfRangeException(nameof(width), width,
                $"room {name}: width {width} is outside the door's {kit.DoorWidth} to the cell's {kit.Interior}");
        if (frames && (kit.ClearWidth < RoomKit.GiantWidth || kit.ClearHeight < RoomKit.GiantHeight))
            throw new ArgumentException(
                $"room {name}: the {kit.Name} kit's door frame ({kit.Frame.Model}) leaves {kit.ClearWidth} by {kit.ClearHeight}, less than a giant's {RoomKit.GiantWidth} by {RoomKit.GiantHeight}");
        if ((kit.Cell - width) % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, $"room {name}: width {width} does not centre on whole units");
        if (outline.Count > 0)
        {
            float t = kit.WallDepth, c = kit.Cell;
            foreach (Rect r in outline)
                if (r.IsEmpty || r.X0 < t || r.Y0 < t || r.X1 > c - t || r.Y1 > c - t)
                    throw new ArgumentException($"room {name}: an outline rectangle {r} is empty or reaches into the cell's walls ({t} to {c - t})");
            foreach (CellFace face in doors)
            {
                var (mins, maxs) = ApproachBox(face);
                var inside = new Rect(Math.Max(mins.X, t), Math.Max(mins.Y, t), Math.Min(maxs.X, c - t), Math.Min(maxs.Y, c - t));
                if (!Rects.Covers(outline, inside))
                    throw new ArgumentException($"room {name}: its {WallName(face)} door's approach ({mins})-({maxs}) is not all open floor of the outline");
            }
        }

        if (role != RoomRole.None && !Inside(arrival, arrival + new Point(0, 0, 72)))
            throw new ArgumentException($"room {name}: its arrival ({arrival}) is outside the room");
    }

    private bool Inside(Point mins, Point maxs)
        => mins.X >= 0 && mins.Y >= 0 && mins.Z >= 0 && maxs.X <= Extent && maxs.Y <= Extent && maxs.Z <= height;

    /// <summary>The socket's name as the tools spell it: the wall's, and in a larger room the cell edge's number after it.</summary>
    public string SocketName(CellFace face, int offset) => cells == 1 ? WallName(face) : WallName(face) + offset.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The map tools refuse a larger room with a visible surface lying on one of its internal cell
    /// faces (the link routes each cell to its own part of the room and would drop it): no face of a
    /// brush may lie wholly in the plane x or y = k x cell, 0 &lt; k &lt; its cells.
    /// </summary>
    private void CheckCellFaces(IEnumerable<Brush> world)
    {
        float c = kit.Cell;
        foreach (Brush b in world)
            foreach (BrushFace f in b.Faces)
                for (int k = 1; k < cells; k++)
                {
                    float plane = k * c;
                    if (f.Polygon.All(v => Math.Abs(v.X - plane) < 0.01f) || f.Polygon.All(v => Math.Abs(v.Y - plane) < 0.01f))
                        throw new ArgumentException($"room {name}: a face of the brush ({b.Mins})-({b.Maxs}) lies on the internal cell face at {plane}; move it off, or let the brush cross it");
                }
    }

    /// <summary>
    /// A larger room's shell: the floor slab over the footprint, let down under each socket; the
    /// ceiling slab; each wall along its whole side with the kit's opening on every cell edge, a
    /// lintel over each and its plug. The east and west walls run the whole side, the north and south
    /// between them, so no two meet face to face, and no piece ends on an internal cell face.
    /// </summary>
    private void MultiShell(List<RoomBox> world)
    {
        float e = Extent, t = kit.WallDepth, h = height, z0 = t, z1 = h - t, c = kit.Cell;
        Floors(world);
        Ceilings(world);
        var sockets = Sockets.ToHashSet();
        foreach (CellFace face in FaceOrder)
        {
            bool full = face is CellFace.East or CellFace.West;
            float start = full ? 0 : t, end = full ? e : e - t, a = start;
            for (int k = 0; k < cells; k++)
            {
                // A cell edge with no socket is wall: the side runs on past it.
                if (!sockets.Contains((face, k)))
                    continue;
                float lo = kit.DoorLow + k * c, hi = kit.DoorHigh + k * c;
                world.Add(Face(face, e - t, e, a, lo, z0, z1, theme.Wall));
                if (kit.Head < z1)
                    world.Add(Face(face, e - t, e, lo, hi, kit.Head, z1, theme.Wall));
                var plug = PlugBox(face, k);
                world.Add(new RoomBox(plug.Mins, plug.Maxs, theme.Plug));
                a = hi;
            }

            world.Add(Face(face, e - t, e, a, end, z0, z1, theme.Wall));
        }

        Band(world, z0, z1);
    }

    private void Shell(List<RoomBox> world)
    {
        float c = kit.Cell, t = kit.WallDepth, h = height;
        float a = (c - width) / 2f, b = a + width;
        float z0 = t, z1 = h - t;

        Floors(world);
        Ceilings(world);

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

        Band(world, z0, z1);
    }

    /// <summary>
    /// The theme's lower band (<see cref="RoomTheme.WallLow"/>): every wall box
    /// standing on the floor is cut at <see cref="RoomTheme.WallBand"/> above
    /// it, the part below in the band's surface, in a room whose wall is tall
    /// enough to show both (twice the band).
    /// </summary>
    private void Band(List<RoomBox> world, float z0, float z1)
    {
        Seam(world, z0, z1);
        if (theme.WallLow is not { } low || z1 - z0 < 2 * theme.WallBand)
            return;
        float cut = z0 + theme.WallBand;
        for (int k = world.Count - 1; k >= 0; k--)
        {
            RoomBox w = world[k];
            if (w.Surface != theme.Wall || w.Mins.Z != z0 || w.Maxs.Z <= cut)
                continue;
            world[k] = w with { Mins = w.Mins with { Z = cut } };
            world.Add(new RoomBox(w.Mins, w.Maxs with { Z = cut }, low));
        }
    }

    /// <summary>
    /// The theme's seam (<see cref="RoomTheme.WallSeam"/>): every wall box
    /// standing on the floor and reaching a voxel over the seam's top is cut
    /// at the seam's two heights, the part between in the seam's surface (a
    /// grey ore band through level 3's red rock).
    /// </summary>
    private void Seam(List<RoomBox> world, float z0, float z1)
    {
        if (theme.WallSeam is not { } seam || z1 - z0 < seam.To + kit.NavVoxel)
            return;
        float a = z0 + seam.From, b = z0 + seam.To;
        for (int k = world.Count - 1; k >= 0; k--)
        {
            RoomBox w = world[k];
            if (w.Surface != theme.Wall || w.Mins.Z != z0 || w.Maxs.Z < b + kit.NavVoxel)
                continue;
            world[k] = w with { Maxs = w.Maxs with { Z = a } };
            world.Add(new RoomBox(w.Mins with { Z = a }, w.Maxs with { Z = b }, seam.Surface));
            world.Add(new RoomBox(w.Mins with { Z = b }, w.Maxs, theme.Wall));
        }
    }

    /// <summary>
    /// The shell of an outlined room: the floor and ceiling slabs over the
    /// cell, every part of the cell between them that is neither open floor
    /// nor a doorway made wall (as few boxes as <see cref="Rects.Subtract"/>
    /// cuts), and each door's lintel and plug.
    /// </summary>
    private void OutlineShell(List<RoomBox> world)
    {
        float c = kit.Cell, t = kit.WallDepth, h = height;
        float z0 = t, z1 = h - t;
        Floors(world);
        Ceilings(world);
        var doorways = doors.Select(f => kit.PlugBox(f)).Select(p => new Rect(p.Mins.X, p.Mins.Y, p.Maxs.X, p.Maxs.Y)).ToArray();
        foreach (Rect r in Rects.Subtract(new Rect(0, 0, c, c), outline.Concat(doorways)))
            world.Add(new RoomBox(new(r.X0, r.Y0, z0), new(r.X1, r.Y1, z1), theme.Wall));
        foreach (CellFace face in doors)
        {
            var plug = kit.PlugBox(face);
            if (kit.Head < z1)
                world.Add(new RoomBox(plug.Mins with { Z = kit.Head }, plug.Maxs with { Z = z1 }, theme.Wall));
            world.Add(new RoomBox(plug.Mins, plug.Maxs, theme.Plug));
        }

        Band(world, z0, z1);
    }

    /// <summary>
    /// The sky ceiling's pieces: the ceiling slab is cut out over each sky
    /// area (<see cref="Build"/> drops the slab pieces there) and filled with
    /// sky boxes of the slab's depth.
    /// </summary>
    private IEnumerable<Brush> SkyCeiling()
    {
        float h = height, t = kit.WallDepth;
        return sky.Select(r => Brush.Box(new(r.X0, r.Y0, h - t), new(r.X1, r.Y1, h), SkyMaterial));
    }

    /// <summary>
    /// How far the floor under each door's plug is let down (task A15, wall
    /// 16): the room compile culls a floor face the plug stands on, so a
    /// joined doorway's floor drew as a strip of the void; under a dip the
    /// floor's face looks into a slit of the room's own air, is compiled, and
    /// gives the sill planks an air leaf to stand in. The dip stops
    /// <see cref="DoorwayDipShort"/> short of the cell face, which seals it.
    /// </summary>
    public const int DoorwayDip = 2;

    /// <summary>How far short of the cell face a doorway's dip stops, so the slit under the plug never opens on the cell's edge.</summary>
    public const int DoorwayDipShort = 1;

    /// <summary>A door's dip under its plug, room-local, on the floor plan.</summary>
    public Rect DipOf(CellFace face, int offset = 0)
    {
        float c = Extent, t = kit.WallDepth, s = DoorwayDipShort, lo = kit.DoorLow + offset * kit.Cell, hi = kit.DoorHigh + offset * kit.Cell;
        return face switch
        {
            CellFace.East => new(c - t, lo, c - s, hi),
            CellFace.West => new(s, lo, t, hi),
            CellFace.North => new(lo, c - t, hi, c - s),
            _ => new(lo, s, hi, t),
        };
    }

    /// <summary>The ceiling slab over the cell, less its sky areas.</summary>
    private void Ceilings(List<RoomBox> world)
    {
        float c = Extent, t = kit.WallDepth, h = height;
        foreach (Rect r in Rects.Subtract(new Rect(0, 0, c, c), sky))
            world.Add(new RoomBox(new(r.X0, r.Y0, h - t), new(r.X1, r.Y1, h), theme.Ceiling));
    }

    /// <summary>The floor slab: the whole cell, its top let down under each door (<see cref="DoorwayDip"/>).</summary>
    private void Floors(List<RoomBox> world)
    {
        float c = Extent, t = kit.WallDepth, d = t - DoorwayDip;
        if (doors.Count == 0)
        {
            world.Add(new RoomBox(new(0, 0, 0), new(c, c, t), theme.Floor));
            return;
        }

        world.Add(new RoomBox(new(0, 0, 0), new(c, c, d), theme.Floor));
        foreach (Rect r in Rects.Subtract(new Rect(0, 0, c, c), Sockets.Select(s => DipOf(s.Face, s.Offset))))
            world.Add(new RoomBox(new(r.X0, r.Y0, d), new(r.X1, r.Y1, t), theme.Floor));
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
    private RoomBox Face(CellFace face, float out0, float out1, float across0, float across1, float z0, float z1, Surface material, int offset = 0)
    {
        float c = Extent, shift = offset * kit.Cell;
        across0 += shift;
        across1 += shift;
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
    /// The frame of the door on <paramref name="face"/> (<see cref="FrameModel"/>)
    /// and its sill plank: <c>prop_static</c>s kept by the link only where the
    /// socket is joined (<c>room_needs joined_&lt;wall&gt;</c>), so each room of
    /// a joint lines its own half of the doorway, at no entity's cost. Each
    /// stands against the cell face, no deeper into the wall than the cell
    /// face, and proud into the room by what the frame is deeper than the wall
    /// (8 on the normal kit: two rooms' frames make one 32 deep across the
    /// doorway), and at least by <see cref="PropProud"/>: vbsp drops a static
    /// prop whose box touches no leaf of the room, and a box that only meets
    /// the room's air at the wall's face can round onto it. Non-solid (the
    /// brushes make the passage); casting no shadow (a prop the link may drop
    /// must not).
    /// </summary>
    /// <remarks>
    /// Not socket furniture (<c>room_socket</c>): the link keeps one room's
    /// furniture at a joint, and a piece reaching through the doorway from one
    /// room would have its origin outside that room's cell once it is deeper
    /// than the frame's asymmetric half, where the library has no room for it.
    /// </remarks>
    private IEnumerable<RoomEntity> FramePieces(CellFace face, int offset = 0)
    {
        FrameModel f = kit.Frame;
        if (theme.FrameBrushes is { } brushes)
            yield return BrushFramePiece(brushes, face, offset);
        else
            yield return ConditionalProp(face, f.Model, Math.Max(f.Depth, kit.WallDepth) + PropProud, f.MinX, kit.Sill - f.Bottom, offset);
        if (f.Sill is { } sill)
            foreach (float inset in SillInsets(kit))
                yield return ConditionalProp(face, sill.Model, inset, sill.MinX, kit.Sill - DoorwayDip - sill.Floor, offset);
    }

    /// <summary>
    /// A theme's brush frame (<see cref="BrushFrame"/>) for the socket on <paramref name="face"/>: one
    /// non-solid <c>func_brush</c> kept only where the socket is joined, its posts and head lining the
    /// room's half of the doorway and standing proud into the room, its flange or cap on the wall round
    /// the opening, its planks across the threshold.
    /// </summary>
    private RoomEntity BrushFramePiece(BrushFrame fr, CellFace face, int offset)
    {
        float e = Extent, wall = e - kit.WallDepth, inner = wall - fr.Proud, lo = kit.DoorLow, hi = kit.DoorHigh, sill = kit.Sill, head = kit.Head;
        var boxes = new List<RoomBox>
        {
            Face(face, inner, e, lo, lo + fr.PostWidth, sill, head, fr.Post, offset),
            Face(face, inner, e, hi - fr.PostWidth, hi, sill, head, fr.Post, offset),
            Face(face, inner, e, lo + fr.PostWidth, hi - fr.PostWidth, head - fr.HeadDepth, head, fr.Head, offset),
        };
        if (fr.Flange > 0)
        {
            float out0 = wall - fr.FlangeDepth, w = fr.Flange;
            boxes.Add(Face(face, out0, wall, lo - w, lo, sill, head + w, fr.Post, offset));
            boxes.Add(Face(face, out0, wall, hi, hi + w, sill, head + w, fr.Post, offset));
            boxes.Add(Face(face, out0, wall, lo, hi, head, head + w, fr.HeadFace ?? fr.Head, offset));
        }

        if (fr.CapOverhang > 0)
            boxes.Add(Face(face, inner, wall, lo - fr.CapOverhang, hi + fr.CapOverhang, head, head + fr.HeadDepth, fr.HeadFace ?? fr.Head, offset));
        if (fr.Crossing is { } planks)
            // Three boards across the threshold, a hand apart, a unit and a half high: the floor of a level crossing.
            for (int k = 0; k < 3; k++)
            {
                float b1 = wall - k * (fr.CrossingDepth / 3f), b0 = b1 - fr.CrossingDepth / 3f + 6;
                boxes.Add(Face(face, b0, b1, lo + fr.PostWidth, hi - fr.PostWidth, sill, sill + 1.5f, planks, offset));
            }

        return new RoomEntity("func_brush", null,
            [new("room_needs", "joined_" + SocketName(face, offset)), new("Solidity", "1"), new("vrad_brush_cast_shadows", "0"),
             new("disableshadows", "1"), new("_minlight", BrushFrame.MinLight)],
            [], [.. boxes.Select(b => b.ToBrush())]);
    }

    /// <summary>
    /// How deep each sill plank of a door stands in from the cell face (its
    /// back's inset), so the planks tile the room's half of the doorway from
    /// the face to the wall's face and the last stands
    /// <see cref="PropProud"/> into the room: one plank when it is as deep
    /// as the wall, else as many as cover it, evenly overlapped.
    /// </summary>
    public static IReadOnlyList<float> SillInsets(RoomKit kit)
    {
        if (kit.Frame.Sill is not { } sill)
            return [];
        float last = Math.Max(sill.Depth, kit.WallDepth) + PropProud;
        int n = (int)Math.Ceiling(kit.WallDepth / sill.Depth);
        if (n <= 1)
            return [last];
        return [.. Enumerable.Range(0, n).Select(k => sill.Depth + (last - sill.Depth) * k / (n - 1))];
    }

    /// <summary>How far at least a door's frame and sill stand proud into the room past the wall's face.</summary>
    public const float PropProud = 1 / 64f;

    /// <summary>
    /// A prop of <paramref name="model"/> facing out through the door on
    /// <paramref name="face"/>, its mesh's back (<paramref name="minX"/>)
    /// <paramref name="inset"/> in from the cell face, its origin
    /// <paramref name="z"/> up, kept only where the socket is joined.
    /// </summary>
    private RoomEntity ConditionalProp(CellFace face, string model, float inset, float minX, float z, int offset = 0)
    {
        float c = Extent, m = kit.Cell / 2f + offset * kit.Cell, along = -inset - minX;
        (Point at, int yaw) = face switch
        {
            CellFace.East => (new Point(c + along, m, z), 0),
            CellFace.North => (new Point(m, c + along, z), 90),
            CellFace.West => (new Point(-along, m, z), 180),
            _ => (new Point(m, -along, z), 270),
        };
        return new RoomEntity("prop_static", at,
            [new("model", model), new("angles", Angles(yaw)), new("solid", "0"), new("disableshadows", "1"),
             new("room_needs", "joined_" + SocketName(face, offset))],
            [], []);
    }

    /// <summary>
    /// The thickness of the cap panel a door gets for when its socket is
    /// capped. The plug stays, and a plug face cannot be lit:
    /// <c>%compileTrigger</c> gives it <c>SURF_NOLIGHT</c> (vbsp's FindMiptex,
    /// the tools' MaterialSurface), so it has no lightmap, which the engine
    /// draws with its white lightmap page (fullbright) and the preview hides or
    /// draws black. The link needs the flag: it strips a joined plug's faces by
    /// it. The panel stands this far in front of the plug, in the wall's
    /// surface (world-aligned, so it reads as the wall), kept by the link only
    /// where the socket is capped (<c>room_needs !joined_&lt;wall&gt;</c>).
    /// </summary>
    public const int CapThickness = 2;

    /// <summary>
    /// The cap of the door on <paramref name="face"/>: a panel of the wall's
    /// surface over the opening, <see cref="CapThickness"/> in front of the
    /// plug, kept by the link only where the socket is capped.
    /// </summary>
    private RoomEntity Cap(CellFace face, int offset = 0)
    {
        float c = Extent, t = kit.WallDepth;
        Brush panel = Face(face, c - t - CapThickness, c - t, kit.DoorLow, kit.DoorHigh, kit.Sill, kit.Head, theme.Wall, offset).ToBrush();
        return new RoomEntity("func_brush", null,
            [new("room_needs", "!joined_" + SocketName(face, offset)), new("Solidity", "2"), new("vrad_brush_cast_shadows", "0")],
            [], [panel]);
    }

    private void RolePieces(List<RoomEntity> all)
    {
        var volume = transitionBox ?? TransitionVolume(kit);
        all.Add(new RoomEntity("trigger_room_transition", null, [new("targetname", VolumeName)], [],
            [Brush.Box(volume.Mins, volume.Maxs, theme.Trigger)]));

        if (hallTrigger is { } walked)
            all.Add(new RoomEntity("trigger_once", null, [new("spawnflags", "1")],
                [new VmfConnection("OnStartTouch", VolumeName, "Transition", "", 0f, -1)],
                [Brush.Box(walked.Mins, walked.Maxs, theme.Trigger)]));
        else if (button is { } pressed)
            all.Add(new RoomEntity("func_button", null, [new("spawnflags", "1025"), new("wait", "-1")],
                [new VmfConnection("OnPressed", VolumeName, "Transition", "", 0f, -1)],
                [Brush.Box(pressed.Mins, pressed.Maxs, theme.Block)]));
        else if (role == RoomRole.Up)
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
            (Point Mins, Point Maxs) hall = transitionBox is { } box
                ? (box.Mins with { X = box.Mins.X - 32, Y = box.Mins.Y - 32 }, box.Maxs with { X = box.Maxs.X + 32, Y = box.Maxs.Y + 32, Z = box.Mins.Z + 128 })
                : HallwayTrigger(kit);
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

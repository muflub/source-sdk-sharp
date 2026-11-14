namespace SourceSharp.MapGen.Descent;

/// <summary>
/// A cell face, room-local, as map-tools' room pipeline names its sockets:
/// east is +x, north is +y.
/// </summary>
public enum CellFace
{
    East,
    West,
    North,
    South,
}

/// <summary>One axis-aligned box brush, in whatever frame its owner says.</summary>
public readonly record struct RoomBox(Point Mins, Point Maxs, Surface Surface)
{
    public string Material => Surface.Material;

    public RoomBox Offset(Point by) => this with { Mins = Mins + by, Maxs = Maxs + by };

    public Brush ToBrush() => Brush.Box(Mins, Maxs, Surface);
}

/// <summary>
/// A grid and door kit for Descent rooms (plan_descent_mine_levels.md §3): the
/// cell every room stands in, the door opening every socket is cut to, and the
/// depth of the shell and of a door plug. Every library placed in one level
/// shares one kit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Doors stand on the floor.</b> The opening is centred along its wall and
/// runs up from the floor's top (z = <see cref="WallDepth"/>) for
/// <see cref="DoorHeight"/>, so a 192-high door is a doorway, not a hatch,
/// whatever the cell. Rooms of any height (<c>room_height</c>) share the same
/// opening, so any two rooms' doors meet.
/// </para>
/// <para>
/// map-tools at 9d600ed centres the opening on the cell face vertically as
/// well (<c>SocketKit.OpeningUnit</c>) and refuses a kit whose centred sill is
/// not the floor (<c>PlayerHull.DoorProblem</c>): it accepts only
/// <see cref="DoorHeight"/> = cell − 2 × wall, which for the 1024 cell is a
/// 960-high door, and then no room may be lower than its cell. The plan's kit
/// needs that rule changed in map-tools; <see cref="ToolsAccept"/> says which
/// kits the pinned tools take, and <see cref="WithFullHeightDoors"/> is the
/// nearest one they do.
/// </para>
/// <para>
/// <b>The navigation voxel</b> is the kit's too: map-tools builds a room's
/// navigation in at most 128 voxels along a cell, so the 16-unit default
/// stops at a 2048 cell, and the 3072 and 4096 arenas take a 32-unit voxel
/// (every door, wall and height of theirs is a multiple of it).
/// </para>
/// </remarks>
public sealed record RoomKit(string Name, int Cell, int DoorWidth, int DoorHeight, int WallDepth, int NavVoxel = RoomKit.DefaultNavVoxel)
{
    /// <summary>A giant robot at scale 1.9 (R16): the narrowest a doorway may be, frame and all.</summary>
    public const int GiantWidth = 92;

    /// <summary>A giant robot's height: the lowest a doorway may be.</summary>
    public const int GiantHeight = 156;

    /// <summary>The frame every socket of this kit is lined with (task A15).</summary>
    public DoorFrame Frame { get; init; } = DoorFrame.Timber;

    /// <summary>The walkable width left through a framed doorway.</summary>
    public int ClearWidth => DoorWidth - 2 * Frame.Post;

    /// <summary>The walkable height left through a framed doorway, from the sill's top to the head's underside.</summary>
    public int ClearHeight => DoorHeight - Frame.Head - Frame.Sill;

    /// <summary>map-tools' default <c>nav_voxel_size</c>.</summary>
    public const int DefaultNavVoxel = 16;

    /// <summary>The most voxels map-tools' navigation takes along one cell.</summary>
    public const int MaxNavVoxelsPerCell = 128;

    /// <summary>Normal levels (§3.1, E1): 1024 cell, a 256 by 192 door, a 32 wall.</summary>
    public static RoomKit Normal { get; } = new("normal", 1024, 256, 192, 32);

    /// <summary>Level 5's arena kit (§3.2).</summary>
    public static RoomKit Boss2048 { get; } = new("boss2048", 2048, 256, 192, 32);

    /// <summary>Level 15's arena kit (§3.2).</summary>
    public static RoomKit Boss3072 { get; } = new("boss3072", 3072, 256, 192, 32, NavVoxel: 32);

    /// <summary>Level 10's arena kit, for the Tank's track (§3.2, M14).</summary>
    public static RoomKit Boss4096 { get; } = new("boss4096", 4096, 256, 192, 32, NavVoxel: 32);

    /// <summary>The door opening's low edge along its wall, cell-local.</summary>
    public int DoorLow => (Cell - DoorWidth) / 2;

    /// <summary>The door opening's high edge along its wall, cell-local.</summary>
    public int DoorHigh => DoorLow + DoorWidth;

    /// <summary>The opening's sill: the floor's top.</summary>
    public int Sill => WallDepth;

    /// <summary>The opening's top: the underside of the lintel.</summary>
    public int Head => WallDepth + DoorHeight;

    /// <summary>The lowest room the door fits in: the door between a floor and a ceiling slab.</summary>
    public int LeastHeight => DoorHeight + 2 * WallDepth;

    /// <summary>The cell less a wall each side: the widest interior, and a cube room's interior height.</summary>
    public int Interior => Cell - 2 * WallDepth;

    /// <summary>
    /// Whether map-tools at 9d600ed accepts this kit: its door, centred on the
    /// face, must have its sill on the floor, so the door is the cell's whole
    /// interior height.
    /// </summary>
    public bool ToolsAccept => DoorHeight == Interior;

    /// <summary>This kit with the door the full interior height, the one the pinned tools accept.</summary>
    public RoomKit WithFullHeightDoors() => this with { Name = Name + "_fullheight", DoorHeight = Interior };

    /// <summary>
    /// A socket's plug box, room-local: the opening, <see cref="WallDepth"/>
    /// deep inward from the cell face, so two jointed rooms' plugs meet face to
    /// face on the shared cell face.
    /// </summary>
    public (Point Mins, Point Maxs) PlugBox(CellFace face)
    {
        float c = Cell, t = WallDepth, lo = DoorLow, hi = DoorHigh, z0 = Sill, z1 = Head;
        return face switch
        {
            CellFace.East => (new(c - t, lo, z0), new(c, hi, z1)),
            CellFace.West => (new(0, lo, z0), new(t, hi, z1)),
            CellFace.North => (new(lo, c - t, z0), new(hi, c, z1)),
            CellFace.South => (new(lo, 0, z0), new(hi, t, z1)),
            _ => throw new ArgumentOutOfRangeException(nameof(face), face, "a cell has four faces"),
        };
    }
}

/// <summary>
/// A door frame (task A15): the timber that lines a socket's opening, so a
/// joined doorway has sides, a sill and a head. The link removes a joined
/// socket's plug, and the faces that touched it were never compiled, so
/// without a frame a doorway draws as a gap at its edges.
/// </summary>
/// <remarks>
/// <para>
/// Each room lines its own half of the doorway, the wall's depth in from the
/// cell face, and stands <see cref="Reveal"/> proud of its wall into the
/// room so it reads as a frame. The frame is a <c>func_brush</c> with
/// <c>room_needs joined_&lt;wall&gt;</c>: the link keeps it only where the
/// socket is joined, so a capped socket stays a plain wall, and both rooms of
/// a joint keep theirs. Socket furniture (<c>room_socket</c>) cannot do this:
/// the link keeps only one side's furniture at a joint, and a brush entity
/// may not leave its cell, so one room's frame could line only its half.
/// A <c>prop_static</c> may reach into the neighbour's doorway, but no TF2
/// model is a closed frame the doorway's depth deep. World or
/// <c>func_detail</c> brushes cannot: they compile into the room's world,
/// where the plug is solid and their faces in it are culled like the walls'.
/// </para>
/// <para>
/// Cost: one entity and one brush model per framed socket of each placed
/// room, so two per joint: a 6 by 6 level of 60 joints spends 120 of its
/// 1,536 entities on frames.
/// </para>
/// </remarks>
/// <param name="Post">The side posts' thickness across the opening.</param>
/// <param name="Head">The head beam's depth below the lintel.</param>
/// <param name="Sill">The sill plate's height on the floor.</param>
/// <param name="Reveal">How far the frame stands proud of the wall into the room.</param>
/// <param name="MinLight">
/// The frame's <c>_minlight</c>. The faces inside the opening are inside the
/// plug when the room is compiled, so the bake leaves them black; this is the
/// floor vrad lifts them to (as a fraction of full light; map-tools' vrad
/// reads the key as stock's does).
/// </param>
/// <param name="Cap">
/// The thickness of the cap panel a door gets for when its socket is capped:
/// the plug stays and is a tool texture, so a capped doorway would show the
/// sky. The panel stands this far in front of the plug, in the wall's
/// surface (world-aligned, so it reads as the wall), kept by the link only
/// where the socket is capped (<c>room_needs !joined_&lt;wall&gt;</c>).
/// </param>
public sealed record DoorFrame(int Post, int Head, int Sill, int Reveal, float MinLight, int Cap = 2)
{
    /// <summary>16 posts and head, a 4 sill, 8 proud: a 256 by 192 door leaves 224 by 172.</summary>
    public static DoorFrame Timber { get; } = new(Post: 16, Head: 16, Sill: 4, Reveal: 8, MinLight: 0.25f);
}

/// <summary>
/// The surfaces a room is drawn in: each a material and its texture scale. A
/// depth's theme (plan §6, task A9) will be one of these. Every surface tiles
/// a whole number of times along a 1024 cell at its scale, so the walls of
/// two rooms side by side line up (<see cref="Brush.ToVmf"/> starts every
/// room's textures at its cell's corner).
/// </summary>
public sealed record RoomTheme(Surface Floor, Surface Wall, Surface Ceiling, Surface Block, Surface Plug, Surface Trigger)
{
    /// <summary>The door frames' timber (<see cref="DoorFrame"/>).</summary>
    public Surface Frame { get; init; } = MineShaft.Beam;

    /// <summary>
    /// The town's TF2 mining set, every entry already proven present in TF2's
    /// VPKs by the town's palette fact. The rock is <c>nature/rockwall012</c>,
    /// blocky rock at the town's 0.25 (a 1024 texture every 256 units);
    /// <c>rockwall011</c>, Phase 0's, is a banded sandstone that reads as
    /// stripes on a wall of any height.
    /// </summary>
    public static RoomTheme Mine { get; } = new(
        Floor: MineShaft.Floor,
        Wall: "nature/rockwall012",
        Ceiling: "nature/rockwall012",
        Block: MineShaft.Beam,
        // The pipeline finds a plug by its trigger surfaces; tools/toolstrigger
        // is what the AI test libraries already link with (AiTestMaps).
        Plug: "TOOLS/TOOLSTRIGGER",
        Trigger: "TOOLS/TOOLSTRIGGER");

    /// <summary>
    /// Phase 1's grey rooms: TF2's measuring textures at scale 1, so a grid
    /// line is every 64 units (<c>dev/graygrid</c>, 512 texels) and a floor
    /// square 64 (<c>dev/dev_measuregeneric01b</c>, 128 texels in four), and
    /// a height can be read off a wall. Blocks, decks and pillars are the
    /// orange <c>dev/dev_measuregeneric01</c>, so features stand out.
    /// </summary>
    public static RoomTheme Grey { get; } = new(
        Floor: new("dev/dev_measuregeneric01b", 1f),
        Wall: new("dev/graygrid", 1f),
        Ceiling: new("dev/graygrid", 1f),
        Block: new("dev/dev_measuregeneric01", 1f),
        Plug: "TOOLS/TOOLSTRIGGER",
        Trigger: "TOOLS/TOOLSTRIGGER")
    {
        Frame = new("dev/dev_measuregeneric01", 1f),
    };
}

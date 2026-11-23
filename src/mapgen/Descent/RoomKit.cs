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
/// The tools stand a door on the floor at any height only for a kit that says
/// so: every <c>info_room</c> writes <see cref="SillKey"/> (map-tools'
/// <c>door_sill floor</c>, merged at 7cdfe4d), without which the tools centre
/// the opening up the cell face and refuse every door lower than the cell's
/// interior.
/// </para>
/// <para>
/// <b>The navigation voxel</b> is the kit's too: map-tools builds a room's
/// navigation in at most 128 voxels along a cell, so the 16-unit default
/// stops at a 2048 cell; a larger cell would take a 32-unit voxel (every
/// door, wall and height a multiple of it). Every depth builds on the normal
/// kit since the boss arenas became rooms of several cells (plan §3.2).
/// </para>
/// </remarks>
public sealed record RoomKit(string Name, int Cell, int DoorWidth, int DoorHeight, int WallDepth, int NavVoxel = RoomKit.DefaultNavVoxel)
{
    /// <summary>A giant robot at scale 1.9 (R16): the narrowest a doorway may be, frame and all.</summary>
    public const int GiantWidth = 92;

    /// <summary>A giant robot's height: the lowest a doorway may be.</summary>
    public const int GiantHeight = 156;

    /// <summary>The frame model every socket of this kit is lined with (task A15), whose size the door is cut to.</summary>
    public FrameModel Frame { get; init; } = FrameModel.FarmDoorframe;

    /// <summary>The width a player sees between the frame's posts, inside the opening.</summary>
    public float ClearWidth => Math.Min(DoorWidth, Frame.InnerWidth);

    /// <summary>The height a player sees under the frame's head, inside the opening.</summary>
    public float ClearHeight => Math.Min(DoorHeight, Frame.InnerHeight);

    /// <summary>map-tools' default <c>nav_voxel_size</c>.</summary>
    public const int DefaultNavVoxel = 16;

    /// <summary>The most voxels map-tools' navigation takes along one cell.</summary>
    public const int MaxNavVoxelsPerCell = 128;

    /// <summary>
    /// The kit a frame model makes on a cell (task A15, the user's request of
    /// 2026-11-10): the doorway is two walls deep and no deeper than the frame,
    /// so the wall is half the frame's depth; the opening is the frame's outer
    /// width (socket furniture may reach out of its cell only into the doorway,
    /// and the posts then cover the doorway's sides) and the frame's height
    /// rounded down to the navigation voxel, which the head still covers.
    /// </summary>
    /// <remarks>
    /// The floor's top is the wall's depth (the door's sill is the floor), and
    /// the tools' navigation stands a marker in the voxel its height falls in,
    /// so the wall is a whole number of voxels: at 16 or 32 a floor 8 up puts
    /// every marker in a voxel the floor blocks (ssmap room refuses each one).
    /// The wall is therefore one voxel (16 on the normal kit and level 5's,
    /// whose frame then lines exactly its half), and at
    /// 32 its frames line only the room side of each half
    /// (<see cref="FrameLining"/>: a prop wholly inside the plug is in no leaf
    /// of the room's compile, which drops it).
    /// </remarks>
    /// <exception cref="ArgumentException">The frame cannot make a kit: an odd depth, or no voxel height its head covers.</exception>
    public static RoomKit For(string name, int cell, FrameModel frame, int navVoxel = DefaultNavVoxel)
    {
        if (frame.Depth % 2 != 0)
            throw new ArgumentException($"frame {frame.Model} is {frame.Depth} deep; a doorway is two whole walls deep");
        int height = (int)(frame.Height / navVoxel) * navVoxel;
        if (height < frame.InnerHeight)
            throw new ArgumentException($"frame {frame.Model}: no multiple of {navVoxel} between its opening's {frame.InnerHeight} and its top's {frame.Height}");
        int wall = (frame.Depth / 2 + navVoxel - 1) / navVoxel * navVoxel;
        return new RoomKit(name, cell, frame.Width, height, wall, navVoxel) { Frame = frame };
    }

    /// <summary>
    /// How deep a frame lines its room's half of a doorway, from the cell face
    /// (<see cref="RoomBuilder"/> stands it against the face, proud into the
    /// room when the wall is thinner than the frame): the whole half (the
    /// wall's depth) unless the wall is deeper than the frame.
    /// </summary>
    public int FrameLining => Math.Min(WallDepth, Frame.Depth);

    /// <summary>
    /// Normal levels (§3.1, E1): a 1024 cell, the door of the grey frame, a
    /// 16-unit voxel and so a wall of 16, which the 16-deep frame lines
    /// exactly (each room's frame fills its half of the doorway). An 8-unit
    /// voxel (wall 8) packed a 19-room library into 100 MB in 26 s; this kit
    /// packs the same rooms smaller and faster (plan §1.3).
    /// </summary>
    public static RoomKit Normal { get; } = For("normal", NormalCell, FrameModel.FarmDoorframe);

    /// <summary>The normal kit's cell (E1).</summary>
    public const int NormalCell = 1024;

    /// <summary>
    /// Level 5's cell (brief p18, ruling M36): its maze is a 10 by 10 block of 512 cells round a 2 by 2
    /// arena, so the level's every room is on this kit, the door the normal kit's (208 by 192, framed);
    /// a room of the normal size covers 2 by 2 of these cells.
    /// </summary>
    public const int MazeCell = 512;

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
    /// The <c>info_room</c> key and value that stand the kit's doors on the
    /// floor (map-tools' <c>door_sill floor</c>): the sill at
    /// <see cref="WallDepth"/>, the head at <see cref="Head"/>, at any cell.
    /// </summary>
    public static (string Key, string Value) SillKey => ("door_sill", "floor");

    /// <summary>
    /// A socket's plug box, room-local: the opening, <see cref="WallDepth"/>
    /// deep inward from the cell face, so two jointed rooms' plugs meet face to
    /// face on the shared cell face.
    /// </summary>
    public (Point Mins, Point Maxs) PlugBox(CellFace face) => PlugBox(face, 0, 1);

    /// <summary>
    /// The plug box of a socket of a room <paramref name="cells"/> cells a side
    /// (M24): the opening on the cell edge <paramref name="offset"/> cells along
    /// its wall (from the south on east and west walls, from the west on north
    /// and south), room-local.
    /// </summary>
    public (Point Mins, Point Maxs) PlugBox(CellFace face, int offset, int cells)
    {
        float c = cells * Cell, t = WallDepth, lo = DoorLow + offset * Cell, hi = DoorHigh + offset * Cell, z0 = Sill, z1 = Head;
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
/// A door frame model (task A15): a shipped prop that lines a socket's opening,
/// so a joined doorway has sides and a head. The link removes a joined
/// socket's plug, and the faces that touched it were never compiled, so
/// without a frame a doorway draws as a gap at its edges.
/// </summary>
/// <remarks>
/// <para>
/// The frame is socket furniture: a non-solid <c>prop_static</c> with
/// <c>room_socket &lt;wall&gt;</c>, centred on the cell face, whose hull
/// reaches out of its cell only into the doorway beyond its socket. The link
/// keeps one room's at a joint and drops it at a cap, and a static prop costs
/// no entity, so a doorway costs nothing. The kit is cut to the frame
/// (<see cref="RoomKit.For"/>): the doorway is the frame's depth, its sides
/// behind the posts, its head behind the frame's head.
/// </para>
/// <para>
/// The numbers are the model's mesh bounds from the asset database
/// (<c>model_view</c>) and its posts and head measured off its front view
/// (<c>/home/lodle/assetdb/models/.../front.png</c>, 1.67 pixels a unit).
/// The model's depth is along its x axis, its width along y.
/// </para>
/// </remarks>
/// <param name="Model">The model path.</param>
/// <param name="MinX">The mesh's back face along its depth axis.</param>
/// <param name="MaxX">The mesh's front face along its depth axis.</param>
/// <param name="Width">The mesh's outer width, centred on the origin.</param>
/// <param name="Bottom">The mesh's lowest point above its origin (the feet of the posts).</param>
/// <param name="Top">The mesh's highest point above its origin.</param>
/// <param name="Post">A post's width across the opening.</param>
/// <param name="Head">The head's depth below the top.</param>
public sealed record FrameModel(string Model, float MinX, float MaxX, int Width, float Bottom, float Top, float Post, float Head)
{
    /// <summary>
    /// TF2's <c>props_farm/doorframe002d</c>: a square timber frame 16 deep,
    /// 208 by 192, posts and head about 8 and 9, no sill, so each half of a
    /// joined doorway's floor is covered by TF2's
    /// <c>props_gameplay/door_grate003_floorplate</c> (10 by 192, 3.2 above the
    /// floor, sunk 7.6 below it; no <c>prop_data</c>, which a static prop may
    /// not have: the room compile deletes a breakable model's static prop).
    /// </summary>
    public static FrameModel FarmDoorframe { get; } =
        new("models/props_farm/doorframe002d.mdl", MinX: -4, MaxX: 12, Width: 208, Bottom: -0.7375796f, Top: 191.26289f, Post: 8, Head: 9)
        {
            Sill = new SillModel("models/props_gameplay/door_grate003_floorplate.mdl", MinX: -5.0185714f, MaxX: 4.9881549f, Width: 192f, Floor: 0f),
        };

    /// <summary>
    /// What covers the doorway's floor when the frame has no sill of its own:
    /// the doorway's floor is the floor under the two plugs, whose faces the
    /// room compile culled, so a joined doorway's floor draws as a strip of the
    /// void (the preview shows sky through it from either side). Null for a
    /// frame with its own sill.
    /// </summary>
    public SillModel? Sill { get; init; }

    /// <summary>The frame's depth, which the doorway is.</summary>
    public int Depth => (int)Math.Round(MaxX - MinX);

    /// <summary>The frame's height from the feet of its posts to its top.</summary>
    public float Height => Top - Bottom;

    /// <summary>The width between the posts.</summary>
    public float InnerWidth => Width - 2 * Post;

    /// <summary>The height under the head, from the feet of the posts.</summary>
    public float InnerHeight => Height - Head;

    /// <summary>The middle of the mesh along its depth axis, which stands on the cell face.</summary>
    public float CentreX => (MinX + MaxX) / 2;
}

/// <summary>
/// A theme's door frame built of brushes instead of a model (the review of 0c79c6fc2: one frame per
/// theme, and the farm frame is the only model that fits the kit): a <c>func_brush</c> per socket kept
/// only where it is joined (<c>room_needs joined_&lt;socket&gt;</c>), never solid, lining the room's
/// half of the doorway with two posts and a head standing <see cref="Proud"/> into the room, and on
/// the wall round the opening a flange (a steel channel), a cap past the posts (a timber set) or a
/// stripe over the head, and planks across the threshold in the room, and a slab over the let-down floor
/// under the plug. Its faces inside the doorway were in the plug when the room was lit, so it carries a
/// <c>_minlight</c> (<see cref="MinLight"/>, the theme's).
/// </summary>
/// <param name="PostWidth">A post's width across the opening, inside it.</param>
/// <param name="HeadDepth">The head's depth under the opening's top, inside it.</param>
/// <param name="Proud">How far posts and head stand into the room past the wall's face.</param>
public sealed record BrushFrame(Surface Post, Surface Head, int PostWidth, int HeadDepth, int Proud)
{
    /// <summary>A flange's width on the wall round the opening (0: none), standing <see cref="FlangeDepth"/> off it.</summary>
    public int Flange { get; init; }

    /// <summary>How far a flange stands off the wall.</summary>
    public int FlangeDepth { get; init; } = 4;

    /// <summary>The flange over the head in its own surface (the Mill's hazard stripe), or null for the head's.</summary>
    public Surface? HeadFace { get; init; }

    /// <summary>A cap beam on the wall over the opening running this far past each post (a timber set's), or 0.</summary>
    public int CapOverhang { get; init; }

    /// <summary>Planks across the threshold on the room's floor (a level crossing), or null.</summary>
    public Surface? Crossing { get; init; }

    /// <summary>How far into the room the threshold's planks run.</summary>
    public int CrossingDepth { get; init; } = 72;

    /// <summary>Triangular haunches this big under the head at both inner corners, so the opening reads as an arch (a rock arch, a carved stone arch), or 0.</summary>
    public int Fillet { get; init; }

    /// <summary>A keystone over the head's middle, standing this high above it on the wall (a carved arch), or 0.</summary>
    public int Keystone { get; init; }

    /// <summary>A plinth this high at each post's foot, as wide as the post and its flange (a carved arch), or 0.</summary>
    public int Plinth { get; init; }

    /// <summary>
    /// A broken set (level 6's wrecked workings): the head cracked in two, its second half sagging this
    /// far at the break and the cap gone past that post; 0 for a whole frame.
    /// </summary>
    public int Sag { get; init; }

    /// <summary>
    /// The light the frame's faces in the doorway get at least (its <c>_minlight</c>): they were in the
    /// plug when the room was lit, so without a floor they draw black. A theme sets it to its own walls'
    /// light (brief p9: at 0.35 on every theme a dark level's arches took lightmaps three to eight times
    /// their walls' and glowed); vrad lights such a face to about half this.
    /// </summary>
    public float MinLight { get; init; } = DefaultMinLight;

    /// <summary>The floor Act I's frames keep: its walls are lit to about this, and its arches read as timber and steel at it.</summary>
    public const float DefaultMinLight = 0.35f;

    /// <summary>
    /// A slab of the frame's post surface over the doorway's let-down floor under the plug, its top this
    /// far above the sill (0: none). The let-down floor and the sill plates on it were in the plug when
    /// the room was lit and draw as a black strip across every joined doorway (plan task H9); the slab
    /// is the frame's, so it takes the frame's floor of light instead.
    /// </summary>
    public float SillSlab { get; init; } = 1;
}

/// <summary>A flat prop laid across a doorway's floor (<see cref="FrameModel.Sill"/>), its depth along x and its width along y.</summary>
/// <param name="Floor">The height in the model's frame that stands on the floor's top.</param>
public sealed record SillModel(string Model, float MinX, float MaxX, float Width, float Floor)
{
    /// <summary>The plank's depth across the doorway.</summary>
    public float Depth => MaxX - MinX;

    /// <summary>The middle of the mesh along its depth axis.</summary>
    public float CentreX => (MinX + MaxX) / 2;
}

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
    public static RoomKit Normal { get; } = For("normal", 1024, FrameModel.FarmDoorframe);

    /// <summary>Level 5's arena kit (§3.2).</summary>
    public static RoomKit Boss2048 { get; } = For("boss2048", 2048, FrameModel.FarmDoorframe);

    /// <summary>Level 15's arena kit (§3.2).</summary>
    public static RoomKit Boss3072 { get; } = For("boss3072", 3072, FrameModel.FarmDoorframe, navVoxel: 32);

    /// <summary>Level 10's arena kit, for the Tank's track (§3.2, M14).</summary>
    public static RoomKit Boss4096 { get; } = For("boss4096", 4096, FrameModel.FarmDoorframe, navVoxel: 32);

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

/// <summary>A flat prop laid across a doorway's floor (<see cref="FrameModel.Sill"/>), its depth along x and its width along y.</summary>
/// <param name="Floor">The height in the model's frame that stands on the floor's top.</param>
public sealed record SillModel(string Model, float MinX, float MaxX, float Width, float Floor)
{
    /// <summary>The plank's depth across the doorway.</summary>
    public float Depth => MaxX - MinX;

    /// <summary>The middle of the mesh along its depth axis.</summary>
    public float CentreX => (MinX + MaxX) / 2;
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
    /// <summary>The frame model the theme's doorways are lined with, which a depth's kit is cut to (<see cref="RoomKit.For"/>).</summary>
    public FrameModel DoorFrame { get; init; } = FrameModel.FarmDoorframe;

    /// <summary>The folder under <c>materials/</c> the Descent rooms' own materials live in.</summary>
    public const string MaterialFolder = "descent";

    /// <summary>
    /// The theme's door plug material: the wall's base texture with
    /// <c>%compileTrigger</c> and nothing else. The trigger flag is what the
    /// link finds a plug by (it strips a joined socket's plug faces); leaving
    /// out <c>%compileNonsolid</c> keeps the plug solid, so a room's compile is
    /// sealed. It draws the wall's texture, but unlit (see
    /// <see cref="RoomBuilder.CapThickness"/>), so a capped socket's plug is
    /// still covered by the cap panel.
    /// </summary>
    public static Surface PlugOf(string name, Surface wall) => new($"{MaterialFolder}/{name}_plug", wall.Scale);

    /// <summary>
    /// The <c>.vmt</c> text of a plug material over <paramref name="baseTexture"/>
    /// (each theme's wall material's own base texture has its name, as the asset
    /// database records for <c>dev/graygrid</c> and <c>nature/rockwall012</c>).
    /// </summary>
    public static string PlugVmt(string baseTexture) =>
        $"\"LightmappedGeneric\"\n{{\n\t\"$basetexture\" \"{baseTexture}\"\n\t\"%compileTrigger\" \"1\"\n}}\n";

    /// <summary>The materials the theme names that ship with the mod, as paths under <c>materials/</c> and their text.</summary>
    public IReadOnlyList<(string Path, string Text)> OwnMaterials =>
        Plug.Material.StartsWith(MaterialFolder + "/", StringComparison.Ordinal)
            ? [(Plug.Material + ".vmt", PlugVmt(Wall.Material))]
            : [];

    /// <summary>Every theme the generators draw rooms in.</summary>
    public static IReadOnlyList<RoomTheme> All => [Grey, Mine];

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
        // The pipeline finds a plug by its trigger surfaces (PlugOf).
        Plug: PlugOf("mine", "nature/rockwall012"),
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
        Plug: PlugOf("grey", new("dev/graygrid", 1f)),
        Trigger: "TOOLS/TOOLSTRIGGER")
;
}

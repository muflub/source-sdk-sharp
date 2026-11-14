namespace SourceSharp.MapGen.Descent;

/// <summary>
/// A shipped model a theme places: its path and its mesh bounds about its
/// origin (the asset database's <c>model_view</c> <c>mesh_min_*</c> and
/// <c>mesh_max_*</c>), whether it can be a <c>prop_static</c> (the
/// database's <c>static_prop</c>, and no <c>prop_data</c>: the room compile
/// deletes a breakable model's static prop), whether it has collision, and
/// which way its front faces in its own frame.
/// </summary>
/// <param name="Front">The yaw of the model's front in its own frame: 0 when it faces +x (Source's convention), 180 when its back is +x (a wall lamp whose mounting plate is its +x side).</param>
public sealed record PropModel(string Model, Point Mins, Point Maxs, bool Static = true, bool Solid = true, int Front = 0)
{
    /// <summary>The mesh's box about the origin when the model stands turned by <paramref name="yaw"/> (a multiple of 90).</summary>
    public (Point Mins, Point Maxs) Turned(int yaw)
    {
        // The four corners of the footprint turned, and the box round them.
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        double r = yaw * Math.PI / 180;
        float c = (float)Math.Round(Math.Cos(r)), s = (float)Math.Round(Math.Sin(r));
        foreach (float x in (float[])[Mins.X, Maxs.X])
            foreach (float y in (float[])[Mins.Y, Maxs.Y])
            {
                float tx = x * c - y * s, ty = x * s + y * c;
                minX = Math.Min(minX, tx);
                maxX = Math.Max(maxX, tx);
                minY = Math.Min(minY, ty);
                maxY = Math.Max(maxY, ty);
            }

        return (new(minX, minY, Mins.Z), new(maxX, maxY, Maxs.Z));
    }

    /// <summary>The model's height.</summary>
    public float Height => Maxs.Z - Mins.Z;

    /// <summary>
    /// How far the model's back stands behind its origin along its front
    /// (the distance from the origin to the wall it is set against).
    /// </summary>
    public float Back => (((Front % 360) + 360) % 360) switch
    {
        0 => -Mins.X,
        90 => -Mins.Y,
        180 => Maxs.X,
        _ => Maxs.Y,
    };
}

/// <summary>Where a lamp's fixture is fixed.</summary>
public enum LampMount
{
    /// <summary>Hung under the ceiling, its top against it.</summary>
    Ceiling,

    /// <summary>On a wall, its back against it, at <see cref="LampKind.WallHeight"/> and rows above.</summary>
    Wall,

    /// <summary>Standing on the floor by a wall.</summary>
    Floor,

    /// <summary>
    /// Hung on a long cord from the ceiling (<see cref="LampKind.Cords"/>), its
    /// bottom <see cref="LampKind.WallHeight"/> above the floor under it, or
    /// above a jump room's rocket air: a tall room's lamps, low enough to light
    /// its floor, and something overhead.
    /// </summary>
    Pendant,
}

/// <summary>A spot lamp's cone (<c>light_spot</c>): its inner and outer angles and how far below level it points.</summary>
public sealed record SpotCone(int Inner, int Outer, int Pitch);

/// <summary>
/// A kind of lamp (task A9): the fixture model, how it is fixed, the light
/// it gives (a <c>light</c>, or a <c>light_spot</c> when it has a
/// <see cref="Spot"/>), and how far apart the dresser hangs them along a
/// room's lit path.
/// </summary>
/// <param name="LightAt">Where the light stands from the fixture's origin, in the fixture's own frame (its front along +x once turned): in the shade's mouth, outside the mesh.</param>
public sealed record LampKind(PropModel Fixture, LampMount Mount, Lamp Light, int Spacing, Point LightAt)
{
    /// <summary>A spot lamp's cone, or null for a point lamp.</summary>
    public SpotCone? Spot { get; init; }

    /// <summary>A ceiling lamp's highest ceiling above the floor; a taller room takes the theme's tall lamps.</summary>
    public int MaxHang { get; init; } = 512;

    /// <summary>A wall or floor lamp's height above the floor (a wall lamp's first row).</summary>
    public int WallHeight { get; init; } = 160;

    /// <summary>A wall lamp's rows up a tall wall, this far apart (0: one row).</summary>
    public int RowPitch { get; init; }

    /// <summary>The pitch of fill lamps over a wide room's open floor (0: the lit path only, so side bays stay in shadow).</summary>
    public int Fill { get; init; }

    /// <summary>
    /// What a floor lamp is set on (a crate under a lantern), or null to set it
    /// on the floor; a lamp on a stand stands against a wall.
    /// </summary>
    public PropModel? Stand { get; init; }

    /// <summary>A pendant's cord pieces, longest first, each standing on its origin along +z (<see cref="LampMount.Pendant"/>).</summary>
    public IReadOnlyList<PropModel> Cords { get; init; } = [];

    /// <summary>A pendant's grid over a tall room's open floor (0: pendants only where the floor is dark).</summary>
    public int Grid { get; init; }

    /// <summary>
    /// Pools: the room's lamps are this few to this many along its route,
    /// hung from the theme's sets where one is near and set on their stands
    /// otherwise, with the dark between them (Level 3's lantern pools); null
    /// for lamps every <see cref="Spacing"/>.
    /// </summary>
    public (int Least, int Most)? Pools { get; init; }
}

/// <summary>A brush frame the dresser stands across a corridor at intervals: two posts against the walls and a cap under the ceiling (a timber or steel set).</summary>
/// <param name="Spacing">How far apart along the corridor.</param>
/// <param name="Post">A post's square section.</param>
/// <param name="Cap">The cap's depth below the ceiling.</param>
/// <param name="MaxWidth">The widest corridor that takes sets; wider rooms are chambers and halls.</param>
public sealed record SetFrame(int Spacing, int Post, int Cap, int MaxWidth = 448)
{
    /// <summary>The chance a set has fallen in: its cap and one post gone, the other post left standing (level 6's wrecked workings).</summary>
    public float Broken { get; init; }
}

/// <summary>
/// The props a theme places, by role (task A9): against walls, on open
/// floor, in corners, along the ceiling, along the floor's route (rails),
/// and brush sets across corridors. Each role's models are picked by the
/// room's seed.
/// </summary>
public sealed record PropSet
{
    /// <summary>Clutter set against walls, its back to the wall.</summary>
    public IReadOnlyList<PropModel> Wall { get; init; } = [];

    /// <summary>At most one wall prop per this many units of wall.</summary>
    public int WallSpacing { get; init; } = 384;

    /// <summary>Clutter on open floor, away from the walls.</summary>
    public IReadOnlyList<PropModel> Floor { get; init; } = [];

    /// <summary>Open-floor props per million square units of floor (a 1024 hall is about one).</summary>
    public float FloorDensity { get; init; } = 2;

    /// <summary>Piles in corners.</summary>
    public IReadOnlyList<PropModel> Corner { get; init; } = [];

    /// <summary>The chance a corner gets a pile.</summary>
    public float CornerChance { get; init; } = 0.5f;

    /// <summary>Runs along the walls under the ceiling (pipes, cables), each model laid end to end along +x from its origin, which is at the ceiling.</summary>
    public PropModel? CeilingRun { get; init; }

    /// <summary>Runs along the floor's route between doors (track), each model laid end to end along its own +y.</summary>
    public PropModel? Rail { get; init; }

    /// <summary>Brush sets across corridors, in the theme's trim.</summary>
    public SetFrame? Sets { get; init; }

    /// <summary>Shorter pieces of <see cref="Rail"/>, longest first, that finish a run of track to its end.</summary>
    public IReadOnlyList<PropModel> RailEnds { get; init; } = [];

    /// <summary>Furniture hung on walls at eye height and above (signs, notice boards, a clock): each with its centre's height above the floor.</summary>
    public IReadOnlyList<HungModel> Hung { get; init; } = [];

    /// <summary>At most one hung piece per this many units of wall.</summary>
    public int HungSpacing { get; init; } = 384;

    /// <summary>The quarter curve that joins two runs of track meeting at a corner of a room, or null for none.</summary>
    public PropModel? RailBend { get; init; }

    /// <summary>The buffer stop that ends a run of track (at a capped door, a spur, a blocked run), or null for none.</summary>
    public PropModel? RailStop { get; init; }

    /// <summary>The shape families whose rooms carry track (<see cref="Rail"/>): the tunnels and yards a level's lines run through.</summary>
    public IReadOnlySet<string> RailFamilies { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The families whose rooms get only a stub of track from one door, ending in a buffer with <see cref="Siding"/>'s cart at it.</summary>
    public IReadOnlySet<string> StubFamilies { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>What stands on a siding beside a room's track (an ore cart), or null for no sidings.</summary>
    public PropModel? Siding { get; init; }

    /// <summary>Small clutter set beside a wall, corner or floor piece, so clutter stands in groups (a crate stack with a barrel and a sack), not alone.</summary>
    public IReadOnlyList<PropModel> Companions { get; init; } = [];

    /// <summary>What stands on a larger room's focus (a hall's plinth): an ore heap, a machine; the first that fits.</summary>
    public IReadOnlyList<PropModel> Centrepiece { get; init; } = [];

    /// <summary>A second run of <see cref="CeilingRun"/> along one wall of a tall room, its underside this high above the floor (0: none).</summary>
    public int WallRunHeight { get; init; }

    /// <summary>Pieces hung from the roof of a tall room upside down (stalactites), each mesh symmetric about its origin.</summary>
    public IReadOnlyList<PropModel> Hanging { get; init; } = [];

    /// <summary>One hanging piece per this much of a tall room's floor, on a grid.</summary>
    public int HangingSpacing { get; init; } = 384;

    /// <summary>The most of a room's walked floor one open-floor prop may cover: a big machine stands only in a big room.</summary>
    public float FloorShare { get; init; } = 0.06f;


    /// <summary>No props.</summary>
    public static PropSet None { get; } = new();

    /// <summary>Every model the set names.</summary>
    public IEnumerable<PropModel> Models =>
        Wall.Concat(Floor).Concat(Corner).Concat(CeilingRun is { } c ? [c] : []).Concat(Rail is { } r ? [r] : [])
            .Concat(RailEnds).Concat(Siding is { } s ? [s] : []).Concat(Companions)
            .Concat(RailBend is { } b ? [b] : []).Concat(RailStop is { } t ? [t] : []).Concat(Hung.Select(h => h.Model)).Concat(Centrepiece).Concat(Hanging);
}

/// <summary>
/// An overhead crane's rails in a tall larger room (levels 2 and 4): two girders of the theme's trim
/// along the room's x axis <see cref="Height"/> above the floor, a bridge girder across them over
/// the focus, and <see cref="Hook"/> hung from the bridge's middle on a short cable.
/// </summary>
public sealed record Gantry(int Height, int Girder, PropModel? Hook, PropModel? Cable);

/// <summary>
/// Trim bands along a room's flat walls, in the theme's trim (task A10, the
/// review of 2026-11-10): strips standing <see cref="Proud"/> off the wall,
/// a skirting on the floor, a dado at <see cref="DadoHeight"/> and a beam
/// under the roof. A height of 0 leaves that band out.
/// </summary>
/// <param name="Belt">The depth of the belts up a tall wall every 512 (0: none).</param>
public sealed record TrimBands(int Skirting, int DadoHeight, int Dado, int Beam, int Belt = 16, int Proud = 4)
{
    /// <summary>The bands' own surface (a hazard stripe), or null for the theme's trim.</summary>
    public Surface? Surface { get; init; }

    /// <summary>The most a skirting may be: a step, which everyone walks over.</summary>
    public const int MaxSkirting = 16;
}

/// <summary>
/// Pilasters (steel sets, timber posts) up a tall wall every
/// <see cref="Spacing"/> along it, from the floor to the roof, in the theme's
/// trim: <see cref="Width"/> along the wall, <see cref="Depth"/> off it, on
/// walls of rooms at least <see cref="MinHeight"/> high.
/// </summary>
public sealed record Pilasters(int Spacing, int Width, int Depth, int MinHeight = 768)
{
    /// <summary>The height of a wall plate joining two neighbouring pilasters into a set lining the wall (0: none).</summary>
    public int Cap { get; init; }

    /// <summary>The wall plate's depth.</summary>
    public int CapDepth { get; init; } = 24;
}

/// <summary>
/// A work lamp each large room gets (its walked floor at least
/// <see cref="MinFloor"/> square units): the kind, and how many.
/// </summary>
/// <param name="AtFocus">Aimed at the floor round the room's focus (its main feature) rather than at all of it.</param>
/// <param name="Apart">How far apart two of a room's work lamps hang at least.</param>
public sealed record WorkLamps(LampKind Kind, int Count = 1, float MinFloor = 250_000, bool AtFocus = false, float Apart = 384);

/// <summary>A piece hung on a wall (<see cref="PropSet.Hung"/>): its model, its back to the wall, and its centre's height above the floor.</summary>
public sealed record HungModel(PropModel Model, int Height);

using System.Globalization;

namespace SourceSharp.MapGen.Descent;

/// <summary>
/// What a depth is drawn in (plan_descent_mine_levels.md §6, §7, task A9):
/// the surfaces of its rooms, each a material and its texture scale; the
/// door frame its kit is cut to; its lamps; its props by role; the fog,
/// tone map and sun of its library; how wet and decayed it is. The shapes
/// take the surfaces; <see cref="Dressing.RoomDresser"/> takes the rest from
/// a built room's own facts, so a theme dresses any shape.
/// </summary>
/// <remarks>
/// Every surface tiles a whole number of times along a 1024 cell at its
/// scale, so the walls of two rooms side by side line up
/// (<see cref="Brush.ToVmf"/> starts every room's textures at its cell's
/// corner). Every material and model a theme names is a row of the asset
/// database, recorded in the depth's palette file
/// (<c>Themes/palettes/dNN.tsv</c>), which a fact holds to the database.
/// </remarks>
public sealed record RoomTheme(Surface Floor, Surface Wall, Surface Ceiling, Surface Block, Surface Plug, Surface Trigger)
{
    /// <summary>The theme's name: its plug material's stem and its palette's key.</summary>
    public string Name { get; init; } = "grey";

    /// <summary>The surface of the sets the dresser stands across a corridor (<see cref="PropSet.Sets"/>); the block's by default.</summary>
    public Surface? Trim { get; init; }

    /// <summary>The surface of a tall wall's lower band, up to <see cref="WallBand"/> above the floor, or null for one surface floor to roof.</summary>
    public Surface? WallLow { get; init; }

    /// <summary>
    /// The wall of a room larger than one cell (a great hall), or null for <see cref="Wall"/>: the same
    /// rock drawn larger, since a 2048-long flat wall repeats a cell's tile into a brick pattern
    /// (brief p4, level 3's halls).
    /// </summary>
    public Surface? LargeWall { get; init; }

    /// <summary>The water of the depth's pools and flooded rooms (a <c>water</c> shader material), or null for the shapes' default (<see cref="Shapes.PoolShape.WaterMaterial"/>).</summary>
    public string? Water { get; init; }

    /// <summary>Water standing on the floor of the depth's rooms (a flooded level's floors, a cave's pools), or null for none.</summary>
    public StandingWater? Standing { get; init; }

    /// <summary>The lower band's height above the floor; only walls twice as tall take the band.</summary>
    public int WallBand { get; init; } = 256;

    /// <summary>Pillars and stepping pillars are square piers, not round ones (a steel haulage level's).</summary>
    public bool SquarePillars { get; init; }

    /// <summary>A band of a second wall material through every wall tall enough (a seam of ore in the rock), or null.</summary>
    public WallSeam? WallSeam { get; init; }

    /// <summary>A dim cold light spread under the roof of every room at this pitch, so the shadow between the lamps' pools is cool, not black (null: none).</summary>
    public (Lamp Light, int Pitch)? CoolFill { get; init; }

    /// <summary>A stripe of this surface over every door's head and along the top edge of every raised deck and ledge (the Mill's hazard paint), or null.</summary>
    public Surface? Hazard { get; init; }

    /// <summary>Beams across the roof of a hall or shaft, this far apart (0: none), in the trim.</summary>
    public int RoofBeams { get; init; }

    /// <summary>An overhead crane over a tall larger room's floor (<see cref="Descent.Gantry"/>), or null.</summary>
    public Gantry? Gantry { get; init; }

    /// <summary>A headframe of the theme's trim over a larger room's raised focus (a winze's timber frame on a hall's plinth), when true.</summary>
    public bool Headframe { get; init; }

    /// <summary>The trim bands the dresser runs along the walls (a skirting, a dado, a beam under the roof), or null.</summary>
    public TrimBands? Bands { get; init; }

    /// <summary>The pilasters or sets the dresser stands up a tall wall, or null.</summary>
    public Pilasters? Pilasters { get; init; }

    /// <summary>The work lamps a large room gets besides the theme's own (a cold white flood among sodium lamps), or null.</summary>
    public WorkLamps? WorkLamps { get; init; }

    /// <summary>The trim, or the block when the theme names none.</summary>
    public Surface TrimOrBlock => Trim ?? Block;

    /// <summary>The frame model the theme's doorways are lined with, which a depth's kit is cut to (<see cref="RoomKit.For"/>).</summary>
    public FrameModel DoorFrame { get; init; } = FrameModel.FarmDoorframe;

    /// <summary>The theme's own door frame of brushes (<see cref="BrushFrame"/>), which lines its doorways instead of <see cref="DoorFrame"/>'s model; null for the model.</summary>
    public BrushFrame? FrameBrushes { get; init; }

    /// <summary>
    /// The lamps the dresser hangs along a room's lit path, or null to keep
    /// the lamps the shapes placed (the grey measuring set).
    /// </summary>
    public LampKind? Lamps { get; init; }

    /// <summary>The lamps of a room too tall for <see cref="Lamps"/> to hang in (its ceiling above their <see cref="LampKind.MaxHang"/>); <see cref="Lamps"/> when null.</summary>
    public LampKind? TallLamps { get; init; }

    /// <summary>
    /// The lamps the dresser hangs, after the others, over floor no lamp yet
    /// reaches (a free-form room's alcoves and ledges, a chamber whose roof is
    /// out of a ceiling lamp's reach), or null to use only the others.
    /// </summary>
    public LampKind? CoverLamps { get; init; }

    /// <summary>
    /// Lamps hung on long cords in a tall room (<see cref="LampMount.Pendant"/>): a grid of them in
    /// halls and shafts, and the cover pass's choice wherever a floor is out of every other lamp's
    /// reach (a larger room's middle), or null for none.
    /// </summary>
    public LampKind? Pendants { get; init; }

    /// <summary>The props the dresser places, by role.</summary>
    public PropSet Props { get; init; } = PropSet.None;

    /// <summary>The models a boss arena stands itself (<see cref="RoomBuilder.WithSetPiece"/>): the robots' lift, a tank gate.</summary>
    public IReadOnlyList<PropModel> SetPieces { get; init; } = [];

    /// <summary>The lamps a boss arena asks for by site (<see cref="RoomBuilder.WithLampSite"/>): its warning lamps, work lights and torches.</summary>
    public IReadOnlyList<LampKind> ArenaLamps { get; init; } = [];

    /// <summary>The surfaces a boss arena draws beyond the theme's own (a catwalk's grating, the Tank's worn track, the platform's stone).</summary>
    public IReadOnlyList<Surface> ArenaSurfaces { get; init; } = [];

    /// <summary>The fog every level of the depth is drawn in (the library's <c>env_fog_controller</c>).</summary>
    public Fog Fog { get; init; } = Fog.Mine;

    /// <summary>The tone map of the depth's library.</summary>
    public ToneMap ToneMap { get; init; } = ToneMap.Mine;

    /// <summary>How wet and decayed the depth is, 0 (dry, swept, new) to 1 (standing slime); §6's fourth row. Read by the dresser's density of decay props and by later acts.</summary>
    public float Wetness { get; init; }

    /// <summary>The library's sun, for a depth with a room open to the sky (level 1's shaft, §8), or null.</summary>
    public Sun? Sun { get; init; }

    /// <summary>The library's sky.</summary>
    public string Sky { get; init; } = MineShaft.Sky;

    /// <summary>
    /// Lamps set in pools along each room's route besides its theme's own (<see cref="Lamps"/>), or
    /// null: level 9's candle clusters on the floor by the walls, so its crypts and naves are lit in
    /// pools rather than by a fill (brief p9). Its <see cref="LampKind.Pools"/> says how many a room
    /// takes; a floor kind with no stand stands on the floor against the wall.
    /// </summary>
    public LampKind? RoutePools { get; init; }

    /// <summary>
    /// The theme a depth's ways in (<c>room_role up</c>) are drawn in instead,
    /// or null: level 1's arrival is the bottom of the town's shaft, in the
    /// town's own timber, rock and lanterns (§8).
    /// </summary>
    public RoomTheme? Arrival { get; init; }

    /// <summary>
    /// The theme the depth's cave families (<see cref="CaveFamilies"/>) are drawn and dressed in instead, or
    /// null: level 11's grottos and flooded galleries, caves the works broke into, in rock and poured concrete
    /// rather than the factory's steel panels (brief p15).
    /// </summary>
    public RoomTheme? Caves { get; init; }

    /// <summary>The families <see cref="Caves"/> draws (a room's name up to its first underscore).</summary>
    public IReadOnlySet<string> CaveFamilies { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// How many <c>prop_dynamic</c>s a room may hold for models that cannot be
    /// static (a lantern); each is a map entity, counted against the level's
    /// budget. At 0 such models are skipped.
    /// </summary>
    public int DynamicBudget { get; init; }

    /// <summary>The folder under <c>materials/</c> the Descent rooms' own materials live in.</summary>
    public const string MaterialFolder = "descent";

    /// <summary>
    /// The theme's door plug material: the wall's base texture with
    /// <c>%compileTrigger</c>, drawn by nothing. The trigger flag is what the
    /// link finds a plug by; leaving out <c>%compileNonsolid</c> keeps the plug
    /// solid, so a room's compile is sealed. A capped socket is covered by its
    /// cap panel (<see cref="RoomBuilder.CapThickness"/>), never by the plug.
    /// </summary>
    public static Surface PlugOf(string name, Surface wall) => new($"{MaterialFolder}/{name}_plug", wall.Scale);

    /// <summary>
    /// The <c>.vmt</c> text of a plug material over <paramref name="baseTexture"/>
    /// (each theme's wall material's own base texture has its name, as the asset
    /// database records for <c>dev/graygrid</c> and <c>nature/rockwall012</c>).
    /// <c>$no_draw</c>: the link keeps a joined socket's plug faces in the map,
    /// flagged <c>SURF_NODRAW</c>, and the engine draws a world face whatever its
    /// flags (vbsp never writes a nodraw one), so without it every joined
    /// doorway was shut in game by a panel of the wall's texture on each side
    /// (brief p5, 2026-11-11), while the preview, which honours the flag,
    /// showed it open.
    /// </summary>
    public static string PlugVmt(string baseTexture) =>
        $"\"LightmappedGeneric\"\n{{\n\t\"$basetexture\" \"{baseTexture}\"\n\t\"$no_draw\" \"1\"\n\t\"%compileTrigger\" \"1\"\n}}\n";

    /// <summary>The materials the theme names that ship with the mod, as paths under <c>materials/</c> and their text.</summary>
    public IReadOnlyList<(string Path, string Text)> OwnMaterials =>
        [.. Plug.Material.StartsWith(MaterialFolder + "/", StringComparison.Ordinal) ? [(Plug.Material + ".vmt", PlugVmt(Wall.Material))] : Array.Empty<(string, string)>(),
         .. Extra.Select(m => (m.Name + ".vmt", m.Vmt))];

    /// <summary>The theme's own materials beside its plug (<see cref="OwnMaterial"/>): a water of its own, a bed under it.</summary>
    public IReadOnlyList<OwnMaterial> Extra { get; init; } = [];

    /// <summary>The database's material a theme's material stands for: itself, or the one an own material of the theme is made from.</summary>
    public string AssetOf(string material) => Extra.FirstOrDefault(m => m.Name == material)?.BasedOn ?? material;

    /// <summary>Every theme the generators draw rooms in.</summary>
    public static IReadOnlyList<RoomTheme> All => [Grey, Mine, .. Themes.ActOne.All, .. Themes.ActTwo.All, .. Themes.ActThree.All, .. Themes.Bosses.All];

    /// <summary>
    /// The same room drawn in <paramref name="to"/>: every brush face of this
    /// theme's floor, wall, ceiling, block, trim and plug materials takes
    /// <paramref name="to"/>'s (a room the shapes built in this theme, redrawn
    /// as another depth's or as the town's).
    /// </summary>
    public Room Redraw(Room room, RoomTheme to)
    {
        var map = new List<(string From, Surface To)>
        {
            (Plug.Material, to.Plug), (Floor.Material, to.Floor), (Wall.Material, to.Wall),
            (Ceiling.Material, to.Ceiling), (Block.Material, to.Block), (TrimOrBlock.Material, to.TrimOrBlock),
        };
        if (WallLow is { } low)
            map.Add((low.Material, to.WallLow ?? to.Wall));
        if (LargeWall is { } large && large.Material != Wall.Material)
            map.Add((large.Material, to.LargeWall ?? to.Wall));
        Brush Map(Brush b)
        {
            // One pass over the faces, so a material two roles share maps once.
            var faces = b.Faces.Select(f => map.FirstOrDefault(m => m.From == f.Surface.Material) is { From: not null } m ? f with { Surface = m.To } : f).ToList();
            Surface main = map.FirstOrDefault(m => m.From == b.Surface.Material) is { From: not null } mm ? mm.To : b.Surface;
            return new Brush(faces, main);
        }

        var world = room.World.Select(Map).ToList();
        // A way in redrawn in a theme with a lower band this one lacks takes the band (level 1's rock hint).
        if (WallLow is null && to.WallLow is { } band)
        {
            float z0 = room.Kit.Sill, cut = z0 + to.WallBand;
            for (int k = world.Count - 1; k >= 0; k--)
            {
                Brush w = world[k];
                if (!w.IsBox || w.Surface != to.Wall || w.Mins.Z != z0 || w.Maxs.Z - z0 < 2 * to.WallBand)
                    continue;
                world[k] = Brush.Box(w.Mins with { Z = cut }, w.Maxs, to.Wall);
                world.Add(Brush.Box(w.Mins, w.Maxs with { Z = cut }, band));
            }
        }

        return room with
        {
            World = world,
            Entities = [.. room.Entities.Select(e => e.Brushes.Count == 0 ? e : e with { Brushes = [.. e.Brushes.Select(Map)] })],
        };
    }

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
        Trigger: "TOOLS/TOOLSTRIGGER")
    {
        Name = "mine",
    };

    /// <summary>
    /// Phase 1's grey rooms: TF2's measuring textures at scale 1, so a grid
    /// line is every 64 units (<c>dev/graygrid</c>, 512 texels) and a floor
    /// square 64 (<c>dev/dev_measuregeneric01b</c>, 128 texels in four), and
    /// a height can be read off a wall. Blocks, decks and pillars are the
    /// orange <c>dev/dev_measuregeneric01</c>, so features stand out. It keeps
    /// the shapes' own lamps and places no props: depths 5 to 15 are drawn in
    /// it until their acts are themed.
    /// </summary>
    public static RoomTheme Grey { get; } = new(
        Floor: new("dev/dev_measuregeneric01b", 1f),
        Wall: new("dev/graygrid", 1f),
        Ceiling: new("dev/graygrid", 1f),
        Block: new("dev/dev_measuregeneric01", 1f),
        Plug: PlugOf("grey", new("dev/graygrid", 1f)),
        Trigger: "TOOLS/TOOLSTRIGGER");

    /// <summary>
    /// The kind of hazard (a row of <c>hazards.json</c>) the theme's pools hold, a volume over each
    /// pool's basin (<see cref="RoomBuilder.WithHazard"/>), or null: the Pit's slime lake (level 15).
    /// Act III's levels say theirs by <see cref="HazardFloor"/>; levels 1 to 11 have none (plan §7:
    /// no lava or slime above the Sump).
    /// </summary>
    public string? PoolHazard { get; init; }

    /// <summary>
    /// The hazard the depth's floors hold (Act III, brief p14: slime at 12 and 14, lava at 13), or null:
    /// a pool's basin, a flooded gallery's water, a chasm's gap and the floor of its families' rooms
    /// that the route between their doors does not need, each under a hazard volume of its kind and
    /// the kind's glow. Levels 1 to 11 hold none.
    /// </summary>
    public HazardFloor? HazardFloor { get; init; }

    /// <summary>The theme of a depth: Act I's four themes at depths 1 to 4, Act II's at 6 to 9, Act III's at 11 to 14, the boss levels' at 5, 10 and 15.</summary>
    public static RoomTheme ForDepth(int depth) => depth switch
    {
        >= 1 and <= 4 => Themes.ActOne.All[depth - 1],
        5 => Themes.Bosses.Magazine,
        >= 6 and <= 9 => Themes.ActTwo.All[depth - 6],
        10 => Themes.Bosses.Bore,
        >= 11 and <= 14 => Themes.ActThree.All[depth - 11],
        15 => Themes.Bosses.Pit,
        _ => Grey,
    };
}

/// <summary>
/// The hazard a depth's floors hold (Act III, brief p14): a kind of <c>hazards.json</c>
/// (<paramref name="Kind"/>), drawn in <paramref name="Surface"/>: a water shader's material standing
/// <paramref name="Depth"/> deep (slime, <paramref name="Liquid"/>), or a lit brush material laid as a
/// non-solid skin two units thick on the floor (lava), lit at least <paramref name="MinLight"/>
/// (a self-lit material the preview draws by its lightmap alone). Over each the dresser hangs
/// <paramref name="Glow"/>, a light with no fixture, every <paramref name="GlowPitch"/>: the hazard's
/// own light. The dresser lays it on the floor of <paramref name="Families"/>' rooms that the route
/// between their doors, their markers and their blast ledges' take-off do not need; pools, flooded
/// galleries and, when <paramref name="Chasms"/>, a chasm leap's gap hold it as the shapes lay it.
/// </summary>
public sealed record HazardFloor(string Kind, string Surface, bool Liquid, int Depth, Lamp Glow, int GlowPitch, IReadOnlySet<string> Families)
{
    /// <summary>A chasm leap's gap holds it (level 13: "chasm leaps over lava").</summary>
    public bool Chasms { get; init; }

    /// <summary>A skin's <c>_minlight</c>: the floor of light a self-lit material keeps in the lightmap.</summary>
    public float MinLight { get; init; }

    /// <summary>
    /// The rock of low lumps (a step high at most) the dresser sets round the edge of each area it lays, half in and
    /// half out of it, so a pool reads as a pool in a cave floor, not a laid slab (brief p15: level 13's lava), or null.
    /// </summary>
    public Surface? Lips { get; init; }

    /// <summary>The most areas the dresser lays in one room.</summary>
    public int MaxAreas { get; init; } = 2;

    /// <summary>The least side of an area the dresser lays, in units.</summary>
    public int MinSide { get; init; } = 128;

    /// <summary>How far above the hazard's surface its volume reaches: a standing player's shins.</summary>
    public const int VolumeAbove = 16;

    /// <summary>A skin's thickness over the floor.</summary>
    public const int Skin = 2;

    /// <summary>The height of the surface over the floor: the liquid's depth, or the skin's.</summary>
    public int SurfaceHeight => Liquid ? Depth : Skin;
}

/// <summary>
/// Water standing on a room's floor (brief p4: level 8 is flooded; level 7's caves hold pools):
/// <paramref name="Depth"/> deep over the open floor of the room's body, kept <paramref name="FromDoors"/>
/// from every door's opening so no water touches a socket's plug, in the rooms of
/// <paramref name="Families"/> only (null: every room but the ways in and down).
/// </summary>
public sealed record StandingWater(int Depth, int FromDoors, IReadOnlySet<string>? Families)
{
    /// <summary>How far in from the room's body the water stops: 0 floods the floor, more leaves a pool in the middle.</summary>
    public int Inset { get; init; }
}

/// <summary>
/// A material a theme ships with the mod (under <c>materials/descent/</c>), made from a database's
/// <paramref name="BasedOn"/>: its palette row and its checks are that one's.
/// </summary>
public sealed record OwnMaterial(string Name, string BasedOn, string Vmt)
{
    /// <summary>
    /// A water that reads as water in an LDR map with no cubemap (brief p9): the scene reflected in it
    /// (<c>_rt_WaterReflection</c>, no cubemap needed; entities not reflected), what is under it refracted,
    /// a short fog of <paramref name="fogColour"/> so its depth tints, and TF2's normal map rippling.
    /// <paramref name="basedOn"/> is the TF2 water it stands for, whose underwater view it keeps.
    /// </summary>
    public static OwnMaterial Water(string name, string basedOn, string beneath, string fogColour, int fogEnd, string reflectTint, float refract = 0.25f) =>
        new(name, basedOn,
            $"\"Water\"\n{{\n\t\"%compilewater\" 1\n\t\"$abovewater\" 1\n\t\"$forceexpensive\" 1\n" +
            $"\t\"$reflecttexture\" \"_rt_WaterReflection\"\n\t\"$reflectamount\" \".6\"\n\t\"$reflecttint\" \"{reflectTint}\"\n\t\"$reflectentities\" 0\n" +
            $"\t\"$envmap\" \"env_cubemap\"\n\t\"$refracttexture\" \"_rt_WaterRefraction\"\n\t\"$refractamount\" \"{refract.ToString(CultureInfo.InvariantCulture)}\"\n" +
            $"\t\"$normalmap\" \"water/tfwater001_normal\"\n\t\"$bumpframe\" \"0\"\n\t\"$surfaceprop\" \"water\"\n\t\"$bottommaterial\" \"{beneath}\"\n" +
            $"\t\"$fogenable\" 1\n\t\"$fogcolor\" \"{{{fogColour}}}\"\n\t\"$fogstart\" \"0\"\n\t\"$fogend\" \"{fogEnd}\"\n" +
            "\t\"Proxies\"\n\t{\n\t\t\"AnimatedTexture\"\n\t\t{\n\t\t\t\"animatedtexturevar\" \"$normalmap\"\n\t\t\t\"animatedtextureframenumvar\" \"$bumpframe\"\n\t\t\t\"animatedtextureframerate\" 30.00\n\t\t}\n" +
            "\t\t\"TextureScroll\"\n\t\t{\n\t\t\t\"texturescrollvar\" \"$bumptransform\"\n\t\t\t\"texturescrollrate\" .05\n\t\t\t\"texturescrollangle\" 45.00\n\t\t}\n" +
            "\t\t\"WaterLOD\"\n\t\t{\n\t\t}\n\t}\n}\n");

    /// <summary>A copy of a lit brush material with no <c>$envmap</c>: the map tools' cubemap fix-up throws on one in a room with no <c>env_cubemap</c> (plan 1.3c).</summary>
    public static OwnMaterial Plain(string name, string basedOn, string baseTexture, string? bumpMap, string surfaceProp) =>
        new(name, basedOn,
            $"\"LightmappedGeneric\"\n{{\n\t\"$basetexture\" \"{baseTexture}\"\n" + (bumpMap is null ? "" : $"\t\"$bumpmap\" \"{bumpMap}\"\n") +
            $"\t\"$surfaceprop\" \"{surfaceProp}\"\n}}\n");
}

/// <summary>A band of <paramref name="Surface"/> through a wall from <paramref name="From"/> to <paramref name="To"/> above the floor.</summary>
public sealed record WallSeam(Surface Surface, int From, int To);

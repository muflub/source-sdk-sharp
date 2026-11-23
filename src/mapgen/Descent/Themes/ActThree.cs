namespace SourceSharp.MapGen.Descent.Themes;

/// <summary>
/// Act III's four themes (plan_descent_mine_levels.md §7, below, levels 11 to 14; brief p14). Below
/// the Bore the shaft runs into Gray Mann's works and then into hell: an abandoned factory of rusted
/// panels and grating under ceiling lamps half of which have failed, red emergency lamps in the dark
/// they leave; its waste level, sewer brick and green concrete round channels and pools of sick green
/// slime, bug zappers buzzing over them; black rock and lava under torches, the lava's own glow on the
/// rock; and the Rot, black mud walls and black-green slime, lit by a few glowing fungi. Each differs
/// from the level above in its colour, its light and its silhouette, and is darker (ruling M1); the
/// slime and lava are hazards (<see cref="HazardFloor"/>), never the only way between two doors.
/// </summary>
/// <remarks>
/// Every material and model here is a row of the asset database at <c>/home/lodle/assetdb</c>, chosen
/// by query and looked at; the depth's palette file says why each, and a fact holds every name here to
/// the database and to its palette. Model bounds are the database's mesh bounds.
/// </remarks>
public static class ActThree
{
    private static PropModel M(string path, float x0, float y0, float z0, float x1, float y1, float z1, bool isStatic = true, bool solid = true, int front = 0)
        => new($"models/{path}.mdl", new(x0, y0, z0), new(x1, y1, z1), isStatic, solid, front);

    /// <summary>A lamp hung on a long cord of steel cable in a tall room.</summary>
    private static LampKind Pendant(PropModel fixture, Lamp light, Point lightAt, int hang, int grid) =>
        new(fixture, LampMount.Pendant, light, Spacing: grid, LightAt: lightAt)
        {
            Cords = [ActOne.Cable128, ActOne.Cable64, ActOne.Cable32],
            WallHeight = hang,
            Grid = grid,
        };

    // Level 11: the robot works.
    public static PropModel FactoryLamp { get; } = M("props_spytech/hanging_ceiling_lamp", -16.29f, -31.29f, -3.89f, 15.29f, 0.29f, 32f);
    public static PropModel FactoryPendant { get; } = M("props_spytech/spytech_hanging_light_52", -16.29f, -31.29f, -3.89f, 15.29f, 0.29f, 48.11f);
    public static PropModel Console { get; } = M("props_spytech/control_room_console01", -40f, -33.87f, 0f, 40f, 28.63f, 66.86f, front: 90);
    public static PropModel Cabinet { get; } = M("props_spytech/computer_wall_cabinet", -45.27f, -25.72f, -1.54f, 3.09f, 25.72f, 112.91f);
    // Non-solid: its run along the top, 186 up, and its foot were standing places no one reaches (the reach gate's islands, brief p14).
    public static PropModel WallPipes { get; } = M("props_spytech/pipes_wall001", -1.02f, -44.53f, -69.47f, 98.68f, 22.93f, 117.01f, solid: false);
    public static PropModel BarrelCrate { get; } = M("props_mvm/barrel_crate", -40.33f, -39.84f, -0.03f, 41.99f, 39.84f, 116.72f);
    public static PropModel Tank { get; } = M("props_hydro/water_machinery2", -87.55f, -87.55f, 0.13f, 87.55f, 87.55f, 238.3f);
    public static PropModel Tanks { get; } = M("props_hydro/water_machinery4", -150.07f, -46.62f, 0f, 150.01f, 56.83f, 142.39f);
    public static PropModel Drum { get; } = M("props_mining/generator_pipe01", -63.97f, -63.97f, -88f, 63.97f, 63.97f, 88f);

    // Level 12: the sump.
    public static PropModel Zapper { get; } = M("props_swamp/bug_zapper", -11.89f, -11.89f, 0.01f, 11.89f, 11.89f, 36.37f);
    public static PropModel PipeElbow { get; } = M("props_island/parts/pipe_256", -50.66f, -37.6f, 0f, 50.66f, 37.6f, 255.96f);
    public static PropModel PipeRiser { get; } = M("props_island/parts/pipe_straight_384", -32.96f, -32.96f, 0f, 32.96f, 32.96f, 384f);
    public static PropModel Drain { get; } = M("props_farm/drain_pipe001", -6f, -27.62f, -27.62f, 4.68f, 27.62f, 27.62f);
    public static PropModel ChemicalBarrels { get; } = M("props_hydro/water_barrel_cluster", -36.82f, -37.42f, -0.01f, 38.02f, 37.48f, 112.26f);

    // Level 13: brimstone.
    public static PropModel Spire { get; } = M("props_halloween/halloween_rock006", -97.4f, -101.39f, 0.76f, 97.4f, 101.39f, 539.64f);
    public static PropModel Horn { get; } = M("props_halloween/halloween_rock009", -106.47f, -108.56f, 1.48f, 106.47f, 108.56f, 520.62f);
    public static PropModel Fang { get; } = M("props_halloween/halloween_rock001", -113.33f, -136.47f, -0.22f, 113.33f, 136.47f, 736.24f);
    public static PropModel Sconce { get; } = M("props_medieval/sconce", 0f, -6.29f, -10.78f, 16.58f, 6.42f, 18.68f, solid: false);

    // Level 14: the rot.
    public static PropModel Fungus { get; } = M("props_swamp/shroom_ref_01_cluster", -7.53f, -13.71f, -0.43f, 13.91f, 9.37f, 9.01f, solid: false);
    public static PropModel Pebbles { get; } = M("props_swamp/rock006c_swamp", -80.9f, -64.06f, -9.32f, 60f, 54.85f, 9.13f, solid: false);

    /// <summary>
    /// Level 11, Robot Works: Gray Mann's factory, abandoned and failing. Rusted orange panels over a
    /// band of dark perforated steel, steel grating for every deck, stair and catwalk, worn tile floors,
    /// steel sets and an overhead crane, consoles, cabinets and pipework on the walls, tanks and drums on
    /// the floor; spytech ceiling lamps of which half have failed, and red emergency lamps where they
    /// leave the floor dark; a dark red haze. Its way in is the second camp (M10).
    /// </summary>
    public static RoomTheme Works { get; } = WorksBase() with
    {
        Caves = WorksCaves(),
        CaveFamilies = new HashSet<string>(StringComparer.Ordinal) { "grotto", "flood" },
    };

    /// <summary>
    /// Level 11's caves: the grottos and flooded galleries the works broke into, drawn in the Bore's grey drilled rock,
    /// floored in poured concrete, rather than the factory's steel panels (brief p15); lit and dressed as the works.
    /// </summary>
    private static RoomTheme WorksCaves() => WorksBase() with
    {
        Floor = new("concrete/concretefloor003", 0.25f),
        Wall = new("nature/rockwall010b", 0.5f),
        Ceiling = new("nature/rockwall010b", 0.5f),
        LargeWall = new("nature/rockwall010b", 0.5f),
        WallLow = null,
    };

    private static RoomTheme WorksBase() => new(
        Floor: new("swamp/tile/floor_tile_002a_worn", 0.25f),
        Wall: new("metal/wall026", 0.25f),
        Ceiling: new("swamp/metal/swampmetal001", 0.25f),
        Block: new("metal/metalgrate011a", 0.25f),
        Plug: RoomTheme.PlugOf("works", new("metal/wall026", 0.25f)),
        Trigger: "TOOLS/TOOLSTRIGGER")
    {
        Name = "works",
        Trim = new("metal/ibeam001b", 0.25f),
        // A steel frame round each doorway: dark perforated posts, a steel head.
        FrameBrushes = new BrushFrame(new("swamp/metal/swampmetal001", 0.25f), new("metal/ibeam001b", 0.25f), PostWidth: 16, HeadDepth: 16, Proud: 12)
        {
            Flange = 20,
            FlangeDepth = 6,
            MinLight = 0.05f,
        },
        WallLow = new("swamp/metal/swampmetal001", 0.25f),
        WallBand = 192,
        LargeWall = new("metal/wall026", 0.5f),
        // Half the ceiling lamps have failed; the floor they leave dark takes the red emergency lamps.
        Lamps = new LampKind(FactoryLamp, LampMount.Ceiling, new Lamp("214 222 236", 47, 192, 768), Spacing: 256, LightAt: new(0, -15, -14))
        {
            Dead = 0.5f,
        },
        CoverLamps = new LampKind(Bosses.Siren, LampMount.Wall, new Lamp("255 40 26", 32, 128, 640), Spacing: 288, LightAt: new(10, 0, 0))
        {
            WallHeight = 176,
        },
        Pendants = Pendant(FactoryPendant, new Lamp("214 222 236", 41, 384, 1280), new(0, -15, -14), hang: 448, grid: 640) with { Dead = 0.5f },
        Gantry = new Gantry(Height: 704, Girder: 32, Hook: ActOne.CraneHook, Cable: ActOne.Cable64),
        Hazard = new("props/hazardstrip001a", 0.5f),
        Props = new PropSet
        {
            Wall = [Console, Cabinet, WallPipes],
            WallSpacing = 352,
            Corner = [BarrelCrate, Tank],
            CornerChance = 0.6f,
            Floor = [Tanks, Drum],
            FloorDensity = 1.2f,
            // Mann Co.'s crates and drums left on the line (M34: the breakables).
            Companions = [ActOne.OilDrum, ActOne.PowderCrate],
            CeilingRun = ActOne.TriplePipe,
            WallRunHeight = 448,
            Centrepiece = [Tank],
            Hung = [new(ActOne.WallVent, 224)],
            HungSpacing = 512,
            Sets = new SetFrame(Spacing: 384, Post: 16, Cap: 16),
        },
        Pilasters = new Pilasters(Spacing: 384, Width: 24, Depth: 16),
        // No skirting: behind a grotto's bulging rock its top was a standing place no one reaches (brief p14).
        Bands = new TrimBands(Skirting: 0, DadoHeight: 192, Dado: 12, Beam: 24, Belt: 16),
        RoofBeams = 384,
        // A dim cold fill under every roof, the factory's dead daylight panels' last glow, so the dark between the
        // lamps that work is grey steel, not black (brief p14: 26 % of the frame under luma 0.02 in game, 12 for 16).
        // Every lamp a sixth up in brief p15: in game 13.6 for 16 with the caves in rock (the gate's 19.6, calibrated 15.1).
        CoolFill = (new Lamp("82 88 104", 17, 448, 1280), 512),
        Fog = new Fog("26 14 12", 512, 1792, 0.65f),
        ToneMap = new ToneMap(1f, 0.5f, 3f, 0.4f),
        Wetness = 0.6f,
    };

    /// <summary>
    /// The Sump's slime: HL2's toxic slime texture (<c>nature/toxicslime002a</c>, in TF2's content as its DX7 fallback) as a
    /// lit skin over the floor, a little self-lit. As a water its fog drew a flat, luminous yellow-green sheet with no
    /// surface, the brightest thing in a room (brief p15); its fog darkened, it drew black.
    /// </summary>
    public static OwnMaterial SumpScum { get; } = OwnMaterial.Plain("descent/sump_scum", "nature/toxicslime002a_dx70", "Nature/toxicslime002a", null, "slime");

    /// <summary>
    /// Level 12, the Sump: the works' waste level. Sewer brick over a green concrete lining, a concrete
    /// vault, mud floors, rusted steel walkways and pipe-top pillars; pools and channels of sick green
    /// slime (a hazard) glowing, red pipes rising out of the floor and turning into the walls, drains,
    /// chemical barrels; bug zappers hung from the roof; a green haze.
    /// </summary>
    public static RoomTheme Sump { get; } = new(
        Floor: new("swamp/nature/swampmud003", 0.25f),
        Wall: new("swamp/brick/swampbrickwall001", 0.25f),
        Ceiling: new("swamp/concrete/swampcrete001", 0.25f),
        // Poured concrete for pillars, walkways and steps: in rusted diamond plate a pillar room's pillar read as a big green
        // steel drum (brief p15).
        Block: new("swamp/concrete/swampcrete001", 0.25f),
        Plug: RoomTheme.PlugOf("sump", new("swamp/brick/swampbrickwall001", 0.25f)),
        Trigger: "TOOLS/TOOLSTRIGGER")
    {
        Name = "sump",
        Trim = new("swamp/metal/swampmetal001", 0.25f),
        // A concrete culvert arch: haunched corners, a flange round it.
        FrameBrushes = new BrushFrame(new("swamp/concrete/swampcrete001", 0.25f), new("swamp/concrete/swampcrete001", 0.25f), PostWidth: 24, HeadDepth: 24, Proud: 16)
        {
            Fillet = 32,
            Flange = 16,
            FlangeDepth = 8,
            MinLight = 0.05f,
        },
        WallLow = new("swamp/concrete/swampcrete001", 0.25f),
        WallBand = 160,
        LargeWall = new("swamp/brick/swampbrickwall001", 0.5f),
        Extra = [SumpScum],
        HazardFloor = new HazardFloor("slime", SumpScum.Name, Liquid: false, Depth: 0, new Lamp("120 196 40", 26, 128, 512), GlowPitch: 256,
            new HashSet<string>(StringComparer.Ordinal) { "pit", "chimney", "cavern", "grotto", "stope", "chasm" })
        {
            MinLight = 0.12f,
        },
        Lamps = new LampKind(Zapper, LampMount.Ceiling, new Lamp("176 188 255", 36, 160, 768), Spacing: 320, LightAt: new(0, 0, -10)),
        CoverLamps = new LampKind(Zapper, LampMount.Ceiling, new Lamp("176 188 255", 33, 192, 900), Spacing: 320, LightAt: new(0, 0, -10)),
        Pendants = Pendant(ActOne.BellLamp, new Lamp("196 216 128", 40, 384, 1280), new(0, 0, -28), hang: 448, grid: 640),
        Props = new PropSet
        {
            Wall = [PipeElbow, PipeRiser, ActTwo.OpenDrum],
            WallSpacing = 384,
            Corner = [ChemicalBarrels, ActTwo.Debris],
            CornerChance = 0.6f,
            Floor = [PipeElbow],
            FloorDensity = 0.8f,
            // Open drums by the piles (M34: the breakables).
            Companions = [ActTwo.OpenDrum, ActTwo.RopePile],
            CeilingRun = ActOne.WhitePipe,
            WallRunHeight = 448,
            Hung = [new(Drain, 48)],
            HungSpacing = 384,
        },
        Pilasters = new Pilasters(Spacing: 384, Width: 16, Depth: 12),
        Bands = new TrimBands(Skirting: 0, DadoHeight: 0, Dado: 0, Beam: 16, Belt: 0),
        // A dim sick-green fill under every roof, so the dark between the zappers is green, not black (brief p14: 37 %
        // of the frame under luma 0.02 in game with the lamp off).
        CoolFill = (new Lamp("96 128 64", 18, 448, 1280), 512),
        Fog = new Fog("14 20 9", 448, 1728, 0.7f),
        ToneMap = new ToneMap(1f, 0.5f, 3f, 0.4f),
        Wetness = 0.85f,
    };

    /// <summary>
    /// Level 13, Brimstone: the diggings broke into the underworld. Black jagged rock, grey stone ledges and
    /// pillars, dark ash floors; lava (a hazard) in the chasms' gaps, the pools and the caves' floors,
    /// lighting the rock round it; black spires of rock; torches on stakes and braziers; a red haze. Its
    /// fill is lower than the Sump's, since its lava glows (§6).
    /// </summary>
    public static RoomTheme Brimstone { get; } = new(
        // A black ash floor under the grey-black rock, so floor and wall take the fire's red in two tones (brief p15: one rust red).
        Floor: new("nature/dirtfloor005b", 0.25f),
        Wall: new("nature/underworld_rockwall001", 0.25f),
        Ceiling: new("nature/underworld_rockwall001", 0.25f),
        // Dark rock veined with rust for ledges, stairs, pillars and arches (the plan's grey nightfall stone read as pale concrete under the lamp, brief p15).
        Block: new("nature/rockwall002_island", 0.25f),
        Plug: RoomTheme.PlugOf("brimstone", new("nature/underworld_rockwall001", 0.25f)),
        Trigger: "TOOLS/TOOLSTRIGGER")
    {
        Name = "brimstone",
        // A rough rock arch: thick jambs and head, haunched corners, a rough surround.
        FrameBrushes = new BrushFrame(new("nature/rockwall002_island", 0.25f), new("nature/rockwall002_island", 0.25f), PostWidth: 24, HeadDepth: 24, Proud: 24)
        {
            Fillet = 40,
            Flange = 24,
            FlangeDepth = 12,
            MinLight = 0.04f,
        },
        LargeWall = new("nature/underworld_rockwall001", 0.5f),
        // Lava: TF2's underworld lava (self-lit in game), kept bright in the lightmap for the preview.
        HazardFloor = new HazardFloor("lava", "nature/underworld_lava002", Liquid: false, Depth: 0, new Lamp("255 86 18", 68, 128, 640), GlowPitch: 256,
            new HashSet<string>(StringComparer.Ordinal) { "cavern", "grotto", "stope", "chimney", "pit" })
        {
            Chasms = true,
            MinLight = 0.6f,
            // Lumps of the black rock round each pool the dresser lays (brief p15: the lava read as laid slabs).
            Lips = new("nature/underworld_rockwall001", 0.25f),
        },
        Lamps = new LampKind(Bosses.Torch, LampMount.Floor, new Lamp("255 120 40", 130, 160, 768), Spacing: 448, LightAt: new(0, -5, 178))
        {
            Pools = (2, 3),
        },
        // An iron sconce on a wall where the torches leave the floor dark: a deck's, a pool's walkway.
        CoverLamps = new LampKind(Sconce, LampMount.Wall, new Lamp("255 110 40", 110, 160, 768), Spacing: 384, LightAt: new(12, 0, 26))
        {
            WallHeight = 128,
        },
        // Fire baskets hung on chains over a tall room's floor, where no wall's sconce reaches its middle.
        Pendants = Pendant(ActTwo.Brazier, new Lamp("255 110 40", 115, 384, 1280), new(0, 0, 48), hang: 448, grid: 768),
        Props = new PropSet
        {
            Wall = [Horn, ActTwo.Gargoyle],
            WallSpacing = 512,
            Corner = [ActTwo.BoulderPile, ActTwo.Boulder3],
            CornerChance = 0.6f,
            Floor = [Spire, ActTwo.Boulder2],
            FloorDensity = 1f,
            Companions = [ActTwo.Boulder3],
            Centrepiece = [Fang, Spire],
        },
        // A dim red glow under every roof, the lava's off the rock (brief p14: 53 % of the frame under luma 0.02 in game
        // with the lamp off, 7 for its 11).
        // Every lamp three fifths up in brief p15: the black ash floor took the level to 7.6 in game for 11 (gate 10.8), and two
        // fifths up read 9.1 (gate 12.9).
        CoolFill = (new Lamp("160 56 26", 46, 448, 1280), 512),
        Fog = new Fog("26 8 4", 448, 1664, 0.7f),
        ToneMap = new ToneMap(1f, 0.5f, 3f, 0.4f),
        Wetness = 0.3f,
    };

    /// <summary>The Rot's slime: the Pit's black-green slime (level 15's own water).</summary>
    public static OwnMaterial RotSlime { get; } =
        OwnMaterial.Water("descent/pit_slime", "water/water_underworld", "water/water_underworld_beneath", "14 24 9", 40, "[.3 .36 .3]", 0.1f);

    /// <summary>
    /// Level 14, the Rot: hell's cesspit. Mossy black-green rock over a band of black mud, green mud floors, pale karst
    /// rock for ledges and pillars; pools and channels of black-green slime (a hazard, the Pit's own) with
    /// a faint glow; drips hanging from the roofs; a few glowing fungi by the walls, almost no other light;
    /// a black-green haze.
    /// </summary>
    /// <summary>The Rot's mud floor: TF2's dark olive mud (<c>nature/mudfloor005a</c>) without its <c>$envmap</c>.</summary>
    public static OwnMaterial RotMud { get; } = OwnMaterial.Plain("descent/rot_mud", "nature/mudfloor005a", "Nature/mudfloor005a", "Nature/mudfloor005a_normal", "mud");

    /// <summary>The Rot's ledges and pillars: TF2's dark mossy rock (<c>nature/rockwall015a</c>) without its <c>$envmap</c>.</summary>
    public static OwnMaterial RotRock { get; } = OwnMaterial.Plain("descent/rot_rock", "nature/rockwall015a", "Nature/rockwall015a", "Nature/rockwall015a_normal", "rock");

    public static RoomTheme Rot { get; } = new(
        // Dark olive mud: the plan's blend drew its second layer, a litter of twigs, on a flat floor (brief p15: leaf litter).
        Floor: new(RotMud.Name, 0.25f),
        // Mossy wet rock, not black mud: in game the mud walls took no light even from the hero's lamp, and 40 % of
        // the frame stayed black at the base lamp (brief p14).
        Wall: new("nature/rockwall006a", 0.25f),
        Ceiling: new("nature/rockwall006a", 0.25f),
        // Dark mossy rock: the plan's karst drew as flat grey concrete under the lamp (brief p15).
        Block: new(RotRock.Name, 0.25f),
        Plug: RoomTheme.PlugOf("rot", new("nature/rockwall006a", 0.25f)),
        Trigger: "TOOLS/TOOLSTRIGGER")
    {
        Name = "rot",
        // A rough arch of slimy cobbles, haunched (the pale karst's glowed in the dark, brief p14's first build).
        FrameBrushes = new BrushFrame(new("swamp/brick/swampcobblewall001", 0.25f), new("swamp/brick/swampcobblewall001", 0.25f), PostWidth: 24, HeadDepth: 24, Proud: 24)
        {
            Fillet = 40,
            Flange = 24,
            FlangeDepth = 12,
            MinLight = 0.03f,
        },
        WallLow = new("swamp/nature/swampmud004", 0.25f),
        WallBand = 128,
        LargeWall = new("nature/rockwall006a", 0.5f),
        Extra = [RotSlime, RotMud, RotRock],
        HazardFloor = new HazardFloor("slime", RotSlime.Name, Liquid: true, Depth: 24, new Lamp("64 124 44", 30, 128, 512), GlowPitch: 320,
            new HashSet<string>(StringComparer.Ordinal) { "cavern", "grotto", "pit", "chimney", "stope", "chasm" }),
        Lamps = new LampKind(Fungus, LampMount.Floor, new Lamp("120 210 110", 54, 160, 900), Spacing: 448, LightAt: new(0, 0, 24))
        {
            Pools = (2, 3),
            // The middle of a cave no wall's fungi reach grows its own.
            FreeStanding = true,
        },
        // Bracket fungus on a wall where the floor's clusters leave the floor dark: a deck's, a pool's walkway.
        // At 128, over a pool's walkway's air (a ledge's 114 over its 64).
        CoverLamps = new LampKind(Fungus, LampMount.Wall, new Lamp("120 210 110", 50, 160, 900), Spacing: 384, LightAt: new(24, 0, 12))
        {
            WallHeight = 128,
        },
        Props = new PropSet
        {
            Wall = [ActTwo.RockSpire],
            WallSpacing = 512,
            Corner = [Pebbles, ActTwo.RockSlab],
            CornerChance = 0.6f,
            // Dark boulders (the pale karst read white in the fungi's light, brief p15).
            Floor = [ActTwo.Boulder2, ActTwo.Boulder3],
            FloorDensity = 1f,
            Companions = [Fungus],
            Hanging = [ActTwo.Stalactites, ActTwo.StalactitesSmall],
            HangingSpacing = 448,
        },
        // A faint black-green fill under every roof, so beyond the fungi the rock is a shape, not nothing (brief p14: 75 %
        // of the frame under luma 0.02 in game with the lamp off).
        CoolFill = (new Lamp("56 84 44", 9, 448, 1280), 512),
        Fog = new Fog("8 11 6", 384, 1600, 0.75f),
        ToneMap = new ToneMap(1f, 0.5f, 3f, 0.4f),
        Wetness = 0.95f,
    };

    /// <summary>Levels 11 to 14 in order.</summary>
    public static IReadOnlyList<RoomTheme> All { get; } = [Works, Sump, Brimstone, Rot];
}

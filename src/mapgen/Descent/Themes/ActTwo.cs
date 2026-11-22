namespace SourceSharp.MapGen.Descent.Themes;

/// <summary>
/// Act II's four themes (plan_descent_mine_levels.md §7, the breakthrough, levels 6 to 9; task A9,
/// brief p4). The mine breaks into raw cave, the cave floods, and below the water it opens into
/// something carved and buried. Each level differs from its neighbours in its dominant colour, its
/// kind of light and its silhouette, and is darker than the one above (ruling M1): grey rubble under
/// a cold grey glow with the miners' lanterns far apart and their work lamps hung on cords; pale
/// limestone in blue light from a string of caged bulbs, stalactites overhead; green-black water
/// standing on the floors under lamp posts, plank decks, rusted steel; carved stone, coffins and
/// cobwebs in candle and brazier light, the darkest of the act.
/// </summary>
/// <remarks>
/// Every material and model here is a row of the asset database at <c>/home/lodle/assetdb</c>, chosen
/// by query and looked at; the depth's palette file says why each, and a fact holds every name here to
/// the database and to its palette. Model bounds are the database's mesh bounds.
/// </remarks>
public static class ActTwo
{
    private static PropModel M(string path, float x0, float y0, float z0, float x1, float y1, float z1, bool isStatic = true, bool solid = true, int front = 0)
        => new($"models/{path}.mdl", new(x0, y0, z0), new(x1, y1, z1), isStatic, solid, front);

    // Level 6: the wrecked workings.
    public static PropModel DeadLantern { get; } = M("props_2fort/lantern001_off", -6.25f, -9.12f, 0f, 6.25f, 9.12f, 47.2f, solid: false);
    public static PropModel WorkLight { get; } = M("props_hydro/construction_light", -15.7f, -17.99f, -29.35f, 15.7f, 7.33f, 1.58f);
    public static PropModel Planks { get; } = M("props_coalmines/wood_debris2", -52f, -10.31f, -43.14f, 51.82f, 1.4f, 40.06f, front: 270, solid: false);
    public static PropModel Boards { get; } = M("props_coalmines/wood_debris4", -62.47f, -5.32f, -60.45f, 62.47f, 5.32f, 60.45f, front: 90, solid: false);
    public static PropModel Barrier { get; } = M("props_mining/wooden_barrier01", -71.3f, -7.64f, -104f, 69.03f, 17.25f, 122.5f, front: 270, solid: false);
    public static PropModel BoulderPile { get; } = M("props_coalmines/boulder_pile1", -37.02f, -50.83f, -1.01f, 54.02f, 42.83f, 36.51f, solid: false);
    public static PropModel RubbleSpill { get; } = M("props_coalmines/boulder_pile2", -70.24f, 0.35f, -0.91f, 75.34f, 44.01f, 21.91f, solid: false);
    public static PropModel Boulder1 { get; } = M("props_coalmines/boulder1", -52.85f, -56.3f, -0.51f, 49.85f, 51.3f, 102.01f);
    public static PropModel Boulder2 { get; } = M("props_coalmines/boulder2", -33.95f, -42.62f, -1.08f, 35.95f, 40.62f, 89.58f);
    public static PropModel Boulder3 { get; } = M("props_coalmines/boulder3", -29.68f, -45.59f, -1.73f, 29.68f, 45.59f, 59.73f);
    public static PropModel Collapse { get; } = M("props_mining/rock005", -135.08f, -143.18f, 0f, 135.08f, 143.18f, 379.12f);

    // Level 7: the dripstone cave.
    public static PropModel CageLight { get; } = M("props_c17/light_cagelight02_on", -0.03f, -2.9f, -10.5f, 7.2f, 2.9f, 1.15f, solid: false);
    public static PropModel RockSpire { get; } = M("props_mining/rock002", -98.24f, -129.1f, 0f, 98.24f, 129.1f, 278.44f);
    public static PropModel RockPillar { get; } = M("props_mining/rock001", -149.16f, -177.15f, 0f, 149.16f, 177.15f, 289.68f);
    public static PropModel RockSlab { get; } = M("props_mining/rock006b", -137.15f, -125.69f, -13.09f, 159.85f, 161.61f, 16.98f, solid: false);
    public static PropModel RockFlat { get; } = M("props_mining/rock006c", -64.06f, -60f, -9.32f, 54.85f, 80.9f, 9.13f, solid: false);
    public static PropModel RockLow { get; } = M("props_mining/rock006", -107.47f, -123.21f, -17.24f, 91.11f, 124.88f, 34.57f, solid: false);
    public static PropModel Stalactites { get; } = M("props_island/stalagtites_med", -110.97f, -124.66f, -185.87f, 110.97f, 124.66f, 185.87f, solid: false);
    public static PropModel StalactitesSmall { get; } = M("props_island/stalagtites_small", -75.46f, -142.87f, -107.53f, 75.46f, 142.87f, 107.53f, solid: false);

    // Level 8: the flooded galleries.
    public static PropModel LampPost { get; } = M("props_swamp/lamp_post001", -8.51f, -8.52f, -0.34f, 8.53f, 8.52f, 91.15f);
    public static PropModel HandPump { get; } = M("props_2fort/waterpump001", -45.82f, -10f, 0f, 18.09f, 10f, 96.25f);
    public static PropModel Grate { get; } = M("props_trainyard/drainage_grate01", -64.35f, -4f, -60.31f, 64.32f, 4f, 59.62f, solid: false, front: 90);
    public static PropModel OpenDrum { get; } = M("props_swamp/oildrum_open", -16f, -16f, 0f, 16f, 16f, 55f);
    public static PropModel KarstLow { get; } = M("props_swamp/karst_rock_05", -89.13f, -87.77f, -0.82f, 91.95f, 162.28f, 50.23f);
    public static PropModel Karst { get; } = M("props_swamp/karst_rock_01", -128.42f, -45.91f, -1.48f, 118.34f, 94.05f, 112.95f);
    public static PropModel RowBoat { get; } = M("props_swamp/row_boat_ref", -104.88f, -42.06f, -15.02f, 112.57f, 42.06f, 17.42f);
    public static PropModel SwampCrate { get; } = M("props_swamp/crate_ref", -26.01f, -18.01f, 0f, 26f, 18.27f, 10.07f);
    public static PropModel RopePile { get; } = M("props_swamp/ropepile", -19.8f, -19.24f, 0f, 25.71f, 19.77f, 16.21f);
    public static PropModel Debris { get; } = M("props_coalmines/wood_debris3", -50.62f, -68.84f, -49.5f, 86.62f, 72.84f, 37.5f, solid: false);

    // Level 9: the buried chapel.
    public static PropModel Candelabra { get; } = M("props_manor/wall_candelabra_01", 0f, -8.88f, -8.88f, 25.8f, 8.88f, 38.82f, solid: false);
    public static PropModel Candles { get; } = M("props_halloween/candle_cluster", -16.42f, 2.69f, -0.07f, 9.89f, 23.67f, 33.03f, solid: false);
    public static PropModel Brazier { get; } = M("props_medieval/brazier", -14.86f, -14.2f, 0.03f, 14.86f, 14.86f, 39.19f);
    public static PropModel Chandelier { get; } = M("props_manor/chandelier_01", -46.95f, -46.95f, 0.95f, 46.95f, 46.95f, 87.88f, solid: false);
    public static PropModel Coffin { get; } = M("props_viaduct_event/coffin_simple_closed", -26.4f, -29.99f, 0.05f, 26.4f, 0.03f, 116.58f, front: 270);
    public static PropModel Headstone { get; } = M("props_manor/gravestone_04", -14.08f, -42.38f, 0f, 14.08f, 42.38f, 77.44f);
    public static PropModel Slab { get; } = M("props_manor/gravestone_01", -6.08f, -25.25f, 0f, 6.08f, 25.3f, 62.29f);
    public static PropModel Monument { get; } = M("props_manor/gravestone_07", -30f, -30f, 0f, 30f, 30f, 150f);
    public static PropModel Monument2 { get; } = M("props_manor/gravestone_08", -33.21f, -33.26f, 0f, 33.31f, 33.26f, 160.16f);
    public static PropModel Bier { get; } = M("props_manor/coffin_01", -62.55f, -27.97f, 0f, 62.55f, 27.97f, 28.5f);
    public static PropModel Statue { get; } = M("props_c17/gravestone_statue001a", -36.15f, -35.22f, -93.05f, 36.15f, 35.22f, 120.93f);
    public static PropModel Gargoyle { get; } = M("props_manor/gargoyle_01", -41.09f, -32.41f, -0.93f, 41.09f, 32.41f, 111.13f);
    public static PropModel Cobweb { get; } = M("props_halloween/cobweb_01", 0f, -24f, -24f, 0f, 24f, 24f, solid: false);
    public static PropModel Cobweb2 { get; } = M("props_halloween/cobweb_02", 0f, -24f, -24f, 0f, 24f, 24f, solid: false);

    /// <summary>A lamp hung on a long cord of steel cable in a tall room.</summary>
    private static LampKind Pendant(PropModel fixture, Lamp light, Point lightAt, int hang, int grid) =>
        new(fixture, LampMount.Pendant, light, Spacing: grid, LightAt: lightAt)
        {
            Cords = [ActOne.Cable128, ActOne.Cable64, ActOne.Cable32],
            WallHeight = hang,
            Grid = grid,
        };

    /// <summary>The cave families: raw rock, no lining, no trim bands (level 6 is half mine, half cave).</summary>
    public static IReadOnlySet<string> CaveFamilies { get; } = new HashSet<string>(StringComparer.Ordinal) { "cavern", "grotto", "chasm", "chimney" };

    /// <summary>
    /// Level 6, the Breakthrough: the miners broke into a natural cave and left. Grey layered rock
    /// over a dark wet band at the foot, dark rubble floors, burnt timber sets along the drifts (some fallen
    /// in), level 4's kit left behind (fuse boxes, hazard drums), boulders and spilled rubble; a cold
    /// grey glow under the roof, the miners' lanterns in pools far apart, their work lamps hung on
    /// cords in the tall rooms and their dead lanterns on the walls; grey haze.
    /// </summary>
    public static RoomTheme Breakthrough { get; } = new(
        Floor: new("nature/blendgroundtogravel007", 0.25f),
        Wall: new("winter/rockwall014", 0.25f),
        Ceiling: new("winter/rockwall014", 0.25f),
        Block: new("wood/wood_wall006", 0.25f),
        Plug: RoomTheme.PlugOf("breakthrough", new("winter/rockwall014", 0.25f)),
        Trigger: "TOOLS/TOOLSTRIGGER")
    {
        Name = "breakthrough",
        Trim = new("cp_manor/wood_beam03_burnt", 0.25f),
        // A broken timber set: posts and a cap, the head cracked and sagging, the cap gone past one post.
        FrameBrushes = new BrushFrame(new("cp_manor/wood_beam03_burnt", 0.25f), new("cp_manor/wood_beam03_burnt", 0.25f), PostWidth: 16, HeadDepth: 16, Proud: 16)
        {
            CapOverhang = 24,
            Sag = 12,
            // Each Act II frame's floor of light is about one and a half times its level's walls' (brief p9:
            // at Act I's 0.35 the arches glowed, three to eight times their walls at levels 7 to 9).
            MinLight = 0.25f,
        },
        WallLow = new("nature/rockwall002_island", 0.25f),
        WallBand = 160,
        LargeWall = new("winter/rockwall014", 0.5f),
        // About a tenth under the first pass's (brief p9: the view figure read level 6 at 35 against 32).
        CoolFill = (new Lamp("150 166 200", 27, 448, 1280), 512),
        Lamps = new LampKind(ActOne.Lantern, LampMount.Floor, new Lamp("255 186 120", 180, 192, 900), Spacing: 448, LightAt: new(0, 0, 40))
        {
            Stand = ActOne.PowderCrate,
            Pools = (2, 3),
        },
        CoverLamps = new LampKind(ActOne.Lantern, LampMount.Wall, new Lamp("255 186 120", 135, 192, 900), Spacing: 320, LightAt: new(0, 0, 40))
        {
            WallHeight = 128,
        },
        Pendants = Pendant(WorkLight, new Lamp("206 214 230", 58, 384, 1280), new(0, -4, -24), hang: 448, grid: 640),
        Props = new PropSet
        {
            Wall = [Planks, Boards, Barrier, ActOne.FuseBox],
            WallSpacing = 352,
            Corner = [BoulderPile, Boulder3],
            CornerChance = 0.7f,
            Floor = [Boulder2, Boulder1, RubbleSpill],
            FloorDensity = 1.5f,
            Companions = [ActOne.HazardBarrel, ActOne.OilDrum, RubbleSpill],
            Centrepiece = [Collapse],
            Hung = [new(DeadLantern, 136)],
            HungSpacing = 512,
            // Timber sets along the drifts, about one in three fallen in (a post left, its cap gone).
            Sets = new SetFrame(Spacing: 320, Post: 16, Cap: 24) { Broken = 0.35f },
        },
        // Far rooms fade to dark, not to a flat grey panel in each doorway (brief p9, tried in game at
        // 58 60 66 / 384-2304 / 0.5, 28 29 32 / 512-3072 / 0.5 and 20 21 24 / 768-4096 / 0.6): a fog near
        // the darks of the level, starting later; it still closes in from level 4's.
        Fog = new Fog("22 23 27", 640, 2400, 0.55f),
        ToneMap = new ToneMap(1f, 0.5f, 3f, 0.35f),
        Wetness = 0.45f,
        DynamicBudget = 8,
    };

    /// <summary>
    /// Level 7, Dripstone: pure cave, wet limestone. Pale grey rock over a darker wet foot, black wet
    /// floors with pools standing in the caves, stalactites hanging in the tall rooms and rock spires
    /// standing, a string of caged bulbs along the walls in blue-white, a cold blue glow, blue haze.
    /// </summary>
    public static RoomTheme Dripstone { get; } = new(
        Floor: new("nature/dirtfloor005b", 0.25f),
        Wall: new("nature/blendrockgroundwallisland_interior02", 0.25f),
        Ceiling: new("nature/blendrockgroundwallisland_interior02", 0.25f),
        Block: new("nature/rockwall001_island", 0.25f),
        Plug: RoomTheme.PlugOf("dripstone", new("nature/blendrockgroundwallisland_interior02", 0.25f)),
        Trigger: "TOOLS/TOOLSTRIGGER")
    {
        Name = "dripstone",
        // A natural rock arch: thick rock jambs and head, haunched corners, a rough surround.
        FrameBrushes = new BrushFrame(new("nature/rockwall001_island", 0.25f), new("nature/rockwall001_island", 0.25f), PostWidth: 24, HeadDepth: 24, Proud: 24)
        {
            Fillet = 40,
            Flange = 24,
            FlangeDepth = 12,
            MinLight = 0.11f,
        },
        WallLow = new("nature/cliffface002a", 0.25f),
        WallBand = 128,
        LargeWall = new("nature/blendrockgroundwallisland_interior02", 0.5f),
        // TF2's well water reflecting the cave, no cubemap needed (brief p9: the pools read as flat dark
        // sheets over the black floor).
        Water = "descent/dripstone_water",
        Extra = [OwnMaterial.Water("descent/dripstone_water", "water/water_well", "water/water_well_beneath", "24 38 53", 80, "[.6 .66 .75]", 0.2f)],
        // Pools in the middle of the caves, not their whole floor (a cave floor is not a flood).
        Standing = new StandingWater(12, 160, CaveFamilies) { Inset = 224 },
        // One fill a cell, not one every 512 (160 lights a level).
        CoolFill = (new Lamp("120 150 214", 42, 640, 1600), 1024),
        // One row of bulbs, two thirds as many as the first pass's and brighter (brief p9: level 7 spent
        // 764 to 816 map entities, 482 of them these bulbs' lights in rows every 640 up the tall walls).
        Lamps = new LampKind(CageLight, LampMount.Wall, new Lamp("186 210 255", 48, 224, 900), Spacing: 448, LightAt: new(12, 0, -5))
        {
            WallHeight = 176,
        },
        // A caged bulb hung from the roof where the walls' string leaves a cave's middle dark.
        CoverLamps = new LampKind(CageLight, LampMount.Ceiling, new Lamp("186 210 255", 44, 256, 900), Spacing: 288, LightAt: new(4, 0, -14)),
        Pendants = Pendant(CageLight, new Lamp("186 210 255", 44, 384, 1280), new(4, 0, -14), hang: 448, grid: 640),
        Props = new PropSet
        {
            Wall = [RockSpire, RockLow],
            WallSpacing = 448,
            Corner = [RockSlab, RockFlat],
            CornerChance = 0.6f,
            Floor = [RockPillar, RockLow],
            FloorDensity = 1f,
            Hanging = [Stalactites, StalactitesSmall],
            HangingSpacing = 384,
        },
        Fog = new Fog("16 20 28", 576, 2240, 0.6f),
        ToneMap = new ToneMap(1f, 0.5f, 3f, 0.35f),
        Wetness = 0.55f,
    };

    /// <summary>
    /// Level 8, the Sunken Galleries: the flooded level. Mossy dark rock over a green concrete lining,
    /// mud floors under standing murky water, plank decks and walkways, rusted steel, hand pumps and
    /// drain grates, a wrecked boat; dim green lamp posts and the water's own green; green fog.
    /// </summary>
    public static RoomTheme Sunken { get; } = new(
        // Wet grey rock, the bottom the standing water and the lamp show (brief p9: the water over the mud
        // read in game as a mud floor); a copy of nature/rockfloor003a without its $envmap.
        Floor: new("descent/sunken_bed", 0.25f),
        Wall: new("nature/rockwall006a", 0.25f),
        Ceiling: new("nature/rockwall006a", 0.25f),
        Block: new("swamp/wood/swampwall015", 0.25f),
        Plug: RoomTheme.PlugOf("sunken", new("nature/rockwall006a", 0.25f)),
        Trigger: "TOOLS/TOOLSTRIGGER")
    {
        Name = "sunken",
        Trim = new("swamp/metal/swampmetal001", 0.25f),
        // A rusted steel flood-door frame: a heavy channel round the opening and steel plates on the sill.
        FrameBrushes = new BrushFrame(new("swamp/metal/swampmetal001", 0.25f), new("swamp/metal/swampmetal001", 0.25f), PostWidth: 16, HeadDepth: 24, Proud: 16)
        {
            Flange = 24,
            FlangeDepth = 8,
            Crossing = new("swamp/metal/swampmetal001", 0.25f),
            MinLight = 0.12f,
        },
        WallLow = new("swamp/concrete/swampcrete001", 0.25f),
        WallBand = 192,
        LargeWall = new("nature/rockwall006a", 0.5f),
        // Murky green water that reflects the gallery (no cubemap needed) over a wet rock bed the lamp lights
        // (brief p9: water_murky's brown fog over the mud read in game as a mud floor; tried in game with
        // a gravel bed and no reflection, and with water_murky's own fog).
        Water = "descent/sunken_water",
        Extra =
        [
            OwnMaterial.Water("descent/sunken_water", "swamp/water/water_murky", "swamp/water/water_murky_beneath", "30 44 30", 64, "[.6 .7 .6]"),
            OwnMaterial.Plain("descent/sunken_bed", "nature/rockfloor003a", "Nature/rockfloor003a", "Nature/rockfloor003a_normal", "concrete"),
        ],
        Standing = new StandingWater(16, 160, null),
        CoolFill = (new Lamp("104 132 96", 17, 448, 1280), 512),
        Lamps = new LampKind(LampPost, LampMount.Floor, new Lamp("190 214 140", 86, 192, 768), Spacing: 384, LightAt: new(0, 0, 74)),
        // A bell lamp under the roof where the posts by the walls leave a room's middle dark, and on long
        // cables over the tall galleries' water.
        CoverLamps = new LampKind(ActOne.BellLamp, LampMount.Ceiling, new Lamp("170 200 130", 46, 256, 900), Spacing: 384, LightAt: new(0, 0, -28)),
        Pendants = Pendant(ActOne.BellLamp, new Lamp("170 200 130", 46, 384, 1280), new(0, 0, -28), hang: 448, grid: 640),
        Bands = new TrimBands(Skirting: 0, DadoHeight: 0, Dado: 0, Beam: 16, Belt: 0),
        Pilasters = new Pilasters(Spacing: 384, Width: 16, Depth: 12),
        Props = new PropSet
        {
            Wall = [HandPump, OpenDrum],
            WallSpacing = 448,
            Corner = [KarstLow, Debris],
            CornerChance = 0.6f,
            Floor = [Karst, RowBoat, SwampCrate],
            FloorDensity = 1.2f,
            Companions = [OpenDrum, RopePile, SwampCrate],
            Hung = [new(Grate, 96)],
            HungSpacing = 448,
            Centrepiece = [RowBoat],
        },
        Fog = new Fog("16 22 16", 512, 2048, 0.65f),
        ToneMap = new ToneMap(1f, 0.5f, 3f, 0.4f),
        Wetness = 0.7f,
    };

    /// <summary>
    /// Level 9, the Buried Chapel: the cave opens into crypts older than the town. Cut stone walls over
    /// a burnt timber wainscot, a dark stone vault, cobbled floors, carved stone columns and arches;
    /// upright coffins, headstones and monuments, cobwebs; candelabra on the walls, candles on the
    /// floor, chandeliers on chains in the naves and braziers in the large rooms, orange in black.
    /// </summary>
    public static RoomTheme Chapel { get; } = new(
        Floor: new("koth_viaduct_event/viaduct_cobblestone001", 0.25f),
        Wall: new("medieval/stonewall001a", 0.25f),
        Ceiling: new("medieval/cobblestone002", 0.25f),
        Block: new("medieval/stonetrim001a", 0.25f),
        Plug: RoomTheme.PlugOf("chapel", new("medieval/stonewall001a", 0.25f)),
        Trigger: "TOOLS/TOOLSTRIGGER")
    {
        Name = "chapel",
        Trim = new("medieval/stonetrim001a", 0.25f),
        // A carved stone arch: jambs on plinths, haunched corners, a keystone over the head.
        FrameBrushes = new BrushFrame(new("medieval/stonetrim001a", 0.25f), new("medieval/stonetrim001a", 0.25f), PostWidth: 24, HeadDepth: 24, Proud: 16)
        {
            Fillet = 32,
            Keystone = 24,
            Plinth = 24,
            Flange = 16,
            FlangeDepth = 8,
            MinLight = 0.06f,
        },
        WallLow = new("cp_manor/wood_wall002_burnt", 0.25f),
        WallBand = 128,
        LargeWall = new("medieval/stonewall001a", 0.5f),
        Lamps = new LampKind(Candelabra, LampMount.Wall, new Lamp("255 150 70", 44, 128, 768), Spacing: 384, LightAt: new(24, 0, 44))
        {
            WallHeight = 112,
            RowPitch = 512,
        },
        // A chandelier under the vault where the sconces leave the floor dark.
        CoverLamps = new LampKind(Chandelier, LampMount.Ceiling, new Lamp("255 156 76", 38, 256, 900), Spacing: 384, LightAt: new(0, 0, 30)),
        Pendants = Pendant(Chandelier, new Lamp("255 160 80", 44, 256, 1024), new(0, 0, 30), hang: 384, grid: 640),
        WorkLamps = new WorkLamps(new LampKind(Brazier, LampMount.Floor, new Lamp("255 120 50", 70, 192, 900), Spacing: 0, LightAt: new(0, 0, 48)), Count: 2, MinFloor: 500_000),
        // Candle clusters burning on the floor by the walls, in pools along every room's route and round
        // the naves' walls, so the crypts read as candle pools in the black and not as a fill (brief p9:
        // level 9 from 17 to about 20 in game).
        RoutePools = new LampKind(Candles, LampMount.Floor, new Lamp("255 140 60", 70, 128, 640), Spacing: 288, LightAt: new(-3, 13, 34))
        {
            Pools = (3, 5),
        },
        Pilasters = new Pilasters(Spacing: 384, Width: 32, Depth: 16, MinHeight: 512),
        Bands = new TrimBands(Skirting: 0, DadoHeight: 128, Dado: 12, Beam: 24, Belt: 16),
        Props = new PropSet
        {
            Wall = [Coffin, Headstone, Slab],
            WallSpacing = 288,
            Corner = [Monument, Monument2],
            CornerChance = 0.6f,
            Floor = [Bier, Monument],
            FloorDensity = 1.5f,
            // Burnt-down candles left by the tombs, and grave slabs.
            Companions = [Candles, Slab, Bier],
            Hung = [new(Cobweb, 208), new(Cobweb2, 176)],
            HungSpacing = 320,
            Centrepiece = [Statue, Gargoyle],
        },
        Fog = new Fog("12 10 9", 448, 1856, 0.7f),
        ToneMap = new ToneMap(1f, 0.5f, 3f, 0.4f),
        Wetness = 0.75f,
    };

    /// <summary>Levels 6 to 9 in order.</summary>
    public static IReadOnlyList<RoomTheme> All { get; } = [Breakthrough, Dripstone, Sunken, Chapel];
}

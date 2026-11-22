namespace SourceSharp.MapGen.Descent.Themes;

/// <summary>
/// Act I's four themes (plan_descent_mine_levels.md §7, the Mann Co. mine,
/// levels 1 to 4; task A9). Each level differs from the one before in its
/// dominant colour, its lamp and its clutter, and is darker (ruling M1):
/// pale painted concrete under close electric lamps; blue-grey corrugated
/// steel over rock and gravel under white floods, with track on every
/// door's axis; red rock and timber sets in pools of lantern light with the
/// dark between; dark steel over machine-green paint, hazard stripes and
/// black coal under a few sodium lamps and one cold work light, in dust.
/// </summary>
/// <remarks>
/// Every material and model here is a row of the asset database at
/// <c>/home/lodle/assetdb</c>, chosen by query and looked at; the depth's
/// palette file (<c>Themes/palettes/dNN.tsv</c>) says why each, and a fact
/// holds every name here to the database and to its palette. Model bounds
/// are the database's mesh bounds. Texture scales keep a whole number of
/// repeats along a 1024 cell.
/// </remarks>
public static class ActOne
{
    //-------------------------------------------------------------------------
    // Models (mesh bounds from model_view).
    //-------------------------------------------------------------------------

    private static PropModel M(string path, float x0, float y0, float z0, float x1, float y1, float z1, bool isStatic = true, bool solid = true, int front = 0)
        => new($"models/{path}.mdl", new(x0, y0, z0), new(x1, y1, z1), isStatic, solid, front);

    // Lamps.
    public static PropModel CeilingLamp { get; } = M("props_mining/industrial_ceiling_lamp", -15.36f, -14.91f, -4.28f, 14.41f, 14.87f, 44f);
    public static PropModel Bulkhead { get; } = M("props_lights/walllight001a", 0.01f, -13.69f, -5.13f, 6.91f, 13.79f, 5.11f, front: 180);
    public static PropModel Flood { get; } = M("props_lights/flood_light_left001", -11f, -12f, -32.4f, 13f, 12f, 3f);
    public static PropModel Lantern { get; } = M("props_2fort/lantern001", -9.12f, -6.25f, 0f, 9.12f, 6.25f, 47.2f, isStatic: false);
    public static PropModel Gooseneck { get; } = M("props_gameplay/security_fence_light01", -18.47f, -84.25f, -17.67f, 18.46f, 1.73f, 30f, front: 270);
    public static PropModel CagedLamp { get; } = M("props_badlands/siloroom_light2", -5.86f, -5.86f, -22.59f, 5.86f, 5.86f, -0.01f);
    public static PropModel HangingBulb { get; } = M("props_lights/hangingbulb", -3.71f, -3.71f, -7.56f, 3.71f, 3.71f, 14.54f);
    public static PropModel BellLamp { get; } = M("props_c17/light_industrialbell01_on", -13.8f, -14.5f, -22.85f, 13.8f, 14.5f, -0.01f);
    public static PropModel Cable128 { get; } = M("props_island/gantry_support_cable_128", -2.01f, -2.01f, 0f, 2.01f, 2.01f, 128f, solid: false);
    public static PropModel Cable64 { get; } = M("props_island/gantry_support_cable_64", -2.01f, -2.01f, 0f, 2.01f, 2.01f, 64f, solid: false);
    public static PropModel CraneHook { get; } = M("props_mining/cranehook001", -18f, -21.48f, -0.03f, 18f, 21.48f, 93f);
    public static PropModel OreHeap { get; } = M("props_mining/rock004", -107.59f, -123.4f, 0f, 107.59f, 123.4f, 221.18f);
    public static PropModel GeneratorMachine { get; } = M("props_mining/generator_machine01", -53.97f, -105.09f, 0f, 54.03f, 128.77f, 114.86f);
    public static PropModel Cable32 { get; } = M("props_island/gantry_support_cable_32", -2.01f, -2.01f, 0f, 2.01f, 2.01f, 32f, solid: false);

    /// <summary>A lamp hung on a long cord of steel cable in a hall (the review of 2026-11-10: tall rooms lacked overhead interest).</summary>
    private static LampKind Pendant(PropModel fixture, Lamp light, Point lightAt, int hang, int grid) =>
        new(fixture, LampMount.Pendant, light, Spacing: grid, LightAt: lightAt)
        {
            Cords = [Cable128, Cable64, Cable32],
            WallHeight = hang,
            Grid = grid,
        };

    // Clutter.
    public static PropModel Lockers { get; } = M("props_gameplay/uniform_locker", -13.33f, -103.07f, 0f, 30.02f, 93.63f, 101.11f);
    public static PropModel FuseBoxes { get; } = M("props_mining/junctionbox03", -0.37f, -51.15f, 0f, 10.69f, 7.82f, 108.41f);
    public static PropModel FuseBox { get; } = M("props_mining/junctionbox02", 0f, -15.73f, 0f, 10.68f, 18.75f, 92.06f);
    public static PropModel ControlPanel { get; } = M("props_mining/computer_industrial04", 0f, -24f, 0f, 3.79f, 24f, 112f);
    public static PropModel DangerSign { get; } = M("props_mining/sign001", -7.22f, -24.39f, 0f, 7.22f, 24.39f, 66.99f);
    public static PropModel PowderCrate { get; } = M("props_2fort/miningcrate002", -25.33f, -22.5f, 0f, 26.43f, 23.09f, 43f);
    public static PropModel CementSacks { get; } = M("props_mvm/sack_stack", -33.21f, -38.52f, -1.21f, 33.36f, 34.4f, 69.99f);
    public static PropModel CableSpool { get; } = M("props_farm/spool_wire", -32f, -32f, -24f, 32f, 32f, 24f);
    public static PropModel Pallet { get; } = M("props_farm/pallet001", -40.64f, -46.98f, -4.91f, 41.84f, 46.98f, 8.18f);
    public static PropModel WhitePipe { get; } = M("props_mining/industrial_pipe01_256_01", 0f, -16.08f, -16.08f, 255.99f, 16.08f, 16.08f);
    public static PropModel Sheeting { get; } = M("props_2fort/corrugated_metal001", -0.5f, -24.97f, 0f, 8.54f, 23f, 95.95f);
    public static PropModel HandTruck { get; } = M("props_well/hand_truck01", -18.2f, -14f, 0f, 18.2f, 18.2f, 84.48f);
    public static PropModel RopeSpool { get; } = M("props_farm/spool_rope", -32f, -32f, -24f, 32f, 32f, 24f);
    public static PropModel OilDrum { get; } = M("props_2fort/oildrum", -16f, -16f, 0f, 16f, 16f, 55f);
    public static PropModel BigCrate { get; } = M("props_lakeside/wood_crate_01", -50f, -50.45f, -2f, 50.25f, 50f, 98.91f);
    public static PropModel OreCart { get; } = M("props_vehicles/mining_car_metal", -23.66f, -41.54f, 0f, 23.66f, 41.54f, 68.33f);
    public static PropModel Track { get; } = M("props_mining/track_straight_256", -28.26f, -127.45f, -1.59f, 28.19f, 127.45f, 5.09f, solid: false);
    public static PropModel Lagging { get; } = M("props_mining/support_wall001a", -0.08f, -64f, 0f, 12f, 64f, 248f);
    public static PropModel Barrel { get; } = M("props_trainyard/distillery_barrel001", -35f, -35f, -45f, 35f, 35f, 45f);
    public static PropModel HalfCrate { get; } = M("props_hydro/barrel_crate_half", -40.33f, -39.84f, -0.12f, 41.99f, 39.84f, 76.83f);
    public static PropModel WoodPile { get; } = M("props_farm/wood_pile", -25.53f, -86.91f, -14f, 25.53f, 86.91f, 14f);
    public static PropModel Welder { get; } = M("props_farm/welding_machine01", -20.17f, -27.43f, -0.07f, 20.17f, 25.9f, 70.05f);
    public static PropModel HazardBarrel { get; } = M("props_badlands/barrel01", -28.11f, -28.11f, 0f, 28.11f, 28.11f, 80.01f);
    public static PropModel RadBarrel { get; } = M("props_badlands/barrel03", -26.32f, -26.34f, 0f, 26.36f, 26.34f, 80.45f);
    public static PropModel DuctBend { get; } = M("props_mining/factory_pipe003", -78.23f, -80.28f, -108f, 77.65f, 79.72f, 108f);
    public static PropModel Generator { get; } = M("props_mining/generator01", -115.8f, -115.8f, -64f, 115.8f, 115.8f, 153.75f);
    public static PropModel TriplePipe { get; } = M("props_hydro/3pipe_straight_256", -128f, -5.01f, -17.01f, 128f, 5.01f, 17.01f);
    public static PropModel TwinPipe { get; } = M("props_hydro/2pipe_straight_256", -128f, -3f, -6f, 128f, 3f, 7f);
    public static PropModel Conduit { get; } = M("props_farm/conduit_pipe02", -1.47f, -4.5f, 0f, 2.11f, 4.5f, 144f);
    public static PropModel Tire { get; } = M("props_2fort/tire002", -27.4f, -30.15f, -0.91f, 27.4f, 64.44f, 68.25f);
    public static PropModel Track128 { get; } = M("props_mining/track_straight_128", -28.1f, -64f, -1.65f, 28.13f, 64f, 5.11f, solid: false);
    public static PropModel Track64 { get; } = M("props_mining/track_straight_64", -27.97f, -32f, -1.65f, 28.13f, 32f, 5.11f, solid: false);
    public static PropModel WallClock { get; } = M("props_spytech/wall_clock", 0f, -15.89f, -15.89f, 3.67f, 15.89f, 15.89f, solid: false);
    public static PropModel Corkboard { get; } = M("props_spytech/corkboard01", 0f, -40f, -25f, 3.04f, 40f, 25f, solid: false);
    public static PropModel ExitSign { get; } = M("props_spytech/exit_sign01", 0f, -15.6f, -12.22f, 4.47f, 15.6f, 12.22f, solid: false);
    public static PropModel WallVent { get; } = M("props_farm/vent001", 0f, -32f, -32f, 10f, 32f, 32f, solid: false);
    public static PropModel TrackBend { get; } = M("props_mining/track_arc_90degree_256", -127.83f, -128f, -1.62f, 128f, 128.2f, 5.11f, solid: false);
    public static PropModel TrackStop { get; } = M("props_trainstation/trackends01", -33.73f, -10f, -11.47f, 33.73f, 10f, 11.47f, solid: false);

    /// <summary>Daylight white: level 1's way in, under the town's sky.</summary>
    public const string Daylight = "236 242 255";

    /// <summary>A cold white work flood on a wall, aimed down into the room: level 1's shaft bottom and the Mill's large rooms.</summary>
    private static LampKind WorkFlood(string colour, int brightness, int fifty, int zero, int height) =>
        new(Flood, LampMount.Wall, new Lamp(colour, brightness, fifty, zero), Spacing: 0, LightAt: new(32, 0, -36))
        {
            Spot = new SpotCone(Inner: 40, Outer: 70, Pitch: 40),
            WallHeight = height,
        };

    //-------------------------------------------------------------------------
    // The four levels.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Level 1, the Collar: the new pit bottom just under the town, painted
    /// and signed. Pale concrete walls with a red painted skirting, cream
    /// painted steel, industrial ceiling lamps close together in warm white,
    /// no fog. Its way in is the bottom of the town's shaft, drawn in the
    /// town's own timber and rock under its lanterns, and the library carries
    /// the town's sun and sky for that shaft (§8).
    /// </summary>
    public static RoomTheme Collar { get; } = new(
        Floor: new("concrete/concretefloor002b", 0.25f),
        Wall: new("concrete/computerwall003c", 0.5f),
        Ceiling: new("concrete/concretewall011", 0.5f),
        Block: new("metal/wall029", 0.25f),
        Plug: RoomTheme.PlugOf("collar", new("concrete/computerwall003c", 0.5f)),
        Trigger: "TOOLS/TOOLSTRIGGER")
    {
        Name = "collar",
        Lamps = new LampKind(CeilingLamp, LampMount.Ceiling, new Lamp("255 234 204", 54, 192, 640), Spacing: 256, LightAt: new(0, 0, -12))
        {
            Fill = 320,
        },
        TallLamps = new LampKind(Bulkhead, LampMount.Wall, new Lamp("255 234 204", 66, 256, 896), Spacing: 256, LightAt: new(10, 0, 0))
        {
            WallHeight = 192,
            RowPitch = 512,
        },
        Props = new PropSet
        {
            Wall = [Lockers, FuseBoxes, ControlPanel, DangerSign],
            WallSpacing = 320,
            Corner = [PowderCrate, CementSacks],
            CornerChance = 0.6f,
            Floor = [CableSpool, Pallet],
            FloorDensity = 1.5f,
            CeilingRun = WhitePipe,
            Companions = [PowderCrate, OilDrum],
            // Above the skirting (the review of 2026-11-10): notice boards at eye height, a clock and
            // exit signs higher up, so a room is not bare above its lockers.
            Hung = [new(Corkboard, 120), new(WallClock, 176), new(ExitSign, 208), new(Corkboard, 120)],
            HungSpacing = 256,
        },
        Pilasters = new Pilasters(Spacing: 320, Width: 32, Depth: 12),
        Bands = new TrimBands(Skirting: 0, DadoHeight: 128, Dado: 8, Beam: 24, Belt: 16),
        Fog = Fog.None,
        ToneMap = new ToneMap(1f, 0.6f, 2f, 0.2f),
        Wetness = 0f,
        Sun = MineShaft.Sun,
        Sky = MineShaft.Sky,
        // The shaft's foot is the Collar's own pale concrete (the review of 2026-11-10: rock and oil lanterns
        // read as level 3), the town's rock kept only as one band at its foot, under daylight-white work
        // lamps: the ceiling lamps in daylight and floods at the shaft's foot, since the shaft's sun cannot
        // reach down 192 of shaft to the cage.
        Arrival = new RoomTheme(
            Floor: new("concrete/concretefloor002b", 0.25f),
            Wall: new("concrete/computerwall003c", 0.5f),
            Ceiling: new("concrete/concretewall011", 0.5f),
            Block: new("metal/wall029", 0.25f),
            Plug: RoomTheme.PlugOf("collar", new("concrete/computerwall003c", 0.5f)),
            Trigger: "TOOLS/TOOLSTRIGGER")
        {
            Name = "collar",
            WallLow = new("nature/rockwall012", 0.25f),
            WallBand = 96,
            Lamps = new LampKind(CeilingLamp, LampMount.Ceiling, new Lamp(Daylight, 54, 192, 640), Spacing: 256, LightAt: new(0, 0, -12)) { Fill = 320 },
            TallLamps = new LampKind(Bulkhead, LampMount.Wall, new Lamp(Daylight, 66, 256, 896), Spacing: 256, LightAt: new(10, 0, 0))
            {
                WallHeight = 192,
                RowPitch = 512,
            },
            // Brought down from 420 (the review of 0c79c6fc2: the shaft foot's floor read pure white) until its
            // concrete shows, still the level's brightest room.
            WorkLamps = new WorkLamps(WorkFlood(Daylight, 230, 448, 1536, 224), Count: 4, MinFloor: 0, AtFocus: true, Apart: 160),
            Bands = new TrimBands(Skirting: 0, DadoHeight: 128, Dado: 8, Beam: 24, Belt: 16),
            Fog = Fog.None,
            ToneMap = new ToneMap(1f, 0.6f, 2f, 0.2f),
            Sun = MineShaft.Sun,
            Sky = MineShaft.Sky,
        },
    };

    /// <summary>
    /// Level 2, Haulage: the rail level. Blue-grey corrugated steel over a
    /// band of rock under a rock roof, gravel floors with track on every
    /// door's axis (it lines up from room to room) and ore carts on sidings,
    /// white flood lights over the path and the side bays left in shadow,
    /// steel sets along the drifts and up the halls' walls, service pipes and
    /// cable conduits, a cold thin haze.
    /// </summary>
    public static RoomTheme Haulage { get; } = new(
        Floor: new("nature/gravelground001", 0.125f),
        Wall: new("metal/wall016d", 0.25f),
        Ceiling: new("nature/rockwall013", 0.25f),
        // Concrete stairs and square piers, not diamond plate (the review of 2026-11-10).
        Block: new("concrete/concretewall001e", 0.25f),
        Plug: RoomTheme.PlugOf("haulage", new("metal/wall016d", 0.25f)),
        Trigger: "TOOLS/TOOLSTRIGGER")
    {
        Name = "haulage",
        Trim = new("metal/ibeam001b", 0.25f),
        // A steel channel round each doorway, planks across its threshold (the review of 2026-11-10).
        FrameBrushes = new BrushFrame(new("metal/ibeam001b", 0.25f), new("metal/ibeam001b", 0.25f), PostWidth: 8, HeadDepth: 8, Proud: 8)
        {
            Flange = 12,
            Crossing = new("wood/wood_floor002", 0.25f),
            // Level 2's walls under its dimmer floods (brief p9) are lit to about 0.115: the arch at 1.5 times.
            MinLight = 0.3f,
        },
        SquarePillars = true,
        RoofBeams = 256,
        // About 15 % under the first pass's (brief p9: the gate's view figure read level 2 at 75 against
        // the game's 70).
        Lamps = new LampKind(Flood, LampMount.Ceiling, new Lamp("226 236 255", 109, 400, 1024), Spacing: 288, LightAt: new(0, 0, -36))
        {
            Spot = new SpotCone(Inner: 75, Outer: 89, Pitch: 90),
        },
        TallLamps = new LampKind(Flood, LampMount.Wall, new Lamp("226 236 255", 140, 400, 1152), Spacing: 448, LightAt: new(32, 0, -36))
        {
            Spot = new SpotCone(Inner: 35, Outer: 60, Pitch: 45),
            WallHeight = 256,
            RowPitch = 512,
        },
        Pendants = Pendant(BellLamp, new Lamp("226 236 255", 64, 384, 1280), new(0, 0, -28), hang: 448, grid: 512),
        // The yard hall's overhead crane (the review of 2026-11-10: a gantry in level 2's and 4's halls).
        Gantry = new Gantry(Height: 704, Girder: 32, Hook: CraneHook, Cable: Cable64),
        Props = new PropSet
        {
            Wall = [Sheeting, HandTruck, RopeSpool, Conduit],
            WallSpacing = 320,
            Corner = [OilDrum, BigCrate],
            CornerChance = 0.5f,
            Floor = [OreCart],
            FloorDensity = 1f,
            Rail = Track,
            RailEnds = [Track128, Track64],
            Siding = OreCart,
            RailBend = TrackBend,
            RailStop = TrackStop,
            RailFamilies = new HashSet<string>(StringComparer.Ordinal) { "drift", "widedrift", "dogleg", "bend", "tee", "cross", "yard", "hall" },
            StubFamilies = new HashSet<string>(StringComparer.Ordinal) { "store", "stope" },
            CeilingRun = TwinPipe,
            WallRunHeight = 448,
            Centrepiece = [OreHeap],
            Hung = [new(WallVent, 224)],
            HungSpacing = 448,
            Sets = new SetFrame(Spacing: 384, Post: 16, Cap: 16),
            Companions = [OilDrum, Tire, PowderCrate],
        },
        // Tall walls: rock below a band at 192, steel sets every 384, belts and a beam in the steel.
        WallLow = new("nature/rockwall013", 0.25f),
        WallBand = 192,
        Pilasters = new Pilasters(Spacing: 384, Width: 24, Depth: 16),
        Bands = new TrimBands(Skirting: 12, DadoHeight: 192, Dado: 12, Beam: 32, Belt: 16),
        Fog = new Fog("52 56 62", 1536, 8192, 0.25f),
        ToneMap = new ToneMap(1f, 0.5f, 2f, 0.25f),
        Wetness = 0.1f,
    };

    /// <summary>
    /// Level 3, the Timber Stopes: older workings held up by timber. Red rock
    /// walls under a lagged plank roof, dirt floors, timber sets close along
    /// the drifts and timber posts up the halls, barrels and woodpiles,
    /// lanterns in three to five pools a room along its route (hung from the
    /// sets or stood on powder crates, never set on the floor) with the dark
    /// between them, warm dust.
    /// </summary>
    public static RoomTheme TimberStopes { get; } = new(
        // A darker floor and paler timber than before, so the level is not one orange (the review of 2026-11-10).
        Floor: new("nature/dirtground002", 0.25f),
        Wall: new("nature/rockwall012", 0.25f),
        Ceiling: new("wood/wood_wall001", 0.25f),
        Block: new("wood/wood_wall003", 0.25f),
        Plug: RoomTheme.PlugOf("stopes", new("nature/rockwall012", 0.25f)),
        Trigger: "TOOLS/TOOLSTRIGGER")
    {
        Name = "stopes",
        Trim = new("wood/wood_wall008a", 0.25f),
        // A timber set round each doorway: two posts and a cap running past them.
        FrameBrushes = new BrushFrame(new("wood/wood_wall008a", 0.25f), new("wood/wood_wall008a", 0.25f), PostWidth: 16, HeadDepth: 16, Proud: 16)
        {
            CapOverhang = 24,
        },
        // A grey seam of ore through the red rock, and a dim cold fill so the dark between the pools is cool.
        WallSeam = new WallSeam(new("nature/rockwall001_island", 0.25f), 144, 272),
        // One wide fill a cell, not one every 384 (the review of 0c79c6fc2: level 3 spent 412 to 431 map
        // entities, about 250 of them this fill's lights).
        CoolFill = (new Lamp("150 172 214", 40, 448, 1280), 1024),
        RoofBeams = 288,
        // Lanterns in three to five pools a room along its route, hung from the timber sets or set on a
        // powder crate, the dark between them: a short half-light, a long tail.
        Lamps = new LampKind(Lantern, LampMount.Floor, new Lamp("255 190 128", 440, 192, 900), Spacing: 320, LightAt: new(0, 0, 40))
        {
            Stand = PowderCrate,
            Pools = (3, 5),
        },
        // What the pools leave dark (an alcove, a pit's floor, a deck) gets a lantern hung on a wall or a
        // post at head height, never one set down on the floor.
        CoverLamps = new LampKind(Lantern, LampMount.Wall, new Lamp("255 190 128", 300, 192, 900), Spacing: 288, LightAt: new(0, 0, 40))
        {
            WallHeight = 128,
        },
        Pendants = Pendant(HangingBulb, new Lamp("255 196 140", 100, 384, 1280), new(0, 0, -10), hang: 448, grid: 576),
        // A timber headframe over a hall's plinth: a winze's frame, the old workings' set piece.
        Headframe = true,
        Props = new PropSet
        {
            Wall = [Lagging],
            WallSpacing = 320,
            Corner = [Barrel, HalfCrate],
            CornerChance = 0.7f,
            // Stacks of sawn lagging and, in a room with the floor for one, an ore heap (brief p4: the halls were bare).
            Floor = [WoodPile, OreHeap],
            FloorDensity = 1.5f,
            Sets = new SetFrame(Spacing: 192, Post: 16, Cap: 24),
            Companions = [PowderCrate, OilDrum],
        },
        // Posts up the tall walls joined by a plate at 320 into timber sets lining the wall, lagging in
        // the bays between them (brief p4: the great halls read as flat brick).
        Pilasters = new Pilasters(Spacing: 288, Width: 24, Depth: 24) { Cap = 320 },
        // A great hall's rock drawn at twice the scale: a 2048 wall at a cell's scale tiles into bricks.
        LargeWall = new("nature/rockwall012", 0.5f),
        Bands = new TrimBands(Skirting: 12, DadoHeight: 128, Dado: 12, Beam: 32, Belt: 24),
        Fog = new Fog("40 42 48", 1024, 6144, 0.4f),
        ToneMap = new ToneMap(1f, 0.5f, 2.5f, 0.3f),
        Wetness = 0.25f,
        DynamicBudget = 12,
    };

    /// <summary>
    /// Level 4, the Mill: ore crushing. Dark steel plate walls over a band of
    /// machine-green paint, hazard stripes at the walls' foot, a dado and the
    /// roof, a cracked concrete roof, blue-grey diamond-plate decks, black coal
    /// gravel, steel sets, generators only in rooms with the floor for one,
    /// ducts and hazard barrels; a few yellow sodium lamps on goosenecks and
    /// one cold white work light in each large room, so the yellow is the
    /// light, not the level; dust.
    /// </summary>
    public static RoomTheme Mill { get; } = new(
        Floor: new("nature/gravelcoal001", 0.25f),
        Wall: new("metal/wall021", 0.25f),
        Ceiling: new("concrete/concretefloor007b", 0.25f),
        Block: new("metal/imetal002", 0.25f),
        Plug: RoomTheme.PlugOf("mill", new("metal/wall021", 0.25f)),
        Trigger: "TOOLS/TOOLSTRIGGER")
    {
        Name = "mill",
        Trim = new("metal/ibeam001b", 0.25f),
        // A heavy steel frame round each doorway, the hazard stripe on its head.
        FrameBrushes = new BrushFrame(new("metal/ibeam001b", 0.25f), new("metal/ibeam001b", 0.25f), PostWidth: 16, HeadDepth: 16, Proud: 12)
        {
            Flange = 20,
            FlangeDepth = 6,
            HeadFace = new("props/hazardstrip001a", 0.5f),
            // The Mill's walls are lit to about 0.10 (brief p9): the arch at 0.15, not 0.18.
            MinLight = 0.3f,
        },
        // Sodium, less saturated (the review of 2026-11-10: the olive cast).
        Lamps = new LampKind(Gooseneck, LampMount.Wall, new Lamp("255 214 150", 72, 256, 900), Spacing: 512, LightAt: new(76, 0, -24))
        {
            // Its arm's underside clears a giant's 160 (brief p7: at 176 it hung 158 over the floor).
            WallHeight = 180,
            RowPitch = 448,
        },
        Props = new PropSet
        {
            Wall = [Welder, FuseBox, ControlPanel],
            WallSpacing = 384,
            Corner = [HazardBarrel, RadBarrel, DuctBend],
            CornerChance = 0.6f,
            Floor = [Generator],
            FloorDensity = 1f,
            CeilingRun = TriplePipe,
            WallRunHeight = 448,
            Centrepiece = [Generator, GeneratorMachine],
            Sets = new SetFrame(Spacing: 320, Post: 16, Cap: 16),
            Companions = [OilDrum, Tire],
        },
        // Dark steel over a band of machine-green paint, hazard stripes along the walls' foot, a dado and
        // the roof, steel sets up the tall walls, and one cold white work light in each large room, so
        // the yellow is the sodium lamps', not the level's.
        WallLow = new("metal/metalpanel01", 0.25f),
        WallBand = 192,
        Pilasters = new Pilasters(Spacing: 320, Width: 24, Depth: 16),
        // One stripe band, only over door heads and along ledges and decks (the review of 2026-11-10);
        // the wall bands are plain steel trim.
        Bands = new TrimBands(Skirting: 16, DadoHeight: 128, Dado: 8, Beam: 16),
        Hazard = new("props/hazardstrip001a", 0.5f),
        RoofBeams = 320,
        WorkLamps = new WorkLamps(WorkFlood("214 228 255", 100, 320, 1024, 256), Count: 2, MinFloor: 200_000),
        Pendants = Pendant(CagedLamp, new Lamp("255 214 150", 45, 384, 1280), new(0, 0, -28), hang: 448, grid: 640),
        Gantry = new Gantry(Height: 704, Girder: 32, Hook: CraneHook, Cable: Cable64),
        Fog = new Fog("74 64 44", 256, 2560, 0.55f),
        ToneMap = new ToneMap(1f, 0.6f, 3f, 0.35f),
        Wetness = 0.35f,
    };

    /// <summary>Levels 1 to 4 in order.</summary>
    public static IReadOnlyList<RoomTheme> All { get; } = [Collar, Haulage, TimberStopes, Mill];
}

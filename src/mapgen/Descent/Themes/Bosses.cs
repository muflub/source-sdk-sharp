namespace SourceSharp.MapGen.Descent.Themes;

/// <summary>
/// The three boss levels' themes (plan_descent_mine_levels.md §7, brief p13): each act's end, drawn
/// in its act's materials and lit for its fight. Level 5, the Powder Magazine: the mine's explosives
/// store at the bottom of the lift, blasted rock over a brick lining, red warning lamps and one white
/// work light over the arena. Level 10, the Bore: the grey cavern Gray Mann's robots bored up into,
/// their steel catwalks over the Tank's track, their cold white light towers. Level 15, the Pit: the
/// black rock and mud of the Rot round a lake of black slime, torches on the arena's platform only.
/// </summary>
/// <remarks>
/// Every material and model here is a row of the asset database at <c>/home/lodle/assetdb</c>, chosen
/// by query and looked at; the depth's palette file says why each, and a fact holds every name here to
/// the database and to its palette. Model bounds are the database's mesh bounds. A boss arena lights
/// itself (<see cref="Room.OwnLamps"/>) with the kinds in each theme's <see cref="RoomTheme.ArenaLamps"/>;
/// the other rooms of a boss level are dressed as any level's.
/// </remarks>
public static class Bosses
{
    private static PropModel M(string path, float x0, float y0, float z0, float x1, float y1, float z1, bool isStatic = true, bool solid = true, int front = 0)
        => new($"models/{path}.mdl", new(x0, y0, z0), new(x1, y1, z1), isStatic, solid, front);

    private static LampKind Pendant(PropModel fixture, Lamp light, Point lightAt, int hang, int grid) =>
        new(fixture, LampMount.Pendant, light, Spacing: grid, LightAt: lightAt)
        {
            Cords = [ActOne.Cable128, ActOne.Cable64, ActOne.Cable32],
            WallHeight = hang,
            Grid = grid,
        };

    //-------------------------------------------------------------------------
    // Level 5, the Powder Magazine.
    //-------------------------------------------------------------------------

    /// <summary>A red caged warning lamp on a wall, its bulb toward +x.</summary>
    public static PropModel Siren { get; } = M("props_spytech/siren", -3.53f, -4.6f, -8.84f, 3.51f, 4.76f, 8.84f);

    /// <summary>The magazine's red warning lamps: a short red pool on the walls, an accent over the store's warm light (brief p13's first review: as the rooms' only lamp the red drowned them).</summary>
    public static LampKind WarningLamp { get; } = new(Siren, LampMount.Wall, new Lamp("255 82 56", 70, 160, 640), Spacing: 448, LightAt: new(10, 0, 0))
    {
        WallHeight = 176,
        RowPitch = 448,
    };

    /// <summary>The arena's warning lamps: brighter, their red pools reaching the floor from high on the walls.</summary>
    public static LampKind ArenaWarning { get; } = WarningLamp with { Light = new Lamp("255 70 44", 150, 256, 900) };

    /// <summary>The one white work light over the arena's middle, on a long cord.</summary>
    public static LampKind WhiteWork { get; } = Pendant(ActOne.BellLamp, new Lamp("232 238 255", 320, 512, 1792), new(0, 0, -28), hang: 576, grid: 0);

    public static RoomTheme Magazine { get; } = new(
        Floor: new("concrete/concretefloor003", 0.25f),
        Wall: new("nature/rockwall007", 0.25f),
        Ceiling: new("nature/rockwall007", 0.25f),
        Block: new("concrete/concretewall009", 0.25f),
        Plug: RoomTheme.PlugOf("magazine", new("nature/rockwall007", 0.25f)),
        Trigger: "TOOLS/TOOLSTRIGGER")
    {
        Name = "magazine",
        Trim = new("wood/wood_wall008a", 0.25f),
        // A timber set round each doorway, the magazine's lining.
        FrameBrushes = new BrushFrame(new("wood/wood_wall008a", 0.25f), new("wood/wood_wall008a", 0.25f), PostWidth: 16, HeadDepth: 16, Proud: 16)
        {
            CapOverhang = 24,
            // About the maze's dark walls' light (brief p18): at 0.3 every frame in the maze read as a pale stone arch.
            MinLight = 0.15f,
        },
        // Brick lining the blasted rock to above head height; the rock drawn larger in the arena.
        WallLow = new("brick/brickwall001", 0.25f),
        WallBand = 192,
        LargeWall = new("nature/rockwall007", 0.5f),
        Lamps = WarningLamp,
        // What the red lamps leave dark gets a caged bulb's warm white; a warm fill under every roof is the store's own light.
        CoverLamps = new LampKind(ActOne.CagedLamp, LampMount.Ceiling, new Lamp("255 220 170", 60, 256, 900), Spacing: 288, LightAt: new(0, 0, -16)),
        CoolFill = (new Lamp("255 206 160", 44, 448, 1280), 512),
        Hazard = new("props/hazardstrip001a", 0.5f),
        Props = new PropSet
        {
            Wall = [ActOne.DangerSign, ActOne.FuseBox],
            WallSpacing = 384,
            Corner = [ActOne.PowderCrate, ActOne.CementSacks],
            CornerChance = 0.7f,
            Floor = [ActOne.CableSpool, ActOne.Pallet],
            FloorDensity = 1f,
            Companions = [ActOne.PowderCrate, ActOne.OilDrum],
            Hung = [new(ActOne.DangerSign, 104)],
            HungSpacing = 512,
        },
        // Timber posts and a wall plate up the 512-high rooms too (the antechamber), whose blasted walls were big flat panels (brief p15).
        Pilasters = new Pilasters(Spacing: 320, Width: 24, Depth: 24, MinHeight: 512) { Cap = 320 },
        Bands = new TrimBands(Skirting: 12, DadoHeight: 192, Dado: 12, Beam: 24, Belt: 16),
        ArenaLamps = [ArenaWarning, WhiteWork],
        Fog = new Fog("46 32 28", 768, 4096, 0.4f),
        ToneMap = new ToneMap(1f, 0.5f, 2.5f, 0.3f),
        Wetness = 0.3f,
        // The lift's last station is the Mill's: the cage came down through it.
        Arrival = ActOne.Mill,
    };

    //-------------------------------------------------------------------------
    // Level 10, the Bore.
    //-------------------------------------------------------------------------

    /// <summary>The robots' mobile light tower: two floods on a mast over a generator trailer, the floods toward -x.</summary>
    public static PropModel LightTower { get; } = M("props_mvm/construction_light02", -44.61f, -40.07f, -0.2f, 48.11f, 40.56f, 152.52f, front: 180);

    /// <summary>A light tower's cold white floods, aimed down across the arena's floor.</summary>
    public static LampKind TowerLamp { get; } = new(LightTower, LampMount.Floor, new Lamp("214 226 255", 250, 512, 2048), Spacing: 640, LightAt: new(52, 0, 136))
    {
        Spot = new SpotCone(Inner: 45, Outer: 75, Pitch: 25),
    };

    /// <summary>The towers the robots left in the tunnels before the arena: the same floods, turned down.</summary>
    public static LampKind TunnelTower { get; } = TowerLamp with { Light = new Lamp("214 226 255", 45, 384, 1280), Spacing = 768 };

    /// <summary>The robots' lift over the bore, a steel headframe (left non-solid: a frame the Tank and the heroes pass through).</summary>
    public static PropModel MineElevator { get; } = M("props_mvm/mine_elevator", -360.12f, -379.69f, -47.48f, 328.54f, 239.02f, 640.07f, solid: false);

    /// <summary>Planks nailed across a tank tunnel's mouth (no collision).</summary>
    public static PropModel TankPlanks { get; } = M("props_mvm/woodplanks_tank_entrance002", -18.6f, -134.2f, -0.57f, 12.55f, 127.03f, 221.89f, solid: false);

    public static RoomTheme Bore { get; } = new(
        Floor: new("nature/gravelfloor002a", 0.25f),
        Wall: new("nature/rockwall010b", 0.25f),
        Ceiling: new("nature/rockwall010b", 0.25f),
        Block: new("nature/cavewall005a", 0.25f),
        Plug: RoomTheme.PlugOf("bore", new("nature/rockwall010b", 0.25f)),
        Trigger: "TOOLS/TOOLSTRIGGER")
    {
        Name = "bore",
        Trim = new("metal/ibeam001b", 0.25f),
        // A robot-cut steel frame round each doorway.
        FrameBrushes = new BrushFrame(new("metal/ibeam001b", 0.25f), new("metal/ibeam001b", 0.25f), PostWidth: 16, HeadDepth: 16, Proud: 12)
        {
            Flange = 20,
            FlangeDepth = 6,
            MinLight = 0.15f,
        },
        LargeWall = new("nature/rockwall010b", 0.5f),
        Lamps = TunnelTower,
        // A caged work light hung from the roof where the towers leave the floor dark.
        CoverLamps = new LampKind(ActTwo.WorkLight, LampMount.Ceiling, new Lamp("206 218 240", 20, 320, 1024), Spacing: 384, LightAt: new(0, -4, -24)),
        Props = new PropSet
        {
            Wall = [ActOne.FuseBox],
            WallSpacing = 512,
            Corner = [ActTwo.BoulderPile, ActTwo.Boulder3],
            CornerChance = 0.6f,
            Floor = [ActTwo.Boulder2, ActTwo.RubbleSpill],
            FloorDensity = 1f,
            Companions = [ActTwo.RubbleSpill],
        },
        ArenaLamps = [TowerLamp],
        SetPieces = [MineElevator, TankPlanks],
        // The catwalks' deck and the Tank's worn track.
        ArenaSurfaces = [new("metal/metalfloor001b", 0.25f), new("nature/gravelcoal001", 0.25f)],
        Fog = new Fog("14 16 20", 512, 2048, 0.65f),
        ToneMap = new ToneMap(1f, 0.5f, 3f, 0.4f),
        Wetness = 0.5f,
    };

    //-------------------------------------------------------------------------
    // Level 15, the Pit.
    //-------------------------------------------------------------------------

    /// <summary>A tall stake with a burning bundle at its top.</summary>
    public static PropModel Torch { get; } = M("props_viaduct_event/underworld_torch01", -6.92f, -13.73f, -0.76f, 6.32f, 4.05f, 172.43f);

    /// <summary>A torch's fire: orange, short, the only light the arena's platform has.</summary>
    public static LampKind TorchLamp { get; } = new(Torch, LampMount.Floor, new Lamp("255 120 40", 80, 160, 768), Spacing: 640, LightAt: new(0, -5, 178));

    /// <summary>The arena platform's torches: brighter, a pool of fire on the stone with the lake black round it.</summary>
    public static LampKind PlatformTorch { get; } = TorchLamp with { Light = new Lamp("255 120 40", 180, 224, 1024) };

    public static RoomTheme Pit { get; } = new(
        // Dark brown mud: the plan's blend drew its second layer, a litter of twigs, on a flat floor, which read as grass, and
        // black mud (swampmud005) took no light: 80 % of the frame black in game, the base lamp 11.8 (brief p15).
        Floor: new("swamp/nature/swampmud003", 0.25f),
        Wall: new("nature/underworld_rockwall001", 0.25f),
        Ceiling: new("nature/underworld_rockwall001", 0.25f),
        Block: new("swamp/brick/swampcobblewall001", 0.25f),
        Plug: RoomTheme.PlugOf("pit", new("nature/underworld_rockwall001", 0.25f)),
        Trigger: "TOOLS/TOOLSTRIGGER")
    {
        Name = "pit",
        // A rough rock arch: thick jambs and head, haunched corners.
        FrameBrushes = new BrushFrame(new("swamp/brick/swampcobblewall001", 0.25f), new("swamp/brick/swampcobblewall001", 0.25f), PostWidth: 24, HeadDepth: 24, Proud: 24)
        {
            Fillet = 40,
            Flange = 24,
            FlangeDepth = 12,
            MinLight = 0.04f,
        },
        WallLow = new("swamp/nature/swampmud004", 0.25f),
        WallBand = 160,
        LargeWall = new("nature/underworld_rockwall001", 0.5f),
        // Black slime: TF2's underworld water, its fog near black (an own copy reflecting without a cubemap).
        Water = "descent/pit_slime",
        Extra = [OwnMaterial.Water("descent/pit_slime", "water/water_underworld", "water/water_underworld_beneath", "14 24 9", 40, "[.3 .36 .3]", 0.1f)],
        Lamps = TorchLamp,
        Props = new PropSet
        {
            Corner = [ActTwo.BoulderPile],
            CornerChance = 0.5f,
            Floor = [ActTwo.Boulder3],
            FloorDensity = 0.5f,
        },
        ArenaLamps = [PlatformTorch],
        Fog = new Fog("6 9 6", 384, 1536, 0.75f),
        ToneMap = new ToneMap(1f, 0.5f, 3f, 0.4f),
        Wetness = 1f,
        PoolHazard = "slime",
    };

    /// <summary>Levels 5, 10 and 15.</summary>
    public static IReadOnlyList<RoomTheme> All { get; } = [Magazine, Bore, Pit];
}

namespace SourceSharp.MapGen;

/// <summary>
/// What tf2_descent_town is built from: every material and model it names, in one
/// place, so a fact can prove each exists in the mounted content (TF2's VPKs)
/// rather than a missing one showing up in game as a checkerboard.
/// </summary>
public sealed record TownPalette(
    string Ground,
    string Street,
    string Cliff,
    string Wall,
    string Floor,
    string Beam,
    string Roof,
    string Sky,
    string WaterTower,
    string OreCart,
    string Track,
    string Lantern,
    string Wheel,
    string Barrel,
    string Truss)
{
    /// <summary>The TF2 mining set (plan_host_sdk §2.10, P0(9): each one listed in the TF2 VPKs).</summary>
    public static TownPalette Tf2Mining { get; } = new(
        Ground: "nature/ground_03",
        Street: "nature/gravelground001",
        Cliff: "nature/rockwall011",
        Wall: "wood/wood_wall007",
        Floor: "wood/wood_floor002",
        Beam: "wood/wood_beam02",
        Roof: "medieval/wood_shingles03",
        Sky: "sky_badlands_01",
        WaterTower: "models/props_mining/watertower.mdl",
        OreCart: "models/props_vehicles/mining_cart_supplies001.mdl",
        Track: "models/props_mining/track_straight_128.mdl",
        Lantern: "models/props_2fort/lantern001.mdl",
        Wheel: "models/props_2fort/wagonwheel001.mdl",
        Barrel: "models/props_farm/wooden_barrel.mdl",
        Truss: "models/props_mining/wooden_truss01.mdl");

    /// <summary>The brush materials, as a map names them (materials/ and .vmt implied).</summary>
    public IEnumerable<string> Materials => [Ground, Street, Cliff, Wall, Floor, Beam, Roof];

    public IEnumerable<string> Models => [WaterTower, OreCart, Track, Lantern, Wheel, Barrel, Truss];
}

/// <summary>
/// tf2_descent_town: a simple mining town with a mineshaft going down (the user's
/// ruling of 2026-09-30, plan_host_sdk §2.10). A sealed valley, one street
/// with boardwalks and six building shells, the square where players arrive,
/// and at the far end the mine head: a headframe over a fenced shaft, and a
/// cage whose button, like the shaft itself, sends the player down through a
/// <c>logic_level_transition</c>, the same entity map-tools writes into every
/// generated level.
///
/// <para>
/// Laid out along X: the square at the west end, the mine against the east
/// cliff. Z is up; the ground's top is z = 0.
/// </para>
/// </summary>
public sealed class TF2DescentTown(TownPalette palette)
{
    public const string MapName = "tf2_descent_town";

    /// <summary>map-tools' ModEntityContract v1: the transition entity, its keys and input.</summary>
    public const string TransitionClass = "logic_level_transition";
    public const string TransitionName = "mine_descent";
    public const string FirstLevel = "tf2_descent-1";

    /// <summary>The targetname of the spawns a player coming up from below uses.</summary>
    public const string ArrivalName = "tf2_descent_arrival";

    public const int SquareSpawns = 16;
    public const int ArrivalSpawns = 8;

    // The valley.
    private const float HalfX = 1536f, HalfY = 1024f, Height = 768f, Wall = 64f, Slab = 16f;

    // The shaft: an opening in the ground at the east end, and how deep it goes.
    private const float ShaftX = 1300f, ShaftHalf = 96f, ShaftDepth = 384f;

    public TF2DescentTown() : this(TownPalette.Tf2Mining) { }

    public TownPalette Palette => palette;

    public VmfMap Build()
    {
        var map = new VmfMap();

        map.World.Add(new("skyname", palette.Sky));
        map.World.Add(new("ssmap_entities", "mod"));
        map.World.Add(new("ssmap_entities_version", "1"));

        Valley(map);
        Street(map);
        Buildings(map);
        MineHead(map);
        Light(map);
        Props(map);
        SquareSpawnPoints(map);
        ArrivalSpawnPoints(map);

        return map;
    }

    //-------------------------------------------------------------------------
    // The valley: ground with the shaft's hole in it, cliffs, the sky. Sealed:
    // a leak makes vbsp skip visibility and the map loads fullbright.
    //-------------------------------------------------------------------------

    private void Valley(VmfMap map)
    {
        float x0 = -HalfX - Wall, x1 = HalfX + Wall, y0 = -HalfY - Wall, y1 = HalfY + Wall;
        float sx0 = ShaftX - ShaftHalf, sx1 = ShaftX + ShaftHalf;

        // The ground, in four pieces around the shaft's opening.
        Add(map, new(x0, y0, -Slab), new(sx0, y1, 0f), palette.Ground);
        Add(map, new(sx1, y0, -Slab), new(x1, y1, 0f), palette.Ground);
        Add(map, new(sx0, y0, -Slab), new(sx1, -ShaftHalf, 0f), palette.Ground);
        Add(map, new(sx0, ShaftHalf, -Slab), new(sx1, y1, 0f), palette.Ground);

        // The cliffs.
        Add(map, new(x0, y0, -Slab), new(-HalfX, y1, Height), palette.Cliff);
        Add(map, new(HalfX, y0, -Slab), new(x1, y1, Height), palette.Cliff);
        Add(map, new(-HalfX, y0, -Slab), new(HalfX, -HalfY, Height), palette.Cliff);
        Add(map, new(-HalfX, HalfY, -Slab), new(HalfX, y1, Height), palette.Cliff);

        // The sky, which seals the top like any solid.
        Add(map, new(x0, y0, Height), new(x1, y1, Height + Slab), "tools/toolsskybox");

        // The shaft: timber-lined walls and a floor, below the opening.
        float floor = -ShaftDepth;
        Add(map, new(sx0 - Wall, -ShaftHalf - Wall, floor - Slab), new(sx1 + Wall, ShaftHalf + Wall, floor), palette.Floor);
        Add(map, new(sx0 - Wall, -ShaftHalf - Wall, floor), new(sx0, ShaftHalf + Wall, -Slab), palette.Wall);
        Add(map, new(sx1, -ShaftHalf - Wall, floor), new(sx1 + Wall, ShaftHalf + Wall, -Slab), palette.Wall);
        Add(map, new(sx0, -ShaftHalf - Wall, floor), new(sx1, -ShaftHalf, -Slab), palette.Wall);
        Add(map, new(sx0, ShaftHalf, floor), new(sx1, ShaftHalf + Wall, -Slab), palette.Wall);
    }

    //-------------------------------------------------------------------------
    // The street and its boardwalks, down the valley's long axis.
    //-------------------------------------------------------------------------

    private void Street(VmfMap map)
    {
        Add(map, new(-HalfX, -192f, 0f), new(ShaftX - ShaftHalf - 128f, 192f, 2f), palette.Street);
        Add(map, new(-1100f, 192f, 0f), new(800f, 256f, 8f), palette.Floor);
        Add(map, new(-1100f, -256f, 0f), new(800f, -192f, 8f), palette.Floor);
    }

    //-------------------------------------------------------------------------
    // Six building shells, three a side: store, assay office, saloon; two
    // bunkhouses and the smithy. Closed in v1; an info_target names each for
    // the shops that come later.
    //-------------------------------------------------------------------------

    public static IReadOnlyList<(string Name, float X, int Side)> Shops =>
    [
        ("general_store", -800f, 1), ("assay_office", -150f, 1), ("saloon", 500f, 1),
        ("bunkhouse_1", -800f, -1), ("bunkhouse_2", -150f, -1), ("smithy", 500f, -1),
    ];

    private void Buildings(VmfMap map)
    {
        foreach (var (name, x, side) in Shops)
        {
            float near = 288f * side, far = 672f * side;
            float y0 = Math.Min(near, far), y1 = Math.Max(near, far);

            Add(map, new(x - 224f, y0, 0f), new(x + 224f, y1, 224f), palette.Wall);
            // The roof: a slab with an overhang, and a ridge along it.
            Add(map, new(x - 240f, y0 - 16f, 224f), new(x + 240f, y1 + 16f, 240f), palette.Roof);
            Add(map, new(x - 240f, (y0 + y1) / 2f - 64f, 240f), new(x + 240f, (y0 + y1) / 2f + 64f, 272f), palette.Roof);
            // The porch, on the street side.
            float porchNear = side > 0 ? y0 - 32f : y1, porchFar = side > 0 ? y0 : y1 + 32f;
            Add(map, new(x - 160f, porchNear, 0f), new(x + 160f, porchFar, 12f), palette.Floor);

            map.Entities.Add(new VmfEntity { ClassName = "info_target" }
                .Set("targetname", "shop_" + name)
                .Set("origin", new Point(x, side > 0 ? y0 - 48f : y1 + 48f, 64f).ToString()));
        }
    }

    //-------------------------------------------------------------------------
    // The mine head: four headframe posts and crossbeams over the shaft, a
    // fence on three sides, the winch house, and on the open side the cage
    // with its button. Below, a trigger across the shaft for anyone who jumps.
    //-------------------------------------------------------------------------

    private void MineHead(VmfMap map)
    {
        float sx0 = ShaftX - ShaftHalf, sx1 = ShaftX + ShaftHalf;

        // The headframe.
        foreach (float px in new[] { sx0 - 24f, sx1 + 8f })
        foreach (float py in new[] { -ShaftHalf - 24f, ShaftHalf + 8f })
            Add(map, new(px, py, 0f), new(px + 16f, py + 16f, 320f), palette.Beam);
        Add(map, new(sx0 - 24f, -ShaftHalf - 24f, 320f), new(sx1 + 24f, -ShaftHalf - 8f, 336f), palette.Beam);
        Add(map, new(sx0 - 24f, ShaftHalf + 8f, 320f), new(sx1 + 24f, ShaftHalf + 24f, 336f), palette.Beam);

        // The fence: north, south and east of the opening, waist high.
        Add(map, new(sx0, -ShaftHalf - 8f, 0f), new(sx1, -ShaftHalf, 40f), palette.Beam);
        Add(map, new(sx0, ShaftHalf, 0f), new(sx1, ShaftHalf + 8f, 40f), palette.Beam);
        Add(map, new(sx1, -ShaftHalf, 0f), new(sx1 + 8f, ShaftHalf, 40f), palette.Beam);

        // The winch house, behind the shaft against the cliff.
        Add(map, new(sx1 + 64f, -128f, 0f), new(HalfX, 128f, 160f), palette.Wall);
        Add(map, new(sx1 + 48f, -144f, 160f), new(HalfX, 144f, 176f), palette.Roof);

        // The cage, on the open (west) side: a floor, bars on two sides, a roof.
        float cx0 = sx0 - 128f, cx1 = sx0;
        Add(map, new(cx0, -64f, 0f), new(cx1, 64f, 8f), palette.Floor);
        Add(map, new(cx0, -72f, 8f), new(cx1, -64f, 112f), palette.Beam);
        Add(map, new(cx0, 64f, 8f), new(cx1, 72f, 112f), palette.Beam);
        Add(map, new(cx0, -72f, 112f), new(cx1, 72f, 120f), palette.Beam);

        // The button on the cage's south bar: pressed, it sends the player down.
        var button = new VmfEntity { ClassName = "func_button" }
            .Set("targetname", "mine_cage_button")
            .Set("spawnflags", "1025")   // don't move; use activates
            .Set("wait", "3")
            .Set("speed", "5")
            .Wire("OnPressed", TransitionName, "Transition");
        button.Solids.Add(VmfMap.Box(new(cx0 + 48f, -64f, 48f), new(cx0 + 80f, -56f, 72f), palette.Beam));
        map.Entities.Add(button);

        // Halfway down the shaft: whoever jumps is sent down before landing.
        var shaftTrigger = new VmfEntity { ClassName = "trigger_multiple" }
            .Set("targetname", "mine_shaft_trigger")
            .Set("spawnflags", "1")      // clients
            .Set("wait", "1")
            .Wire("OnStartTouch", TransitionName, "Transition");
        shaftTrigger.Solids.Add(VmfMap.Box(new(sx0, -ShaftHalf, -ShaftDepth + 32f), new(sx1, ShaftHalf, -ShaftDepth / 2f), "tools/toolstrigger"));
        map.Entities.Add(shaftTrigger);

        map.Entities.Add(new VmfEntity { ClassName = TransitionClass }
            .Set("targetname", TransitionName)
            .Set("direction", "down")
            .Set("map", FirstLevel)
            .Set("StartDisabled", "0")
            .Set("origin", new Point(ShaftX, 0f, 32f).ToString()));
    }

    //-------------------------------------------------------------------------
    // An afternoon sun, and lanterns on the porches and at the mine head.
    //-------------------------------------------------------------------------

    private void Light(VmfMap map)
    {
        map.Entities.Add(new VmfEntity { ClassName = "light_environment" }
            .Set("origin", new Point(0f, 0f, Height - 64f).ToString())
            .Set("angles", "-50 35 0")
            .Set("pitch", "-50")
            .Set("_light", "255 232 196 450")
            .Set("_ambient", "126 138 160 70")
            .Set("_lightHDR", "-1 -1 -1 1")
            .Set("_ambientHDR", "-1 -1 -1 1"));

        foreach (var (_, x, side) in Shops)
            Lantern(map, new(x, 248f * side, 96f));
        Lantern(map, new(ShaftX - ShaftHalf - 64f, 0f, 100f));
        Lantern(map, new(ShaftX + ShaftHalf + 40f, 0f, 200f));
    }

    private void Lantern(VmfMap map, Point at)
    {
        map.Entities.Add(new VmfEntity { ClassName = "light" }
            .Set("origin", at.ToString())
            .Set("_light", "255 196 120 120")
            .Set("_lightHDR", "-1 -1 -1 1")
            .Set("_fifty_percent_distance", "192")
            .Set("_zero_percent_distance", "512"));
        // prop_dynamic: the lantern model is not compiled with $staticprop.
        Prop(map, palette.Lantern, at + new Point(0f, 0f, -16f), 0f, "prop_dynamic");
    }

    //-------------------------------------------------------------------------
    // The dressing: a water tower by the square, ore carts on a rail spur by
    // the mine, barrels on the porches, a wheel up on the headframe.
    //-------------------------------------------------------------------------

    private void Props(VmfMap map)
    {
        Prop(map, palette.WaterTower, new(-1300f, 700f, 0f), 0f);
        Prop(map, palette.Truss, new(-1300f, -700f, 0f), 90f);

        for (int i = 0; i < 4; i++)
            Prop(map, palette.Track, new(ShaftX - ShaftHalf - 192f - i * 128f, 160f, 2f), 0f);
        Prop(map, palette.OreCart, new(ShaftX - ShaftHalf - 256f, 160f, 4f), 0f);
        Prop(map, palette.OreCart, new(ShaftX - ShaftHalf - 448f, 160f, 4f), 0f);

        foreach (var (_, x, side) in Shops)
            Prop(map, palette.Barrel, new(x + 128f, 300f * side, 12f), 0f);

        Prop(map, palette.Wheel, new(ShaftX, 0f, 360f), 90f);
    }

    private static void Prop(VmfMap map, string model, Point at, float yaw, string className = "prop_static") =>
        map.Entities.Add(new VmfEntity { ClassName = className }
            .Set("model", model)
            .Set("origin", at.ToString())
            .Set("angles", $"0 {yaw} 0")
            .Set("solid", "6"));

    //-------------------------------------------------------------------------
    // Spawns: sixteen in the square facing the mine; eight around the shaft,
    // named so a player coming up from below arrives there.
    //-------------------------------------------------------------------------

    private static void SquareSpawnPoints(VmfMap map)
    {
        for (int i = 0; i < SquareSpawns; i++)
        {
            float x = -1400f + (i % 4) * 64f, y = -96f + (i / 4) * 64f;
            map.Entities.Add(new VmfEntity { ClassName = "info_player_deathmatch" }
                .Set("origin", new Point(x, y, 16f).ToString())
                .Set("angles", "0 0 0"));
        }
    }

    private static void ArrivalSpawnPoints(VmfMap map)
    {
        // A half ring on the west side of the shaft, clear of the fence and cage.
        for (int i = 0; i < ArrivalSpawns; i++)
        {
            float x = ShaftX - ShaftHalf - 224f - (i % 2) * 64f;
            float y = -256f + (i / 2) * 160f;
            map.Entities.Add(new VmfEntity { ClassName = "info_player_deathmatch" }
                .Set("targetname", ArrivalName)
                .Set("origin", new Point(x, y, 16f).ToString())
                .Set("angles", "0 180 0"));
        }
    }

    private static void Add(VmfMap map, Point mins, Point maxs, string material) =>
        map.WorldSolids.Add(VmfMap.Box(mins, maxs, material));
}

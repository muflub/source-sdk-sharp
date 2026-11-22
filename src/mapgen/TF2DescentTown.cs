using System.Globalization;
using SourceSharp.MapGen.Descent;

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
    string WallRed,
    string WallGrey,
    string WallTan,
    string WallBattened,
    string Floor,
    string Beam,
    string Roof,
    string Sky,
    string WaterTower,
    string OreCart,
    string Track,
    string Lantern,
    string Headframe,
    string Barrel,
    string Window,
    string DoorFrame,
    string Awning,
    string Chimney,
    string TallChimney,
    string RockTall,
    string RockLong,
    string Scree,
    string Shrub,
    string Crate,
    string SmallCrate,
    string OilDrum,
    string WoodPile,
    string OreCar,
    string Pole,
    string DangerSign)
{
    /// <summary>The TF2 mining set (plan_host_sdk §2.10, P0(9): each one listed in the TF2 VPKs).</summary>
    public static TownPalette TF2Mining { get; } = new(
        Ground: "nature/dirtground001",
        Street: "nature/gravelground001",
        Cliff: "nature/rockwall012",
        Wall: MineShaft.Lining,
        WallRed: "wood/wood_wall002",
        WallGrey: "wood/wood_wall001",
        WallTan: "wood/wood_wall003",
        WallBattened: "wood/wood_wall006",
        Floor: MineShaft.Floor,
        Beam: "wood/wood_beam03",
        Roof: "metal/wall026",
        Sky: MineShaft.Sky,
        WaterTower: "models/props_mining/watertower.mdl",
        OreCart: "models/props_vehicles/mining_cart_supplies001.mdl",
        Track: "models/props_mining/track_straight_128.mdl",
        Lantern: MineShaft.LanternModel,
        Headframe: "models/props_mining/minehead.mdl",
        Barrel: "models/props_farm/wooden_barrel.mdl",
        Window: "models/props_2fort/window004.mdl",
        DoorFrame: "models/props_farm/doorframe001a.mdl",
        Awning: "models/props_farm/awning002.mdl",
        Chimney: "models/props_2fort/chimney005.mdl",
        TallChimney: "models/props_2fort/chimney004.mdl",
        RockTall: "models/props_mining/rock005.mdl",
        RockLong: "models/props_mining/rock003.mdl",
        Scree: "models/props_mining/rock006b.mdl",
        Shrub: "models/props_foliage/shrub_03_cluster.mdl",
        Crate: "models/props_2fort/miningcrate001.mdl",
        SmallCrate: "models/props_2fort/miningcrate002.mdl",
        OilDrum: "models/props_2fort/oildrum.mdl",
        WoodPile: "models/props_farm/wood_pile.mdl",
        OreCar: "models/props_vehicles/mining_car_metal.mdl",
        Pole: "models/props_2fort/telephonepole001.mdl",
        DangerSign: "models/props_mining/sign001.mdl");

    /// <summary>The brush materials, as a map names them (materials/ and .vmt implied).</summary>
    public IEnumerable<string> Materials => [Ground, Street, Cliff, Wall, WallRed, WallGrey, WallTan, WallBattened, Floor, Beam, Roof];

    public IEnumerable<string> Models =>
    [
        WaterTower, OreCart, Track, Lantern, Headframe, Barrel, Window, DoorFrame, Awning, Chimney, TallChimney,
        RockTall, RockLong, Scree, Shrub, Crate, SmallCrate, OilDrum, WoodPile, OreCar, Pole, DangerSign,
    ];

    /// <summary>
    /// The models TF2 ships without a collision model (<c>.phy</c>). The engine
    /// refuses a static prop placed solid with none ("static prop with bogus
    /// solid type") and makes it non-solid anyway, with a map error at load,
    /// so these are placed non-solid and given player clip where a player
    /// should not walk through them.
    /// </summary>
    public IReadOnlySet<string> WithoutCollision => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { OreCart, Track, Headframe, Scree, Shrub };
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

    /// <summary>
    /// The cliffs' texture scale. At the default a cliff texture repeats every
    /// 128 units and the cliffs read as brickwork; stock badlands maps run
    /// their rock walls at about this scale.
    /// </summary>
    public const float CliffTextureScale = 0.75f;

    // The valley. The sky is high enough for the headframe (924 tall), but
    // only the east cliff behind it rises to it: cliffs that tall all round
    // put the square, where players arrive, in shade the whole afternoon.
    // Above the lower cliffs is sky.
    private const float HalfX = 1536f, HalfY = 1024f, Height = 1152f, CliffHeight = 768f, Wall = 64f, Slab = 16f;

    /// <summary>Where the clip above the lower cliffs starts, a player's height below their tops, and how thick it is.</summary>
    private const float CliffTop = CliffHeight - 72f, ClipThickness = 8f;

    // The shaft: an opening in the ground at the east end (MineShaft's size), and how deep it goes.
    public const float ShaftX = 1300f, ShaftHalf = MineShaft.Half, ShaftDepth = 384f;

    // The cage beside it: MineShaft's square floor and height.
    private const float CageSide = MineShaft.CageSize, CageHalf = MineShaft.CageSize / 2f, CageTop = MineShaft.CageHeight;

    /// <summary>
    /// The rail spur's centre line, y. The track and carts run along X here,
    /// clear of the arrival spawns on either side of it.
    /// </summary>
    public const float SpurY = 160f;

    /// <summary>The track and the carts are modelled along their own Y, so they are turned a quarter to run along X.</summary>
    public const float SpurYaw = 90f;

    /// <summary>The ore cart's half extents once turned along the spur.</summary>
    public const float CartHalfLength = 46f, CartHalfWidth = 24f;

    /// <summary>The track model's length: the spur's pieces sit this far apart so the rails meet.</summary>
    public const float TrackLength = 128f;

    /// <summary>
    /// Where the headframe model stands: its tower over the shaft, its raked
    /// back leg running east over the winch house into the cliff.
    /// </summary>
    public const float HeadframeX = ShaftX, HeadframeYaw = 0f;

    /// <summary>The headframe model's height above its origin.</summary>
    public const float HeadframeHeight = 924f;

    /// <summary>The winch house's west wall, x, and its roof's top, z.</summary>
    public const float WinchHouseX = ShaftX + ShaftHalf + 64f, WinchHouseRoof = 176f;

    /// <summary>Where a player who reaches the shaft floor is sent back to: the arrival ring.</summary>
    public const string ShaftReturnName = "mine_shaft_return";

    public TF2DescentTown() : this(TownPalette.TF2Mining) { }

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
        PortalPadRow(map);

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

        // The cliffs: the east one behind the mine head full height, the
        // others lower with sky above them to seal the valley.
        Add(map, new(HalfX, y0, -Slab), new(x1, y1, Height), palette.Cliff, CliffTextureScale);
        Add(map, new(x0, y0, -Slab), new(-HalfX, y1, CliffHeight), palette.Cliff, CliffTextureScale);
        Add(map, new(-HalfX, y0, -Slab), new(HalfX, -HalfY, CliffHeight), palette.Cliff, CliffTextureScale);
        Add(map, new(-HalfX, HalfY, -Slab), new(HalfX, y1, CliffHeight), palette.Cliff, CliffTextureScale);
        Add(map, new(x0, y0, CliffHeight), new(-HalfX, y1, Height), "tools/toolsskybox");
        Add(map, new(-HalfX, y0, CliffHeight), new(HalfX, -HalfY, Height), "tools/toolsskybox");
        Add(map, new(-HalfX, HalfY, CliffHeight), new(HalfX, y1, Height), "tools/toolsskybox");
        // Player clip inside the lower cliffs, from below their tops to the
        // sky: a blast jump that clears a cliff's lip would land on its top
        // and walk the valley's rim.
        Add(map, new(-HalfX, -HalfY, CliffTop), new(-HalfX + ClipThickness, HalfY, Height), "tools/toolsplayerclip");
        Add(map, new(-HalfX + ClipThickness, -HalfY, CliffTop), new(HalfX, -HalfY + ClipThickness, Height), "tools/toolsplayerclip");
        Add(map, new(-HalfX + ClipThickness, HalfY - ClipThickness, CliffTop), new(HalfX, HalfY, Height), "tools/toolsplayerclip");

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
    // the shops that come later. Each differs in height and planking, the
    // store and saloon put up a western false front, and each faces the
    // street with a door, a window and an awning; chimneys mark the
    // bunkhouses and the smithy. Six identical grey boxes read as a set, not
    // a town.
    //-------------------------------------------------------------------------

    public static IReadOnlyList<(string Name, float X, int Side)> Shops =>
    [
        ("general_store", -800f, 1), ("assay_office", -150f, 1), ("saloon", 500f, 1),
        ("bunkhouse_1", -800f, -1), ("bunkhouse_2", -150f, -1), ("smithy", 500f, -1),
    ];

    /// <summary>What sets a shop apart: its wall height, its planking, a false front, a chimney.</summary>
    public enum ShopChimney { None, Small, Tall }

    /// <summary>A shop's look, by name.</summary>
    public (float Height, string Wall, bool FalseFront, ShopChimney Chimney) StyleOf(string shop) => shop switch
    {
        "general_store" => (256f, palette.WallRed, true, ShopChimney.None),
        "assay_office" => (224f, palette.WallBattened, false, ShopChimney.None),
        "saloon" => (288f, palette.WallTan, true, ShopChimney.None),
        "bunkhouse_1" => (224f, palette.Wall, false, ShopChimney.Small),
        "bunkhouse_2" => (240f, palette.WallGrey, false, ShopChimney.Small),
        "smithy" => (224f, palette.WallRed, false, ShopChimney.Tall),
        _ => throw new ArgumentOutOfRangeException(nameof(shop), shop, "not a shop"),
    };

    /// <summary>A building's street face, y; its back wall is 384 further from the street.</summary>
    public const float ShopFront = 288f;

    /// <summary>The porch's depth in front of the street face, and its top.</summary>
    public const float PorchDepth = 32f, PorchTop = 12f;

    /// <summary>How far a false front rises above the roof slab.</summary>
    public const float FalseFrontRise = 80f;

    /// <summary>
    /// The two stalls with no building of their own, the Engineer's armour
    /// stall and his craft bench: each an <c>info_target</c> in front of the
    /// smithy, one either side of its porch, where the server stands them.
    /// </summary>
    public const string EngineerMarker = "shop_engineer", BenchMarker = "shop_bench";

    /// <summary>The building the Engineer's stalls stand in front of.</summary>
    public const string EngineerShop = "smithy";

    /// <summary>
    /// How far either side of the smithy's own marker the Engineer's two
    /// stand: just past the ends of its porch (320 wide), so the porch, its
    /// barrel and the door stay clear. The server stood them here before the
    /// town had markers for them, so a map with them moves nothing.
    /// </summary>
    public const float EngineerBeside = 176f;

    /// <summary>
    /// The town portals' exit pads: an <c>info_target</c> the server starts
    /// the row at, the row running across the marker's facing, a pad every
    /// 96 units out from it, alternating sides.
    /// </summary>
    public const string PortalMarker = "tf2_descent_portal";

    /// <summary>
    /// Where the pad row stands: across the street where it leaves the
    /// square, facing back at the square, so a player who spawns there faces
    /// the pads; west of the boardwalks, so the row runs onto open ground on
    /// either side of the street without a step.
    /// </summary>
    public const float PortalX = -1136f;

    /// <summary>How many pads the row has room for (one per arrival spawn), and the server's gap between two.</summary>
    public const int PortalPads = ArrivalSpawns;

    public const float PortalSpacing = 96f;

    private void Buildings(VmfMap map)
    {
        foreach (var (name, x, side) in Shops)
        {
            var (height, wall, falseFront, chimney) = StyleOf(name);
            float near = ShopFront * side, far = (ShopFront + 384f) * side;
            float y0 = Math.Min(near, far), y1 = Math.Max(near, far);

            Add(map, new(x - 224f, y0, 0f), new(x + 224f, y1, height), wall);
            // The roof: a slab with an overhang, and a ridge along it.
            Add(map, new(x - 240f, y0 - 16f, height), new(x + 240f, y1 + 16f, height + 16f), palette.Roof);
            Add(map, new(x - 240f, (y0 + y1) / 2f - 64f, height + 16f), new(x + 240f, (y0 + y1) / 2f + 64f, height + 48f), palette.Roof);
            // The porch, on the street side.
            float porchNear = side > 0 ? y0 - PorchDepth : y1, porchFar = side > 0 ? y0 : y1 + PorchDepth;
            Add(map, new(x - 160f, porchNear, 0f), new(x + 160f, porchFar, PorchTop), palette.Floor);

            // A false front: the street wall carried up past the roof, the
            // western storefront's silhouette.
            if (falseFront)
            {
                float f0 = side > 0 ? y0 - 16f : y1, f1 = side > 0 ? y0 : y1 + 16f;
                Add(map, new(x - 224f, f0, height), new(x + 224f, f1, height + 16f + FalseFrontRise), wall);
            }

            // The street face: a door left of the lantern, a window right of
            // it, an awning over the door. The door is a darker panel in the
            // wall, framed; models modelled facing +X turn to face the street.
            float faceYaw = side > 0 ? 270f : 90f;
            float doorX = x - 96f, windowX = x + 96f;
            float panel0 = side > 0 ? near - 4f : near, panel1 = side > 0 ? near : near + 4f;
            Add(map, new(doorX - 56f, panel0, PorchTop), new(doorX + 56f, panel1, PorchTop + 148f), palette.WallRed == wall ? palette.WallGrey : palette.WallRed);
            Prop(map, palette.DoorFrame, new(doorX, near - 4f * side, PorchTop), faceYaw);
            Prop(map, palette.Awning, new(doorX, near, PorchTop + 180f), faceYaw);
            Prop(map, palette.Window, new(windowX, near, 112f), faceYaw);

            if (chimney != ShopChimney.None)
                Prop(map, chimney == ShopChimney.Tall ? palette.TallChimney : palette.Chimney,
                    new(x + 128f, (y0 + y1) / 2f + 120f * side, height + 16f), 0f);

            float markerY = side > 0 ? y0 - 48f : y1 + 48f;
            Marker(map, "shop_" + name, new(x, markerY, 64f));
            if (name == EngineerShop)
            {
                Marker(map, EngineerMarker, new(x - EngineerBeside, markerY, 64f));
                Marker(map, BenchMarker, new(x + EngineerBeside, markerY, 64f));
            }
        }
    }

    private static void Marker(VmfMap map, string name, Point at) =>
        map.Entities.Add(new VmfEntity { ClassName = "info_target" }
            .Set("targetname", name)
            .Set("origin", at.ToString()));

    //-------------------------------------------------------------------------
    // The mine head: TF2's steel headframe over the shaft, the landmark the
    // street leads to, a fence on three sides, the winch house, and on the
    // open side the cage with its button. Below, a trigger across the shaft
    // for anyone who jumps, and under it a way back up.
    //-------------------------------------------------------------------------

    private void MineHead(VmfMap map)
    {
        float sx0 = ShaftX - ShaftHalf, sx1 = ShaftX + ShaftHalf;

        // The headframe: TF2's steel minehead over the shaft.
        Prop(map, palette.Headframe, new(HeadframeX, 0f, 0f), HeadframeYaw);

        // The fence: north, south and east of the opening, waist high.
        Add(map, new(sx0, -ShaftHalf - 8f, 0f), new(sx1, -ShaftHalf, 40f), palette.Beam);
        Add(map, new(sx0, ShaftHalf, 0f), new(sx1, ShaftHalf + 8f, 40f), palette.Beam);
        Add(map, new(sx1, -ShaftHalf, 0f), new(sx1 + 8f, ShaftHalf, 40f), palette.Beam);

        // The winch house, behind the shaft against the cliff.
        Add(map, new(WinchHouseX, -128f, 0f), new(HalfX, 128f, WinchHouseRoof - 16f), palette.Wall);
        Add(map, new(WinchHouseX - 16f, -144f, WinchHouseRoof - 16f), new(HalfX, 144f, WinchHouseRoof), palette.Roof);

        // The cage, on the open (west) side: a floor, bars on two sides, a roof.
        float cx0 = sx0 - CageSide, cx1 = sx0;
        Add(map, new(cx0, -CageHalf, 0f), new(cx1, CageHalf, 8f), palette.Floor);
        Add(map, new(cx0, -CageHalf - 8f, 8f), new(cx1, -CageHalf, CageTop - 8f), palette.Beam);
        Add(map, new(cx0, CageHalf, 8f), new(cx1, CageHalf + 8f, CageTop - 8f), palette.Beam);
        Add(map, new(cx0, -CageHalf - 8f, CageTop - 8f), new(cx1, CageHalf + 8f, CageTop), palette.Beam);

        // The button on the cage's south bar: pressed, it sends the player down.
        var button = new VmfEntity { ClassName = "func_button" }
            .Set("targetname", "mine_cage_button")
            .Set("spawnflags", "1025")   // don't move; use activates
            .Set("wait", "3")
            .Set("speed", "5")
            .Wire("OnPressed", TransitionName, "Transition");
        button.Solids.Add(VmfMap.Box(new(cx0 + 48f, -CageHalf, 48f), new(cx0 + 80f, -CageHalf + 8f, 72f), palette.Beam));
        map.Entities.Add(button);

        // Halfway down the shaft: whoever jumps is sent down before landing.
        var shaftTrigger = new VmfEntity { ClassName = "trigger_multiple" }
            .Set("targetname", "mine_shaft_trigger")
            .Set("spawnflags", "1")      // clients
            .Set("wait", "1")
            .Wire("OnStartTouch", TransitionName, "Transition");
        shaftTrigger.Solids.Add(VmfMap.Box(new(sx0, -ShaftHalf, -ShaftDepth + 32f), new(sx1, ShaftHalf, -ShaftDepth / 2f), "tools/toolstrigger"));
        map.Entities.Add(shaftTrigger);

        // The floor, below that trigger: a player only gets here when the
        // transition did not happen (no host, or the hop was refused). TF2 has
        // no ladders and the shaft is too deep to jump out of, so without this
        // a failed hop strands the player; it sends them back up to the
        // arrival ring instead.
        var shaftReturn = new VmfEntity { ClassName = "trigger_teleport" }
            .Set("targetname", "mine_shaft_floor")
            .Set("target", ShaftReturnName)
            .Set("spawnflags", "1");     // clients
        shaftReturn.Solids.Add(VmfMap.Box(new(sx0, -ShaftHalf, -ShaftDepth), new(sx1, ShaftHalf, -ShaftDepth + 32f), "tools/toolstrigger"));
        map.Entities.Add(shaftReturn);
        map.Entities.Add(new VmfEntity { ClassName = "info_teleport_destination" }
            .Set("targetname", ShaftReturnName)
            .Set("origin", new Point(ShaftX - ShaftHalf - 256f, -SpurY, 16f).ToString())
            .Set("angles", "0 180 0"));

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

    /// <summary>
    /// The sun's and the sky's brightness. The buildings, the water tower and
    /// the cliffs leave most of the street and square in shade, so the sky
    /// light is what a player arriving sees by: at 450 against 70 the spawn
    /// views were half black pixels in the map preview, at 400 against 200 a
    /// tenth. Two to one keeps the shadows readable without losing the sun.
    /// The colours stay <see cref="MineShaft.Sun"/>'s, which level 1 shares;
    /// the town, open to the sky, is lit brighter.
    /// </summary>
    public const int SunBrightness = 400, AmbientBrightness = 200;

    /// <summary>
    /// The town's sun is five degrees higher than the shared sun: the 924-tall
    /// headframe and the east cliff behind it shadowed the square at 50.
    /// </summary>
    public const string SunAngles = "-55 35 0", SunPitch = "-55";

    /// <summary>A light's "r g b brightness" with its brightness replaced.</summary>
    public static string WithBrightness(string light, int brightness) =>
        light[..(light.LastIndexOf(' ') + 1)] + brightness.ToString(CultureInfo.InvariantCulture);

    private void Light(VmfMap map)
    {
        Sun sun = MineShaft.Sun;
        map.Entities.Add(new VmfEntity { ClassName = "light_environment" }
            .Set("origin", new Point(0f, 0f, Height - 64f).ToString())
            .Set("angles", SunAngles)
            .Set("pitch", SunPitch)
            .Set("_light", WithBrightness(sun.Light, SunBrightness))
            .Set("_ambient", WithBrightness(sun.Ambient, AmbientBrightness))
            .Set("_lightHDR", "-1 -1 -1 1")
            .Set("_ambientHDR", "-1 -1 -1 1"));

        foreach (var (_, x, side) in Shops)
            Lantern(map, new(x, 248f * side, 96f));
        Lantern(map, new(ShaftX - ShaftHalf - 64f, 0f, 100f));
        // Off the winch house's west wall, under its eave, the same 40 units out
        // as the porch lanterns.
        Lantern(map, new(WinchHouseX - 40f, 0f, WinchHouseRoof - 56f));
        // Down the shaft, so a look over the fence sees its timbering; below the
        // headframe's legs, which reach 131 under the ground and shadow it.
        Lantern(map, new(ShaftX, ShaftHalf - 24f, -200f));
    }

    private void Lantern(VmfMap map, Point at)
    {
        map.Entities.Add(MineShaft.Lantern.Entity(at));
        // prop_dynamic: the lantern model is not compiled with $staticprop.
        Prop(map, palette.Lantern, at + new Point(0f, 0f, -16f), 0f, "prop_dynamic");
    }

    //-------------------------------------------------------------------------
    // The dressing: a water tower by the square, ore carts on a rail spur by
    // the mine, barrels on the porches, rock at the cliffs' feet, crates and
    // drums in the alleys, poles down the street.
    //-------------------------------------------------------------------------

    private void Props(VmfMap map)
    {
        Prop(map, palette.WaterTower, new(-1300f, 700f, 0f), 0f);

        for (int i = 0; i < 4; i++)
            Prop(map, palette.Track, new(ShaftX - ShaftHalf - 192f - i * TrackLength, SpurY, 2f), SpurYaw);
        float cartX = ShaftX - ShaftHalf - 256f;
        Prop(map, palette.OreCart, new(cartX, SpurY, 4f), SpurYaw);
        // The supply cart has no collision model: clip its body so players
        // walk round it, not through it. Turned along the spur it is 88 long
        // and 48 wide. Behind it, a plain ore car, which has one.
        Add(map, new(cartX - CartHalfLength, SpurY - CartHalfWidth, 4f), new(cartX + CartHalfLength, SpurY + CartHalfWidth, 124f), "tools/toolsplayerclip");
        Prop(map, palette.OreCar, new(ShaftX - ShaftHalf - 448f, SpurY, 4f), SpurYaw);

        Rocks(map);
        AlleyDressing(map);

        // Oil drums for the winch, between the fence and the winch house, and
        // a danger sign at the shaft's open corner by the cage.
        Prop(map, palette.OilDrum, new(WinchHouseX - 28f, 100f, 0f), 0f);
        Prop(map, palette.OilDrum, new(WinchHouseX - 28f, 136f, 0f), 30f);
        Prop(map, palette.DangerSign, new(ShaftX - ShaftHalf + 16f, -ShaftHalf - 32f, 0f), 0f);

        // Telephone poles along the north side, in the alley mouths and at the
        // square: a vertical rhythm down the street. The square's stands west
        // of the portal pads' row, which reaches the alley-mouth line.
        foreach (float px in new[] { -1216f, -475f, 175f })
            Prop(map, palette.Pole, new(px, AlleyMouth, 0f), 90f);

        // On the porch against the wall: the barrel's origin is its centre,
        // 23 across and 30 up.
        foreach (var (_, x, side) in Shops)
            Prop(map, palette.Barrel, new(x + 128f, (ShopFront - 24f) * side, PorchTop + 30f), 0f);

    }

    /// <summary>The y of the alleys' mouths, just behind the boardwalks' outer edge.</summary>
    public const float AlleyMouth = 272f;

    /// <summary>The two alleys between each side's three buildings, x.</summary>
    public static IReadOnlyList<float> AlleyXs => [-475f, 175f];

    /// <summary>
    /// Rock at the cliffs' feet: a brush cliff meets the ground and its
    /// neighbours in hard right angles, which the preview showed plainly from
    /// every height. A tall rock in each corner, long rocks along the long
    /// cliffs, scree and brush in front.
    /// </summary>
    /// <remarks>
    /// A rock's mesh is not a box, so between it and the cliff it leans on
    /// are pockets a player can fall into and not get out of: the preview's
    /// fuzzer wedged players there and found a one-position trap behind a
    /// corner rock. Player clip fills the space behind each rock: in a corner
    /// a column to the sky (no perch on its top either), along a cliff a box
    /// as tall as the rock.
    /// </remarks>
    private void Rocks(VmfMap map)
    {
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sy in new[] { -1f, 1f })
        {
            float cx = sx * (HalfX - CornerRockInset), cy = sy * (HalfY - CornerRockInset);
            Prop(map, palette.RockTall, new(cx, cy, 0f), sx * sy > 0f ? 30f : 120f);
            Prop(map, palette.Shrub, new(cx - sx * 150f, cy - sy * 60f, 0f), sy > 0 ? 180f : 0f);
            float x0 = sx > 0 ? HalfX - CornerClip : -HalfX, x1 = sx > 0 ? HalfX : -HalfX + CornerClip;
            float y0 = sy > 0 ? HalfY - CornerClip : -HalfY, y1 = sy > 0 ? HalfY : -HalfY + CornerClip;
            Add(map, new(x0, y0, 0f), new(x1, y1, Height), "tools/toolsplayerclip");
        }

        foreach (float x in LongRockXs)
        foreach (float sy in new[] { -1f, 1f })
        {
            Prop(map, palette.RockLong, new(x, sy * (HalfY - LongRockInset), 0f), 90f);
            Prop(map, palette.Scree, new(x + 260f, sy * (HalfY - 150f), 0f), sy > 0 ? 200f : 20f);
            float y0 = sy > 0 ? HalfY - LongRockInset : -HalfY, y1 = sy > 0 ? HalfY : -HalfY + LongRockInset;
            Add(map, new(x - 210f, y0, 0f), new(x + 210f, y1, 240f), "tools/toolsplayerclip");
        }
    }

    /// <summary>A corner rock's centre, in from both cliffs; and the clip column behind it, square from the corner.</summary>
    public const float CornerRockInset = 140f, CornerClip = 280f;

    /// <summary>A long rock's centre line, in from its cliff: its back (106 deep) meets the cliff.</summary>
    public const float LongRockInset = 100f;

    /// <summary>Where the long rocks stand along the north and south cliffs, x.</summary>
    public static IReadOnlyList<float> LongRockXs => [-900f, 300f];

    /// <summary>The alley mouths between the buildings: crates, drums, a wood pile.</summary>
    private void AlleyDressing(VmfMap map)
    {
        float a = AlleyXs[0], b = AlleyXs[1];
        // North: a crate stack by the store, drums by the saloon.
        Prop(map, palette.Crate, new(a - 40f, AlleyMouth + 60f, 0f), 90f);
        Prop(map, palette.SmallCrate, new(a - 40f, AlleyMouth + 60f, 39f), 15f);
        Prop(map, palette.Crate, new(a + 30f, AlleyMouth + 90f, 0f), 0f);
        Prop(map, palette.OilDrum, new(b - 50f, AlleyMouth + 60f, 0f), 0f);
        Prop(map, palette.OilDrum, new(b - 50f, AlleyMouth + 96f, 0f), 40f);
        Prop(map, palette.OilDrum, new(b - 14f, AlleyMouth + 78f, 0f), 75f);
        // South: the smithy's wood pile and a crate stack by the bunkhouses.
        Prop(map, palette.WoodPile, new(b + 40f, -AlleyMouth - 140f, 14f), 0f);
        Prop(map, palette.Crate, new(a + 40f, -AlleyMouth - 60f, 0f), 90f);
        Prop(map, palette.SmallCrate, new(a - 30f, -AlleyMouth - 70f, 0f), 50f);
    }

    private void Prop(VmfMap map, string model, Point at, float yaw, string className = "prop_static") =>
        map.Entities.Add(new VmfEntity { ClassName = className }
            .Set("model", model)
            .Set("origin", at.ToString())
            .Set("angles", $"0 {yaw} 0")
            .Set("solid", palette.WithoutCollision.Contains(model) ? "0" : "6"));

    //-------------------------------------------------------------------------
    // Spawns: sixteen in the square facing the mine; eight around the shaft,
    // named so a player coming up from below arrives there; and where the
    // portal pads stand, in front of the square's spawns.
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

    private static void PortalPadRow(VmfMap map) =>
        map.Entities.Add(new VmfEntity { ClassName = "info_target" }
            .Set("targetname", PortalMarker)
            .Set("origin", new Point(PortalX, 0f, 16f).ToString())
            .Set("angles", "0 180 0"));

    private static void Add(VmfMap map, Point mins, Point maxs, string material, float textureScale = VmfMap.DefaultTextureScale) =>
        map.WorldSolids.Add(VmfMap.Box(mins, maxs, material, textureScale: textureScale));
}

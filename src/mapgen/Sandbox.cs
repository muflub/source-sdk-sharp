using SourceSharp.Shared;
namespace SourceSharp.MapGen;

/// <summary>
/// ss_sandbox: one worked example of every map entity this mod implements.
///
/// The map is a single sealed hall divided into a grid of plots. Each plot is a
/// station: a group of entities that demonstrates one family, a description that
/// appears when you walk in, and a button that puts the station back how it
/// started.
///
/// Two rules shape the whole thing.
///
/// First, the descriptions are game_text rather than anything in the world. A
/// mod with no NPCs and no custom content has no other way to say a sentence to
/// a player, and point_message -- the obvious choice -- calls
/// UTIL_GetLocalPlayer, which returns NULL in multiplayer and asserts under
/// developer 1. So it would break a debug build and show nothing in a release
/// one.
///
/// Second, every station gets its furniture from <see cref="Build"/> rather than
/// building its own. A station that forgot its reset button would still look
/// finished, and the point of the map is that every entity has one.
/// </summary>
public static class Sandbox
{
    /// <summary>Plot pitch. Wide enough that two stations cannot trigger each other.</summary>
    public const float Cell = 640f;

    public const int Columns = 6;

    /// <summary>
    /// How many rows the station list actually needs.
    ///
    /// Derived rather than written down, because the alternative is what
    /// happened the first time a station was added past the grid: the plot
    /// landed outside the wall, the map LEAKED, vbsp skipped visibility, and the
    /// only symptom was a fullbright map that still loaded.
    /// </summary>
    public static int Rows => (Stations.Count + SpawnPlots + Columns - 1) / Columns;

    /// <summary>
    /// Plots the station list is not allowed to fill, because a map with nowhere
    /// to spawn is a map nobody can look at.
    ///
    /// <para>
    /// <see cref="Spawns"/> puts an <c>info_player_deathmatch</c> in every cell
    /// the stations do not reach, and until this existed the count of those was
    /// whatever the arithmetic happened to leave: with forty-seven stations over
    /// six columns it was ONE, and the forty-eighth station would have taken
    /// that one -- leaving a sealed hall, correct in every other respect, with no
    /// spawn point in it at all. Nothing would have said so; the map compiles and
    /// loads, and the player arrives outside the room.
    /// </para>
    ///
    /// <para>
    /// Four, which is what docs/sandbox.md has always said the back row is for.
    /// </para>
    /// </summary>
    private const int SpawnPlots = 4;

    /// <summary>Half the interior, corner to corner. Whatever the grid needs.</summary>
    public static float Half => (Math.Max(Columns, Rows) * Cell * 0.5f) + 128f;

    public const float Ceiling = 640f;

    private const float WallThickness = 32f;

    /// <summary>Half the skylight over the worldfx plot. Inside the plot, so it lights one station.</summary>
    private const float SkylightHalf = 192f;

    /// <summary>
    /// Where a station sits in the list, by slug.
    ///
    /// <para>
    /// The hall needs this because the skylight is cut over ONE plot and the
    /// plot's position is derived from the list order, which changes whenever a
    /// station is added. Looked up rather than written down: an index written
    /// down would go on compiling and start lighting somebody else's plot.
    /// </para>
    /// </summary>
    private static int IndexOfStation(string slug)
    {
        for (int i = 0; i < Stations.Count; i++)
            if (Stations[i].Slug == slug)
                return i;

        throw new InvalidOperationException($"no station called '{slug}'");
    }

    //-------------------------------------------------------------------------
    // Lighting, in one place because it is the thing most likely to want
    // changing. The first version used 500 with a 700-unit half distance across
    // nine lights, which washed the whole hall white -- every surface hit full
    // brightness and the map read as flat.
    //-------------------------------------------------------------------------
    private const string CeilingLight = "255 248 235 140";
    private const string CeilingHalfDistance = "500";
    private const string CeilingZeroDistance = "1100";

    /// <summary>A small warm light over each reset button, to draw the eye to it.</summary>
    private const string ButtonLight = "255 200 120 45";

    /// <summary>One family of entities, and what a player should see happen.</summary>
    public sealed record Station(
        string Slug,
        string Title,
        string Expect,
        Action<Site> Build);

    //-------------------------------------------------------------------------
    // Models. Stock HL2 content, because this mod ships none of its own and a
    // missing model spawns an error sign rather than failing loudly.
    //-------------------------------------------------------------------------
    private const string Crate  = "models/props_junk/wood_crate001a.mdl";
    private const string Drum   = "models/props_c17/oildrum001.mdl";
    private const string Boom   = "models/props_c17/oildrum001_explosive.mdl";
    private const string Chair  = "models/props_c17/FurnitureChair001a.mdl";
    private const string Can    = "models/props_junk/PopCan01a.mdl";
    private const string Door   = "models/props_doors/door01_dynamic.mdl";

    // Two with no prop data, so a plain prop_dynamic (and an ornament, which is
    // one) keeps them in stock: everything above is a physics prop's model.
    private const string Sink    = "models/props_c17/furnituresink001a.mdl";
    private const string Fixture = "models/props_c17/lampfixture01a.mdl";

    /// <summary>
    /// The one material here chosen for what vbsp does with it. A face carrying
    /// %compilewater becomes a volume you can swim in; any other texture makes
    /// the rising pool an ordinary slab that happens to be blue. The cheap
    /// variant on purpose -- $forcecheap water needs no reflection buffer, so
    /// the pool does not drag a water_lod_control and a cubemap in behind it.
    ///
    /// <para>
    /// Stock canal water rather than the dev water that used to be here. The
    /// two .vmt files are the same file with different tints: both are
    /// "Water", both carry %compilewater and $forcecheap 1, both name
    /// dev/dev_waterbeneath2 as their $bottommaterial. This one also declares
    /// $abovewater 1, which the dev material leaves to the default -- so the
    /// real material is, if anything, the better specified of the two.
    /// </para>
    /// </summary>
    private const string Water  = "nature/water_canals_cheap001";

    /// <summary>
    /// The mirror pool's water: stock canal water that IS expensive -- it
    /// declares $forceexpensive 1, a $reflecttexture and a $refracttexture at
    /// dx90 -- so the renderer draws the water's reflection and refraction views
    /// for it (viewrender.cpp's CAboveWaterView). It is the render plan's water
    /// oracle (plan_client_novalve C7 step 1): the one place in either map where
    /// those views are drawn.
    /// </summary>
    private const string MirrorWater = "nature/water_canals_water2";

    /// <summary>
    /// The sprite sheet vbsp cuts grass and rubble tufts out of, named by the
    /// world's `detailmaterial`. UnlitGeneric with vertex colour and alpha --
    /// a detail sprite is a camera-facing card, not a surface, so it is
    /// neither brush nor model art and gets checked as neither.
    /// </summary>
    private const string DetailSprites = "detail/detailsprites";

    //-------------------------------------------------------------------------
    // Decals. PROJECTED, never painted on a face -- which is why they are held
    // apart from everything above and checked by a different rule: a decal
    // material on a brush face tiles a translucent sign across the whole wall.
    //-------------------------------------------------------------------------

    /// <summary>Stamped where it stands by infodecal, which needs a trace to find a surface.</summary>
    private const string StampDecal = "decals/decalsigndanger001a";

    /// <summary>Thrown along a facing by info_projecteddecal, which lets the engine find the surface.</summary>
    private const string ThrownDecal = "decals/decalsigncaution001b";

    //-------------------------------------------------------------------------
    // Tool materials, which are not textures at all: each one is a flag vbsp
    // reads off the face and then throws the face away. Here rather than beside
    // the drawn materials in Site because nothing DRAWS any of them -- a brush
    // carrying one is a statement to the compiler.
    //-------------------------------------------------------------------------

    /// <summary>Fills a doorway and becomes a hole vbsp can shut. See the visibility station.</summary>
    private const string AreaPortal = "TOOLS/TOOLSAREAPORTAL";

    /// <summary>Marks a brush as an occluder, which vbsp turns into occluder polygons.</summary>
    private const string Occluder = "TOOLS/TOOLSOCCLUDER";

    /// <summary>A volume with no surface: what a weather brush is made of.</summary>
    /// <remarks>
    /// NOT for a dust volume — see <see cref="SolidVolume"/>. vbsp's
    /// %compileInvisible does <c>contents &amp;= ~CONTENTS_SOLID; contents |=
    /// CONTENTS_GRATE</c> (utils/vbsp/textures.cpp:202-209), so a brush wearing
    /// this is not solid, and anything that finds its own volume by asking the
    /// engine for point contents finds nothing inside it.
    /// </remarks>
    private const string Invisible = "TOOLS/TOOLSINVISIBLE";

    /// <summary>
    /// An invisible volume that is still SOLID to a point test: what a dust
    /// brush is made of.
    ///
    /// <para>
    /// %compileTrigger sets surface flags only (SURF_NOLIGHT | SURF_TRIGGER,
    /// plus SURF_NODRAW under -nodrawtriggers) and never touches the contents
    /// word, so utils/vbsp/map.cpp:2745's "no visible contents" rule gives the
    /// brush CONTENTS_SOLID. It draws nothing, exactly like TOOLSINVISIBLE, and
    /// is what Valve's own maps use for these entities — cs_militia.vmf
    /// textures all seven of its func_dustmotes with it, and the func_dustmotes
    /// brush model of every shipped TF2 map that has one reads back contents
    /// 0x00000001.
    /// </para>
    ///
    /// <para>
    /// WHY IT MATTERS HERE AND NOT FOR THE NEIGHBOURS. C_Func_Dust picks each
    /// mote's position by testing random points against its own brush --
    /// <c>enginetrace->GetPointContents_Collideable(...) &amp; CONTENTS_SOLID</c>
    /// -- and puts a mote only where that answers true. On a TOOLSINVISIBLE
    /// brush it never does: 8e-render measured 4,050 spawn calls, 40,500 point
    /// tests, zero motes, and spent a lane eliminating causes above the map
    /// while the map was the cause. func_precipitation, func_smokevolume and
    /// env_embers do not ask the engine about their own brush, so TOOLSINVISIBLE
    /// is still right for them and they are deliberately left alone.
    /// </para>
    /// </summary>
    private const string SolidVolume = "TOOLS/TOOLSTRIGGER";

    /// <summary>Sky. A face carrying it draws the 2D skybox and still seals the map.</summary>
    private const string Sky = "TOOLS/TOOLSSKYBOX";

    public static IReadOnlyList<Station> Stations { get; } =
    [
        new("markers", "MARKERS AND SPAWN POINTS",
            "Nothing to see: these mark places other entities name, or nothing at all.", Markers),

        new("health", "HEALTH AND ARMOUR",
            "Walk over each one. The chargers top you up on contact.", Health),

        new("ammo", "AMMUNITION",
            "Every ammo type the mod defines, in one row.", Ammo),

        new("weapons", "WEAPONS",
            "Every weapon the mod defines, lying where a map would leave them.", Weapons),

        new("propsdyn", "DYNAMIC PROPS",
            "Models that animate but are never simulated. The door opens.", DynamicProps),

        new("propsphys", "PHYSICS PROPS",
            "Shoot them. The red barrel explodes, and comes back by itself.", PhysicsProps),

        new("doors", "DOORS",
            "Press USE on each. One slides, one swings, one is water in name only.", Doors),

        new("buttons", "BUTTONS",
            "Press USE. Two toggle the brush beside them; the wheel winds a slab up.", Buttons),

        new("movers", "PLATFORMS AND MOVERS",
            "A lift, a sliding brush, a filling tank and a spinner, all started by USE.", Movers),

        new("belts", "A LIFT THAT TURNS, AND A BELT",
            "Ride the lift, then walk onto the belt. The cans on it are NOT carried.", Belts),

        new("trains", "TRAINS AND PATH CORNERS",
            "The button sends the slab round four corners. It pauses at the red one.", Trains),

        new("tracks", "TRACK TRAINS",
            "The button sends the train to the far end and back. Its controls are inert.", Tracks),

        new("turntables", "TRACK CHANGERS",
            "Left swings when its button is pressed; right swings when its train arrives.", Turntables),

        new("keyframes", "KEYFRAMED MOVEMENT",
            "An invisible mover walks its keys, and the crate copies what it does.", Keyframes),

        new("walls", "WALLS AND SURFACES",
            "Solid, decorative, toggleable and slippery, left to right.", Walls),

        new("breakable", "BREAKABLES",
            "Shoot the panel, then the glass. They break in different ways.", Breakables),

        new("forces", "FORCE TRIGGERS",
            "Pads: hurt, gravity, push, impulse, float. Wind at the back, a shove on USE.", Forces),

        new("playermove", "PLAYER MOVEMENT",
            "A plate that weighs its load, a pipe you duck under, and a leash on the button.", PlayerMovement),

        new("teleports", "TELEPORT TRIGGERS",
            "Step in the near pad. Both move you within this station.", Teleports),

        new("detect", "DETECTION TRIGGERS",
            "Pads that notice you entering, once, looking, in range, or using. One cannot.", Detection),

        new("filters", "FILTERS",
            "Each pad fires only for what its filter allows. USE asks two of them outright.", Filters),

        new("timing", "TIMING LOGIC",
            "A timer ticks into a counter. Two listeners wait on events; one never hears.", Timing),

        new("branching", "BRANCHING LOGIC",
            "A branch, a case, and two comparisons, driven by the button.", Branching),

        new("math", "ARITHMETIC",
            "A counter feeds a remap and a colour blend.", Arithmetic),

        new("rules", "GAME RULES",
            "Scoring, teams, budgets, controls and two consoles. game_end is NOT wired.", Rules),

        new("constraints", "CONSTRAINTS",
            "Two props joined four ways. Shove them and watch the joints.", Constraints),

        new("joints", "ROPES AND RAILS",
            "A pulley, a rail with a motor on it, and a joint that may only turn so far.", Joints),

        new("controllers", "MOTION CONTROLLERS",
            "A motor, a thruster, a torque and an upright keeper.", Controllers),

        new("vphysics", "A WELL, A ROCKET AND A MAGNET",
            "Wade into the left pen. The crate drifts down, the cannister lights once and flies, the magnet pulls your corpse.",
            VPhysics),

        new("sensors", "SENSORS",
            "Two sensors reading the spinning drum's velocity.", Sensors),

        new("emitters", "EMITTERS",
            "Step on a pad. The reset button sets off the visible kind.", Emitters),

        new("physbrush", "PHYSICS BRUSHES",
            "Simulated brushes. One you shove by walking. The button drops the hanging slab and the left crate. "
            + "The two middle boxes are different classes and behave identically, which is the point.", PhysicsBrushes),

        new("spawning", "SPAWNING FROM A TEMPLATE",
            "The reset button makes another crate. Stand on the spot and it will not.", Spawning),

        new("effects", "EFFECTS ON OTHER ENTITIES",
            "The button lights the barrel, starts the sparks, and opens fire across the back.",
            Effects),

        new("fire", "FIRE",
            "The reset button lights the left fire. It spreads right, and the sensor notices.", Fire),

        new("retarget", "REWRITING WHAT SOMETHING POINTS AT",
            "Press SWAP, then LIFT. The lamp swings right about a tenth of a second later, "
            + "and LIFT raises the far crate instead of the near one.", Retarget),

        new("camera", "CAMERA CONTROL",
            "Press the button. It holds you still for ten seconds and flies a loop watching you, "
            + "then hands your eyes back. Your gun stays drawn, which is what stock does too.", Camera),

        new("inert", "VOLUMES WITH NOTHING TO ACT ON",
            "Three shapes. The left one deletes itself and plays nothing, the middle one notices "
            + "you and can mark nothing, and the right one is never asked about at all.", Inert),

        new("soundscape", "SOUNDSCAPES AND OTHER NOISE",
            "Walk the row and listen. Three soundscapes, a volume that hands one out, "
            + "a looping machine, a meter reading it, and two things the button sets off.",
            Soundscapes),

        new("notices", "MESSAGES TO A PLAYER",
            "Press the button: a titles.txt message and a key hint. The third one talks "
            + "only to whoever stands next to it. The back row is written in the world "
            + "instead, one line per orientation mode -- walk round it.", Notices),

        new("visibility", "WHAT THE RENDERER IS TOLD TO SKIP",
            "A sealed room with a real door and a real window in it, and four slabs beside "
            + "it. Only the last of the four is solid to you, and only to a vehicle.",
            Visibility),

        new("weather", "VOLUMES THAT EMIT",
            "Seven boxes you cannot see, each full of something you can. The button "
            + "switches the back row on and off.", Weather),

        new("trails", "TRAILS THAT FOLLOW SOMETHING",
            "Six point entities from one file, each drawing a plume that is put back "
            + "where its entity is every frame rather than every update.", Trails),

        new("water", "A POOL, AND WHAT NOTICES IT",
            "Wade in. Bubbles rise from the far end, and the button drops a splash "
            + "on the surface.", Pool),

        new("worldfx", "THE SKY, THE SUN AND THE WHOLE SCREEN",
            "The only plot with sky over it. The button changes the sky, and the three "
            + "lights here are the only ones in the hall that survive the compiler.",
            WorldEffects),

        new("oneshots", "EFFECTS THAT HAPPEN ONCE",
            "Press the button and watch the row. Two of them mark the wall and stay, "
            + "one of them fires and deletes itself, and one draws nothing at all.",
            OneShots),

        new("sprites", "SPRITES",
            "Three names for one class, and a fourth that trails. The left one is "
            + "additive and always on; the middle two are glows that fade behind the "
            + "pillar. The trail at the front draws nothing, because nothing here can "
            + "move it. The button toggles all four and turns the right glow blue.",
            Sprites),

        new("tempents", "TEMP ENTITIES",
            "Press the button. Forty-three one-shot effects sweep the plot, one per temp "
            + "entity this build sends. Nothing here is an entity you can walk up to.",
            TempEntities),
    ];

    /// <summary>Where a plot sits, by its position in the list.</summary>
    public static Point SiteOf(int index)
    {
        int column = index % Columns;
        int row = index / Columns;

        // Centred on the grid the list actually fills, on both axes, so that a
        // grid which is not square still sits in the middle of the hall.
        return new Point(
            (column - ((Columns - 1) * 0.5f)) * Cell,
            (row - ((Rows - 1) * 0.5f)) * Cell,
            0f);
    }

    public static VmfMap Build()
    {
        var map = new VmfMap();

        map.World.Add(new("skyname", "sky_day01_01"));

        // The one worldspawn key whose consequence is game logic, kept here as
        // a live canary for the C# read (Phase 12). Its observables are two
        // console lines, not a draw: the server logs "chapter title:
        // SS SANDBOX" at level init, and on first spawn the client's HudText
        // handler logs "no titles.txt entry named 'SS SANDBOX'" -- proof the
        // message crossed. Deliberately NOT a real titles.txt name: a title
        // that drew would sit on top of every future screenshot comparison.
        map.World.Add(new("chaptertitle", "SS SANDBOX"));
        map.World.Add(new("maxpropscreenwidth", "-1"));
        map.World.Add(new("detailvbsp", "detail.vbsp"));
        map.World.Add(new("detailmaterial", DetailSprites));

        Room(map);
        Lights(map);
        Spawns(map);

        for (int i = 0; i < Stations.Count; i++)
        {
            var station = Stations[i];
            var site = new Site(map, station.Slug, SiteOf(i));

            Furniture(map, site, station);
            station.Build(site);
        }

        return map;
    }

    //-------------------------------------------------------------------------
    // The hall.
    //
    // Six boxes, overlapping at the corners. Sealed is the only property that
    // matters: a leak makes vbsp skip visibility entirely and the map loads
    // fullbright, which reads as "the lighting is broken" rather than "there is
    // a hole in the wall".
    //-------------------------------------------------------------------------
    private static void Room(VmfMap map)
    {
        float outer = Half + WallThickness;

        map.WorldSolids.Add(VmfMap.Box(
            new(-outer, -outer, -WallThickness), new(outer, outer, 0f), Site.Floor));

        // The ceiling, in four bands around a hole, with sky in the hole.
        //
        // The hole is over the worldfx plot and it is the only reason four of
        // that station's entities do anything at all: light_environment lights
        // through a sky FACE and nothing else, skybox_swapper changes what a
        // sky face draws, shadow_control decides which way the sun casts, and a
        // 3D skybox is only rendered where one is visible. A sealed hall with
        // no sky face is a hall where all four of them are correct and silent,
        // which is exactly the shape of coverage this pass exists to avoid.
        //
        // Sky seals. A toolsskybox face stops the flood fill the same as any
        // other solid, so cutting the hole costs no leak.
        Point well = SiteOf(IndexOfStation("worldfx"));

        float wx0 = well.X - SkylightHalf, wx1 = well.X + SkylightHalf;
        float wy0 = well.Y - SkylightHalf, wy1 = well.Y + SkylightHalf;

        map.WorldSolids.Add(VmfMap.Box(
            new(-outer, -outer, Ceiling), new(outer, wy0, Ceiling + WallThickness), Site.Overhead));

        map.WorldSolids.Add(VmfMap.Box(
            new(-outer, wy1, Ceiling), new(outer, outer, Ceiling + WallThickness), Site.Overhead));

        map.WorldSolids.Add(VmfMap.Box(
            new(-outer, wy0, Ceiling), new(wx0, wy1, Ceiling + WallThickness), Site.Overhead));

        map.WorldSolids.Add(VmfMap.Box(
            new(wx1, wy0, Ceiling), new(outer, wy1, Ceiling + WallThickness), Site.Overhead));

        map.WorldSolids.Add(VmfMap.Box(
            new(wx0, wy0, Ceiling), new(wx1, wy1, Ceiling + WallThickness), Sky));

        map.WorldSolids.Add(VmfMap.Box(
            new(-outer, -outer, 0f), new(-Half, outer, Ceiling), Site.Hall));

        map.WorldSolids.Add(VmfMap.Box(
            new(Half, -outer, 0f), new(outer, outer, Ceiling), Site.Hall));

        map.WorldSolids.Add(VmfMap.Box(
            new(-Half, -outer, 0f), new(Half, -Half, Ceiling), Site.Hall));

        map.WorldSolids.Add(VmfMap.Box(
            new(-Half, Half, 0f), new(Half, outer, Ceiling), Site.Hall));
    }

    private static void Lights(VmfMap map)
    {
        // One per plot column and row, high up. Bright and plain: a test map
        // wants to be readable, not atmospheric.
        // Spread across whatever the grid turned out to be rather than at fixed
        // offsets: a light grid that does not follow the plot grid leaves the
        // outer stations in the dark as soon as a column is added.
        float lightX = (Columns - 1) * Cell * 0.5f;
        float lightY = (Rows - 1) * Cell * 0.5f;

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                map.Entities.Add(new VmfEntity { ClassName = "light" }
                    .Set("origin", new Point(x * lightX, y * lightY, Ceiling - 64f).ToString())
                    .Set("_light", CeilingLight)
                    .Set("_lightHDR", "-1 -1 -1 1")
                    .Set("_lightscaleHDR", "1")
                    .Set("_fifty_percent_distance", CeilingHalfDistance)
                    .Set("_zero_percent_distance", CeilingZeroDistance));
            }
        }
    }

    private static void Spawns(VmfMap map)
    {
        // The four plots the station list does not reach, at the back of the
        // hall, so a player arrives looking across the whole grid.
        for (int i = Stations.Count; i < Columns * Rows; i++)
        {
            var at = SiteOf(i);

            map.Entities.Add(new VmfEntity { ClassName = EntityNames.InfoPlayerDeathmatch }
                .Set("origin", new Point(at.X, at.Y, 16f).ToString())
                .Set("angles", "0 270 0"));
        }
    }

    //-------------------------------------------------------------------------
    // What every station gets, whether it asks or not: a floor pad, a sign that
    // reads itself out when you walk in, and a reset button.
    //-------------------------------------------------------------------------
    private static void Furniture(VmfMap map, Site site, Station station)
    {
        site.Solid(new(-256f, -256f, 0f), new(256f, 256f, 8f), Site.Pad);
        site.Solid(new(-28f, -300f, 8f), new(28f, -252f, 44f), Site.Wall);

        // The bright plate in front of the pedestal, and a light over it.
        //
        // Both exist because the first version was unfindable: the button was
        // grey on a grey plinth and the trigger that shows the description is
        // invisible by definition, so there was nothing to walk towards. The
        // plate is the loudest material in the map on purpose.
        site.Solid(new(-72f, -250f, 8f), new(72f, -190f, 11f), Site.Accent);

        // Three channels, three lines. A channel holds one message and a new one
        // replaces it, so walking into another station overwrites this text
        // rather than stacking on top of it.
        // Colours carry an alpha, and it is not optional: CGameText::KeyValue
        // reads "color" as FOUR integers, so a three-component value leaves
        // alpha at zero and the text draws perfectly, invisibly. That is
        // exactly what happened, and it looked like the message was never sent.
        site.Ent("game_text", "title", new(0f, -320f, 32f),
            "message", station.Title,
            "channel", "1", "x", "0.03", "y", "0.60",
            "color", "255 220 120 255", "holdtime", "10",
            "fadein", "0.1", "fadeout", "0.5", "effect", "0");

        site.Ent("game_text", "expect", new(0f, -320f, 48f),
            "message", station.Expect,
            "channel", "2", "x", "0.03", "y", "0.645",
            "color", "215 215 215 255", "holdtime", "10",
            "fadein", "0.1", "fadeout", "0.5", "effect", "0");

        site.Ent("game_text", "hint", new(0f, -320f, 64f),
            "message", "Press USE on the button to reset this station.",
            "channel", "3", "x", "0.03", "y", "0.69",
            "color", "140 190 255 255", "holdtime", "10",
            "fadein", "0.1", "fadeout", "0.5", "effect", "0");

        // Said only by the reset relay, and never by walking in.
        //
        // Without it there is no way to tell a working button from a broken
        // one: pressing USE re-showed the same three lines that appear when you
        // walk onto the plot, so a button that did nothing and a button that
        // worked looked identical. This line appears for one reason only.
        site.Ent("game_text", "done", new(0f, -320f, 80f),
            "message", "STATION RESET",
            "channel", "4", "x", "0.03", "y", "0.735",
            "color", "120 255 150 255", "holdtime", "3",
            "fadein", "0", "fadeout", "0.4", "effect", "0");

        var zone = site.Brush(EntityNames.TriggerMultiple, "zone",
            new(-288f, -288f, 8f), new(288f, 288f, 240f), Site.Trigger,
            "wait", "4", "spawnflags", "1");

        foreach (string line in new[] { "title", "expect", "hint" })
            zone.Wire("OnStartTouch", site.Name(line), "Display");

        // The relay, not the button, is what a station wires its reset to: a
        // station should not have to know how the reset was asked for.
        var reset = site.Ent(EntityNames.LogicRelay, "reset", new(0f, -340f, 16f));

        foreach (string line in new[] { "title", "expect", "hint", "done" })
            reset.Wire("OnTrigger", site.Name(line), "Display");

        site.Brush(EntityNames.FuncButton, "button",
            new(-16f, -292f, 44f), new(16f, -260f, 68f), Site.Switch,
            "wait", "1")
            .Wire("OnPressed", site.ResetName, "Trigger");

        site.Marker("beam", new(0f, -276f, 68f), "120 255 150");

        // NAMELESS, and that is the whole of what this line is about.
        //
        // vbsp treats a light with a targetname as a SWITCHED light: it gets a
        // lightstyle of its own out of a table of thirty-two, and the
        // thirty-third one in a map is a hard error -- "Too many switched
        // lights", and no BSP. This lamp is never switched by anything, so the
        // name bought nothing and cost a station: the map stopped compiling at
        // thirty-three plots, having compiled at thirty-two, with the failure
        // reported against whichever station happened to be last in the list.
        site.Ent("light", "", new(0f, -252f, 132f),
            "_light", ButtonLight,
            "_lightHDR", "-1 -1 -1 1", "_lightscaleHDR", "1",
            "_fifty_percent_distance", "110",
            "_zero_percent_distance", "300");
    }

    //=========================================================================
    // The stations.
    //=========================================================================

    private static void Markers(Site site)
    {
        site.Ent(EntityNames.InfoPlayerStart, "start", new(-160f, 0f, 16f), "angles", "0 90 0");
        site.Ent(EntityNames.InfoPlayerDeathmatch, "dm1", new(-96f, 0f, 16f), "angles", "0 90 0");
        site.Ent(EntityNames.InfoTarget, "target", new(-32f, 0f, 32f));
        site.Ent(EntityNames.InfoTeleportDestination, "dest", new(0f, 0f, 16f), "angles", "0 90 0");
        site.Ent(EntityNames.InfoLandmark, "landmark", new(32f, 0f, 16f));
        site.Ent(EntityNames.InfoMassCenter, "masscentre", new(64f, 0f, 16f));
        site.Ent(EntityNames.InfoNull, "null", new(96f, 0f, 16f));
        site.Ent(EntityNames.InfoConstraintAnchor, "anchor", new(128f, 0f, 48f));

        // The one entity in the mod with no keys, no inputs and no outputs.
        //
        // Stock never gave logic_proximity any -- it is a name bound to a bare
        // point entity and whatever it was meant to become was never written --
        // so a worked example of it IS a point in space that does nothing, and
        // this is the station where that is the promise rather than a failure.
        // The mod's entity that measures distance to a point is
        // point_proximity_sensor, at the SENSORS station.
        site.Ent(EntityNames.LogicProximity, "proximity", new(160f, 0f, 16f));

        // Where the camera stands when the round is over, and the only marker
        // here that AIMS itself: it is placed for the view, told the name of the
        // thing worth looking at, and works the angles out two seconds after the
        // map loads. That is the whole of what makes it more than the info_target
        // it is pointed at, four along this row.
        //
        // No angles of its own on purpose. Stock zeroes them in Spawn before it
        // has aimed, so an info_intermission has never been able to be aimed by
        // hand, and the managed one keeps that rather than improving on it —
        // every map that has one was placed against the version that discards
        // them.
        site.Ent(EntityNames.InfoIntermission, "camera", new(192f, 0f, 96f),
            "target", site.Name("target"));

        // The one marker here that describes a TRANSFORM rather than a place.
        //
        // info_lighting_relative is a pair of places and the difference between
        // them: a prop aimed at it by name is lit as though it stood at the
        // LANDMARK instead of where it really is. Stock's client half spells
        // what that means exactly --
        //
        //     offset = EntityToWorld(this) * inverse(EntityToWorld(landmark))
        //
        // -- so the thing being demonstrated is a matrix, and both the
        // translation and the rotation are part of it.
        //
        // NON-IDENTITY DELIBERATELY, which is the whole reason this instance
        // earns its place rather than copying what a shipping map does.
        // dm_lockdown holds the only two info_lighting_relative in any map here
        // and BOTH describe the identity matrix: wall2_lighting_rel names no
        // landmark at all, and sink_lighting's LightingLandmark is its own
        // targetname -- a transform from a place to itself. So between them the
        // live maps exercise the name resolve and never once the offset. This
        // one stands 256 units along the row from the info_target it names, 16
        // below it and turned 45 degrees away, so the matrix it describes
        // differs from the identity in translation AND in rotation. Copying the
        // map here would have demonstrated the identity a third time.
        //
        // The landmark is markers_target, four lines up: an ordinary entity
        // found by name, which is the branch InfoLightingRelative.OnThink takes
        // when a landmark is named and resolves. The other two branches are not
        // placed -- an unnamed landmark demonstrates nothing a bare info_target
        // does not, and one that resolves to nothing is a DevWarning on every
        // load of this map, which is a defect and not a demonstration.
        //
        // Built through site.Name rather than spelled out, because a landmark
        // is a KEYVALUE and not a connection: SandboxTests.NamesAndWires checks
        // that every wire lands somewhere and would never see a typo here.
        site.Ent(EntityNames.InfoLightingRelative, "lightrel", new(224f, 0f, 16f),
            "angles", "0 45 0",
            "LightingLandmark", site.Name("target"));
    }

    private static void Health(Site site)
    {
        site.Ent(EntityNames.ItemHealthcharger, "charger", new(-96f, 96f, 16f), "angles", "0 270 0");
        site.Ent(EntityNames.ItemSuitcharger, "suit", new(96f, 96f, 16f), "angles", "0 270 0");
        site.Ent(EntityNames.ItemHealthkit, "kit", new(-64f, -32f, 16f));
        site.Ent(EntityNames.ItemHealthvial, "vial", new(0f, -32f, 16f));
        site.Ent(EntityNames.ItemBattery, "battery", new(64f, -32f, 16f));
    }

    private static void Ammo(Site site)
    {
        string[] kinds =
        [
            EntityNames.ItemAmmoPistol, EntityNames.ItemAmmoPistolLarge,
            EntityNames.ItemAmmoSmg1, EntityNames.ItemAmmoSmg1Large, EntityNames.ItemAmmoSmg1Grenade,
            EntityNames.ItemAmmoAr2, EntityNames.ItemAmmoAr2Large, EntityNames.ItemAmmoAr2Altfire,
            EntityNames.ItemAmmo357, EntityNames.ItemAmmo357Large,
            EntityNames.ItemBoxBuckshot, EntityNames.ItemRpgRound,
        ];

        for (int i = 0; i < kinds.Length; i++)
        {
            // Two rows of six, so nothing overlaps and every model is reachable.
            float x = -160f + ((i % 6) * 64f);
            float y = (i / 6) == 0 ? 48f : -48f;

            site.Ent(kinds[i], $"a{i}", new(x, y, 16f));
        }
    }

    private static void Weapons(Site site)
    {
        // The names WeaponPickup claims (Phase 10) — a row is a weapon,
        // exactly as the table says, and WeaponTests pins these to the table.
        string[] kinds =
        [
            EntityNames.WeaponCrowbar, EntityNames.WeaponPistol, EntityNames.Weapon357,
            EntityNames.WeaponSmg1, EntityNames.WeaponAr2, EntityNames.WeaponShotgun,
            EntityNames.WeaponCrossbow, EntityNames.WeaponRpg, EntityNames.WeaponFrag,
            EntityNames.WeaponStunstick, EntityNames.WeaponSlam,
        ];

        for (int i = 0; i < kinds.Length; i++)
        {
            // Two rows of six, as the ammo station lays its twelve: a third row
            // of five would land on the second.
            float x = -160f + ((i % 6) * 64f);
            float y = (i / 6) == 0 ? 48f : -48f;

            site.Ent(kinds[i], $"w{i}", new(x, y, 16f));
        }
    }

    private static void DynamicProps(Site site)
    {
        // A prop_dynamic must be on a model with no prop data: stock's
        // CBaseProp::Spawn deletes one on a physics prop's model, which is what
        // prop_dynamic_override is for -- dyn3 shows that on the chair.
        site.Ent(EntityNames.PropDynamic, "dyn", new(-128f, 64f, 8f), "model", Sink, "angles", "0 90 0");
        site.Ent(EntityNames.DynamicProp, "dyn2", new(-32f, 64f, 8f), "model", Sink, "angles", "0 90 0");
        site.Ent(EntityNames.PropDynamicOverride, "dyn3", new(64f, 64f, 8f), "model", Chair,
            "angles", "0 90 0", "health", "40");

        site.Ent(EntityNames.PropDoorRotating, "door", new(96f, -96f, 8f),
            "model", Door, "angles", "0 0 0",
            "spawnpos", "0", "distance", "90", "speed", "100", "returndelay", "-1");

        // THE ORNAMENT, riding the door — which is the whole of what this entity
        // is for and the reason it could not be ported until now. It is placed on
        // the door rather than on one of the chairs above deliberately: an
        // ornament on something that never moves is indistinguishable from a
        // prop_dynamic, and proves nothing. This one visibly swings with the
        // door, or visibly does not.
        //
        // No "angles": an ornament's facing comes from its parent, and giving it
        // one here would hide a composition that ignored rotation.
        site.Ent(EntityNames.PropDynamicOrnament, "ornament", new(96f, -96f, 56f),
            "model", Fixture, "InitialOwner", site.Name("door"));

        site.OnReset("door", "Close");
        site.OnReset("dyn", "Enable");
        site.OnReset("dyn2", "Enable");
        site.OnReset("dyn3", "Enable");
    }

    private static void PhysicsProps(Site site)
    {
        site.Ent(EntityNames.PropPhysics, "crate", new(-128f, 64f, 24f), "model", Crate);
        site.Ent(EntityNames.PhysicsProp, "crate2", new(-32f, 64f, 24f), "model", Crate);
        site.Ent(EntityNames.PropPhysicsOverride, "drum", new(64f, 64f, 24f), "model", Drum, "health", "30");

        site.Ent(EntityNames.PropPhysicsRespawnable, "barrel", new(96f, -64f, 24f),
            "model", Boom, "RespawnTime", "10");

        // The entity the parallel hierarchy was proved with. It is an ordinary
        // C#-backed prop now rather than a class of its own, so it belongs in
        // here with the rest — and it is still the one entity whose CLIENT half
        // is written, which is what makes it worth having somewhere reachable.
        site.Ent(EntityNames.SsBarrel, "ss_barrel_test", new(160f, 64f, 24f), "model", Drum);

        // Stock's own debug sphere, and in this build a prop with the model's
        // hull. It belongs among these rather than in a station of its own for a
        // reason that is also the divergence: OverridePropdata stops the model's
        // prop data being read, so every part of prop_physics that decides
        // anything — health, damage modifiers, the explosion, the gibs — has
        // nothing left to decide from, and what remains is a model the solver
        // moves. The twelve-unit sphere object stock builds instead of the
        // compiled hull is the part that could not come over, so this rolls
        // exactly as far as the can it is made of. See docs/divergences.md.
        site.Ent(EntityNames.PropSphere, "sphere", new(160f, -64f, 24f), "model", Can);

        foreach (string name in new[] { "crate", "crate2", "drum", "sphere" })
            site.OnReset(name, "Wake");
    }

    private static void Doors(Site site)
    {
        site.Brush(EntityNames.FuncDoor, "slider",
            new(-192f, -32f, 8f), new(-64f, 32f, 136f), Site.Wall,
            "movedir", "-90 0 0", "speed", "80", "wait", "4", "lip", "8");

        // The hinge, written out because a door is the one rotating brush whose
        // axis is NOT its middle. Site.Brush defaults a rotator's origin to the
        // centre of its own box, which is right for a spinner and would make
        // this a revolving door; a door swings about an edge, so this one says
        // where. The +X edge rather than the -X one because the sweep on that
        // side is empty floor, and on this side it would pass through the water
        // slab behind it.
        site.Brush(EntityNames.FuncDoorRotating, "swinger",
            new(64f, -32f, 8f), new(192f, 32f, 136f), Site.Wall,
            "origin", (site.At + new Point(192f, 0f, 72f)).ToString(),
            "speed", "90", "wait", "4", "distance", "90");

        //---------------------------------------------------------------------
        // Water, in name only, and it earns a slab beside the two doors rather
        // than a footnote because a door is genuinely all it is here.
        //
        // Stock registers func_water onto CBaseDoor and then treats the
        // classname as a switch inside it: the volume gets
        // FSOLID_VOLUME_CONTENTS and a vphysics fluid controller, and those two
        // are the whole of what makes a body of water something you swim in and
        // things float on. Managed brushes reach solidity through three named
        // values and none of them is "the contents are water", so what is left
        // over is a door — the travel, the speed, the wait, USE to open. The
        // other water name, func_water_analog, is at the MOVERS station and
        // loses exactly the same thing.
        //
        // Textured like its neighbours for that reason. A blue lid would promise
        // swimming, and this cannot deliver it.
        //---------------------------------------------------------------------
        site.Brush(EntityNames.FuncWater, "water",
            new(-64f, 96f, 8f), new(64f, 160f, 72f), Site.Wall,
            "movedir", "-90 0 0", "speed", "40", "wait", "4", "lip", "0");

        site.OnReset("slider", "Close");
        site.OnReset("swinger", "Close");
        site.OnReset("water", "Close");
    }

    private static void Buttons(Site site)
    {
        // Each button drives an indicator so a press is visible. func_brush is
        // demonstrated properly at the walls station; here it is the readout.
        site.Brush(EntityNames.FuncBrush, "lamp1",
            new(-192f, 64f, 8f), new(-128f, 128f, 72f), Site.Pad, "Solidity", "0");

        site.Brush(EntityNames.FuncBrush, "lamp2",
            new(128f, 64f, 8f), new(192f, 128f, 72f), Site.Pad, "Solidity", "0");

        site.Brush(EntityNames.FuncButton, "push",
            new(-176f, -32f, 8f), new(-144f, 0f, 64f), Site.Wall, "wait", "2")
            .Wire("OnPressed", site.Name("lamp1"), "Toggle");

        site.Brush(EntityNames.FuncRotButton, "twist",
            new(144f, -32f, 8f), new(176f, 0f, 64f), Site.Wall,
            "wait", "2", "speed", "90", "distance", "90")
            .Wire("OnPressed", site.Name("lamp2"), "Toggle");

        //---------------------------------------------------------------------
        // A wheel, whatever the classname says, and the slab behind it is the
        // only way to show the difference. A button reports that it was pressed;
        // this reports WHERE IT IS, continuously, as a fraction between nought
        // and one — so its Position output drives a func_movelinear's own
        // SetPosition and the slab follows the turn all the way through rather
        // than jumping at the ends of it. That is how the SDK builds a valve
        // that opens a door by however far the player has wound it.
        //
        // func_movelinear is demonstrated properly at the MOVERS station; here
        // it is the gauge, the same way func_brush is the two lamps.
        //
        // returnspeed 0, so it stays where it is put. A wheel that wound itself
        // back would move the slab on its own and the readout would be the
        // spring rather than the player.
        //---------------------------------------------------------------------
        site.Brush(EntityNames.MomentaryRotButton, "wheel",
            new(-16f, -32f, 8f), new(16f, 0f, 64f), Site.Switch,
            "speed", "60", "distance", "90", "returnspeed", "0",
            "StartPosition", "0", "spawnflags", "0");

        site.Brush(EntityNames.FuncMovelinear, "gauge",
            new(-32f, 64f, 8f), new(32f, 128f, 40f), Site.Accent,
            "movedir", "-90 0 0", "MoveDistance", "96", "speed", "200",
            "StartPosition", "0");

        site.Wire("wheel", "Position", site.Name("gauge"), "SetPosition");

        site.Marker("m1", new(-160f, -16f, 64f), "255 220 120");
        site.Marker("m2", new(160f, -16f, 64f), "255 220 120");
        site.Marker("m3", new(0f, -16f, 64f), "255 220 120");

        site.OnReset("lamp1", "Enable");
        site.OnReset("lamp2", "Enable");

        // Only the wheel is put back, because putting the wheel back is what
        // puts the gauge back: wound to nought it reports nought, and the slab
        // follows the report rather than the button.
        site.OnReset("wheel", "SetPositionImmediately", "0");
    }

    private static void Movers(Site site)
    {
        site.Brush(EntityNames.FuncMovelinear, "slide",
            new(-192f, 32f, 8f), new(-96f, 128f, 40f), Site.Wall,
            "movedir", "-90 0 0", "MoveDistance", "128", "speed", "60", "StartPosition", "0");

        site.Brush(EntityNames.MomentaryDoor, "momentary",
            new(-64f, 32f, 8f), new(32f, 128f, 40f), Site.Wall,
            "movedir", "-90 0 0", "MoveDistance", "96", "speed", "40", "StartPosition", "0");

        site.Brush(EntityNames.FuncPlat, "lift",
            new(64f, 32f, 8f), new(176f, 128f, 24f), Site.Pad,
            "height", "128", "speed", "50");

        site.Brush(EntityNames.FuncRotating, "spinner",
            new(-48f, -128f, 40f), new(48f, -32f, 72f), Site.Wall,
            "maxspeed", "90", "spawnflags", "0");

        // A tank, because water that rises has to rise INTO something.
        //
        // func_water_analog is the same class as the slider at the other end of
        // this plot -- stock registers both names to CFuncMoveLinear -- so the
        // only thing worth showing is what the classname changes, and that is
        // the one thing a slab of moving wall cannot show: you can get into it.
        //
        // The four walls are 48 tall so a player can jump the rim, and the pool
        // is a brush that TRANSLATES rather than a level that fills. That is
        // what the entity does, and it dictates the geometry: the pool spans
        // from the underside of the hall floor to just below the tank floor, so
        // that when it is up its bottom is still buried and the only edge on
        // show is the surface. Drawn a unit low as well, because a face exactly
        // coplanar with the floor z-fights with it.
        site.Solid(new(88f, -168f, 8f), new(216f, -160f, 56f), Site.Wall);
        site.Solid(new(88f, -48f, 8f), new(216f, -40f, 56f), Site.Wall);
        site.Solid(new(88f, -160f, 8f), new(96f, -48f, 56f), Site.Wall);
        site.Solid(new(208f, -160f, 8f), new(216f, -48f, 56f), Site.Wall);

        var pool = site.Brush(EntityNames.FuncWaterAnalog, "pool",
            new(96f, -160f, -32f), new(208f, -48f, 7f), Site.NoDraw,
            "movedir", "-90 0 0", "MoveDistance", "38", "speed", "12", "StartPosition", "0");

        // The lid alone carries the water material, and the sides are nodraw.
        // vbsp draws a water surface wherever it finds one, so a brush textured
        // all over is a glowing box rather than a pool; side 0 is the +Z face,
        // which is the order VmfMap.Box writes them in.
        pool.Solids[0].Sides[0].Material = Water;

        var start = site.Ent(EntityNames.LogicRelay, "start", new(0f, -180f, 16f));
        start.Wire("OnTrigger", site.Name("slide"), "Open");
        start.Wire("OnTrigger", site.Name("momentary"), "Open");
        start.Wire("OnTrigger", site.Name("lift"), "GoUp");
        start.Wire("OnTrigger", site.Name("spinner"), "Start");
        start.Wire("OnTrigger", site.Name("pool"), "Open");

        site.OnReset("slide", "Close");
        site.OnReset("momentary", "Close");
        site.OnReset("lift", "GoDown");
        site.OnReset("spinner", "Stop");
        site.OnReset("pool", "Close");
        site.OnReset("start", "Trigger", delay: 1.0f);
    }

    private static void Walls(Site site)
    {
        site.Brush(EntityNames.FuncWall, "wall",
            new(-208f, 64f, 8f), new(-144f, 128f, 104f), Site.Wall);

        site.Brush(EntityNames.FuncIllusionary, "ghost",
            new(-112f, 64f, 8f), new(-48f, 128f, 104f), Site.Pad);

        site.Brush(EntityNames.FuncWallToggle, "toggle",
            new(-16f, 64f, 8f), new(48f, 128f, 104f), Site.Wall);

        site.Brush(EntityNames.FuncBrush, "brush",
            new(80f, 64f, 8f), new(144f, 128f, 104f), Site.Wall, "Solidity", "1");

        // Stock's key (CFrictionModifier). It grips everything but a player:
        // a player walks across it unchanged, in stock and here.
        site.Brush(EntityNames.FuncFriction, "ice",
            new(-128f, -160f, 8f), new(128f, -32f, 24f), Site.Trigger,
            "modifier", "15");

        site.OnReset("toggle", "TurnOn");
        site.OnReset("brush", "Enable");
    }

    private static void Breakables(Site site)
    {
        site.Brush(EntityNames.FuncBreakable, "panel",
            new(-96f, 0f, 8f), new(96f, 32f, 128f), Site.Wall,
            "health", "50", "minhealthdmg", "0");

        site.OnReset("panel", "SetHealth", "50");

        // Glass, which breaks differently: shooting it takes out the pane that
        // was hit rather than damaging the whole sheet. The material has to be a
        // real glass one -- func_breakable_surf looks up a cracked variant of
        // whatever it is textured with, and a dev texture has none.
        //
        // Deliberately thin and drawn on ONE face: vbsp rejects a surface whose
        // drawn face is not a single quad, and the entity says so and removes
        // itself rather than misbehaving.
        // Drawn on ONE face, the rest nodraw, and with no "error" key of its own.
        //
        // Both matter. vbsp derives the pane grid from a single drawn quad and
        // rejects a brush textured all over as "multiple faces that aren't
        // NODRAW" — and it reports that by WRITING the error key, so a map that
        // sets error itself overrides the verdict. A surface that gets past the
        // check without a grid networks pane dimensions that were never
        // computed, and a NaN in a networked float asserts in dt_send.
        // "+y" is the box side at MAX y, and it is the one visible from the side
        // the station is approached from — its caption and accent strip are at
        // local y -190 to -320, so a player reads the sign and then looks at
        // this. That is measured, not derived: the pane was textured one face at
        // a time and photographed from both sides, because the winding does not
        // read the way the name does.
        //
        // A breakable surface has exactly ONE drawn face by design. Pane used to
        // texture both, vbsp rejected the brush for having two, and the sandbox
        // suppressed the complaint by writing error 0 itself — so the window
        // compiled broken, said nothing, and appeared to be on both sides, which
        // is why nobody could say which side it was on.
        //
        // No "error" key of its own either. vbsp writes that one to report a
        // brush it could not divide, and a map that sets it is overriding the
        // verdict rather than passing it on — which is how a surface with no
        // grid gets past the check and networks dimensions nothing computed.
        site.Pane(EntityNames.FuncBreakableSurf, "glass",
            new(-96f, -64f, 8f), new(96f, -60f, 128f), Site.Glass, "+y",
            "surfacetype", "0", "fragility", "100", "health", "0",
            "spawnflags", "1");
    }

    private static void Forces(Site site)
    {
        site.Brush(EntityNames.TriggerHurt, "hurt",
            new(-208f, 32f, 8f), new(-112f, 128f, 96f), Site.Trigger,
            "damage", "5", "wait", "1");

        site.Brush(EntityNames.TriggerGravity, "lowgrav",
            new(-80f, 32f, 8f), new(16f, 128f, 96f), Site.Trigger,
            "gravity", "0.25");

        site.Brush(EntityNames.TriggerPush, "push",
            new(48f, 32f, 8f), new(144f, 128f, 96f), Site.Trigger,
            "pushdir", "-90 0 0", "speed", "200");

        site.Brush(EntityNames.TriggerApplyImpulse, "impulse",
            new(-48f, -160f, 8f), new(48f, -64f, 96f), Site.Trigger,
            "impulse_dir", "-90 0 0", "force", "400");

        site.Marker("m1", new(-160f, 80f, 96f), "255 140 140");
        site.Marker("m2", new(-32f, 80f, 96f), "140 255 255");
        site.Marker("m3", new(96f, 80f, 96f), "255 220 140");
        site.Marker("m4", new(0f, -112f, 96f), "200 160 255");

        // The POINT versions of the three volumes above, and the difference is
        // the whole reason they are side by side: a trigger acts on whoever is
        // inside it, a point entity acts when it is FIRED — on whoever is near
        // it, or on an entity it was told the name of, or on whoever set it off.
        site.Ent(EntityNames.PointHurt, "pointhurt", new(-160f, -64f, 32f),
            "DamageRadius", "96", "Damage", "5", "DamageDelay", "0.5",
            "DamageType", "0");

        // Something to put back, and a teleport set to put it back where the map
        // DREW it rather than to bring it here — spawnflag 1, which is the
        // difference between a destination and a reset button. Shove the crate
        // anywhere in the plot and the station's own reset returns it.
        //
        // It targeted "landing" before, which belongs to the TELEPORTS station:
        // Site.Name prefixes with the plot slug, so this asked for
        // forces_landing and no such entity exists. The station demonstrated
        // nothing and said nothing, which is exactly what this map is for
        // catching -- it was found by firing the input at a running game.
        site.Ent(EntityNames.PropPhysics, "cargo", new(-96f, -64f, 40f), "model", Crate);

        site.Ent(EntityNames.PointTeleport, "pointport", new(-96f, -64f, 32f),
            "target", site.Name("cargo"), "spawnflags", "1");

        site.OnReset("pointport", "Teleport");

        // The third of them, and the one whose target is neither a radius nor a
        // name: target_changegravity puts the ACTIVATOR on a gravity scale, so
        // the pad has to be what fires it. Stepping off puts it back, and the
        // reset button does too — pressing it makes the player the activator a
        // second time, which is the only way a station can undo something that
        // was done to one person rather than to the room.
        //
        // Next to trigger_gravity on purpose. That one is a volume and holds you
        // light only while you stand in it; this one hands out a scale that
        // follows you out of the pad and across the whole hall.
        site.Ent(EntityNames.TargetChangegravity, "floaty", new(160f, -64f, 32f),
            "gravity", "0.2");

        var floatPad = site.Brush(EntityNames.TriggerMultiple, "floatpad",
            new(112f, -160f, 8f), new(208f, -96f, 96f), Site.Trigger,
            "wait", "1", "spawnflags", "1");

        floatPad.Wire("OnStartTouch", site.Name("floaty"), "ChangeGrav");
        floatPad.Wire("OnEndTouch", site.Name("floaty"), "ResetGrav");

        site.Marker("m5", new(160f, -128f, 96f), "200 160 255");

        site.OnReset("floaty", "ResetGrav");

        //---------------------------------------------------------------------
        // The second thing here handed to one player rather than to the room, and
        // it belongs beside the gravity one for the same reason that one belongs
        // beside trigger_gravity: what it hands out follows the player out of the
        // pad, so the pad has to be what fires it and stepping off has to be what
        // lifts it.
        //
        // The speed is only the name. What a map actually buys here is the SET —
        // one number and the suppressions that go with it — because nobody slows
        // a player to a third for the sake of the walking speed: they are wading,
        // or being dragged, and a player who can still sprint and jump out of it
        // was never in that state. So the flags are 37: weapons holstered, no
        // jump, no sprint. The other five bits are the same table and would read
        // as the same thing said louder.
        //
        // The reset button lifts it too, and here that is a safety rather than a
        // nicety: a player who left the pad by dying never sent OnEndTouch, and
        // the state this entity hands out is one they cannot take off themselves.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.PlayerSpeedmod, "slow", new(-208f, -64f, 32f),
            "spawnflags", "37");

        var slowPad = site.Brush(EntityNames.TriggerMultiple, "slowpad",
            new(-248f, -176f, 8f), new(-152f, -80f, 96f), Site.Trigger,
            "wait", "1", "spawnflags", "1");

        slowPad.Wire("OnStartTouch", site.Name("slow"), "ModifySpeed", "0.3");
        slowPad.Wire("OnEndTouch", site.Name("slow"), "ModifySpeed", "1");

        site.Marker("m7", new(-200f, -128f, 96f), "255 220 140");

        site.OnReset("slow", "ModifySpeed", "1");

        //---------------------------------------------------------------------
        // The impulse pad's twin, asked for rather than walked into, and the two
        // of them together are the point. trigger_apply_impulse SETS a velocity
        // on whatever enters it, so a can and a crate leave at the same speed;
        // this applies a FORCE when the map asks for one, so the crate barely
        // shifts and the can goes over the wall — and it scatters the direction,
        // so a pile does not leave in formation.
        //
        // Its whole input is an input: nothing touches it into life, so the
        // station's own button is the only thing that can ask. A can and a crate
        // rather than two of either, because a force divided by a mass is the
        // difference being demonstrated and one prop cannot show a difference.
        // A player standing in it gets the viewkick, which is the half of the
        // entity a prop cannot show at all.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.PropPhysics, "light", new(184f, 72f, 24f), "model", Can);
        site.Ent(EntityNames.PropPhysics, "heavy", new(216f, 72f, 40f), "model", Crate);

        // No spawnflags, which is what the impulse pad beside it does too: the
        // kind flags on a volume are one gate rather than a per-toucher test, so
        // the honest way to say "everything in here" is to say nothing.
        site.Brush(EntityNames.TriggerImpact, "shove",
            new(152f, 24f, 8f), new(248f, 120f, 104f), Site.Trigger,
            "angles", "-30 180 0", "Magnitude", "900", "Noise", "0.3",
            "Viewkick", "0.3");

        site.Marker("m6", new(200f, 72f, 104f), "255 200 140");

        //---------------------------------------------------------------------
        // Weather, in a pen, and the pen is not decoration.
        //
        // A wind is the one force here that does not stop: what blows in is given
        // a motion controller and keeps it until it leaves, the lift is always
        // upwards, and the gust wanders forty degrees either side of the yaw. So
        // cans blown across an open pad would drift out of the plot and end up
        // furniture in somebody else's station. The four walls are the volume's
        // own edges and are 56 tall — enough that the lift cannot carry a can
        // over the rim, low enough to look down into.
        //
        // Spawnflag 8, physics objects only, and that is not a nicety: a player
        // in a gale is moved by the movement code rather than by the solver, so a
        // force on their shadow object does nothing whatsoever. The entity throws
        // players out for that reason and the flag says so out loud.
        //---------------------------------------------------------------------
        site.Solid(new(-120f, 160f, 8f), new(120f, 168f, 64f), Site.Wall);
        site.Solid(new(-120f, 240f, 8f), new(120f, 248f, 64f), Site.Wall);
        site.Solid(new(-120f, 168f, 8f), new(-112f, 240f, 64f), Site.Wall);
        site.Solid(new(112f, 168f, 8f), new(120f, 240f, 64f), Site.Wall);

        site.Brush(EntityNames.TriggerWind, "wind",
            new(-112f, 168f, 8f), new(112f, 240f, 120f), Site.Trigger,
            "spawnflags", "8", "Speed", "150", "SpeedNoise", "60",
            "DirectionNoise", "40", "HoldTime", "1", "HoldNoise", "0.5");

        // Cans rather than crates, because the force is a force: a drum weighs
        // eighty-odd kilos and a hundred and fifty units of wind is not felt by
        // it at all, which reads as the entity being broken rather than as the
        // drum being heavy.
        for (int i = 0; i < 3; i++)
        {
            site.Ent(EntityNames.PropPhysics, $"blown{i}",
                new(-80f + (i * 40f), 204f, 24f), "model", Can);
        }

        // Woken before it is shoved, and the wind's three woken at all, because
        // a physics object asleep on the floor of a trigger has stopped
        // reporting that it is inside one — so a wind can lose what it caught to
        // a lull, and an impact can be asked to throw something the volume has
        // forgotten about.
        foreach (string name in new[] { "light", "heavy", "blown0", "blown1", "blown2" })
            site.OnReset(name, "Wake");

        site.OnReset("shove", "Impact", delay: 0.2f);

        foreach (string name in new[] { "hurt", "lowgrav", "push", "impulse", "floatpad", "slowpad" })
            site.OnReset(name, "Enable");
    }

    private static void Teleports(Site site)
    {
        // Clear of both volumes. A destination inside the trigger that sent you
        // there is a loop the player cannot walk out of.
        site.Ent(EntityNames.InfoTarget, "landing", new(176f, -112f, 16f));

        site.Brush(EntityNames.TriggerTeleport, "port",
            new(-208f, 32f, 8f), new(-112f, 128f, 96f), Site.Trigger,
            "target", site.Name("landing"));

        // Sideways, not up.
        //
        // This used to lift the player 96 units, and the volume is 88 tall: you
        // arrived just above it, fell straight back in, and were lifted again.
        // A vertical offset inside a room is always that trap, because gravity
        // returns you to exactly where you were standing.
        site.Brush(EntityNames.TriggerTeleportRelative, "shift",
            new(-48f, 32f, 8f), new(48f, 128f, 96f), Site.Trigger,
            "teleportoffset", "0 -224 0");

        site.Marker("m1", new(-160f, 80f, 96f), "140 190 255");
        site.Marker("m2", new(0f, 80f, 96f), "140 190 255");
        site.Marker("m3", new(176f, -112f, 32f), "120 255 150", 90f);

        site.OnReset("port", "Enable");
        site.OnReset("shift", "Enable");
    }

    private static void Detection(Site site)
    {
        site.Ent(EntityNames.InfoTarget, "watched", new(0f, 160f, 32f));

        site.Brush(EntityNames.TriggerMultiple, "many",
            new(-208f, 0f, 8f), new(-128f, 96f, 96f), Site.Trigger, "wait", "2");

        site.Brush(EntityNames.TriggerOnce, "once",
            new(-96f, 0f, 8f), new(-16f, 96f, 96f), Site.Trigger);

        site.Brush(EntityNames.TriggerLook, "look",
            new(16f, 0f, 8f), new(96f, 96f, 96f), Site.Trigger,
            "target", site.Name("watched"), "LookTime", "1", "FieldOfView", "0.9");

        site.Brush(EntityNames.TriggerProximity, "near",
            new(128f, 0f, 8f), new(208f, 96f, 96f), Site.Trigger,
            "measuretarget", site.Name("watched"), "radius", "128");

        // Removes physics props that fall in, not players -- the pad sits under
        // a shelf rather than on the walkway.
        site.Brush(EntityNames.TriggerRemove, "bin",
            new(-48f, -160f, 8f), new(48f, -80f, 48f), Site.Trigger);

        site.Marker("m1", new(-168f, 48f, 96f), "140 190 255");
        site.Marker("m2", new(-56f, 48f, 96f), "140 190 255");
        site.Marker("m3", new(56f, 48f, 96f), "140 190 255");
        site.Marker("m4", new(168f, 48f, 96f), "140 190 255");

        //---------------------------------------------------------------------
        // The volume that predates the four pads behind it, kept working for the
        // maps that still contain one. Two things date it: it reports being touched
        // and being USED as separate events, and its filtering is a bitfield of
        // what to IGNORE rather than the name of a filter entity. That is what a
        // trigger looked like before filter_activator_name existed, and C++ marks
        // it obsolete, so nothing new will ever be one.
        //
        // The light is the touch half. The counter is the use half, and the use
        // half needs the button: a managed trigger volume is not solid, so there
        // is nothing for a player's +use to trace against, and the input arrives
        // as an I/O connection instead — which is the other way stock's Use is
        // reached. The counter is cleared first and the volume used half a second
        // later, so one press always reads one.
        //---------------------------------------------------------------------
        var old = site.Brush(EntityNames.TriggerBrush, "oldpad",
            new(-208f, -160f, 8f), new(-128f, -80f, 96f), Site.Trigger,
            "wait", "1", "InputFilter", "0", "DontMessageParent", "0");

        old.Wire("OnStartTouch", site.Name("oldlight"), "LightOn");
        old.Wire("OnEndTouchAll", site.Name("oldlight"), "LightOff");
        old.Wire("OnUse", site.Name("uses"), "Add", "1");

        site.Marker("oldlight", new(-168f, -120f, 8f), "255 220 120").Set("spawnflags", "2");

        site.Ent(EntityNames.MathCounter, "uses", new(-168f, -48f, 16f),
            "StartValue", "0", "Min", "0", "Max", "999");

        //---------------------------------------------------------------------
        // The base class of every volume in this station, with a map name of its
        // own and nothing else, drawn at the size of the four pads it is the base
        // of and incapable of noticing anybody.
        //
        // CBaseTrigger::Spawn never calls InitTrigger, which is what gives a
        // volume its brush model, its trigger flag and its invisibility — so a
        // placed one has no shape and is never touched, and none of the six
        // outputs its datadesc declares can ever fire. Hammer does not offer it
        // and nothing in the SDK creates one by name; it is the base of the
        // family, exposed by accident.
        //
        // The managed class is exactly as inert, and deliberately: the brush
        // shell's default is a solid, visible wall, so without it a map author
        // asking for "trigger" would find their doorway bricked up. Nothing is
        // wired to it and nothing resets it, because there is no output it could
        // fire and no input it could be sent. This station promises pads that
        // notice you, and this is the one that cannot.
        //---------------------------------------------------------------------
        site.Brush(EntityNames.Trigger, "base",
            new(128f, -160f, 8f), new(208f, -80f, 96f), Site.Trigger);

        site.OnReset("uses", "SetValue", "0");
        site.OnReset("oldpad", "Use", delay: 0.5f);

        foreach (string name in new[] { "many", "once", "look", "near", "bin", "oldpad" })
            site.OnReset(name, "Enable");
    }

    private static void Filters(Site site)
    {
        site.Ent(EntityNames.FilterActivatorName, "byname", new(-160f, 128f, 16f),
            "filtername", "ss_nobody");

        site.Ent(EntityNames.FilterActivatorClass, "byclass", new(-96f, 128f, 16f),
            "filterclass", "player");

        site.Ent(EntityNames.FilterActivatorTeam, "byteam", new(-32f, 128f, 16f),
            "filterteam", "2");

        site.Ent(EntityNames.FilterDamageType, "bydamage", new(32f, 128f, 16f),
            "damagetype", "64");

        site.Ent(EntityNames.FilterMulti, "multi", new(96f, 128f, 16f),
            "FilterType", "0",
            "Filter01", site.Name("byclass"),
            "Filter02", site.Name("byteam"));

        // Passes anybody, which is the whole of what filter_base asks. Stock
        // registers the base class itself under this name, so it is a real
        // entity and not an abstract one leaking into the map list.
        site.Ent(EntityNames.FilterBase, "anyone", new(160f, 128f, 16f));

        site.Ent(EntityNames.FilterActivatorMassGreater, "bymass", new(224f, 128f, 16f),
            "filtermass", "20");

        // The pad only fires for a player, which is what the filter says.
        site.Brush(EntityNames.TriggerMultiple, "filtered",
            new(-96f, -96f, 8f), new(96f, 32f, 96f), Site.Trigger,
            "wait", "2", "filtername", site.Name("byclass"))
            .Wire("OnStartTouch", site.Name("expect"), "Display");

        //---------------------------------------------------------------------
        // Asking a filter directly, which is the half of a filter that the pad
        // above cannot show.
        //
        // TestActivator hands the filter whoever fired it and gets OnPass or
        // OnFail back, so a map can BRANCH on a question rather than only gate a
        // trigger with it. Both lights are wired the same way to the same
        // button, and both are driven either way -- an unlit one has been told
        // "no" rather than told nothing, which is the difference between a
        // readout and an absence.
        //---------------------------------------------------------------------
        // Each behind the filter it reports on, and behind the row rather than
        // in front of it: the two pads below are marked by lights of their own,
        // and a fifth beam standing in one of them would read as belonging to
        // the pad rather than to the button.
        site.Marker("yes", new(160f, 176f, 8f), "120 255 150").Set("spawnflags", "2");
        site.Marker("no", new(-160f, 176f, 8f), "255 140 140").Set("spawnflags", "2");

        site.Wire("anyone", "OnPass", site.Name("yes"), "LightOn");
        site.Wire("anyone", "OnFail", site.Name("yes"), "LightOff");
        site.Wire("byname", "OnPass", site.Name("no"), "LightOn");
        site.Wire("byname", "OnFail", site.Name("no"), "LightOff");

        //---------------------------------------------------------------------
        // Weight. A player is not a physics object and so weighs nothing at all
        // -- walking into this pad yourself does nothing, which is stock's
        // reading and not an omission. Shove a prop in instead: the drum is over
        // the twenty units the filter asks for and the can is nowhere near it,
        // and they are the same two props so that the only variable is mass.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.PropPhysics, "heavy", new(-208f, 48f, 48f), "model", Drum);
        site.Ent(EntityNames.PropPhysics, "light", new(-160f, 48f, 32f), "model", Can);

        var weighed = site.Brush(EntityNames.TriggerMultiple, "weighed",
            new(-232f, -80f, 8f), new(-136f, 16f, 88f), Site.Trigger,
            "wait", "1", "filtername", site.Name("bymass"));

        weighed.Wire("OnStartTouch", site.Name("scales"), "LightOn");
        weighed.Wire("OnEndTouchAll", site.Name("scales"), "LightOff");

        site.Marker("scales", new(-184f, -32f, 8f), "255 220 120").Set("spawnflags", "2");

        //---------------------------------------------------------------------
        // Range, and the pad is long because the range is the point.
        //
        // filter_enemy measures from the FILTER, not from whoever asked -- this
        // mod has no NPCs, so there is no acquiring AI whose position stock
        // would have used, and the mapper's own placement is the only position
        // this side of the boundary knows. The filter therefore sits at the far
        // end of its own pad: walk in at the near end and nothing happens,
        // because you are outside the ninety-six units it wants, and the light
        // comes on as you cross the marker.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.FilterEnemy, "byenemy", new(184f, 80f, 16f),
            "filtername", "!player", "filter_radius", "96", "filter_max_per_enemy", "0");

        // Players only, and that spawnflag is doing real work here. A filter
        // about an enemy reads backwards on purpose -- anything that is NOT the
        // named enemy passes untouched, which is stock and which is right for a
        // filter that exists to constrain one target -- so a crate rolled in
        // would light the marker from any distance and the range would look
        // broken. The pad narrows the question to the thing the filter names.
        var hunted = site.Brush(EntityNames.TriggerMultiple, "hunted",
            new(136f, -80f, 8f), new(232f, 96f, 88f), Site.Trigger,
            "wait", "1", "spawnflags", "1", "filtername", site.Name("byenemy"));

        hunted.Wire("OnStartTouch", site.Name("close"), "LightOn");
        hunted.Wire("OnEndTouchAll", site.Name("close"), "LightOff");

        // On the boundary itself, ninety-six units out from the filter.
        site.Marker("close", new(184f, -16f, 8f), "140 190 255").Set("spawnflags", "2");

        site.OnReset("anyone", "TestActivator");
        site.OnReset("byname", "TestActivator");

        foreach (string name in new[] { "filtered", "weighed", "hunted" })
            site.OnReset(name, "Enable");
    }

    private static void Timing(Site site)
    {
        site.Ent(EntityNames.LogicTimer, "timer", new(-64f, 64f, 16f),
            "refireTime", "2", "startDisabled", "0");

        site.Ent(EntityNames.MathCounter, "ticks", new(0f, 64f, 16f),
            "StartValue", "0", "Min", "0", "Max", "999");

        site.Ent(EntityNames.LogicRelay, "tick", new(64f, 64f, 16f));

        site.Ent(EntityNames.LogicAuto, "auto", new(-64f, -64f, 16f))
            .Wire("OnMapSpawn", site.Name("timer"), "Enable");

        site.Ent(EntityNames.LogicScript, "script", new(64f, -64f, 16f));

        site.Wire("timer", "OnTimer", site.Name("tick"), "Trigger");
        site.Wire("tick", "OnTrigger", site.Name("ticks"), "Add", "1");

        //---------------------------------------------------------------------
        // The third and fourth things in this station that fire without being on
        // the end of a wire. A game event is the engine's own broadcast channel —
        // a death, a join, a team change — and a listener is the bridge from that
        // channel into map logic, so a map can open a door when somebody dies
        // without anything in the map knowing how a death is reported.
        //
        // IsEnabled is written out rather than left to the default, because the
        // default is off: the keyfield starts zeroed in stock too, so a listener
        // a map forgot to enable is a listener that never fires and says nothing
        // about it. This is the station where that would be noticed.
        //
        // player_death is one of the five events C# declares, and the sandbox can
        // produce one — the hurt pad at the FORCES station will do it. The counter
        // is what makes it visible, since a listener has nothing to look at.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.LogicEventlistener, "deaths", new(-64f, 0f, 16f),
            "EventName", "player_death", "IsEnabled", "1",
            "TeamNum", "0", "FetchEventData", "0")
            .Wire("OnEventFired", site.Name("died"), "Add", "1");

        site.Ent(EntityNames.MathCounter, "died", new(-64f, -128f, 16f),
            "StartValue", "0", "Min", "0", "Max", "999");

        // The same class with the event name decided for it, and the event is one
        // nothing in this build raises: item_equip is not declared in
        // SourceSharp.Shared/GameEvents.cs and no code in src/game sends it, so
        // this resolves, accepts its keys, answers Enable and Disable, and never
        // reports. Stock is in the same position for the same reason.
        //
        // Placed rather than left out, and wired rather than left bare, because a
        // map may legally contain one and because the day somebody declares
        // item_equip this station shows it without being touched. Its two weapon
        // filters are left unwritten on purpose: the feed hands a listener a name,
        // a userid and a team and never the weapon, so a filter set here would be
        // a key that reads as doing something and does not.
        site.Ent(EntityNames.LogicEventlistenerItemequip, "equips", new(64f, 0f, 16f),
            "IsEnabled", "1", "TeamNum", "0")
            .Wire("OnEventFired", site.Name("equipped"), "Add", "1");

        site.Ent(EntityNames.MathCounter, "equipped", new(64f, -128f, 16f),
            "StartValue", "0", "Min", "0", "Max", "999");

        site.OnReset("ticks", "SetValue", "0");
        site.OnReset("timer", "ResetTimer");
        site.OnReset("deaths", "Enable");
        site.OnReset("equips", "Enable");
        site.OnReset("died", "SetValue", "0");
        site.OnReset("equipped", "SetValue", "0");
    }

    private static void Branching(Site site)
    {
        site.Ent(EntityNames.LogicBranch, "branch", new(-96f, 64f, 16f), "InitialValue", "0");
        site.Ent(EntityNames.LogicBranchListener, "listener", new(-32f, 64f, 16f),
            "Branch01", site.Name("branch"));

        site.Ent(EntityNames.LogicCase, "case", new(32f, 64f, 16f),
            "Case01", "1", "Case02", "2", "Case03", "3");

        site.Ent(EntityNames.LogicCompare, "compare", new(96f, 64f, 16f),
            "InitialValue", "0", "CompareValue", "3");

        site.Ent(EntityNames.LogicMulticompare, "multi", new(-32f, -64f, 16f),
            "IntegerValue", "1", "ShouldComparetoValue", "1");

        site.Ent(EntityNames.Multisource, "source", new(32f, -64f, 16f));

        site.OnReset("branch", "Toggle");
        site.OnReset("case", "PickRandom");
        site.OnReset("compare", "Compare");
        site.OnReset("listener", "Test");
        site.OnReset("source", "Trigger");
    }

    private static void Arithmetic(Site site)
    {
        site.Ent(EntityNames.MathCounter, "counter", new(-96f, 64f, 16f),
            "StartValue", "0", "Min", "0", "Max", "10");

        site.Ent(EntityNames.MathRemap, "remap", new(-32f, 64f, 16f),
            "in1", "0", "in2", "10", "out1", "0", "out2", "255");

        site.Ent(EntityNames.MathColorblend, "blend", new(32f, 64f, 16f),
            "inmin", "0", "inmax", "255",
            "colormin", "0 0 255", "colormax", "255 0 0");

        site.Ent(EntityNames.LogicLineto, "line", new(96f, 64f, 16f),
            "source", site.Name("counter"), "target", site.Name("blend"));

        site.Wire("counter", "OutValue", site.Name("remap"), "InValue");
        site.Wire("remap", "OutValue", site.Name("blend"), "InValue");

        site.OnReset("counter", "SetValue", "0");
        site.OnReset("counter", "Add", "1", delay: 0.5f);
    }

    private static void Rules(Site site)
    {
        site.Ent(EntityNames.GameScore, "score", new(-96f, 64f, 16f), "points", "1");
        site.Ent(EntityNames.GamePlayerEquip, "equip", new(-32f, 64f, 16f));
        site.Ent(EntityNames.GamePlayerHurt, "hurt", new(32f, 64f, 16f), "dmg", "5");

        site.Ent(EntityNames.EnvGlobal, "global", new(96f, 64f, 16f),
            "globalstate", "ss_sandbox_state", "initialstate", "0", "counter", "0");

        site.Ent(EntityNames.LogicCollisionPair, "pair", new(-64f, -64f, 16f),
            "attach1", "propsphys_crate", "attach2", "propsphys_crate2",
            "startdisabled", "1");

        site.Ent(EntityNames.LogicAutosave, "save", new(0f, -64f, 16f));
        site.Ent(EntityNames.LogicActiveAutosave, "activesave", new(64f, -64f, 16f));

        // game_end ends the map for everyone. It is placed so the class is
        // covered and deliberately wired to nothing -- a reset button that
        // ended the round would be a practical joke, not a demonstration.
        site.Ent(EntityNames.GameEnd, "end", new(0f, -128f, 16f));

        // The mode switch (tf2 stage 0.7): a mapper wires anything to its
        // SwitchTo <id> input and the map changes the game's mode at runtime.
        // Placed with no wiring and no target id, on game_end's argument: a
        // map may legally hold one, and a reset button that switched the
        // whole server's game mode would be a practical joke, not a demo.
        // The switch itself is GameModes.SwitchTo's, tested in C#.
        site.Ent(EntityNames.GameModeSwitch, "modeswitch", new(-96f, -128f, 16f));

        // The budget a map hands out for bodies, and the only game_ rule here
        // with nothing to look at: this mod has no ragdolls, so the number is
        // held and never spent. It is placed rather than left out because a map
        // that staged a fight would set it exactly like this, and the DX8 key
        // beside it is the same budget for a machine that cannot afford the
        // first one. What the button proves is that the input lands.
        site.Ent(EntityNames.GameRagdollManager, "ragdolls", new(160f, 64f, 16f),
            "MaxRagdollCount", "4", "MaxRagdollCountDX8", "2", "SaveImportant", "1");

        // The same budget for the pieces a breaking prop throws, beside it because
        // the pair is one idea said twice — and said once in code, since the
        // arithmetic choosing between the two numbers is
        // GameRagdollManager.Budget rather than a second copy of it. Stock wrote
        // that rule out twice, which is how two entities come to disagree about
        // it.
        //
        // Unread for a reason of its own: props_shared.cpp reaches a gib manager
        // through GetGibManager, whose first line is `#ifndef HL2_EPISODIC return
        // NULL`, and this is not an episodic build, so stock's own limit has never
        // applied in this mod either. What the numbers mean is worth knowing
        // anyway — it is a burst budget and not a world one, so a crate broken on
        // its own is never refused however much debris is already lying about.
        site.Ent(EntityNames.GameGibManager, "gibs", new(208f, 64f, 16f),
            "maxpieces", "12", "maxpiecesdx8", "6", "allownewgibs", "0");

        // A ballot box nothing in this build can open. g_voteControllerGlobal is
        // assigned only by TF's rules, no CBaseIssue subclass exists outside TF,
        // and the five user messages a vote is drawn with are registered only in
        // tf_usermessages.cpp — so the native entity has been an idle state
        // machine here since the mod began, and the managed one is the same
        // machine with its rules written down where they can be tested.
        //
        // Placed on the same argument as logic_proximity at the MARKERS station:
        // a map may legally contain one, this station already holds a budget
        // nobody reads, and doing nothing is only a failure where the station
        // promised something. It has no keys, no inputs and no outputs, so there
        // is nothing to wire to it and nothing for the button to reset.
        site.Ent(EntityNames.VoteController, "votes", new(-160f, 64f, 16f));

        //---------------------------------------------------------------------
        // The broom: at most one loose SMG on the floor and the rest swept every
        // two seconds. What it decides is not the number, which is a keyvalue,
        // but WHICH ones are surplus — and the answer is never the ones the level
        // designer placed. Whatever is in the world when it first looks is the
        // map's furniture, and everything that appears afterwards is loose.
        //
        // So it needs a death to demonstrate, which is worth saying out loud:
        // nothing in HL2MP drops a weapon on purpose, so the way to make a loose
        // one is to die holding it — the hurt pad at the FORCES station — and
        // then watch the pile thin back down to one. A manager named after a
        // weapon this mod does not have removes itself instead, which is stock's
        // answer too.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.GameWeaponManager, "broom", new(-208f, -64f, 16f),
            "weaponname", "weapon_smg1", "maxpieces", "1", "ammomod", "1");

        //---------------------------------------------------------------------
        // Controls, handed from the game to the map. While one of these holds a
        // player their keys stop being movement and become outputs, which is how
        // the SDK builds a turret console or a crane: the controls are a map
        // rather than a vehicle class.
        //
        // Press the station's reset button to sit down — the relay passes the
        // activator along, and the activator is whoever pressed. One light says
        // the controls are held and the other says forward is down, and both are
        // driven in both directions, so an unlit one is an answer rather than an
        // absence. Spawnflag 128 is the way back out, +use again, and only the
        // leading edge of it counts: otherwise the press that seats you would
        // eject you on the next tick, because +use is what put you there.
        //
        // FieldOfView is written at its default of -1, which is "never let go",
        // because that is the choice and not an omission: a console that released
        // the player for looking away would be indistinguishable from one that
        // dropped them, and the exit here is a key rather than a glance.
        //
        // A player at these controls is not frozen and keeps their weapon.
        // FL_ATCONTROLS and the weapon-holster path are both out of managed
        // reach, so the two spawnflags asking for them are carried and ignored;
        // that is why only the two exits are set here. See docs/divergences.md.
        //---------------------------------------------------------------------
        var ui = site.Ent(EntityNames.GameUi, "console", new(-208f, -128f, 16f),
            "FieldOfView", "-1", "spawnflags", "128");

        ui.Wire("PlayerOn", site.Name("seated"), "LightOn");
        ui.Wire("PlayerOff", site.Name("seated"), "LightOff");
        ui.Wire("PressedForward", site.Name("ahead"), "LightOn");
        ui.Wire("UnpressedForward", site.Name("ahead"), "LightOff");

        site.Marker("seated", new(-232f, -176f, 8f), "120 255 150").Set("spawnflags", "2");
        site.Marker("ahead", new(-176f, -176f, 8f), "140 190 255").Set("spawnflags", "2");

        //---------------------------------------------------------------------
        // The first of the two entities here with a button of their own, and both
        // have one because they do something to the PLAYER rather than to the
        // room: pressing this moves you onto the Rebels, and switching sides
        // mid-round kills you — which is HL2MP's rule and not this entity's. On
        // the station's reset button that would be a practical joke, the same
        // objection that keeps game_end off the end of a wire.
        //
        // "Rebels" rather than the name of a game_team_master, because stock's
        // two hops both lead nowhere: nothing in the tree registers that class
        // and CTeamplayRules::ChangePlayerTeam is a body Valve commented out, so
        // the stock entity moves nobody on any map. What survived to the end of
        // the chain was a team name, and the key holds one.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.GamePlayerTeam, "team", new(160f, -64f, 16f),
            "target", "Rebels");

        site.Brush(EntityNames.FuncButton, "teamswitch",
            new(144f, -112f, 8f), new(176f, -80f, 56f), Site.Switch, "wait", "2")
            .Wire("OnPressed", site.Name("team"), "Use");

        site.Marker("switch", new(160f, -136f, 8f), "255 140 140");

        //---------------------------------------------------------------------
        // game_player_equip's opposite number, and the second of the two with a
        // button of its own, on the same objection: a station reset that emptied
        // your inventory would be a practical joke rather than a demonstration.
        //
        // Strip DESTROYS what it takes, which is the distinction worth showing
        // and the reason it is not built out of dropping a weapon: a dropped gun
        // is left in the world for somebody else and only the active one goes,
        // and a map that strips a player before a scripted moment means the gear
        // to be gone. The suit is a second input rather than a keyvalue because
        // in stock the caller decides, so this button asks for the milder of the
        // two and the harder one is a wire away.
        //
        // The equipment is not gone for good: the sandbox arms a player on spawn,
        // so dying is the way back — which is worth knowing before pressing it.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.PlayerWeaponstrip, "strip", new(-128f, -64f, 16f));

        site.Brush(EntityNames.FuncButton, "stripswitch",
            new(-144f, -112f, 8f), new(-112f, -80f, 56f), Site.Switch, "wait", "2")
            .Wire("OnPressed", site.Name("strip"), "Strip");

        site.Marker("stripped", new(-128f, -136f, 8f), "255 140 140");

        //---------------------------------------------------------------------
        // The two entities that type at a console, side by side, because the
        // difference between them is the whole of what makes one of them the most
        // dangerous entity in the SDK: one reaches a single player's own settings
        // and one reaches the server.
        //
        // The client one gets impulse 100, the flashlight toggle — the most
        // visible harmless thing a client command can do, and a client is
        // entitled to ignore it either way. It only ever answers the activator,
        // which is why the reset button can drive it and a timer could not.
        //
        // The server one gets a line of chat, and on a default server it will
        // refuse to run it: sv_allow_point_servercommand is not "always" unless
        // an operator made it so, and that refusal IS the demonstration, because
        // what this entity owns is the permission rather than the command.
        // Nothing here changes level, execs a file or goes near rcon, and the
        // station's own button is the only thing wired to either.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.PointClientcommand, "toplayer", new(-64f, -176f, 16f));
        site.Ent(EntityNames.PointServercommand, "toserver", new(64f, -176f, 16f));

        //---------------------------------------------------------------------
        // Counting heads, which is the odd one of the family: a brush rather
        // than a point, and the only game_ entity that reads the world instead
        // of acting on whoever set it off.
        //
        // Stand on the lit square and press USE. The light stays on, because
        // the zone answered OnPlayerInZone for you; stand off it and press USE
        // and the same light goes out, because the same wire answered
        // OnPlayerOutZone instead. Both are wired, so an unlit light is an
        // answer rather than a failure -- and the counter beside it holds the
        // total, which is the part that scales past one player.
        //---------------------------------------------------------------------
        var heads = site.Brush(EntityNames.GameZonePlayer, "heads",
            new(-64f, 112f, 8f), new(64f, 224f, 128f), Site.Trigger);

        heads.Wire("OnPlayerInZone", site.Name("inside"), "LightOn");
        heads.Wire("OnPlayerOutZone", site.Name("inside"), "LightOff");
        heads.Wire("PlayersInCount", site.Name("headcount"), "SetValue");

        site.Ent(EntityNames.MathCounter, "headcount", new(0f, 240f, 16f),
            "StartValue", "0", "Min", "0", "Max", "64");

        site.Marker("inside", new(0f, 168f, 8f), "120 255 150");

        site.OnReset("global", "TurnOff");
        site.OnReset("pair", "DisableCollisions");
        site.OnReset("ragdolls", "SetMaxRagdollCount", "8");
        site.OnReset("gibs", "SetMaxPieces", "24");
        site.OnReset("broom", "SetMaxPieces", "4");
        site.OnReset("heads", "CountPlayersInZone");
        site.OnReset("console", "Activate");
        site.OnReset("toplayer", "Command", "impulse 100");
        site.OnReset("toserver", "Command", "say point_servercommand ran this");
    }

    private static void Constraints(Site site)
    {
        site.Ent(EntityNames.PhysConstraintsystem, "system", new(0f, 160f, 16f));

        site.Ent(EntityNames.PropPhysics, "anchorprop", new(-160f, 64f, 48f), "model", Crate,
            "spawnflags", "8");
        site.Ent(EntityNames.PropPhysics, "hanger", new(-160f, 64f, 128f), "model", Can);

        site.Ent(EntityNames.PhysConstraint, "fixed", new(-160f, 64f, 96f),
            "attach1", site.Name("anchorprop"), "attach2", site.Name("hanger"),
            "constraintsystem", site.Name("system"));

        site.Ent(EntityNames.PropPhysics, "hingebase", new(-32f, 64f, 48f), "model", Crate,
            "spawnflags", "8");
        site.Ent(EntityNames.PropPhysics, "hingearm", new(-32f, 64f, 128f), "model", Chair);

        site.Ent(EntityNames.PhysHinge, "hinge", new(-32f, 64f, 96f),
            "attach1", site.Name("hingebase"), "attach2", site.Name("hingearm"),
            "hingeaxis", "0 0 1", "hingefriction", "0");

        site.Ent(EntityNames.PropPhysics, "ballbase", new(96f, 64f, 48f), "model", Crate,
            "spawnflags", "8");
        site.Ent(EntityNames.PropPhysics, "ballarm", new(96f, 64f, 128f), "model", Can);

        site.Ent(EntityNames.PhysBallsocket, "ball", new(96f, 64f, 96f),
            "attach1", site.Name("ballbase"), "attach2", site.Name("ballarm"));

        site.Ent(EntityNames.PropPhysics, "ropetop", new(0f, -96f, 160f), "model", Crate,
            "spawnflags", "8");
        site.Ent(EntityNames.PropPhysics, "ropeload", new(0f, -96f, 48f), "model", Drum);

        site.Ent(EntityNames.PhysLengthconstraint, "rope", new(0f, -96f, 104f),
            "attach1", site.Name("ropetop"), "attach2", site.Name("ropeload"),
            "attachpoint", (site.At + new Point(0f, -96f, 48f)).ToString(),
            "minlength", "0", "addlength", "0");

        // A spring, which belongs in this station because it looks like a
        // constraint and behaves like nothing else here: the load hangs BELOW
        // where the rope would hold it and bounces, because a spring limits
        // nothing and only pulls.
        site.Ent(EntityNames.PropPhysics, "springtop", new(160f, -96f, 176f), "model", Crate,
            "spawnflags", "8");
        site.Ent(EntityNames.PropPhysics, "springload", new(160f, -96f, 64f), "model", Drum);

        site.Ent(EntityNames.PhysSpring, "spring", new(160f, -96f, 176f),
            "attach1", site.Name("springtop"), "attach2", site.Name("springload"),
            "springaxis", (site.At + new Point(160f, -96f, 64f)).ToString(),
            // 2000, not stock's default of 150, because a spring has to lift
            // what it holds: the barrel's weight is mass times gravity in
            // Source's units, which 150 does not come close to. At the default
            // the barrel simply lies on the floor with the spring stretched
            // taut, which reads as the spring not existing.
            "constant", "2000", "damping", "1", "relativedamping", "0.1",
            "length", "80");

        foreach (string name in new[] { "hanger", "hingearm", "ballarm", "ropeload", "springload" })
            site.OnReset(name, "Wake");

        // Winching the load in is the input that shows what a spring's length
        // actually means: it is the separation at which the spring pulls with
        // nothing, so shortening it drags the load up.
        site.OnReset("spring", "SetSpringLength", "80");
    }

    private static void Controllers(Site site)
    {
        site.Ent(EntityNames.PropPhysics, "wheel", new(-160f, 64f, 48f), "model", Drum);
        site.Ent(EntityNames.PhysMotor, "motor", new(-160f, 64f, 48f),
            "attach1", site.Name("wheel"), "speed", "200", "spinup", "1",
            "axis", "0 0 1", "inertiafactor", "1");

        site.Ent(EntityNames.PropPhysics, "sled", new(-32f, 64f, 48f), "model", Crate);
        site.Ent(EntityNames.PhysThruster, "thruster", new(-32f, 64f, 48f),
            "attach1", site.Name("sled"), "force", "300", "forcetime", "2",
            "angles", "0 90 0");

        site.Ent(EntityNames.PropPhysics, "spinner", new(96f, 64f, 48f), "model", Crate);
        site.Ent(EntityNames.PhysTorque, "torque", new(96f, 64f, 48f),
            "attach1", site.Name("spinner"), "force", "200", "forcetime", "2",
            "axis", "0 0 1");

        site.Ent(EntityNames.PropPhysics, "standing", new(0f, -96f, 48f), "model", Chair);
        site.Ent(EntityNames.PhysKeepupright, "upright", new(0f, -96f, 48f),
            "attach1", site.Name("standing"), "angularlimit", "15");

        foreach (string name in new[] { "wheel", "sled", "spinner", "standing" })
            site.OnReset(name, "Wake");

        site.OnReset("thruster", "Activate");
        site.OnReset("torque", "Activate");
    }

    private static void Sensors(Site site)
    {
        site.Ent(EntityNames.PropPhysics, "drum", new(0f, 64f, 48f), "model", Drum);

        site.Ent(EntityNames.PhysMotor, "spin", new(0f, 64f, 48f),
            "attach1", site.Name("drum"), "speed", "150", "spinup", "1",
            "axis", "0 0 1", "inertiafactor", "1");

        site.Ent(EntityNames.PointAngularvelocitysensor, "angular", new(-96f, -64f, 32f),
            "target", site.Name("drum"), "axis", "0 0 1",
            "threshold", "50", "fireinterval", "0.2", "usehelper", "0");

        site.Ent(EntityNames.PointVelocitysensor, "linear", new(96f, -64f, 32f),
            "target", site.Name("drum"), "axis", "0 0 1", "enabled", "1");

        // Which way the drum is FACING, rather than how fast it is turning. It
        // watches the drum against a point beside it, so spinning the drum
        // sweeps the angle through the tolerance and back out again.
        site.Ent(EntityNames.InfoTarget, "facing", new(0f, 0f, 48f));

        site.Ent(EntityNames.PointAnglesensor, "angle", new(-96f, -32f, 32f),
            "target", site.Name("drum"), "lookatname", site.Name("facing"),
            "tolerance", "30", "duration", "0", "StartDisabled", "0");

        // How far the drum is from a fixed point, reported as a number rather
        // than as a threshold — the only sensor here whose output carries a
        // value.
        site.Ent(EntityNames.PointProximitySensor, "near", new(96f, -32f, 32f),
            "target", site.Name("drum"), "StartDisabled", "0");

        site.OnReset("drum", "Wake");
    }

    /// <summary>
    /// The three entities that act on whatever is near them rather than on
    /// something they were told the name of.
    ///
    /// Each gets a pad rather than sharing the station's button, because they
    /// differ in WHEN they act — a blast is an event, a push field is a state
    /// that lasts while you stand in it — and one button could not show that.
    /// </summary>
    /// <summary>
    /// The brushes that are simulated rather than moved.
    ///
    /// Worth a station of its own next to BREAKABLES because the difference is
    /// invisible until something touches them: a func_breakable panel is part
    /// of the map until it shatters, and a func_physbox is furniture.
    /// </summary>
    private static void PhysicsBrushes(Site site)
    {
        // A stack, so it is obvious they are simulated: an unsimulated brush
        // would hang in the air exactly where it was drawn.
        for (int i = 0; i < 3; i++)
        {
            site.Brush(EntityNames.FuncPhysbox, $"box{i}",
                new(-176f + (i * 8f), 32f, 16f + (i * 40f)),
                new(-128f + (i * 8f), 80f, 52f + (i * 40f)),
                Site.Pad, "health", "0", "massScale", "0.5");
        }

        // Breakable, to show that a physbox is a breakable that also falls over.
        site.Brush(EntityNames.FuncPhysbox, "fragile",
            new(-32f, 32f, 16f), new(32f, 80f, 80f), Site.Wall,
            "health", "40", "minhealthdmg", "0");

        // Beside the plain one on purpose, because the demonstration IS that
        // you cannot tell them apart. Stock's multiplayer physbox is a physbox
        // plus IMultiplayerPhysics -- an interface whose only consumers sit in
        // obstacle_pushaway.cpp, which nothing in this build calls -- plus
        // COLLISION_GROUP_PUSHAWAY, which the managed one deliberately does not
        // take. Pushaway means "non-solid to the player, and the player's move
        // code shoves it aside instead", and only the first half still runs
        // here, so reproducing it would have made this the one crate in the map
        // you walk through with nothing pushing it. It collides instead.
        site.Brush(EntityNames.FuncPhysboxMultiplayer, "mpbox",
            new(48f, 32f, 16f), new(112f, 80f, 80f), Site.Wall,
            "health", "40", "minhealthdmg", "0");

        // Pinned until it is shot loose, which is the one behaviour a mapper
        // cannot get from func_breakable or from a plain physbox.
        site.Brush(EntityNames.FuncPhysbox, "pinned",
            new(112f, 32f, 96f), new(176f, 80f, 144f), Site.Accent,
            "health", "60", "spawnflags", "32768", "damagetoenablemotion", "40");

        // The only brush in the map a player moves by walking into it, and it
        // takes no code at all: stock pins a pushable's mass at thirty however
        // big the mapper drew it, because what makes this a pushable rather than
        // a wall is that a player wins against it, and vphysics already resolves
        // a player against a light object.
        //
        // No spawnflag 128, which is the entity's other half. Without it a
        // pushable cannot be hurt at all — shooting it shoves it — and with it,
        // it is func_breakable that also falls over, which is the physbox
        // directly in front of it. So the flag is left off and the two stay two
        // different things.
        site.Brush(EntityNames.FuncPushable, "cart",
            new(-24f, 120f, 8f), new(24f, 168f, 56f), Site.Accent,
            "health", "1", "minhealthdmg", "0");

        // Scenery until the button is pressed, which is the whole of what
        // phys_convert does: neither a func_brush nor a prop_dynamic is ever
        // simulated, so one hanging in the air is unambiguous before and after.
        //
        // ONE CONVERTER PER KIND, because stock's CreateSimplePhysicsObject makes
        // a different class of each: a simple_physics_brush of a brush model
        // (the slab) and a simple_physics_prop of anything else (the crate). So
        // the button makes one of each, each original goes, and its name moves
        // across to the copy (tools/live-gate's phys-convert step checks all
        // three). Two converters rather than one target name, because a name is
        // one entity in this map (Site.Remember). scenery1 is the control: a
        // prop_dynamic nothing targets, which stays.
        //
        // These replaced a hand-placed simple_physics_brush and
        // simple_physics_prop, which stood in for the pair because the
        // conversion converted nothing until c0283726a.
        site.Brush(EntityNames.FuncBrush, "slab",
            new(-64f, -128f, 64f), new(-16f, -80f, 112f), Site.Pad);

        for (int i = 0; i < 2; i++)
        {
            site.Ent(EntityNames.PropDynamicOverride, $"scenery{i}", new(112f + (i * 48f), -128f, 72f),
                "model", Crate, "solid", "6");
        }

        site.Ent(EntityNames.PhysConvert, "convertslab", new(-40f, -160f, 16f),
            "target", site.Name("slab"));

        site.Ent(EntityNames.PhysConvert, "convert", new(136f, -160f, 16f),
            "target", site.Name("scenery0"));

        site.OnReset("convertslab", "ConvertTarget");
        site.OnReset("convert", "ConvertTarget");

        // A magnet with something metal to catch. Pinned, because a magnet that
        // is itself simulated falls on the thing it is meant to pick up.
        // Motion disabled (2) and sucking (4). Pinned because a magnet that is
        // itself simulated falls on the thing it is meant to pick up.
        site.Ent(EntityNames.PhysMagnet, "magnet", new(-160f, -128f, 96f),
            "model", "models/props_junk/metal_paintcan001a.mdl",
            "spawnflags", "6", "forcelimit", "0", "torquelimit", "0",
            "maxobjects", "4");

        // One dropped onto it, and two lying underneath. The dropped one sticks
        // on contact; the loose ones are what the suck is for, and the magnet
        // only reaches for them once something has already landed on it.
        site.Ent(EntityNames.PropPhysics, "filings", new(-160f, -128f, 160f), "model", Can);
        site.Ent(EntityNames.PropPhysics, "loose1", new(-192f, -112f, 24f), "model", Can);
        site.Ent(EntityNames.PropPhysics, "loose2", new(-128f, -144f, 24f), "model", Can);

        foreach (string name in new[] { "box0", "box1", "box2", "cart" })
            site.OnReset(name, "Wake");
    }

    /// <summary>
    /// The one station whose entities are not all in the map when it loads.
    ///
    /// A point_template EATS what it names: the entities it lists are turned
    /// into stored text during level load and removed from the world, so the
    /// crate below is never seen until something spawns a copy of it. The
    /// stashing still happens inside the entity parser, where managed code has
    /// no moment to act — but the ENTITY is C# now, and this station is the only
    /// live check that the four calls it makes into that parser still line up.
    ///
    /// The room check is what the station is really for. The maker sits above a
    /// marked spot; stand on it, press reset, and nothing appears, because the
    /// hull it would occupy is full of player.
    /// </summary>
    private static void Spawning(Site site)
    {
        // The thing being copied. It vanishes at load, into the template.
        //
        // A C#-owned class on purpose: this could not be one until the template
        // pass learnt about the parallel hierarchy, and it briefly read
        // prop_physics_multiplayer -- a native class -- to work around that. It
        // is the station's job to prove the fix. Both ends of the pass are
        // managed now, which makes this the check that the ONE remaining native
        // half still finds an entity of either kind.
        site.Ent(EntityNames.PropPhysics, "stock", new(0f, 96f, 40f), "model", Crate);

        // Two more in the template, wired to EACH OTHER, and both C#-owned.
        //
        // This is what proves the wiring survives instancing. Name fixup
        // rewrites the copies' targetnames AND the outputs that point at them,
        // so a copy's relay must reach the copy's counter and not the original's
        // -- the original is eaten by the template at load and no longer exists.
        // Every step of that is text manipulation on stored map data, which is
        // why it works for the parallel hierarchy at all.
        site.Ent(EntityNames.MathCounter, "count", new(-64f, 96f, 8f),
            "startvalue", "0", "min", "0", "max", "0")
            .Set("targetname", site.Name("count"));

        site.Ent(EntityNames.LogicRelay, "relay", new(-96f, 96f, 8f))
            .Wire("OnTrigger", site.Name("count"), "Add", "5");

        // No spawnflags: 1 would keep the sources in the world and 2 PRESERVES
        // names, which is the opposite of what a spawner wants -- every copy
        // would answer to the original's name and anything wired by name would
        // reach all of them at once. Unset means fixup on, which is the default
        // for the good reason.
        site.Ent("point_template", "tmpl", new(0f, 96f, 8f),
            "Template01", site.Name("stock"),
            "Template02", site.Name("relay"),
            "Template03", site.Name("count"));

        // CHECK_FOR_SPACE (8) and no auto-spawn: the map asks for one when the
        // button is pressed, and is told when it could not have one.
        site.Ent(EntityNames.EnvEntityMaker, "maker", new(0f, 96f, 8f),
            "EntityTemplate", site.Name("tmpl"),
            "PostSpawnSpeed", "120", "PostSpawnDirection", "0 0 0",
            "PostSpawnDirectionVariance", "0.4", "PostSpawnInheritAngles", "0",
            "spawnflags", "8");

        // Where to stand to see the check refuse. The crate lands here, so the
        // second press is already blocked by the first crate unless it rolled.
        site.Marker("spot", new(0f, 96f, 96f), "255 200 140");

        site.OnReset("maker", "ForceSpawn");
    }

    /// <summary>
    /// The three effects that DO something to something else.
    ///
    /// Grouped because they share one division of labour rather than one file:
    /// what burns or sparks is an effect the client draws, and what is managed
    /// is which entity, for how long, and when. Both are wired to the station's
    /// own reset button.
    ///
    /// env_entity_dissolver is NOT here and could not be: the engine builds one
    /// BY CLASSNAME for its own dissolve effect, so owning the name makes that
    /// effect construct a managed shell. See docs/divergences.md.
    /// </summary>
    private static void Effects(Site site)
    {
        // Two props rather than one: burning and dissolving look nothing alike
        // and the point is to tell them apart.
        //
        // BURNING WORKS AGAIN, and what stood here recorded that it did not.
        // The note said the station demonstrated a REFUSAL: a flame is an
        // entity PARENTED to what it burns, SetParent takes a CBaseEntity, and
        // prop_physics_multiplayer had just stopped being one -- so the igniter
        // found its target and could attach nothing to it.
        //
        // Both halves of that have gone. Parenting a standalone child to either
        // kind of parent landed in shell_standalone_hierarchy.cpp, and then the
        // flame itself became managed (ss_entityflame), so the two entities are
        // on the SAME hierarchy and the link is ordinary. Proved live rather
        // than assumed: while it burns, cs_sv_hierarchy reports
        //   ss_entityflame '(unnamed)' -> standalone 'effects_burnme'
        //   local 0.00 0.00 0.00
        // and the client says which particle system it started.
        //
        // DISSOLVING IS STILL A REFUSAL, and is now the whole reason there are
        // two props here rather than one: that effect is a shader the client
        // draws over a CBaseAnimating, and an entity on the parallel hierarchy
        // is not one. Repointing it at prop_ragdoll would not help -- a ragdoll
        // needs a model compiled with a ragdoll skeleton and the drum has none,
        // so that swap trades an honest refusal for a prop that fails for an
        // unrelated reason. See docs/divergences.md.
        site.Ent(EntityNames.PropPhysicsMultiplayer, "burnme", new(-96f, 64f, 40f), "model", Drum);

        site.Ent(EntityNames.EnvEntityIgniter, "igniter", new(-96f, 0f, 16f),
            "target", site.Name("burnme"), "lifetime", "20");


        // Directional (512) and audible, so it reads as a fault in the wall
        // rather than as decoration. Started by the reset button, not at spawn:
        // a station that is already sparking when you walk in has not shown you
        // that anything switched it on.
        site.Ent(EntityNames.EnvSpark, "sparks", new(0f, 96f, 48f),
            "MaxDelay", "0.4", "Magnitude", "2", "TrailLength", "2",
            "spawnflags", "512", "angles", "0 270 0");

        // A screen whose material has several frames, stepped one at a time.
        // The brush carries the animated material; the toggle only says which
        // frame is up.
        site.Brush(EntityNames.FuncBrush, "screen",
            new(96f, 120f, 24f), new(160f, 124f, 88f), Site.Accent,
            "solidity", "2");

        site.Ent(EntityNames.EnvTexturetoggle, "toggle", new(96f, 64f, 16f),
            "target", site.Name("screen"));

        // Zoom is fired at whoever walks in rather than by the reset button:
        // it acts on ONE pair of eyes, so it needs an activator that is a
        // player, and the button's relay is not one.
        site.Ent(EntityNames.EnvZoom, "zoom", new(160f, 64f, 16f),
            "FOV", "30", "Rate", "1.0");

        var zoomPad = site.Brush(EntityNames.TriggerMultiple, "zoompad",
            new(112f, 0f, 8f), new(208f, 64f, 72f), Site.Trigger, "spawnflags", "1");

        zoomPad.Wire("OnStartTouch", site.Name("zoom"), "Zoom");
        zoomPad.Wire("OnEndTouch", site.Name("zoom"), "UnZoom");

        // A dispenser, and the reset button is the coin slot. Walk over the can
        // it drops for a point of health; press again for the next one, up to
        // the three it holds.
        site.Ent(EntityNames.EnvBeverage, "drinks", new(-160f, 64f, 24f),
            "beveragetype", "6", "health", "3");

        // A plate of real metal to step onto, and a trigger watching for it.
        //
        // The material is chosen for its SURFACEPROP, not its look. This map
        // used to be textured entirely from DEV/DEV_MEASURE*, every one of
        // which is $surfaceprop "concrete": there was exactly one game material
        // in the whole map and nothing could notice a change of ground, so this
        // station fired nothing at all.
        //
        // The palette is now concrete / tile / wood / carpet underfoot, and
        // this plate is the only metal in it -- deliberately.
        // CEnvPlayerSurfaceTrigger::SetPlayerSurface is called from game
        // movement and told to EVERY trigger in the map, so a second metal
        // walkable anywhere would fire this station from across the hall.
        // MaterialCatalogueTests.WalkableSurfacesAreDistinctAndOnlyOneIsMetal
        // holds that. 77 is 'M', the metal game material.
        site.Solid(new(-208f, -112f, 8f), new(-112f, -16f, 12f), Site.Metal);

        site.Ent(EntityNames.EnvPlayerSurfaceTrigger, "surface", new(-160f, -64f, 16f),
            "gamematerial", "77", "StartDisabled", "0");

        // Debris, thrown upward and outward. Physics simulation so it tumbles
        // and settles rather than sliding: the difference between the modes is
        // the visible thing this demonstrates.
        site.Ent(EntityNames.EnvShooter, "debris", new(160f, 120f, 24f),
            "shootmodel", "models/gibs/hgibs.mdl",
            "m_iGibs", "6", "m_flVelocity", "260", "m_flVariance", "0.35",
            "m_flGibLife", "12", "delay", "0.12", "Simulation", "1",
            "gibanglevelocity", "200", "gibgravityscale", "1",
            "shootsounds", "3", "scale", "1", "skin", "0",
            "angles", "-70 0 0", "spawnflags", "1");

        // The same machine with the model already chosen, which is all that
        // separates the two classnames. Point simulation rather than physics, so
        // the pair shows both modes side by side.
        site.Ent(EntityNames.Gibshooter, "gibs", new(208f, 120f, 24f),
            "m_iGibs", "4", "m_flVelocity", "220", "m_flVariance", "0.4",
            "m_flGibLife", "10", "delay", "0.1", "Simulation", "0",
            "angles", "-70 0 0", "spawnflags", "1");

        // A laser across the plot, pointed at a marker, that hurts anything
        // standing in it. Off at spawn; the reset button switches it on.
        site.Ent(EntityNames.InfoTarget, "laserend", new(-160f, 160f, 48f));

        site.Ent(EntityNames.EnvLaser, "laser", new(160f, 160f, 48f),
            "texture", "sprites/laserbeam.spr",
            "LaserTarget", site.Name("laserend"),
            "width", "4", "NoiseAmplitude", "0", "TextureScroll", "35",
            "renderamt", "180", "rendercolor", "255 40 40",
            "damage", "20", "spawnflags", "0");

        // A beam strung between two markers, which is the mode this mod
        // carries over: it stays lit and follows both ends. Started on, so the
        // station shows one without being asked.
        site.Ent(EntityNames.InfoTarget, "beamA", new(-208f, 160f, 96f));
        site.Ent(EntityNames.InfoTarget, "beamB", new(208f, 160f, 96f));

        site.Ent(EntityNames.EnvBeam, "fence", new(0f, 160f, 96f),
            "texture", "sprites/laserbeam.spr",
            "LightningStart", site.Name("beamA"),
            "LightningEnd", site.Name("beamB"),
            // Quiet numbers on purpose. The first pass used noise 8 at
            // brightness 200 and the beam read as a lens-shaped blob rather
            // than a line — a laser sprite is already additive, so amplitude
            // and brightness compound.
            "life", "0", "BoltWidth", "2", "NoiseAmplitude", "2",
            "TextureScroll", "20", "renderamt", "110", "rendercolor", "120 200 255",
            "spawnflags", "1");

        //---------------------------------------------------------------------
        // Somebody offscreen shooting at something else: the tracers and the
        // noise of a firefight with neither side in the map.
        //
        // It belongs beside env_laser rather than with the emitters because it
        // is the same division of labour as the rest of this station -- what is
        // managed is the SHAPE of the firefight, and only the tracer is drawn.
        // The bursts, the lulls, the rate, the width of the group and how the
        // rounds are DISTRIBUTED across that width are all decisions, and the
        // bias is the one worth watching: at 1 the rounds bunch towards the
        // middle, at 0 they are flat across the circle, and below 0 they are
        // pushed towards its edge, which is what makes suppressing fire look
        // like it is going past rather than at.
        //
        // Nothing here does damage -- a round is a trace and a tracer -- so
        // walking through the line of fire is safe, which is why it can run
        // across the back of a station a player has to stand in.
        //
        // Off at spawn and switched on by the button, for the reason the sparks
        // are: a station that is already firing when you walk in has not shown
        // you that anything started it.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.InfoTarget, "shootat", new(200f, -176f, 40f));

        site.Ent(EntityNames.EnvGunfire, "gunfire", new(-200f, -176f, 40f),
            "target", site.Name("shootat"),
            "minburstsize", "3", "maxburstsize", "8",
            "minburstdelay", "1", "maxburstdelay", "3",
            "rateoffire", "10", "spread", "5", "bias", "1",
            "collisions", "0", "startdisabled", "1",
            "shootsound", "Weapon_AR2.Single");

        site.Marker("m1", new(200f, -176f, 8f), "255 140 140");

        site.OnReset("igniter", "Ignite");
        site.OnReset("gunfire", "Enable");
        site.OnReset("sparks", "StartSpark");
        site.OnReset("toggle", "IncrementTextureIndex");
        site.OnReset("drinks", "Activate");
        site.OnReset("debris", "Shoot");
        site.OnReset("gibs", "Shoot");
        site.OnReset("laser", "Toggle");
        site.OnReset("fence", "Toggle");
    }

    private static void Fire(Site site)
    {
        // The three sit in a ROW, and the spacing IS the station.
        //
        // A fire reaches about half its own size to spread and half of that
        // again to burn, so the numbers here are chosen so that each step can
        // only have come from the step before: the source is close enough to
        // light the left fire and too far to light the right one, and the right
        // one is inside the left one's spread. Nothing here is decoration.
        //
        // Spawnflag 1 is INFINITE: no fuel to spend, so they burn until put out.
        // A fire GIVEN fuel removes itself when it runs out, which is right for
        // one dropped by something passing through and wrong for a station you
        // want to try twice.
        site.Ent(EntityNames.EnvFiresource, "source", new(-220f, 0f, 16f),
            "fireradius", "50", "firedamage", "60", "spawnflags", "0");

        // Large on purpose. A 96-unit fire only BURNS 24 units out, which is
        // less than a crate is wide -- so at that size nothing can be placed
        // near enough to catch without being placed on top of the fire, and a
        // crate on top of a fire blocks the trace the fire uses to find the
        // floor. At 192 there is room for both.
        site.Ent(EntityNames.EnvFire, "left", new(-180f, 0f, 16f),
            "firesize", "192", "fireattack", "6", "ignitionpoint", "80",
            "damagescale", "1", "firetype", "0", "spawnflags", "1");

        site.Ent(EntityNames.EnvFire, "right", new(-120f, 0f, 16f),
            "firesize", "64", "fireattack", "6", "ignitionpoint", "20",
            "damagescale", "1", "firetype", "0", "spawnflags", "1");

        // Beside the left fire rather than between the two, close enough to
        // burn and clear of the spot the fire needs to trace down through.
        site.Ent(EntityNames.PropPhysics, "fuel", new(-180f, 44f, 40f), "model", Crate);

        // Close enough to see both fires. heatlevel is low and heattime short on
        // purpose: a sandbox is for watching the thing work, and with a long
        // hold there is no telling a broken sensor from a patient one.
        site.Ent(EntityNames.EnvFiresensor, "sensor", new(0f, -64f, 16f),
            "fireradius", "256", "heatlevel", "8", "heattime", "1",
            "spawnflags", "1");

        // Counters, because the outputs are the half of this station with no
        // picture. A flame tells you a fire is lit; only these say whether it
        // SAID so, and whether the sensor agreed.
        site.Ent(EntityNames.MathCounter, "lit", new(-180f, 128f, 16f),
            "StartValue", "0", "Min", "0", "Max", "1");

        site.Ent(EntityNames.MathCounter, "spread", new(-120f, 128f, 16f),
            "StartValue", "0", "Min", "0", "Max", "1");

        site.Ent(EntityNames.MathCounter, "hot", new(0f, 128f, 16f),
            "StartValue", "0", "Min", "0", "Max", "1");

        site.Wire("left", "OnIgnited", site.Name("lit"), "SetValue", "1");
        site.Wire("left", "OnExtinguished", site.Name("lit"), "SetValue", "0");
        site.Wire("right", "OnIgnited", site.Name("spread"), "SetValue", "1");
        site.Wire("sensor", "OnHeatLevelStart", site.Name("hot"), "SetValue", "1");
        site.Wire("sensor", "OnHeatLevelEnd", site.Name("hot"), "SetValue", "0");

        site.OnReset("source", "Enable");
    }

    private static void Emitters(Site site)
    {
        // A blast under a stack of crates. Magnitude is well over what it takes
        // to move them: the point is to see it work, not to tune it.
        for (int i = 0; i < 4; i++)
            site.Ent(EntityNames.PropPhysics, $"crate{i}", new(-160f, 96f, 24f + (i * 40f)), "model", Crate);

        site.Ent(EntityNames.EnvPhysexplosion, "blast", new(-160f, 96f, 16f),
            "magnitude", "150", "radius", "200", "spawnflags", "1");

        site.Brush(EntityNames.TriggerMultiple, "blastpad", new(-208f, -80f, 8f), new(-112f, -16f, 72f),
            Site.Trigger, "wait", "3", "spawnflags", "1")
            .Wire("OnStartTouch", site.Name("blast"), "Explode");

        // Enabled by standing in it and disabled on the way out, so the field is
        // visibly a state rather than an event. Players and physics both: 8|16.
        // Offset from the field rather than sitting on it: a push points away
        // from its own origin, so a can directly underneath is shoved into the
        // floor and demonstrates nothing.
        site.Ent(EntityNames.PropPhysics, "driftcan", new(-64f, 96f, 24f), "model", Can);

        var push = site.Ent(EntityNames.PointPush, "push", new(32f, 96f, 32f),
            "magnitude", "400", "radius", "200", "enabled", "0", "spawnflags", "24");

        var pushPad = site.Brush(EntityNames.TriggerMultiple, "pushpad", new(-48f, -80f, 8f), new(48f, -16f, 72f),
            Site.Trigger, "spawnflags", "1");

        pushPad.Wire("OnStartTouch", site.Name("push"), "Enable");
        pushPad.Wire("OnEndTouch", site.Name("push"), "Disable");

        // Pointed along its own angles at a can a little way off, so the shove
        // lands on the side rather than through the centre and the can tips.
        site.Ent(EntityNames.PropPhysics, "target", new(160f, 160f, 24f), "model", Drum);

        // The magnitude is large because an impact is a FORCE applied for one
        // physics frame, not an impulse: it is multiplied by a fraction of a
        // second before it reaches the drum. Maps use numbers in the thousands
        // for the same reason, and a plausible-looking 600 moves nothing.
        site.Ent(EntityNames.EnvPhysimpact, "impact", new(160f, 32f, 40f),
            "magnitude", "12000", "distance", "256", "angles", "0 90 0");

        site.Brush(EntityNames.TriggerMultiple, "impactpad", new(112f, -80f, 8f), new(208f, -16f, 72f),
            Site.Trigger, "wait", "3", "spawnflags", "1")
            .Wire("OnStartTouch", site.Name("impact"), "Impact");

        // The visible half of an explosion, next to the physics-only one, since
        // the pair is the thing a mapper most often gets the wrong way round.
        // Small radius so it does not reach the blast station's crates.
        //
        // spawnflags 2 is SF_ENVEXPLOSION_REPEATABLE and it is not decoration:
        // without it CEnvExplosion::Explode ends in UTIL_Remove( this ), so the
        // FIRST press of this station's reset button deletes the entity the
        // station exists to demonstrate, and every press after that reaches
        // nothing. This was 0 until the Phase 4 batch pass measured the map
        // after pulling every reset — env_explosion was the one classname the
        // server no longer had. A reset that destroys its own subject is the
        // opposite of what docs/sandbox.md promises a reset does.
        site.Ent("env_explosion", "bang", new(0f, 160f, 24f),
            "iMagnitude", "60", "iRadiusOverride", "120", "spawnflags", "2");

        // A shake to go with it, felt everywhere so it is unmistakable.
        site.Ent(EntityNames.EnvShake, "shake", new(0f, 160f, 40f),
            "amplitude", "6", "frequency", "40", "duration", "1.5",
            "radius", "0", "spawnflags", "1");

        // The other two things a map does to a player's screen. Grouped with the
        // shake because they share a shape: the decision is here, the transport
        // is a user message, and none of the three is visible from outside the
        // game -- which is the point of having a map you can walk around in.
        site.Ent(EntityNames.EnvFade, "fade", new(0f, 160f, 56f),
            "duration", "0.4", "holdtime", "0.2",
            "rendercolor", "255 220 160", "renderamt", "200", "spawnflags", "0");

        site.Ent(EntityNames.EnvViewpunch, "punch", new(0f, 160f, 72f),
            "punchangle", "6 0 0", "radius", "0", "spawnflags", "1");

        site.OnReset("bang", "Explode");
        site.OnReset("shake", "StartShake");
        site.OnReset("fade", "Fade");
        site.OnReset("punch", "ViewPunch");

        foreach (string name in new[] { "crate0", "crate1", "crate2", "crate3", "driftcan", "target" })
            site.OnReset(name, "Wake");
    }

    //=========================================================================
    // Movement along a path, and the three kinds of node that describe one.
    //
    // Split across four stations rather than gathered into one, because the
    // node types are not variants of each other: a path_corner is a place with
    // a pause, a path_track is a doubly linked rail with branches, and a
    // keyframe_track is a schedule. What walks each is a different class with a
    // different idea of what "where am I" means, and putting three of them on
    // one plot would have hidden exactly that.
    //
    // ONE THING IS SHARED BY ALL OF THEM AND IS EASY TO MISS: every brush here
    // carries an explicit "origin" key. vbsp only offsets a brush entity's
    // geometry when it finds one -- Hammer writes it from a tools/toolsorigin
    // brush, and a generated map has no Hammer -- so a brush without one has
    // its origin at the WORLD origin. A mover then travels relative to a point
    // two thousand units away and a rotating one sweeps the whole hall. See
    // docs/divergences.md.
    //=========================================================================

    /// <summary>
    /// The oldest mover in the SDK, walking the oldest node type.
    ///
    /// A func_train has no direction to reverse and no idea where it is beyond
    /// which corner it last reached, so a round trip is a mapper drawing the
    /// corners out and back. This one is a closed rectangle, which is the same
    /// trick with the last corner naming the first.
    /// </summary>
    private static void Trains(Site site)
    {
        // At the height of the train's MIDDLE and not at floor level. A
        // func_train puts its CENTRE on the corner it is heading for -- stock
        // works out the same offset and calls its own arithmetic busted in a
        // comment -- so a chain drawn on the floor would bury the brush in it.
        site.Ent(EntityNames.PathCorner, "c1", new(-176f, 176f, 32f),
            "target", site.Name("c2"));

        // The speed belongs to the corner being LEFT, not the one being
        // approached, and that asymmetry is the reason this key is here rather
        // than on the train: the leg out of this corner runs at 200 and the
        // rest of the loop at the train's own 80, so one side of the rectangle
        // is visibly quicker than the other three. Stock reads it the same way
        // and maps were timed against it.
        site.Ent(EntityNames.PathCorner, "c2", new(176f, 176f, 32f),
            "target", site.Name("c3"), "speed", "200");

        //---------------------------------------------------------------------
        // The far corner, and the one entity in this station that is not what
        // its name suggests.
        //
        // path_corner_crash is stock's EMPTY subclass of path_corner: an HL1
        // name kept so that HL1 maps still load, adding no keys, no inputs and
        // no behaviour of its own there or here. So a worked example of it is a
        // working corner that the train cannot tell from the other three, and
        // making it a dead end would be inventing a meaning it has never had.
        //
        // It is the corner with the pause and the red beam, so that the one
        // thing a player can check -- that a mover treats it as an ordinary
        // stop -- is the thing the station shows.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.PathCornerCrash, "c3", new(176f, -32f, 32f),
            "target", site.Name("c4"), "wait", "2", "speed", "80");

        site.Ent(EntityNames.PathCorner, "c4", new(-176f, -32f, 32f),
            "target", site.Name("c1"));

        site.Brush(EntityNames.FuncTrain, "train",
            new(-208f, 144f, 8f), new(-144f, 208f, 56f), Site.Accent,
            "origin", (site.At + new Point(-176f, 176f, 32f)).ToString(),
            "target", site.Name("c1"), "speed", "80", "dmg", "0");

        site.Marker("m1", new(-176f, 176f, 8f), "255 220 120");
        site.Marker("m2", new(176f, 176f, 8f), "255 220 120");
        site.Marker("m3", new(176f, -32f, 8f), "255 140 140");
        site.Marker("m4", new(-176f, -32f, 8f), "255 220 120");

        // Stop before Start, and the order is not tidiness.
        //
        // A named train spawns marked "waiting to be sent for", and Start clears
        // that mark and sets off. Once it is running, Start alone does nothing
        // at all -- there is no mark left to clear -- so a reset button wired
        // only to Start works exactly once and then looks broken. Stopping first
        // also makes the press visible on a train that was already going round.
        site.OnReset("train", "Stop");
        site.OnReset("train", "Start", delay: 0.6f);
    }

    /// <summary>
    /// The other train, and the difference is not that it is bigger.
    ///
    /// A func_train runs a sequence of errands. A track train has no sequence:
    /// it has a direction and a speed, its track is linked both ways, and every
    /// input a map has for it is about one of those two. So this station is a
    /// line rather than a loop, and the far node is what turns the train round.
    /// </summary>
    private static void Tracks(Site site)
    {
        // The nodes are where the train's ORIGIN goes, plus its height key --
        // not where its centre goes, which is the func_train rule one station
        // along. Two movers, two conventions, and the brushes are drawn to suit.
        site.Ent(EntityNames.PathTrack, "n1", new(-208f, 96f, 48f), "target", site.Name("n2"));
        site.Ent(EntityNames.PathTrack, "n2", new(-64f, 96f, 48f), "target", site.Name("n3"));

        // A node with a speed of its own, which is how a map slows a train for a
        // corner without wiring anything: the train takes the number as it
        // passes.
        site.Ent(EntityNames.PathTrack, "n3", new(80f, 96f, 48f),
            "target", site.Name("n4"), "speed", "60");

        site.Ent(EntityNames.PathTrack, "n4", new(208f, 96f, 48f));

        site.Brush(EntityNames.FuncTracktrain, "train",
            new(-240f, 72f, 24f), new(-176f, 120f, 72f), Site.Accent,
            "origin", (site.At + new Point(-208f, 96f, 48f)).ToString(),
            "target", site.Name("n1"),
            // No "speed", which is how a track train says "sit still until
            // something starts me": stock's default is zero here and a hundred
            // on a func_train, and the two classes mean opposite things by an
            // absent key. startspeed is the ceiling every scaled input is a
            // fraction of.
            "startspeed", "140", "wheels", "64", "height", "0", "dmg", "0");

        // The far end sends it back, which is the whole of what makes this a
        // shuttle: nothing here decides to turn round, a node does.
        site.Wire("n4", "OnPass", site.Name("train"), "StartBackward", "", 1.5f);

        //---------------------------------------------------------------------
        // The controls, and they are PLACED AND INERT -- there is nothing to
        // stand on here and no way to drive this train.
        //
        // The entity itself works: it measures its own box in the train's frame
        // and hands it over, and a wrong name is reported. What cannot work is
        // the other half. Stock offers a player the controls from
        // CBasePlayer::PlayerUse and HandleFuncTrain, and both reach the train
        // through GetGroundEntity() -- which can never be a parallel-hierarchy
        // brush, because the movement code resolves the ground through a cast
        // that answers nothing for one. So no player is ever offered a managed
        // train and OnControls is never called.
        //
        // Placed rather than left out, on the argument logic_proximity is placed
        // on at the MARKERS station: a map may legally contain one, the volume
        // is the half that CAN be got right, and the station says out loud that
        // the other half is missing. See docs/divergences.md.
        //---------------------------------------------------------------------
        site.Brush(EntityNames.FuncTraincontrols, "controls",
            new(-232f, 80f, 72f), new(-184f, 112f, 144f), Site.Trigger,
            "origin", (site.At + new Point(-208f, 96f, 108f)).ToString(),
            "target", site.Name("train"),
            // Named so the entity does not warn. Stock gets the offset for free
            // from being parented; there is no parenting on this hierarchy, so
            // the box is measured against the train's map position instead and
            // the key is only a note that the mapper meant to.
            "parentname", site.Name("train"));

        site.Marker("m1", new(-208f, 96f, 8f), "140 190 255");
        site.Marker("m2", new(80f, 96f, 8f), "255 220 120");
        site.Marker("m3", new(208f, 96f, 8f), "120 255 150");

        site.OnReset("train", "Stop");
        site.OnReset("train", "TeleportToPathTrack", site.Name("n1"), delay: 0.4f);
        site.OnReset("train", "StartForward", delay: 0.8f);
    }

    /// <summary>
    /// Two turntables, and the difference between them is WHEN they turn.
    ///
    /// Both are a platform big enough to hold a train, which swings between two
    /// tracks and takes the train with it; only one of the two tracks it names
    /// is live at a time, and which one is whichever end the platform is at. So
    /// a train running off a changer has exactly one way to go and the changer
    /// decides which by being somewhere.
    ///
    /// The manual one is pressed. The automatic one switches the nodes one PAST
    /// each end instead, so a train running towards it already knows which way
    /// it will leave -- and it is the arriving TRAIN that sets it off, which is
    /// the one place in this family where a managed entity has to ask what an
    /// activator is.
    ///
    /// A train apiece, because the two mechanisms want the train in different
    /// places at once and one shared between them would spend most of its time
    /// parked at the wrong end of the plot.
    /// </summary>
    private static void Turntables(Site site)
    {
        //---------------------------------------------------------------------
        // Left: the one with a button.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.PathTrack, "m1", new(-128f, 208f, 44f), "target", site.Name("m2"));

        // The end of the main line, and it sits ON the platform. That is the
        // arrangement the whole mechanism rests on: a changer narrows each track
        // it names to the node nearest its own middle, and everything after that
        // compares node identities.
        site.Ent(EntityNames.PathTrack, "m2", new(-128f, 48f, 44f));

        // The siding, which is a chain of its own rather than a fork. A fork is
        // a path_track's altpath and is taken by the train; this is taken by the
        // platform, and the two nodes at the middle are deliberately in the same
        // place so that swinging is all it takes to change which chain the train
        // is on.
        site.Ent(EntityNames.PathTrack, "g1", new(-128f, 48f, 44f), "target", site.Name("g2"));
        site.Ent(EntityNames.PathTrack, "g2", new(-240f, 48f, 44f));

        site.Brush(EntityNames.FuncTracktrain, "train1",
            new(-152f, 184f, 24f), new(-104f, 232f, 64f), Site.Accent,
            "origin", (site.At + new Point(-128f, 208f, 44f)).ToString(),
            "target", site.Name("m1"),
            // The wheelbase is not decoration here. A changer decides whether a
            // train is parked ON it by comparing the flat distance against this
            // number, so a train that never says has a wheelbase of zero and can
            // never be carried by anything.
            "startspeed", "120", "wheels", "64", "height", "0", "dmg", "0");

        // Square, because it turns a quarter and has to look the same afterwards.
        // Spawnflag 16 is stock's "do not move": a changer bolted to the floor
        // takes the journey's duration from the TURN instead of from a climb,
        // which is why the speed key here reads as degrees per second.
        site.Brush(EntityNames.FuncTrackchange, "changer",
            new(-192f, -16f, 8f), new(-64f, 112f, 24f), Site.Pad,
            "origin", (site.At + new Point(-128f, 48f, 16f)).ToString(),
            "toptrack", site.Name("m2"), "bottomtrack", site.Name("g1"),
            "train", site.Name("train1"),
            "rotation", "90", "speed", "45", "spawnflags", "16");

        // A button of its own rather than the station's, because it is an
        // instruction and not a reset: the platform only moves when a player
        // decides the train has arrived, and that decision is the entity.
        site.Brush(EntityNames.FuncButton, "swing",
            new(-160f, -152f, 8f), new(-96f, -120f, 56f), Site.Switch, "wait", "4")
            .Wire("OnPressed", site.Name("changer"), "Use");

        // Three seconds later, which is the turn plus a moment. A changer hands
        // the train its new node and stops there -- it does not start it -- so
        // without this the train sits on the siding facing the right way and
        // never goes.
        site.Wire("swing", "OnPressed", site.Name("train1"), "StartForward", "", 3f);

        //---------------------------------------------------------------------
        // Right: the one that decides for itself.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.PathTrack, "a1", new(128f, 208f, 44f), "target", site.Name("a2"));
        site.Ent(EntityNames.PathTrack, "a2", new(128f, 48f, 44f), "target", site.Name("a3"));

        // The node one PAST the platform, and the reason the automatic changer
        // needs the line to continue at all: it switches the nodes beyond each
        // end rather than the ones on them, so this is what gets closed to stop
        // the train running off the far side, and closing it is what parks the
        // train on the platform in the first place.
        site.Ent(EntityNames.PathTrack, "a3", new(128f, -112f, 44f));

        site.Ent(EntityNames.PathTrack, "b1", new(128f, 48f, 44f), "target", site.Name("b2"));
        site.Ent(EntityNames.PathTrack, "b2", new(240f, 48f, 44f), "speed", "120");

        site.Brush(EntityNames.FuncTracktrain, "train2",
            new(104f, 184f, 24f), new(152f, 232f, 64f), Site.Accent,
            "origin", (site.At + new Point(128f, 208f, 44f)).ToString(),
            "target", site.Name("a1"),
            "startspeed", "120", "wheels", "64", "height", "0", "dmg", "0");

        site.Brush(EntityNames.FuncTrackautochange, "autochanger",
            new(64f, -16f, 8f), new(192f, 112f, 24f), Site.Pad,
            "origin", (site.At + new Point(128f, 48f, 16f)).ToString(),
            "toptrack", site.Name("a2"), "bottomtrack", site.Name("b1"),
            "train", site.Name("train2"),
            "rotation", "90", "speed", "45", "spawnflags", "16");

        //---------------------------------------------------------------------
        // What makes it automatic, and it IS the Trigger input.
        //
        // The changer splits on WHO fired it: a func_tracktrain means "I am here,
        // swing if you are going to", and anything else means "swap which way the
        // branch points, and let the next train find it that way". This is the
        // wiring stock was built for, and it is the node rather than the train
        // that carries the answer -- CFuncTrackTrain::Next announces itself to
        // the node it reached with AcceptInput( "InPass", this, this, ... ), and
        // CPathTrack::InputPass passes that same activator straight into OnPass.
        // So the node fires and the TRAIN is what the changer sees.
        //
        // That could not be said in this build until an activator stopped being
        // CBaseEntity-typed all the way down the event queue -- see activator.h.
        // Measured with a control, twice. Across builds, same map and the same
        // binary but for whether PathTrack.Pass names the follower: without it
        // the changer read "not a train" on all 90 arrivals sampled and the
        // platform never left 90 degrees of yaw, so the train parked on it and
        // stayed there; with it the platform swung on every arrival. Within one
        // run: the train arriving swept it 0 to 88 degrees and back, while four
        // Trigger inputs fired at the same entity from the dev API -- same input
        // name, activator not a train -- moved it not at all.
        //
        // The delay is load-bearing. A train still under power is refused
        // whatever its position, and a train announces reaching a node from
        // inside the arrival, before the dead end that stops it. Fired with no
        // delay this always says "blocking".
        //---------------------------------------------------------------------
        site.Wire("a2", "OnPass", site.Name("autochanger"), "Trigger", "", 0.4f);

        // And the same problem at the other end: the automatic changer WILL give
        // a parked train the speed of the node it has just opened, but it does
        // that before it takes the train aboard, while the old branch is still
        // the one under it -- so the train dead-ends again and arrives on the
        // siding stopped. The node it left is what sends it on.
        site.Wire("a2", "OnPass", site.Name("train2"), "StartForward", "", 4f);

        site.Marker("beam1", new(-128f, 208f, 8f), "140 190 255");
        site.Marker("beam2", new(-240f, 48f, 8f), "120 255 150");
        site.Marker("beam3", new(128f, 208f, 8f), "140 190 255");
        site.Marker("beam4", new(240f, 48f, 8f), "120 255 150");

        // Both halves back to the main line, then both trains back to the top of
        // it. Staged, because a changer refuses to move while a train is half on
        // it and a teleport into a swinging platform is a train in mid-air.
        site.OnReset("train1", "Stop");
        site.OnReset("train2", "Stop");
        site.OnReset("changer", "GoUp", delay: 0.2f);
        site.OnReset("autochanger", "GoUp", delay: 0.2f);
        site.OnReset("train1", "TeleportToPathTrack", site.Name("m1"), delay: 2.8f);
        site.OnReset("train2", "TeleportToPathTrack", site.Name("a1"), delay: 2.8f);
        site.OnReset("train1", "StartForward", delay: 3.2f);
        site.OnReset("train2", "StartForward", delay: 3.2f);
    }

    /// <summary>
    /// The third node type, and the only one that owns its own schedule.
    ///
    /// A corner and a track node are places, and whoever walks them decides how
    /// fast. A keyframe carries the speed itself, so it decides how long the leg
    /// OUT of it takes -- and when two keys are in the same place and differ
    /// only in facing, the same number reads as degrees and the leg is a turn.
    ///
    /// What walks them draws nothing at all: a move_keyframed is a point entity
    /// with no model, and it IS a keyframe -- the head of its own chain. So it
    /// needs something to watch it, which is what the other half of this station
    /// is for.
    /// </summary>
    private static void Keyframes(Site site)
    {
        site.Ent(EntityNames.KeyframeTrack, "k1", new(96f, 128f, 120f),
            "NextKey", site.Name("k2"), "MoveSpeed", "80");

        site.Ent(EntityNames.KeyframeTrack, "k2", new(96f, 32f, 120f),
            "NextKey", site.Name("k3"), "MoveSpeed", "80");

        // Names nothing, which is how a chain ends rather than a mistake. A
        // mover that runs out turns round and comes back, so the rectangle is
        // walked both ways forever -- there are no inputs on a move_keyframed
        // and nothing can stop it.
        site.Ent(EntityNames.KeyframeTrack, "k3", new(-96f, 32f, 120f), "MoveSpeed", "80");

        site.Ent(EntityNames.MoveKeyframed, "mover", new(-96f, 128f, 120f),
            "NextKey", site.Name("k1"), "MoveSpeed", "80",
            // Read back and used by nothing. Stock hands them to the keyframe
            // library, which fits a spline through the keys on either side of a
            // leg; this travels the straight line between two. Written out so
            // that a map's values survive a round trip. See docs/divergences.md.
            "PositionInterpolator", "0", "RotationInterpolator", "0");

        //---------------------------------------------------------------------
        // Something to watch it with, and the four names are the whole entity.
        //
        // Where the measured entity sits RELATIVE to its reference is worked out
        // every tick and put back on the target relative to the TARGET's
        // reference. So the pair of references is what makes this a measurement
        // of movement rather than of position -- and it is why the crate can
        // trace the mover's rectangle somewhere else entirely without either
        // knowing about the other.
        //
        // Scale is left at 1. The obvious thing to do here is shrink the copy,
        // and the obvious thing is a trap: the key divides the offset, so a
        // mapper who reads it as "how big" and writes 2 gets a copy that leaves
        // the plot.
        //---------------------------------------------------------------------
        //---------------------------------------------------------------------
        // The OTHER keyframe family, overhead where nothing walks: a rope.
        //
        // move_rope is the head of a chain and keyframe_rope every link after
        // it, each naming the next in NextKey — and every link whose NextKey
        // resolves is one drawn span, sagging by its Slack. The tail names
        // nothing, which is how a chain ends: it draws no span of its own and
        // exists to be hung FROM, the same shape as k3 above. Two spans, with
        // visibly different slack so the sag is the thing the station shows.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.MoveRope, "rope0", new(-96f, 128f, 230f),
            "NextKey", site.Name("rope1"), "Slack", "120",
            "Width", "2", "TextureScale", "1", "Subdiv", "4", "Type", "0",
            "RopeMaterial", "cable/cable.vmt");

        site.Ent(EntityNames.KeyframeRope, "rope1", new(96f, 128f, 230f),
            "NextKey", site.Name("rope2"), "Slack", "30",
            "Width", "2", "TextureScale", "1", "Subdiv", "4", "Type", "0",
            "RopeMaterial", "cable/cable.vmt");

        site.Ent(EntityNames.KeyframeRope, "rope2", new(96f, 32f, 230f));

        site.Ent(EntityNames.InfoTarget, "from", new(-96f, 128f, 120f));
        site.Ent(EntityNames.InfoTarget, "home", new(-96f, -64f, 40f));

        // prop_dynamic and not prop_physics, and that is the point rather than a
        // convenience: this entity TELEPORTS its target every tick, and anything
        // the solver also owns would spend the frame fighting it.
        site.Ent(EntityNames.PropDynamicOverride, "copy", new(-96f, -64f, 40f), "model", Crate);

        site.Ent(EntityNames.LogicMeasureMovement, "measure", new(0f, -160f, 16f),
            "MeasureTarget", site.Name("mover"),
            "MeasureReference", site.Name("from"),
            "target", site.Name("copy"),
            "TargetReference", site.Name("home"),
            "TargetScale", "1", "MeasureType", "0");

        site.Marker("m1", new(-96f, 128f, 120f), "255 220 120");
        site.Marker("m2", new(96f, 128f, 120f), "255 220 120");
        site.Marker("m3", new(96f, 32f, 120f), "255 220 120");
        site.Marker("m4", new(-96f, 32f, 120f), "255 220 120");

        site.OnReset("measure", "Disable");
        site.OnReset("measure", "Enable", delay: 1f);
    }

    /// <summary>
    /// Two brushes that move what is ON them rather than themselves.
    ///
    /// A func_platrot is a lift with a turn bolted into its timing rather than
    /// on top of it -- the turn takes exactly as long as the climb, so it
    /// arrives level rather than still swinging. A func_conveyor never moves at
    /// all and is the only brush in the map that hands a rider a velocity.
    ///
    /// Both carry an explicit origin, and the belt needs one for a second
    /// reason: it finds its riders with a box query around its own origin, so
    /// a belt without one looks for riders at the middle of the map.
    /// </summary>
    private static void Belts(Site site)
    {
        // Drawn where it is when it is UP, because a named platform starts at
        // the top and is sent for. Drawn at floor level it would have to sink
        // into the ground to have anywhere to go.
        site.Brush(EntityNames.FuncPlatrot, "lift",
            new(-208f, 64f, 104f), new(-96f, 176f, 120f), Site.Pad,
            "origin", (site.At + new Point(-152f, 120f, 112f)).ToString(),
            // Spawnflag 1 is stock's toggle bit: without it the platform drops
            // back by itself three seconds after it arrives, which is right for
            // a step and wrong for something a player is standing on.
            "height", "96", "rotation", "180", "speed", "60", "spawnflags", "1");

        site.Brush(EntityNames.FuncButton, "call",
            new(-184f, -24f, 8f), new(-120f, 8f, 56f), Site.Switch, "wait", "3")
            .Wire("OnPressed", site.Name("lift"), "Toggle");

        //---------------------------------------------------------------------
        // The belt. Direction is written as ANGLES and means a vector, which is
        // stock's own conversion and the reason the key is called movedir: 0 0 0
        // is straight down +X.
        //
        // What it carries is found by position rather than by being told. Stock
        // puts FL_CONVEYOR on the brush and the movement code carries whatever
        // is standing on it; neither half is reachable from a parallel-hierarchy
        // entity, so a rider is anything whose feet are at the top face. The
        // ride is then base velocity, which is the one channel the movement code
        // does not overwrite from the player's input each tick.
        //---------------------------------------------------------------------
        site.Brush(EntityNames.FuncConveyor, "belt",
            new(-192f, -168f, 8f), new(192f, -72f, 16f), Site.Accent,
            "origin", (site.At + new Point(0f, -120f, 12f)).ToString(),
            "movedir", "0 0 0", "speed", "100");

        // Ends, so the cans stay in the station. A belt with nothing at the end
        // of it delivers its riders into whatever is next door.
        site.Solid(new(192f, -168f, 8f), new(200f, -72f, 56f), Site.Wall);
        site.Solid(new(-200f, -168f, 8f), new(-192f, -72f, 56f), Site.Wall);

        //---------------------------------------------------------------------
        // The cans are the COUNTER-example, and the station says so out loud.
        //
        // A belt carries by base velocity, which is the engine's channel for
        // "moved by something else" and is read by the movement code and by step
        // movement. It means nothing at all to the physics solver, so the class
        // asks for a movetype of walk or step before it pushes anything — and a
        // prop_physics is neither. A crate lying on a stock conveyor is not
        // carried either; the belt is a floor for players.
        //
        // Left here rather than removed, because "the cans do not move" is the
        // question every player will ask within two seconds of walking up to a
        // belt, and a station that answers it is worth more than one that avoids
        // it. Measured, not assumed: three cans on the running belt sat exactly
        // where they were dropped.
        //---------------------------------------------------------------------
        for (int i = 0; i < 3; i++)
            site.Ent(EntityNames.PropPhysics, $"can{i}", new(-64f + (i * 64f), -120f, 32f), "model", Can);

        site.Brush(EntityNames.FuncButton, "flip",
            new(24f, -24f, 8f), new(88f, 8f, 56f), Site.Switch, "wait", "2")
            .Wire("OnPressed", site.Name("belt"), "ToggleDirection");

        site.Marker("m1", new(-152f, 120f, 8f), "140 190 255");
        site.Marker("m2", new(0f, -120f, 16f), "255 220 120");

        site.OnReset("lift", "GoUp");
        site.OnReset("belt", "SetSpeed", "100");

        foreach (string name in new[] { "can0", "can1", "can2" })
            site.OnReset(name, "Wake");
    }

    /// <summary>
    /// The three entities that take something away from a player, or notice one
    /// standing on something.
    ///
    /// Grouped because none of them acts on the room: a plate weighs what is on
    /// it, a volume suppresses part of the movement code for whoever is inside,
    /// and a constraint hands one player four numbers that follow them out of
    /// it. The last two are per player and not per volume, which is why neither
    /// has a refire delay.
    /// </summary>
    private static void PlayerMovement(Site site)
    {
        //---------------------------------------------------------------------
        // A plate that weighs its load.
        //
        // Stock measures the normal force at each of the plate's own contact
        // points and divides the sum by gravity on the way out, saying "sum is
        // now kg" in its last comment. This weighs what is standing on the plate
        // instead, which is the same quantity and the question the keyvalue
        // asks. A PLAYER weighs nothing to it either way -- a player is not in
        // the solver -- so the drum is not decoration, it is the only thing here
        // that can press this.
        //---------------------------------------------------------------------
        site.Brush(EntityNames.FuncWeightButton, "plate",
            new(-176f, 64f, 8f), new(-80f, 160f, 20f), Site.Accent,
            "WeightToActivate", "20");

        site.Ent(EntityNames.PropPhysics, "load", new(-128f, 112f, 160f), "model", Drum);

        // Spawnflag 1: put it back where the MAP drew it rather than bringing it
        // here, which is the difference between a destination and a reset
        // button. The drum then falls onto the plate again and the light comes
        // back on, so the press is repeatable.
        site.Ent(EntityNames.PointTeleport, "hoist", new(-128f, 112f, 32f),
            "target", site.Name("load"), "spawnflags", "1");

        // Both directions wired, so an unlit lamp is an answer rather than an
        // absence. Equal to the threshold is deliberately neither pressed nor
        // released -- stock's pair of strict comparisons -- which matters for a
        // plate keyed to exactly one crate's mass.
        site.Wire("plate", "OnPressed", site.Name("weighed"), "LightOn");
        site.Wire("plate", "OnReleased", site.Name("weighed"), "LightOff");

        site.Marker("weighed", new(-128f, 32f, 8f), "120 255 150").Set("spawnflags", "2");

        //---------------------------------------------------------------------
        // A pipe to duck under, and the volume that ducks for you.
        //
        // Two spawnflags and nothing else: no keys, no outputs. 0x800 holds the
        // duck button down for whoever is inside, and 0x80 takes away the
        // engine's habit of moving a player where they did not steer -- no
        // auto-step, no auto-ladder, no wall slide. Maps use the first under a
        // low pipe and the second around a ledge.
        //
        // The lintel is real geometry and is forty units above the floor, which
        // is under a standing player and over a ducked one. Without something
        // that cannot be walked through there is no way to tell a volume that
        // ducked you from one that did nothing.
        //---------------------------------------------------------------------
        site.Solid(new(16f, 64f, 48f), new(208f, 160f, 88f), Site.Wall);

        // Taller than the lintel and thirty-two units wider than it on every
        // side, which is not tidiness: a volume that stops at the lintel's
        // underside only ever contains a player who is ALREADY under it, and a
        // player is caught by the pipe before they are caught by the trigger.
        // The band around it is where the duck has to be asked for.
        //
        // 2177 and not 2176, and the 1 is the whole difference between a volume
        // that works and one that is never touched by anybody. 0x800 and 0x80
        // are this class's own two flags, and a volume's KIND flags live in the
        // same word: the base asks whether clients or everything-else is wanted
        // before it looks at anything, and a spawnflags value that is non-zero
        // and names neither answers "nothing". Measured -- at 2176 the volume's
        // OnStartTouch never fired for a player standing squarely inside it.
        //
        // NOT A DIVERGENCE, and that was measured too, against the stock HL2MP
        // build rather than reasoned from the source: five identical volumes
        // over one live player in one run, and a stock trigger_multiple keyed
        // 2176 was never entered while the same volume keyed 2177 was. The bit
        // is what every stock map has always had to set; this station keys what
        // a mapper has to key, not a workaround for anything of ours. The base
        // now warns at spawn when a volume is keyed so nothing can touch it.
        site.Brush(EntityNames.TriggerPlayermovement, "duck",
            new(-16f, 40f, 8f), new(232f, 184f, 88f), Site.Trigger,
            "spawnflags", "2177");

        site.Marker("m2", new(112f, 32f, 8f), "140 190 255");

        //---------------------------------------------------------------------
        // A leash, and it is a leash rather than a fence: what it does is make
        // walking away slower and slower, not stop it.
        //
        // The slowing itself happens inside movement, per command, on both the
        // server and the client, off four networked numbers on the PLAYER. What
        // is decided here is who is held, when they got out, and what happens
        // then -- and the snapshot is taken at TurnOn and never again, which is
        // stock's reading and is why the station's own button is what applies
        // it: you have to be standing near the button to be caught by it.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.PointPlayermoveconstraint, "leash", new(0f, -120f, 32f),
            "radius", "180", "width", "80", "speedfactor", "0.15");

        site.Wire("leash", "OnConstraintBroken", site.Name("escaped"), "LightOn");

        site.Marker("escaped", new(0f, -184f, 8f), "255 140 140").Set("spawnflags", "2");
        site.Marker("m3", new(0f, -120f, 8f), "200 160 255");

        site.OnReset("escaped", "LightOff");
        site.OnReset("leash", "TurnOff");
        site.OnReset("leash", "TurnOn", delay: 0.4f);
        site.OnReset("hoist", "Teleport");
        site.OnReset("load", "Wake", delay: 0.1f);
        site.OnReset("duck", "Enable");
    }

    /// <summary>
    /// The three constraints that would not fit beside the first five.
    ///
    /// A second plot rather than a bigger one: the CONSTRAINTS station is
    /// already five rigs of two props each, and a constraint is only legible
    /// while there is room to shove one end of it without hitting the next.
    ///
    /// What these three have in common is that each is a shape rather than a
    /// joint -- a rope over two pulleys, a line, and a cone of angles -- so each
    /// needs its ends put somewhere particular before it means anything.
    /// </summary>
    private static void Joints(Site site)
    {
        //---------------------------------------------------------------------
        // One rope over two pulleys. The entity's own origin is the first
        // pulley and position2 is the second, in WORLD coordinates -- Hammer
        // draws it as a line you drag, so the key holds a place and not an
        // offset.
        //
        // The gear ratio goes over unchanged, one and all: native measures the
        // rope as extra + firstSpan + secondSpan * ratio and only substitutes 1
        // for a zero ratio AFTERWARDS, so an unset key gives a rope that ignores
        // the second span entirely. Written out here because the sandbox should
        // not depend on which side of that quirk it lands on.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.PropPhysics, "counterweight", new(-176f, 112f, 64f), "model", Drum);
        site.Ent(EntityNames.PropPhysics, "sandbag", new(-64f, 112f, 152f), "model", Crate);

        site.Ent(EntityNames.PhysPulleyconstraint, "pulley", new(-176f, 112f, 224f),
            "attach1", site.Name("counterweight"), "attach2", site.Name("sandbag"),
            "position2", (site.At + new Point(-64f, 112f, 224f)).ToString(),
            "addlength", "0", "gearratio", "1", "forcelimit", "0", "torquelimit", "0");

        //---------------------------------------------------------------------
        // A rail. The far end is a POINT and not a direction, for the same
        // reason: Hammer draws the axis as a line from the entity to here.
        //
        // Only one end is named, so the other is the world -- and the WORLD goes
        // first, which is not cosmetic. The first object of a constraint is the
        // reference and everything the second gets is measured against it, so a
        // slide built the other way round measures its travel limits from the
        // world's origin and points its motor at the world. Measured: a drum on
        // a world-anchored slide did not shift a unit under a motor until the
        // ends were ordered this way.
        //
        // Spawnflag 2 puts stops on both ends. Without it the rail has none and
        // the drum runs off either end of it.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.PropPhysics, "carriage", new(96f, 160f, 72f), "model", Drum);

        site.Ent(EntityNames.PhysSlideconstraint, "rail", new(96f, 160f, 72f),
            "attach1", site.Name("carriage"),
            "slideaxis", (site.At + new Point(96f, -32f, 72f)).ToString(),
            "slidefriction", "0", "systemloadscale", "1",
            "spawnflags", "2", "forcelimit", "0", "torquelimit", "0");

        // The motor, which is the half of a slide that a shove cannot show: the
        // value is units per second along the axis and the sign is which way.
        // Driving it back to the far end is also what makes it a reset.
        site.OnReset("rail", "SetVelocity", "-90");

        //---------------------------------------------------------------------
        // A joint that may swing only so far about each of its own three axes.
        //
        // The limits are degrees about the CONSTRAINT's frame -- its position
        // and the angles it was turned to -- rather than about either object, so
        // a transposition between the three pairs would limit the wrong axis and
        // look like a joint that was simply built crooked.
        //
        // Zero to zero on X and Y and forty-five either way on Z: the arm may
        // turn and may not fall over, which is one thing a hinge cannot do and
        // one a ballsocket cannot either.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.PropPhysics, "socket", new(208f, 32f, 48f), "model", Crate,
            "spawnflags", "8");

        site.Ent(EntityNames.PropPhysics, "limb", new(208f, 32f, 128f), "model", Chair);

        site.Ent(EntityNames.PhysRagdollconstraint, "ragdoll", new(208f, 32f, 96f),
            "attach1", site.Name("socket"), "attach2", site.Name("limb"),
            "xmin", "0", "xmax", "0", "ymin", "0", "ymax", "0",
            "zmin", "-45", "zmax", "45",
            "xfriction", "0", "yfriction", "0", "zfriction", "0",
            "forcelimit", "0", "torquelimit", "0");

        site.Marker("m1", new(-120f, 112f, 8f), "255 220 120");
        site.Marker("m2", new(96f, 64f, 8f), "140 190 255");
        site.Marker("m3", new(208f, -32f, 8f), "200 160 255");

        foreach (string name in new[] { "counterweight", "sandbag", "carriage", "limb" })
            site.OnReset(name, "Wake");
    }

    /// <summary>
    /// Three things vphysics does that are not constraints, and the last of them
    /// is here on the same argument game_ragdoll_manager is at the RULES
    /// station: placed, wired, and honest about doing nothing.
    /// </summary>
    private static void VPhysics(Site site)
    {
        //---------------------------------------------------------------------
        // A volume that everything physical inside it moves THROUGH rather than
        // in: lighter gravity, thicker air, a ceiling on how fast anything may
        // fall. One motion controller for the whole volume rather than one per
        // entrant, which is the difference from trigger_wind -- a wind blows on
        // each object differently, and a medium is one medium.
        //
        // WALK IN, then press the button and watch the crate come down. It
        // drifts rather than drops: an eighth of gravity to get it moving,
        // thick air, and a ceiling of 70 units a second that it reaches about
        // a third of the way down and then holds all the way to the floor.
        // Measured -- 212 to 28 takes about three seconds of game time, against
        // the eight tenths of a second the same fall takes outside the volume.
        //
        // The player is the other half and is not the medium at all: entering
        // switches on the pair of flags that make a player wade, which is one
        // call. Both halves arrive by the same route, which is the SOLVER
        // rather than the engine's trigger lists -- see the note in
        // TriggerVPhysicsMotion.OnSpawn for why a vphysics volume has to be
        // told about in that direction.
        //
        // Penned, because a crate that drifts out of the volume proves nothing
        // either way.
        //---------------------------------------------------------------------
        site.Solid(new(-224f, 40f, 8f), new(-216f, 216f, 232f), Site.Wall);
        site.Solid(new(-64f, 40f, 8f), new(-56f, 216f, 232f), Site.Wall);
        site.Solid(new(-216f, 40f, 8f), new(-64f, 48f, 232f), Site.Wall);
        site.Solid(new(-216f, 208f, 8f), new(-64f, 216f, 232f), Site.Wall);

        site.Brush(EntityNames.TriggerVphysicsMotion, "well",
            new(-216f, 48f, 8f), new(-64f, 208f, 232f), Site.Trigger,
            "SetGravityScale", "0.12", "SetAdditionalAirDensity", "3",
            "SetVelocityLimit", "70", "SetVelocityLimitDelta", "0",
            "SetVelocityScale", "1", "SetAngVelocityLimit", "60",
            "SetAngVelocityScale", "1",
            "SetLinearForce", "0", "SetLinearForceAngles", "0 0 0");

        site.Ent(EntityNames.PropPhysics, "float", new(-140f, 128f, 212f), "model", Crate);

        site.Ent(EntityNames.PointTeleport, "hoist", new(-140f, 128f, 32f),
            "target", site.Name("float"), "spawnflags", "1");

        //---------------------------------------------------------------------
        // A gas cannister, which is three things bolted together: a breakable
        // with health, a thruster on a motion controller, and a bang. What makes
        // it a cannister rather than a barrel is that DAMAGE SETS IT OFF instead
        // of wearing it down -- a hit that does not kill it lights it, and a hit
        // on one already lit finishes it.
        //
        // Penned as well, and for a plainer reason: the thrust is an
        // ACCELERATION, 900 is half again the world's gravity, and it pushes
        // along whichever way the cannister happens to be lying. Without the
        // walls it would be furniture in somebody else's station within the
        // second.
        //
        // ONE-SHOT, and the station says so rather than pretending otherwise.
        // Douse zeroes the fuel -- stock's own behaviour, said here as a flag
        // rather than a zeroed keyvalue -- so a cannister that has burned can
        // never be lit again, by the button or by anything else. Shooting it is
        // the other ending and is just as final. This is the one station whose
        // reset button genuinely cannot put it back, and the alternative was to
        // leave the classname undemonstrated.
        //---------------------------------------------------------------------
        site.Solid(new(24f, 40f, 8f), new(32f, 216f, 232f), Site.Wall);
        site.Solid(new(216f, 40f, 8f), new(224f, 216f, 232f), Site.Wall);
        site.Solid(new(32f, 40f, 8f), new(216f, 48f, 232f), Site.Wall);
        site.Solid(new(32f, 208f, 8f), new(216f, 216f, 232f), Site.Wall);

        //
        // NINE HUNDRED, which is what a stock map would write, and five seconds
        // of it. The key is an ACCELERATION, so 900 is half again the world's
        // gravity and the cannister drives itself the length of the pen in about
        // a second.
        //
        // It used to say forty thousand, a number no stock map contains, and the
        // reason is worth keeping: the thrust was being pushed along the model's
        // +X -- across the barrel -- instead of down its own up axis, so a
        // cannister lying on its side spent the whole burn pressing itself into
        // the floor. At 900 it moved a tenth of a unit in five seconds; only a
        // forty-fold overdose bounced it around enough to look like thrust. The
        // axis is fixed, so the number goes back to one a mapper would write.
        site.Ent(EntityNames.PhysicsCannister, "rocket", new(124f, 128f, 40f),
            "model", "models/props_junk/propanecanister001a.mdl",
            "health", "25", "thrust", "900", "fuel", "5",
            "expdamage", "40", "expradius", "120");

        // A place, and nothing else -- the plainest entity in the mod, and it is
        // here because it is a marker the PHYSICS entities read.
        //
        // Stock's CEnableMotionFixup is an empty two-line CBaseEntity. Nothing
        // is asked of it and nothing is fired at it: a cannister or a
        // motion-enable input finds it BY NAME and takes its origin, which is
        // where a prop should be re-placed when its motion is switched on. So
        // there is no button to press and nothing to watch, and this placement
        // is the demonstration -- the entity exists, spawns, holds a position
        // and is findable, which is the whole of what it ever did.
        //
        // Beside the cannister deliberately: physics_cannister is the entity in
        // stock that reaches for one.
        site.Ent(EntityNames.PointEnableMotionFixup, "fixup", new(60f, 128f, 40f));

        //---------------------------------------------------------------------
        // Where a body should fall. DIE HERE.
        //
        // A mapper places one of these beside the ledge a dead guard is meant to
        // drop over: its pull is added to the death force before the ragdoll is
        // built, so the corpse goes over the edge rather than folding onto it.
        // There are no NPCs here, so the only body available is the player's own
        // -- and the player's works.
        //
        // This comment used to say it could not, on the grounds that HL2MP's
        // Event_Killed calls CreateRagdollEntity before chaining up to
        // CBaseCombatCharacter::Event_Killed, where the magnet is asked. True of
        // HL2MP and irrelevant here: hl2mp_player.cpp is not in this build at
        // all -- CHL2MP_Player is not a symbol in server.so -- and this mod's
        // player is a CBasePlayer, whose CreateRagdollEntity is an empty stub.
        // The death path is CBaseCombatCharacter::Event_Killed adding the
        // magnet's pull to forceVector and handing that same vector to
        // BecomeRagdoll and on to BecomeRagdollOnClient, which is the one thing
        // that ever writes the networked m_vecForce the client builds the
        // ragdoll from.
        //
        // Measured with ai_debug_ragdoll_magnets on, killing a player standing
        // at the marker: enabled, the magnet is asked and answers
        // (0, -796.3, -76.5), which points at it from where the body is;
        // disabled, it is asked and declines. See docs/divergences.md.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.PhysRagdollmagnet, "magnet", new(0f, -128f, 40f),
            "radius", "128", "force", "800", "axis", "0 0 0",
            "StartDisabled", "0", "spawnflags", "0");

        site.Marker("m1", new(-140f, 128f, 8f), "140 190 255");
        site.Marker("m2", new(124f, 128f, 8f), "255 140 140");
        site.Marker("m3", new(0f, -128f, 8f), "200 160 255");

        site.OnReset("hoist", "Teleport");
        site.OnReset("float", "Wake", delay: 0.1f);
        site.OnReset("magnet", "Enable");
        // Activate and nothing else. Deactivate is a real input and putting it
        // on a reset would be the worst possible use of it: fired at a burning
        // cannister it would spend the fuel and end the demonstration a second
        // after it started.
        site.OnReset("rocket", "Activate");
    }

    /// <summary>
    /// The entity that reaches into another entity's map data and changes what
    /// it POINTS AT, shown on both hierarchies at once.
    ///
    /// Two victims, because ai_changetarget's whole difficulty is that nobody is
    /// TOLD. It rewrites a keyvalue and walks away, and whether anything comes
    /// of that depends entirely on when the victim next bothers to re-read the
    /// key. The lamp re-reads on a think; the teleport re-reads when it is
    /// fired. Side by side those are the only two answers there are, and a
    /// mapper who has seen both knows what to expect of a third entity.
    ///
    /// <para>
    /// The lamp is a light_dynamic, which is stock C++ and the only live reader
    /// of a rewritten target left in this build — CDynamicLight::DynamicLightThink
    /// calls GetNextTarget() every think and aims at whatever comes back. The
    /// hoist is a managed point_teleport, whose target is a [ScriptVars] key and
    /// not a CBaseEntity field at all. One input reaches both because the
    /// retargeter goes through KeyValue rather than through a field, and that is
    /// the port's whole design decision made visible.
    /// </para>
    ///
    /// <para>
    /// A TENTH OF A SECOND, and the station text says so out loud rather than
    /// leaving it to be discovered. DynamicLightThink is scheduled on a 0.1s
    /// interval, so the input lands, the key really is rewritten, and a reading
    /// taken in the same frame still shows the old angles. That looks exactly
    /// like a port that does not work, and it is the single most likely thing
    /// about this entity to be reported as a bug.
    /// </para>
    /// </summary>
    private static void Retarget(Site site)
    {
        //---------------------------------------------------------------------
        // The C++ half: a spotlight aimed at one of two plates on the floor.
        //
        // A CONE is what makes this demonstrable at all. C_DynamicLight only
        // traces a beam and puts a lit pool where it lands when m_OuterAngle is
        // above zero; with no "_cone" key the entity is an omnidirectional
        // dlight whose angles nothing reads, so retargeting it would be correct,
        // networked, and completely invisible. "distance" has to clear the drop
        // to the floor for the same reason -- the trace is only m_Radius long.
        //
        // NAMED, which is safe here and is not for the lamps over the reset
        // buttons: vbsp's SetLightStyles skips light_dynamic by classname before
        // it counts a targetname against the thirty-two switched styles.
        //---------------------------------------------------------------------
        site.Ent("light_dynamic", "lamp", new(0f, 128f, 240f),
            "target", site.Name("nearpad"),
            "_light", "255 96 48 255",
            "brightness", "3",
            "distance", "480",
            "spotlight_radius", "112",
            "_inner_cone", "12",
            "_cone", "26",
            "style", "0",
            "spawnflags", "0");

        //---------------------------------------------------------------------
        // What the lamp aims at, and NOT an info_target, which is what this
        // station was built with first and why it showed nothing at all.
        //
        // CDynamicLight is native and resolves its target through
        // CBaseEntity::GetNextTarget, which is gEntList.FindEntityByName -- the
        // SDK half of the entity list and only that. info_target is managed in
        // this build, so it is not in that list, and a lamp aimed at one finds
        // nothing, returns early, and sits at its map angles forever. Measured:
        // the lamp read (0 0 0) with a managed info_target named in its target
        // key, and swung to (21.39 252.07) the moment the key was rewritten to
        // name a native entity in the same room. Nothing warns; see
        // docs/divergences.md.
        //
        // point_spotlight is native, is already the marker every other station
        // uses, and has an origin -- so it is both the thing the lamp aims at
        // and the thing that shows a player where. Pointed UP rather than down,
        // because the pool the lamp draws lands on this plate and two beams
        // crossing on the same square of floor reads as one bright mess.
        //---------------------------------------------------------------------
        site.Ent("point_spotlight", "nearpad", new(-176f, 128f, 12f),
            "angles", "-90 0 0",
            "SpotlightLength", "170", "SpotlightWidth", "12",
            "rendercolor", "255 220 140", "renderamt", "150",
            "HDRColorScale", "0.7", "IgnoreSolid", "1", "spawnflags", "3");

        site.Ent("point_spotlight", "farpad", new(176f, 128f, 12f),
            "angles", "-90 0 0",
            "SpotlightLength", "170", "SpotlightWidth", "12",
            "rendercolor", "255 220 140", "renderamt", "150",
            "HDRColorScale", "0.7", "IgnoreSolid", "1", "spawnflags", "3");

        site.Solid(new(-216f, 88f, 8f), new(-136f, 168f, 11f), Site.Accent);
        site.Solid(new(136f, 88f, 8f), new(216f, 168f, 11f), Site.Accent);

        //---------------------------------------------------------------------
        // The C# half: a teleport that lifts whichever crate it currently names.
        //
        // Two crates and ONE hoist rather than two hoists, because the thing
        // being demonstrated is that the same entity does something different
        // afterwards. The hoist re-resolves its target inside the Teleport
        // input, so the rewrite is not noticed until the next press of LIFT --
        // the opposite schedule to the lamp's, which is why both are here.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.PropPhysics, "near", new(-96f, -32f, 24f), "model", Crate);
        site.Ent(EntityNames.PropPhysics, "far", new(96f, -32f, 24f), "model", Crate);

        site.Ent(EntityNames.PointTeleport, "hoist", new(0f, -32f, 208f),
            "target", site.Name("near"));

        //---------------------------------------------------------------------
        // Four retargeters in two pairs, and four rather than two because one
        // ai_changetarget carries ONE new value. Its "target" picks the victims
        // by name and every victim is written the same string, so a swap that
        // has to say two different things needs two entities to say them.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.AiChangetarget, "aimfar", new(-64f, 32f, 16f),
            "target", site.Name("lamp"), "m_iszNewTarget", site.Name("farpad"));

        site.Ent(EntityNames.AiChangetarget, "aimnear", new(-32f, 32f, 16f),
            "target", site.Name("lamp"), "m_iszNewTarget", site.Name("nearpad"));

        site.Ent(EntityNames.AiChangetarget, "pickfar", new(32f, 32f, 16f),
            "target", site.Name("hoist"), "m_iszNewTarget", site.Name("far"));

        site.Ent(EntityNames.AiChangetarget, "picknear", new(64f, 32f, 16f),
            "target", site.Name("hoist"), "m_iszNewTarget", site.Name("near"));

        // Two buttons of its own, and the pair is the demonstration: SWAP shows
        // one half of what it did, and LIFT is what makes the other half
        // visible. One button doing both would show the result and hide the
        // mechanism.
        site.Brush(EntityNames.FuncButton, "swap",
            new(-104f, -160f, 8f), new(-72f, -128f, 56f), Site.Switch, "wait", "2")
            .Wire("OnPressed", site.Name("aimfar"), "Activate");

        site.Wire("swap", "OnPressed", site.Name("pickfar"), "Activate");

        site.Brush(EntityNames.FuncButton, "lift",
            new(72f, -160f, 8f), new(104f, -128f, 56f), Site.Switch, "wait", "2")
            .Wire("OnPressed", site.Name("hoist"), "Teleport");

        site.Marker("swapmark", new(-88f, -144f, 56f), "255 220 140", 72f);
        site.Marker("liftmark", new(88f, -144f, 56f), "140 190 255", 72f);

        //---------------------------------------------------------------------
        // Putting it back is itself a retarget, which is the tidiest thing about
        // this station: the reset does not restore a saved value, it fires the
        // OTHER two ai_changetargets. There is no second mechanism.
        //---------------------------------------------------------------------
        site.OnReset("aimnear", "Activate");
        site.OnReset("picknear", "Activate");

        // The crates go home through teleports of their own -- spawnflag 1, the
        // "put it back where the map drew it" reading -- because the hoist is
        // the entity under test and must not also be the entity that tidies up
        // after it.
        site.Ent(EntityNames.PointTeleport, "homenear", new(-96f, -32f, 24f),
            "target", site.Name("near"), "spawnflags", "1");

        site.Ent(EntityNames.PointTeleport, "homefar", new(96f, -32f, 24f),
            "target", site.Name("far"), "spawnflags", "1");

        site.OnReset("homenear", "Teleport");
        site.OnReset("homefar", "Teleport");
    }

    /// <summary>
    /// point_viewcontrol: the entity that takes a player's eyes.
    ///
    /// It gets a plot to itself for one reason -- a camera that goes wrong
    /// strands somebody -- and the flags are chosen around that. SF_InfiniteWait
    /// is not set, so "wait" is a hard stop that arrives whatever else happens:
    /// the think is armed because the aim target is the player and a player is
    /// always valid, and the wait check is the first thing in it. The station's
    /// reset relay fires Disable as well.
    ///
    /// <para>
    /// SF_TakeControl IS set, and it is not optional here. Without it the think
    /// multiplies the camera's velocity by 0.8 every tick and zeroes it below
    /// ten units a second, while Move only adds about a thirtieth of the leg's
    /// velocity per tick -- so the damping wins and the camera creeps. Measured
    /// before the flag went on: twenty-one units of travel in eight seconds at a
    /// map speed of 140. That is stock's own arithmetic, kept verbatim, and it
    /// means a camera that leaves the player in charge cannot fly a path at all.
    /// Since the path is the half of this class that did not work before the
    /// port, the flag is what makes the station worth having.
    /// </para>
    ///
    /// <para>
    /// SF_Interrupt is therefore NOT set, because with SF_TakeControl it could
    /// never fire. CBasePlayer::PlayerRunCommand zeroes ucmd->buttons outright
    /// while FL_FROZEN is on, so the camera samples nothing but zero and its
    /// "a button changed" test can never be true. Setting it would be a flag in
    /// the map that reads as an escape route and is not one. See
    /// docs/divergences.md.
    /// </para>
    ///
    /// <para>
    /// The flight is a real path_corner chain, and that is the part of the port
    /// that was actually broken before: stock holds its path node as a
    /// CBaseEntity* and path_corner is managed here, so CBaseEntity::Instance
    /// answered NULL and the native camera walked a chain it could never find. A
    /// camera sitting still would demonstrate the class and not the fix.
    /// </para>
    ///
    /// <para>
    /// SF_AimAtPlayer rather than a named target, because the one thing a player
    /// can check without being told what to look for is whether the camera is
    /// looking back at them. It also makes the aim target something that cannot
    /// vanish mid-flight -- a target going invalid is one of the four ways the
    /// camera ends, and it would end this one early.
    /// </para>
    ///
    /// <para>
    /// TWO THINGS IT DOES NOT DO, stated rather than left to be found: the
    /// VIEWMODEL stays drawn for the whole shot -- stock hides the world weapon
    /// and never touched the viewmodel, and that is kept -- and
    /// "targetattachment" is not implemented and warns if a map writes one. Both
    /// are in docs/divergences.md.
    /// </para>
    /// </summary>
    private static void Camera(Site site)
    {
        // The loop, a storey above head height so the shot looks down on the
        // plot. The camera starts ON the first corner: Enable calls Move with a
        // distance of zero, which passes the first node immediately and sets off
        // for the second, so "moveto" names where the flight BEGINS rather than
        // where it is first going.
        site.Ent(EntityNames.PathCorner, "p1", new(-192f, -192f, 208f),
            "target", site.Name("p2"));

        site.Ent(EntityNames.PathCorner, "p2", new(192f, -192f, 208f),
            "target", site.Name("p3"));

        // One corner with a pause and a speed of its own, so the flight is not a
        // constant circle: the camera decelerates into this corner, sits for a
        // second and accelerates out. Both keys belong to the corner being LEFT,
        // which is the path_corner rule the trains station is also built on.
        site.Ent(EntityNames.PathCorner, "p3", new(192f, 192f, 208f),
            "target", site.Name("p4"), "wait", "1", "speed", "90");

        site.Ent(EntityNames.PathCorner, "p4", new(-192f, 192f, 208f),
            "target", site.Name("p1"));

        // 2 = aim at the player, 4 = take control. See the note above for why
        // the second one is what makes the first one worth looking at.
        //
        // Drawn on the first corner rather than in the middle of the plot,
        // because the camera does not fly TO its first node -- it is already
        // there, and one drawn elsewhere would start with a jump.
        site.Ent(EntityNames.PointViewcontrol, "eye", new(-192f, -192f, 208f),
            "spawnflags", "6",
            "moveto", site.Name("p1"),
            // Ten seconds and a speed of 140 is one lap of a 384-unit square
            // with the corner pause in it, near enough. Long enough to see the
            // path work, short enough that being held still is not a wait.
            "wait", "10",
            "speed", "140",
            "acceleration", "300",
            "deceleration", "300");

        // Pressed rather than walked into, and that is a safety decision. A pad
        // would take the view from anybody who wandered across the plot, and
        // take it again every time its wait expired while they stood on it; a
        // button is asked for once, by somebody who has read the sign. It
        // matters more here than it would elsewhere, because the camera holds
        // the player still while it runs.
        site.Brush(EntityNames.FuncButton, "start",
            new(-16f, 96f, 8f), new(16f, 128f, 56f), Site.Switch, "wait", "12")
            .Wire("OnPressed", site.Name("eye"), "Enable");

        site.Marker("startmark", new(0f, 112f, 56f), "255 220 140", 72f);

        // The one output the class has, and the only thing in the map that can
        // tell "the camera let go" from "the camera is still running and happens
        // to be pointing at your own feet". Lit at rest, dark for the duration
        // of the shot, lit again by OnEndFollow -- which fires however the
        // camera ended, and not only when the wait ran out.
        site.Marker("held", new(-176f, 0f, 8f), "120 255 150");

        site.Wire("start", "OnPressed", site.Name("held"), "LightOff");
        site.Wire("eye", "OnEndFollow", site.Name("held"), "LightOn");

        // Something with a shape to fly around, so the flight reads as movement
        // rather than as a slow pan. Nothing is wired to it and nothing needs to
        // be; a camera orbiting an empty floor looks like a camera standing
        // still.
        site.Solid(new(-48f, -48f, 8f), new(48f, 48f, 120f), Site.Metal);

        // The rescue, and the reason this plot's reset button matters more than
        // the other thirty-six: a player who still has the camera's view when
        // they reach the pedestal presses USE and gets their eyes back.
        site.OnReset("eye", "Disable");
        site.OnReset("held", "LightOn");
    }

    /// <summary>
    /// The three volumes this build can place and cannot demonstrate, together,
    /// with the plot saying so.
    ///
    /// They are here rather than in the coverage exception list because each is
    /// a legitimate thing for a map to contain, and all three load, spawn and
    /// hold their shape. What none of them can do is show a player the behaviour
    /// they exist for, and for three different reasons -- so the station
    /// promises the SHAPE and names the reason, which is the honest version of a
    /// worked example when there is nothing to work.
    ///
    /// <para>
    /// trigger_cdaudio's live effect really is that it deletes itself. It stuffs
    /// "cd play N" at the player who set it off, and this engine's command table
    /// -- 662 entries, counted on the running game -- has no "cd" in it: the
    /// command went out after Half-Life 1, and an unknown stuffed command is
    /// discarded without a word. The entity is correct; the thing on the other
    /// end of it left.
    /// </para>
    ///
    /// <para>
    /// trigger_serverragdoll sets m_bForceServerRagdoll on CBaseCombatCharacter,
    /// and nothing in this build can be its subject. ai_basenpc.cpp is not
    /// compiled, so CAI_BaseNPC does not exist; the player is the only
    /// CBaseCombatCharacter left and this volume excludes players on purpose;
    /// every prop that can walk into it is on the standalone hierarchy and is
    /// not a CBaseEntity at all. The TOUCHING half works and is what the lamp
    /// shows -- a player and a prop_physics both produce OnStartTouch -- so what
    /// is unobservable is precisely the flag and nothing else.
    /// </para>
    ///
    /// <para>
    /// trigger_transition does nothing here and did nothing before the port
    /// either. Both its readers are the save/restore transition path, and
    /// CChangeLevel::ChangeLevelNow opens with "Don't work in deathmatch" and
    /// returns before it reaches the volume test. It is a named volume and a
    /// shape, in C# exactly as it was in C++.
    /// </para>
    /// </summary>
    private static void Inert(Site site)
    {
        //---------------------------------------------------------------------
        // Left: the one that removes itself.
        //
        // NO RESET WIRE, deliberately. It is gone after the first player walks
        // in and the station cannot bring it back -- a brush entity has no
        // template that could respawn it -- so a reset firing Enable at a name
        // with nothing behind it would be a wire that looks like a promise. The
        // soda can is left out of coverage for the same shape of reason; this
        // one is placeable, so it is placed.
        //---------------------------------------------------------------------
        site.Brush(EntityNames.TriggerCdaudio, "cdaudio",
            new(-208f, 16f, 8f), new(-112f, 144f, 104f), Site.Trigger,
            // The track, and the key really is "health": stock reads m_iHealth
            // because in 1998 an entity had a handful of general-purpose fields
            // and a map used whichever one was free. Hammer still shows it as
            // Health. Three is inside the -1..30 range the class checks.
            "health", "3");

        site.Ent("game_text", "gone", new(-160f, -16f, 32f),
            "message", "The cdaudio volume fired and removed itself. No track played.",
            "channel", "5", "x", "0.03", "y", "0.78",
            "color", "255 160 140 255", "holdtime", "5",
            "fadein", "0.1", "fadeout", "0.5", "effect", "0");

        // The output arrives BEFORE the removal, which is what makes this line
        // true rather than hopeful: TriggerVolume::OnTouch fires OnStartTouch
        // and only then calls Fire, and Fire is where the removal happens. So
        // the wire does reach the text, once, from an entity that is about to
        // stop existing.
        site.Wire("cdaudio", "OnStartTouch", site.Name("gone"), "Display");

        site.Marker("cdmark", new(-160f, 80f, 8f), "255 140 140");

        //---------------------------------------------------------------------
        // Middle: the one that notices you and can mark nothing.
        //
        // The lamp is wired to the touch outputs rather than to anything about
        // ragdolls, because touching is the half that works. A crate is parked
        // beside it so the OTHER kind of toucher can be tried: it lights the
        // lamp exactly as a player does, and is exactly as impossible a subject
        // for the flag.
        //---------------------------------------------------------------------
        site.Brush(EntityNames.TriggerServerragdoll, "ragdoll",
            new(-48f, 16f, 8f), new(48f, 144f, 104f), Site.Trigger);

        // Spawnflag 1 is "start on"; 2 is the no-dynamic-light bit the marker
        // always carries. So this one starts dark, and lighting it means
        // something.
        site.Marker("ragmark", new(0f, 80f, 8f), "255 220 120").Set("spawnflags", "2");

        site.Wire("ragdoll", "OnStartTouch", site.Name("ragmark"), "LightOn");
        site.Wire("ragdoll", "OnEndTouchAll", site.Name("ragmark"), "LightOff");

        site.Ent(EntityNames.PropPhysics, "body", new(0f, -96f, 24f), "model", Crate);

        site.Ent(EntityNames.PointTeleport, "homebody", new(0f, -96f, 24f),
            "target", site.Name("body"), "spawnflags", "1");

        //---------------------------------------------------------------------
        // Right: the one nothing asks about.
        //
        // A landmark beside it, because that pairing is what the entity is FOR:
        // stock's convention is that the volume carries the SAME targetname as
        // the info_landmark it belongs to, so InTransitionVolume can find it by
        // that one name. This map cannot spell that -- every wire here is by
        // name and the generator refuses two entities sharing one -- and it
        // costs nothing, because the code that would look the name up is the
        // code that returns before it looks.
        //
        // Nothing to wire in either direction. It is not a CBaseTrigger in stock
        // and is not a TriggerVolume here: it derives from a point entity, takes
        // a brush model, and offers a mapper no inputs and no outputs at all.
        //---------------------------------------------------------------------
        site.Brush(EntityNames.TriggerTransition, "transition",
            new(112f, 16f, 8f), new(208f, 144f, 104f), Site.Trigger);

        site.Ent(EntityNames.InfoLandmark, "landmark", new(160f, -16f, 16f));

        site.Marker("transmark", new(160f, 80f, 8f), "140 190 255");

        site.OnReset("ragdoll", "Enable");
        site.OnReset("ragmark", "LightOff");
        site.OnReset("homebody", "Teleport");
    }

    //=========================================================================
    // Sound.
    //
    // One station rather than several, because none of it can be seen. The only
    // way to tell any two of these apart is to stand somewhere else and listen,
    // so they have to be within walking distance of each other for the
    // difference to be audible at all -- which is also why the row runs left to
    // right across the plot rather than being scattered around it.
    //=========================================================================

    /// <summary>
    /// The three soundscape classes, the volume that hands the third one out,
    /// and the four other ways a map makes a noise or measures one.
    /// </summary>
    private static void Soundscapes(Site site)
    {
        //---------------------------------------------------------------------
        // Left: the ordinary soundscape, chosen by being near it.
        //
        // "labs_mp" is one of the four soundscape scripts this mod ships, and it
        // is the one picked because three of its loops carry "position" "0" --
        // so the position0 key below actually moves a sound. A soundscape with
        // no positioned sound ignores every one of the eight keys silently.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.InfoTarget, "vent", new(-176f, 176f, 96f));

        site.Ent(EntityNames.EnvSoundscape, "near", new(-176f, 96f, 40f),
            "soundscape", "labs_mp", "radius", "300",
            "position0", site.Name("vent"));

        //---------------------------------------------------------------------
        // Middle: the proxy, which is the same soundscape in a second place.
        //
        // It names the one on the left and borrows its index and its eight
        // positions, keeping only its own origin and radius. What that buys is
        // the thing worth demonstrating: the client restarts a soundscape when
        // the INDEX changes, so walking from the left pad to this one does not
        // restart anything -- and walking to the right pad does.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvSoundscapeProxy, "proxy", new(-48f, 96f, 40f),
            "radius", "300", "MainSoundscapeName", site.Name("near"));

        //---------------------------------------------------------------------
        // Right: the triggerable, which the automatic pass never picks.
        //
        // No radius on purpose. Stock excludes a triggerable by accident -- its
        // radius defaults to zero so the range test never passes -- and this
        // mod excludes it by saying so, so a radius here would be a number
        // nothing reads. The volume is what hands it out, and its "soundscape"
        // key names the ENTITY rather than the script.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvSoundscapeTriggerable, "inside", new(176f, 96f, 40f),
            "soundscape", "under_tunnels");

        site.Brush(EntityNames.TriggerSoundscape, "box",
            new(112f, 32f, 8f), new(240f, 160f, 136f), Site.Trigger,
            "soundscape", site.Name("inside"), "spawnflags", "1");

        site.Marker("nearmark", new(-176f, 96f, 8f), "140 190 255");
        site.Marker("proxymark", new(-48f, 96f, 8f), "140 255 190");
        site.Marker("insidemark", new(176f, 96f, 8f), "255 220 120");

        //---------------------------------------------------------------------
        // A machine that hums, and a meter reading it.
        //
        // ambient_generic is an Effect rather than a logical entity, and that is
        // not cosmetic: it is transmitted to nearby clients, which a logical
        // entity could not be because a logical entity has no entity index to
        // transmit. "message" is the sound and "health" is the volume, both of
        // them names from 1998 that Hammer still shows.
        //
        // The radius is what the sound level is computed FROM -- 40dB at the
        // reference distance falling 20dB a decade -- so a small number here is
        // a sound you have to walk up to, which is the only way to hear that it
        // has a position at all.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.PropDynamicOverride, "machine", new(-176f, -96f, 8f),
            "model", Drum, "solid", "6");

        site.Ent(EntityNames.AmbientGeneric, "hum", new(-176f, -96f, 48f),
            "message", "ambient/machines/lab_loop1.wav",
            "health", "8", "radius", "400", "pitch", "100", "spawnflags", "0");

        // The microphone MEASURES: it fires SoundLevel with a number every
        // sample and does not re-play anything, which is the one thing its name
        // suggests and it does not do. Sound bits 1|2|4 are combat, world and
        // player noise -- the three kinds anything in this map makes.
        site.Ent(EntityNames.EnvMicrophone, "meter", new(-48f, -96f, 48f),
            "spawnflags", "7", "Sensitivity", "2", "SmoothFactor", "0.3",
            "MaxRange", "600");

        // Somewhere for the reading to land. A math_counter is the only thing in
        // the map that takes a number and can be read back out of a running
        // game, which is what makes the measurement checkable.
        site.Ent(EntityNames.MathCounter, "reading", new(-48f, -136f, 48f),
            "StartValue", "0", "Min", "0", "Max", "1");

        site.Wire("meter", "SoundLevel", site.Name("reading"), "SetValue");

        //---------------------------------------------------------------------
        // The two the button sets off.
        //
        // ai_sound enters a noise into the world's record of noises -- the thing
        // an NPC would hear -- rather than playing anything. This mod has no
        // NPCs, so what it is FOR here is giving the microphone above something
        // to read that nobody had to fire a gun to make. Its location proxy is
        // the info_target by the vent, so the noise is credited to a place the
        // entity is not.
        //
        // target_cdaudio needs a PLAYER as the activator and answers nobody
        // else, so it hangs off the reset relay: the button passes whoever
        // pressed it all the way through.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.AiSound, "noise", new(48f, -96f, 48f),
            "soundtype", "2", "soundcontext", "0", "volume", "600",
            "duration", "0.5", "locationproxy", site.Name("vent"));

        site.Ent(EntityNames.TargetCdaudio, "track", new(176f, -96f, 48f),
            "health", "5");

        site.Marker("noisemark", new(48f, -96f, 8f), "255 160 140");

        site.OnReset("noise", "EmitAISound");
        site.OnReset("track", "ChangeCDTrack");
        site.OnReset("reading", "SetValueNoFire", "0");
        site.OnReset("hum", "PlaySound");
    }

    /// <summary>
    /// The three entities that say a sentence to a player, none of which is a
    /// game_text.
    ///
    /// <para>
    /// All three reach exactly one player -- whoever set them off -- unless the
    /// All Players flag is set, and in this mod that is not a limitation of the
    /// port: stock falls back to <c>UTIL_GetLocalPlayer</c>, which is NULL on
    /// any server with more than one slot. So a stock env_message fired by a
    /// timer reaches nobody here too. The difference is that these say so.
    /// </para>
    /// </summary>
    private static void Notices(Site site)
    {
        //---------------------------------------------------------------------
        // A titles.txt entry, which is what env_message sends: a NAME, looked
        // up in the engine's own copy of the file on the client. CHAPTER1_TITLE
        // is one HL2 ships, and it resolves to a token the localisation tables
        // then finish -- which is the whole of what makes this different from a
        // game_text, and the reason it is worth having both.
        //
        // The sound goes out on the ambient channel with the volume and
        // attenuation the map keyed, rather than through a soundscript, because
        // those two keys are the only reason the entity has a position.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvMessage, "titlemsg", new(-160f, 96f, 40f),
            "message", "CHAPTER1_TITLE",
            "messagesound", "ambient/machines/lab_loop1.wav",
            "messagevolume", "6", "messageattenuation", "1",
            "spawnflags", "0");

        //---------------------------------------------------------------------
        // The key hint, which is the engine's KeyHintText message and had no
        // reader in this mod at all until the HUD moved to C#. The text is a
        // list on the wire and stock always sends one entry; so does this.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvHudhint, "keyhint", new(0f, 96f, 40f),
            "message", "This hint arrived as KeyHintText, not as a game_text.",
            "spawnflags", "0");

        //---------------------------------------------------------------------
        // point_message is the one that needs no wire: it draws itself, in the
        // world, to anybody standing inside its radius.
        //
        // In stock it drew to NOBODY in a game with more than one slot --
        // CMessageEntity::Think measures from UTIL_GetLocalPlayer and returns at
        // its first line -- so this is the entity that a multiplayer mod has
        // never been able to use. It measures against the nearest player here,
        // which is the same rule in a game that has more than one of them.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.PointMessage, "sign", new(160f, 96f, 56f),
            "message", "point_message: only readable from close up.",
            "radius", "220", "spawnflags", "0");

        //---------------------------------------------------------------------
        // point_worldtext is the fourth, and it is the odd one out twice over:
        // it is the only one of the four that is not addressed to a player at
        // all -- it is text hanging in the world that everybody sees -- and it
        // is the only one whose whole client half is a mesh builder.
        //
        // THREE of them, because the entity's one real decision is the
        // ORIENTATION and there is no way to see what it does with one. 0 draws
        // along the entity's own angles and is therefore the only one that can
        // be read from behind (it cannot -- it is mirrored, which is the point);
        // 1 turns the whole line to face the viewer; 2 turns it to face the
        // viewer but keeps it upright, which is the difference you only see by
        // standing above or below it. They are in a row facing the walkway so
        // the three can be compared by walking past once.
        //
        // The rainbow flag is on the middle one, because it is the field that
        // proves the client is animating rather than redrawing what it was
        // sent: nothing on the wire changes while the hue cycles.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.PointWorldtext, "worldtext", new(-160f, -96f, 56f),
            "message", "orientation 0\nfixed to my angles",
            "angles", "0 90 0",
            "textsize", "8", "orientation", "0",
            "color", "255 220 120 255", "font", "0");

        site.Ent(EntityNames.PointWorldtext, "facingtext", new(0f, -96f, 56f),
            "message", "orientation 1: I follow you",
            "textsize", "8", "orientation", "1",
            "color", "255 255 255 255", "rainbow", "1", "font", "0");

        site.Ent(EntityNames.PointWorldtext, "uprighttext", new(160f, -96f, 56f),
            "message", "orientation 2\nfacing, but upright",
            "textsize", "8", "orientation", "2", "textspacingY", "4",
            "color", "140 255 190 255", "font", "0");

        site.Marker("msgmark", new(-160f, 96f, 8f), "255 220 120");
        site.Marker("keymark", new(0f, 96f, 8f), "140 190 255");
        site.Marker("signmark", new(160f, 96f, 8f), "140 255 190");
        site.Marker("textmark", new(-160f, -96f, 8f), "255 220 120");
        site.Marker("facemark", new(0f, -96f, 8f), "255 255 255");
        site.Marker("uprightmark", new(160f, -96f, 8f), "140 255 190");

        site.OnReset("titlemsg", "ShowMessage");
        site.OnReset("keyhint", "ShowHudHint");
        site.OnReset("sign", "Enable");

        // The button drives the text as well, and it drives the two inputs
        // worth seeing move: a SetText that has to reach the client as a string
        // and re-measure the glyphs there, and a SetColor that has to survive
        // being packed into one 32-bit number and unpacked on the far side.
        site.OnReset("worldtext", "SetText", "the button set this text");
        site.OnReset("worldtext", "SetColor", "120 200 255 255");
        site.OnReset("facingtext", "SetTextSize", "12");
        site.OnReset("uprighttext", "SetFont", "1");
    }

    //=========================================================================
    // Visibility: six classes that exist to make the renderer do LESS.
    //
    // The reason they waited for a batch pass is that five of the six are
    // invisible by construction and the sixth is a pane of glass, so there is
    // nothing to look at unless the map builds them something to act on.
    //
    // The closet is that something, and it is a REAL sealed room -- eleven
    // world brushes -- with two holes in it. That is what makes the two area
    // portals portals rather than decoration: vbsp floods the map from outside,
    // finds an areaportal brush separating two volumes of space, and cuts an
    // area boundary there. A portal brush that separates nothing compiles to
    // "areaportal brush doesn't touch two areas" and a portal number that shuts
    // nothing -- a placement that reads as coverage and is not, which is
    // exactly what this pass exists to avoid.
    //=========================================================================

    /// <summary>
    /// A closet with a door and a window, and the five slabs that occlude,
    /// fade, clip vehicles, clip physics and reflect.
    /// </summary>
    private static void Visibility(Site site)
    {
        //---------------------------------------------------------------------
        // The closet. Interior x -224..-48, y 40..216, floor to 160; walls 8
        // thick, floor borrowed from the station pad.
        //
        // The door is in the +x wall so it faces the middle of the plot, and
        // the window is in the -y wall so it faces the way a player walks in.
        // Both openings are cut out of the wall by writing the wall as the
        // pieces AROUND the hole -- the compiler has no subtraction, and a
        // brush that overlapped the portal would have vbsp cut the portal at
        // the overlap instead, which looks exactly like a portal that never
        // shuts.
        //---------------------------------------------------------------------
        site.Solid(new(-232f, 32f, 8f), new(-224f, 224f, 168f), Site.Wall);    // -x
        site.Solid(new(-232f, 216f, 8f), new(-40f, 224f, 168f), Site.Wall);    // +y
        site.Solid(new(-232f, 32f, 160f), new(-40f, 224f, 168f), Site.Wall);   // lid

        // +x wall, around the doorway at y 104..176, z 8..128.
        site.Solid(new(-48f, 32f, 8f), new(-40f, 104f, 168f), Site.Wall);
        site.Solid(new(-48f, 176f, 8f), new(-40f, 224f, 168f), Site.Wall);
        site.Solid(new(-48f, 104f, 128f), new(-40f, 176f, 168f), Site.Wall);

        // -y wall, around the window at x -160..-80, z 48..120.
        site.Solid(new(-232f, 32f, 8f), new(-160f, 40f, 168f), Site.Wall);
        site.Solid(new(-80f, 32f, 8f), new(-40f, 40f, 168f), Site.Wall);
        site.Solid(new(-160f, 32f, 8f), new(-80f, 40f, 48f), Site.Wall);
        site.Solid(new(-160f, 32f, 120f), new(-80f, 40f, 168f), Site.Wall);

        //---------------------------------------------------------------------
        // The door, which is a number rather than a thing.
        //
        // func_areaportal is the one Logical entity in this station and that is
        // not a detail: vbsp MOVES its brush into the world and writes the
        // hole's number back as "portalnumber", so what reaches the game is a
        // number and a switch with no geometry at all. It answers entindex()
        // with -1 for the same reason, which is why `debug-api ents
        // func_areaportal` finds nothing and `debug-api instances` finds it.
        //---------------------------------------------------------------------
        site.Brush(EntityNames.FuncAreaportal, "door",
            new(-48f, 104f, 8f), new(-40f, 176f, 128f), AreaPortal,
            "StartOpen", "1");

        //---------------------------------------------------------------------
        // The window, which is two entities.
        //
        // func_areaportalwindow is a portal that FADES rather than one that
        // shuts: inside FadeStartDist you see through it, past FadeDist you see
        // the background instead. Its own brush goes into the world exactly as
        // the door's does, so it has no surface -- the "target" key names a
        // second brush entity and THAT is the glass you look at.
        //
        // vbsp reads the same key: it strips CONTENTS_SOLID off the named brush
        // and marks it a window, which is what stops the pane sealing the hole
        // its own portal just made. So the two overlap on purpose.
        //---------------------------------------------------------------------
        site.Brush(EntityNames.FuncAreaportalwindow, "window",
            new(-160f, 32f, 48f), new(-80f, 40f, 120f), AreaPortal,
            "target", site.Name("pane"),
            "FadeStartDist", "200", "FadeDist", "700", "TranslucencyLimit", "0.2");

        site.Brush(EntityNames.FuncBrush, "pane",
            new(-160f, 34f, 48f), new(-80f, 38f, 120f), Site.Glass,
            "Solidity", "1", "rendermode", "9", "renderamt", "170");

        // A crate inside, so there is something for the portals to be hiding.
        site.Ent(EntityNames.PropDynamicOverride, "shut", new(-136f, 128f, 8f),
            "model", Crate, "solid", "6");

        site.Marker("doormark", new(-8f, 140f, 8f), "255 220 120");
        site.Marker("windowmark", new(-120f, -16f, 8f), "140 190 255");

        //---------------------------------------------------------------------
        // The occluder: a wall the renderer is told to believe things hide
        // behind.
        //
        // Textured all over rather than on one face, which is what Hammer
        // produces and what vbsp wants: it clips the brush against the world
        // and emits an occluder polygon per surviving face, then writes the
        // index back as "occludernumber". The CLIENT is the half that reads it
        // -- occlusion is a rendering decision the server never makes -- so
        // this is one of the sixteen classes whose receive table had never
        // decoded anything from this map.
        //---------------------------------------------------------------------
        site.Brush(EntityNames.FuncOccluder, "blocker",
            new(16f, 128f, 8f), new(144f, 136f, 136f), Occluder,
            "StartActive", "1");

        site.Ent(EntityNames.PropDynamicOverride, "hidden", new(80f, 184f, 8f),
            "model", Crate, "solid", "6");

        site.Marker("blockmark", new(80f, 96f, 8f), "255 160 140");

        //---------------------------------------------------------------------
        // The one that fades out with distance, and the one nothing here can
        // notice.
        //
        // func_lod disappears past DisappearDist, measured to the middle of its
        // own render bounds -- stock measures to the collision centre, which for
        // a brush is the same box. 400 on purpose: the fade is a thing that
        // happens far away, so a large number would put it outside the hall.
        //
        // func_vehicleclip is solid to vehicles and to nothing else, and this
        // mod has no vehicles, so it is a slab you walk straight through. The
        // pair stands side by side for exactly that reason: two brushes that
        // are not there, for two completely different reasons.
        //---------------------------------------------------------------------
        site.Brush(EntityNames.FuncLod, "fading",
            new(-16f, -176f, 8f), new(48f, -48f, 136f), Site.Accent,
            "DisappearDist", "400", "Solid", "0");

        site.Brush(EntityNames.FuncVehicleclip, "carwall",
            new(80f, -176f, 8f), new(144f, -48f, 136f), Site.Wall);

        //---------------------------------------------------------------------
        // The third brush that is not there, and the only one you can SEE not
        // being there.
        //
        // func_clip_vphysics stops physics objects and nothing else: a player
        // walks through it, a bullet goes through it, a trace does not find it.
        // So it is demonstrated by what rests ON it -- the crate below falls
        // from above the slab and stops in mid-air at its top face, standing on
        // a brush that is not in the trace world at all. Walk through the crate's
        // support and then try to take the crate off it.
        //
        // Set apart from the -y row on purpose: the other two are slabs you walk
        // through and notice nothing, and this one has to be seen holding
        // something up or it demonstrates the same nothing they do.
        //---------------------------------------------------------------------
        site.Brush(EntityNames.FuncClipVphysics, "physwall",
            new(-224f, -176f, 8f), new(-160f, -48f, 136f), Site.Wall);

        site.Ent(EntityNames.PropPhysics, "perched", new(-192f, -112f, 160f),
            "model", Crate);

        //---------------------------------------------------------------------
        // The mirror.
        //
        // Switched off is switched INVISIBLE, and that is the implementation
        // rather than a coincidence: the client's reflection sweep skips a pane
        // with no model, and a hidden entity on the parallel hierarchy has none.
        // So a disabled mirror costs no reflection pass, which is what a mapper
        // switching one off is asking for.
        //---------------------------------------------------------------------
        site.Brush(EntityNames.FuncReflectiveGlass, "mirror",
            new(176f, -176f, 8f), new(240f, -48f, 136f), Site.Glass,
            "Solidity", "1", "StartDisabled", "0");

        site.Marker("mirrormark", new(208f, -16f, 8f), "140 255 190");

        site.OnReset("door", "Open");
        site.OnReset("mirror", "Enable");
        site.OnReset("blocker", "Activate");
        site.OnReset("carwall", "Enable");
    }

    //=========================================================================
    // Emitting volumes.
    //
    // Every one of these is a brush that draws nothing and collides with
    // nothing; the brush is the SHAPE the effect comes out of, and the effect
    // is drawn entirely on the client. That is the whole reason this station
    // could not exist before a map-placed brush did: `ent_create` makes a point
    // entity with no brush model, so five of these seven had no bounds to emit
    // across and emitted nothing, however correct the code was.
    //
    // All seven are SOLID_BSP underneath with FSOLID_NOT_SOLID on top, which is
    // not a contradiction: the client asks the brush "is this point inside you"
    // for every place it considers putting a particle, and a collideable with
    // no shape answers no every time.
    //=========================================================================

    /// <summary>
    /// Rain, two kinds of dust, smoke, embers and the two steam jets.
    /// </summary>
    private static void Weather(Site site)
    {
        //---------------------------------------------------------------------
        // Front row: rain, and the two dust volumes.
        //
        // func_dustmotes and func_dustcloud are ONE class with two names, and
        // the whole difference between them is a flag: motes scale with
        // distance and a cloud does not. They stand next to each other so that
        // difference is the only thing changing.
        //
        // Both are SolidVolume where the rain beside them is Invisible, and the
        // difference is not cosmetic: a dust volume finds its own motes by
        // point-testing its brush for CONTENTS_SOLID, which a TOOLSINVISIBLE
        // brush never has. See the SolidVolume declaration.
        //---------------------------------------------------------------------
        site.Brush(EntityNames.FuncPrecipitation, "rain",
            new(-232f, 96f, 16f), new(-136f, 224f, 240f), Invisible,
            // 0 rain, 1 snow, 2 ash, 3 snowfall. renderamt is the density.
            "preciptype", "0", "renderamt", "160");

        // The map's weather system proper: one env_wind, invisible and
        // global, whose gusts the rope station's cables and any tree sprites
        // sway to. The numbers are Hammer's FGD defaults — the entity's own
        // defaults are all zero, which is a wind that never blows.
        site.Ent(EntityNames.EnvWind, "wind", new(-184f, 160f, 260f),
            "angles", "0 45 0",
            "minwind", "20", "maxwind", "50",
            "mingust", "100", "maxgust", "250",
            "mingustdelay", "10", "maxgustdelay", "20",
            "gustdirchange", "20", "gustduration", "5");

        site.Brush(EntityNames.FuncDustmotes, "motes",
            new(-104f, 96f, 16f), new(-8f, 224f, 200f), SolidVolume,
            "Color", "255 235 200", "Alpha", "160", "SpawnRate", "60",
            "SizeMin", "10", "SizeMax", "20", "SpeedMax", "12",
            "LifetimeMin", "2", "LifetimeMax", "5", "DistMax", "800",
            "FallSpeed", "1");

        site.Brush(EntityNames.FuncDustcloud, "cloud",
            new(24f, 96f, 16f), new(120f, 224f, 200f), SolidVolume,
            "Color", "200 210 255", "Alpha", "120", "SpawnRate", "50",
            "SizeMin", "40", "SizeMax", "70", "SpeedMax", "8",
            "LifetimeMin", "3", "LifetimeMax", "6", "DistMax", "800",
            "FallSpeed", "0");

        //---------------------------------------------------------------------
        // Smoke, which is the one with a material of its own.
        //
        // Density is what a map drives to fade smoke in and out, and the ramp
        // speed is what stops that being a snap: the client eases toward
        // whatever it last heard. Both are wired to the button below.
        //---------------------------------------------------------------------
        site.Brush(EntityNames.FuncSmokevolume, "smoke",
            new(152f, 96f, 16f), new(248f, 224f, 200f), Invisible,
            "Color1", "60 60 70", "Color2", "120 120 130",
            "material", "particle/particle_smokegrenade",
            "ParticleDrawWidth", "120", "ParticleSpacingDistance", "80",
            "DensityRampSpeed", "0.6", "RotationSpeed", "12",
            "MovementSpeed", "12", "Density", "1");

        //---------------------------------------------------------------------
        // Back row: embers, and the two steam jets.
        //
        // The embers volume is NAMED, and that is what makes it wait. Stock's
        // rule is that an UNNAMED env_embers starts on whatever its flags say,
        // because nothing could ever switch it on; only a named one waits for a
        // Use. The Toggle flag is set as well, because without it the first
        // press is the last -- stock clears its own use function after one.
        //---------------------------------------------------------------------
        site.Brush(EntityNames.EnvEmbers, "embers",
            new(-232f, -224f, 16f), new(-136f, -96f, 200f), Invisible,
            "Density", "50", "Lifetime", "4", "Speed", "32",
            // 1 start on, 2 answer more than one Use.
            "spawnflags", "3");

        //---------------------------------------------------------------------
        // env_steam and env_steamjet are one class with two names too, and the
        // whole difference is which way the jet points: the legacy classname
        // faces LEFT where the modern one faces forward. Same keys, same
        // angles, so the two plumes coming out at right angles to each other is
        // the entire demonstration.
        //
        // Neither answers Use. Stock's CSteamJet::Use handles USE_ON and
        // USE_OFF only, has no default case, and refuses a player outright --
        // so both ways in were closed in stock as well. TurnOn/TurnOff/Toggle
        // are how a map has always switched one, and they are what the button
        // uses.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvSteam, "jet", new(-32f, -160f, 24f),
            "angles", "-90 0 0",
            "SpreadSpeed", "15", "Speed", "120", "StartSize", "10",
            "EndSize", "60", "Rate", "26", "JetLength", "160",
            "InitialState", "1", "type", "0", "rendercolor", "255 255 255");

        site.Ent(EntityNames.EnvSteamjet, "legacyjet", new(128f, -160f, 24f),
            "angles", "-90 0 0",
            "SpreadSpeed", "15", "Speed", "120", "StartSize", "10",
            "EndSize", "60", "Rate", "26", "JetLength", "160",
            "InitialState", "1", "type", "0", "rendercolor", "255 190 120");

        site.Marker("jetmark", new(-32f, -160f, 8f), "255 255 255", 16f);
        site.Marker("legacymark", new(128f, -160f, 8f), "255 190 120", 16f);

        site.OnReset("embers", "Use");
        site.OnReset("jet", "Toggle");
        site.OnReset("legacyjet", "Toggle");
        site.OnReset("smoke", "SetDensity", "1");
        site.OnReset("motes", "TurnOn");
        site.OnReset("cloud", "TurnOn");
    }

    /// <summary>
    /// A pool of real water, the bubbles that come out of it, and the two
    /// classes that only mean anything where there is some.
    ///
    /// <para>
    /// The water is a world brush rather than an entity: a face carrying
    /// %compilewater becomes a volume vbsp lets you swim in, and nothing has to
    /// own it for that to happen. The cheap variant, so the pool needs no
    /// reflection buffer -- which is a decision the water_lod_control beside it
    /// is precisely about.
    /// </para>
    /// </summary>
    /// <summary>
    /// The six classes in <c>smoke_trail.cpp</c>.
    ///
    /// <para>
    /// Every one of them is a POINT entity that draws a plume, and every one
    /// is drawn from a managed client entity's own sim (ClientTrails.cs, through
    /// the native particle executor). They stand together because
    /// they came across together and because the interesting thing about them
    /// is shared: a trail is by definition something that FOLLOWS, and the
    /// position it is drawn at is swept in PreRender rather than delivered on
    /// a data change, because a managed entity's origin is a real prop outside
    /// the blob and moving one changes no blob at all.
    /// </para>
    /// </summary>
    private static void Trails(Site site)
    {
        //---------------------------------------------------------------------
        // env_fire_trail: the cheapest of the six. DT_FireTrail is two props
        // and neither is reachable from a map, so this instance carries no
        // keys at all -- every size, speed and colour a flame trail uses is a
        // #define in C_FireTrail::Update. It is here for exactly that reason: a
        // station entry with no keys is the honest picture of the entity.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvFireTrail, "firetrail", new(-232f, 96f, 32f));

        site.Marker("firetrailmark", new(-232f, 96f, 8f), "255 160 60", 16f);

        //---------------------------------------------------------------------
        // env_smoketrail: the head of the six and the widest. Every key here is
        // stock's own constructor default written out, which is the point --
        // thirteen of its fifteen fields are set in SmokeTrail::SmokeTrail and
        // not in Spawn, so an instance that says nothing is what a mapper
        // actually gets. This one says everything, so the two can be compared.
        //
        // The colours are in the mapper's 0-255; the entity divides once,
        // because the client reads them in 0-1 space.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvSmoketrail, "smoketrail", new(-104f, 96f, 32f),
            "spawnrate", "10", "opacity", "0.5", "lifetime", "5",
            "startcolor", "180 180 190", "endcolor", "20 20 30",
            "minspeed", "2", "maxspeed", "4",
            "mindirectedspeed", "0", "maxdirectedspeed", "40",
            "startsize", "16", "endsize", "40", "spawnradius", "4");

        site.Marker("smoketrailmark", new(-104f, 96f, 8f), "180 180 190", 16f);

        //---------------------------------------------------------------------
        // env_dusttrail: the same entity with one colour instead of two. It
        // stands next to the smoke on purpose -- the keys are identical apart
        // from "color" replacing "startcolor"/"endcolor", so what changes
        // between the two plumes is exactly what the classes differ by.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvDusttrail, "dusttrail", new(24f, 96f, 32f),
            "spawnrate", "10", "opacity", "0.5", "lifetime", "5",
            "color", "190 175 150",
            "minspeed", "2", "maxspeed", "4",
            "mindirectedspeed", "0", "maxdirectedspeed", "40",
            "startsize", "16", "endsize", "40", "spawnradius", "4");

        site.Marker("dusttrailmark", new(24f, 96f, 8f), "190 175 150", 16f);

        //---------------------------------------------------------------------
        // env_rockettrail: the third of the smoke-trail shape, and the one with
        // NO map keys at all -- RocketTrail's datadesc is fifteen DEFINE_FIELDs
        // and not one DEFINE_KEYFIELD. So this instance can carry nothing, and
        // what it draws is stock's constructor with a flare on the front.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvRockettrail, "rockettrail", new(152f, 96f, 32f));

        site.Marker("rockettrailmark", new(152f, 96f, 8f), "255 220 160", 16f);

        //---------------------------------------------------------------------
        // The one that MOVES, and the reason this station is called what it is.
        //
        // A second smoke trail, parented to a train that shuttles across the
        // plot. It is not decoration: the position a managed trail is drawn at
        // is swept in PreRender rather than delivered on a data change, because
        // OnDataChanged fires when the BLOB changes and a managed entity's
        // origin is a real prop OUTSIDE the blob. A trail with no sweep draws
        // where it was created and stays there while the entity walks away --
        // which is exactly what the steam jets did before the sweep existed,
        // and it is invisible in any static shot. This is the instance that
        // shows the difference.
        //
        // Coloured hot so the plume it leaves behind is legible against the
        // grey one standing still two squares away.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.PathCorner, "railwest", new(-160f, -16f, 20f),
            "target", site.Name("raileast"));

        site.Ent(EntityNames.PathCorner, "raileast", new(160f, -16f, 20f),
            "target", site.Name("railwest"), "wait", "1");

        site.Brush(EntityNames.FuncTrain, "rail",
            new(-176f, -32f, 8f), new(-144f, 0f, 32f), Site.Accent,
            "origin", (site.At + new Point(-160f, -16f, 20f)).ToString(),
            "target", site.Name("railwest"), "speed", "90", "dmg", "0");

        site.Ent(EntityNames.EnvSmoketrail, "movingtrail", new(-160f, -16f, 36f),
            "parentname", site.Name("rail"),
            "spawnrate", "40", "opacity", "0.7", "lifetime", "3",
            "startcolor", "255 150 40", "endcolor", "80 20 0",
            "minspeed", "2", "maxspeed", "6",
            "mindirectedspeed", "0", "maxdirectedspeed", "0",
            "startsize", "8", "endsize", "22", "spawnradius", "2");

        site.OnReset("rail", "Stop");
        site.OnReset("rail", "Start", delay: 0.6f);

        //---------------------------------------------------------------------
        // env_sporetrail, AND IT DRAWS NOTHING -- in stock as much as here.
        //
        // SporeTrail's constructor sets six of its seven fields and does not
        // touch m_bEmit, entity memory arrives zeroed, and the class has no key,
        // no input and no Spawn. So nothing a map or a player can do will make
        // one emit; only a C++ caller writing the member could, and every one of
        // those is in hl2/. The instance is here because the coverage test asks
        // for one and because "this entity is inert as shipped" is a fact the
        // map should carry rather than a note somebody has to find.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvSporetrail, "sporetrail", new(-232f, -96f, 32f));

        site.Marker("sporetrailmark", new(-232f, -96f, 8f), "225 140 64", 16f);

        //---------------------------------------------------------------------
        // env_sporeexplosion: the only one of the six a map can drive.
        //
        // It starts NOT emitting whatever the map says -- SporeExplosion's
        // constructor sets the flag and its Spawn then clears it -- so the
        // reset button's Enable is the whole demonstration. Which also makes it
        // the evidence that its neighbour's dead emit flag is an oversight: the
        // two classes are five fields apart and only this one was thought about.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvSporeexplosion, "sporeburst", new(-104f, -96f, 32f),
            "spawnrate", "100", "startdisabled", "0");

        site.Marker("sporeburstmark", new(-104f, -96f, 8f), "225 140 64", 16f);

        //---------------------------------------------------------------------
        // env_smokestack, AND THE TWO LIGHTS THAT MAKE IT WORTH LOOKING AT.
        //
        // The pair is the point. env_particlelight has four keys and no code at
        // all; the only thing that ever reads one is a smokestack, which finds
        // the lights that NAME IT and copies their intensity, colour and world
        // position into its own send table -- once, when the level is loaded.
        // The client then divides each light's intensity by the SQUARE of its
        // distance to each puff, which is what makes a column read as lit from
        // below rather than as flat grey.
        //
        // Two lights because a stack has exactly two slots: one ambient and one
        // directional. They sit low and to either side, so the difference
        // between them is visible from the front of the plot -- the orange one
        // is the fire under the chimney and the blue one is the sky above it.
        //
        // The stack itself carries every key, including the four DEFINE_INPUTs,
        // because CSmokeStack's constructor leaves all four at ZERO on the
        // server. A stack that set none of them would network four zeros, and a
        // column with no speed and no rate emits nothing -- which is stock's
        // behaviour and a poor demonstration of it.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvSmokestack, "stack", new(24f, -96f, 24f),
            "StartSize", "20", "EndSize", "90", "BaseSpread", "18",
            "Twist", "20", "Roll", "20", "InitialState", "1",
            "SpreadSpeed", "12", "Speed", "60", "Rate", "26", "JetLength", "220",
            "WindAngle", "45", "WindSpeed", "10",
            "rendercolor", "40 40 45", "renderamt", "255");

        site.Ent(EntityNames.EnvParticlelight, "stackfire", new(-16f, -96f, 12f),
            "PSName", site.Name("stack"),
            "Intensity", "9000", "Color", "255 130 40", "Directional", "1");

        site.Ent(EntityNames.EnvParticlelight, "stacksky", new(64f, -96f, 12f),
            "PSName", site.Name("stack"),
            "Intensity", "6000", "Color", "80 130 255", "Directional", "0");

        site.Marker("stackmark", new(24f, -96f, 8f), "40 40 45", 16f);
        site.Marker("stackfiremark", new(-16f, -96f, 8f), "255 130 40", 16f);
        site.Marker("stackskymark", new(64f, -96f, 8f), "80 130 255", 16f);

        site.OnReset("sporeburst", "Enable");
        site.OnReset("stack", "TurnOn");
    }

    private static void Pool(Site site)
    {
        //---------------------------------------------------------------------
        // The pen: four walls and a water lid, sunk into the pad so a player
        // can walk in over the rim.
        //
        // Only the LID carries the water material. vbsp draws a water surface
        // wherever it finds one, so a brush textured all over is a glowing box
        // rather than a pool.
        //---------------------------------------------------------------------
        site.Solid(new(-224f, 32f, 8f), new(-216f, 224f, 72f), Site.Wall);
        site.Solid(new(-48f, 32f, 8f), new(-40f, 224f, 72f), Site.Wall);
        site.Solid(new(-224f, 32f, 8f), new(-40f, 40f, 72f), Site.Wall);
        site.Solid(new(-224f, 216f, 8f), new(-40f, 224f, 72f), Site.Wall);

        site.Solid(new(-216f, 40f, 8f), new(-48f, 216f, 64f), Site.NoDraw, Water);

        //---------------------------------------------------------------------
        // The mirror pool: a pen east of the first, filled with expensive water
        // (MirrorWater) deep enough to stand in with the eye under the surface
        // (the lid is 120 deep; a standing eye is 64 up), so the renderer draws
        // its under-water view and the refraction it looks up through. Nothing
        // else in the plot reaches into it; the bubbles and the splash stay the
        // first pool's.
        //---------------------------------------------------------------------
        site.Solid(new(40f, 32f, 8f), new(48f, 224f, 136f), Site.Wall);
        site.Solid(new(216f, 32f, 8f), new(224f, 224f, 136f), Site.Wall);
        site.Solid(new(40f, 32f, 8f), new(224f, 40f, 136f), Site.Wall);
        site.Solid(new(40f, 216f, 8f), new(224f, 224f, 136f), Site.Wall);
        site.Solid(new(48f, 40f, 8f), new(216f, 216f, 128f), Site.NoDraw, MirrorWater);

        //---------------------------------------------------------------------
        // The bubbles.
        //
        // Sunk INSIDE the water, at the far end, because the effect is a column
        // rising from the brush's own corners -- and the corners are what goes
        // on the wire. Stock's TE names the entity and lets the client read its
        // bounds, which cannot work here: nothing on the parallel hierarchy has
        // a client-side entity to look the bounds up on, so the effect would
        // arrive, find nothing and emit nothing. See docs/divergences.md.
        //
        // "current" pushes them sideways as they rise, which is the only key
        // that makes the column read as water rather than as a cylinder.
        //---------------------------------------------------------------------
        site.Brush(EntityNames.EnvBubbles, "bubbles",
            new(-200f, 152f, 12f), new(-136f, 208f, 60f), Site.Trigger,
            "density", "12", "frequency", "18", "current", "24");

        //---------------------------------------------------------------------
        // The splash, which asks two questions before it makes one.
        //
        // Flag 1 finds the water surface below rather than trusting the
        // entity's own height -- so this can sit above the pool and still land
        // on it, and would follow the surface if the pool moved. Flag 2 makes
        // the splash smaller in deeper water, and below a tenth scale stock
        // draws nothing at all rather than a speck. Both are refusals as often
        // as they are answers: an env_splash UNDER the water makes none.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvSplash, "splash", new(-128f, 128f, 96f),
            "scale", "24", "spawnflags", "3");

        site.Marker("splashmark", new(-128f, 128f, 96f), "140 190 255", 60f);

        //---------------------------------------------------------------------
        // water_lod_control: how far away the engine gives up on expensive
        // water and draws the cheap kind instead.
        //
        // It is worth saying that vbsp inserts one of these into ANY map with
        // water whether the mapper wrote it or not -- which is why this class
        // was the one entry in the missing list that already had a live
        // instance in the sandbox. Writing it explicitly is the difference
        // between a default and a decision, and the entity vbsp adds carries
        // the shipped 1000/2000 rather than these.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.WaterLodControl, "lod", new(0f, 96f, 32f),
            "cheapwaterstartdistance", "500", "cheapwaterenddistance", "1200");

        site.OnReset("splash", "Splash");
        site.OnReset("bubbles", "Activate");
    }

    //=========================================================================
    // The sky, the sun and the whole screen.
    //
    // Every one of these acts on something bigger than itself -- the skybox,
    // the lightmap, the tonemapper, the frame buffer -- so none of them can be
    // demonstrated by looking at where the entity IS. What makes this plot
    // different is that <see cref="Room"/> cuts a skylight over it: it is the
    // one place in the hall with a sky face overhead, and four of the eight
    // entities here need one to do anything at all.
    //
    // THE THREE LIGHTS ARE NAMED, and that is not decoration. vbsp treats a
    // named light as a SWITCHED light and gives it a lightstyle of its own out
    // of a table of thirty-two; an UNNAMED one is a value vrad bakes and then
    // discards, and this mod's Light.OnSpawn deletes any light that arrives
    // without a name because there is nothing left for it to drive. So the nine
    // ceiling lamps and the thirty-odd button lamps in this map all spawn and
    // all remove themselves, and these three are the only light entities in the
    // hall that are alive to look at.
    //=========================================================================

    /// <summary>
    /// The two spot lights, the sun, the skybox pair, and the three that act on
    /// the whole screen.
    /// </summary>
    private static void WorldEffects(Site site)
    {
        //---------------------------------------------------------------------
        // The sun.
        //
        // Everything on a light_environment is read by vrad and burned into the
        // lightmap -- the angle, the colour, the ambient term -- and none of it
        // is legible to anything at runtime. What is left is a switchable
        // light, which is what stock's CEnvLight is too: it derives from CLight
        // and overrides nothing but a key it throws away.
        //
        // The pitch is in BOTH the angles and the pitch key, which is vrad's
        // rule rather than a duplication: it reads "pitch" if there is one and
        // the angles otherwise, and Hammer writes both.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.LightEnvironment, "sun", new(0f, 0f, 520f),
            "angles", "0 300 0", "pitch", "-40",
            "_light", "240 235 210 200",
            "_ambient", "120 130 160 60",
            "_lightHDR", "-1 -1 -1 1", "_lightscaleHDR", "1",
            "_ambientHDR", "-1 -1 -1 1", "_AmbientScaleHDR", "1",
            "SunSpreadAngle", "5");

        //---------------------------------------------------------------------
        // The two cones.
        //
        // light_spot is the ordinary one and vrad knows it. light_glspot is a
        // classname from GoldSource that the game still registers and vrad does
        // not: it prints "unsupported light entity" and contributes nothing to
        // the lightmap. Both are still live entities with a switchable style, so
        // both take TurnOn, TurnOff and SetPattern -- and standing them side by
        // side is the only honest way to show that one of them lights nothing.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.LightSpot, "cone", new(-128f, 0f, 200f),
            "angles", "0 0 0", "pitch", "-90",
            "_light", "255 200 140 400",
            "_lightHDR", "-1 -1 -1 1", "_lightscaleHDR", "1",
            "_inner_cone", "30", "_cone", "50", "_exponent", "1",
            "_fifty_percent_distance", "160", "_zero_percent_distance", "420");

        site.Ent(EntityNames.LightGlspot, "oldcone", new(128f, 0f, 200f),
            "angles", "0 0 0", "pitch", "-90",
            "_light", "140 200 255 400",
            "_inner_cone", "30", "_cone", "50", "_exponent", "1");

        // Something under each cone to see the difference on.
        site.Ent(EntityNames.PropDynamicOverride, "underlit", new(-128f, 0f, 8f),
            "model", Crate, "solid", "6");

        site.Ent(EntityNames.PropDynamicOverride, "unlit", new(128f, 0f, 8f),
            "model", Crate, "solid", "6");

        //---------------------------------------------------------------------
        // The skybox pair.
        //
        // sky_camera pushes the 3D skybox's origin, scale and fog ONCE at
        // spawn, to everybody, rather than per player -- there is one skybox
        // and it is the same for all of them. Its fog keys are the interesting
        // half here: fogdir is negated on the way in, and the sign is not
        // cosmetic, because the blend downstream is a dot product against the
        // view direction and a flipped vector makes the sky brightest where it
        // should be darkest.
        //
        // skybox_swapper changes which sky is drawn, and it does it by setting
        // sv_skyname through the console -- the same route a player typing the
        // convar takes -- because the material is chosen from that string on
        // both machines and there is no other switch. Wired to the button, so
        // pressing it changes the sky through the hole above.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.SkyCamera, "skycam", new(0f, -160f, 96f),
            "scale", "16", "angles", "0 300 0", "use_angles", "1",
            "fogenable", "1", "fogblend", "1", "fogradial", "0",
            "fogcolor", "160 180 200", "fogcolor2", "220 210 190",
            "fogstart", "500", "fogend", "3000", "fogmaxdensity", "1");

        site.Ent(EntityNames.SkyboxSwapper, "swap", new(64f, -160f, 96f),
            "SkyboxName", "sky_day02_03");

        // The OTHER half of the map's fog, and the reason it sits beside the
        // sky camera rather than in a plot of its own: sky_camera's fog keys
        // above are the 3D SKYBOX's fog, and these are the world's. The two
        // were on opposite sides of the boundary until env_fog_controller was
        // ported, and reading them next to each other is the point.
        //
        // The keys deliberately differ from the sky camera's, so a fog wired to
        // the wrong one is visible rather than plausible: this starts twice as
        // far out and ends short of the skybox's, and its second colour is warm
        // where the skybox's is cool. use_angles is on for the same reason it is
        // above -- the direction is the NEGATED facing, and a dropped sign puts
        // the second colour on the wrong side of the sky.
        //
        // Not wired to the reset button. Nothing in this build delivers world
        // fog to a client -- CBasePlayer::InitFogController and
        // C_BasePlayer::GetFogParams both left with the -ss_player flip -- so a
        // button promising a visible change would be promising one that cannot
        // happen. The entity answers its inputs and holds its state; when a fog
        // channel to the managed client exists, this is where it is driven from.
        site.Ent(EntityNames.EnvFogController, "worldfog", new(-128f, -160f, 96f),
            "angles", "0 120 0", "use_angles", "1",
            "fogenable", "1", "fogblend", "1", "fogradial", "0",
            "fogcolor", "160 180 200", "fogcolor2", "200 170 130",
            "fogstart", "1000", "fogend", "2400", "fogmaxdensity", "1",
            "farz", "-1", "foglerptime", "2");

        //---------------------------------------------------------------------
        // The sun's shadows, which are a separate entity from the sun.
        //
        // shadow_control decides the direction, colour and length of every
        // dynamic shadow in the map, and it is a networked Effect rather than
        // anything the server acts on -- the client is what has shadows. The
        // "direction" key is a second way of writing the angles and the two
        // disagree happily, which is why the angles are what this writes.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.ShadowControl, "shadows", new(-64f, -160f, 96f),
            "angles", "60 300 0", "color", "80 80 90 0", "distance", "120",
            "disableallshadows", "0");

        //---------------------------------------------------------------------
        // The two that act on the frame buffer.
        //
        // env_tonemap_controller drives the auto-exposure the whole screen is
        // scaled by. Its SetTonemapScale goes through a convar and stays one --
        // there is no per-player tonemap in this engine -- so the button
        // pressing it changes the picture for everybody, which is what the
        // entity has always done.
        //
        // color_correction names a lookup this mod does not ship, and that is
        // said out loud rather than hidden: the mod ships no content of its own
        // and a colour-correction lookup is a 32x32x32 .raw cube. The entity is
        // real on both sides regardless -- the server networks the filename and
        // the falloff, the client half decodes them, asks the engine for the
        // lookup and prints that it would not load. That warning IS the proof
        // the receive table ran.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvTonemapController, "tonemap", new(-160f, -160f, 96f));

        // env_detail_controller clamps every viewer's detail-prop fade down to
        // the map's. This hall compiles no detail props, so its proof is the
        // convar it writes rather than anything drawn: stand anywhere and
        // cl_detaildist reads 1100 against a default of 1200, restored on the
        // next level load. Both keys are set BELOW the client defaults on
        // purpose — the clamp only ever brings the fade closer, so values
        // above the defaults would be indistinguishable from the entity not
        // existing.
        site.Ent(EntityNames.EnvDetailController, "detail", new(64f, -160f, 96f),
            "fademindist", "380", "fademaxdist", "1100");

        site.Ent(EntityNames.ColorCorrection, "grade", new(160f, -160f, 96f),
            "filename", "materials/correction/sandbox.raw",
            "minfalloff", "200", "maxfalloff", "900", "maxweight", "0.8",
            "fadeInDuration", "1", "fadeOutDuration", "1", "StartDisabled", "0");

        // The volume-shaped grade, beside the point-shaped one so the two ways
        // of asking for a correction sit together. Same undeliverable lookup,
        // same argument: the warning that it would not load is the proof the
        // wire ran. The interesting half cannot be seen even with a lookup that
        // loads -- the crossing fade runs on each CLIENT, against where that
        // viewer is standing, because stock's server-side "local player" test
        // was NULL in multiplayer and the entity had never once fired in this
        // mod. Walk into the box and your grade fades in over two seconds;
        // nobody else's does.
        site.Brush(EntityNames.ColorCorrectionVolume, "gradezone",
            new(96f, -224f, 8f), new(224f, -96f, 104f), Site.Trigger,
            "filename", "materials/correction/sandbox.raw",
            "maxweight", "0.8", "fadeDuration", "2", "StartDisabled", "0");

        site.Marker("sunmark", new(0f, 96f, 8f), "255 235 200");

        site.OnReset("swap", "Trigger");
        site.OnReset("cone", "Toggle");
        site.OnReset("oldcone", "Toggle");
        site.OnReset("sun", "Toggle");
        site.OnReset("tonemap", "SetBloomScale", "1.4");
        site.OnReset("grade", "Enable");
        site.OnReset("gradezone", "Enable");
        site.OnReset("shadows", "SetDistance", "120");
    }

    //=========================================================================
    // Effects that happen once.
    //
    // Six temp-entity dispatches and two decals, and what they have in common
    // is that none of them is a thing in the world: every one is a message to
    // the client saying "draw this, here, now". So the only way to see any of
    // them is to fire it and be looking, and the only way to know one WORKED
    // from outside the game is that it left something behind -- which is why
    // the two decals are in this station rather than in a station of their own.
    //=========================================================================

    /// <summary>
    /// The six one-shot effects and the two marks.
    /// </summary>
    /// <summary>
    /// env_sprite, env_sprite_oriented and env_glow: one C++ class under three
    /// names, and three managed classnames drawn by one shell.
    ///
    /// <para>
    /// dm_lockdown places twenty-three env_sprites and none of the other two,
    /// so this is the only place the other two names are exercised at all.
    /// They are worth exercising for a reason a count does not show: an entity
    /// on the parallel hierarchy that draws NOTHING looks exactly like one that
    /// networks nothing, and the difference between the three names here is
    /// entirely in what is drawn.
    /// </para>
    ///
    /// <para>
    /// A PILLAR stands in front of the two glow sprites, and it is the point of
    /// the station rather than scenery. Render modes 3 and 9 fade against a
    /// pixel-visibility query — how much of an occlusion proxy the viewer can
    /// actually see — so a glow sprite behind something is dimmer than one in
    /// the open, and a glow sprite whose query never settles is either fully on
    /// or fully off. Walking sideways past the pillar is the only way to see
    /// that the query is running.
    /// </para>
    /// </summary>
    private static void Sprites(Site site)
    {
        //---------------------------------------------------------------------
        // env_sprite, additive, and NAMED -- which is what makes the start-on
        // flag matter. Stock's rule is that an unnamed sprite is on whatever
        // its flags say, because nothing could ever switch it on; only a named
        // one waits. All three here are named and all three set the flag, so
        // all three start visible and the button is what changes that.
        //
        // Every key is dm_lockdown's, down to the sprite: this is the exact
        // corridor light that map hangs twenty-three of.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvSprite, "plain", new(-160f, 0f, 72f),
            "model", "sprites/glow06.spr",
            "scale", "2.5", "framerate", "10.0",
            "rendermode", "5", "rendercolor", "189 233 247", "renderamt", "128",
            "GlowProxySize", "2.0", "renderfx", "0",
            "spawnflags", "1");

        //---------------------------------------------------------------------
        // env_sprite_oriented, which differs from the one above by a Spawn that
        // turns its angles round -- and by nothing else at all. Whether the
        // angles are READ is decided by the .vmt's own orientation field, not
        // by the classname, so a sprite material that faces the viewer ignores
        // them either way. Given angles here so that a mapper reading the map
        // can see which name was meant to care.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvSpriteOriented, "oriented", new(0f, 0f, 72f),
            "angles", "0 90 0",
            "model", "sprites/glow01.spr",
            "scale", "0.5", "framerate", "10.0",
            "rendermode", "9", "rendercolor", "213 0 0", "renderamt", "255",
            "GlowProxySize", "4", "renderfx", "0",
            "spawnflags", "1");

        //---------------------------------------------------------------------
        // env_glow: stock's own back-compatibility alias, whose comment in
        // Sprite.cpp reads "remove when no longer needed". It is the same class
        // as the first one with a different name on it, and it is here so that
        // the name keeps working.
        //---------------------------------------------------------------------
        //---------------------------------------------------------------------
        // env_spritetrail, riding the mover so that it has something to trail
        // BEHIND.
        //
        // It is the one member of the family that has to move to be worth
        // looking at, and the one whose client half is a stock object rather
        // than a draw call: a ribbon is a ring of points laid down over time,
        // and something has to own the ring. Parked on the plot it would draw a
        // dot; carried along the rail it draws what it is for.
        //
        // The mover is a func_tracktrain because a keyframed one would need a
        // second entity per corner, and what is being demonstrated here is the
        // TRAIL rather than the movement.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvSpritetrail, "ribbon", new(-160f, -160f, 96f),
            "spritename", "sprites/bluelaser1.vmt",
            "lifetime", "1.5", "startwidth", "16", "endwidth", "2",
            "rendermode", "5", "rendercolor", "120 190 255", "renderamt", "255",
            "scale", "1", "framerate", "0",
            "spawnflags", "1");

        site.Marker("ribbonmark", new(-160f, -160f, 8f), "120 190 255", 80f);

        // NOTHING MOVES IT, and that is the demonstration rather than a gap.
        //
        // A trail draws a ribbon between the places its entity has BEEN, so one
        // that never moves lays every point on top of the last and draws
        // nothing at all. That is stock's situation too: an env_spritetrail is
        // meant to be handed to something in flight by
        // CSpriteTrail::SpriteTrailCreate, which has zero callers in this build,
        // and the class has no map input that moves it.
        //
        // A point_teleport would be the obvious wire and is deliberately not
        // here: point_teleport resolves its target with the ENGINE's
        // FindEntityByName, which cannot see the parallel hierarchy, so the wire
        // would report success and do nothing -- exactly the silence
        // PointTeleport's own comment is about. See report.md.
        //
        // So the button does what a map CAN do to one: switch it off and on.
        site.OnReset("ribbon", "ToggleSprite");

        site.Ent(EntityNames.EnvGlow, "glow", new(160f, 0f, 72f),
            "model", "sprites/glow01.spr",
            "scale", "1.5", "framerate", "10.0",
            "rendermode", "9", "rendercolor", "120 255 160", "renderamt", "255",
            "GlowProxySize", "4", "renderfx", "0",
            "spawnflags", "1");

        // What the glows fade behind. Narrow, so that a step sideways moves the
        // sprite from hidden to clear rather than switching it.
        site.Solid(new(-32f, 96f, 8f), new(32f, 128f, 168f), Site.Wall);

        site.Marker("plainmark", new(-160f, 0f, 8f), "189 233 247", 56f);
        site.Marker("orientedmark", new(0f, 0f, 8f), "213 0 0", 56f);
        site.Marker("glowmark", new(160f, 0f, 8f), "120 255 160", 56f);

        // Three different inputs on the three sprites, because they are three
        // different code paths: ToggleSprite reads the current state,
        // HideSprite and ShowSprite do not, and a class that only implemented
        // the toggle would pass a test that only pressed the button twice.
        site.OnReset("plain", "ToggleSprite");
        site.OnReset("oriented", "HideSprite");
        site.OnReset("glow", "ShowSprite");

        // And one that changes an appearance rather than a visibility. The blue
        // channel alone, so a green glow going cyan says the input reached the
        // right channel -- which "set it to white" would not.
        site.OnReset("glow", "ColorBlueValue", "255");
    }

    private static void OneShots(Site site)
    {
        // Something to spray at and stamp on: a wall along the back of the plot.
        site.Solid(new(-240f, 200f, 8f), new(240f, 216f, 168f), Site.Wall);

        //---------------------------------------------------------------------
        // Blood.
        //
        // The "color" key is not a colour: it is an index, and stock's switch
        // recognises exactly one value of it -- 1, yellow -- and silently
        // leaves everything else red, values that name real blood colours
        // included. Kept, because a map that wrote 2 has been getting red since
        // 2004.
        //
        // Spawnflags 8|32 are "leave a decal" and "throw drops". The decal is
        // the half worth having here: it is the only one of these six that
        // survives being fired.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvBlood, "blood", new(-192f, 128f, 64f),
            "spraydir", "0 90 0", "amount", "1", "color", "0",
            "spawnflags", "40");

        //---------------------------------------------------------------------
        // Dust, and the key it ignores.
        //
        // env_dustpuff reads a "color" key, keeps it, and never sends it: the
        // temp entity that draws the puff has no colour field. Stock is the
        // same and the key is claimed rather than dropped so that a mapper
        // setting it is not told they made a typo.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvDustpuff, "puff", new(-128f, 128f, 40f),
            "scale", "24", "speed", "48", "color", "200 190 160");

        //---------------------------------------------------------------------
        // The funnel, which is the one that leaves.
        //
        // It removes itself the moment it fires -- stock schedules SUB_Remove
        // in the same frame -- so this is the one entity in the sandbox whose
        // reset button destroys it. NOT a bug and not worth working around: a
        // funnel is an event, and the station is honest about it in its text.
        //
        // "Fire" is an addition to stock, which has only Use. An input that
        // cannot be typed into ent_fire cannot be tested from outside the game
        // at all, and the whole verification method here depends on being able
        // to.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvFunnel, "funnel", new(-64f, 128f, 40f),
            "spawnflags", "0");

        //---------------------------------------------------------------------
        // The tracer, which aims where its target WAS.
        //
        // It resolves the target once, on its first think, and keeps the
        // POSITION rather than the entity -- so a target that moves afterwards
        // is not followed. Stock does exactly this and it is kept: a tracer
        // that started tracking would change every map that has one rather than
        // fix them.
        //
        // The first think is also the earliest moment the target can be found
        // at all. There is no Activate on the parallel hierarchy, and OnSpawn
        // runs while the map is still being parsed, so a tracer whose target
        // appears later in the .bsp would find nothing and never start.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.InfoTarget, "far", new(0f, 200f, 96f));

        site.Ent(EntityNames.EnvTracer, "tracer", new(0f, -64f, 40f),
            "delay", "2", "target", site.Name("far"));

        //---------------------------------------------------------------------
        // The muzzle flash, which flashes where the mapper put it.
        //
        // Stock re-parents one to a named attachment on its parent's model so
        // it can hang off a gun. The hierarchy can do that now -- it takes an
        // attachment index, and the shell claims a map's parentattachment
        // beside its parentname -- but env_muzzleflash has not been ported onto
        // it yet, so the key is still read and unused here. Written anyway,
        // the key being CLAIMED rather than ignored is the point: an unclaimed
        // key is reported as a mapper's typo, and this one is not one.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvMuzzleflash, "flash", new(64f, 128f, 56f),
            "scale", "2", "parentattachment", "muzzle");

        //---------------------------------------------------------------------
        // The quadratic beam, which draws nothing, and that IS the
        // demonstration.
        //
        // CEnvQuadraticBeam declares no keyvalues and no inputs; every field on
        // it is set by CreateQuadraticBeam, which nothing in this build calls.
        // So a mapper placing one has always got an invisible entity and still
        // does -- and it is placed rather than excluded because the CLASS is
        // real on both sides: the server spawns it white and visible, and the
        // client half decodes the three spline points and asks the engine to
        // draw a curve between them. All three are the entity's own origin, so
        // the curve has zero length.
        //
        // What is new is that managed code can now BUILD one, the way env_laser
        // builds an ss_beam. Nothing does yet, deliberately: a factory written
        // for nobody is a factory written against a guess.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.EnvQuadraticbeam, "curve", new(128f, 128f, 72f));

        //---------------------------------------------------------------------
        // The two marks, and the one thing worth knowing about them: an
        // infodecal is NEVER alive to look at.
        //
        // An unnamed one paints during level init and is gone in the same pass;
        // a NAMED one waits for Activate, sprays, and is gone a tenth of a
        // second later. So `debug-api ents infodecal` answers zero on a running
        // game whether every decal painted or none of them did, and the only
        // thing that tells the two apart is the count MapDecal.PaintPlaced
        // prints.
        //
        // Both of these are named, so both are still there to be found until
        // the button is pressed -- which is what makes this station able to
        // prove they exist at all, and is why the reset wire is worth having
        // even though it destroys them.
        //
        // The difference between the classes is how they find a surface.
        // infodecal is stamped where it stands and needs a trace; a projected
        // decal is thrown along a facing and lets the ENGINE find the surface,
        // which is what makes it work against a prop the trace filter would
        // have refused.
        //---------------------------------------------------------------------
        site.Ent(EntityNames.Infodecal, "stamp", new(-32f, 198f, 96f),
            "texture", StampDecal);

        site.Ent(EntityNames.InfoProjecteddecal, "thrown", new(96f, 160f, 96f),
            "texture", ThrownDecal,
            "angles", "0 90 0", "distance", "64");

        site.Ent(EntityNames.PropDynamicOverride, "post", new(96f, 190f, 8f),
            "model", Crate, "solid", "6");

        site.Marker("bloodmark", new(-192f, 128f, 8f), "255 140 140");
        site.Marker("puffmark", new(-128f, 128f, 8f), "200 190 160");
        site.Marker("funnelmark", new(-64f, 128f, 8f), "140 190 255");
        site.Marker("flashmark", new(64f, 128f, 8f), "255 235 140");
        site.Marker("curvemark", new(128f, 128f, 8f), "140 255 190");

        site.OnReset("blood", "EmitBlood");
        site.OnReset("puff", "SpawnDust");
        site.OnReset("funnel", "Fire", delay: 0.5f);
        site.OnReset("flash", "Fire");
        site.OnReset("stamp", "Activate", delay: 1f);
        site.OnReset("thrown", "Activate", delay: 1f);
    }

    //-------------------------------------------------------------------------
    // TEMP ENTITIES.
    //
    // The one station whose subject has no classname, no factory and no
    // instance: a temp entity is a message, not an object. That is exactly why
    // it needs a station -- SandboxTests fails the build when a
    // [ScriptedEntity] classname has no example in the map, and forty-three
    // ported effects were invisible to that rule because none of them IS an
    // entity. plan 9.1 and 9.2.
    //
    // ONE ss_te_tester PER TEMP ENTITY, each firing at its own origin, laid out
    // on the grid TeDemos.Offset defines -- the same grid `cs_te_fire all` fires
    // on, so the map and the headless gate put the same effect in the same
    // place. The book of what each effect looks like is TeDemos, in the server
    // assembly, and this station places what is in it rather than a list of its
    // own: a second list would be a second answer to "which temp entities are
    // there", and the coverage test would then be checking one against the
    // other instead of against the declarations.
    //
    // THE RESET RELAY FIRES ALL FORTY-THREE, one wire each, staggered a seventh
    // of a second apart. A single FireAll input would have been one connection
    // instead of forty-three and would have hidden the thing worth seeing: with
    // a wire per tester, SandboxTests' own rule that every wire lands on an
    // entity that exists checks all forty-three names, and the map says in its
    // own entity lump which effects it claims to demonstrate.
    //
    // The stagger is on the WIRE and not on the temp entity's own delay field,
    // because a relay delay is in the .vmf where the staleness check can see it
    // -- and because the tester's `delay` keyvalue is the CLIENT's playback
    // delay, which is a different thing wearing the same word.
    //-------------------------------------------------------------------------
    private static void TempEntities(Site site)
    {
        var demos = SourceSharp.Server.TeDemos.All;

        for (int i = 0; i < demos.Count; i++)
        {
            var demo = demos[i];

            // Plot(), NOT Offset(): the second returns an SourceSharp.Abi.Vector,
            // and a single local of that type in this assembly puts an ABI
            // reference in MapGen's metadata -- which is what discovery uses to
            // decide an assembly might carry declarations. LoadedAssemblyTests
            // pins that MapGen is on the other side of the line, and caught this.
            (float x, float y, float z) = SourceSharp.Server.TeDemos.Plot(i);

            site.Ent(EntityNames.SsTeTester, $"te_{demo.Slug}",
                new Point(x, y, z),
                "te", demo.ClassName);

            // A seventh of a second apart, so the plot sweeps rather than
            // flashing once. Forty-three effects in one frame is one white
            // frame and nothing anybody can tell apart.
            site.OnReset($"te_{demo.Slug}", "Fire", delay: i * (1f / 7f));
        }
    }
}

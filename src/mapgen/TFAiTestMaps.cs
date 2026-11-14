namespace SourceSharp.MapGen;

/// <summary>
/// The TF2 AI's purpose-built test maps (plan_bots_teams_tf2_part_g G.5), as
/// room libraries for map-tools' rooms pipeline, like <see cref="AiTestMaps"/>.
/// Our own fixtures, so <c>ss_</c> names; the entities inside are stock TF2
/// classes.
/// </summary>
public static class TFAiTestMaps
{
    /// <summary>ss_ai_tf_jump's one room: the widest a room's navigation takes (128 voxels of 16 u), for a 1,800 u run.</summary>
    public const float JumpCell = 2048f;

    /// <summary>The floor's top, and the two lanes' and the yard's bounds across the room (y).</summary>
    public const float FloorZ = 16f, RunSouth = 16f, RunNorth = 448f, LowSouth = 464f, LowNorth = 800f, YardSouth = 816f;

    /// <summary>The court the lanes open on at the west, and where each lane's far end stands (x).</summary>
    public const float CourtEast = 400f, LaneEnd = 1920f;

    /// <summary>The low lane's headroom under its slab: a standing body (82) fits and a jump does not.</summary>
    public const float LowHeadroom = 100f;

    /// <summary>The three ledges' heights over the floor, past a Scout's double jump (117).</summary>
    public static readonly float[] LedgeHeights = [150f, 250f, 350f];

    /// <summary>The ledges' and the gap platforms' size, the gap between the platforms, and their height.</summary>
    public const float LedgeSize = 256f, GapWidth = 500f, GapHeight = 192f;

    private const string Shell = "DEV/DEV_MEASUREWALL01A";
    private const string Ledge = "DEV/DEV_MEASUREGENERIC01";

    public const int Red = 2, Blue = 3;

    /// <summary>How far over the floor a spawn point stands.</summary>
    public const float SpawnLift = 16f;

    /// <summary>The centre of the top of ledge <paramref name="i"/> (0 the lowest).</summary>
    public static Point LedgeTop(int i) => new(428f + i * 600f, 1150f, FloorZ + LedgeHeights[i]);

    /// <summary>The far gap platform's top centre and the near one's; the near one is <see cref="GapNearHalf"/> each way along x, the far one <see cref="LedgeSize"/>.</summary>
    public static Point GapFar => new(1640f, 1778f, FloorZ + GapHeight);

    public static Point GapNear => new(692f, 1778f, FloorZ + GapHeight);

    public const float GapNearHalf = 192f;

    /// <summary>Each lane's ends on the floor: the run's, then the low lane's.</summary>
    public static Point RunWest => new(128f, 232f, FloorZ);

    public static Point RunEast => new(LaneEnd, 232f, FloorZ);

    public static Point LowEast => new(LaneEnd, 632f, FloorZ);

    /// <summary>
    /// ss_ai_tf_jump: a court at the west with the red spawns, and three parts
    /// east of it. The run: a flat lane 1,800 u long to the blue flag. The low
    /// lane: the same under a slab <see cref="LowHeadroom"/> over the floor,
    /// where a jump meets the ceiling, to red's capture zone. So a red bot
    /// alone plays capture the flag round and round: east along the run for
    /// the flag (a blast jump for speed is timed here), back along it
    /// carrying, east under the slab to capture, and back under it with
    /// nothing in its hands. The yard: three ledges 150, 250 and 350 u up with
    /// no way to them on foot, and two platforms <see cref="GapHeight"/> up and
    /// <see cref="GapWidth"/> apart, stairs to the near one only. A full ammo
    /// pack at each lane's end and on each top. The flags make it a CTF map,
    /// without which the tf2 mod runs no teams (TFGameModeSelector).
    /// </summary>
    public static VmfMap Jump()
    {
        var map = new VmfMap();
        map.World.Add(new("skyname", "sky_day01_01"));
        AiTestMaps.Room(map, 0, "course", east: false, west: false, JumpCell);
        float c = JumpCell, w = AiTestMaps.Wall;

        // The lanes' walls, from the court's east edge to the east shell.
        map.WorldSolids.Add(VmfMap.Box(new Point(CourtEast, RunNorth, FloorZ), new Point(c - w, LowSouth, c - w), Shell));
        map.WorldSolids.Add(VmfMap.Box(new Point(CourtEast, LowNorth, FloorZ), new Point(c - w, YardSouth, c - w), Shell));

        // The court: four red spawns before the run, a health kit.
        foreach (float y in (float[])[136, 200, 264, 328])
            TeamSpawn(map, new Point(RunWest.X, y, FloorZ), Red, 0);
        AiTestMaps.Item(map, "item_healthkit_full", new Point(128, 632, FloorZ + 8));
        AiTestMaps.Item(map, "item_ammopack_full", new Point(288, 232, FloorZ + 8));

        // The run and the low lane: a blue spawn and ammo at each far end.
        TeamSpawn(map, RunEast, Blue, 180);
        AiTestMaps.Item(map, "item_ammopack_full", RunEast + new Point(-96, 0, 8));
        TeamSpawn(map, LowEast, Blue, 180);
        AiTestMaps.Item(map, "item_ammopack_full", LowEast + new Point(-96, 0, 8));
        Flag(map, RunEast + new Point(-192, 0, 8), Blue, "flag_blue");
        Flag(map, new Point(128, 720, FloorZ + 8), Red, "flag_red");
        var zone = new VmfEntity { ClassName = "func_capturezone" };
        zone.Set("targetname", "capturezone_red").Set("TeamNum", $"{Red}").Set("capturepoint", "1").Set("StartDisabled", "0");
        zone.Solids.Add(VmfMap.Box(LowEast + new Point(-256, -64, 0), LowEast + new Point(-128, 64, 128), "TOOLS/TOOLSTRIGGER"));
        map.Entities.Add(zone);
        map.WorldSolids.Add(VmfMap.Box(new Point(500, LowSouth, FloorZ + LowHeadroom), new Point(1800, LowNorth, FloorZ + LowHeadroom + 32), Ledge));

        // The yard's ledges: a block each, ammo on top.
        for (int i = 0; i < LedgeHeights.Length; i++)
        {
            Point top = LedgeTop(i);
            float h = LedgeSize / 2;
            map.WorldSolids.Add(VmfMap.Box(new Point(top.X - h, top.Y - h, FloorZ), new Point(top.X + h, top.Y + h, top.Z), Ledge));
            AiTestMaps.Item(map, "item_ammopack_full", top + new Point(0, 0, 8));
        }

        // The gap: twelve 16 u steps up to the near platform, the far one alone.
        float near0 = GapNear.X - GapNearHalf, near1 = GapNear.X + GapNearHalf, y0 = GapNear.Y - LedgeSize / 2, y1 = GapNear.Y + LedgeSize / 2;
        for (int s = 1; s <= 12; s++)
            map.WorldSolids.Add(VmfMap.Box(new Point(near0 - (13 - s) * 32, y0, FloorZ), new Point(near0, y1, FloorZ + s * 16), Ledge));
        map.WorldSolids.Add(VmfMap.Box(new Point(near0, y0, FloorZ), new Point(near1, y1, GapNear.Z), Ledge));
        map.WorldSolids.Add(VmfMap.Box(new Point(near1 + GapWidth, y0, FloorZ), new Point(near1 + GapWidth + 2 * LedgeSize, y1, GapFar.Z), Ledge));
        AiTestMaps.Item(map, "item_ammopack_full", GapFar + new Point(0, 0, 8));
        return map;
    }

    /// <summary>A CTF flag with ctf_2fort's keys.</summary>
    private static void Flag(VmfMap map, Point at, int team, string name)
    {
        var e = new VmfEntity { ClassName = "item_teamflag" };
        e.Set("origin", at.ToString()).Set("targetname", name).Set("TeamNum", $"{team}").Set("GameType", "0").Set("ReturnTime", "60")
         .Set("StartDisabled", "0").Set("flag_model", "models/flag/briefcase.mdl");
        map.Entities.Add(e);
    }

    private static void TeamSpawn(VmfMap map, Point at, int team, int yaw)
    {
        // Clear of the floor: the mod's spawn walk refuses a spot whose hull starts in a solid (TFSpawnSelector.IsSpawnPointValid).
        var e = new VmfEntity { ClassName = "info_player_teamspawn" };
        e.Set("origin", (at + new Point(0, 0, SpawnLift)).ToString()).Set("angles", $"0 {yaw} 0").Set("TeamNum", $"{team}").Set("StartDisabled", "0");
        map.Entities.Add(e);
    }
}

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

    // ------------------------------------------------------------------ ss_ai_tf_course (lane G7a)

    /// <summary>One stage of the course: a platform's top, from <see cref="West"/>/<see cref="South"/> to <see cref="East"/>/<see cref="North"/>, at <see cref="Top"/>.</summary>
    public readonly record struct CourseStage(float West, float South, float East, float North, float Top)
    {
        public Point Centre => new((West + East) / 2f, (South + North) / 2f, Top);
    }

    /// <summary>The course's pit floor: every stage is over <see cref="CoursePitDepth"/> above it.</summary>
    public const float CourseFloorZ = 16f, CoursePitDepth = 640f;

    /// <summary>The side of the bay a stage's cabinet stands in.</summary>
    public const float BaySize = 128f;

    /// <summary>
    /// The stages S0 (the spawns) to S6 (the end), in the course's order: a U, east along the south, north, then west.
    /// Each jump is the next stage's rise and gap from the one before: +100 over 128, +200 over 128, a 400 u gap
    /// level, +300 over 128, a 560 u gap level, +350 over 128 (the last: at +400 the pass finds no jump). S1 is 320 u
    /// across and the first three stages step north (S0's middle 64 u south of S1's, S1's 96 u south of S2's): the line
    /// out from S2's edge crosses S1 and misses S0, so no jump skips a stage (at one line the pass found S0 to S2), and
    /// each takeoff is 64 u in from its platform's side.
    /// </summary>
    public static readonly CourseStage[] CourseStages =
    [
        new(64f, 176f, 320f, 432f, CourseFloorZ + CoursePitDepth),
        new(448f, 208f, 704f, 528f, CourseFloorZ + CoursePitDepth + 100f),
        new(832f, 336f, 1088f, 592f, CourseFloorZ + CoursePitDepth + 300f),
        new(1488f, 336f, 1744f, 592f, CourseFloorZ + CoursePitDepth + 300f),
        new(1488f, 720f, 1744f, 976f, CourseFloorZ + CoursePitDepth + 600f),
        new(1488f, 1536f, 1744f, 1792f, CourseFloorZ + CoursePitDepth + 600f),
        new(1104f, 1536f, 1360f, 1792f, CourseFloorZ + CoursePitDepth + 950f),
    ];

    /// <summary>
    /// Where each stage's cabinet bay is: a <see cref="BaySize"/> square on the platform's side away from the line the
    /// course takes over it (none for S0 and the end, whose cabinets take the whole top). A bot walking from its landing to
    /// its next takeoff passes it by: it goes in for health. Each bay is off the lines out from the stages' edges, so it
    /// gives the link pass no way round a stage.
    /// </summary>
    public static readonly (float West, float South)?[] CourseBays =
    [
        null,
        (512f, 80f),
        (896f, 592f),
        (1744f, 400f),
        (1744f, 784f),
        (1744f, 1600f),
        null,
    ];

    /// <summary>
    /// The teleport regions under the course, one a jump, which split the room's floor plan between them: a fall in one
    /// puts the body back on the jump's takeoff stage. (x0, y0, x1, y1) boxes; the fourth jump's is two.
    /// </summary>
    public static readonly (int Jump, float X0, float Y0, float X1, float Y1)[] CourseResets =
    [
        (1, 16f, 16f, 576f, 1300f),
        (2, 576f, 16f, 1000f, 1300f),
        (3, 1000f, 16f, 2032f, 656f),
        (4, 1000f, 656f, 2032f, 1040f),
        (4, 1000f, 1040f, 1424f, 1300f),
        (5, 1424f, 1040f, 2032f, 2032f),
        (6, 16f, 1300f, 1424f, 2032f),
    ];

    /// <summary>A reset's volume ends this far under the lowest top over its region, so nobody standing on a stage is in one.</summary>
    public const float ResetUnder = 128f;

    /// <summary>A stage's capture area rises this far over its top.</summary>
    public const float CaptureHeight = 128f;

    /// <summary>
    /// ss_ai_tf_course: a rocket-jump course of seven stages over a pit, each stage reached from the one before only by a
    /// blast jump (plan_bots_teams_tf2_part_g G.7, lane G7a). What a jump map has round it: the spawns and a cabinet on
    /// the first stage; a cabinet in a bay on each stage after; a control point on each stage after the first, each
    /// needing the one before, its capture area over the stage, under a master that lets no team win, so the course
    /// stays done; and a reset under each jump, a teleport back to its takeoff stage. The master makes it a control-point
    /// map, without which the tf2 mod runs no teams; there is no flag.
    /// </summary>
    public static VmfMap Course()
    {
        var map = new VmfMap();
        map.World.Add(new("skyname", "sky_day01_01"));
        AiTestMaps.Room(map, 0, "course", east: false, west: false, JumpCell);

        for (int s = 0; s < CourseStages.Length; s++)
        {
            CourseStage st = CourseStages[s];
            map.WorldSolids.Add(VmfMap.Box(new Point(st.West, st.South, CourseFloorZ), new Point(st.East, st.North, st.Top), Ledge));
            if (CourseBays[s] is { } bay)
            {
                map.WorldSolids.Add(VmfMap.Box(new Point(bay.West, bay.South, CourseFloorZ), new Point(bay.West + BaySize, bay.South + BaySize, st.Top), Ledge));
                Regenerate(map, new Point(bay.West, bay.South, st.Top), new Point(bay.West + BaySize, bay.South + BaySize, st.Top + CaptureHeight));
            }
            else
            {
                Regenerate(map, new Point(st.West, st.South, st.Top), new Point(st.East, st.North, st.Top + CaptureHeight));
            }

            var destination = new VmfEntity { ClassName = "info_teleport_destination" };
            destination.Set("targetname", $"stage{s}_start").Set("origin", (st.Centre + new Point(0, 0, 8)).ToString()).Set("angles", $"0 {CourseYaw(s)} 0");
            map.Entities.Add(destination);
            if (s == 0)
                continue;

            var point = new VmfEntity { ClassName = "team_control_point" };
            string previous = s == 1 ? $"stage{s}" : $"stage{s - 1}";
            point.Set("targetname", $"stage{s}").Set("origin", st.Centre.ToString()).Set("point_index", $"{s - 1}").Set("point_default_owner", "0")
                 .Set("point_printname", $"Stage {s}").Set("spawnflags", "2").Set("team_previouspoint_2_0", previous).Set("team_previouspoint_3_0", previous);
            map.Entities.Add(point);
            var area = new VmfEntity { ClassName = "trigger_capture_area" };
            area.Set("area_cap_point", $"stage{s}").Set("area_time_to_cap", "1").Set("team_cancap_2", "1").Set("team_cancap_3", "1")
                .Set("team_numcap_2", "1").Set("team_numcap_3", "1");
            area.Solids.Add(VmfMap.Box(new Point(st.West, st.South, st.Top), new Point(st.East, st.North, st.Top + CaptureHeight), "TOOLS/TOOLSTRIGGER"));
            map.Entities.Add(area);
        }

        var master = new VmfEntity { ClassName = "team_control_point_master" };
        master.Set("targetname", "course_master").Set("origin", "1024 1024 64").Set("cpm_restrict_team_cap_win", "1");
        map.Entities.Add(master);

        // The spawns: four of each team on the first stage, facing the first jump.
        CourseStage first = CourseStages[0];
        foreach (float y in (float[])[224, 272, 320, 368])
        {
            TeamSpawn(map, new Point(first.West + 64, y, first.Top), Red, 0);
            TeamSpawn(map, new Point(first.West + 128, y, first.Top), Blue, 0);
        }

        foreach ((int jump, float x0, float y0, float x1, float y1) in CourseResets)
        {
            var reset = new VmfEntity { ClassName = "trigger_teleport" };
            reset.Set("target", $"stage{jump - 1}_start").Set("spawnflags", "1");
            reset.Solids.Add(VmfMap.Box(new Point(x0, y0, CourseFloorZ), new Point(x1, y1, ResetTop(jump, x0, y0, x1, y1)), "TOOLS/TOOLSTRIGGER"));
            map.Entities.Add(reset);
        }

        return map;
    }

    /// <summary>
    /// The top of jump <paramref name="jump"/>'s reset over (<paramref name="x0"/>, <paramref name="y0"/>)-(<paramref name="x1"/>,
    /// <paramref name="y1"/>): <see cref="ResetUnder"/> under the lowest of its two stages' tops and any stage's over it.
    /// </summary>
    public static float ResetTop(int jump, float x0, float y0, float x1, float y1)
    {
        float lowest = MathF.Min(CourseStages[jump - 1].Top, CourseStages[jump].Top);
        for (int s = 0; s < CourseStages.Length; s++)
        {
            CourseStage st = CourseStages[s];
            if (Overlaps(st.West, st.South, st.East, st.North, x0, y0, x1, y1)
                || (CourseBays[s] is { } bay && Overlaps(bay.West, bay.South, bay.West + BaySize, bay.South + BaySize, x0, y0, x1, y1)))
                lowest = MathF.Min(lowest, st.Top);
        }

        return lowest - ResetUnder;
    }

    private static bool Overlaps(float ax0, float ay0, float ax1, float ay1, float bx0, float by0, float bx1, float by1)
        => ax0 < bx1 && bx0 < ax1 && ay0 < by1 && by0 < ay1;

    /// <summary>The way stage <paramref name="s"/>'s jump goes on (its destination faces it): east, then north, then west at the last.</summary>
    public static int CourseYaw(int s) => s switch
    {
        <= 2 => 0,
        3 or 4 => 90,
        _ => 180,
    };

    /// <summary>A resupply cabinet's zone for every team (TeamNum 0, as jump maps have them).</summary>
    private static void Regenerate(VmfMap map, Point mins, Point maxs)
    {
        var zone = new VmfEntity { ClassName = "func_regenerate" };
        zone.Set("TeamNum", "0");
        zone.Solids.Add(VmfMap.Box(mins, maxs, "TOOLS/TOOLSTRIGGER"));
        map.Entities.Add(zone);
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

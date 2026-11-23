using System.Text.Json;
using SourceSharp.Host.Contracts;
using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Dressing;
using SourceSharp.MapGen.Descent.Shapes;
using SourceSharp.MapGen.Descent.Themes;
using TF2.Descent.Rules.Layout;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The themed boss arenas (brief p13) and their contract with the server's boss encounter (the game-logic
/// session's, <see cref="ArenaData"/>, <see cref="BossNames"/>): the boss's spot and facing, the Tank's
/// track and its fit, the giants' reach to its stations, the brushes the encounter switches, and the
/// <c>ARNA</c> data a level carries.
/// </summary>
public sealed class DescentBossArenaTests(DescentLibraries libs) : IClassFixture<DescentLibraries>
{
    private (Room Built, Room Dressed, RoomTheme Theme) Arena(int depth, ulong seed = 0) =>
        libs.Dressed(depth, seed).Single(r => r.Dressed.Entry.Tags.Contains("boss_arena"));

    public static TheoryData<int> BossDepths => [5, 10, 15];

    public static TheoryData<int, ulong> TrackSeeds => new() { { 10, 0 }, { 10, 1 }, { 10, 2 }, { 10, 3 } };

    private static IEnumerable<RoomEntity> Named(Room room, string name) => room.Entities.Where(e => e.Key("targetname") == name);

    //-------------------------------------------------------------------------
    // The boss spot.
    //-------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BossDepths))]
    public void A_boss_arena_carries_its_boss_spot_as_its_first_pack_marker(int depth)
    {
        Room arena = Arena(depth).Dressed;
        var first = arena.Markers.First(m => m.Kind == MarkerKind.PackSpawn);

        Assert.NotNull(arena.Arena);
        Assert.Equal(arena.Arena!.Boss, first.At);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    // Level 5's arena has no west door: its boss faces the maze's way in, west (DescentMazeLevelTests, brief p18).
    public void The_boss_faces_the_arenas_way_in(int depth)
    {
        Room arena = Arena(depth).Dressed;
        var (face, offset) = arena.Sockets.First(s => s.Face == CellFace.West);
        Point door = arena.DoorInside(face, offset, 0);
        double want = (Math.Atan2(door.Y - arena.Arena!.Boss.Y, door.X - arena.Arena.Boss.X) * 180 / Math.PI + 360) % 360;

        Assert.InRange(Math.Abs(arena.Arena.BossYaw - want), 0, 1);
    }

    [Fact]
    public void A_second_boss_spot_or_one_after_a_pack_is_refused()
    {
        RoomBuilder Two() => new RoomBuilder("arena_t", RoomKit.Normal, RoomTheme.Grey).WithFootprint(2).WithSockets((CellFace.West, 0)).WithHeight(1024);

        Assert.Throws<InvalidOperationException>(() => Two().WithBossSpot(new Point(1024, 1024, 16), 0).WithBossSpot(new Point(900, 900, 16), 0));
        Assert.Throws<InvalidOperationException>(() => Two().Pack(900, 900).WithBossSpot(new Point(1024, 1024, 16), 0));
        Assert.NotNull(Two().WithBossSpot(new Point(1024, 1024, 16), 0).Pack(900, 900));
    }

    [Theory]
    [InlineData(5, 3)]
    [InlineData(10, 3)]
    [InlineData(15, 4)]
    public void Each_arenas_mouths_open_leg_by_leg_from_the_first_to_its_bosss_last_phase(int depth, int phases)
    {
        var legs = Arena(depth).Dressed.Arena!.Mouths.Select(m => m.Leg).Distinct().Order();

        Assert.Equal(Enumerable.Range(1, phases), legs);
    }

    //-------------------------------------------------------------------------
    // The Bore's track.
    //-------------------------------------------------------------------------

    [Fact]
    public void Only_the_bores_arena_has_a_track()
    {
        Assert.Empty(Arena(5).Dressed.Arena!.Track);
        Assert.Empty(Arena(15).Dressed.Arena!.Track);
        Assert.NotEmpty(Arena(10).Dressed.Arena!.Track);
    }

    [Fact]
    public void The_bores_track_has_four_stations_then_the_final_one_last()
    {
        var track = Arena(10).Dressed.Arena!.Track;

        Assert.Equal(4, track.Count(p => p.Station && !p.Final));
        Assert.True(track[^1].Final);
        Assert.Single(track, p => p.Final);
        Assert.Equal([1u, 1u, 1u, 1u, 3u], track.Where(p => p.Station || p.Final).Select(p => p.Flags));
    }

    [Fact]
    public void The_bores_track_runs_about_ten_and_a_half_thousand_units()
    {
        // The economy model assumes five stations over about 10,700 (the game-logic session's contract).
        Assert.InRange(Arena(10).Dressed.Arena!.TrackLength, 10_000, 11_000);
    }

    [Theory]
    [MemberData(nameof(TrackSeeds))]
    public void The_tanks_hull_fits_along_the_whole_track_dressed(int depth, ulong seed)
    {
        var (_, dressed, theme) = Arena(depth, seed);

        var hits = TankTrack.Collisions(dressed, DressLint.Models(theme));

        Assert.True(hits.Count == 0, string.Join("\n", hits));
    }

    [Fact]
    public void A_block_on_a_leg_stops_the_tank()
    {
        var (_, dressed, theme) = Arena(10);
        // A block on the middle of the third leg's line (whichever way the track runs there).
        Point a = dressed.Arena!.Track[4].At, c = dressed.Arena!.Track[5].At, at = new((a.X + c.X) / 2, (a.Y + c.Y) / 2, a.Z);
        Room blocked = dressed with { World = [.. dressed.World, Brush.Box(new Point(at.X - 32, at.Y - 32, at.Z), new Point(at.X + 32, at.Y + 32, at.Z + 64), theme.Block)] };

        Assert.Empty(TankTrack.Collisions(dressed, DressLint.Models(theme)));
        Assert.NotEmpty(TankTrack.Collisions(blocked, DressLint.Models(theme)));
    }

    [Fact]
    public void The_dressing_keeps_out_of_the_tracks_keep_clear()
    {
        var (built, dressed, theme) = Arena(10);
        var (lo, hi) = built.KeepClear[3];
        var prop = new RoomEntity("prop_static", new Point((lo.X + hi.X) / 2, (lo.Y + hi.Y) / 2, built.Kit.Sill),
            [new("model", Bosses.LightTower.Model), new("angles", "0 0 0"), new("solid", "6"), new(RoomDresser.RoleKey, nameof(RoomDresser.Role.Floor))], [], []);

        Assert.DoesNotContain(DressLint.Check(built, dressed, theme), f => f.Rule == DressLint.KeepClear);
        Assert.Contains(DressLint.Check(built, dressed with { Entities = [.. dressed.Entities, prop] }, theme), f => f.Rule == DressLint.KeepClear);
    }

    [Theory]
    [MemberData(nameof(TrackSeeds))]
    public void A_giant_walks_from_the_way_in_to_every_station_with_every_pillar_and_its_rubble_standing(int depth, ulong seed)
    {
        // Every switched brush solid at once is the most the floor is ever blocked: a pillar falls into rubble, so
        // any state of the fight blocks a subset of this.
        var (_, dressed, theme) = Arena(depth, seed);
        var walk = new FlatHullWalk(dressed, DressLint.Models(theme));
        var (face, offset) = dressed.Sockets.First(s => s.Face == CellFace.West);
        var reached = walk.Reached(walk.DoorColumn(face, offset).I, walk.DoorColumn(face, offset).J);

        foreach (TrackPoint p in dressed.Arena!.Stations)
            Assert.Contains(FloorPlan.ColumnOf(p.At.X, p.At.Y), reached);
    }

    [Fact]
    public void A_wall_across_the_bore_cuts_the_giant_off_its_far_stations()
    {
        var (_, dressed, theme) = Arena(10);
        float m = dressed.Extent / 2f, f = dressed.Kit.Sill;
        Room walled = dressed with { World = [.. dressed.World, Brush.Box(new Point(m - 8, 16, f), new Point(m + 8, dressed.Extent - 16, f + 200), theme.Block)] };
        var walk = new FlatHullWalk(walled, DressLint.Models(theme));
        var (face, offset) = walled.Sockets.First(s => s.Face == CellFace.West);
        var reached = walk.Reached(walk.DoorColumn(face, offset).I, walk.DoorColumn(face, offset).J);

        Assert.Contains(walled.Arena!.Stations, p => !reached.Contains(FloorPlan.ColumnOf(p.At.X, p.At.Y)));
    }

    //-------------------------------------------------------------------------
    // The brushes the encounter switches.
    //-------------------------------------------------------------------------

    [Fact]
    public void The_bore_names_a_pillar_and_its_rubble_for_each_station_by_number()
    {
        Room arena = Arena(10).Dressed;

        for (int n = 1; n <= 5; n++)
        {
            var pillar = Assert.Single(Named(arena, $"descent_boss_pillar_{n}"));
            var rubble = Assert.Single(Named(arena, $"descent_boss_rubble_{n}"));
            Assert.Equal("func_brush", pillar.ClassName);
            Assert.Equal("0", pillar.Key("StartDisabled"));
            Assert.Equal("1", rubble.Key("StartDisabled"));
            Assert.Equal("0", pillar.Key("Solidity"));
        }

        Assert.Empty(Named(arena, "descent_boss_pillar_6"));
    }

    [Fact]
    public void The_final_station_is_the_central_pillars_and_each_station_stands_beside_its_pillar()
    {
        Room arena = Arena(10).Dressed;
        var stations = arena.Arena!.Stations;
        float m = arena.Extent / 2f;

        for (int n = 1; n <= stations.Count; n++)
        {
            var (x, y, r) = ArenaShape.BorePillars[n - 1];
            float d = MathF.Sqrt(MathF.Pow(stations[n - 1].At.X - (m + x), 2) + MathF.Pow(stations[n - 1].At.Y - (m + y), 2));
            Assert.InRange(d - r, ArenaShape.TankRadius, ArenaShape.TankRadius + 200);
        }

        Assert.Equal(0, ArenaShape.BorePillars[^1].X);
        Assert.Equal(0, ArenaShape.BorePillars[^1].Y);
    }

    [Fact]
    public void The_pit_names_its_platform_sections_for_phases_two_to_four_and_stages_its_slime()
    {
        Room arena = Arena(15).Dressed;

        foreach (int phase in (int[])[2, 3, 4])
        {
            Assert.All(Named(arena, $"descent_boss_platform_{phase}"), e => Assert.Equal("func_brush", e.ClassName));
            Assert.NotEmpty(Named(arena, $"descent_boss_platform_{phase}"));
            var slime = Named(arena, $"descent_boss_hazard_{phase}_slime").ToArray();
            Assert.NotEmpty(slime);
            Assert.All(slime, e => Assert.Equal(("trigger_multiple", "1"), (e.ClassName, e.Key("StartDisabled"))));
        }

        Assert.Empty(Named(arena, "descent_boss_platform_1"));
        Assert.DoesNotContain(arena.Entities, e => e.Key("targetname") is { } n && n.StartsWith("descent_boss_pillar_", StringComparison.Ordinal));
    }

    [Fact]
    public void A_staged_hazard_is_the_class_and_kind_with_hazard_writes_named_by_its_phase()
    {
        Room room = new RoomBuilder("pit_t", RoomKit.Normal, RoomTheme.Grey).WithDoors(CellFace.West).WithoutDoorFrames()
            .WithStagedHazard(3, "slime", new Point(100, 100, 16), new Point(200, 200, 48)).WithHazard("slime", new Point(300, 300, 16), new Point(400, 400, 48)).Build();

        var staged = Assert.Single(Named(room, "descent_boss_hazard_3_slime"));
        var always = Assert.Single(Named(room, "tf2_descent_hazard_slime"));
        Assert.Equal(always.ClassName, staged.ClassName);
        Assert.Equal("1", staged.Key("StartDisabled"));
        Assert.Null(always.Key("StartDisabled"));
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(10, true)]
    [InlineData(15, false)]
    [InlineData(6, false)]
    [InlineData(9, false)]
    public void Only_a_boss_levels_way_down_is_shut_under_the_exit_cover(int depth, bool shut)
    {
        var exits = libs.Built(depth).Where(r => r.Role == RoomRole.Down).ToArray();

        if (shut)
        {
            Assert.NotEmpty(exits);
            Assert.All(exits, r => Assert.Single(Named(r, "descent_boss_exit")));
            Assert.All(exits.SelectMany(r => Named(r, "descent_boss_exit")), e => Assert.Equal(("func_brush", "0"), (e.ClassName, e.Key("StartDisabled"))));
        }
        else
            Assert.DoesNotContain(exits, r => Named(r, "descent_boss_exit").Any());
    }

    [Fact]
    public void The_exit_cover_fills_the_hole_over_the_way_downs_volume()
    {
        Room exit = libs.Built(5).First(r => r.Role == RoomRole.Down);
        Brush cover = Assert.Single(Assert.Single(Named(exit, "descent_boss_exit")).Brushes);
        Brush volume = Assert.Single(exit.Entities.Single(e => e.ClassName == "trigger_room_transition").Brushes);

        Assert.True(cover.Mins.X <= volume.Mins.X && cover.Maxs.X >= volume.Maxs.X && cover.Mins.Y <= volume.Mins.Y && cover.Maxs.Y >= volume.Maxs.Y);
        Assert.Equal(exit.Kit.Sill, cover.Mins.Z);
    }

    [Fact]
    public void Boss_names_are_the_contracts_exact_spelling()
    {
        Assert.Equal("descent_boss_exit", BossNames.Exit);
        Assert.Equal("descent_boss_pillar_5", BossNames.Pillar(5));
        Assert.Equal("descent_boss_rubble_12", BossNames.Rubble(12));
        Assert.Equal("descent_boss_platform_2", BossNames.Platform(2));
        Assert.Equal("descent_boss_hazard_3_slime", BossNames.Hazard(3, "slime"));
        Assert.Equal("descent_boss_mouth_1", BossNames.Mouth(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => BossNames.Pillar(0));
    }

    //-------------------------------------------------------------------------
    // The Magazine's height.
    //-------------------------------------------------------------------------

    [Fact]
    public void The_magazine_is_768_high_with_rocket_air_over_its_floor_and_headroom_over_its_ledges()
    {
        // Brief p16: Sir Nukesalot (x1.9, the largest the nav's hulls hold) read small in a 1,024-high hall.
        Room arena = Arena(5).Dressed;
        var columns = new RoomColumns(arena);
        var solids = new RoomSolids(arena);
        float sill = arena.Kit.Sill;
        int[] ledges = [JumpTable.Of(JumpTier.S2).Ledge(0.5f, arena.Kit.NavVoxel), JumpTable.Of(JumpTier.B1).Ledge(0.5f, arena.Kit.NavVoxel)];
        // Every probe standing on the ring ledge (96) or a shelf (224), and the air over it to the next brush or the roof.
        var probes = Enumerable.Range(0, arena.Extent / RoomLint.Grid).Select(i => i * RoomLint.Grid + RoomColumns.Probe).ToArray();
        var tops = (from x in probes
                    from y in probes
                    from h in ledges
                    where solids.FloorBelow(x, y, sill + h + 1) is { } z && Math.Abs(z - sill - h) < 0.5f
                    select (H: h, X: x, Y: y, Air: (solids.CeilingAbove(x, y, sill + h) ?? arena.Height) - sill - h)).ToArray();

        Assert.Equal(768, arena.Height);
        Assert.Empty(RoomLint.CheckJumpClearAir(arena, columns));
        Assert.All(ledges, h => Assert.Contains(tops, t => t.H == h));
        Assert.All(tops, t => Assert.True(t.Air >= JumpTable.LedgeClearance, $"the ledge at {t.H} at ({t.X} {t.Y}) has {t.Air} of air over it"));
    }

    //-------------------------------------------------------------------------
    // Lighting.
    //-------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BossDepths))]
    public void A_boss_arena_hangs_every_lamp_it_asks_for(int depth)
    {
        var (built, dressed, theme) = Arena(depth);

        Assert.NotEmpty(built.LampSites);
        Assert.DoesNotContain(DressLint.Check(built, dressed, theme), f => f.Rule == DressLint.LampSite);
    }

    [Fact]
    public void A_lamp_site_with_no_room_for_its_lamp_is_a_finding()
    {
        var (built, dressed, theme) = Arena(5);
        var (kind, at, yaw) = built.LampSites[0];
        // The lamp asked for again at a point in the middle of the floor, on no wall: never hung.
        Room asks = built with { LampSites = [.. built.LampSites, (kind, new Point(at.X + 400, at.Y + 400, at.Z), yaw)] };

        Assert.Contains(DressLint.Check(asks, dressed, theme), f => f.Rule == DressLint.LampSite);
    }

    [Fact]
    public void The_magazine_hangs_one_white_work_light_and_its_red_lamps()
    {
        var (_, dressed, _) = Arena(5);
        var fixtures = dressed.Entities.Where(e => e.Key(RoomDresser.RoleKey) == nameof(RoomDresser.Role.Lamp) && e.ClassName == "prop_static").ToArray();

        Assert.Single(fixtures, e => e.Key("model") == ActOne.BellLamp.Model);
        Assert.True(fixtures.Count(e => e.Key("model") == Bosses.Siren.Model) >= 6);
    }

    [Fact]
    public void The_pits_torches_stand_on_its_platform_only()
    {
        var (_, dressed, _) = Arena(15);
        float m = dressed.Extent / 2f;
        var torches = dressed.Entities.Where(e => e.Key("model") == Bosses.Torch.Model).ToArray();

        Assert.NotEmpty(torches);
        Assert.All(torches, t => Assert.True(Math.Abs(t.Origin!.Value.X - m) <= 512 && Math.Abs(t.Origin!.Value.Y - m) <= 512, $"a torch at ({t.Origin})"));
        Assert.All(torches, t => Assert.True(Math.Abs(t.Origin!.Value.X - m) <= 224 || t.Origin!.Value.X < m, $"a torch on a section that breaks away ({t.Origin})"));
    }

    [Theory]
    [MemberData(nameof(PitSeeds))]
    public void No_dressing_stands_on_or_at_the_edge_of_a_platform_section_that_breaks_away(int depth, ulong seed)
    {
        // Brief p15: platform 3's torches stood at the core's edge by it and floated in game once it dropped. Every
        // piece of dressing (lamps, props, set brushes, breakables) keeps PlatformEdge off each section's box.
        var (built, dressed, theme) = Arena(depth, seed);
        var sections = PlatformSections(dressed);

        var on = OnSections(built, dressed, theme, sections);

        Assert.Equal(3, sections.Count);
        Assert.True(on.Count == 0, string.Join("\n", on));
    }

    [Fact]
    public void A_torch_stood_by_a_sections_edge_is_on_it()
    {
        var (built, dressed, theme) = Arena(15);
        var sections = PlatformSections(dressed);
        var torch = dressed.Entities.First(e => e.Key("model") == Bosses.Torch.Model);
        Rect north = sections.Single(s => s.Name == "descent_boss_platform_3").Box;
        var moved = torch with { Origin = new Point((north.X0 + north.X1) / 2, north.Y0 - 24, torch.Origin!.Value.Z) };

        Assert.Empty(OnSections(built, dressed, theme, sections));
        Assert.NotEmpty(OnSections(built, dressed with { Entities = [.. dressed.Entities.Select(e => ReferenceEquals(e, torch) ? moved : e)] }, theme, sections));
    }

    [Theory]
    [MemberData(nameof(BossDepths))]
    public void Every_room_of_a_boss_levels_library_has_its_preview_camera_in_open_air(int depth)
    {
        // Brief p15: two of the arenas' views stood inside a bench and a stepping pillar.
        var inside = libs.Dressed(depth, 0).Select(r => (r.Dressed.Name, PreviewCamera.Of(r.Dressed).At, Solids: new RoomSolids(r.Dressed)))
            .Where(c => !PreviewCamera.InOpenAir(c.Solids, c.At)).Select(c => $"{c.Name}: ({c.At})").ToArray();

        Assert.Empty(inside);
    }

    public static TheoryData<int, ulong> PitSeeds => new() { { 15, 0 }, { 15, 1 }, { 15, 2 }, { 15, 3 } };

    private static IReadOnlyList<(string Name, Rect Box)> PlatformSections(Room arena) =>
        [.. arena.Entities.Where(e => e.Key("targetname") is { } n && n.StartsWith(BossNames.PlatformPrefix, StringComparison.Ordinal))
            .Select(e => (e.Key("targetname")!, new Rect(e.Brushes.Min(b => b.Mins.X), e.Brushes.Min(b => b.Mins.Y), e.Brushes.Max(b => b.Maxs.X), e.Brushes.Max(b => b.Maxs.Y))))];

    private static List<string> OnSections(Room built, Room dressed, RoomTheme theme, IReadOnlyList<(string Name, Rect Box)> sections) =>
        [.. from p in DressLint.Pieces(built, dressed, DressLint.Models(theme))
            from s in sections
            where p.Box.Mins.X < s.Box.X1 + BossNames.PlatformEdge && p.Box.Maxs.X > s.Box.X0 - BossNames.PlatformEdge
                && p.Box.Mins.Y < s.Box.Y1 + BossNames.PlatformEdge && p.Box.Maxs.Y > s.Box.Y0 - BossNames.PlatformEdge
            select $"{p.What} within {BossNames.PlatformEdge} of {s.Name}"];

    //-------------------------------------------------------------------------
    // ARNA: library.json and the level's .arena.
    //-------------------------------------------------------------------------

    [Fact]
    public void Library_json_carries_the_arenas_data_and_no_other_room_does()
    {
        using var doc = JsonDocument.Parse(LibraryManifestWriter.Write("t", libs.Built(10)));
        var rooms = doc.RootElement.GetProperty("rooms").EnumerateObject().ToArray();
        var withArena = rooms.Where(r => r.Value.TryGetProperty("arena", out _)).ToArray();

        var arena = Assert.Single(withArena);
        Assert.StartsWith("arena_", arena.Name, StringComparison.Ordinal);
        Assert.Equal(ArenaShape.BoreTrack.Count, arena.Value.GetProperty("arena").GetProperty("track").GetArrayLength());
        Assert.Equal(4, arena.Value.GetProperty("arena").GetProperty("boss").GetArrayLength());
        Assert.Equal(3, arena.Value.GetProperty("arena").GetProperty("track")[ArenaShape.BoreTrack.Count - 1][3].GetInt32());
    }

    [Fact]
    public void The_arena_file_turns_the_arena_into_the_levels_frame()
    {
        string manifest = LibraryManifestWriter.Write("t", libs.Built(10));
        string name = libs.Built(10).Single(r => r.Entry.Tags.Contains("boss_arena")).Name;
        Room room = libs.Built(10).Single(r => r.Name == name);

        using var flat = JsonDocument.Parse(DescentLevel.ArenaFile(Plan(name, 0), manifest));
        using var turned = JsonDocument.Parse(DescentLevel.ArenaFile(Plan(name, 90), manifest));
        float[] Boss(JsonDocument d) => [.. d.RootElement.GetProperty("boss").EnumerateArray().Select(v => v.GetSingle())];

        // Placed at column 2, row 1, unturned: the room's point moved by the cells.
        Assert.Equal([room.Arena!.Boss.X + 2048, room.Arena.Boss.Y + 1024, room.Arena.Boss.Z, room.Arena.BossYaw], Boss(flat));
        // Turned a quarter: about the footprint's middle, its yaw turned with it.
        float m = room.Extent / 2f;
        Assert.Equal(2048 + m - (room.Arena.Boss.Y - m), Boss(turned)[0], 0.01f);
        Assert.Equal(1024 + m + (room.Arena.Boss.X - m), Boss(turned)[1], 0.01f);
        Assert.Equal((room.Arena.BossYaw + 90) % 360, Boss(turned)[3]);
        Assert.Equal(room.Arena.Track.Count, flat.RootElement.GetProperty("track").GetArrayLength());
    }

    [Fact]
    public void A_level_without_an_arena_writes_an_empty_arena_file()
    {
        string manifest = LibraryManifestWriter.Write("t", libs.Built(10));
        string drift = libs.Built(10).First(r => !r.Entry.Tags.Contains("boss_arena") && r.Cells == 1).Name;

        Assert.Equal("{}\n", DescentLevel.ArenaFile(Plan(drift, 0), manifest));
    }

    private static LevelPlan Plan(string room, int rotation) => new(8, 8, [new Placement(room, 1, 2, rotation, "")], "", "");
}

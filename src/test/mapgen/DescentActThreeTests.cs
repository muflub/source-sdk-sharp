using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Dressing;
using SourceSharp.MapGen.Descent.Themes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Act III's four themes (plan_descent_mine_levels.md §7, brief p14): each level differs from the
/// themed level above it (level 11 from level 9, over the Bore) in wall, floor, lamp and light colour,
/// closes its fog in and frames its doors in brushes of its own; level 11's lamps have half failed and
/// its emergency lamps light what they leave dark; slime lies at 12 and 14 and lava at 13, in the
/// pools, the chasms' gaps and the floor of their rooms that the way between the doors does not need,
/// never the only way between two doors, under a volume of its kind with its own glow over it.
/// Single rooms are built and dressed here, never a whole depth, so the class stays quick.
/// </summary>
public sealed class DescentActThreeTests
{
    public static TheoryData<int> Levels => [11, 12, 13, 14];

    private static RoomTheme Level(int depth) => DescentRooms.ThemeFor(depth);

    /// <summary>The themed level above: level 10 is the Bore's row, so level 11 is held to level 9.</summary>
    private static RoomTheme Above(int depth) => Level(depth == 11 ? 9 : depth - 1);

    private static Room Built(int depth, string stem) =>
        RoomCatalogue.Named(RoomCatalogue.Rooms(RoomCatalogue.Mine, DescentRooms.KitFor(depth), Level(depth), 0), stem);

    private static Room Dressed(int depth, string stem) => RoomDresser.Dress(Built(depth, stem), Level(depth), 0, Breakables.CapAt(depth));

    /// <summary>Every room of <paramref name="family"/> at the depth, dressed.</summary>
    private static Room[] Family(int depth, string family) =>
        [.. RoomCatalogue.Rooms(RoomCatalogue.Mine, DescentRooms.KitFor(depth), Level(depth), 0)
            .Where(r => r.Role == RoomRole.None && RoomDresser.Family(r.Name) == family)
            .Select(r => RoomDresser.Dress(r, Level(depth), 0, Breakables.CapAt(depth)))];

    private static IEnumerable<(string Kind, Point Mins, Point Maxs)> Hazards(Room room) => RoomDresser.HazardVolumes(room);

    [Theory]
    [MemberData(nameof(Levels))]
    public void An_act_three_level_is_themed(int depth)
    {
        Assert.Same(ActThree.All[depth - 11], Level(depth));
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void An_act_three_level_has_another_wall_than_the_themed_level_above(int depth)
    {
        Assert.NotEqual(Above(depth).Wall.Material, Level(depth).Wall.Material);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void An_act_three_level_has_another_floor_than_the_themed_level_above(int depth)
    {
        Assert.NotEqual(Above(depth).Floor.Material, Level(depth).Floor.Material);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void An_act_three_level_has_another_lamp_than_the_themed_level_above(int depth)
    {
        Assert.NotEqual(Above(depth).Lamps!.Fixture.Model, Level(depth).Lamps!.Fixture.Model);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void An_act_three_level_has_another_light_colour_than_the_themed_level_above(int depth)
    {
        Assert.NotEqual(Above(depth).Lamps!.Light.Colour, Level(depth).Lamps!.Light.Colour);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void An_act_three_levels_fog_closes_in_on_the_themed_level_above(int depth)
    {
        Assert.True(Level(depth).Fog.End < Above(depth).Fog.End);
    }

    [Fact]
    public void The_pits_fog_closes_in_on_the_rot()
    {
        Assert.True(Bosses.Pit.Fog.End < Level(14).Fog.End);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void An_act_three_level_has_its_own_fog_colour(int depth)
    {
        Assert.NotEqual(Above(depth).Fog.Colour, Level(depth).Fog.Colour);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void An_act_three_level_frames_its_doors_in_brushes_of_its_own(int depth)
    {
        Assert.NotEqual(Above(depth).FrameBrushes?.Post.Material, Level(depth).FrameBrushes!.Post.Material);
    }

    [Theory]
    [InlineData(12, "slime")]
    [InlineData(13, "lava")]
    [InlineData(14, "slime")]
    public void An_act_three_levels_floors_hold_its_hazard_a_kind_of_hazards_json(int depth, string kind)
    {
        Assert.Equal(kind, Level(depth).HazardFloor!.Kind);
        Assert.Contains(kind, HazardKinds());
    }

    [Fact]
    public void The_robot_works_hold_no_hazard()
    {
        Assert.Null(Level(11).HazardFloor);
    }

    [Fact]
    public void The_rot_stands_its_slime_as_a_liquid_and_lava_lies_as_a_skin()
    {
        Assert.True(Level(14).HazardFloor!.Liquid);
        Assert.False(Level(13).HazardFloor!.Liquid);
    }

    [Fact]
    public void The_sumps_slime_lies_as_a_lit_skin_of_slime_with_a_surface()
    {
        // Brief p15: as a water its fog drew a flat luminous sheet with no surface.
        HazardFloor slime = Level(12).HazardFloor!;

        Assert.False(slime.Liquid);
        Assert.Equal(ActThree.SumpScum.Name, slime.Surface);
        Assert.Equal("nature/toxicslime002a_dx70", Level(12).AssetOf(slime.Surface));
        Assert.InRange(slime.MinLight, 0.01f, 0.3f);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void Act_threes_libraries_take_its_theme_and_the_normal_kit(int depth)
    {
        Assert.Equal(RoomKit.Normal.Cell, DescentRooms.KitFor(depth).Cell);
        Assert.Same(Level(depth), DescentRooms.ThemeFor(depth));
    }

    //-------------------------------------------------------------------------
    // Level 11: the lamps that failed.
    //-------------------------------------------------------------------------

    [Fact]
    public void Half_the_works_lamps_hang_dark_with_no_light()
    {
        Room room = Dressed(11, "gallery_d");
        var lamps = room.Entities.Where(e => e.Key(RoomDresser.RoleKey) == nameof(RoomDresser.Role.Lamp) && e.Key("model") == ActThree.FactoryLamp.Model).ToArray();
        var dead = lamps.Where(e => e.Key(RoomDresser.DeadKey) == "1").ToArray();
        var lights = room.Entities.Where(e => e.ClassName is "light" or "light_spot").Select(e => e.Origin!.Value).ToArray();

        Assert.NotEmpty(dead);
        Assert.True(dead.Length < lamps.Length, $"{dead.Length} of {lamps.Length} dead");
        Assert.All(dead, d => Assert.DoesNotContain(lights, l => Math.Abs(l.X - d.Origin!.Value.X) < 1 && Math.Abs(l.Y - d.Origin!.Value.Y - ActThree.Works.Lamps!.LightAt.Y) < 32));
    }

    [Fact]
    public void The_works_red_emergency_lamps_light_what_the_dead_ones_leave_dark()
    {
        Room room = Dressed(11, "gallery_d");

        Assert.Contains(room.Entities, e => e.Key("model") == Bosses.Siren.Model);
        Assert.Empty(RoomLint.CheckLit(room, new RoomColumns(room)));
    }

    [Fact]
    public void A_works_room_with_no_lamp_failed_takes_no_emergency_lamp_it_does_not_need()
    {
        // The control: every lamp alive, the emergency lamps only where the ceiling lamps never reached.
        RoomTheme alive = ActThree.Works with { Lamps = ActThree.Works.Lamps! with { Dead = 0 }, Pendants = ActThree.Works.Pendants! with { Dead = 0 } };
        Room room = RoomDresser.Dress(RoomCatalogue.Named(RoomCatalogue.Rooms(RoomCatalogue.Mine, DescentRooms.KitFor(11), alive, 0), "gallery_d"), alive, 0);
        Room failed = Dressed(11, "gallery_d");

        Assert.DoesNotContain(room.Entities, e => e.Key(RoomDresser.DeadKey) is not null);
        Assert.True(room.Entities.Count(e => e.Key("model") == Bosses.Siren.Model) < failed.Entities.Count(e => e.Key("model") == Bosses.Siren.Model));
    }

    [Fact]
    public void The_dress_lint_lets_a_failed_lamp_hang_dark_but_refuses_a_live_one_with_no_light()
    {
        Room built = Built(11, "gallery_d");
        Room dressed = RoomDresser.Dress(built, Level(11), 0, Breakables.CapAt(11));
        RoomEntity dead = dressed.Entities.First(e => e.Key(RoomDresser.DeadKey) == "1");
        Room undead = dressed with { Entities = [.. dressed.Entities.Select(e => ReferenceEquals(e, dead) ? e with { Keys = [.. e.Keys.Where(k => k.Key != RoomDresser.DeadKey)] } : e)] };

        Assert.DoesNotContain(DressLint.Check(built, dressed, Level(11), Breakables.CapAt(11)), f => f.Rule == DressLint.LampLight);
        Assert.Contains(DressLint.Check(built, undead, Level(11), Breakables.CapAt(11)), f => f.Rule == DressLint.LampLight);
    }

    //-------------------------------------------------------------------------
    // Level 14: a few glows.
    //-------------------------------------------------------------------------

    [Fact]
    public void The_rots_fungi_grow_in_a_caves_middle_where_no_walls_reach()
    {
        RoomTheme walls = Level(14) with { Lamps = Level(14).Lamps! with { FreeStanding = false } };
        Room built = Built(14, "grotto_a");
        Room room = RoomDresser.Dress(built, Level(14), 0, Breakables.CapAt(14));
        Room byWalls = RoomDresser.Dress(RoomCatalogue.Named(RoomCatalogue.Rooms(RoomCatalogue.Mine, DescentRooms.KitFor(14), walls, 0), "grotto_a"), walls, 0);

        Assert.Empty(RoomLint.CheckLit(room, new RoomColumns(room)));
        Assert.NotEmpty(RoomLint.CheckLit(byWalls, new RoomColumns(byWalls)));
    }

    //-------------------------------------------------------------------------
    // Slime and lava.
    //-------------------------------------------------------------------------

    [Theory]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void An_act_three_pools_basin_holds_its_hazard_under_one_volume(int depth)
    {
        Room pool = Built(depth, "pool_b");

        var (kind, _, _) = Assert.Single(Hazards(pool));
        Assert.Equal(Level(depth).HazardFloor!.Kind, kind);
    }

    [Fact]
    public void Brimstones_pool_lays_lava_as_a_skin_and_stands_no_water()
    {
        Room pool = Built(13, "pool_b");

        Assert.Empty(pool.Water);
        Assert.Contains(pool.Entities, e => e.ClassName == "func_brush" && e.Brushes.Any(b => b.Material == "nature/underworld_lava002"));
    }

    [Fact]
    public void Brimstones_chasm_holds_lava_in_its_leaps_gap()
    {
        Room chasm = Built(13, "chasm_b");
        var (kind, mins, maxs) = Assert.Single(Hazards(chasm));
        // chasm_b's leap: decks from x 160 along the north, the gap after the first deck, the bridge on its far side.
        int deck = 160, gap = JumpTable.Of(JumpTier.S2).Gap(0.5f);

        Assert.Equal("lava", kind);
        Assert.True(mins.Y >= 688 && maxs.Y <= 880 && maxs.X - mins.X >= 400, $"({mins})-({maxs})");
        Assert.True(mins.X >= 160 + deck - 1 && maxs.X <= 160 + deck + JumpTable.Of(JumpTier.S2).GapMax + 1, $"({mins})-({maxs}) for a gap of about {gap}");
    }

    [Fact]
    public void A_chasm_above_brimstone_holds_no_hazard_in_its_gap()
    {
        Assert.Empty(Hazards(Built(12, "chasm_b")));
    }

    [Theory]
    [InlineData(12, "pit")]
    [InlineData(13, "cavern")]
    [InlineData(14, "cavern")]
    public void A_hazard_floor_lies_where_the_way_between_the_doors_does_not_go(int depth, string family)
    {
        Room[] rooms = Family(depth, family);

        Assert.Contains(rooms, r => Hazards(r).Any());
        Assert.All(rooms, r => Assert.Empty(RoomLint.CheckHazardPath(r, new RoomColumns(r))));
    }

    [Theory]
    [InlineData(12, "pit")]
    [InlineData(13, "cavern")]
    public void A_room_of_a_family_the_theme_leaves_dry_holds_no_hazard_floor(int depth, string family)
    {
        Room wet = Family(depth, family).First(r => Hazards(r).Any());
        RoomTheme dry = Level(depth) with { HazardFloor = Level(depth).HazardFloor! with { Families = new HashSet<string>(StringComparer.Ordinal) } };
        Room room = RoomDresser.Dress(RoomCatalogue.Named(RoomCatalogue.Rooms(RoomCatalogue.Mine, DescentRooms.KitFor(depth), dry, 0), wet.Name.Split('-')[0]), dry, 0);

        Assert.Empty(Hazards(room));
    }

    [Theory]
    [InlineData(12, "pit")]
    [InlineData(13, "cavern")]
    [InlineData(13, "pool")]
    public void Every_hazard_volume_has_its_glow_over_it(int depth, string family)
    {
        Room room = Family(depth, family).First(r => Hazards(r).Any());
        var glows = room.Entities.Where(e => e.ClassName == "light" && e.Key("_light")!.StartsWith(Level(depth).HazardFloor!.Glow.Colour + " ", StringComparison.Ordinal))
            .Select(e => e.Origin!.Value).ToArray();

        Assert.All(Hazards(room), v => Assert.Contains(glows, g => g.X >= v.Mins.X && g.X <= v.Maxs.X && g.Y >= v.Mins.Y && g.Y <= v.Maxs.Y));
    }

    [Theory]
    [InlineData(12, "pit")]
    [InlineData(13, "cavern")]
    [InlineData(13, "stope")]
    [InlineData(14, "cavern")]
    public void Nothing_stands_on_a_hazard_floor(int depth, string family)
    {
        Room room = Family(depth, family).First(r => Hazards(r).Any());
        var floor = Hazards(room).Select(v => new Rect(v.Mins.X, v.Mins.Y, v.Maxs.X, v.Maxs.Y)).ToArray();
        var pieces = DressLint.Pieces(Built(depth, room.Name.Split('-')[0]), room, DressLint.Models(Level(depth)))
            .Where(p => p.Entity is not null && p.Box.Mins.Z <= room.Kit.Sill + RoomLint.Step).ToArray();

        Assert.NotEmpty(pieces);
        Assert.DoesNotContain(pieces, p => floor.Any(a => p.Box.Mins.X < a.X1 && p.Box.Maxs.X > a.X0 && p.Box.Mins.Y < a.Y1 && p.Box.Maxs.Y > a.Y0));
    }

    [Fact]
    public void A_hazard_floor_keeps_off_every_pack_spawn()
    {
        Room room = Family(12, "pit").First(r => Hazards(r).Any());
        var packs = room.Markers.Where(m => m.Kind == MarkerKind.PackSpawn).Select(m => m.At).ToArray();

        Assert.All(Hazards(room), v => Assert.DoesNotContain(packs, p => p.X > v.Mins.X - RoomDresser.HazardFromMarker + 16 && p.X < v.Maxs.X + RoomDresser.HazardFromMarker - 16
                                                                            && p.Y > v.Mins.Y - RoomDresser.HazardFromMarker + 16 && p.Y < v.Maxs.Y + RoomDresser.HazardFromMarker - 16));
    }

    //-------------------------------------------------------------------------
    // Breakables (M34).
    //-------------------------------------------------------------------------

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    public void The_works_and_the_sump_turn_their_crates_and_drums_into_breakables(int depth)
    {
        Assert.Contains(Level(depth).Props.Models, m => m.Breakable is not null);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(14)]
    public void Brimstone_and_the_rot_hold_no_breakable(int depth)
    {
        Assert.DoesNotContain(Level(depth).Props.Models, m => m.Breakable is not null);
    }

    private static IReadOnlyList<string> HazardKinds()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(RepoLayout.Repo(["src", "tf2_descent", "rules", "Content", "hazards.json"])));
        return [.. doc.RootElement.GetProperty("kinds").EnumerateArray().Select(k => k.GetProperty("id").GetString()!)];
    }
}

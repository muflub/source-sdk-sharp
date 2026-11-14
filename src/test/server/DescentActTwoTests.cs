using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Dressing;
using SourceSharp.MapGen.Descent.Shapes;
using SourceSharp.MapGen.Descent.Themes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Act II's four themes (plan_descent_mine_levels.md §7, brief p4): each level differs from the
/// themed level above it (level 6 from level 4, over the boss level) in wall, floor, lamp and wall
/// clutter, is wetter and closer in its fog, and has its own door frame; level 6's sets are broken,
/// level 7 hangs stalactites, 7 and 8 stand water on their floors. Single rooms are built and
/// dressed here, never a whole depth, so the class stays quick.
/// </summary>
public sealed class DescentActTwoTests
{
    public static TheoryData<int> Levels => [6, 7, 8, 9];

    private static RoomTheme Level(int depth) => DescentRooms.ThemeFor(depth);

    /// <summary>The themed level above: level 5 is the boss's row, still grey, so level 6 is held to level 4.</summary>
    private static RoomTheme Above(int depth) => Level(depth == 6 ? 4 : depth - 1);

    [Theory]
    [MemberData(nameof(Levels))]
    public void An_act_two_level_is_themed(int depth)
    {
        Assert.Same(SourceSharp.MapGen.Descent.Themes.ActTwo.All[depth - 6], Level(depth));
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void An_act_two_level_has_another_wall_than_the_themed_level_above(int depth)
    {
        Assert.NotEqual(Above(depth).Wall.Material, Level(depth).Wall.Material);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void An_act_two_level_has_another_floor_than_the_themed_level_above(int depth)
    {
        Assert.NotEqual(Above(depth).Floor.Material, Level(depth).Floor.Material);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void An_act_two_level_has_another_lamp_than_the_themed_level_above(int depth)
    {
        Assert.NotEqual(Above(depth).Lamps!.Fixture.Model, Level(depth).Lamps!.Fixture.Model);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void An_act_two_level_has_another_light_colour_than_the_themed_level_above(int depth)
    {
        Assert.NotEqual(Above(depth).Lamps!.Light.Colour, Level(depth).Lamps!.Light.Colour);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void An_act_two_level_shares_no_wall_clutter_with_the_level_above(int depth)
    {
        var above = Above(depth).Props.Wall.Concat(Above(depth).Props.Hung.Select(h => h.Model)).Select(m => m.Model).ToHashSet();

        Assert.DoesNotContain(Level(depth).Props.Wall.Concat(Level(depth).Props.Hung.Select(h => h.Model)), m => above.Contains(m.Model));
    }

    [Fact]
    public void The_breakthrough_leaves_level_fours_kit_behind()
    {
        // Brief p4: half the rooms mine, level 4's kit, wrecked.
        var four = ActOne.Mill.Props.Models.Select(m => m.Model).ToHashSet();

        Assert.Contains(Level(6).Props.Models, m => four.Contains(m.Model));
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void An_act_two_level_is_wetter_than_the_themed_level_above(int depth)
    {
        Assert.True(Level(depth).Wetness > Above(depth).Wetness);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void An_act_two_levels_fog_closes_in_on_the_themed_level_above(int depth)
    {
        Assert.True(Level(depth).Fog.End < Above(depth).Fog.End);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void An_act_two_level_has_its_own_fog_colour(int depth)
    {
        Assert.NotEqual(Above(depth).Fog.Colour, Level(depth).Fog.Colour);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void An_act_two_level_frames_its_doors_in_brushes_of_its_own(int depth)
    {
        BrushFrame mine = Level(depth).FrameBrushes!;

        Assert.NotEqual(Above(depth).FrameBrushes?.Post.Material, mine.Post.Material);
    }

    [Fact]
    public void The_breakthroughs_door_frame_is_a_broken_set()
    {
        Assert.True(Level(6).FrameBrushes!.Sag > 0);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(9)]
    public void The_cave_and_the_chapel_frame_their_doors_as_arches(int depth)
    {
        Assert.True(Level(depth).FrameBrushes!.Fillet > 0);
    }

    [Fact]
    public void The_chapels_arch_has_a_keystone_and_plinths()
    {
        Assert.True(Level(9).FrameBrushes!.Keystone > 0 && Level(9).FrameBrushes!.Plinth > 0);
    }

    [Fact]
    public void The_sunken_galleries_flood_door_has_steel_on_its_sill()
    {
        Assert.Equal(Level(8).FrameBrushes!.Post.Material, Level(8).FrameBrushes!.Crossing?.Material);
    }

    private static Room Built(int depth, ShapeVariants family, string name) =>
        RoomCatalogue.Named(RoomCatalogue.Rooms([new RoomShape(name.Split('_')[0], family)], DescentRooms.KitFor(depth), Level(depth), 0), name);

    private static RoomEntity FrameOf(Room room) => room.Entities.First(e => e.ClassName == "func_brush" && (e.Key("room_needs") ?? "").StartsWith("joined_", StringComparison.Ordinal));

    [Fact]
    public void A_broken_sets_head_sags_at_the_break()
    {
        Room drift = Built(6, DriftShape.Variants, "drift_a");
        var heads = FrameOf(drift).Brushes.Where(b => b.Mins.Z > drift.Kit.Sill + 100 && b.Maxs.Z <= drift.Kit.Head + 1).Select(b => b.Maxs.Z).Distinct().ToArray();

        Assert.Equal(2, heads.Length);
        Assert.Equal(Level(6).FrameBrushes!.Sag, heads.Max() - heads.Min());
    }

    [Theory]
    [InlineData(7)]
    [InlineData(9)]
    public void An_arch_has_a_haunch_in_each_top_corner_of_its_opening(int depth)
    {
        Room drift = Built(depth, DriftShape.Variants, "drift_a");

        Assert.Equal(2, FrameOf(drift).Brushes.Count(b => !b.IsBox));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(9)]
    public void An_arch_keeps_a_giants_way_through_its_door(int depth)
    {
        Room drift = Built(depth, DriftShape.Variants, "drift_a");

        Assert.Empty(RoomLint.CheckFrameClear(drift));
    }

    [Fact]
    public void Some_of_the_breakthroughs_sets_have_fallen_in()
    {
        // Every drift's sets at a few seeds: some stand as a lone post with no cap over it.
        var walls = Enumerable.Range(0, 4).SelectMany(seed => DriftShape.Variants(DescentRooms.KitFor(6), Level(6), (ulong)seed)
            .Select(r => (Built: r, Dressed: RoomDresser.Dress(r with { Name = LayoutNames.Name(r, "drift") }, Level(6), (ulong)seed)))).ToArray();
        string trim = Level(6).TrimOrBlock.Material;
        int caps = 0, posts = 0;
        foreach (var (built, dressed) in walls)
        {
            var sets = dressed.World.Except(built.World).Where(b => b.Material == trim).ToArray();
            caps += sets.Count(b => b.Maxs.Z >= built.Height - built.Kit.WallDepth - 1 && b.Mins.Z > built.Kit.Sill);
            posts += sets.Count(b => b.Mins.Z == built.Kit.Sill);
        }

        Assert.True(caps > 0, "no set stands whole");
        Assert.True(posts > 2 * caps, $"{posts} posts under {caps} caps: none has fallen in");
    }

    [Fact]
    public void A_flooded_levels_drift_stands_water_on_its_floor_clear_of_its_doors()
    {
        Room built = Built(8, DriftShape.Variants, "drift_a");
        Room dressed = RoomDresser.Dress(built, Level(8), 0);
        var doors = dressed.Sockets.Select(s => Box.DoorOpening(dressed.Kit, s.Face, s.Offset, dressed.Cells)).ToArray();

        Assert.NotEmpty(dressed.Water);
        Assert.All(dressed.Water, w => Assert.All(doors, d => Assert.True(d.DistanceTo(new Box(w.Mins, w.Maxs)) >= Level(8).Standing!.FromDoors - 0.5f)));
        Assert.All(dressed.Water, w => Assert.Equal(dressed.Kit.Sill + Level(8).Standing!.Depth, w.Maxs.Z));
    }

    [Fact]
    public void A_dripstone_drift_stands_no_water_since_only_its_caves_hold_pools()
    {
        Room dressed = RoomDresser.Dress(Built(7, DriftShape.Variants, "drift_a"), Level(7), 0);

        Assert.Empty(dressed.Water);
    }

    [Fact]
    public void A_dripstone_cavern_holds_a_pool_of_the_levels_water()
    {
        Room dressed = RoomDresser.Dress(Built(7, CavernShape.Variants, "cavern_b"), Level(7), 0);

        Assert.NotEmpty(dressed.Water);
        Assert.All(dressed.Water, w => Assert.Equal(Level(7).Water, w.Material));
    }

    [Fact]
    public void A_flooded_levels_pool_is_drawn_in_its_murky_water()
    {
        Room pool = Built(8, PoolShape.Variants, "pool_a");

        Assert.All(pool.Water, w => Assert.Equal(Level(8).Water, w.Material));
    }

    [Fact]
    public void A_dripstone_cave_hangs_stalactites_from_its_roof_clear_of_a_players_head()
    {
        Room built = Built(7, CavernShape.Variants, "cavern_d");
        Room dressed = RoomDresser.Dress(built, Level(7), 0);
        var hung = dressed.Entities.Where(e => e.Key(RoomDresser.RoleKey) == nameof(RoomDresser.Role.Hanging)).ToArray();

        Assert.NotEmpty(hung);
        Assert.All(hung, e => Assert.EndsWith(" 180", e.Key("angles") ?? "", StringComparison.Ordinal));
        Assert.All(hung, e =>
        {
            PropModel m = Level(7).Props.Hanging.Single(h => h.Model == e.Key("model"));
            Assert.True(e.Origin!.Value.Z - m.Maxs.Z >= built.Kit.Sill + RoomDresser.HangingClear - 0.5f);
        });
    }

    [Fact]
    public void A_lamp_post_leaves_no_sliver_to_the_clutter_beside_it()
    {
        // Depth 8 seed 1 (round 2): a hand pump set 30 from a lamp post walled a two-node trap against the wall.
        var models = DressLint.Models(Level(8));
        var rooms = RoomCatalogue.Rooms([new RoomShape("yard", YardShape.Variants)], DescentRooms.KitFor(8), Level(8), 0);
        int posts = 0;
        foreach (Room built in rooms)
        {
            var pieces = DressLint.Pieces(built, RoomDresser.Dress(built, Level(8), 0), models);
            var standing = pieces.Where(p => p.Role == RoomDresser.Role.Lamp && p.Solid && p.Box.Mins.Z <= built.Kit.Sill + 1).ToArray();
            var clutter = pieces.Where(p => p.Solid && p.Role is RoomDresser.Role.Wall or RoomDresser.Role.Corner or RoomDresser.Role.Floor).ToArray();
            posts += standing.Length;
            Assert.DoesNotContain(standing, l => clutter.Any(c => c.Box.DistanceTo(l.Box) is > 6 and < 56));
        }

        Assert.True(posts > 0, "no lamp post stood in any room");
    }
}

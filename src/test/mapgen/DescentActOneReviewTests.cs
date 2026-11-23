using System.Globalization;
using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Dressing;
using SourceSharp.MapGen.Descent.Themes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What the review of Act I's previews asked of its four levels
/// (2026-11-10), one fact a request: lantern pools hung from sets or set on
/// crates at level 3, a cold work light among level 4's sodium lamps, work
/// floods at level 1's shaft foot, track on each door's axis at level 2,
/// trim bands and pilasters on tall walls, a second material low on a tall
/// wall, clutter in groups, machines sized to their room.
/// </summary>
public sealed class DescentActOneReviewTests(DescentLibraries act) : IClassFixture<DescentLibraries>
{
    private static IEnumerable<RoomEntity> Role(Room r, RoomDresser.Role role) => r.Entities.Where(e => e.Key(RoomDresser.RoleKey) == role.ToString());

    private static Point Origin(RoomEntity e) => e.Origin!.Value;

    private static Box BoxOf(RoomEntity e, PropModel m)
    {
        int yaw = (int)float.Parse((e.Key("angles") ?? "0 0 0").Split(' ')[1], CultureInfo.InvariantCulture);
        var (mins, maxs) = m.Turned(yaw);
        return new Box(Origin(e) + mins, Origin(e) + maxs);
    }

    private IEnumerable<Box> Lanterns(Room r) =>
        Role(r, RoomDresser.Role.Lamp).Where(e => e.Key("model") == ActOne.Lantern.Model).Select(e => BoxOf(e, ActOne.Lantern));

    [Fact]
    public void Level_three_hangs_lanterns_from_its_timber_sets()
    {
        // A lantern whose top is under a set brush's underside: hung from its cap.
        var hung = act.Of(3).Where(r => Lanterns(r.Dressed).Any(l => r.Dressed.World.Skip(r.Built.World.Count)
            .Any(b => Math.Abs(b.Mins.Z - l.Maxs.Z) <= 1 && b.Mins.X <= l.Maxs.X && b.Maxs.X >= l.Mins.X && b.Mins.Y <= l.Maxs.Y && b.Maxs.Y >= l.Mins.Y)));

        Assert.NotEmpty(hung);
    }

    [Fact]
    public void Level_three_stands_lanterns_on_crates()
    {
        var stood = act.Of(3).Where(r => Lanterns(r.Dressed).Any(l => Role(r.Dressed, RoomDresser.Role.Lamp)
            .Where(e => e.Key("model") == ActOne.PowderCrate.Model).Select(e => BoxOf(e, ActOne.PowderCrate))
            .Any(c => Math.Abs(c.Maxs.Z - l.Mins.Z) <= 1)));

        Assert.NotEmpty(stood);
    }

    [Fact]
    public void No_lantern_of_level_three_is_set_down_on_the_floor()
    {
        var onFloor = act.Of(3).SelectMany(r => Lanterns(r.Dressed).Where(l => l.Mins.Z <= r.Dressed.Kit.Sill + 1).Select(_ => r.Dressed.Name));

        Assert.Empty(onFloor);
    }

    [Fact]
    public void Level_three_lights_its_rooms_with_fewer_lanterns_than_a_ring()
    {
        // Pools, not an even wash: no room carries more than the theme's budget, and the level's
        // lanterns are under four a room on average (the ring stood ten round a chamber).
        var perRoom = act.Of(3).Select(r => Lanterns(r.Dressed).Count()).ToArray();

        Assert.True(perRoom.Average() < 4, $"{perRoom.Average():0.0} lanterns a room");
    }

    private static bool White(RoomEntity light) =>
        light.Key("_light") is { } v && v.Split(' ') is [var r, var g, var b, ..] && int.Parse(b, CultureInfo.InvariantCulture) >= int.Parse(r, CultureInfo.InvariantCulture);

    [Fact]
    public void Every_large_room_of_level_four_has_a_cold_white_work_light()
    {
        WorkLamps work = ActOne.Mill.WorkLamps!;
        var large = act.Of(4).Where(r =>
        {
            var columns = new RoomColumns(r.Built);
            return RoomLint.WalkedFloor(r.Built, columns).Count * RoomLint.Grid * RoomLint.Grid >= work.MinFloor * 1.2f;
        }).ToArray();

        Assert.NotEmpty(large);
        Assert.All(large, r => Assert.Contains(r.Dressed.Entities, e => e.ClassName == "light_spot" && White(e)));
    }

    [Fact]
    public void Level_ones_way_in_is_lit_by_work_floods_at_the_shaft_foot()
    {
        var arrivals = act.Of(1).Where(r => r.Dressed.Role == RoomRole.Up).ToArray();

        Assert.NotEmpty(arrivals);
        Assert.All(arrivals, r => Assert.True(r.Dressed.Entities.Count(e => e.ClassName == "light_spot") >= 2, r.Dressed.Name));
    }

    [Fact]
    public void Level_twos_track_runs_to_every_door_of_a_room_with_track()
    {
        // A piece within a door's approach on its axis: the track lines up across joined doors.
        foreach (var (_, dressed, _) in act.Of(2))
        {
            var rails = Role(dressed, RoomDresser.Role.Rail).Select(Origin).ToArray();
            // A store or a stope carries a stub from one door only (DescentTrackTests).
            if (rails.Length < 3 || ActOne.Haulage.Props.StubFamilies.Contains(RoomDresser.Family(dressed.Name)))
                continue;
            float c = dressed.Kit.Cell;
            // Every socket on its own axis: a larger room's (the yard hall's) as a room of one cell's.
            foreach (var (face, offset) in dressed.Sockets)
            {
                Point door = dressed.DoorInside(face, offset, 0);
                float m = c / 2 + offset * c;
                bool met = rails.Any(p => face is CellFace.East or CellFace.West
                    ? Math.Abs(p.Y - m) < 1 && Math.Abs(p.X - door.X) < 160
                    : Math.Abs(p.X - m) < 1 && Math.Abs(p.Y - door.Y) < 160);
                if (!met && rails.Any(p => Math.Abs(p.Y - m) < 1 || Math.Abs(p.X - m) < 1))
                    Assert.Fail($"{dressed.Name}: no track reaches its {face} door on the door's axis");
            }
        }
    }

    [Fact]
    public void Level_two_puts_an_ore_cart_on_a_siding()
    {
        Assert.Contains(act.Of(2), r => Role(r.Dressed, RoomDresser.Role.Floor).Any(e => e.Key("model") == ActOne.OreCart.Model)
                                        && Role(r.Dressed, RoomDresser.Role.Rail).Count() > 2);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void A_tall_room_gets_a_beam_under_its_roof(int depth)
    {
        TrimBands bands = DescentRooms.ThemeFor(depth).Bands!;
        var tall = act.Of(depth).Where(r => r.Built.Height >= 1024).ToArray();

        Assert.NotEmpty(tall);
        Assert.Contains(tall, r => r.Dressed.World.Skip(r.Built.World.Count).Any(b => Math.Abs(b.Maxs.Z - (r.Built.Height - r.Built.Kit.WallDepth)) < 0.5f
                                                                                     && b.Maxs.Z - b.Mins.Z == bands.Beam));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void A_tall_room_gets_sets_up_its_walls(int depth)
    {
        float top(Room r) => r.Height - r.Kit.WallDepth;
        Assert.Contains(act.Of(depth), r => r.Built.Height >= 1024
            && r.Dressed.World.Skip(r.Built.World.Count).Count(b => b.Mins.Z == r.Built.Kit.Sill && b.Maxs.Z == top(r.Built)) >= 2);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void A_tall_wall_has_a_second_material_low_down(int depth)
    {
        RoomTheme theme = DescentRooms.ThemeFor(depth);

        Assert.Contains(act.Of(depth), r => r.Built.World.Any(b => b.Material == theme.WallLow!.Value.Material && b.Maxs.Z == r.Built.Kit.Sill + theme.WallBand));
    }

    [Fact]
    public void Level_fours_walls_are_not_its_lamps_yellow()
    {
        // The review: walls, floor, props and light all one mustard. The wall and the deck are grey
        // and blue-grey steel now (the palette's average colours), the floor black coal.
        RoomTheme mill = ActOne.Mill;

        Assert.Equal(("metal/wall021", "metal/imetal002", "nature/gravelcoal001"), (mill.Wall.Material, mill.Block.Material, mill.Floor.Material));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Clutter_stands_in_groups(int depth)
    {
        // A companion within a hand of another clutter piece.
        var companions = DescentRooms.ThemeFor(depth).Props.Companions.Select(m => m.Model).ToHashSet();
        var grouped = act.Of(depth).Where(r =>
        {
            var pieces = r.Dressed.Entities.Where(e => e.Key(RoomDresser.RoleKey) is "Wall" or "Corner" or "Floor").ToArray();
            return pieces.Any(a => companions.Contains(a.Key("model") ?? "")
                                   && pieces.Any(b => b != a && Math.Abs(Origin(a).X - Origin(b).X) + Math.Abs(Origin(a).Y - Origin(b).Y) < 128));
        });

        Assert.NotEmpty(grouped);
    }

    [Fact]
    public void A_machine_stands_only_in_a_room_with_the_floor_for_it()
    {
        PropModel generator = ActOne.Generator;
        float footprint = (generator.Maxs.X - generator.Mins.X) * (generator.Maxs.Y - generator.Mins.Y);
        var crowded = act.Of(4).Where(r => Role(r.Dressed, RoomDresser.Role.Floor).Any(e => e.Key("model") == generator.Model)
                                           && RoomLint.WalkedFloor(r.Built, new RoomColumns(r.Built)).Count * 256f * ActOne.Mill.Props.FloorShare < footprint);

        Assert.Empty(crowded.Select(r => r.Dressed.Name));
    }
}

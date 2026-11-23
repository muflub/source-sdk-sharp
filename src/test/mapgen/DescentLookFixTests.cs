using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Dressing;
using SourceSharp.MapGen.Descent.Themes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The second review of Act I's sheets (2026-11-10, brief p3b step 3), one fact
/// a request: furniture above level 1's skirting, square piers in level 2,
/// a grey seam in level 3's rock, level 4's hazard stripe only at door heads
/// and ledges, beams over the tall rooms.
/// </summary>
public sealed class DescentLookFixTests(DescentLibraries act) : IClassFixture<DescentLibraries>
{
    private static IEnumerable<RoomEntity> Role(Room r, RoomDresser.Role role) => r.Entities.Where(e => e.Key(RoomDresser.RoleKey) == role.ToString());

    [Fact]
    public void Level_one_hangs_furniture_on_its_walls_above_the_skirting()
    {
        var hung = act.Of(1).SelectMany(r => Role(r.Dressed, RoomDresser.Role.Hung).Select(e => (r.Dressed, e))).ToArray();

        Assert.True(hung.Length >= 5, $"{hung.Length} hung pieces on level 1");
        Assert.All(hung, h => Assert.True(h.e.Origin!.Value.Z >= h.Dressed.Kit.Sill + 96, $"{h.Dressed.Name}: {h.e.Key("model")} at {h.e.Origin}"));
    }

    [Fact]
    public void Level_two_builds_its_pillars_as_square_piers()
    {
        RoomTheme theme = DescentRooms.ThemeFor(2);
        var pillars = act.Of(2).Where(r => RoomDresser.Family(r.Built.Name) == "pillar").ToArray();

        Assert.NotEmpty(pillars);
        Assert.DoesNotContain(pillars.SelectMany(r => r.Built.World), b => b.Material == theme.Block.Material && b.Faces.Count > 6);
    }

    [Fact]
    public void Level_three_cuts_a_grey_seam_through_its_rock()
    {
        WallSeam seam = DescentRooms.ThemeFor(3).WallSeam!;

        Assert.Contains(act.Of(3).SelectMany(r => r.Built.World), b => b.Material == seam.Surface.Material);
    }

    [Fact]
    public void Level_fours_hazard_stripe_is_never_a_wall_band()
    {
        RoomTheme theme = DescentRooms.ThemeFor(4);
        string hazard = theme.Hazard!.Value.Material;
        var stripes = act.Of(4).SelectMany(r => r.Dressed.World.Where(b => b.Material == hazard).Select(b => (r.Dressed, b))).ToArray();

        Assert.Null(theme.Bands!.Surface);
        Assert.NotEmpty(stripes);
        // Over a door's head, or along the top of a deck or ledge: never at the wall's foot or its dado.
        Assert.All(stripes, s => Assert.True(s.b.Mins.Z >= s.Dressed.Kit.Sill + 20, $"{s.Dressed.Name}: a stripe from {s.b.Mins.Z}"));
    }

    [Fact]
    public void A_door_of_level_four_has_a_hazard_stripe_over_its_head()
    {
        string hazard = DescentRooms.ThemeFor(4).Hazard!.Value.Material;

        Assert.Contains(act.Of(4), r => r.Dressed.World.Any(b => b.Material == hazard && b.Mins.Z == r.Dressed.Kit.Head + 8));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void A_tall_room_has_beams_across_its_roof(int depth)
    {
        RoomTheme theme = DescentRooms.ThemeFor(depth);
        var tall = act.Of(depth).Where(r => r.Built.Height >= 768 && !RoomDresser.IsHallway(r.Built)).ToArray();

        Assert.NotEmpty(tall);
        Assert.Contains(tall, r => r.Dressed.World.Except(r.Built.World).Any(b =>
            b.Material == theme.TrimOrBlock.Material && b.Maxs.Z == r.Built.Height - r.Built.Kit.WallDepth
            && Math.Max(b.Maxs.X - b.Mins.X, b.Maxs.Y - b.Mins.Y) >= 256 && Math.Min(b.Maxs.X - b.Mins.X, b.Maxs.Y - b.Mins.Y) <= 32));
    }

    [Fact]
    public void Level_threes_cool_fill_is_one_light_a_cell_at_most()
    {
        // The review of 0c79c6fc2: the fill every 384 was about 250 of the level's 412 to 431 map entities.
        var fills = act.Of(3).Select(r => (r.Dressed.Cells, Fills: r.Dressed.Entities.Count(e => e.Key(RoomDresser.RoleKey) == nameof(RoomDresser.Role.Fill))));

        Assert.All(fills, f => Assert.InRange(f.Fills, 0, f.Cells * f.Cells));
    }

    [Fact]
    public void The_mills_stripe_runs_along_a_decks_edge_but_not_a_flights_steps()
    {
        // A flight of six 16 steps rising north (each step a box from the floor to its tread, Brush.Stairs), and a deck 64 high.
        var b = new SourceSharp.MapGen.Descent.RoomBuilder("store_x", RoomKit.Normal, ActOne.Mill).WithHeight(512).WithDoors(CellFace.West, CellFace.East);
        SourceSharp.MapGen.Descent.Shapes.Parts.StairsUp(b, new Rect(400, 560, 640, 752), Compass.North, 96);
        SourceSharp.MapGen.Descent.Shapes.Parts.Block(b, new Rect(300, 200, 460, 360), 64);
        Room dressed = RoomDresser.Dress(b.Build(), ActOne.Mill, 0);
        float sill = RoomKit.Normal.Sill;
        var stripeTops = dressed.World.Where(w => w.Material == ActOne.Mill.Hazard!.Value.Material && w.Maxs.Z > sill + 16 && w.Maxs.Z < sill + 128)
            .Select(w => w.Maxs.Z - sill).Distinct().Order();

        Assert.Equal([64f], stripeTops);
    }
}

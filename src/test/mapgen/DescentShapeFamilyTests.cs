using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Shapes;
using TF2.Descent.Rules.Layout;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The mine's shape families (plan §5, task A6): each lints clean, keeps its
/// height class and door sets, varies its outline, and promises what its
/// plan row says.
/// </summary>
public sealed class DescentShapeFamilyTests
{
    private static readonly RoomKit Kit = RoomKit.Normal;

    private static SourceSharp.MapGen.Descent.RoomShape Family(string name) => RoomCatalogue.Mine.Single(f => f.Family == name);

    private static IReadOnlyList<Room> Rooms(string family, ulong seed = 0) => RoomCatalogue.Rooms([Family(family)], Kit, RoomTheme.Grey, seed);

    public static TheoryData<string> Families => [.. RoomCatalogue.Mine.Select(f => f.Family)];

    public static TheoryData<string> Connectors => ["drift", "bend", "tee", "cross", "dogleg", "stub", "widedrift"];

    public static TheoryData<string> Destinations => ["pillar", "split", "pit", "store", "yard", "pool", "stope", "gallery", "chasm", "chimney", "cavern", "crypt"];

    private static int DoorCount(Room r) => r.Doors.Count;

    [Theory]
    [MemberData(nameof(Families))]
    public void Every_familys_rooms_lint_clean_at_four_seeds(string family)
    {
        for (ulong seed = 0; seed < 4; seed++)
            Assert.All(Rooms(family, seed), r => Assert.Empty(RoomLint.Check(r)));
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void Every_family_has_three_variants_at_least(string family)
    {
        Assert.True(Rooms(family).Count >= 3, $"{family} has {Rooms(family).Count}");
    }

    [Theory]
    [MemberData(nameof(Connectors))]
    public void A_connector_is_low_and_holds_one_pack_at_most(string family)
    {
        Assert.All(Rooms(family), r =>
        {
            Assert.InRange(r.Height, Kit.LeastHeight, LayoutNames.LowMax);
            Assert.True(r.Entry.Low);
            Assert.True(r.Markers.Count(m => m.Kind == MarkerKind.PackSpawn) <= 1);
        });
    }

    [Theory]
    [MemberData(nameof(Destinations))]
    public void A_chamber_or_hall_family_offers_a_one_door_room_with_a_reward(string family)
    {
        var one = Rooms(family).Where(r => DoorCount(r) == 1).ToArray();

        Assert.NotEmpty(one);
        Assert.All(one, r => Assert.Contains(r.Markers, m => Markers.IsReward(m.Kind)));
    }

    [Theory]
    [MemberData(nameof(Destinations))]
    public void A_chamber_or_hall_family_offers_two_doors_opposite_or_at_a_corner(string family)
    {
        var sets = Rooms(family).Select(r => LayoutNames.DoorsOf(r.Doors)).ToArray();

        Assert.Contains(sets, d => d is (Doors.West | Doors.East) or (Doors.North | Doors.East));
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void A_familys_variants_do_not_share_one_outline(string family)
    {
        var outlines = Rooms(family).Select(r => string.Join(";", r.Outline.Concat(r.World.Select(b => new Rect(b.Mins.X, b.Mins.Y, b.Maxs.X, b.Maxs.Y))))).ToArray();

        Assert.Equal(outlines.Length, outlines.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Destinations))]
    public void Most_of_a_familys_rooms_leave_part_of_their_cell_unused(string family)
    {
        float interior = Kit.Interior * (float)Kit.Interior;
        var rooms = Rooms(family).Where(r => r.Role == RoomRole.None).ToArray();

        Assert.True(rooms.Count(r => r.Outline.Count > 0 && Rects.Subtract(new Rect(Kit.WallDepth, Kit.WallDepth, Kit.Cell - Kit.WallDepth, Kit.Cell - Kit.WallDepth), r.Outline).Sum(x => x.Width * x.Depth) > 0.05f * interior) * 2 >= rooms.Length,
            $"{family}: its rooms fill their cells");
    }

    [Theory]
    [InlineData("drift", 60)]
    [InlineData("bend", 60)]
    [InlineData("tee", 60)]
    [InlineData("cross", 60)]
    [InlineData("dogleg", 60)]
    [InlineData("stub", 60)]
    [InlineData("widedrift", 60)]
    [InlineData("pillar", 150)]
    [InlineData("split", 150)]
    [InlineData("pit", 150)]
    [InlineData("store", 150)]
    [InlineData("yard", 150)]
    [InlineData("pool", 150)]
    [InlineData("stope", 150)]
    [InlineData("gallery", 150)]
    [InlineData("chasm", 150)]
    [InlineData("chimney", 150)]
    [InlineData("cavern", 150)]
    public void A_familys_rooms_stay_within_their_brush_budget(string family, int budget)
    {
        Assert.All(Rooms(family), r => Assert.True(r.World.Count + r.Entities.Sum(e => e.Brushes.Count) < budget, $"{r.Name}: {r.World.Count} world brushes"));
    }

    [Fact]
    public void A_chamber_is_512_high()
    {
        foreach (string family in (string[])["pillar", "split", "pit", "store", "yard", "pool"])
            Assert.All(Rooms(family), r => Assert.Equal(LayoutNames.ChamberMax, r.Height));
    }

    [Fact]
    public void A_hall_is_a_jump_room_of_1024_or_more()
    {
        foreach (string family in (string[])["stope", "gallery", "chasm", "chimney", "cavern"])
            Assert.All(Rooms(family), r => Assert.True(r.Entry.Jump && r.Height >= 1024, r.Name));
    }

    [Fact]
    public void A_chimney_is_shaft_high()
    {
        Assert.All(Rooms("chimney"), r => Assert.True(r.Height > LayoutNames.HallMax, r.Name));
    }

    [Theory]
    [InlineData("split", JumpTier.S2)]
    [InlineData("pit", JumpTier.S2)]
    [InlineData("pool", JumpTier.S2)]
    [InlineData("stope", JumpTier.B1)]
    [InlineData("gallery", JumpTier.B2)]
    [InlineData("chimney", JumpTier.B3)]
    [InlineData("cavern", JumpTier.B2)]
    public void A_familys_rooms_hold_its_jump_feature(string family, JumpTier tier)
    {
        Assert.All(Rooms(family), r => Assert.Contains(tier, r.JumpTiers));
    }

    [Fact]
    public void A_chasm_offers_both_scout_leaps()
    {
        var tiers = Rooms("chasm").SelectMany(r => r.JumpTiers).ToHashSet();

        Assert.Contains(JumpTier.S2, tiers);
        Assert.Contains(JumpTier.S3, tiers);
    }

    [Fact]
    public void A_dogleg_blocks_the_sight_line_between_its_doors()
    {
        Assert.All(Rooms("dogleg"), r =>
        {
            // The band a player sees down from one door to the other, at eye height.
            Point mins = new(Kit.WallDepth, Kit.DoorLow, Kit.Sill + 60), maxs = new(Kit.Cell - Kit.WallDepth, Kit.DoorHigh, Kit.Sill + 70);
            bool blocked = Enumerable.Range(0, (Kit.DoorHigh - Kit.DoorLow) / 8 + 1).All(k =>
            {
                float y = Kit.DoorLow + 8 * k;
                return r.World.Any(b => b.Overlaps(mins with { Y = y - 1 }, maxs with { Y = y + 1 }));
            });
            Assert.True(blocked, $"{r.Name}: a line from door to door is open");
        });
    }

    [Fact]
    public void A_wide_drift_has_a_row_of_posts_down_its_middle()
    {
        float m = Kit.Cell / 2f;
        Assert.All(Rooms("widedrift"), r =>
            Assert.True(r.World.Count(b => b.Mins.Y < m && b.Maxs.Y > m && b.Maxs.X - b.Mins.X <= 32 && b.Maxs.Z >= r.Height - 2 * Kit.WallDepth) >= 3, r.Name));
    }

    [Fact]
    public void Every_stub_is_a_shrine_with_a_shrine_marker()
    {
        Assert.All(Rooms("stub"), r =>
        {
            Assert.Contains("shrine", r.Entry.Tags);
            Assert.Contains(r.Markers, m => m.Kind == MarkerKind.Shrine);
            Assert.Single(r.Doors);
        });
    }

    [Fact]
    public void A_pools_water_never_touches_a_door_plug()
    {
        Assert.All(Rooms("pool"), r =>
        {
            Assert.NotEmpty(r.Water);
            foreach (CellFace face in r.Doors)
            {
                var (mins, maxs) = Kit.PlugBox(face);
                Assert.DoesNotContain(r.Water, w => w.Overlaps(mins - new Point(1, 1, 1), maxs + new Point(1, 1, 1)));
            }
        });
    }

    [Fact]
    public void A_pools_water_is_not_in_its_world()
    {
        Assert.All(Rooms("pool"), r => Assert.DoesNotContain(r.World, b => b.Material == PoolShape.WaterMaterial));
    }

    [Fact]
    public void A_caverns_rubble_mound_rises_toward_its_east_side()
    {
        // Displacement rows run along the face's last edge (+y on a floor patch) and columns along its first (+x).
        var mound = Rooms("cavern").SelectMany(r => r.World).SelectMany(b => b.Faces).First(f => f.Displacement is not null && f.Normal.Z > 0.9f).Displacement!;
        int n = mound.Size, mid = n / 2;

        Assert.True(mound.Distances[mid, n - 2] > mound.Distances[mid, 1], "the crest is not to the east");
    }

    [Fact]
    public void A_cavern_has_rough_walls_and_stalactites()
    {
        Assert.All(Rooms("cavern"), r => Assert.Contains(r.World, b => b.Faces.Any(f => f.Displacement is not null)));
        Assert.Contains(Rooms("cavern").SelectMany(r => r.World), b => b.Faces.Count >= 7 && b.Faces.Any(f => f.Polygon.Count == 3) && b.Maxs.Z > 800);
    }
}

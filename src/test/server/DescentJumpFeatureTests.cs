using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The jump features of plan §4.3 (task A5): each at its tier's height,
/// each with clear air above it, and each paying for the jump.
/// </summary>
public sealed class DescentJumpFeatureTests
{
    private static readonly RoomKit Kit = RoomKit.Normal;
    private static readonly float Floor = Kit.Sill;

    private static RoomBuilder Hall(int height = 1024) =>
        new RoomBuilder("h", Kit, RoomTheme.Grey).WithHeight(height).WithDoors(CellFace.West);

    private static float Top(Room room, float x, float y) =>
        room.World.Select(b => b.Column(x, y)).Where(c => c is not null && c.Value.Low >= Floor - 1 && c.Value.High < room.Height - Kit.WallDepth)
            .Max(c => c!.Value.High) - Floor;

    private static int Tier(JumpTier tier, float t = 0.5f) => JumpTable.Of(tier).Ledge(t);

    [Fact]
    public void Crate_steps_rise_a_crouch_jump_each()
    {
        Room room = Hall().CrateSteps(600, 600, Compass.East, steps: 2).Build();

        Assert.Equal(Tier(JumpTier.T1, 1), Top(room, 632, 632));
        Assert.Equal(2 * Tier(JumpTier.T1, 1), Top(room, 696, 632));
    }

    [Fact]
    public void A_mezzanines_deck_is_at_the_double_jumps_height_and_its_stairs_reach_it()
    {
        Room room = Hall().Mezzanine(CellFace.North, 200, 500).Build();
        float y = Kit.Cell - Kit.WallDepth - 50;

        Assert.Equal(Tier(JumpTier.S2), Top(room, 300, y));
        Assert.Equal(Tier(JumpTier.S2), Top(room, 510, y));
        Assert.Equal(16, Top(room, 690, y));
    }

    [Fact]
    public void A_rocket_shelf_is_at_the_rocket_jumps_height_with_a_container_on_it()
    {
        Room room = Hall(512).RocketShelf(CellFace.East, 400, 624).Build();
        var (kind, at) = Assert.Single(room.Markers);

        Assert.Equal(MarkerKind.Container, kind);
        Assert.Equal(Tier(JumpTier.B1), at.Z - Floor);
        Assert.Equal(Tier(JumpTier.B1), Top(room, at.X, at.Y));
    }

    [Fact]
    public void A_catwalk_ring_runs_round_all_four_walls_at_the_crouched_rocket_jumps_height()
    {
        Room room = Hall().CatwalkRing().Build();
        float near = Kit.WallDepth + 40, far = Kit.Cell - Kit.WallDepth - 40, m = Kit.Cell / 2f;

        Assert.All([(near, m), (far, m), (m, near), (m, far)], p => Assert.Equal(Tier(JumpTier.B2), Top(room, p.Item1, p.Item2)));
        Assert.Single(room.Markers);
    }

    [Fact]
    public void Stepping_pillars_climb_from_the_double_to_the_triple_jump_with_a_reward_on_the_last()
    {
        Room room = Hall().SteppingPillars(300, 512, Compass.East, JumpTier.S3).Build();
        var (kind, at) = Assert.Single(room.Markers);

        Assert.Equal(Tier(JumpTier.S2), Top(room, 300, 512));
        Assert.Equal(Tier(JumpTier.S3), Top(room, at.X, at.Y));
        Assert.True(Markers.IsReward(kind));
    }

    [Fact]
    public void A_chasm_leaps_gap_is_its_tiers_with_a_bridge_along_the_far_side()
    {
        Room room = Hall().ChasmLeap(160, 200, Compass.East, 400, JumpTier.S2).Build();
        int gap = JumpTable.Of(JumpTier.S2).Gap();
        float deckEnd = 160 + 160, inGap = deckEnd + gap / 2f;

        Assert.Equal(128, Top(room, deckEnd - 8, 300));
        Assert.Equal(128, Top(room, deckEnd + gap + 8, 300));
        Assert.Throws<InvalidOperationException>(() => Top(room, inGap, 300));
        Assert.Equal(128, Top(room, inGap, 200 + 400 - 48));
    }

    [Fact]
    public void Chimney_ledges_stand_at_the_crouched_rocket_and_sticky_jumps_heights_each_with_a_reward()
    {
        Room room = Hall(1536).ChimneyLedges(CellFace.North, CellFace.South).Build();

        Assert.Equal([Tier(JumpTier.B2), Tier(JumpTier.B3)], room.Markers.Select(m => (int)(m.At.Z - Floor)).Order());
        Assert.All(room.Markers, m => Assert.True(Markers.IsReward(m.Kind)));
    }

    [Fact]
    public void A_roof_gallery_is_at_the_top_of_the_sticky_tier()
    {
        Room room = Hall(1536).RoofGallery(CellFace.South, 300, 700).Build();

        Assert.Equal(Tier(JumpTier.B3, 1), Assert.Single(room.Markers).At.Z - Floor);
    }

    [Fact]
    public void A_ledge_without_its_clear_air_under_the_ceiling_is_refused()
    {
        // A 768 gallery needs 882 of air; a 1024 hall has 960 under its ceiling, a 768 one 704.
        Assert.Throws<ArgumentException>(() => Hall(768).RoofGallery(CellFace.South, 300, 700));
    }

    [Fact]
    public void A_feature_in_front_of_a_door_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Hall().CrateSteps(40, 480, Compass.East).Build());
    }

    [Fact]
    public void A_ledge_on_a_32_voxel_kit_stands_on_a_whole_voxel()
    {
        RoomKit big = RoomKit.For("voxel32", 4096, FrameModel.FarmDoorframe, navVoxel: 32);
        Room room = new RoomBuilder("r", big, RoomTheme.Grey).WithHeight(1280).WithDoors(CellFace.West)
            .RocketShelf(CellFace.North, 1800, 2200, t: 0.4f).SteppingPillars(1500, 1500, Compass.East, JumpTier.S2).Build();

        Assert.All(room.Markers, m => Assert.Equal(0, (m.At.Z - big.Sill) % big.NavVoxel));
    }

    [Fact]
    public void A_triple_jumps_pillars_cannot_stand_on_a_32_voxel_kit()
    {
        var b = new RoomBuilder("r", RoomKit.For("voxel32", 4096, FrameModel.FarmDoorframe, navVoxel: 32), RoomTheme.Grey).WithHeight(1280).WithDoors(CellFace.West);

        Assert.Throws<InvalidOperationException>(() => b.SteppingPillars(1500, 1500, Compass.East, JumpTier.S3));
    }
}

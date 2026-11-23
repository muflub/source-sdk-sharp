using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>The jump-tier table (plan §4.1, task A5) as data.</summary>
public sealed class DescentJumpTierTests
{
    [Fact]
    public void The_table_is_plan_section_4_1s_seven_tiers()
    {
        Assert.Equal(
            [(JumpTier.T0, 0, 18, 0, 0), (JumpTier.T1, 19, 64, 0, 160), (JumpTier.S2, 88, 104, 400, 512), (JumpTier.S3, 136, 144, 704, 768),
             (JumpTier.B1, 192, 256, 0, 448), (JumpTier.B2, 320, 448, 0, 640), (JumpTier.B3, 640, 768, 0, 1024)],
            JumpTable.Tiers.Select(t => (t.Tier, t.LedgeMin, t.LedgeMax, t.GapMin, t.GapMax)));
    }

    [Fact]
    public void Each_tiers_ledges_start_above_the_tier_belows()
    {
        for (int i = 1; i < JumpTable.Tiers.Count; i++)
            Assert.True(JumpTable.Tiers[i].LedgeMin > JumpTable.Tiers[i - 1].LedgeMax, $"{JumpTable.Tiers[i].Tier}");
    }

    [Theory]
    [InlineData(JumpTier.S2, 0.5f, 96)]
    [InlineData(JumpTier.S3, 0.5f, 144)]
    [InlineData(JumpTier.B1, 0.5f, 224)]
    [InlineData(JumpTier.B2, 0.5f, 384)]
    [InlineData(JumpTier.B3, 0.5f, 704)]
    [InlineData(JumpTier.B3, 1f, 768)]
    [InlineData(JumpTier.T1, 1f, 64)]
    public void A_ledge_is_a_whole_number_of_voxels_inside_its_tier(JumpTier tier, float t, int height)
    {
        Assert.Equal(height, JumpTable.Of(tier).Ledge(t));
    }

    [Fact]
    public void A_ledge_needs_clear_air_to_its_height_plus_114()
    {
        Assert.Equal(224 + 114, JumpTierSpec.ClearAbove(224));
    }

    [Fact]
    public void A_rocket_jump_room_needs_576_over_half_its_floor_and_a_768_run()
    {
        Assert.Equal((576, 768, 1104), (JumpTable.RocketClearAir, JumpTable.RocketRun, JumpTable.StickyClearAir));
    }

    [Fact]
    public void A_tier_with_no_ledge_on_the_grid_says_so()
    {
        var odd = new JumpTierSpec(JumpTier.S2, "x", 97, 103, 0, 0);

        Assert.Throws<InvalidOperationException>(() => odd.Ledge());
    }
}

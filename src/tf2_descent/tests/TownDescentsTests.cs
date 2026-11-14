using TF2.Descent.Rules;
using Xunit;

namespace TF2.Descent.Tests;

/// <summary>C5: the depths the town sends a party to (M10).</summary>
public sealed class TownDescentsTests
{
    [Fact]
    public void A_party_that_has_never_gone_down_may_go_to_level_1()
    {
        Assert.Equal([1], TownDescents.Destinations(0, []));
    }

    [Fact]
    public void The_cage_offers_the_levels_reached_up_to_5()
    {
        Assert.Equal([1, 2, 3], TownDescents.Destinations(3, []));
        Assert.Equal([1, 2, 3, 4, 5], TownDescents.Destinations(5, []));
    }

    [Fact]
    public void A_level_below_the_cage_reached_without_a_kill_offers_no_more()
    {
        Assert.Equal([1, 2, 3, 4, 5], TownDescents.Destinations(9, []));
    }

    [Fact]
    public void The_level_5_kill_opens_the_camp_at_6()
    {
        Assert.Equal([1, 2, 3, 4, 5, 6], TownDescents.Destinations(6, [5]));
    }

    [Fact]
    public void The_level_10_kill_opens_the_camp_at_11()
    {
        Assert.Equal([1, 2, 3, 4, 5, 6, 11], TownDescents.Destinations(11, [5, 10]));
    }

    [Fact]
    public void Reaching_a_deep_level_without_the_level_10_kill_opens_no_second_camp()
    {
        Assert.DoesNotContain(11, TownDescents.Destinations(14, [5]));
    }

    [Fact]
    public void The_last_kill_opens_nothing_more()
    {
        Assert.Equal(TownDescents.Destinations(15, [5, 10]), TownDescents.Destinations(15, [5, 10, 15]));
    }

    [Fact]
    public void A_kill_at_a_depth_with_no_boss_is_refused()
    {
        Assert.Throws<ArgumentException>(() => TownDescents.Destinations(9, [7]));
    }

    [Fact]
    public void A_negative_depth_reached_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TownDescents.Destinations(-1, []));
    }
}

using TF2.Descent.Ai.Level;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The hunters' leash (plan_descent_logic L25): on eight rooms in a row, a robot at home in room 0
/// on three doors stops for a quarry or a next room four doors out, never for a quarry in its home
/// room, and not at all with no leash.
/// </summary>
public sealed class HordeLeashTests
{
    private static readonly HordeLevel Eight = Row(8, markers: 0);
    private static readonly HordeLeash Three = new(3);

    [Fact]
    public void A_quarry_three_doors_from_home_is_within_the_leash()
    {
        Assert.False(Three.Past(Eight, 0, 1, 3));
    }

    [Fact]
    public void A_quarry_four_doors_from_home_is_past_the_leash()
    {
        Assert.True(Three.Past(Eight, 0, 1, 4));
    }

    [Fact]
    public void A_next_room_four_doors_from_home_is_past_the_leash_with_the_quarry_within()
    {
        Assert.True(Three.Past(Eight, 0, 4, 3));
    }

    [Fact]
    public void A_quarry_in_the_home_room_is_never_past_the_leash()
    {
        Assert.True(Three.Past(Eight, 0, 5, 1), "the control: that next room alone is past it");
        Assert.False(Three.Past(Eight, 0, 5, 0));
    }

    [Fact]
    public void No_leash_is_never_past()
    {
        Assert.False(default(HordeLeash).Past(Eight, 0, 7, 7));
    }

    [Fact]
    public void A_place_in_no_room_is_not_asked()
    {
        Assert.False(Three.Past(Eight, 0, Centre(1), At(-5000, -5000)));
    }

    [Fact]
    public void By_place_the_next_room_is_the_room_graphs_next_towards_the_quarry()
    {
        // From room 7 to a quarry in room 2 (two doors from home): the walk enters room 6, six out.
        Assert.Equal(6, HordeLeash.NextOf(Eight, 7, 2));
        Assert.True(Three.Past(Eight, 0, Centre(7), Centre(2)));
        Assert.False(Three.Past(Eight, 0, Centre(1), Centre(2)));
    }

    [Fact]
    public void A_hero_within_engage_range_past_the_leash_is_not_let_go()
    {
        Assert.False(Three.LetsGo(Eight, 0, Centre(3), Centre(3) + At(300, 0), 640f));
        Assert.True(Three.LetsGo(Eight, 0, Centre(3), Centre(3) + At(300, 0), 200f));
    }
}

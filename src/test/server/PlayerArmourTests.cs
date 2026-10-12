using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The suit (plan_client_ui U3's HUD gap): armour is the player's networked
/// managed state, and a hit spends it by stock's arithmetic before health.
/// </summary>
public class PlayerArmourTests
{
    [Fact]
    public void A_bullet_into_a_full_suit_takes_a_fifth_from_health_and_the_rest_from_armour()
    {
        var (toHealth, left, absorbed) = SsPlayer.Absorb(100, 50f, DamageType.Bullet);
        Assert.Equal((10f, 60, 40f), (toHealth, left, absorbed));
    }

    [Fact]
    public void A_fall_goes_straight_to_health()
    {
        var (toHealth, left, absorbed) = SsPlayer.Absorb(100, 30f, DamageType.Fall);
        Assert.Equal((30f, 100, 0f), (toHealth, left, absorbed));
    }

    [Fact]
    public void Armour_is_a_networked_word_of_the_player()
    {
        var state = new PlayerNetState();
        state.BindLocal();
        state.Armour = 37;
        Assert.Equal(37, state.Armour);
        Assert.Contains(typeof(PlayerNetState).GetProperties(), p => p.Name == nameof(PlayerNetState.Armour));
    }
}

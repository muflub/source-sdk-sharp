using SourceSharp.Abi;

using TF2.Server.Combat;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary>
/// <see cref="TFFallDamage"/>: <c>CTFGameRules::FlPlayerFallDamage</c>
/// (<c>tf_gamerules.cpp:13368-13414</c>): above <c>TF_PLAYER_MAX_SAFE_FALL_SPEED</c>
/// 650, <c>5 × speed / 300 × maxHealth / 100</c>, times a random 0.8-1.2.
/// </summary>
public sealed class TFFallDamageTests
{
    private static float Never(float min, float max) => throw new InvalidOperationException("spread disabled: no roll");

    [Fact]
    public void A_landing_at_or_under_650_does_nothing()
    {
        Assert.Equal(0f, TFFallDamage.Damage(650f, 125, disableSpread: true, Never));
        Assert.Equal(0f, TFFallDamage.Damage(600f, 125, disableSpread: false, Never));
    }

    [Fact]
    public void Without_spread_the_damage_is_the_tfc_formula_scaled_by_max_health()
    {
        // 5 * 900 / 300 = 15, * 125 / 100 = 18.75 for a scout; * 300 / 100 = 45 for a heavy.
        Assert.Equal(18.75f, TFFallDamage.Damage(900f, 125, disableSpread: true, Never), 4);
        Assert.Equal(45f, TFFallDamage.Damage(900f, 300, disableSpread: true, Never), 4);
    }

    [Fact]
    public void With_spread_the_damage_is_inside_the_stock_band_and_the_roll_is_0_8_to_1_2()
    {
        var asked = new List<(float Min, float Max)>();
        float low = TFFallDamage.Damage(900f, 125, false, (lo, hi) => { asked.Add((lo, hi)); return lo; });
        float high = TFFallDamage.Damage(900f, 125, false, (lo, hi) => hi);

        Assert.Equal((0.8f, 1.2f), Assert.Single(asked));
        Assert.Equal(15f, low, 4);
        Assert.Equal(22.5f, high, 4);
    }

    [Theory]
    [InlineData(600f)]
    [InlineData(700f)]
    [InlineData(900f)]
    [InlineData(1200f)]
    public void The_landing_speed_is_recovered_from_the_cores_damage(float speed)
        => Assert.Equal(speed, TFFallDamage.FallSpeedOf(MoveMath.FallDamage(speed)), 2);

    [Fact]
    public void No_core_damage_is_no_speed()
        => Assert.Equal(0f, TFFallDamage.FallSpeedOf(0f));
}

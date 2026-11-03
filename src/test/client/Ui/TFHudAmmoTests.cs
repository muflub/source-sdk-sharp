using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The ammo row's two stock arms and the silence between them
/// (<c>CTFHudWeaponAmmo::OnThink</c>, tf_hud_ammostatus.cpp:248-319): the
/// clip weapon writes both dialog variables (<c>Ammo</c> and
/// <c>AmmoInReserve</c>, :298-304), the weapon with no clip writes the
/// pool as the one variable and hides the other (:279-282, :305-309),
/// and a weapon <c>ShouldDraw</c> hides gets no corner at all (:120-163).
/// </summary>
public class TFHudAmmoTests
{
    [Fact]
    public void The_clip_weapon_draws_the_clip_and_the_pool_beneath_it()
    {
        Assert.False(HasElement("medigun-hides-the-ammo", "ammo"));                  // before: ShouldDraw, no row
        Assert.True(HasElement("rocket-launcher-clip-and-reserve", "ammo"));         // after: the clip, :276
        Assert.True(HasElement("rocket-launcher-clip-and-reserve", "reserve"));      // and its pool, :284-304
    }

    [Fact]
    public void A_weapon_with_no_clip_draws_the_pool_as_the_one_number()
    {
        Assert.True(HasElement("rocket-launcher-clip-and-reserve", "reserve"));      // before: the clip arm shows the pool label
        Assert.True(HasElement("flamethrower-pool-total", "ammo"));                   // after: the total, nAmmo1 < 0 (:279-282)
        Assert.False(HasElement("flamethrower-pool-total", "reserve"));               // AmmoInReserve stays hidden (:305-309)
    }

    [Fact]
    public void The_ammo_corner_exists_only_for_a_weapon_that_shows_a_readout()
    {
        Assert.False(HasElement("healthy-soldier", "ammo bg"));                       // empty hands: no corner, no row
        Assert.False(HasElement("medigun-hides-the-ammo", "ammo bg"));               // ShouldDraw, the medigun arm (:137-140)
        Assert.True(HasElement("rocket-launcher-clip-and-reserve", "ammo bg"));      // after: the corner is there, art and all
        Assert.False(HasElement("rocket-launcher-clip-and-reserve", "ammo plate"));  // the art up: no plate under it
        Assert.True(TFHudTests.HasFallback("rocket-launcher-clip-and-reserve", "ammo plate")); // artless: the plate backs the row
        Assert.True(HasElement("healthy-soldier", "player corner"));                  // stimulus: the health corner is not gated
    }

    [Fact]
    public void The_low_ammo_warning_draws_only_under_the_threshold()
    {
        Assert.False(HasElement("rocket-launcher-clip-and-reserve", "low ammo"));  // full stock: no swap
        Assert.True(HasElement("low-on-rockets", "low ammo"));                     // 20 of 60 < 24: the swap (:313-325)
        Assert.False(HasElement("low-ammo-boundary", "low ammo"));                 // exactly 24: the strict < of :321
    }

    /// <summary>Whether the named part drew on the sample's screen.</summary>
    private static bool HasElement(string slug, string key) => TFHudTests.HasElement(slug, key);
}

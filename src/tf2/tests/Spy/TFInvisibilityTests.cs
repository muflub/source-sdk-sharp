using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Spy;

/// <summary>
/// The invisibility level (<c>CTFPlayerShared::GetPercentInvisible</c>,
/// tf_player_shared.cpp:8047, fed by <c>InvisibilityThink</c> :7977-8041) as
/// the carrier and the clock give it: the networked
/// <c>m_flInvisChangeCompleteTime</c> and condition words are the only inputs,
/// on the server as on the client.
/// </summary>
public sealed class TFInvisibilityTests
{
    private static TFPlayerNetState Carrier(TFClass playerClass, params TFCond[] conds)
    {
        var state = new TFPlayerNetState();
        state.BindLocal();
        state.PlayerClass = (int)playerClass;
        TFCondMask mask = default;
        foreach (TFCond cond in conds)
            mask |= TFCondMask.Of(cond);
        TFPlayerNetStateViews.SetConditions(state, mask);
        return state;
    }

    [Fact]
    public void A_cloaking_spys_level_rises_to_one_over_the_invis_time()
    {
        var state = Carrier(TFClass.Spy, TFCond.Stealthed);
        state.InvisChangeCompleteTime = 101f;   // cloaked at 100 over the 1.0 s

        // The fade-in arm (tf_player_shared.cpp:8001-8004).
        Assert.Equal(0.25f, TFInvisibility.Percent(state, 100.25f), 4);
        Assert.Equal(1f, TFInvisibility.Percent(state, 101f), 4);
        Assert.Equal(1f, TFInvisibility.Percent(state, 102f), 4);
    }

    [Fact]
    public void An_uncloaking_spys_level_falls_to_zero_over_the_unstealth_time()
    {
        var state = Carrier(TFClass.Spy);
        state.InvisChangeCompleteTime = 102.5f;  // decloaked at 100.5 over 2.0 s

        // The fade-out arm's half rate (:8005-8008).
        Assert.Equal(0.5f, TFInvisibility.Percent(state, 101.5f), 4);
        Assert.Equal(0f, TFInvisibility.Percent(state, 102.5f), 4);
    }

    [Fact]
    public void A_blink_scales_the_level_to_four_fifths_and_a_half()
    {
        var state = Carrier(TFClass.Spy, TFCond.Stealthed, TFCond.StealthedBlink);
        state.InvisChangeCompleteTime = 101f;

        // TF_SPY_STEALTH_BLINKSCALE 0.85 (tf_player_shared.cpp:215, :7992-7996).
        Assert.Equal(0.425f, TFInvisibility.Percent(state, 100.5f), 4);

        var urine = Carrier(TFClass.Spy, TFCond.Stealthed, TFCond.Urine);
        urine.InvisChangeCompleteTime = 101f;
        Assert.Equal(0.425f, TFInvisibility.Percent(urine, 100.5f), 4);
    }

    [Fact]
    public void A_stealthed_non_spy_is_not_invisible()
    {
        var state = Carrier(TFClass.Scout, TFCond.Stealthed);
        state.InvisChangeCompleteTime = 101f;

        // The safety net (tf_player_shared.cpp:7979-7988).
        Assert.Equal(0f, TFInvisibility.Percent(state, 100.5f), 4);
    }

    [Fact]
    public void A_clients_percent_comes_from_the_networked_carrier_alone()
    {
        // The decoded state of a spy mid-fade-out, as a client holds it — no
        // server-side object in sight, the condition word gone with the cloak.
        var state = new TFPlayerNetState();
        state.BindLocal();
        state.PlayerClass = (int)TFClass.Spy;
        state.InvisChangeCompleteTime = 102.5f;

        Assert.Equal(0.5f, TFInvisibility.Percent(state, 101.5f), 4);
    }
}

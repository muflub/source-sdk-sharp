using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Spy;

/// <summary>
/// The no-attack window's stamp: <c>FadeInvis</c>'s
/// <c>m_flStealthNoAttackExpire</c> (tf_player_shared.cpp:7961-7969) and its
/// scales — the decloak-rate attribute (:7950-7956), the fade floor :7961 and
/// the user-buff-fading guard :7965. The refusal that reads it is the weapon
/// lane's (<c>TFNoAttackRefusalTests</c>).
/// </summary>
public sealed class TFNoAttackWindowTests
{
    private static IAttributeProvider Attr(string hook, float value)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(5100, "attr 5100", hook, AttributeDescriptionFormat.Percentage,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }

    [Fact]
    public void A_watch_decloak_stamps_no_attack_for_the_convar_over_the_unstealth_time()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100f;
        rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex);

        // CTFWeaponInvis::RemoveInvisibility (tf_weapon_invis.cpp:226) fades at
        // the full rate, so the stamp (tf_player_shared.cpp:7968) is
        // 100 + 2.0 * 1 * 1.
        rig.Slice.FadeInvis(1f, rig.Clock.Now);

        Assert.Equal(102f, rig.State.NoAttackExpire, 4);
        Assert.Equal(102f, rig.State.InvisChangeCompleteTime, 4);
    }

    [Fact]
    public void A_forced_remove_stamps_half_the_window()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100f;
        rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex);

        // CTFPlayer::RemoveInvisibility (tf_player.cpp:15988-16005) fades at
        // 0.5, and the stamp takes the caller's scale too (:7968).
        rig.Spies.RemoveInvisibility(SpyRig.Spy.EntryIndex);

        Assert.Equal(101f, rig.State.NoAttackExpire, 4);
    }

    [Fact]
    public void A_decloak_rate_attribute_scales_both_clocks()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100f;
        rig.Owners.Managers[SpyRig.Spy.EntryIndex] = Attr("mult_decloak_rate", 2f);
        rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex);

        // The hook (:7950-7956) scales the fade :7959 and the window :7968
        // alike: 100 + 2.0 * 2 * 1.
        rig.Slice.FadeInvis(1f, rig.Clock.Now);

        Assert.Equal(104f, rig.State.NoAttackExpire, 4);
        Assert.Equal(104f, rig.State.InvisChangeCompleteTime, 4);
    }

    [Fact]
    public void A_sub_floor_fade_arms_the_fade_but_no_window()
    {
        var rig = new SpyRig();
        rig.Clock.Now = 100f;

        // tf_player_shared.cpp:7961-7964: "this was a force respawn, they can
        // attack whenever" — 0.05 * 2.0 = 0.1 of fade is under the 0.15 floor.
        rig.Slice.FadeInvis(0.05f, rig.Clock.Now);

        Assert.Equal(0f, rig.State.NoAttackExpire, 4);
        Assert.Equal(100.1f, rig.State.InvisChangeCompleteTime, 4);
    }

    [Fact]
    public void An_expiring_user_buff_fading_stamps_no_window()
    {
        var rig = new SpyRig();
        rig.Clock.Now = 100f;
        rig.Conditions.Add(SpyRig.Spy, TFCond.StealthedUserBuffFading, provider: SpyRig.Spy);

        // tf_player_shared.cpp:7965's second arm: a user buff already fading
        // leaves the window it stamped alone.
        rig.Slice.FadeInvis(1f, rig.Clock.Now);

        Assert.Equal(0f, rig.State.NoAttackExpire, 4);
        Assert.Equal(102f, rig.State.InvisChangeCompleteTime, 4);
    }
}

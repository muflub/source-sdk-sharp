using SourceSharp.Abi;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Tests.Rules;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The player-half effect arms of stock's <c>OnConditionAdded</c>
/// (tf_player_shared.cpp:1571-1886) for the conditions this build has: the
/// fire-starting sound of <c>OnAddBurning</c> (:7320), run by the condition
/// writer on every add and refresh — the arm's client half (particles,
/// overlay) waits for the client's condition-effect driver
/// (<c>docs/plans/tf2/deferred.tsv</c>).
/// </summary>
public sealed class TFPlayerConditionEffectArmsTests
{
    private static (PlayerRig Rig, TFPlayerConditions Conditions) Wired()
    {
        var rig = new PlayerRig();
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine,
                                                new ListHooks<ITFConditionHook>(new TFPlayerConditionEffectArms(rig.Engine)));
        rig.Join();
        return (rig, conditions);
    }

    [Fact]
    public void A_player_set_alight_emits_the_engulf_sound()
    {
        var (rig, conditions) = Wired();

        Assert.Equal(TFCondAdd.Added, conditions.Add(PlayerRig.Player, TFCond.Burning, 5f));

        var emit = Assert.Single(rig.Engine.AllOf("Sound.EntityEmitSound"));
        Assert.Equal(PlayerRig.Player, emit.ArgAs<EntityHandle>(0));
        Assert.Equal("Fire.Engulf", emit.ArgAs<string>(1));
    }

    /// <summary>Stock's <c>AddCond</c> runs the arm on every add, refresh included — the sound is the arm's, not the ignition's.</summary>
    [Fact]
    public void A_refreshed_fire_asks_for_the_sound_again()
    {
        var (rig, conditions) = Wired();
        conditions.Add(PlayerRig.Player, TFCond.Burning, 5f);

        Assert.Equal(TFCondAdd.Refreshed, conditions.Add(PlayerRig.Player, TFCond.Burning, 8f));

        Assert.Equal(2, rig.Engine.CountOf("Sound.EntityEmitSound"));
    }

    [Fact]
    public void A_condition_with_no_sound_arm_emits_nothing()
    {
        var (rig, conditions) = Wired();

        conditions.Add(PlayerRig.Player, TFCond.Zoomed);
        conditions.Add(PlayerRig.Player, TFCond.Stealthed);

        Assert.Equal(0, rig.Engine.CountOf("Sound.EntityEmitSound"));
    }
}

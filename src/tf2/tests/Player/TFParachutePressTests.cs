using SourceSharp.Abi;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Movement;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The B.A.S.E. Jumper's airborne press over a real server carrier: the
/// toggle's pair must sit on the player's one condition set — stock's
/// <c>AddCond</c>/<c>RemoveCond</c> with the permanent duration
/// (<c>tf_gamemovement.cpp:1139-1151</c>) — so the ticker's next countdown
/// (<see cref="TFPlayerConditionTicker"/>) keeps it, which a bare word-write
/// does not (the tick sweeps a bit whose tracked duration is absent).
/// </summary>
public sealed class TFParachutePressTests
{
    /// <summary>
    /// One real press through the movement rules over the rig's attached
    /// carrier: the first airborne press puts both the active bit (cond 80)
    /// and the deployed record (cond 122) on and they stand through a
    /// condition tick; the second takes the active bit off and keeps the
    /// record, which also stands (:1139-1141, :1148).
    /// </summary>
    [Fact]
    public void The_real_press_holds_the_pair_through_the_condition_tick()
    {
        var rig = new PlayerRig().As(TFClass.Soldier);

        // The parachute's answer, on the player's own manager — what the
        // toggle's hook walks (:1135's ON_OTHER root), as in the shipped
        // schema fact's grant (TFPlayerLoadoutTests).
        var manager = new AttributeContainer();
        manager.Set(AttributeValue.OfFloat(new AttributeDefinition(5009, "attr 5009", "parachute_attribute",
            AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), 1));
        rig.Items.SetManager(PlayerRig.Index, manager);

        var rules = TFMovementFixtures.Rules(rig.Carriers,
                                             owners: new ServerOwners(rig), hooks: new AttributeHooks());
        var request = new JumpRequest { PlayerIndex = PlayerRig.Index, VerticalSpeed = 100f };

        // The airborne press: the pair goes on together (:1150-1151).
        rules.AirJump(in request);
        Assert.True(rig.State.InCond(TFCond.ParachuteActive));
        Assert.True(rig.State.InCond(TFCond.ParachuteDeployed));

        // The ticker's next frame (Order 20's countdown): the permanent pair
        // stands (:1144), a bit written to the words alone does not.
        TickOnce(rig);
        Assert.True(rig.State.InCond(TFCond.ParachuteActive));
        Assert.True(rig.State.InCond(TFCond.ParachuteDeployed));

        // The second press closes the active one and keeps the record, and
        // the record still stands through another tick (:1139-1141).
        rules.AirJump(in request);
        TickOnce(rig);
        Assert.False(rig.State.InCond(TFCond.ParachuteActive));
        Assert.True(rig.State.InCond(TFCond.ParachuteDeployed));
    }

    /// <summary>One countdown over the carrier's set, as the ticker runs it.</summary>
    private static void TickOnce(PlayerRig rig)
    {
        Assert.NotNull(rig.Carriers.ConditionsOf(PlayerRig.Index));
        if (rig.Carriers.ConditionsOf(PlayerRig.Index) is { } set)
            set.Tick(0.016f, healers: 0, default);
    }

    /// <summary>The owners seam as the server registers it (<see cref="TFServerWeaponOwners"/>):
    /// the condition set and the manager off the carrier and the items table.</summary>
    private sealed class ServerOwners(PlayerRig rig) : ITFWeaponOwners
    {
        public TFClass ClassOf(int playerIndex) => rig.Carriers.Get(playerIndex)?.Class() ?? TFClass.Undefined;

        public TFConditions? ConditionsOf(int playerIndex) => rig.Carriers.ConditionsOf(playerIndex);

        public float CritMult(int playerIndex) => rig.Carriers.Get(playerIndex)?.CritMultiplier() ?? 1f;

        public bool WinsCrits(int playerIndex) => false;

        public IAttributeProvider? ItemOf(int playerIndex, string weaponName) => rig.Items.Of(playerIndex, weaponName);

        public IAttributeProvider? ManagerOf(int playerIndex) => rig.Items.ManagerOf(playerIndex);

        public bool OffGround(int playerIndex) => true;   // the press is airborne (AirJump answers only those)

        public bool EyesUnderwater(int playerIndex) => false;

        public bool DuckingOf(int playerIndex) => false;
    }
}

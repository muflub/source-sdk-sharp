using SourceSharp.Abi;

using TF2.Server.Medigun;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Tests.Player;

using Xunit;

namespace TF2.Tests.Medigun;

/// <summary>
/// The 2.9 seam in the condition countdown: the ticker asks the medigun lane's
/// healing how many healers keep the player topped up (stock's
/// <c>GetNumHealers</c>, the argument of <c>ConditionGameRulesThink</c>'s
/// countdown) instead of the 2.3 none. The consumption half — how a healer
/// count speeds the fast-expiring conditions — is held by
/// <c>TFConditionsTests</c>; what these facts pin is that the count is asked,
/// of the right player, only once conditions exist to expire.
/// </summary>
public sealed class TFMedigunTickerTests
{
    /// <summary>The healing as the ticker sees it: it only ever asks the healer count.</summary>
    private sealed class CountingHealing : ITFPlayerHealing
    {
        /// <summary>The patient indices the count was asked for, in ask order.</summary>
        public readonly List<int> Asked = [];

        public int Healers { get; set; } = 1;

        public int NumHealers(int patientIndex)
        {
            Asked.Add(patientIndex);
            return Healers;
        }

        // The ticker calls nothing else on the healing.
        public void Heal(EntityHandle patient, EntityHandle healer, float healRate, float now,
                         float overhealBonus = ITFPlayerHealing.StandardOverhealBonus,
                         float decayMult = ITFPlayerHealing.StandardOverhealDecayMult,
                         bool dispenserHeal = false, int scorer = -1)
            => throw new InvalidOperationException();
        public float StopHealing(EntityHandle patient, EntityHandle healer, float now) => throw new InvalidOperationException();
        public void RecalculateChargeEffects(EntityHandle player, float now) => throw new InvalidOperationException();
        public void NoteReleasing(int playerIndex, bool releasing) => throw new InvalidOperationException();
        public int MaxBuffedHealth(EntityHandle patient, bool ignoreHealthOverMax = false) => throw new InvalidOperationException();
        public bool CanReceiveMedigunChargeEffect(EntityHandle player) => throw new InvalidOperationException();
        public void RemoveChargeEffect(EntityHandle player) => throw new InvalidOperationException();
        public void TestAndExpireChargeEffect(EntityHandle player, float now) => throw new InvalidOperationException();
        public void TickPatient(EntityHandle patient, float now, float frametime) => throw new InvalidOperationException();
        public void Extinguished(EntityHandle patient) => throw new InvalidOperationException();
    }

    /// <summary>A joined player, their conditions service and the ticker over the counting healing.</summary>
    private static (PlayerRig Rig, TFPlayerConditions Conditions, TFPlayerConditionTicker Ticker,
                    CountingHealing Healing) Tickered()
    {
        var rig = new PlayerRig();
        var healing = new CountingHealing();
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine);
        var ticker = new TFPlayerConditionTicker(rig.Carriers, conditions, rig.Engine, healing);
        rig.Extra.Add(ticker);
        rig.Join();
        return (rig, conditions, ticker, healing);
    }

    [Fact]
    public void The_condition_countdown_asks_the_healing_for_the_players_healer_count()
    {
        var (rig, conditions, ticker, healing) = Tickered();
        conditions.Add(PlayerRig.Player, TFCond.Burning, 3f);

        ticker.Tick(10f);                                       // the priming tick: no frametime, no ask
        ticker.Tick(10.5f);

        Assert.Collection(healing.Asked, i => Assert.Equal(PlayerRig.Index, i));
        Assert.True(conditions.InCond(PlayerRig.Player, TFCond.Burning));   // half a second of a three
    }

    [Fact]
    public void A_frame_with_no_frametime_and_a_player_with_no_carrier_are_never_asked()
    {
        var (rig, _, ticker, healing) = Tickered();
        var outsider = new TFPlayerConditionTicker(rig.Carriers, new TFPlayerConditions(rig.Carriers, rig.Engine),
                                                   rig.Engine, healing);
        outsider.Attach(new EntityHandle(0x0001_0000u | 5));   // a player who never joined

        ticker.Tick(10f);                                       // joined, but the priming frame
        outsider.Tick(10f);
        outsider.Tick(11f);                                     // a full second, and still no carrier

        Assert.Empty(healing.Asked);
    }

    [Fact]
    public void Without_the_healing_lane_the_countdown_counts_no_healers_as_2_3_did()
    {
        var rig = new PlayerRig();
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine);
        var ticker = new TFPlayerConditionTicker(rig.Carriers, conditions, rig.Engine);   // the 2.3 shape still compiles
        rig.Extra.Add(ticker);
        rig.Join();
        conditions.Add(PlayerRig.Player, TFCond.Burning, 3f);

        ticker.Tick(10f);
        ticker.Tick(10.5f);

        Assert.True(conditions.InCond(PlayerRig.Player, TFCond.Burning));     // the countdown still ran
    }
}

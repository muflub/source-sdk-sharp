using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;

using TF2.Tests.Combat;

using Xunit;

namespace TF2.Tests.Spy;

/// <summary>
/// The Dead Ringer's half of the watch: the ready toggle a mode-one watch
/// answers on the press (tf_weapon_invis.cpp:230-242), the feign a real hit
/// triggers on the armed spy (tf_player.cpp:9673-9692, SpyDeadRingerDeath
/// :15674-15697), and the buffs and instant cloak of
/// <c>OnAddFeignDeath</c> (tf_player_shared.cpp:7170-7191, :6993-7004).
/// </summary>
public sealed class DeadRingerTests
{
    /// <summary>The held item answering the watch's invis mode, as the schema carries it.</summary>
    private static IAttributeProvider Mode(int mode)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(5110, "attr 5110", "set_weapon_mode", AttributeDescriptionFormat.Additive,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), mode));
        return container;
    }

    /// <summary>
    /// A hit on an armed spy plays him dead once: the watch arms from a full
    /// meter alone, the feign halves the meter, takes the burning and the
    /// bleed off and rides the feign condition and its buffs for the stock
    /// seconds, and the one answer spends the ready bit.
    /// </summary>
    [Fact]
    public void A_feigned_hit_plays_the_armed_spy_dead_once()
    {
        var rig = new SpyRig();
        rig.Owners.Managers[SpyRig.Spy.EntryIndex] = Mode(1);       // HasFeignDeath, tf_weapon_invis.h:66-67
        rig.State.CloakMeter = 99f;

        // The watch arms from a full meter alone (:237): below it the press
        // answers and the hit stays a plain hit.
        Assert.False(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));
        Assert.False(rig.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex));
        Assert.Equal(99f, rig.State.CloakMeter, 4);

        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100.2f;                                     // past the toggle's cooldown
        Assert.False(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));   // the watch's own arm starts no cloak (:230-242)
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));

        Assert.True(rig.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex));                 // SpyDeadRingerDeath (:15674)
        Assert.Equal(50f, rig.State.CloakMeter, 4);                                   // :15688
        var conds = rig.Conditions.Of(SpyRig.Spy.EntryIndex)!;
        Assert.True(conds.Has(TFCond.FeignDeath));
        Assert.Equal(3f, conds.Duration(TFCond.FeignDeath), 3);                       // tf_feign_death_duration (:180)
        Assert.True(conds.Has(TFCond.Stealthed));                                     // OnAddFeignDeath (:7180)
        Assert.True(conds.Has(TFCond.SpeedBoost));
        Assert.True(conds.Has(TFCond.AfterburnImmune));                               // :7185-7186
        Assert.Equal(3f, conds.Duration(TFCond.SpeedBoost), 3);                       // tf_feign_death_speed_duration (:181)

        // The one answer spends the ready bit (:9678): the next hit lands plain.
        Assert.False(rig.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex));
        Assert.Equal(50f, rig.State.CloakMeter, 4);
    }

    /// <summary>The other half of the toggle (:232-236): with the bit armed,
    /// the next press lays it down and a hit then answers nothing.</summary>
    [Fact]
    public void The_second_press_lays_the_armed_watch_down()
    {
        var rig = new SpyRig();
        rig.Owners.Managers[SpyRig.Spy.EntryIndex] = Mode(1);
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100f;
        Assert.False(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));    // armed (:237-241)

        rig.Clock.Now = 100.2f;
        Assert.False(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));    // off again (:232-236)
        Assert.False(rig.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex));
        Assert.Equal(100f, rig.State.CloakMeter, 4);
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.FeignDeath));
    }

    /// <summary>A taunted hit consumes the ready bit but plays no death
    /// (:9679): the meter stands and the condition never lands.</summary>
    [Fact]
    public void A_taunting_spy_spends_the_ready_bit_without_dying()
    {
        var rig = new SpyRig();
        rig.Owners.Managers[SpyRig.Spy.EntryIndex] = Mode(1);
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100f;
        Assert.False(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));    // armed
        rig.Conditions.Add(SpyRig.Spy, TFCond.Taunting);

        Assert.False(rig.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex));                // the taunt gate (:9679)
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.FeignDeath));
        Assert.Equal(100f, rig.State.CloakMeter, 4);

        rig.Conditions.Remove(SpyRig.Spy, TFCond.Taunting);
        Assert.False(rig.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex));                // the bit went with the taunted hit (:9678)
    }

    /// <summary>A telefrag or croc hit stands outside the spy's arm
    /// (:9673): the scoped hit answers through no watch, and the next plain
    /// hit still plays the spy dead on the armed bit.</summary>
    [Fact]
    public void A_telefrag_never_answers_through_the_watch()
    {
        var rig = new SpyRig();
        rig.Owners.Managers[SpyRig.Spy.EntryIndex] = Mode(1);
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100f;
        Assert.False(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));    // armed

        using (rig.Scopes.Push(TFDamageCustom.Telefrag, TFWeaponId.None, ""))         // the kinds :9673 stands outside
            Assert.False(rig.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex));

        Assert.True(rig.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex));                  // the plain hit still answers (:9673)
        Assert.Equal(50f, rig.State.CloakMeter, 4);
    }

    /// <summary>The damage-side arm: every hit on a living spy victim is
    /// answered through the watch's seam (:9673), and a non-spy victim's hit
    /// stands outside it.</summary>
    [Fact]
    public void The_layer_asks_only_a_spy_victim_about_the_feign()
    {
        var spies = new SpyRecorder();
        var rig = new CombatRig(spies: spies);
        rig.VictimState.PlayerClass = (int)TFClass.Spy;

        rig.Hit(5f, DamageType.Bullet);

        Assert.Single(spies.Asked);
        Assert.Equal(CombatRig.Victim.EntryIndex, spies.Asked[0]);

        var plain = new SpyRecorder();
        var scoutRig = new CombatRig(spies: plain);                                   // the scout victim of the rig's own draw
        scoutRig.Hit(5f, DamageType.Bullet);
        Assert.Empty(plain.Asked);
    }

    /// <summary>The watch seam as the layer's arm asks it: the asks recorded,
    /// every other answer empty.</summary>
    private sealed class SpyRecorder : ITFSpyState
    {
        public List<int> Asked { get; } = [];

        public bool Swing(int ownerIndex, float range) => false;
        public bool Smack(int ownerIndex) => false;
        public bool ShouldDisguiseOnBackstab(int ownerIndex) => false;
        public bool DisguiseOnKill(int ownerIndex) => false;
        public void RemoveDisguise(int playerIndex) { }
        public void RemoveInvisibility(int playerIndex) { }
        public bool ActivateInvisibilityWatch(int playerIndex) => false;
        public bool FeignDeathHit(int victimIndex) { Asked.Add(victimIndex); return false; }
        public bool KnifeReady(int ownerIndex, float now) => true;
        public void MeltKnife(int ownerIndex, float now, float seconds) { }
        public bool Disguise(int playerIndex, TFClass disguiseClass, TFTeam disguiseTeam) => false;
        public bool LastDisguise(int playerIndex, TFClass desired = TFClass.Undefined) => false;
    }
}

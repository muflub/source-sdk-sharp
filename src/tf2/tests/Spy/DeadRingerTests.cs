using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;

using TF2.Tests.Combat;

using TF2.Server.Combat;

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

    /// <summary>The watch's seat (TF2.Server.Player.TFSpyComponent.WatchScript): the mode the press reads is the watch item's own.</summary>
    private static Dictionary<string, IAttributeProvider> Watch(IAttributeProvider watch) => new() { [TF2.Server.Player.TFSpyComponent.WatchScript] = watch };

    /// <summary>
    /// The Ambassador beside the Dead Ringer (the Spy lane's review, F2): the player's manager sums both items' mode to 2,
    /// a motion cloak, but the press reads the watch's own 1 and arms the feign, no cloak.
    /// </summary>
    [Fact]
    public void The_Ambassador_beside_the_Dead_Ringer_still_arms_the_feign()
    {
        var rig = new SpyRig();
        rig.Owners.Managers[SpyRig.Spy.EntryIndex] = Mode(2);       // the Ambassador's 1 and the watch's 1, summed on the player
        rig.Owners.ByName[SpyRig.Spy.EntryIndex] = Watch(Mode(1));
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100f;
        Assert.False(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));
        Assert.True(rig.State.FeignDeathReady);
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));
    }

    /// <summary>The Ambassador beside the Invis Watch: the player's manager reads 1, a feign watch's mode, but the watch's own is 0 and the press cloaks.</summary>
    [Fact]
    public void The_Ambassador_beside_the_Invis_Watch_cloaks()
    {
        var rig = new SpyRig();
        rig.Owners.Managers[SpyRig.Spy.EntryIndex] = Mode(1);       // the Ambassador's set_weapon_mode 1
        rig.Owners.ByName[SpyRig.Spy.EntryIndex] = Watch(Mode(0));
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100f;
        Assert.True(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));
        Assert.False(rig.State.FeignDeathReady);
        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Stealthed));
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
        rig.Owners.ByName[SpyRig.Spy.EntryIndex] = Watch(Mode(1));       // HasFeignDeath, tf_weapon_invis.h:66-67
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
        rig.Owners.ByName[SpyRig.Spy.EntryIndex] = Watch(Mode(1));
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
        rig.Owners.ByName[SpyRig.Spy.EntryIndex] = Watch(Mode(1));
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
        rig.Owners.ByName[SpyRig.Spy.EntryIndex] = Watch(Mode(1));
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100f;
        Assert.False(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));    // armed

        using (rig.Scopes.Push(TFDamageCustom.Telefrag, TFWeaponId.None, ""))         // the kinds :9673 stands outside
            Assert.False(rig.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex));

        Assert.True(rig.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex));                  // the plain hit still answers (:9673)
        Assert.Equal(50f, rig.State.CloakMeter, 4);
    }

    /// <summary>The fake death's body (:15724-15782) through the real press and
    /// a real hit carrying its attacker: the stop-audio (:15724), the death's
    /// notice (:15733), the pack at the death spot (:15740) and the corpse
    /// (:15782), in stock's own order.</summary>
    [Fact]
    public void The_feigned_death_runs_the_audio_the_notice_the_pack_and_the_corpse_in_order()
    {
        var rig = new SpyRig();
        rig.Owners.ByName[SpyRig.Spy.EntryIndex] = Watch(Mode(1));
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100f;
        Assert.False(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));    // armed (:237-241)

        Assert.True(rig.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex, CombatRig.Victim));   // the hit's attacker (:15736)

        string[] wanted = ["Sound.EntityEmitSound", "GameEvents.EventCreate", "Entities.GetOrigin",
                           "EntityRender.GetModelName", "Entities.Create"];
        var seq = rig.Engine.Calls.Select(c => c.Member).Where(wanted.Contains).Distinct().ToArray();
        Assert.Equal(wanted, seq);                                                    // stock's order (:15724-15782)

        // The notice is the death's own, and the attacker rides on it (:15736).
        Assert.Equal("player_death", (rig.Engine.Last("GameEvents.EventCreate") ?? default).ArgAs<string>(0));
    }

    /// <summary>The think's early end of the feign (:2997-3002): the add's stamp
    /// (:7190) is the buffs' window, so the think takes the condition off once
    /// that window closes — the strict <strong>less than</strong> (:2999)
    /// leaving the close itself standing.</summary>
    [Fact]
    public void The_think_ends_the_feign_at_the_close_of_the_buffs_window()
    {
        var rig = new SpyRig();
        rig.Owners.ByName[SpyRig.Spy.EntryIndex] = Watch(Mode(1));
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100f;
        Assert.False(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));    // armed (:237-241)

        Assert.True(rig.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex));                  // added at now 100 (:15695), stamped 103 (:7190)

        rig.Slice.Tick(103f);
        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.FeignDeath));            // stamp == now stands (:2999)

        rig.Slice.Tick(104f);
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.FeignDeath));           // the window closed (:3001)
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

    /// <summary>The armed watch through the layer (:7399-7401): while the
    /// ready bit answers, the hit takes the activate scale.</summary>
    [Fact]
    public void An_armed_watch_answers_the_hit_at_the_activate_scale()
    {
        var rig = new CombatRig();
        rig.VictimState.PlayerClass = (int)TFClass.Spy;
        rig.VictimState.CloakMeter = 100f;
        rig.VictimState.FeignDeathReady = true;                       // armed, no feign yet (:7393)
        rig.Engine.Returns("Entities.GetHealth", 20);                 // after 25 stands over it: a trim, not a save

        Assert.Equal(25f, rig.Hit(100f, DamageType.Bullet), 4);       // 100 * 0.25 (:7401)
    }

    /// <summary>The feign's own scale through the layer (:7396): a half-full
    /// meter answers the feign scale and a draining one rides toward the
    /// stealth reduction.</summary>
    [Fact]
    public void A_feigning_spy_takes_the_scale_his_cloak_meter_answers()
    {
        var rig = new CombatRig();
        rig.VictimState.PlayerClass = (int)TFClass.Spy;
        rig.VictimState.CloakMeter = 50f;
        rig.Conditions.Add(CombatRig.Victim, TFCond.FeignDeath);
        rig.Engine.Returns("Entities.GetHealth", 30);                 // after 35 stands over it: a trim, not a save

        Assert.Equal(35f, rig.Hit(100f, DamageType.Bullet), 4);       // halfway: the feign scale (:7396)

        var drained = new CombatRig();
        drained.VictimState.PlayerClass = (int)TFClass.Spy;
        drained.VictimState.CloakMeter = 25f;
        drained.Conditions.Add(CombatRig.Victim, TFCond.FeignDeath);
        drained.Engine.Returns("Entities.GetHealth", 50);             // after 57.5 stands over it too

        Assert.Equal(57.5f, drained.Hit(100f, DamageType.Bullet), 4); // halfway between the scales (:7396)
    }

    /// <summary>The save check (:7412-7422): the scale that would have killed
    /// and did not fires the watch's event, spy and attacker by user id, the
    /// hit broadcast to nobody.</summary>
    [Fact]
    public void The_scale_that_saves_the_spy_fires_the_watchs_save_event()
    {
        var rig = new CombatRig();
        rig.VictimState.PlayerClass = (int)TFClass.Spy;
        rig.VictimState.CloakMeter = 100f;
        rig.VictimState.FeignDeathReady = true;
        rig.Engine.Returns("Entities.GetHealth", 30);                 // the save check's floor (:7413)
        rig.Engine.Returns("GameEvents.EventCreate", new IntPtr(1)); // the event file declares it
        rig.Engine.Returns("GameEvents.EventFire", true);

        Assert.Equal(10f, rig.Hit(40f, DamageType.Bullet), 4);        // before 40 >= 30, after 10 < 30 (:7413)

        Assert.Equal(1, rig.Engine.CountOf("GameEvents.EventCreate"));
        Assert.Equal("deadringer_cheat_death", (rig.Engine.Last("GameEvents.EventCreate") ?? default).ArgAs<string>(0));
        var sets = rig.Engine.AllOf("GameEvents.EventSetInt").ToArray();   // args: handle, key, value
        Assert.Equal("spy", sets[0].ArgAs<string>(1));                // :7418
        Assert.Equal(103, sets[0].ArgAs<int>(2));
        Assert.Equal("attacker", sets[1].ArgAs<string>(1));           // :7419
        Assert.Equal(104, sets[1].ArgAs<int>(2));
        Assert.True((rig.Engine.Last("GameEvents.EventFire") ?? default).ArgAs<bool>(1));   // dontBroadcast (:7420)
    }

    /// <summary>The same check's other half (:7413): a hit the scale only
    /// trims, not saves, leaves the event uncreated.</summary>
    [Fact]
    public void A_hit_the_scale_only_trims_leaves_the_save_event_unfired()
    {
        var rig = new CombatRig();
        rig.VictimState.PlayerClass = (int)TFClass.Spy;
        rig.VictimState.CloakMeter = 100f;
        rig.VictimState.FeignDeathReady = true;
        rig.Engine.Returns("Entities.GetHealth", 30);
        rig.Engine.Returns("GameEvents.EventCreate", new IntPtr(1));

        Assert.Equal(0.5f, rig.Hit(2f, DamageType.Bullet), 4);        // before 2 is under the 30-health floor

        Assert.Equal(0, rig.Engine.CountOf("GameEvents.EventCreate"));
    }

    /// <summary>The fake corpse's dress, through the armed hit on a spy who
    /// completed a teammate disguise: the body wears the disguise's team and
    /// class (:15870-15874) — the pair the client skins and poses the body by
    /// (c_tf_player.cpp:712-719).</summary>
    [Fact]
    public void A_fakes_corpse_wears_the_teammate_disguise()
    {
        var rig = NewDisguisedRig(TFClass.Soldier, TFTeam.Red);           // a RED spy disguised as a RED soldier
        Assert.True(rig.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex));      // the armed hit dresses the corpse (:15782)

        Assert.Equal((int)TFTeam.Red, rig.CorpseBody.State.Team);         // :15872
        Assert.Equal((int)TFClass.Soldier, rig.CorpseBody.State.Class);   // :15873
    }

    /// <summary>The dress's other arm (:15875-15879): with no disguise, or one
    /// worn on the enemy's team — stock's bDisguised needs the disguise team
    /// to be the spy's own (:15713) — the corpse is the spy he died as.</summary>
    [Fact]
    public void A_fakes_corpse_wears_the_spy_without_a_teammates_disguise()
    {
        var plain = NewArmedRig();
        Assert.True(plain.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex));    // the armed hit, no disguise made
        Assert.Equal((int)TFTeam.Red, plain.CorpseBody.State.Team);       // :15877
        Assert.Equal((int)TFClass.Spy, plain.CorpseBody.State.Class);     // :15878

        var enemy = NewDisguisedRig(TFClass.HeavyWeapons, TFTeam.Blue);   // the enemy's colours
        Assert.True(enemy.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex));
        Assert.Equal((int)TFTeam.Red, enemy.CorpseBody.State.Team);       // the team he died on, not the disguise's
        Assert.Equal((int)TFClass.Spy, enemy.CorpseBody.State.Class);
    }

    /// <summary>The fake corpse's burn (:15817, stock's bBurning of :15716): a
    /// burning spy's body arrives on fire — the answer taken before the feign
    /// strips the burn off him (:15719) — the burn surviving any disguise but
    /// a pyro's.</summary>
    [Fact]
    public void A_burning_spys_fake_corpse_burns()
    {
        var burning = NewArmedRig();
        burning.Conditions.Add(SpyRig.Spy, TFCond.Burning);              // the burn the hit left
        Assert.True(burning.Conditions.InCond(SpyRig.Spy, TFCond.Burning));
        Assert.True(burning.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex));  // the armed hit
        Assert.True(burning.CorpseBody.State.Burning);                    // :15817
        Assert.False(burning.Conditions.InCond(SpyRig.Spy, TFCond.Burning));   // the strip (:15719)

        var soldier = NewDisguisedRig(TFClass.Soldier, TFTeam.Red);       // burning under a soldier's colour
        soldier.Conditions.Add(SpyRig.Spy, TFCond.Burning);
        Assert.True(soldier.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex));
        Assert.True(soldier.CorpseBody.State.Burning);                    // :15716's !disguised arm

        var pyro = NewDisguisedRig(TFClass.Pyro, TFTeam.Red);             // the disguise was a pyro's
        pyro.Conditions.Add(SpyRig.Spy, TFCond.Burning);
        Assert.True(pyro.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex));
        Assert.False(pyro.CorpseBody.State.Burning);                      // :15716's pyro arm
    }

    /// <summary>The fake corpse's pieces (:15816, the bGib of :15729-15731):
    /// the hit that triggered the feign decides, by the death walk's own rule —
    /// a critical blast gibing, a plain bullet leaving the body whole.</summary>
    [Fact]
    public void The_fake_corpse_gibs_by_the_hit_that_triggered_the_feign()
    {
        var critBlast = NewArmedRig();
        critBlast.Records.Note(SpyRig.Spy.EntryIndex,
            new TFHitRecord(SpyRig.Victim, 80f, DamageType.Blast, TFCritType.Mini,
                            TFDamageCustom.None, TFWeaponId.None, "tf_weapon_rocketlauncher", 100.1f), damager: false);
        Assert.True(critBlast.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex));
        Assert.True(critBlast.CorpseBody.State.Gib);                       // :15816, the crit blast (:10959)

        var plain = NewArmedRig();
        plain.Records.Note(SpyRig.Spy.EntryIndex,
            new TFHitRecord(SpyRig.Victim, 40f, DamageType.Bullet, TFCritType.None,
                            TFDamageCustom.None, TFWeaponId.None, "tf_weapon_smg", 100.1f), damager: false);
        Assert.True(plain.Spies.FeignDeathHit(SpyRig.Spy.EntryIndex));
        Assert.False(plain.CorpseBody.State.Gib);                          // a bullet leaves a body (:10953)
    }

    /// <summary>An armed, full spy at :100.2 — the door the feign answers
    /// through, the press that arms the watch (:237-241) already made.</summary>
    private static SpyRig NewArmedRig()
    {
        var rig = new SpyRig();
        rig.Owners.ByName[SpyRig.Spy.EntryIndex] = Watch(Mode(1));
        rig.State.CloakMeter = 100f;
        rig.Clock.Now = 100.2f;
        Assert.False(rig.Spies.ActivateInvisibilityWatch(SpyRig.Spy.EntryIndex));   // the press arms, not cloaks
        return rig;
    }

    /// <summary><see cref="NewArmedRig"/>, whose spy then completed a disguise
    /// at :8344 — the carrier words the dress reads.</summary>
    private static SpyRig NewDisguisedRig(TFClass disguiseClass, TFTeam disguiseTeam)
    {
        var rig = NewArmedRig();
        Assert.True(rig.Slice.Disguise(100.2f, disguiseClass, disguiseTeam));
        rig.Slice.Tick(102.3f);
        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguised));
        Assert.Equal((int)disguiseClass, rig.State.DisguiseClass);
        Assert.Equal((int)disguiseTeam, rig.State.DisguiseTeam);

        rig.State.CloakMeter = 100f;
        return rig;
    }

    /// <summary>The watch seam as the layer's arm asks it: the asks recorded,
    /// every other answer empty.</summary>
    private sealed class SpyRecorder : ITFSpyState
    {
        public List<int> Asked { get; } = [];

        public bool Swing(int ownerIndex, float range) => false;
        public bool Smack(int ownerIndex) => false;
        public bool IsBackstab(int ownerIndex) => false;
        public bool ShouldDisguiseOnBackstab(int ownerIndex) => false;
        public bool DisguiseOnKill(int ownerIndex) => false;
        public void RemoveDisguise(int playerIndex) { }
        public void RemoveInvisibility(int playerIndex) { }
        public bool ActivateInvisibilityWatch(int playerIndex) => false;
        public bool FeignDeathHit(int victimIndex, EntityHandle attacker = default) { Asked.Add(victimIndex); return false; }
        public bool KnifeReady(int ownerIndex, float now) => true;
        public bool DecreaseKnifeRegenerationTime(int ownerIndex, float value) => false;   // the exists gate (:163)
        public void MeltKnife(int ownerIndex, float now, float seconds) { }
        public bool Disguise(int playerIndex, TFClass disguiseClass, TFTeam disguiseTeam) => false;
        public bool LastDisguise(int playerIndex, TFClass desired = TFClass.Undefined) => false;
    }
}

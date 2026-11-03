using SourceSharp.Abi;
using SourceSharp.Tests;

using TF2.Server.Medigun;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Tests;

using Xunit;

namespace TF2.Tests.Medigun;

/// <summary>
/// The player half of the medigun: <c>CTFPlayerShared::Heal</c>/<c>StopHealing</c>,
/// the heal accumulate, the overheal cap and its drain, the second-window
/// <c>player_healed</c>, and the charge-effect pair (uber on, wear-off, expiry).
/// Patient 3, medic 1; health and class maxes come from <see cref="MedigunRig.Give"/>.
/// </summary>
public sealed class TFPlayerHealingTests
{
    private static EntityHandle H(int index) => MedigunRig.HandleFor(index);

    // ---- Heal / StopHealing / the healers list ----------------------------------

    [Fact]
    public void Healing_registers_the_healer_and_puts_HEALTH_BUFF_on_the_patient_with_the_medic_as_provider()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);

        rig.Healing.Heal(H(3), H(1), 24f, 10f);

        Assert.Equal(1, rig.Healing.NumHealers(3));
        Assert.Contains("add 3 HealthBuff d-1 p1", rig.Conditions.Calls);
    }

    [Fact]
    public void The_last_healer_leaving_takes_HEALTH_BUFF_away()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        rig.Healing.Heal(H(3), H(2), 24f, 10f);

        rig.Healing.StopHealing(H(3), H(1), 10.5f);
        Assert.True(rig.Conditions.Holds(3, TFCond.HealthBuff));
        Assert.DoesNotContain("remove 3 HealthBuff", rig.Conditions.Calls);

        rig.Healing.StopHealing(H(3), H(2), 10.5f);
        Assert.False(rig.Conditions.Holds(3, TFCond.HealthBuff));
        Assert.Contains("remove 3 HealthBuff", rig.Conditions.Calls);
    }

    [Fact]
    public void StopHealing_answers_the_heal_the_healer_had_accumulated()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.NoteDamage(3, time: 10f);          // recent: scale 1
        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        rig.Healing.TickPatient(H(3), now: 10f, frametime: 1f);    // pays out 24

        Assert.Equal(24f, rig.Healing.StopHealing(H(3), H(1), 10.5f));
    }

    [Fact]
    public void A_still_registered_healer_being_healed_again_keeps_its_accumulated_heal()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.NoteDamage(3, time: 10f);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        rig.Healing.TickPatient(H(3), now: 10f, frametime: 1f);    // the entry carries 24

        rig.Healing.Heal(H(3), H(1), 24f, 10f);      // stock replaces the entry, carrying flHealAccum across
        Assert.Equal(24f, rig.Healing.StopHealing(H(3), H(1), 10.5f));
    }

    // ---- the heal rate, the out-of-combat ramp, the cap and the drain -----------

    [Fact]
    public void A_recently_hurt_patient_takes_the_heal_rate_once_per_second()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.NoteDamage(3, time: 10f);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);

        rig.Healing.TickPatient(H(3), now: 10f, frametime: 0.5f);   // scale 1: 24/s for half a second

        Assert.Contains("take 3 12 True", rig.Resupply.Takes);
    }

    [Fact]
    public void A_patient_unharmed_for_a_whole_ramp_heals_at_three_times_the_rate()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);                  // no damage record: past the ramp, scale 3
        rig.Healing.Heal(H(3), H(1), 24f, 10f);

        rig.Healing.TickPatient(H(3), now: 100f, frametime: 1f);

        Assert.Contains("take 3 72 True", rig.Resupply.Takes);
    }

    [Fact]
    public void The_heal_stops_at_the_buffed_max()
    {
        var rig = new MedigunRig();
        rig.Give(3, 180, 125);                  // buffed max: (int)(125 × 1.5 / 5) × 5 = 185
        rig.NoteDamage(3, time: 10f);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);

        rig.Healing.TickPatient(H(3), now: 10f, frametime: 1f);     // asks 24, the cap takes 5

        Assert.Contains("take 3 5 True", rig.Resupply.Takes);
        Assert.DoesNotContain("take 3 24 True", rig.Resupply.Takes);
    }

    [Fact]
    public void A_healer_whose_overheal_bonus_is_reached_heals_nothing_and_the_drain_goes_on()
    {
        var rig = new MedigunRig();
        rig.Give(3, 450, 300);                  // heavy at 450 of a 1.5×300 cap: exactly the bonus
        rig.Healing.Heal(H(3), H(1), 24f, 10f);

        rig.Healing.TickPatient(H(3), now: 10f, frametime: 1f);     // drain: (450−300)/15 = 10/s

        Assert.Empty(rig.Resupply.Takes);
        Assert.Equal(440, rig.Health(3));       // stock drops m_iHealth itself
    }

    [Fact]
    public void No_drain_while_a_healer_is_still_keeping_the_patient_up()
    {
        var rig = new MedigunRig();
        rig.Give(3, 400, 300);                  // 1.33×: inside the bonus, so this healer still heals
        rig.NoteDamage(3, time: 10f);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);

        rig.Healing.TickPatient(H(3), now: 10f, frametime: 1f);

        Assert.Contains("take 3 24 True", rig.Resupply.Takes);
        Assert.False(rig.Engine.Called("Entities.SetHealth"));      // bDecayHealth was cleared
        Assert.Equal(400, rig.Health(3));
    }

    [Fact]
    public void The_drain_uses_the_unclamped_buffed_max()
    {
        // The drain asks GetMaxBuffedHealth(false, true) (tf_player_shared.cpp:2704): the
        // floored boost max without the current-health clamp (:2256-2264) — a constant
        // rate, not one that speeds up the further over max the player sits.
        var rig = new MedigunRig();
        rig.Give(3, 200, 125);                  // an item put them at 200 of a 185 boost max

        rig.Healing.TickPatient(H(3), now: 10f, frametime: 1f);

        Assert.Equal(196, rig.Health(3));       // (185 − 125)/15 = 4/s, not (200 − 125)/15 = 5/s
    }

    [Fact]
    public void HEALTH_OVERHEALED_comes_on_above_max_and_off_at_it()
    {
        var rig = new MedigunRig();
        rig.Give(3, 400, 300);                  // overhealed by no healer of mine: the drain arm still runs

        rig.Healing.TickPatient(H(3), now: 10f, frametime: 0.05f);  // 0.5/s of fraction: nothing drains yet
        Assert.Contains("add 3 HealthOverhealed d-1 p8191", rig.Conditions.Calls);   // permanent, no provider

        rig.Give(3, 300, 300);
        rig.Healing.TickPatient(H(3), now: 11f, frametime: 0.05f);
        Assert.Contains("remove 3 HealthOverhealed", rig.Conditions.Calls);
        Assert.False(rig.Conditions.Holds(3, TFCond.HealthOverhealed));
    }

    // ---- the stat and the second-window event ------------------------------------

    [Fact]
    public void The_medic_is_credited_the_healing_health()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.NoteDamage(3, time: 10f);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);

        rig.Healing.TickPatient(H(3), now: 10f, frametime: 1f);

        Assert.Contains("Healing 1 24", rig.Stats.Heals);
    }

    [Fact]
    public void Riding_the_cap_with_no_fresh_damage_credits_no_stat_and_fires_no_event()
    {
        var rig = new MedigunRig();
        rig.Give(3, 184, 125);                  // buffed max 185: the report gate (boost max − health > 1) fails
        rig.Healing.Heal(H(3), H(1), 24f, 10f);       // and nothing hurt: its fresh-damage half fails too

        rig.Healing.TickPatient(H(3), now: 100f, frametime: 0.5f);  // scale 3: 36 asks, 1 fits

        Assert.Contains("take 3 1 True", rig.Resupply.Takes);
        Assert.Empty(rig.Stats.Heals);
        Assert.Empty(rig.Events.Fired);
    }

    [Fact]
    public void player_healed_reports_the_seconds_healing_to_the_patient_and_the_medic()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.NoteDamage(3, time: 10f);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);

        rig.Healing.TickPatient(H(3), now: 10f, frametime: 1f);     // 24 taken, 124 of a 125 max

        Assert.Contains("amount=24 event=player_healed healer=101 patient=103 priority=1", rig.Events.Fired);
    }

    // ---- the charge effects: who is invulnerable -----------------------------------

    [Fact]
    public void Heal_recalculates_the_patient_s_charge_effect()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Healing.NoteReleasing(1, true);              // the medic is already releasing

        rig.Healing.Heal(H(3), H(1), 24f, 10f);          // tf_player_shared.cpp:8808: Heal ends in the recalc

        Assert.Contains("add 3 Invulnerable d9 p1", rig.Conditions.Calls);
    }

    [Fact]
    public void StopHealing_starts_the_patient_s_uber_wearing_off()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        rig.Healing.NoteReleasing(1, true);
        rig.Healing.RecalculateChargeEffects(H(3), now: 10f);
        Assert.True(rig.Conditions.Holds(3, TFCond.Invulnerable));

        rig.Healing.StopHealing(H(3), H(1), 10.5f);      // tf_player_shared.cpp:8844: StopHealing ends in the recalc

        Assert.True(rig.Conditions.Holds(3, TFCond.Invulnerable));           // non-instant: it wears off
        Assert.True(rig.Conditions.Holds(3, TFCond.InvulnerableWearingoff));
        rig.Healing.TestAndExpireChargeEffect(H(3), now: 11.6f);
        Assert.False(rig.Conditions.Holds(3, TFCond.Invulnerable));
    }

    [Fact]
    public void A_releasing_medic_makes_his_patient_invulnerable_with_himself_as_provider()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        rig.Healing.NoteReleasing(1, true);

        rig.Healing.RecalculateChargeEffects(H(3), now: 10f);

        Assert.Contains("add 3 Invulnerable d9 p1", rig.Conditions.Calls);   // 9 = chargerelease_rate 8 + 1
    }

    [Fact]
    public void A_releasing_medic_is_invulnerable_by_his_own_medigun_too()
    {
        var rig = new MedigunRig();
        rig.Healing.NoteReleasing(1, true);

        rig.Healing.RecalculateChargeEffects(H(1), now: 10f);

        Assert.Contains("add 1 Invulnerable d9 p1", rig.Conditions.Calls);
    }

    [Fact]
    public void A_patient_that_stops_being_covered_wears_the_uber_off_over_tf_invuln_time()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        rig.Healing.NoteReleasing(1, true);
        rig.Healing.RecalculateChargeEffects(H(3), now: 10f);
        rig.Healing.NoteReleasing(1, false);

        rig.Healing.RecalculateChargeEffects(H(3), now: 10.5f);             // offTime = 10.5 + 1.0
        Assert.True(rig.Conditions.Holds(3, TFCond.InvulnerableWearingoff));
        Assert.True(rig.Conditions.Holds(3, TFCond.Invulnerable));

        rig.Healing.TestAndExpireChargeEffect(H(3), now: 11.6f);
        Assert.False(rig.Conditions.Holds(3, TFCond.Invulnerable));
        Assert.False(rig.Conditions.Holds(3, TFCond.InvulnerableWearingoff));
    }

    [Fact]
    public void A_wear_off_already_pending_is_not_pushed_forward_by_another_recalc()
    {
        // SetChargeEffect returns early when m_flChargeEffectOffTime is already set
        // (tf_player_shared.cpp:9112-9114): re-acquiring the patient during their
        // wear-off cannot extend the expiry.
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        rig.Healing.NoteReleasing(1, true);
        rig.Healing.RecalculateChargeEffects(H(3), now: 10f);
        rig.Healing.NoteReleasing(1, false);
        rig.Healing.RecalculateChargeEffects(H(3), now: 10.5f);   // wearing off; expires past 11.5

        rig.Healing.Heal(H(3), H(1), 24f, 11f);                    // re-acquired during the wear-off

        rig.Healing.TestAndExpireChargeEffect(H(3), now: 11.6f);
        Assert.False(rig.Conditions.Holds(3, TFCond.Invulnerable));   // expired on the original clock
    }

    [Fact]
    public void A_medic_that_starts_releasing_again_clears_a_pending_wear_off()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        rig.Healing.NoteReleasing(1, true);
        rig.Healing.RecalculateChargeEffects(H(3), now: 10f);
        rig.Healing.NoteReleasing(1, false);
        rig.Healing.RecalculateChargeEffects(H(3), now: 10.5f);

        rig.Healing.NoteReleasing(1, true);
        rig.Healing.RecalculateChargeEffects(H(3), now: 11f);
        Assert.Contains("remove 3 InvulnerableWearingoff", rig.Conditions.Calls);

        rig.Healing.TestAndExpireChargeEffect(H(3), now: 99f);              // no expiry: the offTime was cleared
        Assert.True(rig.Conditions.Holds(3, TFCond.Invulnerable));
    }

    [Fact]
    public void A_flag_carrier_receives_no_charge_effect()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        rig.Healing.NoteReleasing(1, true);
        rig.Ctf.Carriers.Add(3);

        rig.Healing.RecalculateChargeEffects(H(3), now: 10f);

        Assert.False(rig.Conditions.Holds(3, TFCond.Invulnerable));
        Assert.DoesNotContain("add 3 Invulnerable d9 p1", rig.Conditions.Calls);
    }

    [Fact]
    public void RemoveChargeEffect_takes_both_conditions_instantly()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        rig.Healing.NoteReleasing(1, true);
        rig.Healing.RecalculateChargeEffects(H(3), now: 10f);
        rig.Healing.NoteReleasing(1, false);
        rig.Healing.RecalculateChargeEffects(H(3), now: 10.5f);             // wearing off with a pending timer

        rig.Healing.RemoveChargeEffect(H(3));

        Assert.False(rig.Conditions.Holds(3, TFCond.Invulnerable));
        Assert.False(rig.Conditions.Holds(3, TFCond.InvulnerableWearingoff));
    }

    // ---- the dispenser healer (stock's bDispenserHeal, tf_player_shared.cpp:2440-2528) --

    [Fact]
    public void A_dispenser_heals_at_a_constant_rate_not_the_out_of_combat_scale()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);                  // no damage record: a medic's heal here would ride scale 3
        rig.Healing.Heal(H(3), H(9), 10f, 100f, dispenserHeal: true, scorer: 2);

        rig.Healing.TickPatient(H(3), now: 100f, frametime: 0.5f);

        Assert.Contains("take 3 5 True", rig.Resupply.Takes);       // the medic's arm would ask 15
    }

    [Fact]
    public void A_dispensers_heal_never_overheals()
    {
        var rig = new MedigunRig();
        rig.Give(3, 125, 125);                  // at max: the bonus of 1 the dispenser hands in is reached
        rig.Healing.Heal(H(3), H(9), 10f, 100f, overhealBonus: 1f, dispenserHeal: true, scorer: 2);

        rig.Healing.TickPatient(H(3), now: 100f, frametime: 1f);

        Assert.Empty(rig.Resupply.Takes);
    }

    [Fact]
    public void A_dispensers_accumulation_is_carried_and_answers_on_stop()
    {
        // Stock's dispenser arm tracks the heal it paid (tf_player_shared.cpp:2506-2507),
        // the sum StopHealing hands back to the builder.
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Healing.Heal(H(3), H(9), 10f, 100f, overhealBonus: 1f, dispenserHeal: true, scorer: 2);

        rig.Healing.TickPatient(H(3), now: 100f, frametime: 1f);

        Assert.Equal(10f, rig.Healing.StopHealing(H(3), H(9), now: 101f));
    }

    [Fact]
    public void The_builder_scores_a_dispensers_heal()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.NoteDamage(3, time: 100f);
        rig.Healing.Heal(H(3), H(9), 24f, 100f, dispenserHeal: true, scorer: 2);

        rig.Healing.TickPatient(H(3), now: 100f, frametime: 0.5f);

        Assert.Contains("Healing 2 12", rig.Stats.Heals);           // not the dispenser's own 9
    }

    [Fact]
    public void player_healed_names_the_builder_as_the_dispensers_healer()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.NoteDamage(3, time: 100f);
        rig.Healing.Heal(H(3), H(9), 24f, 100f, dispenserHeal: true, scorer: 2);

        rig.Healing.TickPatient(H(3), now: 100f, frametime: 1f);
        rig.Healing.TickPatient(H(3), now: 101f, frametime: 0.5f);  // the second window opens

        Assert.Contains(rig.Events.Fired, line => line.Contains("event=player_healed") && line.Contains("healer=102"));
    }

    [Fact]
    public void A_dispenser_refills_a_spys_cloak_even_at_full_health()
    {
        // Stock runs the cloak arm before the overheal check (tf_player_shared.cpp:2440-2443).
        var rig = new MedigunRig();
        rig.Give(3, 125, 125);
        var spy = new TFPlayerNetState();
        spy.BindLocal();
        spy.PlayerClass = (int)TFClass.Spy;
        spy.CloakMeter = 50f;
        rig.Data.States[3] = spy;
        rig.Healing.Heal(H(3), H(9), 10f, 100f, overhealBonus: 1f, dispenserHeal: true, scorer: 2);

        rig.Healing.TickPatient(H(3), now: 100f, frametime: 1f);

        Assert.Equal(60f, spy.CloakMeter);
        Assert.Empty(rig.Resupply.Takes);       // full health: the heal itself was skipped
    }

    [Fact]
    public void Only_a_spy_takes_the_dispensers_cloak()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        var scout = new TFPlayerNetState();
        scout.BindLocal();
        scout.PlayerClass = (int)TFClass.Scout;
        scout.CloakMeter = 50f;
        rig.Data.States[3] = scout;
        rig.Healing.Heal(H(3), H(9), 10f, 100f, dispenserHeal: true, scorer: 2);

        rig.Healing.TickPatient(H(3), now: 100f, frametime: 1f);

        Assert.Equal(50f, scout.CloakMeter);
        Assert.Contains("take 3 10 True", rig.Resupply.Takes);
    }

    [Fact]
    public void The_cloak_meter_clamps_at_a_hundred()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        var spy = new TFPlayerNetState();
        spy.BindLocal();
        spy.PlayerClass = (int)TFClass.Spy;
        spy.CloakMeter = 99.5f;
        rig.Data.States[3] = spy;
        rig.Healing.Heal(H(3), H(9), 10f, 100f, dispenserHeal: true, scorer: 2);

        rig.Healing.TickPatient(H(3), now: 100f, frametime: 1f);

        Assert.Equal(100f, spy.CloakMeter);
    }

    // ---- the lifecycle halves --------------------------------------------------------

    [Fact]
    public void A_level_shutdown_clears_every_healing_list()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);

        rig.Healing.OnLevelShutdown();

        Assert.Equal(0, rig.Healing.NumHealers(3));
    }

    [Fact]
    public void A_disconnected_player_loses_their_healing_state()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);

        rig.Healing.OnClientDisconnected(3);

        Assert.Equal(0, rig.Healing.NumHealers(3));
    }

    // ---- the extinguish event --------------------------------------------------------

    [Fact]
    public void An_extinguished_patient_fires_player_extinguished_once_per_healer()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Give(1, 100, 125);
        rig.Give(2, 100, 125);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        rig.Healing.Heal(H(3), H(2), 24f, 10f);

        rig.Healing.Extinguished(H(3));

        Assert.Equal([
            "event=player_extinguished healer=1 itemdefindex=65535 victim=3",
            "event=player_extinguished healer=2 itemdefindex=65535 victim=3",
        ], rig.Events.Fired);
    }

    [Fact]
    public void A_dispenser_heal_names_its_builder_as_the_extinguishing_healer()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        // The dispenser (index 9) is no player: stock's !pHealer->IsPlayer() arm
        // falls to pHealScorer, the builder.
        rig.Healing.Heal(H(3), H(9), 10f, 10f, dispenserHeal: true, scorer: 4);

        rig.Healing.Extinguished(H(3));

        Assert.Equal(["event=player_extinguished healer=4 itemdefindex=65535 victim=3"], rig.Events.Fired);
    }

    [Fact]
    public void A_non_player_healer_names_its_scorer_without_being_a_dispenser()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Healing.Heal(H(3), H(9), 10f, 10f, dispenserHeal: false, scorer: 5);

        rig.Healing.Extinguished(H(3));

        Assert.Equal(["event=player_extinguished healer=5 itemdefindex=65535 victim=3"], rig.Events.Fired);
    }

    [Fact]
    public void An_extinguish_without_the_health_buff_hears_nothing()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        rig.Conditions.Remove(H(3), TFCond.HealthBuff);

        rig.Healing.Extinguished(H(3));

        Assert.Empty(rig.Events.Fired);
    }

    [Fact]
    public void The_extinguish_event_carries_the_healers_active_weapons_item()
    {
        var log = new RecordingLog();
        var files = new EconTestFiles().With(ItemSchema.FilePath, EconTestFiles.Fixture("items_game.trimmed.txt"));
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Give(1, 100, 125);
        rig.Owners.Items[1] = new AttributeContainer(new ItemSchema(files, log).Item(13),
                                                     AttributeProviderType.Generic);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);

        rig.Healing.Extinguished(H(3));

        Assert.Contains("itemdefindex=13", Assert.Single(rig.Events.Fired));
    }
}

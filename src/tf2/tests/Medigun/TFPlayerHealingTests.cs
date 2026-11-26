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
    public void A_mega_healed_patient_takes_three_times_the_scaled_heal()
    {
        var rig = new MedigunRig();
        rig.Give(3, 80, 200);                   // buffed max 300: room for the tripled payout
        rig.Conditions.Add(H(3), TFCond.Megaheal);   // the Quick-Fix charge's own condition
        rig.Healing.Heal(H(3), H(1), 24f, 10f);

        rig.Healing.TickPatient(H(3), now: 100f, frametime: 1f);     // scale 3 × megaheal 3: pays out 216

        Assert.Contains("take 3 216 True", rig.Resupply.Takes);
        Assert.DoesNotContain("take 3 72 True", rig.Resupply.Takes);   // without the condition the same tick pays 72
    }

    [Fact]
    public void The_patients_mult_healing_from_medics_multiplies_the_medigun_heal()
    {
        var rig = new MedigunRig();
        rig.Give(3, 80, 200);
        rig.NoteDamage(3, time: 10f);                     // recent: scale starts at 1
        rig.Items.SetManager(3, HealScale("mult_healing_from_medics", 2f));   // :2519 — asked on the patient's own manager
        rig.Healing.Heal(H(3), H(1), 24f, 10f);

        rig.Healing.TickPatient(H(3), now: 10f, frametime: 1f);   // scale 1 walked to 3: pays out 72

        Assert.Contains("take 3 72 True", rig.Resupply.Takes);
        Assert.DoesNotContain("take 3 24 True", rig.Resupply.Takes);   // :2519 — without the attribute the same tick pays 24
    }

    [Fact]
    public void The_healers_overheal_fill_rate_rides_the_heal_past_the_class_max()
    {
        // tf_player_shared.cpp:2429-2434, :2520: once the patient stands past their class
        // max, the healer's own overheal_fill_rate walks onto their heal — asked on the
        // healer's manager, and only for a player's heal. Past the max the payout lands in
        // the boost room above it (:2506-2507), so the attribute's tripple of the
        // accumulate is what fills it.
        var rig = new MedigunRig();
        rig.Give(3, 110, 100);                     // past the class max: the walk runs
        rig.NoteDamage(3, time: 10f);              // recent: scale starts at 1
        rig.Items.SetManager(1, HealScale("overheal_fill_rate", 2f));   // healer's manager, not the patient's
        rig.Healing.Heal(H(3), H(1), 24f, 10f);

        rig.Healing.TickPatient(H(3), now: 10f, frametime: 1f);   // 24 walked to 72, the room to the boost max takes 40

        Assert.Contains("take 3 40 True", rig.Resupply.Takes);     // the boost-max room, filled by the walked heal
        Assert.DoesNotContain("take 3 24 True", rig.Resupply.Takes);   // without the attribute the same tick pays 24
    }

    /// <summary>An attribute manager holding one Additive class, the shape the
    /// heal-scale facts declare (<c>AddedSeconds</c>, <c>TFMedigunWeaponTests</c>).</summary>
    private static AttributeContainer HealScale(string attributeClass, float value)
    {
        var def = new AttributeDefinition(30045, "heal scale", attributeClass,
                                          AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                          false, false, AttributeValueType.Default, MergedKeys.Block("healscale"));
        var manager = new AttributeContainer(null, AttributeProviderType.Generic);
        manager.Set(AttributeValue.OfFloat(def, value));
        return manager;
    }

    [Fact]
    public void The_held_mult_health_fromhealers_multiplies_what_healers_give()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.NoteDamage(3, time: 10f);                     // recent: scale 1
        rig.Items.SetManager(3, HealScale("mult_health_fromhealers", 0.5f));   // :2406 — asked on the patient's own manager
        rig.Healing.Heal(H(3), H(1), 24f, 10f);

        rig.Healing.TickPatient(H(3), now: 10f, frametime: 1f);   // scale 1 × attribute scale 1.5

        Assert.Contains("take 3 36 True", rig.Resupply.Takes);
        Assert.DoesNotContain("take 3 24 True", rig.Resupply.Takes);   // :2403-2411 — without the attribute the same tick pays 24
    }

    [Fact]
    public void The_penalty_on_the_held_weapon_is_asked_next_and_multiplies_the_heal_too()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.NoteDamage(3, time: 10f);
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_pistol");
        rig.Items.Set(3, new Dictionary<string, IAttributeProvider> { ["tf_weapon_pistol"] =
            HealScale("mult_health_fromhealers_penalty_active", 0.25f) });   // :2410 — asked on the active weapon
        rig.Healing.Heal(H(3), H(1), 24f, 10f);

        rig.Healing.TickPatient(H(3), now: 10f, frametime: 1f);   // scale 1 × attribute scale 1.25

        Assert.Contains("take 3 30 True", rig.Resupply.Takes);   // :2503/:2520 — the penalty rides the healer fraction
    }

    /// <summary>
    /// The healing debuff (tf_player_shared.cpp:2537-2540): while the patient rides it —
    /// the Pyro's afterburn leaves one behind (:6653) — the tick's accumulated fraction is
    /// scaled by <c>1 − PYRO_AFTERBURN_HEALING_REDUCTION</c> before it becomes health.
    /// </summary>
    [Fact]
    public void A_patient_under_the_healing_debuff_takes_eighty_percent_of_the_same_heal()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.NoteDamage(3, time: 10f);                           // recent: scale 1
        rig.Conditions.Add(H(3), TFCond.HealingDebuff);         // the afterburn's leftover
        rig.Healing.Heal(H(3), H(1), 24f, 10f);

        rig.Healing.TickPatient(H(3), now: 10f, frametime: 1f);   // fraction 24 × 0.8 = 19.2 → pays out 19

        Assert.Contains("take 3 19 True", rig.Resupply.Takes);
        Assert.DoesNotContain("take 3 24 True", rig.Resupply.Takes);   // the same tick without the debuff pays 24 (:2537)
    }

    /// <summary>
    /// The overheal drain divided by the healer's decay multiplier (tf_player_shared.cpp:2709-2711):
    /// half the multiplier doubles the drain per second, so a lower multiplier drains faster.
    /// </summary>
    [Fact]
    public void A_healer_with_a_lower_decay_multiplier_drains_the_overheal_faster()
    {
        // max 150: the boost room is (int)(150 × 1.5 / 5) × 5 − 150 = 75; tf_boost_drain_time 15.
        var rig = new MedigunRig();
        rig.Give(3, 160, 150);
        rig.Healing.Heal(H(3), H(1), 0f, 10f, ITFPlayerHealing.StandardOverhealBonus, decayMult: 0.5f);
        rig.Healing.TickPatient(H(3), now: 10f, frametime: 1f);      // healer up: the walk banks the multiplier (:2531)
        rig.Healing.StopHealing(H(3), H(1), now: 11f);

        rig.Healing.TickPatient(H(3), now: 11f, frametime: 0.5f);    // drain 75/(15 × 0.5) = 10/s → half a second drains 5

        Assert.Equal(155, rig.Health(3));       // multiplier 1 would drain only 5/s → health 158 after the half second
    }

    /// <summary>
    /// The best-multiplier reset (tf_player_shared.cpp:2730-2733): with no healer left and
    /// the health drained back to max, the banked multiplier returns to −1, so the next
    /// overheal drains at multiplier 1 again.
    /// </summary>
    [Fact]
    public void The_banked_decay_multiplier_resets_once_the_drain_brings_the_patient_back_to_max()
    {
        var rig = new MedigunRig();
        rig.Give(3, 160, 150);
        rig.Healing.Heal(H(3), H(1), 0f, 10f, ITFPlayerHealing.StandardOverhealBonus, decayMult: 0.5f);
        rig.Healing.TickPatient(H(3), now: 10f, frametime: 1f);      // the walk banks 0.5; the healer keeps the drain off
        rig.Healing.StopHealing(H(3), H(1), now: 11f);

        rig.Healing.TickPatient(H(3), now: 11f, frametime: 0.5f);    // 10/s under the banked 0.5
        Assert.Equal(155, rig.Health(3));
        rig.Healing.TickPatient(H(3), now: 11.5f, frametime: 0.5f);
        Assert.Equal(150, rig.Health(3));                             // exactly the class max
        rig.Healing.TickPatient(H(3), now: 12f, frametime: 0.5f);    // at max: the else-if clears the banked multiplier

        rig.Give(3, 153, 150);                                        // an item puts them over max again, with no healer
        rig.Healing.TickPatient(H(3), now: 12.5f, frametime: 0.4f);   // multiplier 1 again: 75/15 = 5/s × 0.4 drains 2

        Assert.Equal(151, rig.Health(3));       // still banked at 0.5 the half-scaled tick would drain 4: health 149
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

    /// <summary>
    /// The Quick-Fix no-decay arm (tf_player_shared.cpp:2458-2470): a healer whose active
    /// medigun is a Quick-Fix keeps a patient already at the overheal bonus up, even though
    /// the heal itself is skipped at the cap; the plain gun leaves the drain on (:2475-2479
    /// leaves bDecayHealth set when it heals nobody).
    /// </summary>
    [Fact]
    public void A_quick_fix_healer_keeps_a_capped_patient_up_and_a_plain_one_drains_them()
    {
        var rig = new MedigunRig();
        rig.Give(3, 450, 300);                       // heavy at exactly the bonus: 450 = 1.5 × 300
        rig.Healing.Heal(H(3), H(1), 24f, 10f);

        rig.Healing.TickPatient(H(3), now: 10f, frametime: 1f);       // plain healer: skipped at the cap, drain runs — (450 − 300)/15 = 10/s
        Assert.Equal(440, rig.Health(3));

        rig.Items.SetManager(1, HealScale("set_weapon_mode", 2f));    // MEDIGUN_QUICKFIX on the healer's manager (:2464-2465)
        rig.Healing.TickPatient(H(3), now: 11f, frametime: 1f);       // the Quick-Fix holds the overheal up (:2463)

        Assert.Equal(440, rig.Health(3));            // :2467-2468 — without the arm this second tick would drain to 430
    }

    /// <summary>
    /// The Quick-Fix arm asks the healer's medigun (GetMedigunType, tf_weapon_medigun.cpp:781-785), not his manager: a plain gun held
    /// beside a crossbow and an Ubersaw (each set_weapon_mode 1, the manager's sum 2, the Quick-Fix's row) lets the drain run.
    /// </summary>
    [Fact]
    public void A_plain_gun_beside_two_moded_weapons_is_no_quick_fix()
    {
        var rig = new MedigunRig();
        var manager = new AttributeContainer(null, AttributeProviderType.Generic);
        var def = new AttributeDefinition(30049, "weapon mode", "set_weapon_mode", AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                          false, false, AttributeValueType.Default, MergedKeys.Block("mode"));
        AttributeContainer Weapon(float mode)
        {
            var item = new AttributeContainer(null, AttributeProviderType.Weapon);
            if (mode != 0f)
                item.Set(AttributeValue.OfFloat(def, mode));
            item.ProvideTo(manager);
            item.Owner = manager;
            return item;
        }

        rig.Items.Set(1, new Dictionary<string, IAttributeProvider>
        {
            ["tf_weapon_medigun"] = Weapon(0f), ["tf_weapon_crossbow"] = Weapon(1f), ["tf_weapon_bonesaw"] = Weapon(1f),
        });
        rig.Items.SetManager(1, manager);
        rig.Give(3, 450, 300);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        rig.Healing.TickPatient(H(3), now: 10f, frametime: 1f);         // at the bonus: skipped, and the drain runs unless a Quick-Fix holds

        Assert.Equal(2, rig.Hooks.Int(0, "set_weapon_mode", manager));   // the manager alone reads the Quick-Fix's row
        Assert.Equal(440, rig.Health(3));                                  // drained, (450 - 300) / 15: no Quick-Fix hold (it would stay 450)
    }

    /// <summary>
    /// The disguised Spy's fake health (tf_player_shared.cpp:2436-2456, :2542-2563): at the
    /// overheal cap the real-health arm is skipped, and the healer's whole fraction lands on
    /// <c>m_iDisguiseHealth</c>, capped to the disguise class's buffed max.
    /// </summary>
    [Fact]
    public void The_medigun_heal_of_a_disguised_spy_lands_on_the_fake_health_not_the_real_one()
    {
        var rig = new MedigunRig();
        rig.Give(3, 450, 300);                       // the spy's own health sits at the bonus: its arm is skipped (:2446)
        rig.NoteDamage(3, time: 10f);                // recent hit: scale 1
        rig.Conditions.Add(H(3), TFCond.Disguised);
        var spy = new TFPlayerNetState();
        spy.BindLocal();
        spy.PlayerClass = (int)TFClass.Spy;
        spy.DisguiseClass = (int)TFClass.Soldier;    // shipped script: soldier max 200
        spy.DisguiseHealth = 50;
        rig.Data.States[3] = spy;

        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        rig.Healing.TickPatient(H(3), now: 10f, frametime: 1f);       // disguise fraction 24 pays onto the fake health

        Assert.Equal(74, spy.DisguiseHealth);                         // 50 + 24, under the soldier's buffed 300
        Assert.Equal(440, rig.Health(3));                             // the real overheal drains on: bHealActual was false (:2476)
        Assert.Empty(rig.Resupply.Takes);                             // :2516-2521 — the skipped arm asks nothing of TakeHealth
    }

    /// <summary>
    /// The disguise's own drain (tf_player_shared.cpp:2737-2757): fake health over the disguise
    /// class's max rides the same drain time after the beam stops, the decay multiplier scaling
    /// the rate after the divide (:2746).
    /// </summary>
    [Fact]
    public void The_disguises_fake_overheal_drains_back_once_the_beam_stops()
    {
        var rig = new MedigunRig();
        rig.Give(3, 300, 300);                       // the spy's own health at max: its drain stands down
        rig.Conditions.Add(H(3), TFCond.Disguised);
        var spy = new TFPlayerNetState();
        spy.BindLocal();
        spy.PlayerClass = (int)TFClass.Spy;
        spy.DisguiseClass = (int)TFClass.Soldier;    // shipped script: soldier max 200
        spy.DisguiseHealth = 250;                    // 50 over the disguise's own max
        rig.Data.States[3] = spy;

        rig.Healing.TickPatient(H(3), now: 10f, frametime: 0.5f);     // (300 − 200)/15 ≈ 6.67/s × 0.5 → whole-drain 3

        Assert.Equal(247, spy.DisguiseHealth);       // :2749-2756
        Assert.Equal(300, rig.Health(3));             // the real health is at max: untouched
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

    // ---- the per-charge on/off sounds (tf_player_shared.cpp:9060-9099) --------------

    [Fact]
    public void The_grant_emits_the_on_sound_once_to_the_patient_alone()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        rig.Healing.NoteReleasing(1, true);

        rig.Healing.RecalculateChargeEffects(H(3), now: 10f);      // the grant, tf_player_shared.cpp:9078-9079

        Assert.Single(rig.Engine.AllOf("Sound.EmitSoundToPlayer"),
                      c => (string)c.Args[2]! == "TFPlayer.InvulnerableOn"
                        && (EntityHandle)c.Args[0]! == H(3)              // CSingleUserRecipientFilter: the patient only
                        && (EntityHandle)c.Args[1]! == H(3)              // stock emits from the player's own index (:9078)
                        && (bool)c.Args[3]! == true);                    // the stock arm keeps the filter's reliable default

        rig.Healing.RecalculateChargeEffects(H(3), now: 10.5f);    // already held: the equal-state arm re-emits nothing (:9038)

        Assert.Single(rig.Engine.AllOf("Sound.EmitSoundToPlayer"),
                      c => (string)c.Args[2]! == "TFPlayer.InvulnerableOn");
    }

    [Fact]
    public void The_wear_off_stops_the_on_sound_once_and_puts_the_off_sound_once()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        rig.Healing.NoteReleasing(1, true);
        rig.Healing.RecalculateChargeEffects(H(3), now: 10f);
        rig.Healing.NoteReleasing(1, false);

        rig.Healing.RecalculateChargeEffects(H(3), now: 10.5f);   // wear-off, tf_player_shared.cpp:9085-9098

        Assert.Single(rig.Engine.AllOf("Sound.EntityStopSound"),
                      c => (string)c.Args[1]! == "TFPlayer.InvulnerableOn"
                        && (EntityHandle)c.Args[0]! == H(3));     // the on sound stopped first (:9087)
        Assert.Single(rig.Engine.AllOf("Sound.EmitSoundToPlayer"),
                      c => (string)c.Args[2]! == "TFPlayer.InvulnerableOff"
                        && (EntityHandle)c.Args[0]! == H(3)              // the off wave reaches the patient alone (:9095)
                        && (bool)c.Args[3]! == true);

        rig.Healing.RecalculateChargeEffects(H(3), now: 10.7f);   // the pending expiry early-returns (:9112 arm)

        Assert.Single(rig.Engine.AllOf("Sound.EntityStopSound"),
                      c => (string)c.Args[1]! == "TFPlayer.InvulnerableOn");
        Assert.Single(rig.Engine.AllOf("Sound.EmitSoundToPlayer"),
                      c => (string)c.Args[2]! == "TFPlayer.InvulnerableOff");
    }

    [Fact]
    public void An_instant_remove_leaves_the_on_sound_to_be_stopped_once_on_the_next_expire_tick()
    {
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        rig.Healing.NoteReleasing(1, true);
        rig.Healing.RecalculateChargeEffects(H(3), now: 10f);      // the grant puts the on sound on
        rig.Healing.RemoveChargeEffect(H(3));                      // instant loss: stock's RecalcEffectOnTarget(target, true)

        rig.Healing.TestAndExpireChargeEffect(H(3), now: 10.1f);   // stock's else-if (:8980-8987) stops it, once

        Assert.Single(rig.Engine.AllOf("Sound.EntityStopSound"),
                      c => (string)c.Args[1]! == "TFPlayer.InvulnerableOn"
                        && (EntityHandle)c.Args[0]! == H(3));

        rig.Healing.TestAndExpireChargeEffect(H(3), now: 10.2f);

        Assert.Single(rig.Engine.AllOf("Sound.EntityStopSound"),
                      c => (string)c.Args[1]! == "TFPlayer.InvulnerableOn");
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
    public void A_flag_carrier_keeps_a_crit_boost_but_receives_no_invulnerability()
    {
        // The refusal after the recalc's loop clears only the invulnerable row
        // (tf_player_shared.cpp:8899-8902): a covered carrier keeps every other
        // charge effect their healer is providing.
        var rig = new MedigunRig();
        rig.Give(3, 100, 125);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        rig.Healing.NoteReleasing(1, true, chargeType: 1);   // the Kritzkrieg
        rig.Ctf.Carriers.Add(3);

        rig.Healing.RecalculateChargeEffects(H(3), now: 10f);

        Assert.True(rig.Conditions.Holds(3, TFCond.Critboosted));
        Assert.False(rig.Conditions.Holds(3, TFCond.Invulnerable));
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
        rig.Items.Set(1, new Dictionary<string, IAttributeProvider> { ["tf_weapon_medigun"] =
            new AttributeContainer(new ItemSchema(files, log).Item(13), AttributeProviderType.Generic) });
        rig.Healing.Heal(H(3), H(1), 24f, 10f);

        rig.Healing.Extinguished(H(3));

        Assert.Contains("itemdefindex=13", Assert.Single(rig.Events.Fired));
    }
}

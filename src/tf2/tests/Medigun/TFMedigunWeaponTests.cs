using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Medigun;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Medigun;

/// <summary>
/// The medigun's own arms: <c>CWeaponMedigun</c>'s target acquisition and beam stick rules,
/// the charge meter, the ÜberCharge release and the drain. Patient 3, medic 1, the
/// script is the parsed stock <c>tf_weapon_medigun</c> (Damage 24, Range 450).
/// </summary>
public sealed class TFMedigunWeaponTests
{
    private static readonly TFWeaponScript Medigun = WeaponFixtures.Parse("tf_weapon_medigun");
    private const int Attack1 = (int)WeaponButtons.Attack;
    private const int Attack2 = (int)WeaponButtons.Attack2;

    private static EntityHandle H(int index) => MedigunRig.HandleFor(index);

    private static WeaponAttackContext Attack(int owner, int buttons, float now)
    {
        var weapon = new WeaponNetState();
        weapon.BindLocal();
        weapon.LastButtons = buttons;
        var view = new ViewModelNetState();
        view.BindLocal();
        return new(default, weapon, view, now, true, default!, new WeaponCommand(0, 9, owner));
    }

    private static MedigunRig Standing(MedigunRig? rig = null)
    {
        rig ??= new MedigunRig();
        rig.Give(1, 100, 100);
        rig.Give(3, 100, 125);
        return rig;
    }

    /// <summary>The two aim traces of a Find, both landing on the patient.</summary>
    private static void QueueFound(MedigunRig rig)
    {
        rig.QueueHit(H(3));
        rig.QueueHit(H(3));
    }

    /// <summary>The maintain's aim trace still reaching the patient: stock keeps the beam
    /// on that alone (tf_weapon_medigun.cpp:694) and never reaches the centre and eye lines.</summary>
    private static void QueueKept(MedigunRig rig) => rig.QueueHit(H(3));

    /// <summary>One command's beam: the medic holds (or does not) the primary fire.</summary>
    private static void Command(MedigunRig rig, TFMedigunWeapon weapon, bool held, float now, int extraButtons = 0)
    {
        var attack = Attack(1, held ? Attack1 | extraButtons : extraButtons, now);
        weapon.Heal(in attack, Medigun, held);
    }

    // ---- target acquisition --------------------------------------------------------

    [Fact]
    public void A_teammate_aimed_at_is_taken_as_the_target_and_starts_being_healed()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);

        Command(rig, weapon, held: true, now: 10f);

        Assert.Equal(3, weapon.HealingTargetIndex(1));
        Assert.Contains("add 3 HealthBuff d-1 p1", rig.Conditions.Calls);
    }

    [Fact]
    public void An_enemy_never_becomes_the_target()
    {
        var rig = Standing();
        rig.Teams[4] = (int)TFTeam.Blue;
        var weapon = rig.NewWeapon();
        rig.QueueHit(H(4));
        rig.QueueHit(H(4));

        Command(rig, weapon, held: true, now: 10f);

        Assert.Equal(-1, weapon.HealingTargetIndex(1));
        Assert.Empty(rig.Resupply.Takes);
    }

    [Fact]
    public void An_enemy_disguised_as_the_medic_s_team_is_taken()
    {
        // Stock's disguise arm: an enemy under TF_COND_DISGUISED whose m_nDisguiseTeam
        // is the medic's team heals like a teammate (tf_weapon_medigun.cpp:593-599).
        var rig = Standing();
        rig.Teams[1] = (int)TFTeam.Red;
        rig.Teams[4] = (int)TFTeam.Blue;
        rig.Give(4, 100, 100);
        var disguise = new TFPlayerNetState();
        disguise.BindLocal();
        disguise.DisguiseTeam = (int)TFTeam.Red;
        rig.Data.States[4] = disguise;
        rig.Conditions.Add(H(4), TFCond.Disguised);
        var weapon = rig.NewWeapon();
        rig.QueueHit(H(4));
        rig.QueueHit(H(4));

        Command(rig, weapon, held: true, now: 10f);

        Assert.Equal(4, weapon.HealingTargetIndex(1));
        Assert.Contains("add 4 HealthBuff d-1 p1", rig.Conditions.Calls);
    }

    [Fact]
    public void An_enemy_disguised_as_his_own_team_is_still_refused()
    {
        var rig = Standing();
        rig.Teams[1] = (int)TFTeam.Red;
        rig.Teams[4] = (int)TFTeam.Blue;
        rig.Give(4, 100, 100);
        var disguise = new TFPlayerNetState();
        disguise.BindLocal();
        disguise.DisguiseTeam = (int)TFTeam.Blue;
        rig.Data.States[4] = disguise;
        rig.Conditions.Add(H(4), TFCond.Disguised);
        var weapon = rig.NewWeapon();
        rig.QueueHit(H(4));
        rig.QueueHit(H(4));

        Command(rig, weapon, held: true, now: 10f);

        Assert.Equal(-1, weapon.HealingTargetIndex(1));
    }

    [Fact]
    public void An_undisguised_enemy_is_refused_despite_a_stale_disguise_team()
    {
        // The arm gates on TF_COND_DISGUISED itself (:591): a carrier still carrying
        // an m_nDisguiseTeam with the condition gone reads as the enemy it is.
        var rig = Standing();
        rig.Teams[1] = (int)TFTeam.Red;
        rig.Teams[4] = (int)TFTeam.Blue;
        rig.Give(4, 100, 100);
        var disguise = new TFPlayerNetState();
        disguise.BindLocal();
        disguise.DisguiseTeam = (int)TFTeam.Red;
        rig.Data.States[4] = disguise;
        var weapon = rig.NewWeapon();
        rig.QueueHit(H(4));
        rig.QueueHit(H(4));

        Command(rig, weapon, held: true, now: 10f);

        Assert.Equal(-1, weapon.HealingTargetIndex(1));
    }

    [Fact]
    public void A_refused_hitbox_hit_leaves_the_hull_target_standing()
    {
        // The loose hull trace gives the candidate (tf_weapon_medigun.cpp:739-755); the
        // hitbox trace replaces it only with a different allowed entity — so a clumped
        // group whose nearest hitbox belongs to a refused player still yields the hull
        // target behind them.
        var rig = Standing();
        rig.Give(2, 100, 100);                          // a teammate
        rig.Teams[5] = (int)TFTeam.Blue;                // an enemy in front of them
        rig.Give(5, 100, 100);
        var weapon = rig.NewWeapon();

        rig.QueueHit(H(2));                             // the loose hull trace: the teammate
        rig.QueueHit(H(5));                             // the hitbox trace: the enemy
        Command(rig, weapon, held: true, now: 10f);

        Assert.Equal(2, weapon.HealingTargetIndex(1));
        Assert.Contains("add 2 HealthBuff d-1 p1", rig.Conditions.Calls);
    }

    [Fact]
    public void A_different_allowed_hitbox_hit_replaces_the_hull_candidate()
    {
        var rig = Standing();
        rig.Give(2, 100, 100);
        var weapon = rig.NewWeapon();
        rig.QueueHit(H(2));                             // the loose hull trace: 2's hull
        rig.QueueHit(H(3));                             // the hitbox trace: 3, clumped inside
        Command(rig, weapon, held: true, now: 10f);

        Assert.Equal(3, weapon.HealingTargetIndex(1));
    }

    [Fact]
    public void A_stealthed_patient_is_refused_while_the_medic_is_visible()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        rig.Conditions.Add(H(3), TFCond.Stealthed);
        QueueFound(rig);

        Command(rig, weapon, held: true, now: 10f);

        Assert.Equal(-1, weapon.HealingTargetIndex(1));
    }

    [Fact]
    public void A_dead_target_drops_the_beam_on_the_next_think()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);
        Assert.Equal(3, weapon.HealingTargetIndex(1));
        rig.Dead.Add(3);

        weapon.TickMedic(1, now: 10.3f, frametime: 0.1f);

        Assert.Equal(-1, weapon.HealingTargetIndex(1));
        Assert.Contains("remove 3 HealthBuff", rig.Conditions.Calls);
    }

    [Fact]
    public void A_teammate_between_the_medic_and_his_patient_does_not_block_the_beam()
    {
        var rig = Standing();
        rig.Give(2, 100, 100);                          // a teammate on the medic's team
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);
        Assert.Equal(3, weapon.HealingTargetIndex(1));

        // The aim line lands on the teammate's hull; the lines to the centre and eyes then
        // stop on him and stock's CMedigunFilter (tf_weapon_medigun.cpp:629-648) ignores
        // own-team entities, so the re-traces walk through to the patient -- the beam holds.
        rig.QueueHit(H(2));
        rig.QueueHit(H(2));
        rig.QueueHit(H(3));
        rig.QueueHit(H(3));
        Command(rig, weapon, held: true, now: 11f);

        Assert.Equal(3, weapon.HealingTargetIndex(1));
        // The beam was never taken down: the teammate's hull did not cost the patient
        // their heal (a drop would leave its remove-and-re-add trail in the calls).
        Assert.DoesNotContain("remove 3 HealthBuff", rig.Conditions.Calls);
    }

    [Fact]
    public void An_enemy_between_the_medic_and_his_patient_blocks_the_beam()
    {
        var rig = Standing();
        rig.Teams[4] = (int)TFTeam.Blue;
        rig.Give(4, 100, 100);
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);
        Assert.Equal(3, weapon.HealingTargetIndex(1));

        // The aim line misses the patient (stock keeps the beam on the aim line alone,
        // tf_weapon_medigun.cpp:694); the line to the centre then stops on the enemy,
        // whom the filter falls through to CTraceFilterSimple on -- the beam is lost.
        rig.QueueMiss();
        rig.QueueHit(H(4));
        Command(rig, weapon, held: true, now: 11f);

        Assert.Equal(-1, weapon.HealingTargetIndex(1));
    }

    [Fact]
    public void A_target_that_has_run_past_the_stick_range_breaks_the_beam()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);

        rig.Centers[3] = new Vector(600f, 0f, 0f);          // stick range is 450 × 1.2
        Command(rig, weapon, held: true, now: 10.5f);

        Assert.Equal(-1, weapon.HealingTargetIndex(1));
    }

    [Fact]
    public void The_beam_re_acquires_on_the_next_command_after_the_stick_range_loss()
    {
        // FindNewTargetForSlot is ungated (tf_weapon_medigun.cpp:719-774): after the
        // maintain drops the target the next command searches, it does not wait out
        // the one-second window.
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);
        rig.Centers[3] = new Vector(600f, 0f, 0f);
        Command(rig, weapon, held: true, now: 10.5f);       // the stick rule drops the beam
        Assert.Equal(-1, weapon.HealingTargetIndex(1));

        rig.Centers[3] = Vector.Zero;
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10.6f);

        Assert.Equal(3, weapon.HealingTargetIndex(1));
    }

    [Fact]
    public void The_maintain_traces_run_once_a_second_not_every_command()
    {
        // MaintainTargetInSlot sets m_flNextTargetCheckTime = curtime + 1.0 itself
        // (tf_weapon_medigun.cpp:677-680); between checks the beam holds without traces.
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);         // the find

        QueueKept(rig);
        Command(rig, weapon, held: true, now: 11.2f);       // the gate opens: three traces
        Assert.Equal(3, weapon.HealingTargetIndex(1));

        rig.QueueHit(H(3));                                 // one trace: enough if the check runs again
        Command(rig, weapon, held: true, now: 11.3f);

        Assert.Equal(3, weapon.HealingTargetIndex(1));      // and the beam never dipped
        Assert.Single(rig.TraceQueue);                      // the check did not run again: the trace is still queued
    }

    [Fact]
    public void Letting_go_of_the_fire_button_drops_the_target()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);

        Command(rig, weapon, held: false, now: 11f);

        Assert.Equal(-1, weapon.HealingTargetIndex(1));
        Assert.Contains("remove 3 HealthBuff", rig.Conditions.Calls);
    }

    [Fact]
    public void The_re_deployed_medigun_checks_its_target_at_once()
    {
        // Deploy resets m_flNextTargetCheckTime to curtime (tf_weapon_medigun.cpp:422):
        // the target traces run on the next command, not a second after the last Find.
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);         // the beam is up; the check clock reads 11
        Command(rig, weapon, held: false, now: 10.2f);      // dropped

        weapon.Unholster(1, now: 10.3f);

        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10.5f);       // inside the old window

        Assert.Equal(3, weapon.HealingTargetIndex(1));
    }

    [Fact]
    public void Re_acquiring_after_a_dropped_beam_pays_nothing_for_the_lost_span()
    {
        // Stock pays gpGlobals->frametime on the frames the beam is up
        // (tf_weapon_medigun.cpp:1272-1273) — never a backlog for the seconds it was down.
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);
        Assert.Equal(3, weapon.HealingTargetIndex(1));

        Command(rig, weapon, held: false, now: 10.5f);      // the beam drops
        Assert.Equal(-1, weapon.HealingTargetIndex(1));

        QueueFound(rig);
        Command(rig, weapon, held: true, now: 20.5f);       // ten seconds later

        Assert.Equal(3, weapon.HealingTargetIndex(1));
        Assert.Equal(0f, weapon.ChargeLevel(1), 5);         // the gap pays nothing
    }

    // ---- the charge meter ------------------------------------------------------------

    [Fact]
    public void The_meter_builds_at_one_per_forty_seconds_of_healing()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);

        QueueKept(rig);
        Command(rig, weapon, held: true, now: 11f);      // one second of the beam

        Assert.Equal(1f / 40f, weapon.ChargeLevel(1), 5);
    }

    [Fact]
    public void The_charge_rate_convar_scales_the_meter_build()
    {
        // Stock pays frametime / weapon_medigun_charge_rate (tf_weapon_medigun.cpp:1272-1275).
        var rig = Standing();
        rig.Set<WeaponMedigunChargeRateConVar>("20");
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);

        QueueKept(rig);
        Command(rig, weapon, held: true, now: 11f);      // one second of the beam

        Assert.Equal(1f / 20f, weapon.ChargeLevel(1), 5);
    }

    [Fact]
    public void A_zero_charge_rate_builds_nothing()
    {
        // Stock guards the whole build on the rate being non-zero (:1275).
        var rig = Standing();
        rig.Set<WeaponMedigunChargeRateConVar>("0");
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);

        QueueKept(rig);
        Command(rig, weapon, held: true, now: 11f);

        Assert.Equal(0f, weapon.ChargeLevel(1), 5);
    }

    [Fact]
    public void The_meter_builds_at_half_rate_when_the_target_nears_its_buffed_max()
    {
        var rig = Standing();
        rig.Give(3, 185, 125);                            // buffed max 185: at the floor where the meter crawls
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);

        QueueKept(rig);
        Command(rig, weapon, held: true, now: 11f);

        Assert.Equal(0.5f / 40f, weapon.ChargeLevel(1), 5);
    }

    [Fact]
    public void In_setup_the_meter_keeps_its_full_build_on_a_target_at_the_buffed_max()
    {
        // The crawl is gated on stock's !(InSetup() && GetActiveRoundTimer())
        // (tf_weapon_medigun.cpp:1288): in setup a full patient still pays the
        // whole second, at the setup triple.
        var rig = Standing();
        rig.Setup.InSetupWithRoundTimer = true;
        rig.Give(3, 185, 125);
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);

        QueueKept(rig);
        Command(rig, weapon, held: true, now: 11f);

        Assert.Equal(3f / 40f, weapon.ChargeLevel(1), 5);
    }

    [Fact]
    public void In_setup_the_meter_builds_at_triple_rate()
    {
        // tf_weapon_medigun.cpp:1347-1350: the setup round's last word on the
        // charge amount, after the patient's bonus.
        var rig = Standing();
        rig.Setup.InSetupWithRoundTimer = true;
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);

        QueueKept(rig);
        Command(rig, weapon, held: true, now: 11f);

        Assert.Equal(3f / 40f, weapon.ChargeLevel(1), 5);
    }

    [Fact]
    public void The_meter_builds_at_half_rate_when_two_medics_heal_the_one_patient()
    {
        var rig = Standing();
        var first = rig.NewWeapon();
        rig.Give(2, 100, 100);
        var second = rig.NewWeapon();

        QueueFound(rig);                                    // the first medic finds the patient
        var found1 = Attack(1, Attack1, 10f);
        first.Heal(in found1, Medigun, held: true);
        QueueFound(rig);                                    // so does the second
        var found2 = Attack(2, Attack1, 10f);
        second.Heal(in found2, Medigun, held: true);

        QueueKept(rig);
        var kept1 = Attack(1, Attack1, 11f);
        first.Heal(in kept1, Medigun, held: true);

        Assert.Equal(2, rig.Healing.NumHealers(3));
        Assert.Equal(0.5f / 40f, first.ChargeLevel(1), 5);   // one second, shared between two healers
    }

    [Fact]
    public void A_healer_bonus_on_the_patient_builds_the_meter_faster_when_neither_is_in_a_room()
    {
        // tf_weapon_medigun.cpp:1329-1340: with neither party in a respawn room the
        // patient's mult_uberchargerate_for_healer hook rides the charge amount.
        // Both asks carry the medic's world centre — stock's weapon centre, which
        // our server weapon has no entity for (docs/divergences.md).
        var rig = Standing();
        rig.Owners.Managers[3] = HealerBonus(2f);
        rig.Centers[1] = new Vector(10f, 0f, 0f);
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);

        QueueKept(rig);
        Command(rig, weapon, held: true, now: 11f);

        Assert.Equal(2f / 40f, weapon.ChargeLevel(1), 5);           // one second, doubled by the bonus
        Assert.Collection(rig.Rooms.Asks,
            ask => { Assert.Equal(3, ask.Target); Assert.Equal(new Vector(10f, 0f, 0f), ask.Origin); },   // patient first
            ask => { Assert.Equal(1, ask.Target); Assert.Equal(new Vector(10f, 0f, 0f), ask.Origin); });  // then the medic
    }

    [Fact]
    public void The_healer_bonus_refuses_while_the_medic_stands_in_a_respawn_room()
    {
        // :1334's second ask: the medic's answer decides it, no bonus.
        var rig = Standing();
        rig.Owners.Managers[3] = HealerBonus(2f);
        rig.Rooms.InRoom.Add(1);
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);

        QueueKept(rig);
        Command(rig, weapon, held: true, now: 11f);

        Assert.Equal(1f / 40f, weapon.ChargeLevel(1), 5);
    }

    [Fact]
    public void The_healer_bonus_refuses_while_the_patient_stands_in_a_respawn_room()
    {
        // :1333's first ask decides it — stock's || short-circuits and the medic is never asked.
        var rig = Standing();
        rig.Owners.Managers[3] = HealerBonus(2f);
        rig.Rooms.InRoom.Add(3);
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);

        QueueKept(rig);
        Command(rig, weapon, held: true, now: 11f);

        Assert.Equal(1f / 40f, weapon.ChargeLevel(1), 5);
        Assert.Single(rig.Rooms.Asks);
    }

    [Fact]
    public void The_medics_uberchargerate_attribute_times_the_meter()
    {
        // tf_weapon_medigun.cpp:1332: the medic's own walk rides the build,
        // after the healer split and before the patient's bonus.
        var rig = Standing();
        rig.Owners.Managers[1] = MeterRate("mult_medigun_uberchargerate", 3f);
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);

        QueueKept(rig);
        Command(rig, weapon, held: true, now: 11f);

        Assert.Equal(3f / 40f, weapon.ChargeLevel(1), 5);
    }

    [Fact]
    public void The_overheal_uberchargerate_applies_when_the_patient_is_at_full_health()
    {
        // :1327-1330's gate is the patient's own max health, read again per command.
        var rig = Standing();
        rig.Give(3, 100, 100);                           // full to the class max, under the buffed floor
        rig.Owners.Managers[1] = MeterRate("mult_medigun_overheal_uberchargerate", 2f);
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);

        QueueKept(rig);
        Command(rig, weapon, held: true, now: 11f);

        Assert.Equal(2f / 40f, weapon.ChargeLevel(1), 5);
    }

    [Fact]
    public void The_overheal_uberchargerate_refuses_while_the_patient_has_health_to_gain()
    {
        var rig = Standing();                            // the patient heals at 100 of 125
        rig.Owners.Managers[1] = MeterRate("mult_medigun_overheal_uberchargerate", 2f);
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);

        QueueKept(rig);
        Command(rig, weapon, held: true, now: 11f);

        Assert.Equal(1f / 40f, weapon.ChargeLevel(1), 5);
    }

    [Fact]
    public void The_release_adds_the_medics_uber_time_to_the_drain()
    {
        // tf_weapon_medigun.cpp:1447-1448: add_uber_time on the medic's walk
        // lengthens the release, carried in an int as stock carries it.
        var rig = Standing();
        rig.Owners.Managers[1] = AddedSeconds("add_uber_time", 8f);
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);
        weapon.SetChargeLevel(1, 1f);
        Command(rig, weapon, held: true, now: 10.5f, extraButtons: Attack2);

        weapon.TickMedic(1, now: 11.5f, frametime: 1f);   // one second of 8+8: 1/16 of the meter

        Assert.Equal(15f / 16f, weapon.ChargeLevel(1), 5);
    }

    /// <summary>An attribute manager holding one Percentage class, as the meter arms are declared.</summary>
    private static AttributeContainer MeterRate(string attributeClass, float value)
    {
        var def = new AttributeDefinition(30043, "meter rate", attributeClass,
                                          AttributeDescriptionFormat.Percentage, AttributeEffectType.Neutral,
                                          false, false, AttributeValueType.Default, MergedKeys.Block("meter"));
        var manager = new AttributeContainer(null, AttributeProviderType.Generic);
        manager.Set(AttributeValue.OfFloat(def, value));
        return manager;
    }

    /// <summary>An attribute manager holding one Additive class, as the release seconds are declared.</summary>
    private static AttributeContainer AddedSeconds(string attributeClass, float value)
    {
        var def = new AttributeDefinition(30044, "added seconds", attributeClass,
                                          AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                          false, false, AttributeValueType.Default, MergedKeys.Block("seconds"));
        var manager = new AttributeContainer(null, AttributeProviderType.Generic);
        manager.Set(AttributeValue.OfFloat(def, value));
        return manager;
    }

    /// <summary>The patient's own attribute manager (stock's <c>m_AttributeManager</c>),
    /// holding <c>mult_uberchargerate_for_healer</c> as a Percentage, as the manager-bearing facts build it.</summary>
    private static AttributeContainer HealerBonus(float multiplier)
    {
        var def = new AttributeDefinition(30042, "healer ubercharge bonus", "mult_uberchargerate_for_healer",
                                          AttributeDescriptionFormat.Percentage, AttributeEffectType.Neutral,
                                          false, false, AttributeValueType.Default, MergedKeys.Block("healer"));
        var manager = new AttributeContainer(null, AttributeProviderType.Generic);
        manager.Set(AttributeValue.OfFloat(def, multiplier));
        return manager;
    }

    [Fact]
    public void Letting_go_freezes_the_meter()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);
        QueueKept(rig);
        Command(rig, weapon, held: true, now: 11f);
        var built = weapon.ChargeLevel(1);
        Assert.True(built > 0f);

        Command(rig, weapon, held: false, now: 12f);
        Command(rig, weapon, held: false, now: 14f);

        Assert.Equal(built, weapon.ChargeLevel(1), 5);
    }

    [Fact]
    public void The_autoheal_toggle_keeps_the_beam_after_the_button_is_released_and_a_press_re_acquires()
    {
        var rig = Standing();
        rig.Clients.ConVars[(1, "tf_medigun_autoheal")] = "1";
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);         // the press that starts it

        Command(rig, weapon, held: false, now: 10.5f);       // the button is off; the toggle heals on

        Assert.Equal(3, weapon.HealingTargetIndex(1));

        QueueFound(rig);
        Command(rig, weapon, held: true, now: 11f);          // a press: drop and re-find, the same command
        Assert.Equal(3, weapon.HealingTargetIndex(1));
    }

    [Fact]
    public void The_autoheal_press_switches_to_whatever_is_under_the_crosshair()
    {
        // ItemPostFrame (tf_weapon_medigun.cpp:1563-1573) removes on the press and reaches
        // PrimaryAttack through :1585 the same frame — the beam re-aims instead of waiting.
        var rig = Standing();
        rig.Give(2, 100, 100);
        var weapon = rig.NewWeapon();
        rig.Clients.ConVars[(1, "tf_medigun_autoheal")] = "1";
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);
        Assert.Equal(3, weapon.HealingTargetIndex(1));

        Command(rig, weapon, held: false, now: 10.5f);       // the button up: the press edge arms

        rig.QueueHit(H(2));                                  // the re-find's loose and hitbox traces on 2
        rig.QueueHit(H(2));
        Command(rig, weapon, held: true, now: 11f);

        Assert.Equal(2, weapon.HealingTargetIndex(1));
        Assert.Contains("remove 3 HealthBuff", rig.Conditions.Calls);
        Assert.Contains("add 2 HealthBuff d-1 p1", rig.Conditions.Calls);
    }

    // ---- the ÜberCharge ---------------------------------------------------------------

    [Fact]
    public void A_released_meter_makes_the_medic_and_his_patient_invulnerable()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);
        weapon.SetChargeLevel(1, 1f);

        Command(rig, weapon, held: true, now: 10.5f, extraButtons: Attack2);

        Assert.True(weapon.IsReleasing(1));
        Assert.Contains("add 1 Invulnerable d9 p1", rig.Conditions.Calls);
        Assert.Contains("add 3 Invulnerable d9 p1", rig.Conditions.Calls);
        Assert.Contains("Invulns 1 1", rig.Stats.Heals);
        Assert.Contains("event=player_chargedeployed targetid=103 userid=101", rig.Events.Fired);
    }

    [Fact]
    public void A_release_below_a_full_meter_is_refused()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);
        weapon.SetChargeLevel(1, 0.5f);

        Command(rig, weapon, held: true, now: 10.5f, extraButtons: Attack2);

        Assert.False(weapon.IsReleasing(1));
        Assert.DoesNotContain("add 1 Invulnerable d9 p1", rig.Conditions.Calls);
    }

    [Fact]
    public void The_second_press_of_a_release_is_refused()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);
        weapon.SetChargeLevel(1, 1f);
        Command(rig, weapon, held: true, now: 10.5f, extraButtons: Attack2);

        QueueKept(rig);
        Command(rig, weapon, held: true, now: 11f, extraButtons: Attack2);   // still held: the release guard refuses

        Assert.True(weapon.IsReleasing(1));                   // still releasing, not a second one
        Assert.Single(rig.Events.Fired, f => f.StartsWith("event=player_chargedeployed"));
    }

    [Fact]
    public void A_held_secondary_fires_the_release_the_moment_the_meter_fills()
    {
        // ItemPostFrame calls SecondaryAttack every frame IN_ATTACK2 is held
        // (tf_weapon_medigun.cpp:1605-1608) and lets its own guards refuse — so a
        // button held through the fill pops the uber the moment the meter reaches 1.0.
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f, extraButtons: Attack2);   // held on an empty meter
        Assert.False(weapon.IsReleasing(1));

        QueueKept(rig);
        weapon.SetChargeLevel(1, 1f);
        Command(rig, weapon, held: true, now: 11f, extraButtons: Attack2);   // still held, meter full

        Assert.True(weapon.IsReleasing(1));
        Assert.Contains("add 1 Invulnerable d9 p1", rig.Conditions.Calls);
    }

    [Fact]
    public void The_release_sees_the_target_the_primary_arm_just_acquired()
    {
        // Stock runs the primary arm first and the secondary after it (:1557-1608): the
        // frame a medic presses attack+attack2, the release recalculates the fresh target.
        var rig = Standing();
        var weapon = rig.NewWeapon();
        weapon.SetChargeLevel(1, 1f);
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f, extraButtons: Attack2);

        Assert.Equal(3, weapon.HealingTargetIndex(1));
        var fired = Assert.Single(rig.Events.Fired, f => f.StartsWith("event=player_chargedeployed"));
        Assert.Contains("targetid=103", fired);
    }

    [Fact]
    public void A_flag_carrier_patient_takes_no_uber_while_the_release_still_fires()
    {
        // Stock refuses the release only on the OWNER's CanRecieveMedigunChargeEffect
        // (tf_weapon_medigun.cpp:1863); the patient's refusal lives inside their own
        // RecalculateChargeEffects (tf_player_shared.cpp:8899-8902). The release starts,
        // the medic ubers, the flag carrier simply is not covered.
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);
        weapon.SetChargeLevel(1, 1f);
        rig.Ctf.Carriers.Add(3);

        Command(rig, weapon, held: true, now: 10.5f, extraButtons: Attack2);

        Assert.True(weapon.IsReleasing(1));
        Assert.Contains("add 1 Invulnerable d9 p1", rig.Conditions.Calls);
        Assert.DoesNotContain("add 3 Invulnerable d9 p1", rig.Conditions.Calls);
    }

    [Fact]
    public void A_release_fires_with_no_patient_and_the_event_has_no_targetid()
    {
        // tf_weapon_medigun.cpp:1822-1826: pTFPlayerPatient may be NULL; the release goes
        // on (:1915 guards the patient recalc, :1930-1933 the targetid key).
        var rig = Standing();
        var weapon = rig.NewWeapon();                       // no queued hits: no patient found
        weapon.SetChargeLevel(1, 1f);

        Command(rig, weapon, held: true, now: 10f, extraButtons: Attack2);

        Assert.True(weapon.IsReleasing(1));
        var fired = Assert.Single(rig.Events.Fired, f => f.StartsWith("event=player_chargedeployed"));
        Assert.DoesNotContain("targetid", fired);
    }

    [Fact]
    public void The_released_meter_drains_at_one_per_eight_seconds()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);
        weapon.SetChargeLevel(1, 1f);
        Command(rig, weapon, held: true, now: 10.5f, extraButtons: Attack2);

        weapon.TickMedic(1, now: 11.5f, frametime: 1f);       // one second of drain: 1/8 of the meter

        Assert.Equal(7f / 8f, weapon.ChargeLevel(1), 5);
    }

    [Fact]
    public void The_meters_state_rides_the_weapon_for_the_charge_meter()
    {
        // tf_hud_mediccharge.cpp reads the medigun's m_flChargeLevel (OnTick
        // :226); here the carried weapon's own state carries it, written at
        // every command the gun answers, next to the clocks the client's
        // replay of the same meter runs on. The release and the beam's
        // patient stay on the carrier.
        var rig = Standing();
        var carrier = new TFPlayerNetState();
        carrier.BindLocal();
        rig.Data.States[1] = carrier;
        var weapon = rig.NewWeapon();
        QueueFound(rig);

        var first = Attack(1, Attack1, now: 10f);
        weapon.Heal(in first, Medigun, held: true);
        Assert.Equal(3, carrier.HealTargetIndex);
        Assert.False(carrier.ChargeReleasing);
        Assert.Equal(0f, first.Weapon.BehaviourMeter, 5);   // the first frame pays nothing (LastHealCall unset)

        var second = Attack(1, Attack1, now: 10.1f);
        weapon.Heal(in second, Medigun, held: true);
        Assert.Equal(0.1f / 40f, second.Weapon.BehaviourMeter, 5);
        Assert.Equal(10.1f, second.Weapon.BehaviourTimeA, 4);

        weapon.SetChargeLevel(1, 1f);
        var release = Attack(1, Attack1 | Attack2, now: 10.2f);
        weapon.Heal(in release, Medigun, held: true);
        Assert.True(carrier.ChargeReleasing);
        Assert.Equal(1f, release.Weapon.BehaviourMeter, 5);
        Assert.Equal(10.2f, release.Weapon.BehaviourTimeB, 4);

        weapon.TickMedic(1, now: 11.2f, frametime: 1f);     // still draining: 7/8 left
        var drained = Attack(1, 0, now: 11.3f);
        weapon.Heal(in drained, Medigun, held: true);
        Assert.True(carrier.ChargeReleasing);
        Assert.Equal(7f / 8f, drained.Weapon.BehaviourMeter, 5);
    }

    [Fact]
    public void Re_detaching_the_same_patient_during_a_release_counts_its_surcharge_once()
    {
        // RemoveHealingTarget updates the entry's time when the target is already in
        // m_DetachedTargets (tf_weapon_medigun.cpp:1668-1686) — one entry per patient.
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);
        weapon.SetChargeLevel(1, 1f);
        Command(rig, weapon, held: true, now: 10.5f, extraButtons: Attack2);   // the release

        Command(rig, weapon, held: false, now: 10.6f);         // detach one
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10.7f);          // re-acquire the same patient
        Command(rig, weapon, held: false, now: 10.8f);         // detach again: the entry updates

        weapon.TickMedic(1, now: 11.3f, frametime: 1f);        // both detach stamps are still inside tf_invuln_time

        // one second at 1/8, slowed by half a rate for the one detached patient: 13/16 left
        Assert.Equal(0.8125f, weapon.ChargeLevel(1), 5);
    }

    [Fact]
    public void An_empty_release_stops_and_the_uber_begins_to_wear_off()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);
        weapon.SetChargeLevel(1, 1f);
        Command(rig, weapon, held: true, now: 10.5f, extraButtons: Attack2);

        weapon.TickMedic(1, now: 18.5f, frametime: 8f);       // the meter empties

        Assert.False(weapon.IsReleasing(1));
        Assert.Equal(0f, weapon.ChargeLevel(1), 5);
        Assert.True(rig.Conditions.Holds(1, TFCond.InvulnerableWearingoff));
        Assert.True(rig.Conditions.Holds(3, TFCond.InvulnerableWearingoff));
    }

    [Fact]
    public void Holstering_drops_the_beam_and_the_medic_s_own_uber_but_the_drain_goes_on()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);
        weapon.SetChargeLevel(1, 1f);
        Command(rig, weapon, held: true, now: 10.5f, extraButtons: Attack2);

        weapon.Holster(1, now: 10.6f);

        Assert.Equal(-1, weapon.HealingTargetIndex(1));
        Assert.False(rig.Conditions.Holds(1, TFCond.Invulnerable));

        // ItemHolsterFrame: the drain does not stop. The patient let go at 10.6 would slow it
        // until tf_invuln_time passes; this tick is past that window, so the plain rate answers.
        weapon.TickMedic(1, now: 11.7f, frametime: 1f);
        Assert.Equal(7f / 8f, weapon.ChargeLevel(1), 5);
        Assert.True(weapon.IsReleasing(1));
    }

    [Fact]
    public void A_medic_who_loses_his_medigun_leaves_his_patient_wearing_off()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);
        weapon.SetChargeLevel(1, 1f);
        Command(rig, weapon, held: true, now: 10.5f, extraButtons: Attack2);

        weapon.RemoveMedigun(1, now: 10.6f);

        Assert.False(rig.Conditions.Holds(1, TFCond.Invulnerable));
        Assert.True(rig.Conditions.Holds(3, TFCond.Invulnerable));            // wears off, not instant
        Assert.True(rig.Conditions.Holds(3, TFCond.InvulnerableWearingoff));
        rig.Healing.TestAndExpireChargeEffect(H(3), now: 11.7f);
        Assert.False(rig.Conditions.Holds(3, TFCond.Invulnerable));
        Assert.Equal(0f, weapon.ChargeLevel(1));
    }

    // ---- ItemPostFrame's taunt arm (tf_weapon_medigun.cpp:1575-1578) --------------------------

    [Fact]
    public void A_taunting_medic_drops_the_beam()
    {
        var rig = Standing();
        rig.Clients.ConVars[(1, "tf_medigun_autoheal")] = "1";
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);
        Assert.Equal(3, weapon.HealingTargetIndex(1));

        rig.Conditions.Add(H(1), TFCond.Taunting);
        Command(rig, weapon, held: false, now: 10.5f);

        Assert.Equal(-1, weapon.HealingTargetIndex(1));
    }

    [Fact]
    public void The_beam_returns_when_the_medic_stops_taunting_and_presses_again()
    {
        var rig = Standing();
        rig.Clients.ConVars[(1, "tf_medigun_autoheal")] = "1";
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);
        rig.Conditions.Add(H(1), TFCond.Taunting);
        Command(rig, weapon, held: false, now: 10.5f);       // the beam is down
        Assert.Equal(-1, weapon.HealingTargetIndex(1));

        rig.Conditions.Remove(H(1), TFCond.Taunting);
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 11f);          // the re-find's two traces on the patient

        Assert.Equal(3, weapon.HealingTargetIndex(1));
    }

    // ---- HealTargetThink's class-change re-attach (:960-975) --------------------------------------

    /// <summary>The patient's carrier, so the think can read the class — stock's GetPlayerClass.</summary>
    private static TFPlayerNetState PatientCarrier(MedigunRig rig, TFClass cls)
    {
        var state = new TFPlayerNetState();
        state.BindLocal();
        state.PlayerClass = (int)cls;
        rig.Data.States[3] = state;
        return state;
    }

    [Fact]
    public void A_patient_who_changes_class_under_an_unchanged_beam_is_dropped_so_the_medic_re_attaches()
    {
        // Stock's Lower() arm: the beam has been riding this same patient since the last
        // detach, and the think finds the class different (tf_weapon_medigun.cpp:964-974).
        var rig = Standing();
        rig.Clients.ConVars[(1, "tf_medigun_autoheal")] = "1";
        var carrier = PatientCarrier(rig, TFClass.Scout);
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);          // attach to patient 3
        Command(rig, weapon, held: false, now: 10.5f);       // the button up, autoheal holds the beam
        weapon.TickMedic(1, now: 10.6f, frametime: 0.02f);   // the priming think saves Scout
        Assert.Equal(3, weapon.HealingTargetIndex(1));

        QueueFound(rig);
        Command(rig, weapon, held: true, now: 11f);          // the toggle drops and re-attaches: this is the last patient too
        Assert.Equal(3, weapon.HealingTargetIndex(1));

        carrier.PlayerClass = (int)TFClass.Medic;            // the patient changed class mid-heal
        weapon.TickMedic(1, now: 12f, frametime: 0.02f);

        Assert.Equal(-1, weapon.HealingTargetIndex(1));      // Lower(): the beam drops for the re-evaluation
    }

    [Fact]
    public void A_freshly_acquired_patient_who_changed_class_keeps_the_beam()
    {
        // Stock's equality (:971): only the patient the medic last held re-attaches; a
        // beam just aimed at someone new already evaluated their attributes.
        var rig = Standing();
        rig.Clients.ConVars[(1, "tf_medigun_autoheal")] = "1";
        rig.Give(2, 100, 100);
        var third = new TFPlayerNetState();
        third.BindLocal();
        third.PlayerClass = (int)TFClass.Scout;
        var second = new TFPlayerNetState();
        second.BindLocal();
        second.PlayerClass = (int)TFClass.Scout;
        rig.Data.States[3] = third;
        rig.Data.States[2] = second;
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(rig, weapon, held: true, now: 10f);          // attach to patient 3
        Assert.Equal(3, weapon.HealingTargetIndex(1));

        Command(rig, weapon, held: false, now: 10.5f);       // button up: the press edge arms
        rig.QueueHit(H(2));
        rig.QueueHit(H(2));
        Command(rig, weapon, held: true, now: 11f);          // the press drops 3; the re-find lands on patient 2
        Assert.Equal(2, weapon.HealingTargetIndex(1));       // the last detach left 3 behind

        second.PlayerClass = (int)TFClass.Medic;             // the new patient changes class
        weapon.TickMedic(1, now: 12f, frametime: 0.02f);     // the first think: change, but a fresh beam

        Assert.Equal(2, weapon.HealingTargetIndex(1));       // stock's equality arm: no Lower here
    }
}

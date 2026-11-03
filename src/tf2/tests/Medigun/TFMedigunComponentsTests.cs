using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Medigun;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Medigun;

/// <summary>
/// The medigun lane's per-player component: the frame that heals, expires the
/// wear-off and drains the meter, the beam's stale-client watchdog, the
/// holster pair at the active-weapon transitions and the death arm. Patient 3,
/// medic 1, the same rig and stock script as the weapon's own facts.
/// </summary>
public sealed class TFMedigunComponentsTests
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

    private static MedigunRig Standing()
    {
        var rig = new MedigunRig();
        rig.Give(1, 100, 100);
        rig.Give(3, 100, 125);
        return rig;
    }

    private static void QueueFound(MedigunRig rig)
    {
        rig.QueueHit(H(3));
        rig.QueueHit(H(3));
    }

    private static void Command(TFMedigunWeapon weapon, bool held, float now, int extraButtons = 0)
    {
        var attack = Attack(1, held ? Attack1 | extraButtons : extraButtons, now);
        weapon.Heal(in attack, Medigun, held);
    }

    /// <summary>The meter released on a healing beam, as the release facts leave it.</summary>
    private static (MedigunRig Rig, TFMedigunWeapon Weapon) Releasing(MedigunRig rig, TFMedigunWeapon weapon)
    {
        QueueFound(rig);
        Command(weapon, held: true, now: 10f);
        weapon.SetChargeLevel(1, 1f);
        Command(weapon, held: true, now: 10.5f, extraButtons: Attack2);
        return (rig, weapon);
    }

    /// <summary>The player's lane component, over a hand-settable active weapon.</summary>
    private static TFMedigunComponent Component(MedigunRig rig, TFMedigunWeapon weapon, int index,
                                                Dictionary<int, string> active)
    {
        rig.Engine.ReturnsEachCall("Players.GetActiveWeapon",
            () => rig.Engine.Calls[^1].Args[0] is EntityHandle h && active.TryGetValue(h.EntryIndex, out var w) ? w : "tf_weapon_pistol");
        var component = (TFMedigunComponent)new TFMedigunComponents(rig.Healing, weapon, rig.Engine).Create(H(index)).Single();
        component.Attach(H(index));
        return component;
    }

    // ---- the frame ------------------------------------------------------------------

    [Fact]
    public void A_healed_patient_takes_the_heal_rate_as_the_component_ticks()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        rig.NoteDamage(3, time: 10f);
        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        var component = Component(rig, weapon, 3, []);

        component.Tick(10f);                                  // the priming tick: no frametime yet
        component.Tick(11f);

        Assert.Contains("take 3 24 True", rig.Resupply.Takes);
    }

    [Fact]
    public void The_wear_off_expires_as_the_component_ticks()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        rig.Healing.Heal(H(3), H(1), 24f, 10f);
        rig.Healing.NoteReleasing(1, true);
        rig.Healing.RecalculateChargeEffects(H(3), now: 10f);
        rig.Healing.NoteReleasing(1, false);
        rig.Healing.RecalculateChargeEffects(H(3), now: 10.5f);   // wearing off; expires past 11.5
        var component = Component(rig, weapon, 3, []);

        component.Tick(10.5f);
        component.Tick(11.7f);

        Assert.False(rig.Conditions.Holds(3, TFCond.Invulnerable));
        Assert.False(rig.Conditions.Holds(3, TFCond.InvulnerableWearingoff));
    }

    [Fact]
    public void A_player_with_no_medigun_lane_ticks_harmlessly()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        var component = Component(rig, weapon, 5, []);

        component.Tick(10f);
        component.Tick(11f);

        Assert.Empty(rig.Conditions.Calls);
        Assert.Empty(rig.Resupply.Takes);
        Assert.False(rig.Engine.Called("Entities.SetHealth"));
    }

    // ---- the medic's watchdog and drain ---------------------------------------------

    [Fact]
    public void A_stale_client_drops_the_beam_as_the_watchdog_thinks()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        QueueFound(rig);
        Command(weapon, held: true, now: 10f);
        var component = Component(rig, weapon, 1, new() { [1] = TFMedigunComponent.MedigunName });

        for (float t = 10f; t <= 16f; t += 1f)
            component.Tick(t);                                    // five seconds past the last command

        Assert.Equal(-1, weapon.HealingTargetIndex(1));
        Assert.Equal(0, rig.Healing.NumHealers(3));
    }

    [Fact]
    public void The_released_meter_drains_as_the_component_ticks()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        Releasing(rig, weapon);
        var component = Component(rig, weapon, 1, new() { [1] = TFMedigunComponent.MedigunName });

        component.Tick(10.5f);                                    // prime: no frametime
        component.Tick(11.5f);                                    // one second of drain

        Assert.Equal(7f / 8f, weapon.ChargeLevel(1), 5);
    }

    // ---- the active-weapon transitions and death --------------------------------------

    [Fact]
    public void Switching_out_of_the_medigun_holsters_it_once_and_the_drain_goes_on()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        Releasing(rig, weapon);
        var active = new Dictionary<int, string> { [1] = TFMedigunComponent.MedigunName };
        var component = Component(rig, weapon, 1, active);
        component.Tick(10.5f);                                    // in hand: the deploy transition
        active[1] = "tf_weapon_pistol";

        component.Tick(11f);                                      // half a second, then the holster
        component.Tick(12.1f);                                    // past the detach window: plain rate

        Assert.True(weapon.IsReleasing(1));
        Assert.Equal(-1, weapon.HealingTargetIndex(1));
        Assert.False(rig.Conditions.Holds(1, TFCond.Invulnerable));
        // 1 − 0.5/8 − 1.1/8: the holster took the medic's own uber exactly once, and the
        // drain did not stop for the hand change.
        Assert.Equal(0.8f, weapon.ChargeLevel(1), 5);
        Assert.Equal(1, rig.Conditions.Calls.Count(c => c == "remove 1 Invulnerable"));
    }

    [Fact]
    public void A_release_still_running_when_the_medigun_comes_back_covers_the_medic_again()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        Releasing(rig, weapon);
        var active = new Dictionary<int, string> { [1] = TFMedigunComponent.MedigunName };
        var component = Component(rig, weapon, 1, active);
        component.Tick(10.5f);
        active[1] = "tf_weapon_pistol";
        component.Tick(11f);                                      // holstered: the releaser report went false
        Assert.False(rig.Conditions.Holds(1, TFCond.Invulnerable));

        active[1] = TFMedigunComponent.MedigunName;
        component.Tick(12f);                                      // back in hand: Deploy's own RecalcEffectOnTarget

        // The add must be a fresh one, after the holster's removal — not the release's original.
        int remove = rig.Conditions.Calls.IndexOf("remove 1 Invulnerable");
        Assert.True(remove >= 0 && rig.Conditions.Calls.LastIndexOf("add 1 Invulnerable d9 p1") > remove,
            "the re-deploy re-added the medic's uber after the holster took it");
    }

    [Fact]
    public void A_dead_medic_loses_the_beam_the_release_and_the_uber()
    {
        var rig = Standing();
        var weapon = rig.NewWeapon();
        Releasing(rig, weapon);
        var component = Component(rig, weapon, 1, new() { [1] = TFMedigunComponent.MedigunName });

        rig.Dead.Add(1);
        component.Tick(11f);

        Assert.Equal(-1, weapon.HealingTargetIndex(1));
        Assert.Equal(0f, weapon.ChargeLevel(1));
        Assert.False(weapon.IsReleasing(1));
        // The medic's own uber goes instantly (RecalcEffectOnTarget(pOwner, true)); the
        // patient's wears off over tf_invuln_time (UpdateOnRemove -> RemoveHealingTarget ->
        // StopHealing -> RecalculateChargeEffects, tf_weapon_medigun.cpp:459-469).
        Assert.False(rig.Conditions.Holds(1, TFCond.Invulnerable));
        Assert.True(rig.Conditions.Holds(3, TFCond.Invulnerable));
        Assert.True(rig.Conditions.Holds(3, TFCond.InvulnerableWearingoff));
        Assert.Equal(0, rig.Healing.NumHealers(3));
    }

    // ---- the registration --------------------------------------------------------------

    [Fact]
    public void The_source_takes_its_place_in_the_band_with_one_ordered_component()
    {
        var rig = Standing();
        var source = new TFMedigunComponents(rig.Healing, rig.NewWeapon(), rig.Engine);

        Assert.Equal(TFMedigunOrders.Components, source.HookOrder);
        Assert.InRange(source.HookOrder, 1_000_000 - 1_000 + 900, 1_000_000 - 1_000 + 929);
        Assert.Equal(40, Assert.Single(source.Create(H(1))).Order);   // the wearables' 30 is not tied
    }
}

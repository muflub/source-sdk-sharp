using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Conditions;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The compound bow's behaviour: the draw, the loose, the lower and the tired
/// shot, each as <see cref="TFBowBehaviour"/> runs it through the machine
/// (<c>tf_weapon_compound_bow.cpp</c>, the citations there).
/// </summary>
public sealed class TFBowBehaviourTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;
    private const int Owner = TFWeaponWorld.Owner;

    /// <summary>A seed whose gun roll draws no crit: the bow's crit is the boosted only.</summary>
    private static int NoCrit => TFWeaponWorld.GunSeed(200, 10000);

    private static TFWeaponWorld Drawn(out WeaponNetState w, out ViewModelNetState vm, float at = 10f)
    {
        var world = new TFWeaponWorld();
        world.Arms.Sequences[WeaponAnim.Charge] = 7;
        (w, vm) = world.Armed("tf_weapon_compound_bow", clip: 1, reserve: 12);
        world.Owners.Of(Owner); // the conditions the drawn bow's TF_COND_AIMING lands in
        world.Run(w, vm, Attack, at, NoCrit);
        return world;
    }

    [Fact]
    public void The_draw_charges_and_fires_nothing_while_held()
    {
        var world = Drawn(out var w, out var vm);

        // The draw: the meter, the wind ACT_ITEM2_VM_CHARGE (the PULLBACK
        // ask stock's SendWeaponAnim retags, :396-399) and the condition the
        // slowed 160 hangs from (PrimaryAttack :208-251).
        Assert.Equal(10f, w.BehaviourMeter, 4);
        Assert.Equal(7, vm.Sequence);
        Assert.True(world.Owners.Of(Owner).Has(TFCond.Aiming));

        // Held past the charge window: stock's m_bNoAutoRelease leaves the
        // pipebomb base's force-release dead — the bow waits for the trigger
        // (:254-263's no-op else), and no round is spent either.
        world.Run(w, vm, Attack, 10.5f, NoCrit);
        world.Run(w, vm, Attack, 11.5f, NoCrit);

        Assert.Empty(world.Effects.Projectiles);
        Assert.Equal(1, w.Clip);
        Assert.Equal(10f, w.BehaviourMeter, 4);
        Assert.Empty(world.Arms.Launches);
    }

    [Fact]
    public void The_release_looses_one_arrow_at_the_charge()
    {
        var world = Drawn(out var w, out var vm, at: 10f);
        world.Run(w, vm, Attack, 10.5f, NoCrit);

        world.Run(w, vm, 0, 10.515f, NoCrit);

        // One arrow, at the draw: the charge stock's GetCurrentCharge clamps
        // (:281-287) — 0.515 s of the one-second window — the round spent, the
        // meter gone and the condition with it (LaunchGrenade :165), the next
        // draw the script's 2 s off (:159) and the idle half the rate again
        // (:162-163).
        var launch = Assert.Single(world.Effects.Projectiles);
        Assert.Equal("tf_weapon_compound_bow", launch.Weapon);
        Assert.Equal(0.515f, launch.Charge, 3);
        Assert.False(launch.Shot.Critical);
        Assert.Equal(0, w.Clip);
        Assert.Equal(0f, w.BehaviourMeter, 4);
        Assert.False(world.Owners.Of(Owner).Has(TFCond.Aiming));
        Assert.Equal(12.515f, w.NextPrimaryAttack, 3);
        Assert.Equal(13.015f, w.TimeWeaponIdle, 3);
        Assert.False(TFBowBehaviour.Tired(w));

        // The loosing itself is the pipebomb base's frame arm (ItemPostFrame
        // :167-170), so the release tick's own fire path ran none of it.
        Assert.Empty(world.Arms.Bullets);
    }

    [Fact]
    public void A_second_click_lowers_and_the_next_draw_waits_for_both_buttons()
    {
        var world = Drawn(out var w, out var vm, at: 10f);
        world.Arms.Sequences[WeaponAnim.DryFire] = 9;

        // The lower (SecondaryAttack :330 → LowerBow :338-358): no arrow
        // loosed, the charge gone — the condition follows in the frame tail —
        // the next draw a second off and m_bNoFire set, the dry fire played.
        world.Run(w, vm, Attack2, 10.5f, NoCrit);

        Assert.Empty(world.Effects.Projectiles);
        Assert.Equal(1, w.Clip);
        Assert.Equal(0f, w.BehaviourMeter, 4);
        Assert.False(world.Owners.Of(Owner).Has(TFCond.Aiming));
        Assert.Equal(11.5f, w.NextPrimaryAttack, 3);
        Assert.Equal(9, vm.Sequence);
        Assert.True((w.BehaviourMode & TFBowBehaviour.ModeNoFire) != 0);

        // The trigger alone does not draw through m_bNoFire (:193-194).
        world.Run(w, vm, Attack, 12f, NoCrit);
        Assert.Equal(0f, w.BehaviourMeter, 4);

        // Both up, the draw again (:457-460) — and it draws.
        world.Run(w, vm, 0, 12.015f, NoCrit);
        Assert.True((w.BehaviourMode & TFBowBehaviour.ModeNoFire) == 0);

        world.Run(w, vm, Attack, 12.03f, NoCrit);
        Assert.Equal(12.03f, w.BehaviourMeter, 4);
    }

    [Fact]
    public void A_draw_past_five_seconds_fires_the_tired_shot()
    {
        var world = Drawn(out var w, out var vm, at: 10f);
        world.Run(w, vm, Attack, 12f, NoCrit);
        world.Run(w, vm, Attack, 15f, NoCrit);

        // Five seconds drawn (TF_ARROW_MAX_CHARGE_TIME, the jitter's gate at
        // :495): the launch is the tired one, marked for the arrow's spawn,
        // and the charge is still the window's one.
        world.Run(w, vm, 0, 15.016f, NoCrit);

        var launch = Assert.Single(world.Effects.Projectiles);
        Assert.Equal(1f, launch.Charge, 3);
        Assert.True(TFBowBehaviour.Tired(w));
    }

    [Fact]
    public void A_refused_attack_empties_the_draw()
    {
        var world = Drawn(out var w, out var vm, at: 10f);

        // A cloaked owner's refused attack clears the charge, as stock's
        // !CanAttack arm does (:196-200) — the condition follows in the tail.
        world.Owners.Of(Owner).Add(TFCond.Stealthed);
        world.Run(w, vm, Attack, 10.5f, NoCrit);

        Assert.Empty(world.Effects.Projectiles);
        Assert.Equal(0f, w.BehaviourMeter, 4);
        Assert.False(world.Owners.Of(Owner).Has(TFCond.Aiming));
    }

    [Fact]
    public void The_draw_pulls_at_the_refire_rate_and_the_hands_ride_it()
    {
        var world = Drawn(out var w, out var vm);

        // The tail of the draw (:212-248): the rate through the bow's chain
        // (one by default) inverted; the viewmodel rate takes it and the pull
        // sounds once through the command at the rate's pitch. The held
        // trigger's held ticks (the else of :254-263) add no second pull.
        Assert.Equal("Weapon_CompoundBow.SinglePull @1", Assert.Single(world.Arms.Sounds));
        Assert.Equal(1f, vm.PlaybackRate, 4);

        world.Run(w, vm, Attack, 10.5f, NoCrit);
        Assert.Equal("Weapon_CompoundBow.SinglePull @1", Assert.Single(world.Arms.Sounds));
    }

    [Fact]
    public void The_fire_anims_round_fills_the_clip_back_while_the_delay_runs()
    {
        var world = Drawn(out var w, out var vm);
        world.Run(w, vm, Attack, 10.5f, NoCrit);
        world.Run(w, vm, 0, 10.515f, NoCrit);

        // The launch leaves one round out (:171-173's empty clip); the fire
        // animation's AE_WPN_INCREMENTAMMO (:446) pulls one back from the pool
        // while the script's two-second delay is still running — the machine's
        // own reload could not at this tick, its gate is the delay's expiry.
        world.Run(w, vm, 0, 11.5f, NoCrit);

        Assert.Equal(1, w.Clip);
        Assert.Equal(11, w.Reserve);
        Assert.Equal(0, w.AnimEvent);
    }

    [Fact]
    public void The_held_charge_returns_to_the_held_idle_when_full()
    {
        var world = Drawn(out var w, out var vm);
        world.Arms.Sequences[WeaponAnim.IdleBow] = 11;
        Assert.Equal(7, vm.Sequence);

        // Held inside the charge window the full-charge ask (:468-471) takes
        // the plain held bow idle, ACT_ITEM2_VM_IDLE_2 (:401-427).
        world.Run(w, vm, Attack, 11.5f, NoCrit);
        Assert.Equal(11, vm.Sequence);
    }

    [Fact]
    public void The_tired_held_idle_enters_on_the_charge_idle_and_settles()
    {
        var world = Drawn(out var w, out var vm);
        world.Arms.Sequences[WeaponAnim.IdleBowChargeFull] = 13;
        world.Arms.Sequences[WeaponAnim.IdleBowFull] = 12;

        // Past the tired window (:495's gate, five seconds) the held idle
        // enters on CHARGE_IDLE_3 (:419), settles on IDLE_3 (:415) one tick
        // later, and holds it (:401-427's pair).
        world.Run(w, vm, Attack, 15.5f, NoCrit);
        Assert.Equal(13, vm.Sequence);

        world.Run(w, vm, Attack, 16.5f, NoCrit);
        Assert.Equal(12, vm.Sequence);

        world.Run(w, vm, Attack, 17.5f, NoCrit);
        Assert.Equal(12, vm.Sequence);
    }
}

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// <c>CTFCrossbow::Holster</c> (<c>tf_weapon_rocketlauncher.cpp:673-697</c>):
/// the empty crossbow reloads itself silently on the switch — one bolt in,
/// the idle and next-fire clocks pushed behind the reload — so the hands come
/// back to an armed gun. Stock's comment says like the flaregun; neither
/// stock's flare gun nor ours reloads there, this arm is the crossbow's own.
/// </summary>
public sealed class TFCrossbowHolsterFacts
{
    private const string Crossbow = "tf_weapon_crossbow";
    private const int Attack = (int)InputButtons.Attack;

    /// <summary>The shipped extra's clocks (:679-684): fire delay 0.23, reload 1.5.</summary>
    private const float FireDelay = 0.23f;
    private const float ReloadTime = 1.5f;

    [Fact]
    public void Holstering_the_empty_crossbow_loads_one_bolt_and_pushes_the_clocks()
    {
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed(Crossbow, clip: 1, reserve: 8);

        // The real press spends the bolt at clock 10 (clip_size 1): empty,
        // its launch having written NextPrimaryAttack at now + fireDelay.
        world.Run(w, vm, Attack, now: 10f);
        Assert.Equal(0, w.Clip);

        // The switch away runs the Holster arm Arsenal.cs calls on the
        // outgoing weapon before the clip is stored back. Stock pushes both
        // clocks to lastFire(+fireDelay)+reload when earlier (:686-691),
        // then loads the round (:693).
        world.Behaviour(Crossbow).Holster(world.Attack(w, vm, now: 11f));

        Assert.Equal(1, w.Clip);                                  // IncrementAmmo (:693)
        Assert.Equal(7, w.Reserve);
        Assert.Equal(10f + FireDelay + ReloadTime, w.NextPrimaryAttack, 3);   // :686-690 summed
        Assert.Equal(10f + FireDelay + ReloadTime, w.TimeWeaponIdle, 3);      // the push's twin (:689)
    }

    [Fact]
    public void Holstering_a_loaded_crossbow_changes_nothing()
    {
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed(Crossbow, clip: 1, reserve: 8);
        w.NextPrimaryAttack = 4f;
        w.TimeWeaponIdle = 4f;

        world.Behaviour(Crossbow).Holster(world.Attack(w, vm, now: 10f));

        // The clip door (:676): a bolt already in the clip and the holster
        // writes neither clocks nor clip.
        Assert.Equal(1, w.Clip);
        Assert.Equal(8, w.Reserve);
        Assert.Equal(4f, w.NextPrimaryAttack, 3);
        Assert.Equal(4f, w.TimeWeaponIdle, 3);
    }

    [Fact]
    public void An_idle_clock_ahead_of_the_sum_stands()
    {
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed(Crossbow, clip: 0, reserve: 5);
        w.TimeWeaponIdle = 30f;

        world.Behaviour(Crossbow).Holster(world.Attack(w, vm, now: 10f));

        // :687 compares before writing: a later idle is kept, the bolt still
        // loads.
        Assert.Equal(1, w.Clip);
        Assert.Equal(30f, w.TimeWeaponIdle, 3);
    }
}

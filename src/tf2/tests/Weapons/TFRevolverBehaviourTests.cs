using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// <see cref="TFRevolverBehaviour"/>: <c>CTFRevolver</c>'s accuracy clock —
/// the stamp of <c>PrimaryAttack</c> (<c>tf_weapon_revolver.cpp:157</c>) and
/// the crosshair scale of <c>GetWeaponCrosshairScale</c> (:207-223), whose
/// sample times are stock's own: the <c>RemapValClamped</c> pair 1.0, 0.5 to
/// 0.75, 2.5 clamped at both ends.
/// </summary>
public sealed class TFRevolverBehaviourTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const string Revolver = "tf_weapon_revolver";

    private static WeaponNetState ClockSetAt(float stamp)
    {
        var w = new WeaponNetState();
        w.BindLocal(); // the generated properties write into the state's own buffer
        w.BehaviourKeptTime = stamp; // the clock TFRevolverBehaviour rides (stock's m_flLastAccuracyCheck)
        return w;
    }

    [Theory]
    [InlineData(10.00f, 2.5f)]  // just fired: the full bloom (clamped high end)
    [InlineData(10.25f, 2.5f)]  // :217's remap clamps everything at or under 0.5 s to 2.5
    [InlineData(10.50f, 2.5f)]  // the exact pair end
    [InlineData(10.75f, 1.625f)] // midway between the pairs
    [InlineData(11.00f, 0.75f)] // one second on the clock: the floor
    [InlineData(12.50f, 0.75f)] // and clamped there
    public void The_headshot_blooms_the_crosshair_off_the_clocks_age(float now, float expected)
    {
        // A shot at 10 s, the HUD asking at `now`: CanHeadshot's remap
        // (tf_weapon_revolver.cpp:217), stock's sample times.
        Assert.Equal(expected, TFRevolverBehaviour.CrosshairScale(ClockSetAt(10f), now, canHeadshot: true), 4);
    }

    [Theory]
    [InlineData(10.00f)]
    [InlineData(10.75f)]
    [InlineData(12.50f)]
    public void A_revolver_without_the_headshot_mode_keeps_the_plain_crosshair(float now)
    {
        // :221: without set_weapon_mode 1 the base answers 1.0 whatever the
        // clock says — the stock spy revolver never blooms.
        Assert.Equal(1f, TFRevolverBehaviour.CrosshairScale(ClockSetAt(10f), now, canHeadshot: false), 4);
    }

    private static (TFWeaponWorld World, WeaponNetState W, ViewModelNetState Vm) Spy(int clip = 6, int reserve = 24)
    {
        var world = new TFWeaponWorld();
        world.Owners.Classes[TFWeaponWorld.Owner] = TFClass.Spy;
        var (w, vm) = world.Armed(Revolver, clip: clip, reserve: reserve);
        return (world, w, vm);
    }

    [Fact]
    public void A_fired_shot_stamps_the_accuracy_clock()
    {
        var (world, w, vm) = Spy();
        int seed = TFWeaponWorld.GunSeed(200, 10000);

        world.Run(w, vm, Attack, now: 10f, seed: seed);

        // :157: the fired shot's curtime, and only for a shot that fired.
        var shot = Assert.Single(world.Arms.Bullets);
        Assert.Equal(Revolver, shot.Weapon);
        Assert.Equal(5, w.Clip);
        Assert.Equal(10f, w.BehaviourKeptTime, 4);
    }

    [Fact]
    public void A_shot_still_on_the_fire_delay_stamps_nothing()
    {
        var (world, w, vm) = Spy();
        int seed = TFWeaponWorld.GunSeed(200, 10000);

        world.Run(w, vm, Attack, now: 10f, seed: seed);
        Assert.Single(world.Arms.Bullets);

        // Stock's :130 return (the timer) precedes the stamp; the machine's
        // owed-shots arithmetic answers the same.
        world.Run(w, vm, Attack, now: 10f + 0.015f, seed: seed);

        Assert.Single(world.Arms.Bullets);
        Assert.Equal(10f, w.BehaviourKeptTime, 4);
    }

    [Fact]
    public void An_empty_clip_reloads_and_stamps_nothing()
    {
        var (world, w, vm) = Spy(clip: 0);
        int seed = TFWeaponWorld.GunSeed(200, 10000);

        // Stock's :126 return (no ammunition) precedes the stamp; the machine
        // turns the empty-clip click into a reload.
        world.Run(w, vm, Attack, now: 10f, seed: seed);

        Assert.Empty(world.Arms.Bullets);
        Assert.Equal(0f, w.BehaviourKeptTime, 4);
    }

    [Fact]
    public void The_revolver_runs_a_behaviour_of_its_own()
    {
        // The kind's row asks for tf2.revolver and gets the accuracy
        // behaviour, not the plain gun's.
        var world = new TFWeaponWorld();
        Assert.IsType<TFRevolverBehaviour>(world.Behaviour(Revolver));
    }
}

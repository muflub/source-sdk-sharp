using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;

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

    /// <summary>mult_dmg_disguised (the Enforcer) through the real press: the
    /// undisguised shot lands the row's damage, and once the owner wears the
    /// disguise (stock's <c>InCond(TF_COND_DISGUISED)</c>,
    /// <c>tf_weaponbase_gun.cpp:913</c>) the next shot times that by the item's
    /// multiplier (<c>:916</c>) — the arm riding the machine's hitscan ask at
    /// <c>ModifyShot</c>, not a hand-built context.</summary>
    [Fact]
    public void A_disguised_spy_press_times_the_shot_by_the_disguise_multiplier()
    {
        var world = new TFWeaponWorld();
        world.Owners.Classes[TFWeaponWorld.Owner] = TFClass.Spy;
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(9002, "disguise bonus", "mult_dmg_disguised", AttributeDescriptionFormat.Percentage,
                                                               AttributeEffectType.Positive, false, false, AttributeValueType.Default,
                                                               MergedKeys.Block("x")), 1.5f));
        world.Owners.Items[TFWeaponWorld.Owner] = item;
        var (w, vm) = world.Armed(Revolver, clip: 6, reserve: 24);
        int seed = TFWeaponWorld.GunSeed(200, 10000);

        world.Run(w, vm, Attack, now: 10f, seed: seed);
        Assert.Equal(new ShotDamage(40f), world.Arms.Bullets[0].Shot);   // the row's damage, plain

        world.Owners.Of(TFWeaponWorld.Owner).Add(TFCond.Disguised);      // stock's disguise cond, :913
        world.Run(w, vm, Attack, now: 10.6f, seed: seed);

        Assert.Equal(2, world.Arms.Bullets.Count);
        Assert.Equal(new ShotDamage(60f), world.Arms.Bullets[1].Shot);   // :916 — the disguised multiplier rides the press
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

    /// <summary>Stock's bucket spend (<c>tf_weapon_revolver.cpp:159-171</c>): the shot
    /// just fired asks the bucket service for one stored crit, at the holder's index —
    /// the gate and clamp of the spend live in the service, not the behaviour.</summary>
    [Fact]
    public void A_fired_shot_spends_one_stored_crit()
    {
        var bucket = new RecordingBucket();
        var world = new TFWeaponWorld(bucket: bucket);
        world.Owners.Classes[TFWeaponWorld.Owner] = TFClass.Spy;
        var (w, vm) = world.Armed(Revolver, clip: 6, reserve: 24);
        int seed = TFWeaponWorld.GunSeed(200, 10000);

        world.Run(w, vm, Attack, now: 10f, seed: seed);

        Assert.Single(world.Arms.Bullets);   // the shot fired
        Assert.Equal(TFWeaponWorld.Owner, Assert.Single(bucket.Spent));
    }

    /// <summary>The bucket as the facts see it: what the shots spent, nothing else.</summary>
    private sealed class RecordingBucket : ITFRevengeCrits
    {
        public readonly List<int> Spent = new();

        public void Grant(EntityHandle owner) { }

        public void SpendOnShot(int ownerIndex) => Spent.Add(ownerIndex);
    }

    [Fact]
    public void The_revolver_runs_a_behaviour_of_its_own()
    {
        // The kind's row asks for tf2.revolver and gets the accuracy
        // behaviour, not the plain gun's.
        var world = new TFWeaponWorld();
        Assert.IsType<TFRevolverBehaviour>(world.Behaviour(Revolver));
    }

    /// <summary>The Ambassador's item: set_weapon_mode 1, stock's CanHeadshot
    /// (tf_weapon_revolver.h:49), as the damage layer's facts dress it.</summary>
    private static AttributeContainer HeadshotItem(int defIndex)
    {
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(defIndex, "mode " + defIndex, "set_weapon_mode",
                                                                AttributeDescriptionFormat.Additive,
                                                                AttributeEffectType.Neutral, false, false,
                                                                AttributeValueType.Default,
                                                                MergedKeys.Block("m")), 1f));
        return item;
    }

    /// <summary>The plain revolver's cone, fired once by a gun without the headshot mode.</summary>
    private static float PlainCone()
    {
        var world = new TFWeaponWorld();
        world.Owners.Classes[TFWeaponWorld.Owner] = TFClass.Spy;
        var (w, vm) = world.Armed(Revolver, clip: 6, reserve: 24);
        world.Run(w, vm, Attack, now: 10f, seed: GunSeed);
        return Assert.Single(world.Arms.Bullets).Spread;
    }

    private const int GunSeed = 200;

    [Fact]
    public void A_shot_past_the_second_is_dead_accurate()
    {
        // GetWeaponSpread's headshot arm (tf_weapon_revolver.cpp:187-201):
        // 1.1 s after the stamp the remap's clamped low end zeroes the cone.
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = HeadshotItem(9120);
        world.Owners.Classes[TFWeaponWorld.Owner] = TFClass.Spy;
        var (w, vm) = world.Armed(Revolver, clip: 6, reserve: 24);

        world.Run(w, vm, Attack, now: 10f, seed: GunSeed);      // the stamp
        world.Run(w, vm, Attack, now: 11.1f, seed: GunSeed);    // 1.1 s on the clock

        Assert.Equal(2, world.Arms.Bullets.Count);
        Assert.Equal(0f, world.Arms.Bullets[^1].Spread, 4);
    }

    [Fact]
    public void A_shot_within_the_half_second_keeps_the_script_cone()
    {
        // The same remap's clamped high end (:195, the 0.5 pair end): the
        // full cone the row was dressed with, the plain gun's.
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = HeadshotItem(9121);
        world.Owners.Classes[TFWeaponWorld.Owner] = TFClass.Spy;
        var (w, vm) = world.Armed(Revolver, clip: 6, reserve: 24);

        world.Run(w, vm, Attack, now: 10f, seed: GunSeed);
        world.Run(w, vm, Attack, now: 10.5f, seed: GunSeed);   // the gun's 0.5 refire, the age the pair ends at

        Assert.Equal(2, world.Arms.Bullets.Count);
        Assert.Equal(PlainCone(), world.Arms.Bullets[^1].Spread, 4);
    }

    [Fact]
    public void The_plain_revolver_never_tightens_on_the_clock()
    {
        // Without set_weapon_mode 1 the spread arm never runs (:191): the
        // shot a second on is the same cone as the rapid one.
        var world = new TFWeaponWorld();
        world.Owners.Classes[TFWeaponWorld.Owner] = TFClass.Spy;
        var (w, vm) = world.Armed(Revolver, clip: 6, reserve: 24);

        world.Run(w, vm, Attack, now: 10f, seed: GunSeed);
        world.Run(w, vm, Attack, now: 11.1f, seed: GunSeed);

        Assert.Equal(2, world.Arms.Bullets.Count);
        Assert.Equal(PlainCone(), world.Arms.Bullets[^1].Spread, 4);
    }
}

using SourceSharp.Abi;

using SourceSharp.Shared;

using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The jars at the machine (<c>CTFJar::PrimaryAttack</c>,
/// <c>tf_weapon_jar.cpp:114-128</c>, the milk its second face): the count
/// gate that is the effect bar, the pool the throw spends and the regen the
/// frame tail runs — over the shipped scripts' clipless purse.
/// </summary>
public sealed class TFJarAttackFacts
{
    private const int Attack = (int)InputButtons.Attack;

    /// <summary>A seed whose gun roll draws no crit: the jars' shot is the plain one here.</summary>
    private static int NoCrit => TFWeaponWorld.GunSeed(200, 10000);

    private static TFWeaponScript JarScript(string name)
    {
        var files = WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses());
        return new TFWeaponScripts(new RecordingLog(), files).Get(name)
            ?? throw new InvalidOperationException($"no script {name}");
    }

    [Fact]
    public void The_jars_run_the_projectile_behaviour()
    {
        Assert.Equal(TFWeaponKind.Projectile, TFWeaponKinds.Of(JarScript("tf_weapon_jar")));
        Assert.Equal(TFWeaponKind.Projectile, TFWeaponKinds.Of(JarScript("tf_weapon_jar_milk")));

        // And the rows the machine reads carry it: the world's catalogue has
        // both, the behaviour key answering the projectile shell.
        var world = new TFWeaponWorld(extras: true);
        Assert.Equal(TFWeaponKinds.Key(TFWeaponKind.Projectile), world.Row("tf_weapon_jar").Behaviour);
        Assert.Equal(TFWeaponKinds.Key(TFWeaponKind.Projectile), world.Row("tf_weapon_jar_milk").Behaviour);
        Assert.NotNull(world.Behaviour("tf_weapon_jar"));
        Assert.NotNull(world.Behaviour("tf_weapon_jar_milk"));
    }

    [Fact]
    public void An_unfinished_regen_refuses_the_throw()
    {
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed("tf_weapon_jar", clip: 0, reserve: 0);
        w.ItemMeter = 0.5f;

        world.Run(w, vm, Attack, 10f, NoCrit);

        // The count is still zero — stock's grant lands at the regen's end,
        // not at the bar's first float — so the machine's empty answer, and
        // the purse the head-of-command restock derives stays empty with it.
        Assert.Empty(world.Effects.Projectiles);
        Assert.Equal(0, w.Reserve);
        Assert.Equal(0, w.Clip);
    }

    [Fact]
    public void A_finished_regen_throws_and_spends_the_pool()
    {
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed("tf_weapon_jar", clip: 0, reserve: 0);
        w.ItemMeter = 1f;

        world.Run(w, vm, Attack, 10f, NoCrit);

        // The jar's lob, asked of the effects under its own name; the pool
        // spent to empty and the bar with it (FireJar's StartEffectBarRegen).
        var launch = Assert.Single(world.Effects.Projectiles);
        Assert.Equal("tf_weapon_jar", launch.Weapon);
        Assert.Equal(0, w.Reserve);
        Assert.Equal(0f, w.ItemMeter);
        Assert.Equal(10.8f, w.NextPrimaryAttack, 2);
        Assert.Empty(world.Arms.Bullets);
    }

    [Fact]
    public void The_milk_throws_under_its_own_name()
    {
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed("tf_weapon_jar_milk", clip: 0, reserve: 0);
        w.ItemMeter = 1f;

        world.Run(w, vm, Attack, 10f, NoCrit);

        Assert.Equal("tf_weapon_jar_milk", Assert.Single(world.Effects.Projectiles).Weapon);
        Assert.Equal(0, w.Reserve);
    }

    [Fact]
    public void The_bar_refills_over_the_jars_twenty_seconds_and_throws_again()
    {
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed("tf_weapon_jar", clip: 0, reserve: 0);

        // The frame tail's regen at InternalGetEffectBarRechargeTime's 20.1 s
        // (tf_weapon_jar.h:59): idle commands a second apart walk the bar up —
        // the first command stamps and pays nothing, a longer span than the
        // tick's command interval pays nothing either — and the full bar is
        // the count's one again, the next throw waiting on nothing.
        world.Run(w, vm, buttons: 0, now: 10f, seed: NoCrit);
        for (int k = 1; k <= 21; k++)
            world.Run(w, vm, buttons: 0, now: 10f + k, seed: NoCrit);

        Assert.Equal(1f, w.ItemMeter);

        world.Run(w, vm, Attack, 33f, NoCrit);
        Assert.Equal("tf_weapon_jar", Assert.Single(world.Effects.Projectiles).Weapon);
    }
}

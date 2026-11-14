using SourceSharp.Abi;

using TF2.Shared;
using TF2.Shared.Conditions;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The decapitating melee's reach (<c>CTFSword::GetSwingRange</c>,
/// tf_weapon_sword.cpp:276-290): 72 plain, 128 under the shield charge, the
/// bat's plain 48 untouched.
/// </summary>
public sealed class TFSwordHookTests
{
    private const int Attack = (int)InputButtons.Attack;

    private static int NoCrit => TFWeaponWorld.MeleeSeed(1500, 10000);

    [Fact]
    public void TheEyelanderReachesSeventyTwo()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_sword", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);
        world.Run(w, vm, 0, now: 10.215f);

        Assert.Equal(TFSwordHook.SwordRange, Assert.Single(world.Effects.Swings).Trace.Range, 4);
    }

    [Fact]
    public void AShieldChargeReachesOneHundredTwentyEight()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_sword", clip: -1, reserve: 0);
        world.Owners.Of(TFWeaponWorld.Owner).Add(TFCond.ShieldCharge);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);
        world.Run(w, vm, 0, now: 10.215f);

        Assert.Equal(TFSwordHook.ChargedRange, Assert.Single(world.Effects.Swings).Trace.Range, 4);
    }

    [Fact]
    public void APlainMeleeKeepsItsReach()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_bat", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);
        world.Run(w, vm, 0, now: 10.215f);

        Assert.Equal(TFWeaponTypes.SwingRange, Assert.Single(world.Effects.Swings).Trace.Range, 4);
    }
}

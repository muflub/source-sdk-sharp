using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Conditions;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The attack the no-attack window refuses: <c>CTFPlayer::CanAttack</c>'s
/// stealth arms (tf_player_shared.cpp:12226) at the behaviours' fire points —
/// the window's stamp is the spy lane's (<c>TFNoAttackWindowTests</c>).
/// </summary>
public sealed class TFNoAttackRefusalTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const string Scattergun = "tf_weapon_scattergun";
    private const string Bat = "tf_weapon_bat";
    private const int Owner = TFWeaponWorld.Owner;

    [Fact]
    public void An_attack_inside_the_window_fires_nothing()
    {
        var world = new TFWeaponWorld();
        world.Owners.Expire = 12f;
        var (w, vm) = world.Armed(Scattergun, clip: 6, reserve: 32);

        world.Run(w, vm, Attack, now: 10f);

        Assert.Empty(world.Arms.Bullets);
    }

    [Fact]
    public void An_attack_after_the_window_fires()
    {
        var world = new TFWeaponWorld();
        world.Owners.Expire = 9f;
        var (w, vm) = world.Armed(Scattergun, clip: 6, reserve: 32);

        world.Run(w, vm, Attack, now: 10f, seed: TFWeaponWorld.GunSeed(1500, 10000));

        Assert.Single(world.Arms.Bullets);
    }

    [Fact]
    public void A_user_buff_stealth_swings_inside_the_window()
    {
        var world = new TFWeaponWorld();
        world.Owners.Expire = 12f;
        world.Owners.Of(Owner).Add(TFCond.StealthedUserBuff);
        var (w, vm) = world.Armed(Scattergun, clip: 6, reserve: 32);

        // tf_player_shared.cpp:12226's first arm: the long cloak's user buff
        // is no refusal while it holds.
        world.Run(w, vm, Attack, now: 10f, seed: TFWeaponWorld.GunSeed(1500, 10000));

        Assert.Single(world.Arms.Bullets);
    }

    [Fact]
    public void A_cloaked_spy_swings_nothing()
    {
        var world = new TFWeaponWorld();
        world.Owners.Of(Owner).Add(TFCond.Stealthed);
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);

        // The second arm, no window needed: cloaked, the melee's swing and
        // its smack never leave.
        world.Run(w, vm, Attack, now: 10f);
        world.Run(w, vm, 0, now: 10.3f);

        Assert.Empty(world.Effects.Swings);
    }
}

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The Widowmaker (527): the shotgun whose clip is none, whose food is the
/// engineer's metal, thirty a shot (<c>tf_weaponbase_gun.cpp:123-131</c>'s
/// refusal, :376-412's spend over <c>GetAmmoPerShot</c>), the clip override
/// (<c>basecombatweapon_shared.cpp:364-374</c>) and the pool override
/// (<c>:269-276</c>) that point the primary at the metal.
/// </summary>
public sealed class TFWidowmakerTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const int Owner = TFWeaponWorld.Owner;

    private const string Shotgun = "tf_weapon_shotgun_primary";

    /// <summary>A seed whose gun roll draws no crit.</summary>
    private static int NoCrit => TFWeaponWorld.GunSeed(200, 10000);

    /// <summary>The item's arms: the clip none, the metal the pool, thirty the shot.</summary>
    internal static IAttributeProvider Widowed(params (string Hook, float Value)[] extra)
    {
        var item = new AttributeContainer(null, AttributeProviderType.Generic);
        int def = 5270;
        void Set(string hook, float value) => item.Set(AttributeValue.OfFloat(
            new AttributeDefinition(def++, "attr " + def, hook, AttributeDescriptionFormat.Additive,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        Set("mod_max_primary_clip_override", -1f);
        Set("mod_use_metal_ammo_type", 1f);
        Set("mod_ammo_per_shot", 30f);
        foreach (var (hook, value) in extra)
            Set(hook, value);
        return item;
    }

    [Fact]
    public void ThePressPaysThirtyMetalForTheShot()
    {
        var world = new TFWeaponWorld();
        world.Owners.Items[Owner] = Widowed();
        var (w, vm) = world.Armed(Shotgun, clip: 0, reserve: 45);

        world.Run(w, vm, Attack, 10f, NoCrit);

        // One volley of the script's ten, the metal the price took it to
        // (RemoveAmmo(GetAmmoPerShot()), tf_weaponbase_gun.cpp:387), the clip
        // none so the fund alone moves, the next shot the fire delay off.
        var bullet = Assert.Single(world.Arms.Bullets);
        Assert.Equal(10, bullet.Pellets);
        Assert.Equal(15, w.Reserve);
        Assert.Equal(0, w.Clip);
        Assert.Equal(10.625f, w.NextPrimaryAttack, 3);
    }

    [Fact]
    public void AFundUnderThePriceClicksAndFiresNothing()
    {
        var world = new TFWeaponWorld();
        world.Owners.Items[Owner] = Widowed();
        var (w, vm) = world.Armed(Shotgun, clip: 0, reserve: 20);

        world.Run(w, vm, Attack, 10f, NoCrit);

        // GetAmmoPerShot() over the count is stock's metal refusal
        // (:123-131): the empty sound at the fire rate, no round, no bullet,
        // the fund standing.
        Assert.Empty(world.Arms.Bullets);
        Assert.Single(world.Arms.Sounds, "Weapon_Shotgun.Empty");
        Assert.Equal(20, w.Reserve);
        Assert.Equal(10.625f, w.NextPrimaryAttack, 3);
    }

    [Fact]
    public void ThePlainShotgunStillFiresFromItsClip()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Shotgun, clip: 6, reserve: 45);

        world.Run(w, vm, Attack, 10f, NoCrit);

        // No item, no arms: the clip pays and the pool stands, the negative
        // control that the item's attributes made the difference above.
        Assert.Single(world.Arms.Bullets);
        Assert.Equal(5, w.Clip);
        Assert.Equal(45, w.Reserve);
    }

    [Fact]
    public void TheItemFeedsTheGunFromTheMetal()
    {
        var world = new TFWeaponWorld();
        var behaviour = world.Behaviour(Shotgun);
        var row = world.Row(Shotgun);

        // No item: the script's own answers — one round a shot, no pool named.
        Assert.Equal(1, behaviour.PrimaryAmmoPerShot(in row, Owner, 1));
        Assert.Null(behaviour.PrimaryAmmoPool(in row, Owner));

        world.Owners.Items[Owner] = Widowed(("mult_clipsize", 2f));

        // The item's price over the script's one, and the pool the spawn arm
        // points m_iPrimaryAmmoType at (basecombatweapon_shared.cpp:269-276).
        // The clip override stands above the multiplier, the minus one
        // keeping the gun clipless whatever the scale asks
        // (tf_weaponbase.cpp:571-575 reads the base's answer).
        Assert.Equal(30, behaviour.PrimaryAmmoPerShot(in row, Owner, 1));
        Assert.Equal("TF_AMMO_METAL", behaviour.PrimaryAmmoPool(in row, Owner));
        Assert.Equal(-1, behaviour.ModifyClipSize(in row, 6, Owner));
    }
}

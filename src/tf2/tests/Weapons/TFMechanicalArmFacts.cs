using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The Short Circuit's trigger (<c>CTFMechanicalArm::SecondaryAttack</c>,
/// <c>tf_weapon_mechanical_arm.cpp:222-292</c>): the orb ask behind the
/// engineer's secondary, the metal purse and the eyes under water its gates.
/// </summary>
public sealed class TFMechanicalArmFacts
{
    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;
    private const int Owner = TFWeaponWorld.Owner;
    private const string ShortCircuit = "tf_weapon_mechanical_arm";

    private static TFWeaponWorld World() => new(extras: true);

    /// <summary>Item 528's arms: the metal the pool, the clip none, five the shot
    /// (<c>mod_ammo_per_shot</c>, the shipped schema's value).</summary>
    private static IAttributeProvider Circuited()
    {
        var item = new AttributeContainer(null, AttributeProviderType.Generic);
        int def = 5280;
        void Set(string hook, float value) => item.Set(AttributeValue.OfFloat(
            new AttributeDefinition(def++, "attr " + def, hook, AttributeDescriptionFormat.Additive,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        Set("mod_max_primary_clip_override", -1f);
        Set("mod_use_metal_ammo_type", 1f);
        Set("mod_ammo_per_shot", 5f);
        return item;
    }

    [Fact]
    public void The_primary_charges_the_item_s_five_a_shot()
    {
        var world = World();
        world.Owners.Items[Owner] = Circuited();
        var (w, vm) = world.Armed(ShortCircuit, clip: -1, reserve: 100);

        world.Run(w, vm, Attack, 10f);

        // The primary's purse is the item's mod_ammo_per_shot
        // (CTFMechanicalArm::PrimaryAttack's CALL_ATTRIB_HOOK_INT over
        // RemoveAmmo, tf_weapon_mechanical_arm.cpp:467-469, the price
        // GetAmmoPerShot's chain reads, tf_weaponbase_gun.cpp:458-471): one
        // press, five metal, the orb's ask none of it.
        Assert.Equal(100 - 5, w.Reserve);
    }

    [Fact]
    public void The_short_circuit_row_is_the_projectiles()
    {
        var world = World();
        Assert.Equal("tf2.projectile", world.Row(ShortCircuit).Behaviour);
    }

    [Fact]
    public void The_trigger_asks_the_shock_attack_and_pays_the_metal()
    {
        var world = World();
        var (w, vm) = world.Armed(ShortCircuit, clip: -1, reserve: 100);

        world.Run(w, vm, Attack2, 10f);

        // The ask, once, and the purse under the price
        // (<c>tf_mecharm_orb_cost</c>, tf_weapon_mechanical_arm.cpp:48, spent
        // at :129): sixty-five metal for the orb.
        Assert.Equal(ShortCircuit, Assert.Single(world.Effects.ShockAttacks));
        Assert.Equal(100 - TFProjectileBehaviour.MechanicalArmCost, w.Reserve);
    }

    [Fact]
    public void A_purse_under_the_price_is_the_empty_and_nothing_else()
    {
        var world = World();
        var (w, vm) = world.Armed(ShortCircuit, clip: -1, reserve: TFProjectileBehaviour.MechanicalArmCost - 1);

        world.Run(w, vm, Attack2, 10f);

        // Stock's own else (:289-292): the empty sound and no orb, no refire
        // booked, no metal moved.
        Assert.Empty(world.Effects.ShockAttacks);
        Assert.Equal(TFProjectileBehaviour.MechanicalArmCost - 1, w.Reserve);
        Assert.Equal(0f, w.NextSecondaryAttack, 4);
    }

    [Fact]
    public void Eyes_under_water_refuse_with_the_empty_and_the_long_refire()
    {
        var world = World();
        var (w, vm) = world.Armed(ShortCircuit, clip: -1, reserve: 100);
        world.Owners.Underwater = true;

        world.Run(w, vm, Attack2, 10f);

        // The water arm before every other (:232-241): the empty and the
        // 0.67 s refire, the purse untouched.
        Assert.Empty(world.Effects.ShockAttacks);
        Assert.Equal(100, w.Reserve);
        Assert.Equal(10f + TFProjectileBehaviour.MechanicalArmEmptyRefire, w.NextSecondaryAttack, 4);
    }

    [Fact]
    public void A_held_alt_waits_out_the_shot_s_refire()
    {
        var world = World();
        var (w, vm) = world.Armed(ShortCircuit, clip: -1, reserve: 300);

        world.Run(w, vm, Attack2, 10f);
        world.Run(w, vm, Attack2, 10.2f);

        // The shot's own tail (:305-306) books both attacks the 0.67 out, so
        // a trigger held down is one orb per refire and not one a tick: the
        // second press, 0.2 s in, is the empty and nothing else.
        Assert.Single(world.Effects.ShockAttacks);
        Assert.Equal(10f + TFProjectileBehaviour.MechanicalArmEmptyRefire, w.NextSecondaryAttack, 4);
        Assert.Equal(10f + TFProjectileBehaviour.MechanicalArmEmptyRefire, w.NextPrimaryAttack, 4);
        Assert.Equal(300 - TFProjectileBehaviour.MechanicalArmCost, w.Reserve);
    }
}

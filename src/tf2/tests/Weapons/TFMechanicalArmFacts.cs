using SourceSharp.Abi;

using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The Short Circuit's trigger (<c>CTFMechanicalArm::SecondaryAttack</c>,
/// <c>tf_weapon_mechanical_arm.cpp:222-292</c>): the orb ask behind the
/// engineer's secondary, the metal purse and the eyes under water its gates.
/// </summary>
public sealed class TFMechanicalArmFacts
{
    private const int Attack2 = (int)InputButtons.Attack2;
    private const string ShortCircuit = "tf_weapon_mechanical_arm";

    private static TFWeaponWorld World() => new(extras: true);

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
}

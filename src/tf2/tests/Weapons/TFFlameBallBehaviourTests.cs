using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The Dragon's Fury's shell (<c>CTFWeaponFlameBall</c>,
/// <c>tf_weapon_dragons_fury.cpp</c>): the charge meter in front of every
/// attack, the one ball off the reserve, and the flamethrower's blast behind
/// the same full-pressure gate with the penalty left on the refill.
/// </summary>
public sealed class TFFlameBallBehaviourTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;
    private static int NoCrit => TFWeaponWorld.GunSeed(200, 10000);

    private static int _nextDef = 5700;

    /// <summary>An item holding one attribute under the named hook class.</summary>
    private static IAttributeProvider ItemWith(string hook, AttributeDescriptionFormat format, float value)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(_nextDef++, "attr " + _nextDef, hook, format,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }

    /// <summary>
    /// <c>HasFullCharge</c> ahead of everything (<c>:91</c> before <c>:94</c>):
    /// an empty bar refuses the ball with the reserve still full.
    /// </summary>
    [Fact]
    public void An_uncharged_fury_refuses_the_ball()
    {
        var world = new TFWeaponWorld(true);
        var (w, vm) = world.Armed("tf_weapon_rocketlauncher_fireball", clip: -1, reserve: 20);

        world.Run(w, vm, Attack, 10f, NoCrit);

        Assert.Empty(world.Effects.Projectiles);
        Assert.Equal(20, w.Reserve);
    }

    /// <summary>
    /// The charged press (:91-126): one ball asked of the server, the round
    /// off the reserve (the clipless <c>RemoveProjectileAmmo</c>), and the bar
    /// back to zero with the shot (<c>:125-126</c>).
    /// </summary>
    [Fact]
    public void A_charged_press_launches_the_ball_and_spends_the_charge()
    {
        var world = new TFWeaponWorld(true);
        var (w, vm) = world.Armed("tf_weapon_rocketlauncher_fireball", clip: -1, reserve: 20);
        w.ItemMeter = 1f;

        world.Run(w, vm, Attack, 10f, NoCrit);

        var launch = Assert.Single(world.Effects.Projectiles);
        Assert.Equal(("tf_weapon_rocketlauncher_fireball", new ShotDamage(25f), 0f), launch);
        Assert.Equal(19, w.Reserve);
        Assert.Equal(0f, w.ItemMeter);
    }

    /// <summary>
    /// The charged blast (:196-234): the flame's twenty rounds, its own
    /// refires (:1044-1045), and behind the success the penalty on the refill
    /// and the bar back to zero (:228-229).
    /// </summary>
    [Fact]
    public void A_charged_blast_spends_its_rounds_and_puts_the_penalty_on_the_refill()
    {
        var world = new TFWeaponWorld(true);
        var (w, vm) = world.Armed("tf_weapon_rocketlauncher_fireball", clip: -1, reserve: 200);
        w.ItemMeter = 1f;

        world.Run(w, vm, Attack2, 10f, NoCrit);

        Assert.Equal(["tf_weapon_rocketlauncher_fireball"], world.Effects.AirBlasts);
        Assert.Equal(180, w.Reserve);
        Assert.Equal(10.75f, w.NextSecondaryAttack, 4);
        Assert.Equal(11.0f, w.NextPrimaryAttack, 4);
        Assert.Equal(TFItemMeter.RechargePenalty, w.FlameBallChargeScale);
        Assert.Equal(0f, w.ItemMeter);
    }

    /// <summary>
    /// The full-pressure gate first (:199): an empty bar refuses the blast,
    /// the rounds and all.
    /// </summary>
    [Fact]
    public void An_uncharged_blast_is_refused()
    {
        var world = new TFWeaponWorld(true);
        var (w, vm) = world.Armed("tf_weapon_rocketlauncher_fireball", clip: -1, reserve: 200);

        world.Run(w, vm, Attack2, 10f, NoCrit);

        Assert.Empty(world.Effects.AirBlasts);
        Assert.Equal(200, w.Reserve);
    }

    /// <summary>
    /// <c>mult_airblast_cost</c> (:212-214): the burst ammo walks the item's
    /// percentage, so the Backburner's 2.0 over the convar's 20 is 40 — both
    /// the check and the spend see that one number (:217-218).
    /// </summary>
    [Fact]
    public void The_blast_costs_the_scaled_rounds()
    {
        var world = new TFWeaponWorld(true);
        world.Owners.Items[TFWeaponWorld.Owner] =
            ItemWith("mult_airblast_cost", AttributeDescriptionFormat.Percentage, 2.0f);
        var (w, vm) = world.Armed("tf_weapon_rocketlauncher_fireball", clip: -1, reserve: 200);
        w.ItemMeter = 1f;

        world.Run(w, vm, Attack2, 10f, NoCrit);

        Assert.Equal(160, w.Reserve);
    }
}

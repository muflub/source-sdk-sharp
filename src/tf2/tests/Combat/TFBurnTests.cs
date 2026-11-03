using SourceSharp.Abi;

using TF2.Server.Combat;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary>
/// <see cref="TFBurn"/>: <c>CTFPlayerShared::Burn</c> and the burning arm of
/// <c>ConditionGameRulesThink</c> at stock's constants: <c>TF_BURNING_FREQUENCY</c>
/// 0.5 s, <c>TF_BURNING_DMG</c> 4, <c>tf_afterburn_max_duration</c> 10 s, the
/// flamethrower's initial 3 s and 0.4 s on hit.
/// </summary>
public sealed class TFBurnTests
{
    private static readonly EntityHandle Pyro = new(0x0001_0002u);
    private const float Tick = 1f / 66f;

    /// <summary>Thinks every server tick from <paramref name="start"/> until the fire is out; the ticks' times and damages.</summary>
    private static (List<(float Time, float Damage)> Ticks, float OutAt) Run(TFBurn burn, TFCondMask conds, float start,
                                                                            TFClass victim = TFClass.Soldier, int water = 0,
                                                                            AttributeHooks? hooks = null,
                                                                            IAttributeProvider? burnWeapon = null)
    {
        var ticks = new List<(float, float)>();
        for (int i = 1; i < 66 * 30; i++)
        {
            float now = start + i * Tick;
            var think = burn.Think(now, Tick, victim, conds, water, hooks, burnWeapon);
            if (think.Extinguish)
                return (ticks, now);
            if (think.TickDamage > 0f)
                ticks.Add((now, think.TickDamage));
        }
        throw new InvalidOperationException("the fire never went out");
    }

    [Fact]
    public void A_first_ignition_adds_burning_and_the_healing_debuff_and_the_flamethrowers_duration()
    {
        var burn = new TFBurn();
        var conds = burn.Ignite(10f, alive: true, TFClass.Soldier, default, Pyro, TFWeaponId.Flamethrower, "tf_weapon_flamethrower");

        Assert.True(conds.Has(TFCond.Burning) && conds.Has(TFCond.HealingDebuff));
        Assert.Equal(3.4f, burn.AfterburnDuration, 4);   // 3 initial + 0.4 on hit
        Assert.Equal(10.5f, burn.FlameBurnTime, 4);
    }

    [Fact]
    public void Afterburn_ticks_four_every_half_second_until_its_duration_is_spent()
    {
        var burn = new TFBurn();
        var conds = burn.Ignite(0f, true, TFClass.Soldier, default, Pyro, TFWeaponId.Flamethrower, "tf_weapon_flamethrower",
                                burningTime: TFBurn.MaxDuration);

        var (ticks, outAt) = Run(burn, conds, 0f);

        // Ten seconds of fire, a tick each half second: 20 ticks of TF_BURNING_DMG.
        Assert.Equal(20, ticks.Count);
        Assert.All(ticks, t => Assert.Equal(TFCombatDefs.BurningDamage, t.Damage));
        Assert.InRange(ticks[0].Time, 0.5f, 0.5f + Tick);
        for (int i = 1; i < ticks.Count; i++)
            Assert.InRange(ticks[i].Time - ticks[i - 1].Time, 0.5f - 0.001f, 0.5f + Tick + 0.001f);
        Assert.InRange(outAt, 10f, 10.5f + Tick);
    }

    [Fact]
    public void Each_hit_feeds_the_fire_and_the_fire_never_exceeds_ten_seconds()
    {
        var burn = new TFBurn();
        var conds = burn.Ignite(0f, true, TFClass.Soldier, default, Pyro, TFWeaponId.Flamethrower, "f");
        for (int i = 0; i < 40; i++)
            conds = burn.Ignite(0f, true, TFClass.Soldier, conds, Pyro, TFWeaponId.Flamethrower, "f");

        Assert.Equal(TFBurn.MaxDuration, burn.AfterburnDuration);
    }

    [Fact]
    public void Water_to_the_waist_puts_the_fire_out()
    {
        var burn = new TFBurn();
        var conds = burn.Ignite(0f, true, TFClass.Soldier, default, Pyro, TFWeaponId.Flamethrower, "f");

        Assert.False(burn.Think(Tick, Tick, TFClass.Soldier, conds, waterLevel: 1).Extinguish);
        Assert.True(burn.Think(2 * Tick, Tick, TFClass.Soldier, conds, waterLevel: TFBurn.WaterLevelWaist).Extinguish);
    }

    [Fact]
    public void Healers_wear_the_burn_off_three_times_as_fast()
    {
        var burn = new TFBurn();
        var conds = burn.Ignite(0f, true, TFClass.Soldier, default, Pyro, TFWeaponId.Flamethrower, "f", burningTime: 10f);
        conds.Set(TFCond.HealthBuff);

        var (ticks, _) = Run(burn, conds, 0f);

        // 10 s run down at 2 x frametime as well as 0.5 a tick: out after about
        // 10 / 3 s, six ticks, not twenty.
        Assert.InRange(ticks.Count, 6, 7);
    }

    [Fact]
    public void A_pyro_burns_briefly_and_takes_no_afterburn()
    {
        var burn = new TFBurn();
        var conds = burn.Ignite(0f, true, TFClass.Pyro, default, Pyro, TFWeaponId.Flamethrower, "f");
        Assert.Equal(TFCombatDefs.BurningFlameLifePyro, burn.AfterburnDuration);

        var (ticks, _) = Run(burn, conds, 0f, victim: TFClass.Pyro);
        Assert.Empty(ticks);
    }

    [Fact]
    public void The_dead_and_the_phased_are_not_set_alight()
    {
        var burn = new TFBurn();
        Assert.False(burn.Ignite(0f, alive: false, TFClass.Scout, default, Pyro, TFWeaponId.Flamethrower, "f").Has(TFCond.Burning));
        Assert.False(burn.Ignite(0f, true, TFClass.Scout, TFCondMask.Of(TFCond.Phase), Pyro, TFWeaponId.Flamethrower, "f").Has(TFCond.Burning));
    }

    [Fact]
    public void A_flare_burns_as_the_flares_kind()
    {
        var burn = new TFBurn();
        var conds = burn.Ignite(0f, true, TFClass.Scout, default, Pyro, TFWeaponId.Flaregun, "tf_weapon_flaregun");
        Assert.Equal(7.5f, burn.AfterburnDuration, 4);
        var think = burn.Think(0.5f, 0.5f, TFClass.Scout, conds, 0);
        Assert.Equal(TFDamageCustom.BurningFlare, think.Custom);
    }

    // ---- the item-attribute arms (tf_player_shared.cpp:6669-6733, 2898) ----

    private static AttributeContainer Item(int defIndex, string hook, AttributeDescriptionFormat format, float value)
    {
        var container = new AttributeContainer();
        container.Set(AttributeValue.OfFloat(new AttributeDefinition(defIndex, "attr " + defIndex, hook, format,
                                                     AttributeEffectType.Positive,
                                                     false, false, AttributeValueType.Default, MergedKeys.Block("x")),
                                             value));
        return container;
    }

    [Fact]
    public void An_afterburn_immune_kit_burns_the_pyrOs_life()
    {
        // The immune arm (tf_player_shared.cpp:6672-6682) ORs each source's
        // afterburn_immunity into the pyro's own, and the immune life is
        // TF_BURNING_FLAME_LIFE_PYRO either way (:6724-6726).
        var source = Item(7001, TFBurn.ImmunityAttrib, AttributeDescriptionFormat.Additive, 1f);
        var burn = new TFBurn();
        burn.Ignite(0f, true, TFClass.Scout, default, Pyro, TFWeaponId.Flamethrower, "f",
                    hooks: new AttributeHooks(), immunitySources: [source]);

        Assert.Equal(TFCombatDefs.BurningFlameLifePyro, burn.AfterburnDuration, 4);
    }

    [Fact]
    public void The_burn_weapons_burntime_scales_the_flame_life()
    {
        // mult_wpn_burntime on the burn weapon (tf_player_shared.cpp:6733),
        // on the flame life — the on-hit 0.4, doubled; the initial 3 is the
        // duration the first ignition starts from, not the flame life.
        var weapon = Item(7002, TFBurn.BurnTimeAttrib, AttributeDescriptionFormat.Percentage, 2f);
        var burn = new TFBurn();
        burn.Ignite(0f, true, TFClass.Scout, default, Pyro, TFWeaponId.Flamethrower, "f",
                    hooks: new AttributeHooks(), burnWeapon: weapon);

        Assert.Equal(3.8f, burn.AfterburnDuration, 4);
    }

    [Fact]
    public void The_burn_weapons_burndmg_scales_each_tick()
    {
        // mult_wpn_burndmg on the burn weapon (tf_player_shared.cpp:2898),
        // before the degree multipliers (both 1 here).
        var weapon = Item(7003, TFBurn.BurnDamageAttrib, AttributeDescriptionFormat.Percentage, 2f);
        var burn = new TFBurn();
        var conds = burn.Ignite(0f, true, TFClass.Soldier, default, Pyro, TFWeaponId.Flamethrower, "f",
                                burningTime: TFBurn.MaxDuration, hooks: new AttributeHooks(), burnWeapon: weapon);

        var (ticks, _) = Run(burn, conds, 0f, hooks: new AttributeHooks(), burnWeapon: weapon);

        Assert.Equal(20, ticks.Count);
        Assert.All(ticks, t => Assert.Equal(2 * TFCombatDefs.BurningDamage, t.Damage, 4));
    }
}

using SourceSharp.Abi;

using TF2.Server.Combat;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary>
/// <see cref="TFDamageModifyRules"/>: stock's <c>ApplyOnDamageModifyRules</c>
/// arithmetic (<c>tf_gamerules.cpp:5914-7103</c>) at the constants it names:
/// the band centre <c>RemapValClamped(d / 512, 0, 2, 1, 0)</c> ± 0.10, the
/// variance <c>SimpleSplineRemapValClamped(v, 0, 1, -R, R)</c> with
/// <c>R = damage × tf_damage_range (0.5)</c>, <c>TF_DAMAGE_CRIT_MULTIPLIER</c> 3
/// and <c>TF_DAMAGE_MINICRIT_MULTIPLIER</c> 1.35. Every expected number is
/// worked by hand from those constants in the fact's comment.
/// </summary>
public sealed class TFDamageModifyRulesTests
{
    private const DamageType Ranged = DamageType.Bullet | TFDamageBits.UseDistanceMod;
    private const DamageType AmmoRow = DamageType.Bullet | TFDamageBits.UseDistanceMod | TFDamageBits.NoCloseDistanceMod;

    private static readonly TFDamageSettings Stock = TFDamageSettings.Defaults;

    private static float NoRandom(float min, float max) => throw new InvalidOperationException("the spread is disabled: nothing may roll");

    private static TFHit Hit(float damage = 10f, DamageType bits = Ranged, float? distance = 0f,
                             TFWeaponId weapon = TFWeaponId.Pistol, bool self = false,
                             TFCondMask victim = default, TFCondMask attacker = default,
                             TFDamageCustom custom = TFDamageCustom.None, IAttributeProvider? item = null,
                             bool sameClass = false, int victimWaterLevel = 0, bool deflectedRocket = false,
                             Vector? victimEye = null, Vector? toVictim = null,
                             bool attackerAirborne = false, bool victimAirborne = false,
                             bool directRocketHit = false) =>
        new(damage, bits, custom, self, AttackerIsPlayer: true, weapon, distance, victim, attacker, item, sameClass,
            victimWaterLevel, deflectedRocket, victimEye, toVictim, attackerAirborne, victimAirborne,
            directRocketHit);

    private static float Damage(in TFHit hit, TFDamageSettings? settings = null) =>
        TFDamageModifyRules.Apply(in hit, settings ?? Stock, NoRandom).Damage;

    // ---- the range curve (tf_damage_disablespread 1, its default) -------------

    [Fact]
    public void Point_blank_a_ranged_hit_ramps_up_to_one_and_a_half()
        // MAX(1, d): c = 1023/1024, v = c - 0.1 + 0.1, spline 0.999997 -> +R = +5.
        => Assert.Equal(15f, Damage(Hit(distance: 0f)), 4);

    [Fact]
    public void At_the_optimal_512_units_a_ranged_hit_is_its_own_damage()
        // c = 0.5, v = 0.5, spline 0.5 -> 0.
        => Assert.Equal(10f, Damage(Hit(distance: 512f)), 4);

    [Fact]
    public void At_256_units_the_ramp_is_the_spline_of_three_quarters()
        // c = 0.75: spline 3(.75)^2 - 2(.75)^3 = 0.84375 -> -5 + 10 * 0.84375 = +3.4375.
        => Assert.Equal(13.4375f, Damage(Hit(distance: 256f)), 4);

    [Fact]
    public void At_768_units_the_falloff_is_the_spline_of_a_quarter()
        // c = 0.25: spline 0.15625 -> -5 + 1.5625 = -3.4375.
        => Assert.Equal(6.5625f, Damage(Hit(distance: 768f)), 4);

    [Theory]
    [InlineData(1024f)]
    [InlineData(2000f)]
    public void Far_away_without_spread_the_floor_is_the_band_floor_plus_the_spread_not_half(float distance)
        // c = 0, band [0, 0.1], v = 0 + 0.1: spline 0.028 -> -5 + 0.28 = -4.72. Stock's
        // disabled spread reads min + spread, so the far floor is x0.528, not x0.5.
        => Assert.Equal(5.28f, Damage(Hit(distance: distance)), 4);

    [Fact]
    public void No_close_distance_mod_damps_the_point_blank_bonus_to_the_spline_of_0_65()
        // MAX(1, d) = 1: c = 1023/1024, RemapVal(c, .5, 1, .5, .65) = 0.649707, v = c':
        // spline 0.717849 -> -5 + 7.17849 = +2.1785 (0.65 itself would be +2.1825).
        => Assert.Equal(12.1785f, Damage(Hit(bits: AmmoRow, distance: 0f)), 4);

    [Fact]
    public void A_hit_without_the_distance_mod_sits_at_the_middle_of_the_band()
        => Assert.Equal(10f, Damage(Hit(bits: DamageType.Bullet, distance: 0f)), 4);

    [Fact]
    public void The_sniper_rifles_optimum_is_two_and_a_half_times_further()
    {
        // At 1280 units: c = 1 - 1280 / (2 * 1280) = 0.5 for the rifle, 0 for a pistol.
        Assert.Equal(10f, Damage(Hit(distance: 1280f, weapon: TFWeaponId.Sniperrifle)), 4);
        Assert.Equal(5.28f, Damage(Hit(distance: 1280f, weapon: TFWeaponId.Pistol)), 4);
    }

    [Fact]
    public void With_the_spread_on_the_value_is_a_roll_in_the_band_around_the_centre()
    {
        var asked = new List<(float, float)>();
        float Roll(float min, float max) { asked.Add((min, max)); return min; }

        var hit = Hit(distance: 0f);
        float damage = TFDamageModifyRules.Apply(in hit, Stock with { DisableSpread = false }, Roll).Damage;

        // MAX(1, d) = 1: c = 1023/1024, band [c - 0.1, MIN(1, c + 0.1)].
        var (lo, hi) = Assert.Single(asked);
        Assert.Equal(1023f / 1024f - 0.1f, lo, 5);
        Assert.Equal(1.0f, hi, 5);
        // v = 0.89902: spline 0.971467 -> -5 + 9.71467 = +4.7147.
        Assert.Equal(14.7147f, damage, 3);
    }

    // ---- weapon arms --------------------------------------------------------

    [Fact]
    public void The_scattergun_gets_half_again_of_its_near_bonus()
        // R = 5 * 1.5 = 7.5 at v = 1.
        => Assert.Equal(17.5f, Damage(Hit(distance: 0f, weapon: TFWeaponId.Scattergun)), 4);

    [Fact]
    public void A_rocket_launcher_gets_half_its_near_bonus_and_its_whole_falloff()
    {
        Assert.Equal(12.5f, Damage(Hit(distance: 0f, weapon: TFWeaponId.Rocketlauncher)), 4);
        Assert.Equal(5.28f, Damage(Hit(distance: 1024f, weapon: TFWeaponId.Rocketlauncher)), 4);
    }

    [Fact]
    public void A_direct_rocket_hit_skips_the_long_range_falloff()
    {
        // The far hit's -4.72 does not land when the hit is a rocket
        // specialist's direct one (tf_gamerules.cpp:6299-6306): the 10 stands.
        Assert.Equal(5.28f, Damage(Hit(distance: 1024f, weapon: TFWeaponId.Rocketlauncher)), 4);
        Assert.Equal(10f, Damage(Hit(distance: 1024f, weapon: TFWeaponId.Rocketlauncher, directRocketHit: true)), 4);
    }

    [Fact]
    public void The_grenade_launchers_range_is_a_fifth()
    {
        // R = 5 * 0.2 = 1: +1 near, -0.944 far.
        Assert.Equal(11f, Damage(Hit(distance: 0f, weapon: TFWeaponId.Pipebomblauncher)), 4);
        Assert.Equal(9.056f, Damage(Hit(distance: 1024f, weapon: TFWeaponId.Grenadelauncher)), 4);
    }

    // ---- crits --------------------------------------------------------------

    [Theory]
    [InlineData(0f)]
    [InlineData(512f)]
    [InlineData(1024f)]
    public void A_crit_is_three_times_the_damage_at_any_range(float distance)
    {
        // A full crit takes neither the near bonus nor the far penalty (:6535-6536).
        var result = TFDamageModifyRules.Apply(Hit(bits: Ranged | TFDamageBits.Critical, distance: distance), Stock, NoRandom);
        Assert.Equal(30f, result.Damage, 4);
        Assert.Equal(TFCritType.Full, result.Crit);
    }

    [Fact]
    public void A_backstab_is_six_times_the_victims_health()
    {
        // The knife's hit as TFSpy.Swing deals it — twice the victim's 100
        // health, crit-flagged, the backstab custom kind (tf_weapon_knife.cpp:348-394)
        // — triples through the modify rules (tf_gamerules.cpp:6479-6487) into
        // stock's 6x backstab; melee carries no distance mod and the disabled
        // spread reads the band's middle, so no variance rides on it.
        var result = TFDamageModifyRules.Apply(
            Hit(damage: 200f, bits: TFDamageBits.Melee | TFDamageBits.Critical, distance: null,
                weapon: TFWeaponId.Knife, custom: TFDamageCustom.Backstab), Stock, NoRandom);

        Assert.Equal(600f, result.Damage, 4);
        Assert.Equal(TFCritType.Full, result.Crit);
        Assert.Equal(TFDamageCustom.Backstab, result.Custom);
    }

    [Fact]
    public void With_crit_falloff_on_a_far_crit_falls_off_first()
        // 5.28 * 3.
        => Assert.Equal(15.84f, Damage(Hit(bits: Ranged | TFDamageBits.Critical, distance: 1024f),
                                       Stock with { CritFalloff = true }), 4);

    [Fact]
    public void A_victims_jarate_mini_crits_with_the_near_bonus_and_no_falloff()
    {
        var jarate = TFCondMask.Of(TFCond.Urine);
        var near = TFDamageModifyRules.Apply(Hit(distance: 0f, victim: jarate), Stock, NoRandom);
        var far = TFDamageModifyRules.Apply(Hit(distance: 1024f, victim: jarate), Stock, NoRandom);

        // 15 * 1.35 and 10 * 1.35: mini-crits keep the ramp-up, never the fall-off.
        Assert.Equal(20.25f, near.Damage, 4);
        Assert.Equal(13.5f, far.Damage, 4);
        Assert.Equal((TFCritType.Mini, TFCritType.Mini), (near.Crit, far.Crit));
        Assert.True((near.Bits & TFDamageBits.Critical) != 0, "a mini-crit carries DMG_CRITICAL on (lambdaDoMinicrit)");
    }

    [Theory]
    [InlineData(TFCond.Offensebuff)]
    [InlineData(TFCond.Nohealingdamagebuff)]
    [InlineData(TFCond.EnergyBuff)]
    public void A_buffed_attacker_mini_crits(TFCond cond)
        => Assert.Equal(13.5f, Damage(Hit(distance: 512f, attacker: TFCondMask.Of(cond))), 4);

    [Fact]
    public void A_deflected_rockets_blast_mini_crits()
    {
        var result = TFDamageModifyRules.Apply(Hit(distance: 512f, weapon: TFWeaponId.Rocketlauncher,
                                                  deflectedRocket: true), Stock, NoRandom);

        // 10 x 1.35: the mini-crit multiplier, and DMG_CRITICAL on for the
        // see-crits (lambdaDoMinicrit).
        Assert.Equal(13.5f, result.Damage, 4);
        Assert.Equal(TFCritType.Mini, result.Crit);
        Assert.True((result.Bits & TFDamageBits.Critical) != 0);
    }

    [Fact]
    public void An_undeflected_rockets_blast_is_plain()
        => Assert.Equal(TFCritType.None, TFDamageModifyRules.Apply(
            Hit(distance: 512f, weapon: TFWeaponId.Rocketlauncher), Stock, NoRandom).Crit);

    [Fact]
    public void A_full_crit_stays_a_full_crit_over_a_deflected_rocket()
    {
        var result = TFDamageModifyRules.Apply(Hit(bits: Ranged | TFDamageBits.Critical, distance: 512f,
                                                   weapon: TFWeaponId.Rocketlauncher, deflectedRocket: true),
                                               Stock, NoRandom);

        // 10 x 3: the crit came first, so the mini-crit arm never runs.
        Assert.Equal(30f, result.Damage, 4);
        Assert.Equal(TFCritType.Full, result.Crit);
    }

    [Fact]
    public void Nobody_mini_crits_or_crits_himself_and_self_damage_takes_no_range()
    {
        var hit = Hit(bits: Ranged | TFDamageBits.Critical, distance: 0f, self: true, victim: TFCondMask.Of(TFCond.Urine));
        var result = TFDamageModifyRules.Apply(in hit, Stock, NoRandom);
        Assert.Equal(10f, result.Damage, 4);
    }

    [Fact]
    public void Falls_and_drowning_take_no_range_and_no_crit()
    {
        Assert.Equal(10f, Damage(Hit(bits: DamageType.Fall | TFDamageBits.UseDistanceMod, distance: 0f)), 4);
        Assert.Equal(10f, Damage(Hit(bits: DamageType.Drown | TFDamageBits.Critical, distance: 0f)), 4);
    }

    // ---- defense buffs ------------------------------------------------------

    [Fact]
    public void The_battalions_backup_takes_the_crit_away_and_35_percent()
    {
        var result = TFDamageModifyRules.Apply(Hit(bits: Ranged | TFDamageBits.Critical, distance: 512f,
                                                   victim: TFCondMask.Of(TFCond.Defensebuff)), Stock, NoRandom);
        Assert.Equal(6.5f, result.Damage, 4);
        Assert.Equal(TFCritType.None, result.Crit);
        Assert.True((result.Bits & TFDamageBits.Critical) == 0);
    }

    [Fact]
    public void Its_high_variant_takes_75_percent_but_leaves_the_crit()
        // 2.5 then x3: the range variance is the dealt damage's (info.GetDamage()), 0 at 512.
        => Assert.Equal(7.5f, Damage(Hit(bits: Ranged | TFDamageBits.Critical, distance: 512f,
                                         victim: TFCondMask.Of(TFCond.DefensebuffHigh))), 4);

    [Fact]
    public void A_backstab_ignores_the_defense_buffs()
        => Assert.Equal(30f, Damage(Hit(bits: Ranged | TFDamageBits.Critical, distance: 512f, custom: TFDamageCustom.Backstab,
                                        victim: TFCondMask.Of(TFCond.Defensebuff))), 4);

    // ---- the weapon's econ crit arms (tf_gamerules.cpp:5958-6042) -----------

    [Fact]
    public void A_crit_vs_condition_arm_crits_the_victim_who_has_the_condition()
    {
        // The mask's bit 0 is Burning (condition_to_attribute_translation[0],
        // tf_shareddefs.cpp:251): a 1 crits a burning victim, full.
        var hooks = new AttributeHooks();
        var hit = Hit(bits: Ranged, victim: TFCondMask.Of(TFCond.Burning), item: Item(7001, "or_crit_vs_playercond", 1f));

        var result = TFDamageModifyRules.Apply(in hit, Stock, NoRandom, hooks);

        Assert.Equal(TFCritType.Full, result.Crit);
        Assert.True((result.Bits & TFDamageBits.Critical) != 0);
        // 10 x 3: the crit multiplier, no mini's 1.35.
        Assert.Equal(30f, result.Damage, 4);
    }

    [Fact]
    public void A_crit_vs_condition_arm_waits_for_the_condition()
    {
        var hooks = new AttributeHooks();
        var hit = Hit(item: Item(7002, "or_crit_vs_playercond", 1f));

        var result = TFDamageModifyRules.Apply(in hit, Stock, NoRandom, hooks);

        Assert.Equal(TFCritType.None, result.Crit);
    }

    [Fact]
    public void A_crit_vs_not_condition_arm_crits_the_victim_who_lacks_it()
    {
        // Bit 2 is Zoomed: the mask 4 crits a victim standing unscoped.
        var hooks = new AttributeHooks();
        var hit = Hit(item: Item(7003, "or_crit_vs_not_playercond", 4f));

        Assert.Equal(TFCritType.Full, TFDamageModifyRules.Apply(in hit, Stock, NoRandom, hooks).Crit);

        var scoped = Hit(item: Item(7003, "or_crit_vs_not_playercond", 4f), victim: TFCondMask.Of(TFCond.Zoomed));
        Assert.Equal(TFCritType.None, TFDamageModifyRules.Apply(in scoped, Stock, NoRandom, hooks).Crit);
    }

    [Fact]
    public void Without_a_weapon_item_the_arms_are_unasked()
    {
        var hooks = new AttributeHooks();
        var hit = Hit(victim: TFCondMask.Of(TFCond.Burning));

        var result = TFDamageModifyRules.Apply(in hit, Stock, NoRandom, hooks);

        Assert.Equal(TFCritType.None, result.Crit);
    }

    // ---- the weapon's promotions and multipliers (:6338-6353, :6394-6432) ----

    [Fact]
    public void A_promoted_mini_crit_is_a_full_one()
    {
        // The victim's jarate mini-crits; minicrits_become_crits promotes it
        // (:6344-6352) and the full multiplier x3 replaces the mini's 1.35.
        var hooks = new AttributeHooks();
        var hit = Hit(victim: TFCondMask.Of(TFCond.Urine), item: Item(7011, "minicrits_become_crits", 1f));

        var result = TFDamageModifyRules.Apply(in hit, Stock, NoRandom, hooks);

        Assert.Equal(TFCritType.Full, result.Crit);
        Assert.Equal(30f, result.Damage, 4);
    }

    [Fact]
    public void The_damage_scales_by_the_players_multiplier_then_the_same_classs()
    {
        // mult_dmg_vs_players x1.5, then mult_dmg_vs_same_class x2 on it
        // (:6394-6411): 10 -> 15 -> 30, the same-class arm after the players'.
        var hooks = new AttributeHooks();
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(7012, "attr 7012", "mult_dmg_vs_players",
                                                                AttributeDescriptionFormat.Percentage,
                                                                AttributeEffectType.Positive, false, false,
                                                                AttributeValueType.Default, MergedKeys.Block("x")), 1.5f));
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(7013, "attr 7013", "mult_dmg_vs_same_class",
                                                                AttributeDescriptionFormat.Percentage,
                                                                AttributeEffectType.Positive, false, false,
                                                                AttributeValueType.Default, MergedKeys.Block("x")), 2f));
        var hit = Hit(distance: 512f, item: item, sameClass: true);

        Assert.Equal(30f, TFDamageModifyRules.Apply(in hit, Stock, NoRandom, hooks).Damage, 4);

        var otherClass = Hit(distance: 512f, item: item);
        Assert.Equal(15f, TFDamageModifyRules.Apply(in otherClass, Stock, NoRandom, hooks).Damage, 4);
    }

    [Fact]
    public void A_nonburning_victims_crit_is_refused_and_the_damage_scaled()
    {
        // set_nocrit_vs_nonburning and mult_dmg_vs_nonburning x1.5
        // (:6414-6431): the crit flag leaves the bits and the crit type, the
        // multiplier stays; the attacker is no crit-boost.
        var hooks = new AttributeHooks();
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(7014, "attr 7014", "set_nocrit_vs_nonburning",
                                                                AttributeDescriptionFormat.Additive,
                                                                AttributeEffectType.Positive, false, false,
                                                                AttributeValueType.Default, MergedKeys.Block("x")), 1f));
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(7015, "attr 7015", "mult_dmg_vs_nonburning",
                                                                AttributeDescriptionFormat.Percentage,
                                                                AttributeEffectType.Positive, false, false,
                                                                AttributeValueType.Default, MergedKeys.Block("x")), 1.5f));
        var hit = Hit(bits: Ranged | TFDamageBits.Critical, distance: 512f, item: item);

        var result = TFDamageModifyRules.Apply(in hit, Stock, NoRandom, hooks);

        Assert.Equal(TFCritType.None, result.Crit);
        Assert.True((result.Bits & TFDamageBits.Critical) == 0);
        Assert.Equal(15f, result.Damage, 4);
    }

    [Fact]
    public void A_burning_victim_keeps_the_crit_and_skips_the_nonburning_multiplier()
    {
        var hooks = new AttributeHooks();
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(7016, "attr 7016", "set_nocrit_vs_nonburning",
                                                                AttributeDescriptionFormat.Additive,
                                                                AttributeEffectType.Positive, false, false,
                                                                AttributeValueType.Default, MergedKeys.Block("x")), 1f));
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(7017, "attr 7017", "mult_dmg_vs_nonburning",
                                                                AttributeDescriptionFormat.Percentage,
                                                                AttributeEffectType.Positive, false, false,
                                                                AttributeValueType.Default, MergedKeys.Block("x")), 1.5f));
        var hit = Hit(bits: Ranged | TFDamageBits.Critical, distance: 512f, item: item,
                      victim: TFCondMask.Of(TFCond.Burning));

        var result = TFDamageModifyRules.Apply(in hit, Stock, NoRandom, hooks);

        Assert.Equal(TFCritType.Full, result.Crit);
        Assert.Equal(30f, result.Damage, 4);
    }

    // ---- the wet and burning arms (tf_gamerules.cpp:5991-6012, :6247-6262) ---

    [Fact]
    public void A_crit_vs_wet_arm_crits_the_jarated_the_milked_and_the_gas()
    {
        // crit_vs_wet_players: any of the three conditions, or water (the
        // level past WL_NOTINWATER). The x3, not the mini's 1.35.
        var hooks = new AttributeHooks();
        var jarate = Hit(victim: TFCondMask.Of(TFCond.Urine), item: Item(7021, "crit_vs_wet_players", 1f));
        var milk = Hit(victim: TFCondMask.Of(TFCond.MadMilk), item: Item(7021, "crit_vs_wet_players", 1f));
        var gas = Hit(victim: TFCondMask.Of(TFCond.Gas), item: Item(7021, "crit_vs_wet_players", 1f));
        var wet = Hit(item: Item(7021, "crit_vs_wet_players", 1f), victimWaterLevel: 1);

        Assert.Equal(TFCritType.Full, TFDamageModifyRules.Apply(in jarate, Stock, NoRandom, hooks).Crit);
        Assert.Equal(TFCritType.Full, TFDamageModifyRules.Apply(in milk, Stock, NoRandom, hooks).Crit);
        Assert.Equal(TFCritType.Full, TFDamageModifyRules.Apply(in gas, Stock, NoRandom, hooks).Crit);
        Assert.Equal(TFCritType.Full, TFDamageModifyRules.Apply(in wet, Stock, NoRandom, hooks).Crit);

        var dry = Hit(item: Item(7021, "crit_vs_wet_players", 1f));
        Assert.Equal(TFCritType.None, TFDamageModifyRules.Apply(in dry, Stock, NoRandom, hooks).Crit);
    }

    [Fact]
    public void A_burning_victim_mini_crits_for_the_weapon_that_asks_not_of_burn_ticks()
    {
        // The chain's final else (:6252-6262): or_minicrit_vs_playercond_
        // burning == 1, the victim burning, the hit no DMG_BURN tick.
        var hooks = new AttributeHooks();
        var hit = Hit(victim: TFCondMask.Of(TFCond.Burning), item: Item(7022, "or_minicrit_vs_playercond_burning", 1f));

        var result = TFDamageModifyRules.Apply(in hit, Stock, NoRandom, hooks);

        Assert.Equal(TFCritType.Mini, result.Crit);
        Assert.Equal(20.25f, result.Damage, 4);
    }

    [Fact]
    public void A_burn_tick_is_no_minicrit_for_the_burning_arm()
    {
        var hooks = new AttributeHooks();
        var burnTick = Hit(bits: DamageType.Burn, distance: null, weapon: TFWeaponId.None,
                           victim: TFCondMask.Of(TFCond.Burning),
                           item: Item(7023, "or_minicrit_vs_playercond_burning", 1f));

        Assert.Equal(TFCritType.None, TFDamageModifyRules.Apply(in burnTick, Stock, NoRandom, hooks).Crit);
    }

    // ---- the axtinguisher's behind test (tf_gamerules.cpp:6044-6070) --------

    [Fact]
    public void An_axtinguisher_full_crits_a_burning_victim_it_stands_behind()
    {
        // The victim faces +X (eye angles 0,0,0) and the attacker is at
        // -100 X: the way to the victim is along its facing, dot 1 > 0 —
        // right behind, the full crit's x3.
        var hooks = new AttributeHooks();
        var hit = Hit(bits: TFDamageBits.Melee, distance: null, weapon: TFWeaponId.Fireaxe,
                      victim: TFCondMask.Of(TFCond.Burning), item: Item(7031, "axtinguisher_properties", 1f),
                      victimEye: new Vector(0f, 0f, 0f), toVictim: new Vector(100f, 0f, 0f));

        var result = TFDamageModifyRules.Apply(in hit, Stock, NoRandom, hooks);

        Assert.Equal(TFCritType.Full, result.Crit);
        Assert.True((result.Bits & TFDamageBits.Critical) != 0);
        Assert.Equal(30f, result.Damage, 4);
    }

    [Fact]
    public void An_axtinguisher_mini_crits_a_burning_victim_it_stands_before()
    {
        // The attacker at +100 X, the victim facing +X: the way to the victim
        // runs against its facing, dot -1 — the mini-crit's 1.35.
        var hooks = new AttributeHooks();
        var hit = Hit(bits: TFDamageBits.Melee, distance: null, weapon: TFWeaponId.Fireaxe,
                      victim: TFCondMask.Of(TFCond.Burning), item: Item(7031, "axtinguisher_properties", 1f),
                      victimEye: new Vector(0f, 0f, 0f), toVictim: new Vector(-100f, 0f, 0f));

        var result = TFDamageModifyRules.Apply(in hit, Stock, NoRandom, hooks);

        Assert.Equal(TFCritType.Mini, result.Crit);
        Assert.True((result.Bits & TFDamageBits.Critical) != 0);
        Assert.Equal(13.5f, result.Damage, 4);
    }

    [Fact]
    public void An_axtinguisher_needs_the_burning_for_either_crit()
    {
        var hooks = new AttributeHooks();
        var hit = Hit(bits: TFDamageBits.Melee, distance: null, weapon: TFWeaponId.Fireaxe,
                      item: Item(7031, "axtinguisher_properties", 1f),
                      victimEye: new Vector(0f, 0f, 0f), toVictim: new Vector(100f, 0f, 0f));

        var result = TFDamageModifyRules.Apply(in hit, Stock, NoRandom, hooks);

        Assert.Equal(TFCritType.None, result.Crit);
        Assert.Equal(10f, result.Damage, 4);
    }

    // ---- the airborne arms (tf_gamerules.cpp:6073-6086, :6264-6278) ----------

    [Fact]
    public void An_attacker_blasted_into_the_air_crits_for_the_weapon_that_asks()
    {
        // crit_while_airborne: full, the x3, over the axtinguisher's pick and
        // under the mini-crit ladder (:6073-6086).
        var hooks = new AttributeHooks();
        var airborne = Hit(item: Item(7051, "crit_while_airborne", 1f), attackerAirborne: true);

        var result = TFDamageModifyRules.Apply(in airborne, Stock, NoRandom, hooks);

        Assert.Equal(TFCritType.Full, result.Crit);
        Assert.True((result.Bits & TFDamageBits.Critical) != 0);
        Assert.Equal(30f, result.Damage, 4);
    }

    [Fact]
    public void A_grounded_attacker_is_no_airborne_crit()
    {
        var hooks = new AttributeHooks();
        var grounded = Hit(item: Item(7051, "crit_while_airborne", 1f));

        Assert.Equal(TFCritType.None, TFDamageModifyRules.Apply(in grounded, Stock, NoRandom, hooks).Crit);
    }

    [Fact]
    public void A_victim_blasted_into_the_air_takes_the_airborne_mini_crit()
    {
        // mini_crit_airborne, the chain's arm after the burning one
        // (:6264-6278): the 1.35, and DMG_CRITICAL with it.
        var hooks = new AttributeHooks();
        var airborne = Hit(item: Item(7052, "mini_crit_airborne", 1f), victimAirborne: true);

        var result = TFDamageModifyRules.Apply(in airborne, Stock, NoRandom, hooks);

        Assert.Equal(TFCritType.Mini, result.Crit);
        Assert.True((result.Bits & TFDamageBits.Critical) != 0);
        Assert.Equal(20.25f, result.Damage, 4);
    }

    [Fact]
    public void A_groundable_victim_is_no_airborne_mini_crit()
    {
        var hooks = new AttributeHooks();
        var grounded = Hit(item: Item(7052, "mini_crit_airborne", 1f));

        Assert.Equal(TFCritType.None, TFDamageModifyRules.Apply(in grounded, Stock, NoRandom, hooks).Crit);
    }

    [Fact]
    public void The_airborne_crit_leaves_a_full_crit_full_past_the_mini_ladder()
    {
        // Both arms on one weapon, both sides up: the attacker's crit lands
        // first (:6073), so the ladder's gate — crit none — never opens and
        // the victim's mini cannot demote it (:6094ff).
        var hooks = new AttributeHooks();
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(7053, "attr 7053", "crit_while_airborne",
                                                                AttributeDescriptionFormat.Or, AttributeEffectType.Positive,
                                                                false, false, AttributeValueType.Default,
                                                                MergedKeys.Block("x")), 1f));
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(7054, "attr 7054", "mini_crit_airborne",
                                                                AttributeDescriptionFormat.Or, AttributeEffectType.Positive,
                                                                false, false, AttributeValueType.Default,
                                                                MergedKeys.Block("x")), 1f));
        var both = Hit(item: item, attackerAirborne: true, victimAirborne: true);

        var result = TFDamageModifyRules.Apply(in both, Stock, NoRandom, hooks);

        Assert.Equal(TFCritType.Full, result.Crit);
        Assert.Equal(30f, result.Damage, 4);
    }

    // ---- the close-range backattack's mini-crit (:6314-6333) ----------------

    [Fact]
    public void A_backattack_mini_crits_a_victim_its_attacker_stands_within_512_of_and_behind()
    {
        // The victim faces +X, the attacker at +100 X behind it, dot 1 over
        // 0.259: the mini-crit's 1.35 on the 512-unit damage.
        var hooks = new AttributeHooks();
        var hit = Hit(distance: 512f, victimEye: new Vector(0f, 0f, 0f), toVictim: new Vector(100f, 0f, 0f),
                      item: Item(7041, "closerange_backattack_minicrits", 1f));

        var result = TFDamageModifyRules.Apply(in hit, Stock, NoRandom, hooks);

        Assert.Equal(TFCritType.Mini, result.Crit);
        Assert.True((result.Bits & TFDamageBits.Critical) != 0);
        Assert.Equal(13.5f, result.Damage, 4);
    }

    [Fact]
    public void A_backattack_before_the_victim_is_no_mini_crit()
    {
        // The attacker at -100 X, the victim facing +X: the way to the victim
        // runs against its facing, dot -1 under 0.259.
        var hooks = new AttributeHooks();
        var hit = Hit(distance: 512f, victimEye: new Vector(0f, 0f, 0f), toVictim: new Vector(-100f, 0f, 0f),
                      item: Item(7041, "closerange_backattack_minicrits", 1f));

        var result = TFDamageModifyRules.Apply(in hit, Stock, NoRandom, hooks);

        Assert.Equal(TFCritType.None, result.Crit);
        Assert.Equal(10f, result.Damage, 4);
    }

    [Fact]
    public void A_backattack_beyond_512_units_is_no_mini_crit()
    {
        // LengthSqr 600^2 over Square( 512 ): the range gates before the dot.
        // No distance mod, so the plain 10 tells the arm from the range curve.
        var hooks = new AttributeHooks();
        var hit = Hit(bits: DamageType.Bullet, distance: 600f, victimEye: new Vector(0f, 0f, 0f),
                      toVictim: new Vector(600f, 0f, 0f),
                      item: Item(7041, "closerange_backattack_minicrits", 1f));

        var result = TFDamageModifyRules.Apply(in hit, Stock, NoRandom, hooks);

        Assert.Equal(TFCritType.None, result.Crit);
        Assert.Equal(10f, result.Damage, 4);
    }

    /// <summary>A weapon item carrying one attribute, the walk's root.</summary>
    private static IAttributeProvider Item(int defindex, string hook, float value)
    {
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(defindex, "attr " + defindex, hook,
                                                                AttributeDescriptionFormat.Or, AttributeEffectType.Positive,
                                                                false, false, AttributeValueType.Default,
                                                                MergedKeys.Block("x")), value));
        return item;
    }
}

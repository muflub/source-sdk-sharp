using TF2.Shared;
using TF2.Shared.Conditions;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// <see cref="TFConditionEffects"/> against the stock rules it cites
/// (<c>tf_gamerules.cpp</c> mini-crits, <c>tf_player.cpp</c> <c>TakeHealth</c>,
/// <c>tf_player_shared.cpp</c> heal and crit-boost tests).
/// </summary>
public sealed class TFConditionsEffectsTests
{
    private readonly TFConditionEffects _effects = new();

    [Fact]
    public void Mini_crit_buffed_attackers_deal_one_point_three_five_times()
    {
        TFCondMask attacker = TFCondMask.Of(TFCond.Offensebuff);

        Assert.Equal(1.35f, _effects.DamageMultiplier(attacker, default));
    }

    [Fact]
    public void Marked_for_death_victims_take_one_point_three_five_times()
    {
        TFCondMask victim = TFCondMask.Of(TFCond.Markedfordeath);

        Assert.Equal(TFDamage.MinicritMultiplier, _effects.DamageMultiplier(default, victim));
    }

    [Fact]
    public void Jarated_victims_take_mini_crits()
    {
        Assert.True(_effects.TakesMiniCrits(TFCondMask.Of(TFCond.Urine)));
    }

    [Fact]
    public void Mad_milk_does_not_mini_crit()
    {
        Assert.False(_effects.TakesMiniCrits(TFCondMask.Of(TFCond.MadMilk)));
    }

    [Fact]
    public void Mini_crits_do_not_stack()
    {
        TFCondMask attacker = TFCondMask.Of(TFCond.Offensebuff);
        TFCondMask victim = TFCondMask.Of([TFCond.Markedfordeath, TFCond.Urine]);

        Assert.Equal(1.35f, _effects.DamageMultiplier(attacker, victim));
    }

    [Fact]
    public void No_condition_leaves_damage_alone()
    {
        TFCondMask attacker = TFCondMask.Of(TFCond.Aiming);
        TFCondMask victim = TFCondMask.Of(TFCond.Burning);

        Assert.Equal(1f, _effects.DamageMultiplier(attacker, victim));
    }

    [Fact]
    public void Burning_does_not_block_healing()
    {
        Assert.False(_effects.HealBlocked(TFCondMask.Of(TFCond.Burning)));
    }

    [Fact]
    public void The_no_healing_buff_blocks_healing()
    {
        Assert.True(_effects.HealBlocked(TFCondMask.Of(TFCond.Nohealingdamagebuff)));
    }

    [Fact]
    public void The_afterburn_debuff_cuts_healers_healing_by_a_fifth()
    {
        Assert.Equal(0.8f, _effects.HealerHealScale(TFCondMask.Of(TFCond.HealingDebuff)));
    }

    [Fact]
    public void Burning_alone_leaves_healers_healing_whole()
    {
        Assert.Equal(1f, _effects.HealerHealScale(TFCondMask.Of(TFCond.Burning)));
    }

    [Fact]
    public void The_crit_boost_crit_boosts()
    {
        Assert.True(_effects.IsCritBoosted(TFCondMask.Of(TFCond.Critboosted), clientSide: false));
    }

    [Fact]
    public void The_demo_charge_crit_boosts_only_on_the_client()
    {
        TFCondMask conds = TFCondMask.Of(TFCond.CritboostedDemoCharge);

        Assert.True(_effects.IsCritBoosted(conds, clientSide: true));
        Assert.False(_effects.IsCritBoosted(conds, clientSide: false));
    }

    [Fact]
    public void The_rage_buff_is_not_an_all_weapon_crit_boost()
    {
        Assert.False(_effects.IsCritBoosted(TFCondMask.Of(TFCond.CritboostedRageBuff), clientSide: false));
    }

    [Fact]
    public void The_uber_fade_is_not_invulnerable()
    {
        Assert.True(_effects.IsInvulnerable(TFCondMask.Of(TFCond.Invulnerable)));
        Assert.False(_effects.IsInvulnerable(TFCondMask.Of(TFCond.InvulnerableWearingoff)));
    }

    [Fact]
    public void Stealth_blink_alone_is_not_stealthed()
    {
        Assert.True(_effects.IsStealthed(TFCondMask.Of(TFCond.Stealthed)));
        Assert.False(_effects.IsStealthed(TFCondMask.Of(TFCond.StealthedBlink)));
    }

    [Fact]
    public void The_expires_fast_set_is_stocks_five_debuffs()
    {
        Assert.Equal(
            TFCondMask.Of(
            [
                TFCond.Burning,
                TFCond.Urine,
                TFCond.Bleeding,
                TFCond.MadMilk,
                TFCond.Gas,
            ]),
            _effects.ExpiresFastUnderHealing);
    }
}

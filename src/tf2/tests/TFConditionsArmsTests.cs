using TF2.Shared;
using TF2.Shared.Conditions;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// <see cref="TFConditionArms"/>: the <c>OnConditionAdded</c>/<c>OnConditionRemoved</c>
/// arms that only remove other conditions (<c>tf_player_shared.cpp</c>
/// <c>OnAddInvulnerable</c>, <c>OnRemoveStealthed</c>, <c>OnRemoveBurning</c>).
/// </summary>
public sealed class TFConditionsArmsTests
{
    private readonly TFConditionArms _arms = new();

    private static TFConditions Server() => new(authoritative: true);

    [Fact]
    public void Uber_puts_out_the_fire_and_washes_off_the_debuffs()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Burning, 5f);
        conditions.Add(TFCond.Urine, 5f);
        conditions.Add(TFCond.Bleeding, 5f);
        conditions.Add(TFCond.MadMilk, 5f);
        conditions.Add(TFCond.Gas, 5f);
        conditions.Add(TFCond.Plague);
        conditions.Add(TFCond.Invulnerable);

        TFCondMask removed = _arms.OnAdded(conditions, TFCond.Invulnerable);

        Assert.Equal(
            TFCondMask.Of([TFCond.Burning, TFCond.Urine, TFCond.Bleeding, TFCond.MadMilk, TFCond.Gas, TFCond.Plague]),
            removed);
        Assert.Equal(TFCondMask.Of(TFCond.Invulnerable), conditions.Bits);
    }

    [Fact]
    public void Uber_removing_the_fire_also_ends_the_afterburn_heal_debuff()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Burning);
        conditions.Add(TFCond.HealingDebuff, 3f);
        conditions.Add(TFCond.InvulnerableUserBuff);

        TFCondMask removed = _arms.OnAdded(conditions, TFCond.InvulnerableUserBuff);

        Assert.Equal(TFCondMask.Of([TFCond.Burning, TFCond.HealingDebuff]), removed);
        Assert.False(conditions.Has(TFCond.HealingDebuff));
    }

    [Fact]
    public void Uber_leaves_other_conditions_alone()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Disguised);
        conditions.Add(TFCond.Invulnerable);

        Assert.True(_arms.OnAdded(conditions, TFCond.Invulnerable).IsEmpty);
        Assert.True(conditions.Has(TFCond.Disguised));
    }

    [Fact]
    public void The_client_leaves_ubers_cleanup_to_the_server()
    {
        var conditions = new TFConditions(authoritative: false);
        conditions.Add(TFCond.Burning);
        conditions.Add(TFCond.Invulnerable);

        Assert.True(_arms.OnAdded(conditions, TFCond.Invulnerable).IsEmpty);
        Assert.True(conditions.Has(TFCond.Burning));
    }

    [Fact]
    public void Leaving_stealth_ends_feign_death_on_both_sides()
    {
        foreach (bool server in new[] { true, false })
        {
            var conditions = new TFConditions(server);
            conditions.Add(TFCond.FeignDeath);

            TFCondMask removed = _arms.OnRemoved(conditions, TFCond.Stealthed);

            Assert.Equal(TFCondMask.Of(TFCond.FeignDeath), removed);
        }
    }

    [Fact]
    public void The_fire_going_out_ends_the_afterburn_heal_debuff()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.HealingDebuff, 3f);

        TFCondMask removed = _arms.OnRemoved(conditions, TFCond.Burning);

        Assert.Equal(TFCondMask.Of(TFCond.HealingDebuff), removed);
    }

    [Fact]
    public void A_condition_with_no_condition_arm_removes_nothing()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Burning);

        Assert.True(_arms.OnAdded(conditions, TFCond.Zoomed).IsEmpty);
        Assert.True(_arms.OnRemoved(conditions, TFCond.Zoomed).IsEmpty);
    }
}

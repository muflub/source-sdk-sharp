using TF2.Descent.Rules;
using TF2.Descent.Rules.Robots;
using Xunit;

namespace TF2.Descent.Tests.Robots;

/// <summary>
/// Robot status effects (default E9) and the blast push (ruling L12), plan_descent_logic RU7:
/// a fact for each status's damage, duration, stacking and expiry and for the immunity
/// interaction; the push against stock's numbers. Ticks run at 10 a second.
/// </summary>
public sealed class StatusAndPushTests
{
    private static readonly TF2DescentRules Rules = new();
    private static StatusRules Statuses => Rules.Statuses;
    private const double Rate = 10;

    private static RobotStatuses Apply(RobotStatuses s, string id, long now, string? immunity = null) =>
        Statuses.Apply(s, id, now, Rate, immunity).After;

    private static double DamageOver(RobotStatuses s, long from, long to)
    {
        double total = 0;
        for (long t = from; t <= to; t++)
        {
            var tick = Statuses.Tick(s, t, Rate);
            total += tick.Damage.Sum(d => d.Amount);
            s = tick.After;
        }
        return total;
    }

    //-------------------------------------------------------------------------
    // Burn
    //-------------------------------------------------------------------------

    [Fact]
    public void Burn_deals_8_fire_a_second_in_half_second_ticks_for_6_seconds()
    {
        var s = Apply(RobotStatuses.None, "burn", 0);
        var first = Statuses.Tick(s, 5, Rate);

        Assert.Equal(4, Assert.Single(first.Damage).Amount);
        Assert.Equal("fire", first.Damage[0].DamageType);
        Assert.Equal(48, DamageOver(s, 0, 100), 9);
    }

    [Fact]
    public void Burn_ends_after_6_seconds()
    {
        var s = Apply(RobotStatuses.None, "burn", 0);

        Assert.NotNull(Statuses.Tick(s, 59, Rate).After.Of("burn"));
        Assert.Null(Statuses.Tick(s, 60, Rate).After.Of("burn"));
    }

    [Fact]
    public void Burning_again_refreshes_the_end_and_does_not_stack()
    {
        var s = Apply(RobotStatuses.None, "burn", 0);
        var (again, what) = Statuses.Apply(s, "burn", 30, Rate, null);

        Assert.Equal(StatusApplied.Refreshed, what);
        Assert.Equal(90, again.Of("burn")!.EndTick);
        Assert.Equal(1, again.Of("burn")!.Stacks);
    }

    [Fact]
    public void A_fire_immune_robot_cannot_burn()
    {
        var (after, what) = Statuses.Apply(RobotStatuses.None, "burn", 0, Rate, "fire");

        Assert.Equal(StatusApplied.Immune, what);
        Assert.Empty(after.Active);
    }

    //-------------------------------------------------------------------------
    // Bleed
    //-------------------------------------------------------------------------

    [Fact]
    public void Bleed_deals_8_bullet_a_second_for_5_seconds()
    {
        var s = Apply(RobotStatuses.None, "bleed", 0);

        Assert.Equal(40, DamageOver(s, 0, 100), 9);
        Assert.Equal("bullet", Statuses.Tick(s, 5, Rate).Damage[0].DamageType);
    }

    [Fact]
    public void Bleed_stacks_to_three_and_each_stack_bleeds()
    {
        var s = RobotStatuses.None;
        for (int i = 0; i < 4; i++) s = Apply(s, "bleed", 0);

        Assert.Equal(3, s.Of("bleed")!.Stacks);
        Assert.Equal(12, Assert.Single(Statuses.Tick(s, 5, Rate).Damage).Amount);
    }

    [Fact]
    public void A_bulletproof_robot_cannot_bleed()
    {
        Assert.Equal(StatusApplied.Immune, Statuses.Apply(RobotStatuses.None, "bleed", 0, Rate, "bullet").What);
    }

    //-------------------------------------------------------------------------
    // Stun, slow, marked
    //-------------------------------------------------------------------------

    [Fact]
    public void Stun_stops_the_robot_for_2_seconds()
    {
        var s = Apply(RobotStatuses.None, "stun", 0);

        Assert.True(Statuses.Stunned(s, 19));
        Assert.Equal(0, Statuses.SpeedMultiplier(s, 19));
        Assert.False(Statuses.Stunned(s, 20));
    }

    [Fact]
    public void A_shorter_stun_never_cuts_a_longer_one_short()
    {
        var s = Apply(RobotStatuses.None, "stun", 0);
        var again = Apply(s, "stun", 5);

        Assert.Equal(25, again.Of("stun")!.EndTick);
        Assert.Equal(25, Apply(again, "stun", 1).Of("stun")!.EndTick);
    }

    [Fact]
    public void Slow_takes_40_percent_off_speed_for_3_seconds()
    {
        var s = Apply(RobotStatuses.None, "slow", 0);

        Assert.Equal(0.6, Statuses.SpeedMultiplier(s, 29), 9);
        Assert.Equal(1, Statuses.SpeedMultiplier(s, 30), 9);
    }

    [Fact]
    public void Marked_adds_35_percent_damage_taken_for_7_seconds()
    {
        var s = Apply(RobotStatuses.None, "marked", 0);

        Assert.Equal(1.35, Statuses.DamageTakenMultiplier(s, 69), 9);
        Assert.Equal(1, Statuses.DamageTakenMultiplier(s, 70), 9);
    }

    [Fact]
    public void An_immune_robot_is_still_stunned_slowed_and_marked()
    {
        foreach (var id in new[] { "stun", "slow", "marked" })
            Assert.Equal(StatusApplied.Applied, Statuses.Apply(RobotStatuses.None, id, 0, Rate, "fire").What);
    }

    [Fact]
    public void Every_shipped_status_has_its_facts_here()
    {
        Assert.Equal(["bleed", "burn", "marked", "slow", "stun"], Rules.Content.Status.Rows.Select(s => s.Id).Order());
    }

    //-------------------------------------------------------------------------
    // Blast push (L12)
    //-------------------------------------------------------------------------

    private static readonly Vec3 Up = new(0, 0, 1);

    [Fact]
    public void A_scale_1_robot_takes_the_push_stock_gives_a_player()
    {
        // Stock: 90 damage × (48·48·82 ÷ 48·48·82) × 6 = 540 along the blast's direction.
        var v = Rules.BlastPush.VelocityChange(new BlastForce(new Vec3(0, 0, 3), 90, BlastOwner.Other), 1.0, 0);

        Assert.Equal(new Vec3(0, 0, 540), v);
    }

    [Fact]
    public void A_push_is_capped_at_1000()
    {
        Assert.Equal(1000, Rules.BlastPush.VelocityChange(new BlastForce(Up, 500, BlastOwner.Other), 1.0, 0).Length, 9);
    }

    [Fact]
    public void A_bigger_robot_is_pushed_by_the_cube_of_its_scale_less()
    {
        double small = Rules.BlastPush.VelocityChange(new BlastForce(Up, 40, BlastOwner.Other), 1.0, 0).Length;
        double giant = Rules.BlastPush.VelocityChange(new BlastForce(Up, 40, BlastOwner.Other), 1.75, 0).Length;

        Assert.Equal(small / Math.Pow(1.75, 3), giant, 9);
    }

    [Fact]
    public void Knockback_resistance_takes_its_share_off_anothers_push()
    {
        double plain = Rules.BlastPush.VelocityChange(new BlastForce(Up, 60, BlastOwner.Other), 1.0, 0).Length;
        double resisted = Rules.BlastPush.VelocityChange(new BlastForce(Up, 60, BlastOwner.Other), 1.0, 25).Length;

        Assert.Equal(plain * 0.75, resisted, 9);
    }

    [Fact]
    public void A_robots_own_blast_ignores_its_knockback_resistance()
    {
        double plain = Rules.BlastPush.VelocityChange(new BlastForce(Up, 60, BlastOwner.SelfSoldierAirborne), 1.0, 0).Length;
        double resisted = Rules.BlastPush.VelocityChange(new BlastForce(Up, 60, BlastOwner.SelfSoldierAirborne), 1.0, 50).Length;

        Assert.Equal(plain, resisted, 9);
    }

    [Theory]
    [InlineData(BlastOwner.Other, 6)]
    [InlineData(BlastOwner.SelfSoldierAirborne, 10)]
    [InlineData(BlastOwner.SelfSoldierGrounded, 5)]
    [InlineData(BlastOwner.SelfOther, 9)]
    public void Each_owner_pushes_with_stocks_force_scale(BlastOwner owner, double scale) =>
        Assert.Equal(10 * scale, Rules.BlastPush.VelocityChange(new BlastForce(Up, 10, owner), 1.0, 0).Length, 9);

    [Fact]
    public void A_heavy_takes_half_of_anothers_push_and_all_of_its_own()
    {
        var push = Rules.BlastPush;

        Assert.Equal(270, push.VelocityChange(new BlastForce(Up, 90, BlastOwner.Other), 1.0, 0, heavy: true).Length, 9);
        Assert.Equal(810, push.VelocityChange(new BlastForce(Up, 90, BlastOwner.SelfOther), 1.0, 0, heavy: true).Length, 9);
    }
}

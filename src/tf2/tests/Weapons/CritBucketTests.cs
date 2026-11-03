using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// <see cref="CritBucket"/> against stock's arithmetic
/// (<c>basecombatweapon_shared.cpp:1593-1636</c>): bucket default 300, cap
/// 1000, bottom -250 (:60-62), cost = damage × 3 × factor (:1621-1624).
/// </summary>
public sealed class CritBucketTests
{
    private const float Cap = 1000f;
    private const float Bottom = -250f;

    [Fact]
    public void A_new_bucket_holds_the_default_one_check_and_no_requests()
    {
        var bucket = new CritBucket(300f);

        Assert.Equal(300f, bucket.Tokens);
        Assert.Equal(1, bucket.Checks);
        Assert.Equal(0, bucket.SeedRequests);
    }

    [Fact]
    public void Three_steps_of_a_scattergun_match_the_hand_computed_stock_sequence()
    {
        // A scattergun shot is 6 damage x 10 pellets = 60 (CalcIsAttackCriticalHelper :1636-1646).
        var bucket = new CritBucket(300f);

        // Step 1: AddToCritBucket(60): 300 + 60 = 360; the roll is counted: checks 2.
        bucket.Add(60f, Cap);
        bucket.CountCheck();
        Assert.Equal(360f, bucket.Tokens);
        Assert.Equal(2, bucket.Checks);

        // Step 2: the seed says crit. Requests 1, ratio 1/2 = 0.5, factor
        // RemapValClamped(0.5, 0.1, 1, 1, 3) = 1 + 2 * (0.4 / 0.9) = 1.8889;
        // cost 60 * 3 * 1.8889 = 340.0 <= 360: withdrawn, 20 left.
        Assert.True(bucket.TryWithdraw(60f, isMelee: false, Bottom));
        Assert.Equal(1, bucket.SeedRequests);
        Assert.Equal(20f, bucket.Tokens, 3);

        // Step 3: the next shot adds 60 (80) and its seed says crit again, but
        // no check was counted: ratio 2/2 = 1, factor 3, cost 540 > 80: refused,
        // nothing paid.
        bucket.Add(60f, Cap);
        Assert.False(bucket.TryWithdraw(60f, isMelee: false, Bottom));
        Assert.Equal(2, bucket.SeedRequests);
        Assert.Equal(80f, bucket.Tokens, 3);
    }

    [Fact]
    public void The_bucket_fills_to_its_cap_and_no_further()
    {
        var bucket = new CritBucket(990f);

        bucket.Add(60f, Cap);
        Assert.Equal(1000f, bucket.Tokens);

        // Full: AddToCritBucket adds only below the cap.
        bucket.Add(60f, Cap);
        Assert.Equal(1000f, bucket.Tokens);
    }

    [Fact]
    public void A_bucket_above_its_cap_is_not_topped_up()
    {
        // The cap is a convar: lowered under a full bucket, the bucket is left as it is.
        var bucket = new CritBucket(1000f);

        bucket.Add(60f, 500f);
        Assert.Equal(1000f, bucket.Tokens);
    }

    [Fact]
    public void A_withdrawal_leaves_the_bucket_no_lower_than_its_bottom()
    {
        var bucket = new CritBucket(360f);
        bucket.CountCheck();

        // The same 340 withdrawal as the sequence, against a bottom of 100: 20 is held at 100.
        Assert.True(bucket.TryWithdraw(60f, isMelee: false, bottom: 100f));
        Assert.Equal(100f, bucket.Tokens);
    }

    [Fact]
    public void Melee_pays_half_whatever_its_request_ratio()
    {
        Assert.Equal(0.5f, CritBucket.Multiplier(isMelee: true, requests: 1, checks: 1));
        Assert.Equal(0.5f, CritBucket.Multiplier(isMelee: true, requests: 1, checks: 100));

        // A bat's 35: 35 * 3 * 0.5 = 52.5 from 300.
        var bucket = new CritBucket(300f);
        Assert.True(bucket.TryWithdraw(35f, isMelee: true, Bottom));
        Assert.Equal(247.5f, bucket.Tokens);
    }

    [Fact]
    public void The_ranged_factor_runs_from_one_at_a_tenth_to_three_at_every_check()
    {
        Assert.Equal(1f, CritBucket.Multiplier(false, requests: 1, checks: 10));
        Assert.Equal(1f, CritBucket.Multiplier(false, requests: 1, checks: 100));
        Assert.Equal(3f, CritBucket.Multiplier(false, requests: 2, checks: 2));
        Assert.Equal(3f, CritBucket.Multiplier(false, requests: 4, checks: 2));
        Assert.Equal(2f, CritBucket.Multiplier(false, requests: 11, checks: 20), 4);
    }

    [Fact]
    public void A_refused_withdrawal_still_counts_its_request()
    {
        var bucket = new CritBucket(0f);

        Assert.False(bucket.TryWithdraw(10f, isMelee: false, Bottom));
        Assert.Equal(1, bucket.SeedRequests);
        Assert.Equal(0f, bucket.Tokens);
    }
}

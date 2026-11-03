using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary><see cref="TFCritBuckets"/>: one bucket per weapon entity and row, per side.</summary>
public sealed class TFCritBucketsTests
{
    [Fact]
    public void A_weapon_and_row_keep_one_bucket_made_full_of_the_default()
    {
        var buckets = new TFCritBuckets();

        var bucket = buckets.For(5, 40, 300f);
        bucket.Add(60f, 1000f);

        Assert.Same(bucket, buckets.For(5, 40, 999f));
        Assert.Equal(360f, buckets.For(5, 40, 999f).Tokens);
    }

    [Fact]
    public void Another_row_in_the_same_hands_has_its_own_bucket()
    {
        var buckets = new TFCritBuckets();

        Assert.NotSame(buckets.For(5, 40, 300f), buckets.For(5, 41, 300f));
        Assert.NotSame(buckets.For(5, 40, 300f), buckets.For(6, 40, 300f));
        Assert.Equal(3, buckets.Count);
    }

    [Fact]
    public void A_reset_forgets_that_weapons_buckets_only()
    {
        var buckets = new TFCritBuckets();
        buckets.For(5, 40, 300f).Add(60f, 1000f);
        buckets.For(5, 41, 300f);
        buckets.For(6, 40, 300f).Add(60f, 1000f);

        buckets.Reset(5);

        Assert.Equal(1, buckets.Count);
        Assert.Equal(300f, buckets.For(5, 40, 300f).Tokens);
        Assert.Equal(360f, buckets.For(6, 40, 300f).Tokens);
    }
}

/// <summary><see cref="TFCritState"/>'s memory of the commands it rolled.</summary>
public sealed class TFCritStateTests
{
    [Fact]
    public void A_remembered_command_is_recalled_with_its_clocks()
    {
        var state = new TFCritState(300f);
        state.Remember(41, new TFCritResult(true, true, 7), new TFCritClocks { CritUntil = 12f, LastRapidCheck = 10f });

        Assert.True(state.Recall(41, out var result, out var clocks));
        Assert.Equal(new TFCritResult(true, true, 7), result);
        Assert.Equal(12f, clocks.CritUntil);
        Assert.False(state.Recall(42, out _, out _));
    }

    [Fact]
    public void Only_the_last_sixty_four_commands_are_remembered()
    {
        var state = new TFCritState(300f);
        for (int seed = 1; seed <= TFCritState.Remembered + 1; seed++)
            state.Remember(seed, new TFCritResult(seed % 2 == 0), default);

        Assert.False(state.Recall(1, out _, out _));
        Assert.True(state.Recall(2, out var oldest, out _));
        Assert.True(oldest.Critical);
        Assert.True(state.Recall(TFCritState.Remembered + 1, out _, out _));
    }

    [Fact]
    public void A_copy_holds_the_tokens_checks_and_requests_and_pays_from_itself()
    {
        var bucket = new CritBucket(300f);
        bucket.CountCheck();
        bucket.TryWithdraw(10f, false, -250f);

        var copy = bucket.Copy();
        Assert.Equal(bucket.Tokens, copy.Tokens);
        Assert.Equal(2, copy.Checks);
        Assert.Equal(1, copy.SeedRequests);

        copy.TryWithdraw(10f, false, -250f);
        Assert.Equal(1, bucket.SeedRequests);
    }
}

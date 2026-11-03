using SourceSharp.Abi;

using TF2.Shared;
using TF2.Shared.Conditions;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// <c>Critboosted</c> in <see cref="TFConditions"/>: stock keeps it in
/// <c>CTFConditionList</c> (<c>tf_condition.cpp</c>) with a min and a max
/// duration instead of <c>AddCond</c>'s keep-the-longer rule.
/// </summary>
public sealed class TFConditionsCritBoostTests
{
    private const float Tick = 0.25f;

    private static TFConditions Server() => new(authoritative: true);

    [Fact]
    public void The_first_add_starts_the_max_duration()
    {
        TFConditions conditions = Server();

        Assert.Equal(TFCondAdd.Added, conditions.Add(TFCond.Critboosted, 5f));

        Assert.True(conditions.Has(TFCond.Critboosted));
        Assert.Equal(5f, conditions.State.CritBoostMaxDuration);
        Assert.Equal(0f, conditions.State.CritBoostMinDuration);
    }

    [Fact]
    public void Its_condition_data_duration_reads_zero_as_stock_does()
    {
        TFConditions conditions = Server();

        conditions.Add(TFCond.Critboosted, 5f);

        Assert.Equal(0f, conditions.Duration(TFCond.Critboosted));
    }

    [Fact]
    public void It_expires_when_the_max_runs_out()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Critboosted, 1f);

        for (int tick = 1; tick <= 3; tick++)
        {
            Assert.True(conditions.Tick(Tick, 0, default).IsEmpty);
        }

        Assert.Equal(TFCondMask.Of(TFCond.Critboosted), conditions.Tick(Tick, 0, default));
        Assert.False(conditions.Has(TFCond.Critboosted));
    }

    [Fact]
    public void Healers_never_speed_it_up()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Critboosted, 1f);

        conditions.Tick(Tick, healers: 3, TFCondMask.Of(TFCond.Critboosted));

        Assert.Equal(1f - Tick, conditions.State.CritBoostMaxDuration);
    }

    [Fact]
    public void A_shorter_readd_becomes_the_min_and_keeps_the_max()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Critboosted, 5f);

        Assert.Equal(TFCondAdd.Merged, conditions.Add(TFCond.Critboosted, 2f));

        Assert.Equal(5f, conditions.State.CritBoostMaxDuration);
        Assert.Equal(2f, conditions.State.CritBoostMinDuration);
    }

    [Fact]
    public void A_longer_readd_replaces_the_max()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Critboosted, 2f);

        conditions.Add(TFCond.Critboosted, 6f);

        Assert.Equal(6f, conditions.State.CritBoostMaxDuration);
        Assert.Equal(0f, conditions.State.CritBoostMinDuration);
    }

    [Fact]
    public void A_permanent_readd_keeps_the_old_max_as_the_min()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Critboosted, 4f);

        conditions.Add(TFCond.Critboosted, TFConditions.Permanent);

        Assert.Equal(TFConditions.Permanent, conditions.State.CritBoostMaxDuration);
        Assert.Equal(4f, conditions.State.CritBoostMinDuration);
    }

    [Fact]
    public void A_remove_waits_out_the_min()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Critboosted, TFConditions.Permanent);
        conditions.Add(TFCond.Critboosted, 2f);

        Assert.False(conditions.Remove(TFCond.Critboosted));

        Assert.True(conditions.Has(TFCond.Critboosted));
        Assert.Equal(2f, conditions.State.CritBoostMaxDuration);
    }

    [Fact]
    public void After_a_refused_remove_it_expires_with_the_min()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Critboosted, TFConditions.Permanent);
        conditions.Add(TFCond.Critboosted, 1f);
        conditions.Remove(TFCond.Critboosted);

        int ticks = 0;
        while (conditions.Has(TFCond.Critboosted) && ticks < 100)
        {
            conditions.Tick(Tick, 0, default);
            ticks++;
        }

        Assert.Equal(4, ticks);
    }

    [Fact]
    public void A_remove_ignoring_duration_ends_it_at_once()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Critboosted, TFConditions.Permanent);
        conditions.Add(TFCond.Critboosted, 2f);

        Assert.True(conditions.Remove(TFCond.Critboosted, ignoreDuration: true));

        Assert.False(conditions.Has(TFCond.Critboosted));
    }

    [Fact]
    public void A_readd_replaces_the_provider()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Critboosted, 5f, new EntityHandle(2));

        conditions.Add(TFCond.Critboosted, 1f, new EntityHandle(8));

        Assert.Equal(new EntityHandle(8), conditions.Provider(TFCond.Critboosted));
    }

    [Fact]
    public void A_client_add_is_left_to_the_server()
    {
        var conditions = new TFConditions(authoritative: false);

        Assert.Equal(TFCondAdd.Ignored, conditions.Add(TFCond.Critboosted, 5f));

        Assert.False(conditions.Has(TFCond.Critboosted));
    }

    [Fact]
    public void A_client_cannot_remove_a_networked_crit_boost()
    {
        var conditions = new TFConditions(authoritative: false);
        conditions.ReceiveBits(TFCondMask.Of(TFCond.Critboosted));

        Assert.False(conditions.Remove(TFCond.Critboosted, ignoreDuration: true));

        Assert.True(conditions.Has(TFCond.Critboosted));
    }

    [Fact]
    public void A_new_boost_after_expiry_starts_with_no_min()
    {
        TFConditions conditions = Server();
        conditions.Add(TFCond.Critboosted, 0.5f);
        conditions.Add(TFCond.Critboosted, 0.25f);
        conditions.Tick(Tick, 0, default);
        conditions.Tick(Tick, 0, default);
        Assert.False(conditions.Has(TFCond.Critboosted));

        conditions.Add(TFCond.Critboosted, 3f);

        Assert.Equal(0f, conditions.State.CritBoostMinDuration);
    }
}

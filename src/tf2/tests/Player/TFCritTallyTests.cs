using TF2.Shared;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>The crit state's tallies and the buckets by weapon that <c>tf2_crits</c> reports.</summary>
public sealed class TFCritTallyTests
{
    [Fact]
    public void A_first_roll_counts_and_a_crit_counts_twice_over()
    {
        var state = new TFCritState(300f);

        state.Remember(1, new TFCritResult(Critical: false), default);
        state.Remember(2, new TFCritResult(Critical: true, Random: true), default);

        Assert.Equal(2, state.Rolls);
        Assert.Equal(1, state.Crits);
        Assert.True(state.Last.Critical);
    }

    [Fact]
    public void A_weapons_buckets_are_found_by_its_entity_index()
    {
        var buckets = new TFCritBuckets();
        buckets.StateFor(40, 3, 300f);
        buckets.StateFor(40, 7, 300f);
        buckets.StateFor(41, 3, 300f);

        Assert.Equal([3, 7], buckets.Of(40).Select(s => s.TypeId).Order());
        Assert.Single(buckets.Of(41));
        Assert.Empty(buckets.Of(42));
    }
}

using Xunit;

namespace SourceSharp.Ai.Tests.Core;

public class AiRandomTests
{
    private static ulong[] Draw(ulong seed, int n)
    {
        var r = new AiRandom(seed);
        return Enumerable.Range(0, n).Select(_ => r.NextUInt64()).ToArray();
    }

    [Fact]
    public void OneSeedDrawsOneSequence()
        => Assert.Equal(Draw(42, 16), Draw(42, 16));

    [Fact]
    public void TwoSeedsDrawDifferentSequences()
        => Assert.NotEqual(Draw(42, 16), Draw(43, 16));

    [Fact]
    public void TheFirstDrawOfSeedZeroIsSplitMix64s()
        // SplitMix64's published first output for seed 0, so the stream is the
        // algorithm it says and not one a runtime may change.
        => Assert.Equal(0xE220A8397B1DCDAFUL, new AiRandom(0).NextUInt64());

    [Fact]
    public void EachSlotOfAMatchGetsItsOwnSeed()
        => Assert.Equal(64, Enumerable.Range(0, 64).Select(s => AiRandom.SeedFor(7, s)).Distinct().Count());

    [Fact]
    public void FloatsStayInTheUnitInterval()
    {
        var r = new AiRandom(9);
        float[] f = Enumerable.Range(0, 10_000).Select(_ => r.NextFloat()).ToArray();
        Assert.True(f.All(x => x is >= 0f and < 1f), "every float is in [0, 1)");
    }
}

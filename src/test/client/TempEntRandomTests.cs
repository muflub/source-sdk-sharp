using SourceSharp.Client.TempEnts;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client temp entities' random stream: Source's ranges, and one seeded
/// stream per owner rather than one static stream for the process.
/// </summary>
public class TempEntRandomTests
{
    [Fact]
    public void AnIntRangeIncludesBothEnds()
    {
        // random->RandomInt(0, 1) can answer 1; Random.Next(0, 1) cannot.
        var random = new TempEntRandom(3);
        var seen = new HashSet<int>();

        for (int i = 0; i < 200; i++)
            seen.Add(random.Int(0, 1));

        Assert.Equal(new HashSet<int> { 0, 1 }, seen);
    }

    [Fact]
    public void AReversedIntRangeAnswersItsLowEnd()
        => Assert.Equal(9, new TempEntRandom(3).Int(9, 4));

    [Fact]
    public void AStreamSeededTheSameWayTwiceDrawsTheSameNumbers()
    {
        var first = new TempEntRandom(20260929);
        var second = new TempEntRandom(20260929);

        float[] a = Enumerable.Range(0, 20).Select(_ => first.Float(0f, 1f)).ToArray();
        float[] b = Enumerable.Range(0, 20).Select(_ => second.Float(0f, 1f)).ToArray();

        Assert.Equal(a, b);
    }

    [Fact]
    public void TwoStreamsSeededDifferentlyDrawDifferently()
    {
        // The property the static stream lacked, and the one the seed source
        // exists to give: two owners do not share one sequence.
        var seeds = new SourceSharp.Abi.RandomSeeds(20260929);
        var first = new TempEntRandom(seeds.Next());
        var second = new TempEntRandom(seeds.Next());

        int[] a = Enumerable.Range(0, 50).Select(_ => first.Int(0, 255)).ToArray();
        int[] b = Enumerable.Range(0, 50).Select(_ => second.Int(0, 255)).ToArray();

        Assert.NotEqual(a, b);
    }
}

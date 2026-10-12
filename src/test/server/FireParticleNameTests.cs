using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The size-to-particle-system decision <c>C_FireSmoke::Start</c> used to make
/// in C++, and the two things about it that were wrong.
///
/// Engine-free on purpose: it is a float and a bool going in and a string
/// coming out, so nothing here needs a running Source or an engine double.
/// </summary>
public class FireParticleNameTests
{
    [Fact]
    public void StocksFourBucketsAreTheFourStockNames()
    {
        Assert.True(FireParticleNames.ForSize(10f, smoke: false) == "env_fire_tiny",
            "a ten-unit flame is stock's tiny fire");
        Assert.True(FireParticleNames.ForSize(64f, smoke: false) == "env_fire_small",
            "a default sixty-four-unit flame is stock's small fire");
        Assert.True(FireParticleNames.ForSize(80f, smoke: false) == "env_fire_medium",
            "an eighty-unit flame is stock's medium fire");
        Assert.True(FireParticleNames.ForSize(200f, smoke: false) == "env_fire_large",
            "a two-hundred-unit flame is stock's large fire");
    }

    [Fact]
    public void TheBucketBoundariesAreWhereDividingByThirtySixPutsThem()
    {
        Assert.True(FireParticleNames.BucketForSize(35.9f) == 0, "just under 36 units is still the tiny bucket");
        Assert.True(FireParticleNames.BucketForSize(36f) == 1, "36 units is the first unit of the small bucket");
        Assert.True(FireParticleNames.BucketForSize(71.9f) == 1, "just under 72 units is still the small bucket");
        Assert.True(FireParticleNames.BucketForSize(72f) == 2, "72 units is the first unit of the medium bucket");
        Assert.True(FireParticleNames.BucketForSize(108f) == 3, "108 units is the first unit of the large bucket");
    }

    [Fact]
    public void TheSmokeFlagPicksTheSmokyVariantOfTheSameBucket()
    {
        Assert.True(FireParticleNames.ForSize(64f, smoke: true) == "env_fire_small_smoke",
            "a smoky flame of the same size is the same bucket's _smoke variant");
        Assert.True(FireParticleNames.ForSize(200f, smoke: true) == "env_fire_large_smoke",
            "the largest bucket has a smoky variant too");
    }

    /// <summary>
    /// The bug being fixed, stated as the case that used to come out wrong.
    ///
    /// A flame past the largest bucket must clamp to large; stock reached the
    /// same answer through its <c>default</c> label, which is why the failure
    /// below went unnoticed for so long — the label was doing double duty.
    /// </summary>
    [Fact]
    public void AnEnormousFlameClampsToTheLargestBucketRatherThanOverrunningIt()
    {
        Assert.True(FireParticleNames.BucketForSize(100000f) == FireParticleNames.MaxBucket,
            "a flame far past the largest bucket is drawn as the largest one");
        Assert.True(FireParticleNames.ForSize(100000f, smoke: false) == "env_fire_large",
            "and it has a name rather than falling off the end of the switch");
    }

    /// <summary>
    /// THE DIVIDE-BY-DEFAULT CASE. Stock's <c>default</c> sits with
    /// <c>case 3</c>, so every one of these came out as the LARGEST fire in the
    /// map. An unconfigured flame is the commonest of them, because the size
    /// arrives from a map key.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(-100000f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ASizeThatIsNotASizeDrawsStocksUnconfiguredFlameNotTheBiggestOne(float size)
    {
        int bucket = FireParticleNames.BucketForSize(size);

        Assert.True(bucket == FireParticleNames.BucketForSize(FireParticleNames.DefaultSize),
            "a size that is not finite and strictly positive falls back to stock's own default size");
        Assert.True(bucket != FireParticleNames.MaxBucket,
            "and specifically not to the largest fire there is, which is where stock's default label sent it");
    }

    [Fact]
    public void EveryBucketAlwaysLandsInRange()
    {
        // Walks the whole domain a map can key at a granularity finer than a
        // bucket, so a clamp that only worked at the ends could not hide.
        for (float size = -500f; size < 1000f; size += 0.5f)
        {
            int bucket = FireParticleNames.BucketForSize(size);

            Assert.True(bucket >= 0 && bucket <= FireParticleNames.MaxBucket,
                $"the bucket for {size} units is one of the four that exist");
        }
    }

    /// <summary>
    /// What the server precaches has to be exactly what the client can ask for.
    /// A particle system that was not precached does not draw, and nothing says
    /// so at the time.
    /// </summary>
    [Fact]
    public void ThePrecacheListIsExactlyTheNamesThatCanBeReturned()
    {
        var all = new HashSet<string>(FireParticleNames.All());

        Assert.True(all.Count == 8, "four buckets with and without smoke is eight names");

        for (int bucket = 0; bucket <= FireParticleNames.MaxBucket; bucket++)
        {
            Assert.True(all.Contains(FireParticleNames.ForBucket(bucket, smoke: false)),
                $"bucket {bucket}'s plain name is precached");
            Assert.True(all.Contains(FireParticleNames.ForBucket(bucket, smoke: true)),
                $"bucket {bucket}'s smoky name is precached");
        }

        // And the reachable set from the SIZE side, which is the door the game
        // actually comes through.
        for (float size = 0f; size < 400f; size += 1f)
        {
            Assert.True(all.Contains(FireParticleNames.ForSize(size, smoke: false)),
                $"the plain name for {size} units was precached");
            Assert.True(all.Contains(FireParticleNames.ForSize(size, smoke: true)),
                $"the smoky name for {size} units was precached");
        }
    }
}

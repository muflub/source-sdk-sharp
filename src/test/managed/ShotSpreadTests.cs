using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="ShotSpread"/> against stock's FireBullets seeding and
/// CShotManipulator::ApplySpread at the default bias.
/// </summary>
public class ShotSpreadTests
{
    private static readonly Angles.Basis Aim = new(new Vector(1, 0, 0), new Vector(0, -1, 0), new Vector(0, 0, 1));

    [Fact]
    public void The_first_seed_is_the_commands_random_seed_masked_to_a_byte()
    {
        Assert.Equal(0x34, ShotSpread.FirstSeed(0x1234));
        Assert.Equal(255, ShotSpread.FirstSeed(0x7fffffff));
    }

    [Fact]
    public void One_seed_spreads_one_way_on_every_side()
    {
        Vector a = ShotSpread.Apply(Aim, 0.1f, 17, new SeededUniformStream());
        Vector b = ShotSpread.Apply(Aim, 0.1f, 17, new SeededUniformStream(99));

        Assert.Equal(a, b);
    }

    [Fact]
    public void The_next_pellets_seed_goes_another_way()
    {
        var stream = new SeededUniformStream();

        Assert.NotEqual(ShotSpread.Apply(Aim, 0.1f, 17, stream), ShotSpread.Apply(Aim, 0.1f, 18, stream));
    }

    [Fact]
    public void It_is_stocks_rejection_sample_of_two_halved_sums()
    {
        // ApplySpread by hand: RandomSeed(seed), then x and y each the sum of
        // two halved draws in [-1,1], drawn again while outside the unit disc.
        const int seed = 200;
        const float spread = 0.08716f;
        var draws = new SeededUniformStream(seed);
        float x, y;
        do
        {
            x = draws.RandomFloat(-1f, 1f) * 0.5f + draws.RandomFloat(-1f, 1f) * 0.5f;
            y = draws.RandomFloat(-1f, 1f) * 0.5f + draws.RandomFloat(-1f, 1f) * 0.5f;
        }
        while (x * x + y * y > 1f);

        Vector got = ShotSpread.Apply(Aim, spread, seed, new SeededUniformStream());

        Assert.Equal(1f, got.X);
        Assert.Equal(-x * spread, got.Y, 6);
        Assert.Equal(y * spread, got.Z, 6);
        Assert.True(x != 0f || y != 0f, "the sample moved the shot (the positive arm)");
    }

    [Fact]
    public void No_spread_is_the_aim_itself()
        => Assert.Equal(Aim.Forward, ShotSpread.Apply(Aim, 0f, 5, new SeededUniformStream()));

    [Fact]
    public void The_spread_is_the_cones_sine()
        => Assert.Equal(MathF.Sin(MathF.PI / 120f), ShotSpread.SpreadOf(1.5f), 6);
}

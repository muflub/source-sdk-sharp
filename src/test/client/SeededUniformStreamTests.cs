using SourceSharp.Client.Effects;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The shared <see cref="SeededUniformStream"/> is vstdlib's
/// <c>CUniformRandomStream</c>, held draw for draw to the client's
/// <see cref="UniformStream"/>, whose native twin vstdlib_random_oracle_test
/// holds to the game's libvstdlib.so.
/// </summary>
public class SeededUniformStreamTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(255)]
    [InlineData(-3)]
    [InlineData(int.MaxValue)]
    public void Draws_the_same_floats_and_ints_as_the_client_stream(int seed)
    {
        var shared = new SeededUniformStream(seed);
        var client = new UniformStream(seed);

        for (int i = 0; i < 300; i++)
        {
            Assert.Equal(client.RandomFloat(-1f, 1f), shared.RandomFloat(-1f, 1f));
            Assert.Equal(client.RandomInt(0, 99), shared.RandomInt(0, 99));
        }
    }

    [Fact]
    public void Reseeding_restarts_the_sequence()
    {
        var stream = new SeededUniformStream(42);
        float first = stream.RandomFloat(0f, 1f);
        float second = stream.RandomFloat(0f, 1f);

        Assert.NotEqual(first, second);

        stream.SetSeed(42);
        Assert.Equal(first, stream.RandomFloat(0f, 1f));
    }

    [Fact]
    public void A_restored_state_continues_where_the_copy_was_taken()
    {
        var stream = new SeededUniformStream(9);
        stream.RandomFloat(0f, 1f);

        Span<int> table = stackalloc int[SeededUniformStream.TableSize];
        stream.CopyState(table, out int state, out int last);
        float next = stream.RandomFloat(0f, 1f);

        var other = new SeededUniformStream(1234);
        other.RestoreState(table, state, last);

        Assert.Equal(next, other.RandomFloat(0f, 1f));
    }
}

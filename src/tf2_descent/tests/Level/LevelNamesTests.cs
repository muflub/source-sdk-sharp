using TF2.Descent.Server.Level;
using Xunit;

namespace TF2.Descent.Tests.Level;

public sealed class LevelNamesTests
{
    [Theory]
    [InlineData("tf2_descent-1-s1", true, 1, 1UL)]
    [InlineData("tf2_descent-12-s4096", true, 12, 4096UL)]
    [InlineData("tf2_descent-3-9f2c1a", true, 3, null)]   // a pool instance: the hash is no seed
    [InlineData("tf2_descent-2", true, 2, null)]
    [InlineData("tf2_descent-1-s", true, 1, null)]
    [InlineData("tf2_descent_town", false, 0, null)]
    [InlineData("ctf_2fort", false, 0, null)]
    public void A_name_says_whether_it_is_a_level_its_depth_and_its_seed(string map, bool level, int depth, ulong? seed)
    {
        Assert.Equal(level, LevelNames.IsLevel(map));
        Assert.Equal(depth, LevelNames.Depth(map));
        Assert.Equal(seed, LevelNames.Seed(map));
    }
}

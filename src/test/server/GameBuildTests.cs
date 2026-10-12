using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>IGameBuild: a side's build, as the version string a mod reports to its hosting service.</summary>
public class GameBuildTests
{
    [Fact]
    public void A_known_half_is_its_hash_and_source()
    {
        var half = new BuildHalf(new BuildStamp("abc123", DateTime.UnixEpoch, 3),
                                 new GitProvenance("main", "6ec0a66", false), "/x", false);

        string version = GameBuild.Version(half);

        Assert.StartsWith("abc123 ", version);
        Assert.Contains("6ec0a66", version);
    }

    [Fact]
    public void An_unknown_half_says_so()
    {
        Assert.Equal("unknown", GameBuild.Version(BuildHalf.Unknown));
    }
}

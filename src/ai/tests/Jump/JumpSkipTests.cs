using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using Xunit;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary><c>ai_tf_links_skip</c>'s place (lane G6b): what it reads, and what it covers.</summary>
public class JumpSkipTests
{
    [Fact]
    public void AFourNumberPlaceIsRead()
        => Assert.Equal(new JumpSkip(new Vector(1028f, 1150.5f, 266f), 200f), JumpSkip.Parse(" 1028 1150.5  266 200 "));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1 2 3")]
    [InlineData("1 2 3 4 5")]
    [InlineData("1 2 3 0")]
    [InlineData("1 2 3 -5")]
    [InlineData("a 2 3 4")]
    [InlineData("1 2 nan 4")]
    public void AnythingElseIsNoPlace(string? text) => Assert.Null(JumpSkip.Parse(text));

    [Fact]
    public void ItCoversALandingWithinItsRadiusOverTheGroundAndHalfABinOfItsHeight()
    {
        var skip = new JumpSkip(new Vector(100f, 100f, 50f), 64f);
        Assert.True(skip.Covers(new Vector(140f, 140f, 50f + JumpSkip.HeightSlack)));
        Assert.False(skip.Covers(new Vector(150f, 150f, 50f)));
        Assert.False(skip.Covers(new Vector(100f, 100f, 50f - JumpSkip.HeightSlack - 1f)));
    }

    [Fact]
    public void ItPrintsAsItIsRead()
    {
        var skip = new JumpSkip(new Vector(1028f, 1150.5f, 266f), 200f);
        Assert.Equal("1028 1150.5 266 200", skip.ToString());
        Assert.Equal(skip, JumpSkip.Parse(skip.ToString()));
    }
}

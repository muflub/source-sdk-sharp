using System.Collections.Generic;

using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>CBasePlayer::ClientSettingsChanged</c>'s two lag compensation words
/// (game/server/player.cpp:3474-3532): the interpolation the client draws with,
/// and whether it asked to be compensated.
/// </summary>
public class LagCompensationClientSettingsTests
{
    private static Func<string, string?> Of(Dictionary<string, string> values)
        => name => values.TryGetValue(name, out string? v) ? v : null;

    private static readonly Dictionary<string, string> Stock = new()
    {
        ["cl_updaterate"] = "20", ["cl_interpolate"] = "1", ["cl_interp"] = "0.1", ["cl_interp_ratio"] = "2",
        ["cl_predict"] = "1", ["cl_lagcompensation"] = "1",
    };

    private static readonly Dictionary<string, string> Bounds = new()
    {
        ["sv_minupdaterate"] = "10", ["sv_maxupdaterate"] = "66",
        ["sv_client_min_interp_ratio"] = "1", ["sv_client_max_interp_ratio"] = "5",
    };

    private static Dictionary<string, string> With(Dictionary<string, string> from, string name, string value)
        => new(from) { [name] = value };

    [Fact]
    public void TheLerpIsTheLargerOfTheInterpAndTheRatioOverTheRate()
    {
        float stock = LagCompensationClientSettings.LerpTime(Of(Stock), Of(Bounds));
        float ratio = LagCompensationClientSettings.LerpTime(Of(With(Stock, "cl_interp", "0")), Of(Bounds));
        float interp = LagCompensationClientSettings.LerpTime(Of(With(Stock, "cl_interp", "0.25")), Of(Bounds));

        Assert.Equal((0.1f, 0.1f, 0.25f), (stock, ratio, interp));
    }

    [Fact]
    public void TheRateAndTheRatioAreHeldToTheServersBounds()
    {
        // A rate of 200 is 66 here; a ratio of 9 is 5: 5/66.
        var asked = With(With(With(Stock, "cl_updaterate", "200"), "cl_interp_ratio", "9"), "cl_interp", "0");

        Assert.Equal(5f / 66f, LagCompensationClientSettings.LerpTime(Of(asked), Of(Bounds)), 6);
    }

    [Fact]
    public void AMinimumRatioOfMinusOneLeavesTheRatioUnbounded()
    {
        var asked = With(With(Stock, "cl_interp_ratio", "9"), "cl_interp", "0");
        var open = With(Bounds, "sv_client_min_interp_ratio", "-1");

        Assert.Equal(9f / 20f, LagCompensationClientSettings.LerpTime(Of(asked), Of(open)), 6);
    }

    [Fact]
    public void ARatioOfZeroIsOne()
    {
        var asked = With(With(Stock, "cl_interp_ratio", "0"), "cl_interp", "0");

        Assert.Equal(1f / 20f, LagCompensationClientSettings.LerpTime(Of(asked), Of(Bounds)), 6);
    }

    [Fact]
    public void NoInterpolationIsNoLerp()
    {
        Assert.Equal(0f, LagCompensationClientSettings.LerpTime(Of(With(Stock, "cl_interpolate", "0")), Of(Bounds)));
    }

    [Fact]
    public void ARateOfZeroWithNoBoundsIsTheInterpAlone()
    {
        var asked = With(Stock, "cl_updaterate", "0");

        Assert.Equal(0.1f, LagCompensationClientSettings.LerpTime(Of(asked), _ => null));
    }

    [Fact]
    public void LagCompensationIsAskedForWithPredictionAndTheConvarBothOn()
    {
        Assert.True(LagCompensationClientSettings.LagCompensation(Of(Stock)));
        Assert.False(LagCompensationClientSettings.LagCompensation(Of(With(Stock, "cl_predict", "0"))));
        Assert.False(LagCompensationClientSettings.LagCompensation(Of(With(Stock, "cl_lagcompensation", "0"))));
        Assert.False(LagCompensationClientSettings.LagCompensation(_ => null));
    }
}

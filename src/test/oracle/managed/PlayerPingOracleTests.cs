using System.Text.RegularExpressions;

using SourceSharp.Server;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// The scoreboard's ping against stock (plan_client_ui U3): CPlayerResource's
/// cadence and smoothing (player_resource.cpp) and UTIL_GetPlayerConnectionInfo's
/// correction and bounds (util.cpp), read from the pristine SDK.
/// </summary>
public class PlayerPingOracleTests
{
    private const string Resource = "src/game/server/player_resource.cpp";
    private const string Util = "src/game/server/util.cpp";

    private static Match Find(string relative, string pattern)
    {
        var m = Regex.Match(ValveSdk.Read(relative), pattern);
        Assert.True(m.Success, $"{relative} no longer says {pattern}; a check of nothing would pass");
        return m;
    }

    [Fact]
    public void The_ping_is_set_every_stock_number_of_thinks_a_tenth_of_a_second_apart()
    {
        int every = int.Parse(Find(Resource, @"if\s*\(\s*!\s*\(\s*m_nUpdateCounter\s*%\s*(\d+)\s*\)\s*\)").Groups[1].Value);
        Find(Resource, @"SetNextThink\(\s*gpGlobals->curtime\s*\+\s*0\.1f\s*\)");
        Assert.True(PlayerPing.ThinksPerUpdate == every, $"stock sets pings every {every} thinks; the port every {PlayerPing.ThinksPerUpdate}");
        Assert.True(PlayerResource.ThinkInterval == 0.1f, $"stock thinks every 0.1 s; the port every {PlayerResource.ThinkInterval}");
        Assert.False(PlayerPing.IsUpdate(every - 1));
        Assert.True(PlayerPing.IsUpdate(every));
    }

    [Fact]
    public void The_ping_is_smoothed_as_stock_smooths_it()
    {
        var m = Find(Resource, @"ping\s*=\s*([0-9.]+)f\s*\*\s*m_iPing\.Get\(\s*iIndex\s*\)\s*\+\s*([0-9.]+)f\s*\*\s*ping\s*;");
        float keep = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        float take = float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True((PlayerPing.Keep, PlayerPing.Take) == (keep, take), $"stock keeps {keep} and takes {take}; the port {PlayerPing.Keep} and {PlayerPing.Take}");

        // Stock's arithmetic, over a spread of old and new pings.
        for (int old = 0; old <= 1000; old += 37)
            for (int sample = 5; sample <= 1000; sample += 41)
                Assert.Equal((int)(keep * old + take * sample), PlayerPing.Smooth(old, sample));
    }

    [Fact]
    public void A_sample_is_the_latency_corrected_and_bounded_as_stock_does()
    {
        Find(Util, @"latency\s*-=\s*\(0\.5f/nCmdRate\)\s*\+\s*TICKS_TO_TIME\(\s*1\.0f\s*\)\s*;");
        Find(Util, @"latency\s*-=\s*TICKS_TO_TIME\(\s*0\.5f\s*\)\s*;");
        var bounds = Find(Util, @"ping\s*=\s*clamp\(\s*ping\s*,\s*(\d+)\s*,\s*(\d+)\s*\)\s*;");
        var stockBounds = (int.Parse(bounds.Groups[1].Value), int.Parse(bounds.Groups[2].Value));
        Assert.True((PlayerPing.Min, PlayerPing.Max) == stockBounds, $"stock bounds {stockBounds}");

        const float tick = 0.015f;
        // 80 ms over the wire at cl_cmdrate 66: 80 - 7.58 - 15 - 7.5 = 49.9 -> 49.
        Assert.Equal(49, PlayerPing.Sample(0.080f, 66, tick));
        // A loopback client's few ms floors at 5.
        Assert.Equal(5, PlayerPing.Sample(0.002f, 66, tick));
        Assert.Equal(1000, PlayerPing.Sample(5f, 66, tick));
        // A cmdrate the client never sent counts as 1.
        Assert.Equal(PlayerPing.Sample(1.0f, 1, tick), PlayerPing.Sample(1.0f, 0, tick));
    }
}

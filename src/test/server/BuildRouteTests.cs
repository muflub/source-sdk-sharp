/*
 * The build stamp as /status carries it.
 *
 * A test process has loaded nothing through the bootstrap, so both halves are
 * unstamped here — which is the case worth pinning, because it is the one where
 * a careless renderer would show a confident answer it does not have.
 */

using System.Text.Json;
using SourceSharp.Abi;
using Debug.Api;
using Xunit;

namespace SourceSharp.Tests;

public class BuildRouteTests
{
    private static JsonElement Status()
    {
        var server = NeverPumped.Server();

        HttpSocketServer.TryParse("GET /status HTTP/1.1\r\n\r\n", out var request);
        var reply = server.Route(request);

        return JsonDocument.Parse(reply.Body).RootElement.GetProperty("build");
    }

    [Fact]
    public void StatusCarriesBothHalves()
    {
        var build = Status();

        Assert.True(build.TryGetProperty("native", out _), "the native half is reported");
        Assert.True(build.TryGetProperty("managed", out _), "and the managed half separately, because a reload moves only one");
        Assert.False(build.TryGetProperty("abi", out _), "and no ABI version: it was dropped (2026-10-01)");
    }

    [Fact]
    public void AnUnstampedHalfSaysSoRatherThanReadingAsACleanBuild()
    {
        var managed = Status().GetProperty("managed");

        Assert.True(!managed.GetProperty("known").GetBoolean(),
            "a half nothing stamped is not known");
        Assert.True(managed.GetProperty("hash").GetString() == "", "with no hash");
        Assert.True(managed.GetProperty("at").GetInt64() == 0, "and no time");

        // The one that matters. false would say "this build has no uncommitted
        // work in it", which is a claim nobody made.
        Assert.True(managed.GetProperty("dirty").ValueKind == JsonValueKind.Null,
            "and dirtiness unknown rather than clean");
        Assert.True(managed.GetProperty("source").GetString() == "unknown",
            "so it reads as unknown and not as a branch it guessed");
    }

    [Fact]
    public void FreshnessIsAWordThePageCanSwitchOn()
    {
        var native = Status().GetProperty("native");

        Assert.True(native.GetProperty("freshness").GetString() == "unknown",
            "nothing loaded means nothing to compare against what is on disk");
    }
}

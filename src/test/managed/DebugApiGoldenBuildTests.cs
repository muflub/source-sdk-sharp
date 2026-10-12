using SourceSharp.Abi;
using Debug.Api;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Golden JSON for the build and crossings parts of /status (plan_devapi_interfaces
/// P2, group G8), written before they moved behind IDebugBuild.
/// </summary>
public partial class DebugApiGoldenBuildTests
{

    internal static readonly BuildHalf DetachedHalf = new(
        new BuildStamp("0123abcd", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1),
        new GitProvenance("", "feedface", null),
        "/game/\"odd\"\\path",
        false);

    internal const string KnownJson =
        "{\"known\":true,\"hash\":\"abc12345\",\"at\":1790942400000,\"parts\":3,\"branch\":\"debug-api-iface\"," +
        "\"commit\":\"deadbeef\",\"dirty\":true,\"source\":\"debug-api-iface@deadbeef+dirty\",\"freshness\":\"current\"," +
        "\"path\":\"/game/bin/managed\"}";

    internal const string DetachedJson =
        "{\"known\":true,\"hash\":\"0123abcd\",\"at\":1767225600000,\"parts\":1,\"branch\":\"(detached)\"," +
        "\"commit\":\"feedface\",\"dirty\":null,\"source\":\"(detached)@feedface+?\",\"freshness\":\"stale\"," +
        "\"path\":\"/game/\\\"odd\\\"\\\\path\"}";

    internal const string UnknownJson =
        "{\"known\":false,\"hash\":\"\",\"at\":0,\"parts\":0,\"branch\":\"\",\"commit\":\"\",\"dirty\":null," +
        "\"source\":\"unknown\",\"freshness\":\"unknown\",\"path\":\"\"}";

    [Fact]
    public void A_build_half_renders_exactly()
    {
        Assert.Equal(KnownJson, DebugApiJson.Half(KnownHalf, BuildFreshness.Current));
        Assert.Equal(DetachedJson, DebugApiJson.Half(DetachedHalf, BuildFreshness.Stale));
        Assert.Equal(UnknownJson, DebugApiJson.Half(BuildHalf.Unknown, BuildFreshness.Unknown));
    }

    [Fact]
    public void The_hook_and_the_shared_writer_render_the_same_halves()
    {
        // G8: the server's IDebugBuild maps the stamp, Debug.Api.Shared writes it.
        Assert.Equal(KnownJson, DebugApiServer.Half(DebugBuild.Half(KnownHalf, BuildFreshness.Current)));
        Assert.Equal(DetachedJson, DebugApiServer.Half(DebugBuild.Half(DetachedHalf, BuildFreshness.Stale)));
        Assert.Equal(UnknownJson, DebugApiServer.Half(DebugBuild.Half(BuildHalf.Unknown, BuildFreshness.Unknown)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("q\"b\\n\nr\rt\t")]
    [InlineData("\u0001\u001f\u007f é")]
    public void The_shared_quote_is_the_contracts_quote(string text)
        => Assert.Equal(DebugApiJson.Quote(text), DebugJson.Quote(text));

    [Fact]
    public void Status_carries_the_server_build_and_the_crossings_as_the_contracts_render_them()
    {
        var crossings = new Crossings();
        crossings.Count(CrossingOp.ServerFrame);   // a non-empty table, so the comparison reads one
        var build = new BuildInfo();
        build.SetManaged(KnownHalf, Array.Empty<string>());   // a known half, so the comparison reads one
        using var server = NeverPumped.Server(crossings, build);
        Assert.True(HttpSocketServer.TryParse("GET /status HTTP/1.1\r\n\r\n", out var request));
        string status = server.Route(request).Body;

        string expected = $",\"build\":{DebugApiJson.Build(build)},\"crossings\":{DebugApiJson.Crossings(crossings)},\"think\":";
        Assert.Contains(expected, status, StringComparison.Ordinal);
        Assert.Contains("\"direction\":\"inbound\"", expected, StringComparison.Ordinal);
    }
}

using System.Text.RegularExpressions;

using SourceSharp.Abi;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Golden JSON for /log (plan_devapi_interfaces P2, group G9), written before
/// the log moved behind IDebugLog. The time and sequence numbers are the only
/// values masked; every key, its order and every other value is exact.
/// </summary>
public class DebugApiGoldenLogTests
{
    private static HttpReply Get(DebugApiServer server, string path)
    {
        Assert.True(HttpSocketServer.TryParse($"GET {path} HTTP/1.1\r\n\r\n", out var request));
        return server.Route(request);
    }

    internal static string Masked(string json) =>
        Regex.Replace(Regex.Replace(json, "\"(seq|next)\":\\d+", "\"$1\":0"), "\"at\":\\d+", "\"at\":0");

    [Fact]
    public void An_unstarted_server_serves_an_empty_log()
    {
        using var server = NeverPumped.Server();
        Assert.Equal("{\"next\":0,\"lines\":[]}", Get(server, "/log").Body);
    }

    [Fact]
    public void A_started_server_serves_each_managed_line_in_order()
    {
        // The lines this composition's console prints (statics to zero: they
        // were the static Log's), which its dev API taps.
        var lines = new ConsoleLines();
        using var server = new DebugApiServer(["-debugapisocket"], TestDebugHooks.Server(lines: lines), TestDebugApiServers.OwnSocket());
        Assert.True(server.Start(), "the test server could not listen");

        lines.Line("golden: a message\n", false);
        lines.Line("golden: a \"quoted\" warning\n", true);

        string body = Get(server, "/log?max=2000").Body;
        string masked = Masked(body);

        Assert.StartsWith("{\"next\":0,\"lines\":[", masked, StringComparison.Ordinal);
        Assert.EndsWith("]}", masked, StringComparison.Ordinal);

        const string Message = "{\"seq\":0,\"at\":0,\"warning\":false,\"text\":\"golden: a message\"}";
        const string Warning = "{\"seq\":0,\"at\":0,\"warning\":true,\"text\":\"golden: a \\\"quoted\\\" warning\"}";
        Assert.Contains(Message + "," + Warning, masked, StringComparison.Ordinal);

        // Sequences are consecutive: the masking hid them, so read them back.
        var seqs = Regex.Matches(body, "\"seq\":(\\d+)").Select(m => long.Parse(m.Groups[1].Value)).ToArray();
        Assert.True(seqs.Length >= 2);
        Assert.Equal(Enumerable.Range(0, seqs.Length).Select(i => seqs[0] + i), seqs);
    }

    [Fact]
    public void The_mask_hides_only_time_and_sequence()
    {
        Assert.Equal("{\"next\":0,\"lines\":[{\"seq\":0,\"at\":0,\"warning\":true,\"text\":\"7\"}]}",
                     Masked("{\"next\":12,\"lines\":[{\"seq\":11,\"at\":1790985157985,\"warning\":true,\"text\":\"7\"}]}"));
    }
}

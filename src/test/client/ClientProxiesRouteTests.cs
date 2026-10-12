using System.Text.Json;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.MaterialProxies;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's <c>proxies</c> route (plan_client_novalve §4.4, lane
/// c5-proxies): the registered proxy names with their binds per frame, and
/// each live proxy's last output by material.
/// </summary>
public class ClientProxiesRouteTests
{
    private sealed class FixedReadings : IMaterialProxyReadings
    {
        public long UnknownBinds => 2;

        public long DroppedOps => 0;

        public IReadOnlyList<MaterialProxyReading> Read() =>
        [
            new(3, "Sine", "models/props_combine/combine_light001a", true, 120, ["Float 0 0.75", "RecordTools -1"]),
            new(4, "AnimatedTexture", "effects/tp_eyefx/tpeye3", true, 60, ["Int 1 5", "RecordTools -1"]),
        ];

        public IReadOnlyList<MaterialProxyNameReading> ReadNames() =>
        [
            new("AnimatedTexture", 1, 60, 1, 0),
            new("Sine", 1, 120, 2, 0),
        ];
    }

    private static string Answer(IMaterialProxyReadings readings, string filter = "")
    {
        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(proxies: new ClientDebugProxies(readings)));
        return client.Answer(DebugChannelQuery.Proxies, filter);
    }

    [Fact]
    public void The_proxies_answer_renders_exactly()
        => Assert.Equal(DebugApiClientGoldenTests.Golden("proxies"), Answer(new FixedReadings()));

    [Fact]
    public void A_filter_keeps_the_names_and_materials_that_contain_it()
    {
        using var doc = JsonDocument.Parse(Answer(new FixedReadings(), "tpeye"));
        var proxies = doc.RootElement.GetProperty("proxies");
        Assert.Equal(1, proxies.GetArrayLength());
        Assert.Equal("AnimatedTexture", proxies[0].GetProperty("name").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("names").GetArrayLength());
    }

    [Fact]
    public unsafe void The_route_reads_a_live_hosts_binds_and_last_output()
    {
        // The positive arm: a host that made and bound a proxy answers non-zero.
        var host = new MaterialProxyHost(new MaterialProxyHostTests.FakeExecutor(), new MaterialProxyHostTests.QueueUniform(),
                                         () => false, new StandardErrorLog());
        Assert.True(host.Init(9, "Sine", "models/x", [new ProxyKeyData("resultVar", 1, "$r", 0, 0, 0, 0, 0, 0)]));
        MaterialProxyHostTests.Bind(host, 9, 0.5f, 1, new ProxyVar { Type = (int)MaterialVarType.Float }, frame: 1);
        MaterialProxyHostTests.Bind(host, 9, 0.6f, 1, new ProxyVar { Type = (int)MaterialVarType.Float }, frame: 2);

        using var doc = JsonDocument.Parse(Answer(host));
        var root = doc.RootElement;
        Assert.Equal(47, root.GetProperty("registered").GetInt32()); // with viewpostprocess.cpp's MotionBlur and engine_post (C7), particle_proxies.cpp's ParticleSphereProxy
        var sine = root.GetProperty("names").EnumerateArray().Single(n => n.GetProperty("name").GetString() == "Sine");
        Assert.Equal(2, sine.GetProperty("binds").GetInt32());
        Assert.Equal(1, sine.GetProperty("bindsLastFrame").GetInt32());
        var proxy = root.GetProperty("proxies")[0];
        Assert.Equal("models/x", proxy.GetProperty("material").GetString());
        Assert.StartsWith("Float 0 ", proxy.GetProperty("lastOutput")[0].GetString(), StringComparison.Ordinal);
    }
}

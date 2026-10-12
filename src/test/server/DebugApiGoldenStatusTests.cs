using System.Text.RegularExpressions;

using SourceSharp.Abi;
using Debug.Api;
using SourceSharp.Server;
using SourceSharp.Server.Interface;

using Xunit;

using static SourceSharp.Tests.DebugApiGolden;

namespace SourceSharp.Tests;

/// <summary>
/// Golden JSON for /status, /services and the current map (plan_devapi_interfaces
/// P2, group G7), written before they moved behind IDebugStatus. The status
/// counters, the game-thread part and the reload tallies are all set to known
/// values; only the process id, the clock and the build and crossings parts
/// (G8's, compared against their own renderer) are taken as found.
/// </summary>
public class DebugApiGoldenStatusTests
{
    // This class's own side state (statics to zero): xUnit builds one
    // instance per fact, so no fact sees another's.
    private readonly ReloadStore _reload = new(Side.Server);

    internal static readonly ServiceInfo[] Services =
    [
        new("IServiceFixture", "type", "instance", "ServiceFixture", true),
        new("cs_test_value", "name", "instance", "TestConVar", true),
        new("ILazy\"Fixture", "type", "lazy", "LazyFixture", false),
    ];

    internal const string ServicesByType =
        "{\"services\":[{\"key\":\"IServiceFixture\",\"lookedUpBy\":\"type\",\"kind\":\"instance\"," +
        "\"implementation\":\"ServiceFixture\",\"built\":true},{\"key\":\"ILazy\\\"Fixture\",\"lookedUpBy\":\"type\"," +
        "\"kind\":\"lazy\",\"implementation\":\"LazyFixture\",\"built\":false}],\"count\":2}";

    internal const string ServicesByName =
        "{\"services\":[{\"key\":\"cs_test_value\",\"lookedUpBy\":\"name\",\"kind\":\"instance\"," +
        "\"implementation\":\"TestConVar\",\"built\":true}],\"count\":1}";

    internal static readonly ReloadStore.ReloadTally Saved = new(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
    internal static readonly ReloadStore.ReloadTally Restored = new(11, 12, 13, 14, 15, 16, 17, 18, 19, 20);

    /// <summary>Everything in /status after the crossings, with the waiting counts read as they are.</summary>
    internal string CountersJson() =>
        "\"think\":{\"pending\":1,\"heapDepth\":2,\"ranLastFrame\":3,\"ranTotal\":4}" +
        ",\"io\":{\"pending\":5,\"connections\":6,\"deliveredLastFrame\":7,\"deliveredTotal\":8,\"unhandled\":9," +
        "\"cyclesRefused\":10,\"unhandledDistinct\":11,\"postedAfterThePass\":12,\"sdkAsks\":13,\"sdkTook\":14}" +
        ",\"touch\":{\"pairs\":15,\"links\":16,\"edgesLastFrame\":17,\"edgesTotal\":18,\"capacity\":19,\"regrows\":20,\"refused\":21}" +
        ",\"mapSpawn\":{\"blocks\":22,\"managedBlocks\":23,\"nativeBlocks\":24,\"managedKeys\":25,\"createCrossings\":26,\"spawnCrossings\":27}" +
        ",\"factories\":{\"available\":true,\"native\":28,\"refused\":29,\"refusalTimes\":30,\"unanswered\":31}" +
        ",\"logical\":{\"classes\":32,\"live\":33,\"allocated\":34,\"released\":35,\"exhausted\":36,\"nativeReach\":37}" +
        ",\"reload\":{\"saved\":{\"entities\":1,\"classes\":2,\"fields\":3,\"bytes\":4,\"parts\":5,\"partFields\":6," +
        "\"partBytes\":7,\"ioEvents\":8,\"ioConnections\":9,\"shellLess\":10}" +
        ",\"restored\":{\"entities\":11,\"classes\":12,\"fields\":13,\"bytes\":14,\"parts\":15,\"partFields\":16," +
        "\"partBytes\":17,\"ioEvents\":18,\"ioConnections\":19,\"shellLess\":20}" +
        ",\"failed\":{\"entities\":2,\"first\":\"prop_\\\"bad\\\"\"}" +
        $",\"waiting\":{{\"entities\":{ReloadCarrier.Count(_reload)},\"parts\":{ReloadCarrier.PartCount(_reload)}," +
        $"\"io\":{ReloadCarrier.IoCount(_reload)},\"shellLess\":{ReloadCarrier.ShellLessCount(_reload)}}}}}" +
        ",\"overlay\":{\"drawn\":38,\"clipped\":39,\"noViewer\":40,\"noOverlay\":41,\"viewerSet\":true}" +
        ",\"quit\":null";

    internal const string GameJson =
        ",\"ticking\":true,\"time\":0,\"map\":\"golden_map\",\"ticks\":42,\"constraints\":43,\"springs\":44,\"motionControllers\":45";

    /// <summary>Sets this class's reload tallies to known values for the body; the store is the fact's own.</summary>
    internal void WithTallies(Action body)
    {
        _reload.NoteSaved(Saved);
        _reload.NoteRestored(Restored);
        _reload.NoteRestoreFailures(2, "prop_\"bad\"");
        body();
    }

    internal static string MaskTime(string json) => Regex.Replace(json, "\"time\":[-0-9.]+", "\"time\":0");

    // This class's own crossing counters (statics to zero), rendered by the
    // server it builds and by the reference renderer alike.
    private readonly Crossings _crossings = new();
    private readonly BuildInfo _build = new();

    private DebugApiServer Server()
    {
        // G7: the same values through the server's IDebugStatus.
        var counters = new DebugCounters(
            new DebugThink(1, 2, 3, 4), new DebugIo(5, 6, 7, 8, 9, 10, 11, 12, 13, 14),
            new DebugTouch(15, 16, 17, 18, 19, 20, 21), new DebugMapSpawn(22, 23, 24, 25, 26, 27),
            new DebugFactories(true, 28, 29, 30, 31), new DebugLogical(32, 33, 34, 35, 36, 37),
            new DebugOverlayCounts(38, 39, 40, 41, true));

        return DebugApiServer.ForTest(TestDebugHooks.Server(
            crossings: _crossings,
            build: _build,
            reload: _reload,
            counters: () => counters,
            game: () => new DebugGameStatus("golden_map", 42, 43, 44, 45),
            services: () => Services,
            map: () => "golden_map"));
    }

    [Fact]
    public void Status_renders_exactly()
    {
        WithTallies(() =>
        {
            using var server = Server();
            string body = MaskTime(Pumped(server, "/status").Body);

            string expected =
                $"{{\"ok\":true,\"pid\":{Environment.ProcessId},\"address\":\"(not listening)\"," +
                $"\"build\":{DebugApiJson.Build(_build)},\"crossings\":{DebugApiJson.Crossings(_crossings)}," +
                CountersJson() + ",\"consoleLinesLost\":0" + GameJson + "}";
            Assert.Equal(expected, body);
        });
    }

    [Fact]
    public void Status_without_a_game_thread_says_it_is_not_ticking()
    {
        WithTallies(() =>
        {
            using var server = DebugApiServer.ForTest(TestDebugHooks.Server(),
                                                    NeverPumped.Wait);
            Assert.EndsWith(",\"ticking\":false}", Get(server, "/status").Body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Services_render_exactly_by_type_name_and_all()
    {
        using var server = Server();
        Assert.Equal(ServicesByType, Get(server, "/services").Body);
        Assert.Equal(ServicesByName, Get(server, "/services?by=name").Body);
        Assert.Contains("\"count\":3}", Get(server, "/services?by=all").Body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_current_map_is_the_one_the_server_names()
    {
        using var server = Server();
        Assert.StartsWith("{\"current\":\"golden_map\",", Get(server, "/map").Body, StringComparison.Ordinal);
        Assert.Contains("\"map\":\"golden_map\"", Get(server, "/map/entities").Body, StringComparison.Ordinal);
    }
}

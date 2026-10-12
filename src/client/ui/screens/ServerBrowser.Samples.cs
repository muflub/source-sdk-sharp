using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

public static class ServerBrowserSamples
{
    /// <summary>The browser from fakes; windows placed from <paramref name="store"/> (empty: the default size).</summary>
    public static UiScreen Build(UiSampleServices s, FakeServerBrowser browser, MemoryWindowStore? store = null)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(store ?? new MemoryWindowStore());
        return browser.Register(s).Create<ServerBrowser>();
    }

    /// <summary>The 2,000-server list, sorted by ping, with the fastest selected.</summary>
    public static FakeServerBrowser TwoThousand()
    {
        var servers = FakeServerBrowser.Many(2000);
        int fastest = 0;
        for (int i = 1; i < servers.Length; i++)
            if (servers[i].Ping < servers[fastest].Ping) fastest = i;
        return new FakeServerBrowser { Servers = servers, Selected = fastest, Players = FakeServerBrowser.SomePlayers(), MaxPing = 150 };
    }

    [UiSample("2,000 servers by ping")]
    private static UiScreen Many(UiSampleServices s) => Build(s, TwoThousand());

    [UiSample("filtered to dm_, not empty")]
    private static UiScreen Filtered(UiSampleServices s)
    {
        var all = FakeServerBrowser.Many(2000);
        return Build(s, new FakeServerBrowser { Servers = all, MapContains = "dm_", NotEmpty = true, Selected = 7, Players = FakeServerBrowser.SomePlayers() });
    }

    [UiSample("LAN, empty")]
    private static UiScreen Lan(UiSampleServices s) => Build(s, new FakeServerBrowser { Tab = ServerTab.Lan });

    [UiSample("refreshing")]
    private static UiScreen Refreshing(UiSampleServices s)
        => Build(s, new FakeServerBrowser { Servers = FakeServerBrowser.Many(640, seed: 9), Refreshing = true, RefreshProgress = 0.32f });

    [UiSample("dragged to a corner")]
    private static UiScreen Corner(UiSampleServices s)
        => Build(s, TwoThousand(), new MemoryWindowStore().With("serverbrowser", new UiWindowPlacement(0, 0, 1100, 640)));

    [UiSample("resized to its minimum")]
    private static UiScreen Minimum(UiSampleServices s)
        => Build(s, TwoThousand(), new MemoryWindowStore().With("serverbrowser", new UiWindowPlacement(200, 120, 10, 10)));

    [UiSample("resized large")]
    private static UiScreen Large(UiSampleServices s)
        => Build(s, TwoThousand(), new MemoryWindowStore().With("serverbrowser", new UiWindowPlacement(0, 0, 100000, 100000)));

    [UiSample("over the console")]
    private static UiScreen OverConsole(UiSampleServices s)
    {
        var store = new MemoryWindowStore()
            .With("console", new UiWindowPlacement(60, 60, 1000, 560))
            .With("serverbrowser", new UiWindowPlacement(560, 300, 1200, 700));
        var console = ConsoleSamples.Build(s, ConsoleSamples.Busy(), store);
        var browser = Build(s, TwoThousand());
        // Described console first, browser second: the browser is on top and focused.
        return new UiScreenGroup("serverbrowser", console, browser);
    }
}

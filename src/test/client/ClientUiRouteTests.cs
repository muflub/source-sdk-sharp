using System.Text.Json;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Shared;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;
using SourceSharp.Tests.Ui;
using Debug.Api;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// /client/{slot}/ui and /client/{slot}/ui/fonts (plan_client_ui U2): the UI
/// host's last frame, through the real hook and the channel's JSON.
/// </summary>
public class ClientUiRouteTests
{
    private static UiHost Host(UiFidelityTests.RecordingSurface surface)
    {
        var screen = new TestScreen("probe", (ref UiFrame ui) =>
        {
            using (ui.Panel().At(UiAnchor.Centre))
                ui.Data("probe text", Text.Body);
        }, UiLayerKind.Hud);
        return UiHostKit.Build(surface, [screen]);
    }

    private static JsonDocument Ask(UiHost host, DebugChannelQuery query, string argument = "")
    {
        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(ui: new ClientDebugUi(host)));
        return JsonDocument.Parse(client.Answer(query, argument));
    }

    [Fact]
    public void Before_a_paint_the_route_says_nothing_was_built()
    {
        using var doc = Ask(Host(new UiFidelityTests.RecordingSurface()), DebugChannelQuery.Ui);
        Assert.False(doc.RootElement.GetProperty("built").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("draws").GetArrayLength());
    }

    [Fact]
    public void After_a_paint_the_route_lists_the_ledger_and_its_counts()
    {
        var host = Host(new UiFidelityTests.RecordingSurface());
        host.Paint(0, 1920, 1080);

        using var doc = Ask(host, DebugChannelQuery.Ui);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("built").GetBoolean());
        Assert.Equal((1920, 1080, 1L), (root.GetProperty("w").GetInt32(), root.GetProperty("h").GetInt32(), root.GetProperty("frames").GetInt64()));
        var draws = root.GetProperty("draws").EnumerateArray().ToArray();
        Assert.Contains(draws, d => d.GetProperty("kind").GetString() == "Text" && d.GetProperty("text").GetString() == "probe text");
        Assert.Contains(draws, d => d.GetProperty("kind").GetString() == "Fill");
        Assert.Equal(draws.Length, root.GetProperty("kinds").EnumerateObject().Sum(k => k.Value.GetInt32()));
        Assert.False(root.TryGetProperty("svg", out _));
    }

    [Fact]
    public void Asked_for_svg_the_route_carries_the_frame_s_render_list()
    {
        var host = Host(new UiFidelityTests.RecordingSurface());
        host.Paint(0, 1280, 720);

        using var doc = Ask(host, DebugChannelQuery.Ui, "1");
        string svg = doc.RootElement.GetProperty("svg").GetString()!;
        Assert.StartsWith("<svg", svg);
        Assert.Contains("probe text", svg);
    }

    [Fact]
    public void A_menu_layer_paint_does_not_draw_the_hud()
    {
        var host = Host(new UiFidelityTests.RecordingSurface());
        host.Paint((int)UiLayerKind.Menu, 1920, 1080);
        Assert.Equal(0, host.Cost.Frames);
    }

    [Fact]
    public void The_fonts_route_lists_each_font_made_with_its_measures()
    {
        var host = Host(new UiFidelityTests.RecordingSurface());
        host.Paint(0, 1920, 1080);

        using var doc = Ask(host, DebugChannelQuery.UiFonts);
        var fonts = doc.RootElement.GetProperty("fonts").EnumerateArray().ToArray();
        Assert.NotEmpty(fonts);
        var f = fonts[0];
        Assert.Equal((20, 15), (f.GetProperty("tall").GetInt32(), f.GetProperty("ascent").GetInt32()));
        Assert.Equal(95, f.GetProperty("advances").GetArrayLength());
        Assert.True(f.GetProperty("handle").GetInt32() > 0);
    }

    [Fact]
    public void A_sweep_makes_every_role_at_every_preview_size_first()
    {
        var host = Host(new UiFidelityTests.RecordingSurface());
        host.Paint(0, 1920, 1080);

        using var before = Ask(host, DebugChannelQuery.UiFonts);
        using var after = Ask(host, DebugChannelQuery.UiFonts, "1");
        int made = before.RootElement.GetProperty("fonts").GetArrayLength();
        int swept = after.RootElement.GetProperty("fonts").GetArrayLength();

        Assert.True(swept > made && swept >= 20, $"{made} fonts before the sweep, {swept} after");
    }

    [Fact]
    public void The_menu_route_lists_the_open_screens_and_an_event_reaches_the_layer()
    {
        var surface = new UiFidelityTests.RecordingSurface();
        var probe = new TestScreen("probe", (ref UiFrame ui) =>
        {
            using (ui.Panel().At(UiAnchor.Centre))
                ui.Data("menu probe", Text.Body);
        });
        var host = UiHostKit.Build(surface, [], [probe]);
        host.Open("probe");
        host.Paint(1, 1280, 720);

        using (var doc = Ask(host, DebugChannelQuery.UiMenu))
        {
            Assert.Equal(["probe"], doc.RootElement.GetProperty("open").EnumerateArray().Select(e => e.GetString()));
            Assert.True(doc.RootElement.GetProperty("menuInput").GetBoolean());
            Assert.Contains(doc.RootElement.GetProperty("draws").EnumerateArray(), d => d.TryGetProperty("text", out var t) && t.GetString() == "menu probe");
        }

        using (var ev = Ask(host, DebugChannelQuery.UiEvent, $"{(int)UiInputKind.Key},0,0,{(int)UiKey.Escape}"))
            Assert.Equal(1, ev.RootElement.GetProperty("inputs").GetInt64());
        Assert.Equal(1, host.Inputs);

        using var bad = Ask(host, DebugChannelQuery.UiEvent, "nonsense");
        Assert.True(bad.RootElement.TryGetProperty("error", out _));
    }
}

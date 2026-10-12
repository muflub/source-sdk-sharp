using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>What the console and the server browser show, from their samples' data.</summary>
public class UiConsoleBrowserTests
{
    private static readonly UiFontLibrary Fonts = new();

    private static UiContext Render(UiScreen screen, int w = 1920, int h = 1080, double now = 100.0)
    {
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, screen, UiSamples.Screen(w, h), now);
        return ctx;
    }

    private static UiScreen Sample(string group, string slug)
        => PreviewRun.AllSamples().Single(s => !s.Gallery && s.Group == group && s.Slug == slug).Create();

    private static int Find(UiContext ctx, string name) => Array.FindIndex(UiTestKit.Elements(ctx), e => UiTestKit.NameOf(ctx, e) == name);

    // ---- console ----

    [Fact]
    public void The_log_follows_its_end_and_colours_each_line_by_severity()
    {
        var log = FakeConsole.BusyLog();
        var ctx = Render(Sample("console", "busy-log"));
        string last = $"\"{log[^1].Text}\"";
        int i = Find(ctx, last);
        Assert.True(i >= 0, "the last line is laid out: the log starts at its end");
        Assert.Equal(-1, Find(ctx, $"\"{log[0].Text}\""));     // the first is scrolled away (virtual)

        var tones = new Dictionary<ConsoleSeverity, Tone>
        {
            [ConsoleSeverity.Message] = Tone.Text, [ConsoleSeverity.Warning] = Tone.Warning,
            [ConsoleSeverity.Error] = Tone.Danger, [ConsoleSeverity.Developer] = Tone.TextMuted, [ConsoleSeverity.Echo] = Tone.Accent,
        };
        int seen = 0;
        foreach (var line in log)
        {
            int e = Find(ctx, $"\"{line.Text}\"");
            if (e < 0) continue;
            Assert.Equal(tones[line.Severity], ctx.Report.TextToneOf(e));
            seen++;
        }
        Assert.True(seen >= 10, $"{seen} lines checked");
    }

    [Fact]
    public void Autocomplete_opens_under_the_input_with_the_selected_suggestion_raised()
    {
        var ctx = Render(Sample("console", "autocomplete-open"));
        var els = UiTestKit.Elements(ctx);
        var field = UiTestKit.Named(ctx, "field").Rect;
        var first = els[Find(ctx, "\"cl_interp\"")].Rect;
        Assert.True(first.Y > field.Y + field.H, $"suggestions {first} under the field {field}");
        int sel = Find(ctx, "\"cl_interp_ratio\"");
        Assert.Equal(Tone.Accent, ctx.Report.TextToneOf(sel));
        Assert.Equal(Tone.Text, ctx.Report.TextToneOf(Find(ctx, "\"cl_interp\"")));
        Assert.True(Find(ctx, "\"6\"") >= 0, "the match count");
    }

    [Fact]
    public void The_input_shows_its_caret_and_selection_and_history_position()
    {
        var ctx = Render(Sample("console", "autocomplete-open"));
        int text = Find(ctx, "\"cl_inter\"");
        var all = ctx.List.AsDrawList().Quads.ToArray();
        var quads = ctx.List.AsDrawList().Ops.ToArray().Where(o => o.Kind == UiPrimitiveKind.Quad && o.Element == text).Select(o => all[o.Index]).ToArray();
        Assert.Equal(2, quads.Length);                     // selection and caret (the caret's half-second is on at t=100)
        Assert.True(quads[0].Rect.W > quads[1].Rect.W);    // "inter" selected, then the thin caret
        Assert.True(Find(Render(Sample("console", "busy-log")), "\"4 / 6\"") >= 0, "history 4 of 6");
        Assert.True(Find(Render(Sample("console", "empty-console")), "\"#SS_UI_Console_Empty\"") < 0, "the empty hint is localised");
    }

    // ---- server browser ----

    private static string[] ShownMaps(UiContext ctx, FakeServerBrowser b, out int[] pings)
    {
        var els = UiTestKit.Elements(ctx);
        var rows = Enumerable.Range(0, els.Length).Where(i => UiTestKit.NameOf(ctx, els[i]).StartsWith("Row[", StringComparison.Ordinal)).ToArray();
        var maps = new List<string>();
        var ps = new List<int>();
        foreach (int r in rows)
        {
            var names = Enumerable.Range(r + 1, 40).Where(i => i < els.Length && els[i].Kind == UiElementKind.Text)
                .Select(i => UiTestKit.NameOf(ctx, els[i]).Trim('"')).ToList();
            maps.Add(names.First(n => b.Servers.Any(s => s.Map == n)));
            ps.Add(int.Parse(names.First(n => int.TryParse(n, out _) && !n.Contains(' ')), System.Globalization.CultureInfo.InvariantCulture));
        }
        pings = [.. ps];
        return [.. maps];
    }

    [Fact]
    public void The_list_is_sorted_by_ping_and_lays_out_only_a_screenful_of_two_thousand()
    {
        var b = ServerBrowserSamples.TwoThousand();
        var ctx = Render(ServerBrowserSamples.Build(new UiSampleServices(), b));
        ShownMaps(ctx, b, out int[] pings);
        Assert.InRange(pings.Length, 10, 40);
        Assert.Equal(pings.OrderBy(p => p), pings);
        Assert.Equal(b.Servers.Where(s => s.Ping <= 150).Min(s => s.Ping), pings[0]);
        Assert.True(Find(ctx, $"\"{b.Servers.Count(s => s.Ping <= 150)}\"") >= 0, "the shown count");
    }

    [Fact]
    public void The_filters_keep_only_dm_maps_with_players()
    {
        var all = FakeServerBrowser.Many(2000);
        var b = new FakeServerBrowser { Servers = all, MapContains = "dm_", NotEmpty = true };
        var ctx = Render(ServerBrowserSamples.Build(new UiSampleServices(), b));
        var maps = ShownMaps(ctx, b, out _);
        Assert.NotEmpty(maps);
        Assert.All(maps, m => Assert.Contains("dm_", m));
        int expected = all.Count(s => s.Map.Contains("dm_") && s.Players > 0);
        Assert.True(Find(ctx, $"\"{expected}\"") >= 0, $"shown count {expected}");
    }

    [Fact]
    public void An_empty_lan_tab_says_so_and_a_refresh_shows_its_progress()
    {
        var lan = Render(Sample("serverbrowser", "lan-empty"));
        Assert.True(Find(lan, "\"NO SERVERS HERE\"") >= 0);
        Assert.Equal(-1, Find(lan, "rows"));
        var refreshing = Render(Sample("serverbrowser", "refreshing"));
        Assert.True(Find(refreshing, "\"32%\"") >= 0);
        Assert.Equal(-1, Find(Render(Sample("serverbrowser", "2-000-servers-by-ping")), "\"32%\""));
    }

    [Fact]
    public void The_details_show_the_selected_server_and_its_players()
    {
        var b = ServerBrowserSamples.TwoThousand();
        var ctx = Render(ServerBrowserSamples.Build(new UiSampleServices(), b));
        Assert.True(Find(ctx, $"\"{b.Servers[b.Selected].Name}\"") >= 0, "the name, in its own case (data keeps its case)");
        Assert.True(Find(ctx, "\"Gordon\"") >= 0);
    }
}

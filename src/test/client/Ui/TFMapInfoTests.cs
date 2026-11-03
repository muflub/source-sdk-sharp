using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// TF2's map info menu (stage 3.3): stock's layout — the map name, game
/// type, briefing text and menu photo at mapinfomenu.res's spots — with
/// the BACK/CONTINUE arms recording through
/// <see cref="ITFMapInfoActions"/> (CTFMapInfoMenu::OnCommand,
/// tf_mapinfomenu.cpp:257-327): no team yet draws BACK and reads
/// CONTINUE (CheckBackContinueButtons, :230-250); on a team the BACK is
/// gone and the gate reads CLOSE.
/// </summary>
public sealed class TFMapInfoTests
{
    private static UiContext Drawn(UiScreen screen)
    {
        var ctx = Context();
        Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
        return ctx;
    }

    private static int TextIndex(UiContext ctx, string token)
    {
        var els = Elements(ctx);
        for (int i = 0; i < els.Length; i++)
            if (RunText(ctx, i) == token)
                return i;
        return -1;
    }

    private static void ClickText(UiContext ctx, UiScreen screen, string token)
    {
        int i = TextIndex(ctx, token);
        Assert.True(i >= 0, $"no element drew {token}");
        var r = Elements(ctx)[i].Rect;
        float x = r.X + r.W / 2, y = r.Y + r.H / 2;
        ctx.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
        ctx.Input(new UiInputEvent(UiInputKind.MouseUp, x, y, (int)UiKey.MouseLeft, '\0', 0));
        Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui));
    }

    private static UiContext DrawnOf(string slug, IUiTextureLibrary? lib = null)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tf2mapinfo" && s.Slug == slug);
        var ctx = Context();
        Frame(ctx, UiTestKit.Screen(1920, 1080),
              (ref UiFrame ui) => (lib is null ? sample.Create() : sample.Create(lib)).Describe(ref ui));
        return ctx;
    }

    /// <summary>The briefing page: the map's name, its game type and the
    /// briefing lines all draw at their stock spots.</summary>
    [Fact]
    public void The_briefing_page_names_the_map_its_type_and_its_briefing()
    {
        var ctx = DrawnOf("the-briefing", new TFClassMenuArtTests.RecordingLibrary());
        var texts = new List<string>();
        for (int i = 0; i < Elements(ctx).Length; i++)
            texts.Add(RunText(ctx, i));
        Assert.Contains("2FORT", texts);
        Assert.Contains("Capture the Flag", texts);
        // The briefing wraps, so its rows are word runs, not the source
        // lines; any row carrying a word proves the text drew.
        Assert.Contains(texts, t => t.Contains("bridge"));
        Assert.Contains(texts, t => t.Contains("defending"));
    }

    /// <summary>Stock's gate (CheckBackContinueButtons, :230-240): no team
    /// yet — BACK shows and CONTINUE records; on a team — no BACK, and
    /// the gate reads CLOSE (:241-250).</summary>
    [Fact]
    public void The_back_and_continue_labels_follow_the_team_gate()
    {
        var none = DrawnOf("the-briefing");
        Assert.True(TextIndex(none, "#TF2_MapInfo_Back") >= 0);
        Assert.True(TextIndex(none, "#TF2_MapInfo_Continue") >= 0);
        Assert.Equal(-1, TextIndex(none, "#TF2_MapInfo_Close"));

        var team = DrawnOf("on-a-team");
        Assert.Equal(-1, TextIndex(team, "#TF2_MapInfo_Back"));
        Assert.True(TextIndex(team, "#TF2_MapInfo_Close") >= 0);
        Assert.Equal(-1, TextIndex(team, "#TF2_MapInfo_Continue"));
    }

    /// <summary>Stock's one click arms: each gate records once, and the
    /// hover frame adds no second.</summary>
    [Fact]
    public void The_gates_record_their_click_once()
    {
        var actions = new FakeTFMapInfoActions();
        var screen = new TFMapInfo(new FakeTFMapInfoSource { MapName = "2FORT" },
                                   new TFClassMenuArtTests.RecordingLibrary(), actions);
        var ctx = Drawn(screen);
        Assert.Equal(0, actions.Continues);                   // before: unpressed

        ClickText(ctx, screen, "#TF2_MapInfo_Continue");
        Assert.Equal(1, actions.Continues);                   // after: once
        Assert.Equal(0, actions.Backs);

        ClickText(ctx, screen, "#TF2_MapInfo_Back");
        Assert.Equal(1, actions.Backs);
        Assert.Equal(1, actions.Continues);
    }

    /// <summary>A map with neither photo nor briefing draws its name and
    /// nothing else (stock's blank page, :344-346, :371-377).</summary>
    [Fact]
    public void An_unknown_map_draws_its_name_and_neither_photo_nor_briefing()
    {
        var ctx = DrawnOf("an-unknown-map");
        Assert.True(Has(ctx, "map title"));
        Assert.False(Has(ctx, "map photo"));
        Assert.False(Has(ctx, "briefing"));
    }

    private static bool Has(UiContext ctx, string name)
        => Elements(ctx).Any(e => NameOf(ctx, e) == name);
}

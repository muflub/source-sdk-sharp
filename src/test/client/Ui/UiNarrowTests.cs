using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;
using Xunit.Abstractions;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// Narrow screens (plan_client_ui §5.1, the 2026-10-05 review: "skinny
/// resolution should have smaller panels laid out properly"): at every narrow
/// preview size (4:3, 5:4, portrait, and the raised-ui_scale sizes that bring
/// the design width under the breakpoint) every screen keeps everything on
/// screen with no overlap, the HUD's panels cover at most
/// <see cref="MaxHudShare"/> of the screen, and the build stamp stays clear of them.
/// </summary>
public class UiNarrowTests(ITestOutputHelper output)
{
    private static readonly UiFontLibrary Fonts = new();

    /// <summary>
    /// The most of the screen the HUD's panels (vitals, ammo, hints) may cover:
    /// 6%. Stock HL2MP's HUD boxes (HudHealth 102, HudSuit 108, HudAmmo 136,
    /// HudAmmoSecondary 60, all 36 tall, of a 640x480 layout) cover 4.8%; 6%
    /// leaves room for the hints panel, and 94% of the screen for the world.
    /// </summary>
    public const double MaxHudShare = 0.06;

    private static IEnumerable<(UiSample Sample, UiPreviewSize Size, UiContext Ctx)> NarrowRenders()
    {
        var theme = SourceSharpTheme.Create();
        var lang = UiSamplesTests.Language();
        foreach (var size in UiSamples.NarrowSizes(theme))
            foreach (var s in PreviewRun.AllSamples().Where(s => !s.Gallery))
            {
                var ctx = UiSamples.PreviewContext(theme, Fonts, lang);
                UiSamples.Render(ctx, s.Create(), size.Screen, s.Now);
                yield return (s, size, ctx);
            }
    }

    [Fact]
    public void The_narrow_sizes_include_portrait_4_3_5_4_and_a_design_width_under_the_breakpoint()
    {
        var theme = SourceSharpTheme.Create();
        var names = UiSamples.NarrowSizes(theme).Select(s => s.Name).ToList();
        Assert.Contains("1024x768", names);
        Assert.Contains("1280x1024", names);
        Assert.Contains("1080x1920", names);
        Assert.Contains("1440x2560", names);
        Assert.Contains(UiSamples.NarrowSizes(theme), s => UiScale.DesignWidth(s.Screen) < theme.NarrowBelowDu);
        Assert.DoesNotContain("1920x1080", names);
        Assert.DoesNotContain("2560x1080", names);
    }

    [Fact]
    public void Every_screen_at_every_narrow_size_is_on_screen_without_overlap()
    {
        var lint = new UiLint(SourceSharpTheme.Create(), Fonts, []);
        int renders = 0;
        var problems = new List<string>();
        foreach (var (s, size, ctx) in NarrowRenders())
        {
            renders++;
            problems.AddRange(lint.Check(ctx, $"{s.Group}/{s.Slug}@{size.Name}")
                .Where(f => f.Rule is "offscreen" or "overlap" or "clipped").Select(f => f.ToString()));
        }
        Assert.True(renders >= 17 * 5, $"{renders} renders");
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(20)));
    }

    private static List<UiRect> Under(UiContext ctx, string panelPrefix, Tone fill)
    {
        var els = UiTestKit.Elements(ctx);
        var roots = Enumerable.Range(0, els.Length).Where(i => UiTestKit.NameOf(ctx, els[i]).StartsWith(panelPrefix, StringComparison.Ordinal)).ToHashSet();
        var found = new List<UiRect>();
        for (int i = 0; i < els.Length; i++)
        {
            if (ctx.Report.FillToneOf(i) != fill || els[i].Rect.W <= 0) continue;
            for (int k = els[i].Parent; k >= 0; k = els[k].Parent)
                if (roots.Contains(k)) { found.Add(els[i].Rect); break; }
        }
        return found;
    }

    [Fact]
    public void The_huds_panels_stay_small_and_the_build_stamp_clear_of_them()
    {
        int checkedHuds = 0;
        foreach (var (s, size, ctx) in NarrowRenders().Concat(Wide()))
        {
            if (s.Group != "hud") continue;
            var panels = Under(ctx, "VitalsPanel[", Tone.Surface).Concat(Under(ctx, "AmmoPanel[", Tone.Surface))
                         .Concat(Under(ctx, "HintsPanel[", Tone.Surface)).ToList();
            if (panels.Count == 0) continue;   // the menu sample: no player
            checkedHuds++;
            double share = panels.Sum(r => (double)r.W * r.H) / (size.W * (double)size.H);
            output.WriteLine($"{s.Slug}@{size.Name}: HUD panels {share:P2} of the screen");
            Assert.True(share <= MaxHudShare, $"{s.Slug}@{size.Name}: the HUD's panels cover {share:P1} of the screen");

            float gap = 4 * size.Screen.Scale;
            foreach (var stamp in Under(ctx, "BuildStampPanel[", Tone.Surface))
                foreach (var p in panels)
                    Assert.False(stamp.X < p.X + p.W + gap && p.X < stamp.X + stamp.W + gap && stamp.Y < p.Y + p.H + gap && p.Y < stamp.Y + stamp.H + gap,
                                 $"{s.Slug}@{size.Name}: the build stamp {stamp} touches a panel {p}");
        }
        Assert.True(checkedHuds >= 7 * 6, $"{checkedHuds} HUD renders checked");
    }

    private static IEnumerable<(UiSample, UiPreviewSize, UiContext)> Wide()
    {
        var theme = SourceSharpTheme.Create();
        var lang = UiSamplesTests.Language();
        foreach (var size in UiSamples.Sizes().Except(UiSamples.NarrowSizes(theme)))
            foreach (var s in PreviewRun.AllSamples().Where(s => s.Group == "hud"))
            {
                var ctx = UiSamples.PreviewContext(theme, Fonts, lang);
                UiSamples.Render(ctx, s.Create(), size.Screen, s.Now);
                yield return (s, size, ctx);
            }
    }

    [Fact]
    public void The_scoreboard_stacks_its_teams_below_the_breakpoint()
    {
        var theme = SourceSharpTheme.Create();
        var sample = PreviewRun.AllSamples().Single(s => s.Group == "scoreboard" && s.Slug == "12-players-two-teams");
        float TeamGapY(UiPreviewSize size)
        {
            var ctx = UiSamples.PreviewContext(theme, Fonts, UiSamplesTests.Language());
            UiSamples.Render(ctx, sample.Create(), size.Screen, sample.Now);
            var tables = UiTestKit.Elements(ctx).Where(e => UiTestKit.NameOf(ctx, e).StartsWith("TeamTable[", StringComparison.Ordinal)).ToArray();
            Assert.Equal(2, tables.Length);
            return tables[1].Rect.Y - tables[0].Rect.Y;
        }
        Assert.Equal(0, TeamGapY(new UiPreviewSize(1920, 1080, 1f)));        // side by side
        Assert.True(TeamGapY(new UiPreviewSize(1080, 1920, 1.75f)) > 100);   // stacked
    }

    private static bool IsNarrow(int w, int h, float uiScale = 1f)
    {
        bool narrow = false;
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiTestKit.Frame(ctx, UiSamples.Screen(w, h, uiScale), (ref UiFrame ui) => narrow = ui.Narrow);
        return narrow;
    }

    [Fact]
    public void Portrait_and_5_4_at_ui_scale_1_use_the_narrow_arrangement_and_4_3_and_16_9_do_not()
    {
        Assert.True(IsNarrow(1080, 1920));
        Assert.True(IsNarrow(1440, 2560));
        Assert.True(IsNarrow(1280, 1024));
        Assert.False(IsNarrow(1024, 768));    // exactly 1440 du: the regular arrangement fits
        Assert.False(IsNarrow(1920, 1080));
        Assert.False(IsNarrow(2560, 1080));

        // And it shows: the portrait scoreboard stacks its teams at ui_scale 1.
        var sample = PreviewRun.AllSamples().Single(s => s.Group == "scoreboard" && s.Slug == "12-players-two-teams");
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1080, 1920), sample.Now);
        var tables = UiTestKit.Elements(ctx).Where(e => UiTestKit.NameOf(ctx, e).StartsWith("TeamTable[", StringComparison.Ordinal)).ToArray();
        Assert.True(tables[1].Rect.Y > tables[0].Rect.Y + 100, "stacked");
    }

    [Fact]
    public void Body_text_is_at_least_12_px_at_every_preview_size()
    {
        var theme = SourceSharpTheme.Create();
        foreach (var size in UiSamples.Sizes())
        {
            int px = (int)MathF.Round(theme.Type(Text.Body).SizeDu * size.Screen.Scale);
            output.WriteLine($"{size.Name}: scale {size.Screen.Scale:0.###}, design {UiScale.DesignWidth(size.Screen):0} x {UiScale.DesignHeight(size.Screen):0} du, body {px} px");
            Assert.True(px >= 12, $"{size.Name}: body text {px} px");
        }
    }
}

using SkiaSharp;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// Goldens for the exemplar screens (plan_client_ui §3.2 "Goldens", §5.8.4):
/// the SVG of each at 1920x1080 and 2560x1080 (a readable diff), and a pixel
/// golden of each drawn by SkiaBackend at 1280x720 over a flat colour (what it
/// looks like), compared with a small per-pixel tolerance. A change to either
/// is regenerated on purpose with UI_GOLDEN_WRITE=1 and reviewed.
/// </summary>
public class UiGoldenTests
{
    private static readonly UiFontLibrary Fonts = new();

    public static TheoryData<string, string> Exemplars() => new()
    {
        { "scoreboard", "12-players-two-teams" },
        { "hud", "full-health-smg" },
        { "hud", "low-health-hurt-from-the-left" },
        { "hud", "smg-2-of-3-grenades" },
        { "hud", "ar2-0-of-3-balls" },
        { "hud", "kill-feed-busy" },
        { "hud", "kill-feed-suicide-and-the-world" },
        { "console", "busy-log" },
        { "console", "autocomplete-open" },
        { "serverbrowser", "2-000-servers-by-ping" },
        { "serverbrowser", "lan-empty" },
        // The descent HUD joins the exemplars (docs/plans/descent_ui.md §6
        // step 3): its twin-orbs state is the reference picture of the lane.
        { "descenthud", "healthy-party-of-two-map-revealed" },
        // The charsheet joins as the plan's reference window screen (§6 step
        // 3: "charsheet (the reference screen; goldens first)"); mid-spend is
        // the state with numbers, buttons and the staged column all live.
        { "charsheet", "mid-spend-l18" },
    };

    private static UiContext Render(string group, string slug, int w, int h)
    {
        var sample = PreviewRun.AllSamples().Single(s => !s.Gallery && s.Group == group && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(w, h), sample.Now);
        return ctx;
    }

    [Theory]
    [MemberData(nameof(Exemplars))]
    public void The_svg_is_its_golden(string group, string slug)
    {
        foreach (var (w, h) in new[] { (1920, 1080), (2560, 1080) })
        {
            var svg = new SvgBackend(Fonts);
            svg.Draw(Render(group, slug, w, h).List.AsDrawList());
            UiGolden.Text($"{group}-{slug}-{w}x{h}.svg", svg.Svg);
        }
    }

    /// <summary>Pixels within this per-channel difference are the same pixel (antialiasing across Skia builds).</summary>
    private const int Tolerance = 12;

    /// <summary>At most this share of pixels may differ by more than the tolerance.</summary>
    private const double MaxDiffering = 0.002;

    [Theory]
    [MemberData(nameof(Exemplars))]
    public void The_pixels_are_their_golden(string group, string slug)
    {
        var ctx = Render(group, slug, 1280, 720);
        using var skia = new SkiaBackend(Fonts);
        skia.Draw(ctx.List.AsDrawList());
        string name = $"{group}-{slug}-1280x720.png";
        string path = UiGolden.PathOf(name);

        if (UiGolden.Writing)
        {
            File.WriteAllBytes(path, skia.Png());
            return;
        }

        Assert.True(File.Exists(path), $"no golden {path}: write it with UI_GOLDEN_WRITE=1 and look at it");
        using var golden = SKBitmap.Decode(path);
        Assert.NotNull(golden);
        var (differing, worst) = Compare(skia.Bitmap, golden);
        double share = differing / (double)(golden.Width * golden.Height);
        Assert.True(share <= MaxDiffering,
            $"{name}: {differing} pixels ({share:P2}) differ by more than {Tolerance} (worst {worst}); regenerate with UI_GOLDEN_WRITE=1 if the change is meant");
    }

    internal static (int Differing, int Worst) Compare(SKBitmap a, SKBitmap b)
    {
        Assert.Equal(b.Width, a.Width);
        Assert.Equal(b.Height, a.Height);
        int differing = 0, worst = 0;
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                var p = a.GetPixel(x, y);
                var q = b.GetPixel(x, y);
                int d = Math.Max(Math.Max(Math.Abs(p.Red - q.Red), Math.Abs(p.Green - q.Green)), Math.Abs(p.Blue - q.Blue));
                worst = Math.Max(worst, d);
                if (d > Tolerance) differing++;
            }
        }
        return (differing, worst);
    }

    [Fact]
    public void The_pixel_comparison_sees_a_one_tone_change()
    {
        // The stimulus reaches the subject: the accent nudged by 30 in red moves
        // more than the tolerance allows on the scoreboard's local row and bars.
        var theme = SourceSharpTheme.Create();
        var a = UiSamples.PreviewContext(theme, Fonts, UiSamplesTests.Language());
        var sample = PreviewRun.AllSamples().Single(s => s.Group == "hud" && s.Slug == "full-health-smg");
        UiSamples.Render(a, sample.Create(), UiSamples.Screen(1280, 720), sample.Now);
        using var skiaA = new SkiaBackend(Fonts);
        skiaA.Draw(a.List.AsDrawList());

        theme.Set(Tone.Accent, theme.Color(Tone.Accent) with { R = 225 });
        var b = UiSamples.PreviewContext(theme, Fonts, UiSamplesTests.Language());
        UiSamples.Render(b, sample.Create(), UiSamples.Screen(1280, 720), sample.Now);
        using var skiaB = new SkiaBackend(Fonts);
        skiaB.Draw(b.List.AsDrawList());

        var (differing, _) = Compare(skiaA.Bitmap, skiaB.Bitmap);
        Assert.True(differing / (double)(1280 * 720) > MaxDiffering, $"only {differing} pixels moved");
    }
}

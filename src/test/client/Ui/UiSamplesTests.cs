using SkiaSharp;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// Every sample renders (plan_client_ui §7.4): each [UiSample] at the five
/// sizes and each [UiGallery] entry, through the preview's own path, with no
/// exception and no element off screen. And the preview's fonts measure as
/// SkiaSharp draws them.
/// </summary>
public class UiSamplesTests
{
    private static readonly UiFontLibrary Fonts = new();

    internal static UiLanguageFile Language()
        // The same chain the preview CLI answers from: the base mod's file,
        // then every game mod's named by tools/ui-preview-assemblies.txt.
        // Unchained, descent tokens render as raw keys — different widths —
        // and the lint would judge a picture nobody will ever see.
        => UiLanguageFile.Chain(Options.LanguageFiles());

    internal static readonly PreviewOptions Options =
        new(RepoLayout.RepoRoot, Path.GetTempPath(), false, true, null);

    [Fact]
    public void Samples_are_discovered_from_both_ui_assemblies()
    {
        var all = PreviewRun.AllSamples();
        Assert.Contains(all, s => s.Gallery && s.Group == "Button");      // the toolkit's gallery
        Assert.Contains(all, s => s.Gallery && s.Group == "TeamTable");   // the screens' gallery
        Assert.True(all.Count >= 14, $"{all.Count} samples");
        Assert.Equal(all.Count, all.Select(s => (s.Group, s.Slug)).Distinct().Count());
    }

    [Fact]
    public void Every_sample_renders_at_every_size_on_screen()
    {
        var lang = Language();
        var theme = SourceSharpTheme.Create();
        int rendered = 0;
        var problems = new List<string>();
        foreach (var s in PreviewRun.AllSamples())
        {
            var sizes = s.Gallery ? [new UiPreviewSize(PreviewRun.GalleryW, PreviewRun.GalleryH, 1f)] : UiSamples.Sizes().ToArray();
            foreach (var size in sizes)
            {
                int w = size.W, h = size.H;
                var ctx = UiSamples.PreviewContext(theme, Fonts, lang);
                UiSamples.Render(ctx, s.Create(), size.Screen, s.Now);
                var list = ctx.List.AsDrawList();
                if (list.Ops.Length == 0)
                    problems.Add($"{s.Group}/{s.Slug}@{size.Name}: drew nothing");
                for (int i = 0; i < list.Elements.Length; i++)
                {
                    var e = list.Elements[i];
                    var r = e.Rect;
                    // A window is where the player put it (its clamp keeps the title reachable: UiWindowTests).
                    if (InWindow(list.Elements, ctx.Report, i))
                        continue;
                    // Inside a scroll region what is drawn is the part inside its clip.
                    if (InScroll(list.Elements, ctx.Report, i))
                    {
                        var c = ctx.Report.ClipOf(i);
                        float x0 = MathF.Max(r.X, c.X), y0 = MathF.Max(r.Y, c.Y);
                        r = new UiRect(x0, y0, MathF.Min(r.X + r.W, c.X + c.W) - x0, MathF.Min(r.Y + r.H, c.Y + c.H) - y0);
                    }
                    if (r.W > 0 && r.H > 0 && (r.X < 0 || r.Y < 0 || r.X + r.W > w || r.Y + r.H > h))
                        problems.Add($"{s.Group}/{s.Slug}@{size.Name}: {new string(list.Text.Slice(e.NameStart, e.NameLength))} off screen at {r}");
                }
                rendered++;
            }
        }
        Assert.True(rendered >= 14, $"{rendered} renders");
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(20)));
    }

    private static bool InWindow(ReadOnlySpan<UiElementInfo> els, UiFrameReport report, int i)
    {
        for (int k = i; k >= 0; k = els[k].Parent)
            if (report.IsMovable(k))
                return true;
        return false;
    }

    private static bool InScroll(ReadOnlySpan<UiElementInfo> els, UiFrameReport report, int i)
    {
        for (int k = els[i].Parent; k >= 0; k = els[k].Parent)
            if (report.IsScroll(k))
                return true;
        return false;
    }

    [Fact]
    public void The_ttf_metrics_match_what_skia_draws_with()
    {
        // An independent measure of the same file: Skia's FreeType against our
        // hmtx/cmap reader, per glyph, at the sizes the theme uses.
        foreach (var face in Fonts.Faces)
        {
            using var tf = SKTypeface.FromData(SKData.CreateCopy(face.Data));
            foreach (int px in new[] { 14, 18, 30, 46 })
            {
                using var font = new SKFont(tf, px) { Subpixel = true, Hinting = SKFontHinting.None };
                var spec = new UiFontSpec(face.Family, px, face.Weight, UiFontFlags.None);
                foreach (char c in "Aa0Wi.…_x")
                {
                    float ours = Fonts.Advance(spec, c);
                    float skia = font.MeasureText(c.ToString());
                    Assert.True(MathF.Abs(ours - skia) < 0.02f, $"{face.Family} {face.Weight} {px}px '{c}': ours {ours}, skia {skia}");
                }
            }
        }
    }
}

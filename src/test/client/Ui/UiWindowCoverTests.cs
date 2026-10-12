using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// A window fully covers the windows beneath it: nothing a lower window draws
/// may show through a higher window's surface. Checked over every sample at
/// every size: for each pair of windows, any primitive of the lower one inside
/// the higher one's rect must lie under an opaque fill of the higher one.
/// </summary>
public class UiWindowCoverTests
{
    private static readonly UiFontLibrary Fonts = new();

    /// <summary>"what shows through where", one line per primitive of a lower window visible through a higher one.</summary>
    internal static List<string> ShowThrough(UiContext ctx)
    {
        var list = ctx.List.AsDrawList();
        var els = list.Elements.ToArray();
        var report = ctx.Report;

        // The windows, in draw order, and each element's window.
        var windows = Enumerable.Range(0, els.Length).Where(report.IsMovable).ToList();
        int WindowOf(int e)
        {
            for (int k = e; k >= 0; k = els[k].Parent)
                if (report.IsMovable(k)) return k;
            return -1;
        }

        var found = new List<string>();
        var ops = list.Ops.ToArray();
        var quads = list.Quads.ToArray();
        var runs = list.GlyphRuns.ToArray();
        string text = new(list.Text);
        for (int hi = 1; hi < windows.Count; hi++)
        {
            int w = windows[hi];
            var cover = els[w].Rect;
            // The higher window's opaque fills: what hides what is beneath.
            var opaque = ops.Where(o => o.Kind == UiPrimitiveKind.Quad && o.Element >= 0 && WindowOf(o.Element) == w
                                        && quads[o.Index].Color.A == 255 && quads[o.Index].Fade == UiFade.None)
                            .Select(o => quads[o.Index].Rect).ToList();
            foreach (var op in ops)
            {
                if (op.Element < 0 || op.Kind != UiPrimitiveKind.GlyphRun) continue;
                int owner = WindowOf(op.Element);
                if (owner < 0 || windows.IndexOf(owner) >= hi) continue;
                var r = runs[op.Index];
                var font = list.Fonts[r.Font];
                float width = 0f;
                foreach (char ch in text.AsSpan(r.TextStart, r.TextLength))
                    width += Fonts.Advance(font, ch);
                float h = Fonts.Metrics(font).LineHeight;
                // Any part of the run inside the higher window, not hidden by one of its opaque fills.
                for (float x = r.X; x < r.X + width; x += 4)
                {
                    float y = r.Y + h / 2;
                    bool inside = x >= cover.X && y >= cover.Y && x < cover.X + cover.W && y < cover.Y + cover.H;
                    bool hidden = opaque.Any(q => x >= q.X && y >= q.Y && x < q.X + q.W && y < q.Y + q.H);
                    if (inside && !hidden)
                    {
                        found.Add($"\"{text.Substring(r.TextStart, r.TextLength)}\" at {x},{y} shows through {cover}");
                        break;
                    }
                }
            }
        }
        return found;
    }

    [Fact]
    public void No_lower_window_shows_through_a_higher_one_in_any_sample()
    {
        var theme = SourceSharpTheme.Create();
        var lang = UiSamplesTests.Language();
        int pairs = 0;
        var problems = new List<string>();
        foreach (var s in PreviewRun.AllSamples())
        {
            foreach (var size in s.Gallery ? [new UiPreviewSize(PreviewRun.GalleryW, PreviewRun.GalleryH, 1f)] : UiSamples.Sizes().ToArray())
            {
                var ctx = UiSamples.PreviewContext(theme, Fonts, lang);
                UiSamples.Render(ctx, s.Create(), size.Screen, s.Now);
                int windows = UiTestKit.Elements(ctx).Count((_) => true) == 0 ? 0
                    : Enumerable.Range(0, ctx.List.ElementCount).Count(ctx.Report.IsMovable);
                if (windows >= 2) pairs++;
                problems.AddRange(ShowThrough(ctx).Select(p => $"{s.Group}/{s.Slug}@{size.Name}: {p}"));
            }
        }
        Assert.True(pairs >= 1, "no sample has two windows: the check saw nothing");
        Assert.True(problems.Count == 0, $"{problems.Count} show-through(s):\n" + string.Join("\n", problems.Take(12)));
    }
}

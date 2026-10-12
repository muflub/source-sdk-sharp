using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The HUD's bottom-left corner: one opaque panel holds Diablo's stack, and
/// that panel is what gives every caption in it contrast over the world. On
/// a narrow screen the column is pinned to a fixed width; a part that keeps
/// its wide size hangs out over the world (the cast bar and the belt did, at
/// 1080x1920, ui1.75 and 1280x1024 — RevC).
/// </summary>
public class DescentHudTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void Everything_in_the_corner_column_stays_on_its_panel_at_every_size()
    {
        var lang = UiSamplesTests.Language();
        var samples = PreviewRun.AllSamples().Where(s => !s.Gallery && s.Group == "descenthud").ToList();
        Assert.True(samples.Count >= 4, $"{samples.Count} descenthud samples");
        int narrowChecked = 0;
        var problems = new List<string>();
        foreach (var sample in samples)
            foreach (var size in UiSamples.Sizes())
            {
                var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, lang);
                UiSamples.Render(ctx, sample.Create(), size.Screen, sample.Now);
                var els = UiTestKit.Elements(ctx);
                int corner = Array.FindIndex(els, e => UiTestKit.NameOf(ctx, e) == "corner");
                Assert.True(corner >= 0, $"{sample.Slug}@{size.Name}: no corner panel");
                var panel = els[corner].Rect;
                bool beltSeen = false;
                for (int i = corner + 1; i < els.Length; i++)
                {
                    if (!Under(els, i, corner) || els[i].Rect.W <= 0f)
                        continue;
                    beltSeen |= UiTestKit.NameOf(ctx, els[i]) == "belt";
                    var r = els[i].Rect;
                    if (r.X < panel.X - 0.5f || r.X + r.W > panel.X + panel.W + 0.5f
                        || r.Y < panel.Y - 0.5f || r.Y + r.H > panel.Y + panel.H + 0.5f)
                        problems.Add($"{sample.Slug}@{size.Name}: {UiTestKit.NameOf(ctx, els[i])} {r} off the panel {panel}");
                }
                // The stimulus: the belt (the widest part) is in the column.
                Assert.True(beltSeen, $"{sample.Slug}@{size.Name}: the belt is not under the corner panel");
                if (UiScale.DesignWidth(size.Screen) < SourceSharpTheme.Create().NarrowBelowDu)
                    narrowChecked++;
            }
        Assert.True(narrowChecked > 0, "no narrow size was checked");
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(12)));
    }

    [Fact]
    public void The_cast_meter_spans_its_bar_whatever_the_spell_name()
    {
        // The channel meter is read by its slope (total -> zero); a track
        // as wide as the spell's name made a short name a short meter: it
        // spanned 97 of the bar's 320 du for "Chain Lightning".
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "descenthud" && s.Slug == "interrupted-cast");
        foreach (var size in new[] { UiSamples.Screen(1920, 1080), UiSamples.Screen(1080, 1920) })
        {
            var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
            UiSamples.Render(ctx, sample.Create(), size, sample.Now);
            var els = UiTestKit.Elements(ctx);
            int cast = Array.FindIndex(els, e => UiTestKit.NameOf(ctx, e) == "cast" && e.Kind == UiElementKind.Box);
            Assert.True(cast >= 0, "no cast bar");
            float s = ctx.Screen.Scale;
            // The track: the widest thin box under the bar (the meter is 4 du tall).
            float track = Enumerable.Range(cast + 1, els.Length - cast - 1)
                .Where(i => Under(els, i, cast) && els[i].Kind == UiElementKind.Box && els[i].Rect.H <= 4 * s + 0.5f)
                .Select(i => els[i].Rect.W).DefaultIfEmpty(0f).Max();
            Assert.True(track >= 0.8f * els[cast].Rect.W, $"track {track} px of a {els[cast].Rect.W} px bar");
        }
    }

    [Fact]
    public void The_downed_overlays_party_pills_share_a_left_edge()
    {
        // The reviver's pill carries the check, so it is wider; centred row by
        // row the pills' icons and bars staggered by half that (12 du).
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "downed" && s.Slug == "being-revived-60-percent");
        var lang = UiSamplesTests.Language();
        foreach (var size in UiSamples.Sizes())
        {
            var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, lang);
            UiSamples.Render(ctx, sample.Create(), size.Screen, sample.Now);
            var pills = UiTestKit.Elements(ctx).Where(e => UiTestKit.NameOf(ctx, e).StartsWith("PartyPill[", StringComparison.Ordinal))
                                               .Select(e => e.Rect).ToArray();
            Assert.True(pills.Length >= 2, $"{size.Name}: {pills.Length} pills");
            // The stimulus: the rows differ in width (the reviver's check).
            Assert.True(pills.Max(p => p.W) - pills.Min(p => p.W) > 0.5f, $"{size.Name}: the pills are all one width");
            Assert.True(pills.Max(p => p.X) - pills.Min(p => p.X) <= 0.5f,
                        $"{size.Name}: pill left edges {string.Join(", ", pills.Select(p => p.X))}");
        }
    }

    private static bool Under(UiElementInfo[] els, int i, int ancestor)
    {
        for (int p = els[i].Parent; p >= 0; p = els[p].Parent)
            if (p == ancestor)
                return true;
        return false;
    }
}

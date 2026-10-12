using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The skill card's buy badge sits in the card's bottom-right corner as an
/// intended layer, so the lint's overlap rule does not read it: nothing but
/// this fact keeps the card's copy out from under it. The family row ran
/// under it and the badge cut "Bullet Family" to "Bullet Famil" (RevB).
/// </summary>
public class DescentSkillCardTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void No_card_text_runs_under_the_buy_badge_at_any_size()
    {
        var lang = UiSamplesTests.Language();
        int badges = 0;
        var problems = new List<string>();
        foreach (var sample in PreviewRun.AllSamples().Where(s => !s.Gallery && s.Group == "skilltree"))
            foreach (var size in UiSamples.Sizes())
            {
                var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, lang);
                UiSamples.Render(ctx, sample.Create(), size.Screen, sample.Now);
                var els = UiTestKit.Elements(ctx);
                float s = ctx.Screen.Scale;
                for (int b = 0; b < els.Length; b++)
                {
                    // The badge: a square box with a single centred shape child, laid
                    // directly on a skill card (the 200-du box).
                    var r = els[b].Rect;
                    int card = els[b].Parent;
                    if (els[b].Kind != UiElementKind.Box || MathF.Abs(r.W - r.H) > 0.5f || r.W < 24 * s || r.W > 48 * s
                        || card < 0 || MathF.Abs(els[card].Rect.W - 200 * s) > 0.5f
                        || !els.Where((e, i) => e.Parent == b).Any(e => e.Kind == UiElementKind.Shape))
                        continue;
                    badges++;
                    for (int t = 0; t < els.Length; t++)
                    {
                        if (els[t].Kind != UiElementKind.Text || !Under(els, t, card))
                            continue;
                        var q = els[t].Rect;
                        if (q.X < r.X + r.W - 0.5f && r.X < q.X + q.W - 0.5f && q.Y < r.Y + r.H - 0.5f && r.Y < q.Y + q.H - 0.5f)
                            problems.Add($"{sample.Slug}@{size.Name}: {UiTestKit.NameOf(ctx, els[t])} {q} under the badge {r}");
                    }
                }
            }
        Assert.True(badges > 0, "no buy badge was drawn: the stimulus missed");
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(12)));
    }

    private static bool Under(UiElementInfo[] els, int i, int ancestor)
    {
        for (int p = els[i].Parent; p >= 0; p = els[p].Parent)
            if (p == ancestor)
                return true;
        return false;
    }
}

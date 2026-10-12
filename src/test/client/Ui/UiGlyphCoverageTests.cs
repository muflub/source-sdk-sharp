using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// Every character a sample draws exists in the face it is drawn with. A
/// character the shipped fonts lack renders as the face's missing glyph (an
/// empty box), and no layout check sees it: the run measures fine. The skill
/// card's family separator was U+25C6, which no IBM Plex face maps; a
/// reviewer read the box as a diamond.
/// </summary>
public class UiGlyphCoverageTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void Every_drawn_character_is_in_its_font()
    {
        var lang = UiSamplesTests.Language();
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        int glyphs = 0;
        foreach (var s in PreviewRun.AllSamples())
        {
            var size = s.Gallery ? UiSamples.Screen(PreviewRun.GalleryW, PreviewRun.GalleryH) : UiSamples.Screen(1920, 1080);
            var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, lang);
            UiSamples.Render(ctx, s.Create(), size, s.Now);
            var list = ctx.List.AsDrawList();
            foreach (var run in list.GlyphRuns)
            {
                var face = Fonts.Face(list.Fonts[run.Font]);
                foreach (char c in list.Text.Slice(run.TextStart, run.TextLength))
                {
                    if (char.IsWhiteSpace(c))
                        continue;
                    glyphs++;
                    if (!face.Has(c))
                        missing.Add($"{s.Group}/{s.Slug}: U+{(int)c:X4} in {face.Family} {face.Weight}");
                }
            }
        }
        Assert.True(glyphs > 10_000, $"only {glyphs} glyphs drawn");   // the stimulus reached the fonts
        Assert.True(missing.Count == 0, string.Join("\n", missing.Take(20)));
    }
}

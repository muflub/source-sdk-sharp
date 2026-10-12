using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.UiPreview;

/// <summary>
/// The lint over every sample of every screen at every size, and every gallery
/// part (tools/ui-lint): one line per defect, exit 1 if there is any.
/// </summary>
public static class LintRun
{
    public static List<UiLintFinding> All(PreviewOptions o)
    {
        IUiFontMetrics fonts = UiRecordedFontMetrics.LoadOrFallback(new UiFontLibrary(), o.FontsFile);
        var lang = UiLanguageFile.Chain(o.LanguageFiles());
        var theme = new PreviewRun(o).Theme();
        var lint = new UiLint(theme, fonts, SkiaBackdrop.Load(o.Backdrops));
        var findings = new List<UiLintFinding>();
        foreach (var s in PreviewRun.AllSamples().Where(s => o.Only is null || s.Group == o.Only))
        {
            var sizes = s.Gallery ? [new UiPreviewSize(PreviewRun.GalleryW, PreviewRun.GalleryH, 1f)] : UiSamples.Sizes().ToArray();
            foreach (var size in sizes)
            {
                var ctx = UiSamples.PreviewContext(theme, fonts, lang);
                UiSamples.Render(ctx, s.Create(), size.Screen, s.Now);
                findings.AddRange(lint.Check(ctx, $"{(s.Gallery ? "gallery/" : "")}{s.Group}/{s.Slug}@{size.Name}"));
            }
        }
        return findings;
    }

    public static int Run(PreviewOptions o)
    {
        var findings = All(o);
        foreach (var f in findings)
            Console.WriteLine(f);
        Console.WriteLine($"ui-lint: {findings.Count} finding(s); rules: {string.Join(", ", UiLint.Rules)}");
        return findings.Count == 0 ? 0 : 1;
    }
}

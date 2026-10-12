using SkiaSharp;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.UiPreview;

/// <summary>
/// The do/don't pairs in docs/ui/authoring.md (plan_client_ui §5.8.7), drawn
/// by the real toolkit and linted by the real lint, so the guide's pictures and
/// the defects it quotes cannot drift from the code. `ui-preview --docs` writes
/// them to docs/ui/img/. The "don't" halves are defects on purpose, which is
/// why they live here and not in the gallery (the gallery is lint-clean).
/// </summary>
public static class DocExamples
{
    private sealed record Pair(string Name, UiPartPreview.Draw Dont, UiPartPreview.Draw Do, UiLayerKind Layer);

    private static IEnumerable<Pair> Pairs()
    {
        yield return new Pair("contrast",
            static (ref UiFrame ui) => ui.Data("Picked up 45 SMG rounds", Text.Body, Tone.TextMuted),
            static (ref UiFrame ui) => ui.Toast("Picked up 45 SMG rounds"),
            UiLayerKind.Hud);

        yield return new Pair("elide",
            static (ref UiFrame ui) =>
            {
                using (ui.Panel().Width(Size.Du(220)))
                    ui.Data("xXx_Headcrab_Slayer_From_Ravenholm_xXx");
            },
            static (ref UiFrame ui) =>
            {
                using (ui.Panel().Width(Size.Du(220)))
                    ui.Data("xXx_Headcrab_Slayer_From_Ravenholm_xXx").Elide();
            },
            UiLayerKind.Menu);

        yield return new Pair("tokens",
            static (ref UiFrame ui) =>
            {
                using (ui.Column(Raw.Du(5)).Fill(Raw.Color(40, 40, 90, 230)).Pad(Raw.Du(7)))
                {
                    ui.Text("Choose your side", Text.Title).Color(Raw.Color(255, 80, 200));
                    ui.Text("Pick a team to spawn", Text.Caption, Tone.TextMuted);
                }
            },
            static (ref UiFrame ui) =>
            {
                using (ui.Panel())
                {
                    ui.Text("#SS_UI_TeamMenu_Title", Text.Title);
                    ui.Text("#SS_UI_TeamMenu_Subtitle", Text.Body, Tone.TextMuted);
                }
            },
            UiLayerKind.Menu);

        yield return new Pair("alignment",
            static (ref UiFrame ui) => Two(ref ui, Raw.Du(2)),
            static (ref UiFrame ui) => Two(ref ui, Space.None),
            UiLayerKind.Hud);
    }

    /// <summary>Two HUD panels in their own layers, the second <paramref name="nudge"/> lower.</summary>
    private static void Two(ref UiFrame ui, UiLength nudge)
    {
        using (ui.Box().Size(Size.Du(520), Size.Du(120)))
        {
            using (ui.Box().Size(Size.Fill, Size.Fill).Layered())
                ui.Sub("health", new HudMeter(), new HudMeterProps("#SS_UI_Health", UiShape.Cross, 100, 100, 25, Tone.Accent));
            using (ui.Box().Size(Size.Fill, Size.Fill).Layered())
            using (ui.Box().At(UiAnchor.TopRight).Inset(Space.None, nudge))
                ui.Sub("suit", new HudMeter(), new HudMeterProps("#SS_UI_Suit", UiShape.Shield, 60, 100, 0, Tone.Accent));
        }
    }

    public static int Write(PreviewOptions o)
    {
        string dir = Path.Combine(o.Root, "docs", "ui", "img");
        Directory.CreateDirectory(dir);
        var fonts = new UiFontLibrary();
        var lang = UiLanguageFile.Chain(o.LanguageFiles());
        var theme = SourceSharpTheme.Create();
        var lint = new UiLint(theme, fonts, SkiaBackdrop.Load(o.Backdrops));
        using var backdrops = new Backdrops(o.Backdrops);
        using var skia = new SkiaBackend(fonts) { Backdrop = backdrops["busy"] };
        var report = new List<string> { "# ui-preview --docs: the lint of each docs/ui example (generated)" };

        foreach (var pair in Pairs())
        {
            foreach (var (half, draw) in new[] { ("dont", pair.Dont), ("do", pair.Do) })
            {
                var screen = new UiPartPreview($"{pair.Name}-{half}", draw, pair.Layer);
                var ctx = UiSamples.PreviewContext(theme, fonts, lang);
                UiSamples.Render(ctx, screen, UiSamples.Screen(1280, 720), 100.0);
                skia.Draw(ctx.List.AsDrawList());
                var findings = lint.Check(ctx, $"{pair.Name}-{half}");
                report.Add($"{pair.Name}-{half}: {findings.Count} finding(s)");
                report.AddRange(findings.Select(f => $"  [{f.Rule}] {f.Element} {f.Message}"));

                // A 640x300 window on the part, centred.
                using var crop = new SKBitmap(640, 300);
                using (var canvas = new SKCanvas(crop))
                {
                    using var img = SKImage.FromBitmap(skia.Bitmap);
                    canvas.DrawImage(img, new SKRect(320, 210, 960, 510), new SKRect(0, 0, 640, 300), new SKSamplingOptions(SKFilterMode.Nearest));
                }
                // JPEG: the world behind the part is a photograph; PNG was 260 kB a picture.
                using var data = crop.PeekPixels().Encode(new SKJpegEncoderOptions(88, SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore))
                    ?? throw new InvalidOperationException("JPEG encode failed");
                File.WriteAllBytes(Path.Combine(dir, $"{pair.Name}-{half}.jpg"), data.ToArray());
            }
        }

        File.WriteAllLines(Path.Combine(dir, "examples-lint.txt"), report);
        Console.WriteLine(string.Join("\n", report));
        return 0;
    }
}

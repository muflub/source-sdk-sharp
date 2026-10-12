using System.Reflection;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

using SkiaSharp;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.UiPreview;

/// <summary>Where the preview reads from and writes to.</summary>
public sealed record PreviewOptions(string Root, string Out, bool Png, bool Quiet, string? Only)
{
    public string Backdrops => Path.Combine(Root, "src", "client", "ui", "backdrops");

    public string LanguageFile => Path.Combine(Root, "game", "mod_test", "resource", "mod_sharp_english.txt");

    /// <summary>
    /// Every language file the preview answers copy from, first-wins: the
    /// mod_test file, then each mod dir named by an extra line of
    /// tools/ui-preview-assemblies.txt (its `=game/&lt;moddir&gt;`), then the
    /// core's #SS_UI_ strings (embedded in the toolkit in game). In game the
    /// same files answer through GameUiLocaliser; a chained mod that names a
    /// dir without a resource/*_english.txt is a failure, never a silent skip.
    /// </summary>
    public IReadOnlyList<string> LanguageFiles()
    {
        var files = new List<string> { LanguageFile };
        foreach (var (_, modDir) in UiAssemblyList.Load(Root))
        {
            if (modDir is null) continue;
            string dir = Path.Combine(Root, modDir, "resource");
            string[] found = Directory.Exists(dir) ? Directory.GetFiles(dir, "*_english.txt") : [];
            Array.Sort(found, StringComparer.Ordinal);
            if (!found.Any())
                throw new InvalidDataException($"{modDir} has no resource/*_english.txt to chain into the preview");
            files.AddRange(found);
        }
        files.Add(Path.Combine(Root, UiLanguageFile.CoreRepoPath));
        return files;
    }

    public string ThemeFile => Path.Combine(Root, "game", "mod_test", "resource", "ui-theme.yaml");

    /// <summary>The fonts as the game measured them (plan_client_ui U2); the TTFs answer what it does not hold.</summary>
    public string FontsFile => Path.Combine(Root, "src", "test", "client", "Fixtures", "ui-fonts.tsv");
}

/// <summary>The three world backdrops (plan_client_ui §5.8.4): bright, dark, busy dm_lockdown stills.</summary>
public sealed class Backdrops : IDisposable
{
    public static readonly string[] Names = ["busy", "dark", "bright"];

    private readonly Dictionary<string, SKBitmap> _images = new();

    public Backdrops(string dir)
    {
        foreach (var n in Names)
        {
            string path = Path.Combine(dir, n + ".jpg");
            _images[n] = SKBitmap.Decode(path) ?? throw new FileNotFoundException($"backdrop {path} is missing or unreadable", path);
        }
    }

    public SKBitmap this[string name] => _images[name];

    public void Dispose()
    {
        foreach (var b in _images.Values) b.Dispose();
    }
}

/// <summary>
/// One pass of the preview: every sample of every screen at every size, and
/// every gallery entry, to SVG + element JSON (and PNG with --png), plus a
/// manifest the web UI's Mockups tab reads.
/// </summary>
public sealed class PreviewRun
{
    private readonly PreviewOptions _o;
    private readonly UiFontLibrary _fonts = new();
    private readonly List<double> _pngMs = new();
    private readonly List<double> _drawMs = new();

    private readonly IUiFontMetrics _metrics;

    public PreviewRun(PreviewOptions options)
    {
        _o = options;
        _metrics = UiRecordedFontMetrics.LoadOrFallback(_fonts, options.FontsFile);
    }

    /// <summary>The gallery's size: one 16:9 screen.</summary>
    public const int GalleryW = 1280, GalleryH = 720;

    public static IReadOnlyList<UiSample> AllSamples()
        => UiSamples.Discover(ScreenAssemblies());

    /// <summary>The core UI assemblies plus the listed game UI assemblies,
    /// for tests that scan types (not samples) over the same set the previews
    /// draw (docs/plans/descent_ui.md §4).</summary>
    public static Assembly[] ScreenAssemblies()
        => ExtraScreenAssemblies(typeof(UiScreen).Assembly, typeof(SourceSharp.Client.Ui.Screens.ScreensAssembly).Assembly);

    /// <summary>
    /// The extra screen assemblies named by tools/ui-preview-assemblies.txt,
    /// loaded by name from beside this tool (docs/plans/descent_ui.md §1): a
    /// game's own UI assembly joins the previews, the lint and the UI tests
    /// without the core ever naming its types. A named assembly that will not
    /// load is a failure, never a silent skip.
    /// </summary>
    static Assembly[] ExtraScreenAssemblies(params Assembly[] core)
    {
        var list = new List<Assembly>(core);
        foreach (var (name, _) in UiAssemblyList.Load(RepoRoot.Find(AppContext.BaseDirectory)))
        {
            var already = list.FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.Ordinal));
            if (already is not null) continue;
            string dll = Path.Combine(AppContext.BaseDirectory, name + ".dll");
            if (!File.Exists(dll))
                throw new FileNotFoundException($"ui-preview: {name} is listed in tools/ui-preview-assemblies.txt but is not beside the tool at {dll}");
            list.Add(Assembly.LoadFrom(dll));
        }
        return [.. list];
    }

    public UiTheme Theme()
    {
        var theme = SourceSharpTheme.Create();
        if (File.Exists(_o.ThemeFile))
            UiThemeFile.Apply(theme, File.ReadAllText(_o.ThemeFile));
        return theme;
    }

    public int Run()
    {
        var total = Stopwatch.StartNew();
        Directory.CreateDirectory(_o.Out);
        var lang = UiLanguageFile.Chain(_o.LanguageFiles());
        var theme = Theme();
        using var backdrops = new Backdrops(_o.Backdrops);
        var samples = AllSamples().Where(s => _o.Only is null || s.Group == _o.Only).ToList();

        // A full run starts clean: pictures of a removed sample must not linger
        // and pass for current (the trial found one).
        if (_o.Only is null)
            foreach (var dir in new[] { "screens", "gallery" })
                if (Directory.Exists(Path.Combine(_o.Out, dir)))
                    Directory.Delete(Path.Combine(_o.Out, dir), recursive: true);
        var manifest = new object[samples.Count];
        int images = 0;

        // Samples in parallel: each has its own context and backends; fonts,
        // theme, language and backdrops are read-only. (Serial, the whole set
        // took 11.8 s; the PNG encode dominates.)
        Parallel.For(0, samples.Count, k =>
        {
            var sample = samples[k];
            using var skia = new SkiaBackend(_fonts);
            var svg = new SvgBackend(_metrics);
            var sizes = sample.Gallery ? [new UiPreviewSize(GalleryW, GalleryH, 1f)] : UiSamples.Sizes().ToArray();
            var files = new Dictionary<string, object>();
            UiLayerKind layer = UiLayerKind.Menu;
            foreach (var size in sizes)
            {
                int w = size.W, h = size.H;
                var screen = sample.Create();
                layer = screen.Layer;
                var ctx = UiSamples.PreviewContext(theme, _metrics, lang);
                UiSamples.Render(ctx, screen, size.Screen, sample.Now);

                string dir = Path.Combine(_o.Out, sample.Gallery ? "gallery" : "screens", sample.Group);
                Directory.CreateDirectory(dir);
                string stem = $"{sample.Slug}-{size.File}";

                svg.Draw(ctx.List.AsDrawList());
                File.WriteAllText(Path.Combine(dir, stem + ".svg"), svg.Svg);
                File.WriteAllText(Path.Combine(dir, stem + ".json"), ElementsJson(ctx));
                Interlocked.Increment(ref images);

                var entry = new Dictionary<string, string>
                {
                    ["svg"] = Rel(Path.Combine(dir, stem + ".svg")),
                    ["json"] = Rel(Path.Combine(dir, stem + ".json")),
                };

                if (_o.Png)
                {
                    // Every image over the busy still; the 16:9 ones over all three.
                    foreach (var b in Backdrops.Names)
                    {
                        if (b != "busy" && (sample.Gallery || size != new UiPreviewSize(1920, 1080, 1f)))
                            continue;
                        string png = Path.Combine(dir, b == "busy" ? stem + ".png" : $"{stem}-{b}.png");
                        var t = Stopwatch.StartNew();
                        skia.Backdrop = backdrops[b];
                        skia.Draw(ctx.List.AsDrawList());
                        double drawn = t.Elapsed.TotalMilliseconds;
                        File.WriteAllBytes(png, skia.Png());
                        lock (_pngMs)
                        {
                            _drawMs.Add(drawn);
                            _pngMs.Add(t.Elapsed.TotalMilliseconds);
                        }
                        entry[b == "busy" ? "png" : "png-" + b] = Rel(png);
                    }
                }

                files[size.Name] = entry;
            }

            manifest[k] = new
            {
                group = sample.Group,
                title = sample.Title,
                slug = sample.Slug,
                gallery = sample.Gallery,
                layer = layer.ToString(),
                files,
            };
        });

        if (_o.Png)
            GallerySheet(samples.Where(s => s.Gallery).ToList(), theme, lang, backdrops);

        CopyAssets();
        File.WriteAllText(Path.Combine(_o.Out, "manifest.json"), JsonSerializer.Serialize(new
        {
            generated = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            sizes = UiSamples.Sizes().Select(s => s.Name),
            backdrops = Backdrops.Names.Select(b => $"backdrops/{b}.jpg"),
            fonts = _fonts.Faces.Select(f => new { family = f.Family, weight = f.Weight, file = $"fonts/{FontFile(f)}" }),
            samples = manifest,
        }, new JsonSerializerOptions { WriteIndented = true }));

        if (!_o.Quiet)
        {
            Console.WriteLine($"ui-preview: {samples.Count} samples, {images} SVGs" +
                (_o.Png ? $", {_pngMs.Count} PNGs ({Mean(_drawMs):0.0} ms draw + {Mean(_pngMs) - Mean(_drawMs):0.0} ms encode mean, {(_pngMs.Count > 0 ? _pngMs.Max() : 0):0.0} ms max per PNG)" : "") +
                $" in {total.ElapsedMilliseconds} ms -> {_o.Out}");
        }

        if (_o.Png)
            File.WriteAllLines(Path.Combine(_o.Out, "png-times.tsv"),
                ["# per PNG: draw + encode, ms (SkiaBackend)", .. _pngMs.Select(m => m.ToString("0.00", CultureInfo.InvariantCulture))]);
        return 0;
    }

    private static double Mean(List<double> v) => v.Count == 0 ? 0 : v.Average();

    private string Rel(string path) => Path.GetRelativePath(_o.Out, path).Replace('\\', '/');

    private static string FontFile(TtfFace f) => $"{f.Family.Replace(" ", "")}-{f.Weight}.ttf";

    /// <summary>Fonts and backdrops beside the pictures, for the web UI.</summary>
    private void CopyAssets()
    {
        string fonts = Path.Combine(_o.Out, "fonts");
        Directory.CreateDirectory(fonts);
        foreach (var f in _fonts.Faces)
            File.WriteAllBytes(Path.Combine(fonts, FontFile(f)), f.Data);

        string back = Path.Combine(_o.Out, "backdrops");
        Directory.CreateDirectory(back);
        foreach (var b in Backdrops.Names)
            File.Copy(Path.Combine(_o.Backdrops, b + ".jpg"), Path.Combine(back, b + ".jpg"), overwrite: true);
    }

    /// <summary>Every element of the last frame: what the inspector shows on hover.</summary>
    public static string ElementsJson(UiContext ctx)
    {
        var list = ctx.List.AsDrawList();
        var sb = new StringBuilder("[\n");
        for (int i = 0; i < list.Elements.Length; i++)
        {
            var e = list.Elements[i];
            string name = new(list.Text.Slice(e.NameStart, e.NameLength));
            sb.Append(CultureInfo.InvariantCulture,
                $"{{\"i\":{i},\"id\":{e.Id},\"parent\":{e.Parent},\"kind\":\"{e.Kind}\",\"name\":{JsonSerializer.Serialize(name)},\"x\":{e.Rect.X},\"y\":{e.Rect.Y},\"w\":{e.Rect.W},\"h\":{e.Rect.H},\"flags\":\"{e.Flags}\"}}");
            sb.Append(i + 1 < list.Elements.Length ? ",\n" : "\n");
        }
        return sb.Append(']').ToString();
    }

    /// <summary>The union of everything a part drew (not its full-screen root or overlay layers), padded.</summary>
    private static SKRect PartBounds(UiContext ctx, int w, int h, float pad)
    {
        var list = ctx.List.AsDrawList();
        float x0 = w, y0 = h, x1 = 0, y1 = 0;
        for (int i = 0; i < list.Elements.Length; i++)
        {
            var r = list.Elements[i].Rect;
            if (r.W <= 0 || r.H <= 0 || (r.W >= w && r.H >= h))
                continue;
            x0 = MathF.Min(x0, r.X); y0 = MathF.Min(y0, r.Y);
            x1 = MathF.Max(x1, r.X + r.W); y1 = MathF.Max(y1, r.Y + r.H);
        }
        if (x1 <= x0) return new SKRect(0, 0, w, h);
        // At least a 16:9 window around it, so small parts keep their context.
        float cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
        float bw = MathF.Max(x1 - x0 + 2 * pad, 480), bh = MathF.Max(y1 - y0 + 2 * pad, 270);
        if (bw / bh < 16f / 9) bw = bh * 16 / 9; else bh = bw * 9 / 16;
        return new SKRect(cx - bw / 2, cy - bh / 2, cx + bw / 2, cy + bh / 2);
    }

    /// <summary>
    /// The gallery as one picture: every part in every state, tiled with its
    /// name under it, so an agent picks parts by looking at one image.
    /// </summary>
    private void GallerySheet(List<UiSample> gallery, UiTheme theme, IUiLocaliser lang, Backdrops backdrops)
    {
        if (gallery.Count == 0)
            return;
        const int cols = 3, tileW = 640, tileH = 360, label = 36;
        int rows = (gallery.Count + cols - 1) / cols;
        using var sheet = new SKBitmap(cols * tileW, rows * (tileH + label));
        using var canvas = new SKCanvas(sheet);
        canvas.Clear(new SKColor(24, 26, 30));
        using var skia = new SkiaBackend(_fonts);
        using var labelFont = new SKFont(SKTypeface.FromData(SKData.CreateCopy(_fonts.Faces[1].Data)), 18);
        using var labelPaint = new SKPaint { Color = new SKColor(230, 226, 218), IsAntialias = true };

        for (int i = 0; i < gallery.Count; i++)
        {
            var s = gallery[i];
            var screen = s.Create();
            var ctx = UiSamples.PreviewContext(theme, _metrics, lang);
            UiSamples.Render(ctx, screen, UiSamples.Screen(GalleryW, GalleryH), s.Now);
            skia.Backdrop = backdrops["busy"];
            skia.Draw(ctx.List.AsDrawList());

            int x = i % cols * tileW, y = i / cols * (tileH + label);
            var src = PartBounds(ctx, GalleryW, GalleryH, 40);
            float k = MathF.Min(1.6f, MathF.Min(tileW / src.Width, tileH / src.Height));
            float dw = src.Width * k, dh = src.Height * k;
            var dst = new SKRect(x + (tileW - dw) / 2, y + (tileH - dh) / 2, x + (tileW + dw) / 2, y + (tileH + dh) / 2);
            using var img = SKImage.FromBitmap(skia.Bitmap);
            canvas.Save();
            canvas.ClipRect(new SKRect(x, y, x + tileW, y + tileH));
            canvas.DrawImage(img, src, dst, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            canvas.Restore();
            canvas.DrawText($"{s.Group} — {s.Title}", x + 12, y + tileH + 25, SKTextAlign.Left, labelFont, labelPaint);
        }

        using var image = SKImage.FromBitmap(sheet);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        Directory.CreateDirectory(Path.Combine(_o.Out, "gallery"));
        File.WriteAllBytes(Path.Combine(_o.Out, "gallery", "sheet.png"), data.ToArray());
    }
}

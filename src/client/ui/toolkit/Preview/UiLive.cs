using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// One part of the game's UI the live mock-ups can draw: a screen (its
/// samples are its data presets), a gallery part (its states are), or a
/// sub-panel drawn alone. <see cref="Kind"/> is <c>screen</c>, <c>hud</c>,
/// <c>part</c> or <c>panel</c>; <see cref="Type"/> is null for a gallery part,
/// which only its samples build.
/// </summary>
public sealed record UiSegment(string Id, string Name, string Kind, string Assembly, Type? Type, IReadOnlyList<UiSample> Samples)
{
    /// <summary>The data preset made up from nothing (every interface answered with plausible values).</summary>
    public const string Auto = "auto";

    /// <summary>The presets, first the default: each sample's slug, then <see cref="Auto"/> when the segment can be built without one.</summary>
    public IEnumerable<string> Presets => Samples.Select(s => s.Slug).Concat(Type is null ? [] : [Auto]);
}

/// <summary>
/// The live mock-ups (the dev web UI's Mockups tab): every screen, HUD
/// element, gallery part and sub-panel in the UI assemblies, drawn on request
/// at any size with its data mocked by <see cref="UiMockSources"/> and the
/// caller's edits laid over it. It loads nothing itself: a host hands it the
/// UI assemblies, theme, font metrics and copy, and asks <see cref="Answer"/>
/// a JSON request, so the preview tool and the game's dev API serve the same
/// answers.
/// </summary>
public sealed class UiLive
{
    private readonly UiTheme _theme;
    private readonly IUiFontMetrics _metrics;
    private readonly IUiLocaliser _lang;
    private readonly IReadOnlyList<TtfFace> _faces;
    private readonly IUiTextureLibrary? _hudArt;
    private readonly IReadOnlyList<UiSegment> _segments;

    public UiLive(IReadOnlyList<UiSegment> segments, UiTheme theme, IUiFontMetrics metrics, IUiLocaliser lang,
                  IReadOnlyList<TtfFace> faces, IUiTextureLibrary? hudArt = null)
    {
        _segments = segments;
        _theme = theme;
        _metrics = metrics;
        _lang = lang;
        _faces = faces;
        _hudArt = hudArt;
    }

    public IReadOnlyList<UiSegment> Segments => _segments;

    /// <summary>
    /// The live mock-ups as a game host serves them (the dev API's
    /// /preview/live): the segments of <paramref name="assemblies"/>, the
    /// default theme with <paramref name="themeFile"/> laid over it when it
    /// exists, the language files first-wins and then the toolkit's own copy,
    /// and the toolkit's faces measuring text.
    /// </summary>
    public static UiLive ForGame(IReadOnlyList<Assembly> assemblies, string? themeFile, IReadOnlyList<string> languageFiles)
    {
        var theme = SourceSharpTheme.Create();
        if (themeFile is not null && File.Exists(themeFile))
            UiThemeFile.Apply(theme, File.ReadAllText(themeFile));
        var lang = UiLanguageFile.Chain([.. languageFiles.Select(f => UiLanguageFile.Parse(File.ReadAllBytes(f))), UiLanguageFile.Core()]);
        var fonts = new UiFontLibrary();
        return new UiLive(Discover([.. assemblies]), theme, fonts, lang, fonts.Faces);
    }

    /// <summary>
    /// Every segment in <paramref name="assemblies"/>: each sampled screen
    /// (grouped by its name), each gallery part, then each screen and
    /// sub-panel type with no sample, in assembly then name order. The
    /// toolkit's own preview frames (<see cref="UiPanelPreview"/>,
    /// <see cref="UiScreenGroup"/>...) are not segments.
    /// </summary>
    public static IReadOnlyList<UiSegment> Discover(params Assembly[] assemblies)
    {
        var samples = UiSamples.Discover(assemblies);
        var list = new List<UiSegment>();
        var sampledTypes = new HashSet<Type>();
        foreach (var group in samples.GroupBy(s => (s.Gallery, s.Group)))
        {
            var first = group.First();
            string asm = first.Method.DeclaringType!.Assembly.GetName().Name!;
            if (group.Key.Gallery)
            {
                list.Add(new UiSegment("part:" + group.Key.Group, group.Key.Group, "part", asm, null, group.ToList()));
                continue;
            }
            var screen = TryCreate(first);
            var type = screen?.GetType();
            bool frame = type is null || IsFrame(type);
            if (!frame)
                sampledTypes.Add(type!);
            list.Add(new UiSegment("screen:" + group.Key.Group, frame ? group.Key.Group : type!.Name, screen?.Layer == UiLayerKind.Hud ? "hud" : "screen",
                type is null || frame ? asm : type.Assembly.GetName().Name!, frame ? null : type, group.ToList()));
        }

        foreach (var asm in assemblies)
        {
            foreach (var t in Types(asm).Where(t => !t.IsAbstract && !t.ContainsGenericParameters && !IsFrame(t))
                                        .OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                string asmName = asm.GetName().Name!;
                if (typeof(UiScreen).IsAssignableFrom(t) && !sampledTypes.Contains(t))
                {
                    var built = Build(t, new UiMockSources(new Dictionary<string, JsonElement>(), fakes: false), null, out _) as UiScreen;
                    list.Add(new UiSegment("screen:" + t.FullName, built?.Name ?? t.Name, built?.Layer == UiLayerKind.Hud ? "hud" : "screen", asmName, t, []));
                }
                else if (PanelProps(t, out _))
                {
                    list.Add(new UiSegment("panel:" + t.FullName, t.Name, "panel", asmName, t, []));
                }
            }
        }
        return list;
    }

    private static IEnumerable<Type> Types(Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(t => t is not null)!;
        }
    }

    private static UiScreen? TryCreate(UiSample sample)
    {
        try
        {
            return sample.Create();
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    /// <summary>The toolkit's preview frames, which draw someone else's content.</summary>
    private static bool IsFrame(Type t)
        => t == typeof(UiPanelPreview) || t == typeof(UiScreenGroup) || t == typeof(UiPartPreview)
           || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(UiPanelPreview<>));

    /// <summary>Whether <paramref name="t"/> is a sub-panel; <paramref name="props"/> is its props type (null for a props-less one).</summary>
    private static bool PanelProps(Type t, out Type? props)
    {
        props = null;
        for (var b = t.BaseType; b is not null; b = b.BaseType)
        {
            if (b == typeof(UiPanel))
                return true;
            if (b.IsGenericType && b.GetGenericTypeDefinition() == typeof(UiPanel<>))
            {
                props = b.GetGenericArguments()[0];
                return true;
            }
        }
        return false;
    }

    /// <summary>A screen of <paramref name="type"/> (a sub-panel framed alone) over made-up data; null and a reason when it cannot be built.</summary>
    private static object? Build(Type type, UiMockSources mocks, IUiTextureLibrary? hudArt, out string? error)
    {
        error = null;
        var services = new UiSampleServices { HudArt = hudArt, Source = mocks.Wrap };
        try
        {
            if (typeof(UiScreen).IsAssignableFrom(type))
                return services.Create(type);
            if (!PanelProps(type, out var props))
                throw new InvalidOperationException($"{type.Name} is neither a screen nor a panel");
            var panel = services.Create(type);
            if (props is null)
                return new UiPanelPreview(type.Name, (UiPanel)panel);
            var value = mocks.Value("props", "props", props, mocks.Plausible(props, "props"));
            var frame = typeof(UiPanelPreview<>).MakeGenericType(props);
            return Activator.CreateInstance(frame, type.Name, panel, value, UiAnchor.Centre, UiLayerKind.Hud);
        }
        catch (Exception e) when (e is TargetInvocationException or InvalidOperationException or ArgumentException or MissingMethodException)
        {
            var inner = e is TargetInvocationException { InnerException: { } i } ? i : e;
            error = $"{inner.GetType().Name}: {inner.Message}";
            return null;
        }
    }

    /// <summary>
    /// Answers one request, as JSON:
    /// <c>{"op":"list"}</c> lists the segments, the preset sizes and the fonts
    /// (base64, for the page to draw the SVG's text in); <c>{"op":"render",
    /// "segment":id, "preset":slug, "w":1920, "h":1080, "scale":1, "values":{key:value}}</c>
    /// draws one and answers its SVG, elements, the values it read and its
    /// layer. A request that fails answers <c>{"error":...}</c>.
    /// </summary>
    public string Answer(string requestJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestJson);
            var root = doc.RootElement;
            string op = root.TryGetProperty("op", out var o) ? o.GetString() ?? "" : "";
            return op switch
            {
                "list" => List(),
                "render" => Render(root),
                _ => Error($"unknown op \"{op}\": list or render"),
            };
        }
        catch (JsonException e)
        {
            return Error("bad request: " + e.Message);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // A dev tool answering one request: whatever a screen or its data
            // threw is the answer, and the host keeps serving.
            return Error($"{e.GetType().Name}: {e.Message}");
        }
    }

    private static string Error(string message) => JsonSerializer.Serialize(new { error = message });

    private string List()
        => JsonSerializer.Serialize(new
        {
            segments = _segments.Select(s => new
            {
                id = s.Id,
                name = s.Name,
                kind = s.Kind,
                assembly = s.Assembly,
                presets = s.Samples.Select(x => new { slug = x.Slug, title = x.Title })
                                   .Concat(s.Type is null ? [] : [new { slug = UiSegment.Auto, title = "made-up data" }]),
            }),
            sizes = UiSamples.Sizes().Select(z => new { name = z.Name, w = z.W, h = z.H, scale = z.UiScale }),
            fonts = _faces.Select(f => new { family = f.Family, weight = f.Weight, data = Convert.ToBase64String(f.Data) }),
        });

    private string Render(JsonElement r)
    {
        string id = r.TryGetProperty("segment", out var s) ? s.GetString() ?? "" : "";
        var segment = _segments.FirstOrDefault(x => x.Id == id);
        if (segment is null)
            return Error($"no segment \"{id}\"");
        string preset = r.TryGetProperty("preset", out var p) ? p.GetString() ?? "" : "";
        int w = Math.Clamp(r.TryGetProperty("w", out var wv) ? wv.GetInt32() : 1920, 160, 8192);
        int h = Math.Clamp(r.TryGetProperty("h", out var hv) ? hv.GetInt32() : 1080, 120, 8192);
        float scale = Math.Clamp(r.TryGetProperty("scale", out var sv) ? sv.GetSingle() : 1f, 0.5f, 4f);
        var edits = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (r.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Object)
            foreach (var v in values.EnumerateObject())
                edits[v.Name] = v.Value.Clone();

        var sample = segment.Samples.FirstOrDefault(x => x.Slug == preset);
        if (sample is null && (preset != UiSegment.Auto || segment.Type is null))
            sample = segment.Samples.FirstOrDefault();
        var mocks = new UiMockSources(edits, fakes: sample is not null);
        var clock = Stopwatch.StartNew();
        UiScreen? screen;
        string? error = null;
        if (sample is not null)
        {
            try
            {
                screen = (UiScreen)sample.Method.Invoke(null, [new UiSampleServices { HudArt = _hudArt, Source = mocks.Wrap }])!;
            }
            catch (TargetInvocationException e)
            {
                screen = null;
                error = $"{e.InnerException?.GetType().Name}: {e.InnerException?.Message}";
            }
        }
        else
        {
            screen = Build(segment.Type!, mocks, _hudArt, out error) as UiScreen;
        }
        if (screen is null)
            return JsonSerializer.Serialize(new { error = $"{segment.Name} could not be built: {error}", values = Leaves(mocks) });

        var ctx = UiSamples.PreviewContext(_theme, _metrics, _lang);
        try
        {
            UiSamples.Render(ctx, screen, UiSamples.Screen(w, h, scale), sample?.Now ?? 100.0);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return JsonSerializer.Serialize(new { error = $"{segment.Name} threw while drawing: {e.GetType().Name}: {e.Message}", values = Leaves(mocks) });
        }
        var svg = new SvgBackend(_metrics);
        svg.Draw(ctx.List.AsDrawList());
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{{\"name\":{JsonSerializer.Serialize(screen.Name)},\"layer\":\"{screen.Layer}\",\"preset\":{JsonSerializer.Serialize(sample?.Slug ?? UiSegment.Auto)},");
        sb.Append(CultureInfo.InvariantCulture, $"\"w\":{w},\"h\":{h},\"scale\":{scale.ToString(CultureInfo.InvariantCulture)},\"ms\":{clock.Elapsed.TotalMilliseconds:0.0},");
        sb.Append("\"svg\":").Append(JsonSerializer.Serialize(svg.Svg)).Append(",\"elements\":").Append(ElementsJson(ctx).Replace("\n", "", StringComparison.Ordinal));
        sb.Append(",\"values\":").Append(JsonSerializer.Serialize(Leaves(mocks))).Append('}');
        return sb.ToString();
    }

    private static IEnumerable<object> Leaves(UiMockSources mocks)
        => mocks.Leaves.Select(l => new { key = l.Key, source = l.Source, type = l.Type, value = l.Value, changed = l.Changed, options = l.Options });

    /// <summary>Every element of the last frame: what the page's inspector names on hover.</summary>
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
}

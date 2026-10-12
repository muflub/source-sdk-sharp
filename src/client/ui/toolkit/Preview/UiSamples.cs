using System.Reflection;

using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// Marks a static method that builds a screen with sample data for previews,
/// goldens and the lint (plan_client_ui §7). It registers fakes for the data
/// interfaces, one registration per interface, and lets the services build the
/// screen by constructor injection, as the game's container will:
/// <code>
/// [UiSample("12 players, two teams")]
/// static UiScreen TwelvePlayers(UiSampleServices s)
///     => s.Add&lt;IScoreboardSource&gt;(FakeScoreboard.Teams(6)).Create&lt;Scoreboard&gt;();
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UiSampleAttribute(string title) : Attribute
{
    public string Title => title;

    /// <summary>The clock the sample is drawn at (seconds): what its data's times are relative to.</summary>
    public double Now { get; init; } = 100.0;
}

/// <summary>
/// Marks a static method that builds a gallery entry: one composite or
/// sub-panel in one state, usually through <see cref="UiPanelPreview{TProps}"/>.
/// <c>[UiGallery("HudMeter", "low health")]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UiGalleryAttribute(string part, string state) : Attribute
{
    public string Part => part;

    public string State => state;

    public double Now { get; init; } = 100.0;
}

/// <summary>One discovered sample or gallery entry.</summary>
public sealed record UiSample(string Group, string Title, bool Gallery, double Now, MethodInfo Method)
{
    /// <summary>A file-name form of the title: "12 players, two teams" → "12-players-two-teams".</summary>
    public string Slug => UiSamples.Slug(Title);

    /// <summary>A new screen from fresh services: nothing is shared between two calls.</summary>
    public UiScreen Create() => (UiScreen)Method.Invoke(null, [new UiSampleServices()])!;
}

/// <summary>A preview size: pixels, and the ui_scale it is drawn at. <see cref="Name"/> is "1920x1080", or "1080x1920@1.75".</summary>
public readonly record struct UiPreviewSize(int W, int H, float UiScale)
{
    public ScreenInfo Screen => Toolkit.UiScale.Screen(W, H, UiScale);

    public string Name => UiScale == 1f ? $"{W}x{H}" : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{W}x{H}@{UiScale:0.##}");

    /// <summary>The name as a file stem part: "1080x1920-ui1.75".</summary>
    public string File => UiScale == 1f ? Name : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{W}x{H}-ui{UiScale:0.##}");
}

/// <summary>Finds samples and renders them: the code previews, goldens and the lint share.</summary>
public static class UiSamples
{
    /// <summary>
    /// The mock-up sizes (plan_client_ui decision 7, and the 2026-10-05 review):
    /// 4:3, 5:4, 16:9, 21:9 (two), portrait (two), and two at a raised ui_scale.
    /// 5:4 and portrait come out narrow at ui_scale 1; 4:3 sits exactly on the
    /// breakpoint and keeps the regular arrangement.
    /// </summary>
    public static IEnumerable<UiPreviewSize> Sizes()
    {
        yield return new(1024, 768, 1f);
        yield return new(1280, 1024, 1f);
        yield return new(1920, 1080, 1f);
        yield return new(2560, 1080, 1f);
        yield return new(3440, 1440, 1f);
        yield return new(1080, 1920, 1f);
        yield return new(1440, 2560, 1f);
        yield return new(1920, 1080, 1.5f);
        yield return new(1080, 1920, 1.75f);
    }

    /// <summary>The sizes whose design width is under the theme's narrow breakpoint, or that are not 16:9 or wider.</summary>
    public static IEnumerable<UiPreviewSize> NarrowSizes(UiTheme theme)
        => Sizes().Where(s => s.W * 9 < s.H * 16 || UiScale.DesignWidth(s.Screen) < theme.NarrowBelowDu - 0.5f);

    public static string Slug(string title)
    {
        var chars = title.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        string s = new(chars);
        while (s.Contains("--", StringComparison.Ordinal)) s = s.Replace("--", "-", StringComparison.Ordinal);
        return s.Trim('-');
    }

    /// <summary>Every [UiSample] and [UiGallery] method in the assemblies, screens grouped by the screen's name.</summary>
    public static IReadOnlyList<UiSample> Discover(params Assembly[] assemblies)
    {
        var found = new List<UiSample>();
        foreach (var asm in assemblies)
        {
            foreach (var type in asm.GetTypes())
            {
                foreach (var m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var sample = m.GetCustomAttribute<UiSampleAttribute>();
                    var gallery = m.GetCustomAttribute<UiGalleryAttribute>();
                    if (sample is null && gallery is null)
                        continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 1 || ps[0].ParameterType != typeof(UiSampleServices) || !typeof(UiScreen).IsAssignableFrom(m.ReturnType))
                        throw new InvalidOperationException($"{type.Name}.{m.Name}: a sample takes a UiSampleServices and returns a UiScreen");

                    if (sample is not null)
                    {
                        string group = ((UiScreen)m.Invoke(null, [new UiSampleServices()])!).Name;
                        found.Add(new UiSample(group, sample.Title, false, sample.Now, m));
                    }
                    else
                    {
                        found.Add(new UiSample(gallery!.Part, gallery.State, true, gallery.Now, m));
                    }
                }
            }
        }
        return found.OrderBy(s => s.Gallery).ThenBy(s => s.Group, StringComparer.Ordinal)
                    .ThenBy(s => s.Method.MetadataToken).ToList();
    }

    /// <summary>A context as previews use one: the default theme, the TTF metrics, names recorded.</summary>
    public static UiContext PreviewContext(UiTheme theme, IUiFontMetrics fonts, IUiLocaliser localiser,
                                           IEnumerable<IUiSlotContributor>? slots = null)
        => new(theme, fonts, localiser, slots ?? []) { RecordNames = true };

    /// <summary>
    /// Draws <paramref name="screen"/> at <paramref name="now"/>: <paramref name="settle"/>
    /// frames at 60 Hz ending at <paramref name="now"/>, so eased values have arrived.
    /// The context's list holds the last frame.
    /// </summary>
    public static void Render(UiContext ctx, UiScreen screen, in ScreenInfo info, double now, int settle = 45)
    {
        for (int i = settle - 1; i >= 0; i--)
        {
            ctx.BeginFrame(info, now - i / 60.0);
            var ui = ctx.BeginScreen(screen.Name, screen.Layer);
            screen.Describe(ref ui);
            ctx.EndScreen();
            ctx.EndFrame();
        }
    }

    public static ScreenInfo Screen(int w, int h, float uiScale = 1f) => UiScale.Screen(w, h, uiScale);
}

/// <summary>
/// A gallery screen that draws one sub-panel with fixed props, anchored at the
/// centre of the screen (or a corner, for HUD parts), so a part is previewed and
/// linted alone before it is placed.
/// </summary>
public sealed class UiPanelPreview<TProps>(string name, UiPanel<TProps> panel, TProps props,
                                           UiAnchor at = UiAnchor.Centre, UiLayerKind layer = UiLayerKind.Hud) : UiScreen
{
    public override string Name => name;

    public override UiLayerKind Layer => layer;

    public override void Describe(ref UiFrame ui)
    {
        using (ui.Box().At(at).Inset(Space.XL))
            ui.Sub("part", panel, props);
    }
}

/// <summary>A gallery screen that draws one props-less sub-panel (its data from its constructor).</summary>
public sealed class UiPanelPreview(string name, UiPanel panel, UiAnchor at = UiAnchor.Centre,
                                   UiLayerKind layer = UiLayerKind.Hud) : UiScreen
{
    public override string Name => name;

    public override UiLayerKind Layer => layer;

    public override void Describe(ref UiFrame ui)
    {
        using (ui.Box().At(at).Inset(Space.XL))
            ui.Sub("part", panel);
    }
}

/// <summary>
/// Several screens described as one, in order: a sample of windows open
/// together (their shared <see cref="UiWindows"/> keeps the z-order).
/// </summary>
public sealed class UiScreenGroup(string name, params UiScreen[] screens) : UiScreen
{
    public override string Name => name;

    public override void Describe(ref UiFrame ui)
    {
        foreach (var s in screens)
            s.Describe(ref ui);
    }
}

/// <summary>
/// A gallery screen drawn by a delegate: for composites that are frame methods
/// rather than panels (Button, KeyHint, EmptyState...).
/// </summary>
public sealed class UiPartPreview(string name, UiPartPreview.Draw draw, UiLayerKind layer = UiLayerKind.Menu) : UiScreen
{
    public delegate void Draw(ref UiFrame ui);

    public override string Name => name;

    public override UiLayerKind Layer => layer;

    public override void Describe(ref UiFrame ui)
    {
        using (ui.Box().At(UiAnchor.Centre))
            draw(ref ui);
    }
}

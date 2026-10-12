using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Tests.Ui;

/// <summary>A screen described by a delegate, for facts.</summary>
internal sealed class TestScreen(string name, TestScreen.Describer describe, UiLayerKind layer = UiLayerKind.Menu) : UiScreen
{
    public delegate void Describer(ref UiFrame ui);

    public override string Name => name;

    public override UiLayerKind Layer => layer;

    public override void Describe(ref UiFrame ui) => describe(ref ui);
}

/// <summary>Localises nothing: every #Token is missing.</summary>
internal sealed class NoLocaliser : IUiLocaliser
{
    public string? Find(string token) => null;
}

/// <summary>Builds contexts and runs frames for the UI facts.</summary>
internal static class UiTestKit
{
    /// <summary>The five mock-up sizes (plan_client_ui decision 7).</summary>
    public static readonly (int W, int H)[] Sizes = [(1024, 768), (1920, 1080), (2560, 1080), (3440, 1440), (1080, 1920)];

    public static ScreenInfo Screen(int w, int h) => UiScale.Screen(w, h);

    /// <summary>A plain theme with every role at a known size: Body 20 du, Number 40 du, Label upper-case.</summary>
    public static UiTheme PlainTheme()
    {
        var t = new UiTheme("test");
        foreach (var tone in Enum.GetValues<Tone>())
            t.Set(tone, new UiColor(200, 200, 200, 255));
        t.Set(Tone.None, default);
        t.Set(Tone.Surface, new UiColor(10, 10, 10, 200));
        t.Set(Tone.Scrim, new UiColor(0, 0, 0, 160));
        t.Set(Tone.Window, new UiColor(12, 12, 12, 255));
        foreach (var role in Enum.GetValues<Text>())
            t.Set(role, new UiTypeStyle("Test Sans", 20, 400, UiFontFlags.None, false));
        t.Set(Text.Number, new UiTypeStyle("Test Sans", 40, 700, UiFontFlags.None, false));
        t.Set(Text.Label, new UiTypeStyle("Test Sans", 14, 600, UiFontFlags.None, true));
        t.Set(Motion.Quick, 0.1f).Set(Motion.Standard, 0.25f).Set(Motion.Slow, 1f);
        return t;
    }

    public static UiContext Context(UiTheme? theme = null, IUiLocaliser? localiser = null,
                                    IEnumerable<IUiSlotContributor>? slots = null)
        => new(theme ?? PlainTheme(), new SvgBackendTests.FixedMetrics(), localiser ?? new NoLocaliser(), slots ?? [])
        {
            RecordNames = true,
        };

    /// <summary>One frame of one described screen.</summary>
    public static UiContext Frame(UiContext ctx, ScreenInfo screen, TestScreen.Describer describe, double now = 1.0,
                                  UiLayerKind layer = UiLayerKind.Menu)
    {
        ctx.BeginFrame(screen, now);
        var ui = ctx.BeginScreen("test", layer);
        describe(ref ui);
        ctx.EndScreen();
        ctx.EndFrame();
        return ctx;
    }

    public static UiElementInfo[] Elements(UiContext ctx) => ctx.List.AsDrawList().Elements.ToArray();

    public static string NameOf(UiContext ctx, in UiElementInfo e)
        => new(ctx.List.AsDrawList().Text.Slice(e.NameStart, e.NameLength));

    /// <summary>The element named <paramref name="name"/> (exactly one).</summary>
    public static UiElementInfo Named(UiContext ctx, string name)
    {
        var all = Elements(ctx).Where(e => NameOf(ctx, e) == name).ToArray();
        if (all.Length != 1)
            throw new InvalidOperationException($"{all.Length} elements named {name}: " +
                string.Join(", ", Elements(ctx).Select(e => NameOf(ctx, e))));
        return all[0];
    }

    /// <summary>The text element <paramref name="element"/> drew (its runs joined).</summary>
    public static string RunText(UiContext ctx, int element)
    {
        var list = ctx.List.AsDrawList();
        var sb = new System.Text.StringBuilder();
        foreach (var op in list.Ops)
        {
            if (op.Kind != UiPrimitiveKind.GlyphRun || op.Element != element)
                continue;
            var r = list.GlyphRuns[op.Index];
            sb.Append(list.Text.Slice(r.TextStart, r.TextLength));
        }
        return sb.ToString();
    }

    public static string Svg(UiContext ctx)
    {
        var svg = new SvgBackend(new SvgBackendTests.FixedMetrics());
        svg.Draw(ctx.List.AsDrawList());
        return svg.Svg;
    }
}

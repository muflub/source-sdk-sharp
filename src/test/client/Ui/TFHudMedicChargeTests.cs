using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The medic's charge meter (tf_hud_mediccharge.cpp): it hangs over the
/// cross only while the local player is a live medic with a medigun-shaped
/// weapon in hand (ShouldDraw :147-170), and draws stock's hudmediccharge.res
/// — the label carrying the percent (the "charge" dialog variable, OnTick
/// :255-259) over the ContinuousProgressBar, the patient named under it.
/// </summary>
public class TFHudMedicChargeTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_meter_draws_only_over_a_presenting_medic()
    {
        Assert.True(HasElement("medic-builds-the-ubercharge", "uber label"));
        Assert.False(HasElement("healthy-soldier", "uber label"));       // a soldier, no medic
        Assert.False(HasElement("medigun-hides-the-ammo", "uber label")); // a medic with no meter yet
    }

    [Fact]
    public void The_label_carries_the_percent_and_the_bar_the_level()
    {
        var (ctx, elements) = Rendered("medic-builds-the-ubercharge");
        int label = Elements(ctx).FindIndex(e => UiTestKit.NameOf(ctx, e) == "uber label");
        Assert.Equal("ÜBERCHARGE: 55%", UiTestKit.RunText(ctx, label));
        Assert.True(HasElement("medic-builds-the-ubercharge", "charge"));
        Assert.True(HasElement("medic-uber-ready", "charge"));
    }

    [Fact]
    public void The_label_is_the_res_HudFontSmallest_face()
    {
        // hudmediccharge.res:40 fonts ChargeLabel HudFontSmallest — the
        // Smallest step (TFHudType, 24.75 du), not the caption role's body
        // face; the box grows with the text as the res's autoResize does.
        var (ctx, elements) = Rendered("medic-builds-the-ubercharge");
        var list = ctx.List.AsDrawList();
        int label = Elements(ctx).FindIndex(e => UiTestKit.NameOf(ctx, e) == "uber label");
        var font = GlyphFont(list, label);
        Assert.Equal(25, font.PxHeight);           // 24.75 du at the 1080 canvas
    }

    /// <summary>The font of the first glyph run on the named element.</summary>
    private static UiFontSpec GlyphFont(UiDrawList list, int element)
    {
        foreach (var op in list.Ops)
            if (op.Kind == UiPrimitiveKind.GlyphRun && op.Element == element)
                return list.Fonts[list.GlyphRuns[op.Index].Font];
        throw new InvalidOperationException("no uber label glyph run drew");
    }

    [Fact]
    public void The_patient_names_only_when_the_beam_is_out()
    {
        Assert.True(HasElement("medic-builds-the-ubercharge", "healing"));
        Assert.True(HasElement("medic-releases-the-uber", "healing"));
        Assert.False(HasElement("medic-uber-ready", "healing"));
    }

    [Fact]
    public void The_artless_meter_keeps_the_contrast_plate()
    {
        // The seedless samples have no texture library: the team plate is the
        // Tone.Surface fallback at the Background's coordinates (the contrast
        // rule's backdrop; hudmediccharge.res's own art in game).
        Assert.True(HasElement("medic-builds-the-ubercharge", "medic plate"));
        Assert.True(HasElement("medic-uber-ready", "medic plate"));
    }

    [Fact]
    public void The_cluster_cross_draws_left_of_the_label()
    {
        // HealthClusterIcon (hudmediccharge.res :174-187): the cross art at
        // 2,17 in the plate, asked and drawn with the meter; a soldier's HUD
        // asks nothing.
        var (ctx, lib) = RenderedArt("medic-builds-the-ubercharge");
        Assert.Contains("hud/ico_health_cluster", lib.Asked);
        Assert.True(HasElementOf(ctx, "medic cluster"));
        Assert.True(HasElementOf(ctx, "uber label"));

        var (_, plain) = RenderedArt("healthy-soldier");
        Assert.DoesNotContain("hud/ico_health_cluster", plain.Asked);    // a soldier, no medic
    }

    /// <summary>The sample's frame over a recording art library: the context
    /// and the names the panel asked (the seedless preview's none would keep
    /// the textured leaves undrawn).</summary>
    private static (UiContext Ctx, TFHudArtTests.RecordingLibrary Lib) RenderedArt(string slug)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == slug);
        var lib = new TFHudArtTests.RecordingLibrary();
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(lib), UiSamples.Screen(1920, 1080), sample.Now);
        return (ctx, lib);
    }

    private static bool HasElementOf(UiContext ctx, string name)
        => UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == name);

    /// <summary>The sample's frame: the context and the elements it drew.</summary>
    private static (UiContext, UiElementInfo[]) Rendered(string slug)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);
        return (ctx, UiTestKit.Elements(ctx).ToArray());
    }

    private static List<UiElementInfo> Elements(UiContext ctx) => UiTestKit.Elements(ctx).ToList();

    /// <summary>Whether the named part drew on the sample's screen.</summary>
    private static bool HasElement(string slug, string name)
    {
        var (ctx, _) = Rendered(slug);
        return Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == name);
    }
}

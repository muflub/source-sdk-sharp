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

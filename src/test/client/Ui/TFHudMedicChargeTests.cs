using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The medic's charge meter (tf_hud_mediccharge.cpp): it hangs over the
/// cross only while the local player is a live medic with a medigun-shaped
/// weapon in hand (ShouldDraw :147-170), shows the level and its percent,
/// and names the beam's patient when the beam is out.
/// </summary>
public class TFHudMedicChargeTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_meter_draws_only_over_a_presenting_medic()
    {
        Assert.True(HasElement("medic-builds-the-ubercharge", "medic charge"));
        Assert.False(HasElement("healthy-soldier", "medic charge"));       // a soldier, no medic
        Assert.False(HasElement("medigun-hides-the-ammo", "medic charge")); // a medic with no meter yet
    }

    [Fact]
    public void The_percent_and_the_bar_read_the_level()
    {
        Assert.True(HasElement("medic-builds-the-ubercharge", "percent"));
        Assert.True(HasElement("medic-builds-the-ubercharge", "charge"));
        Assert.True(HasElement("medic-uber-ready", "charge"));
        Assert.True(HasElement("medic-releases-the-uber", "charge"));
    }

    [Fact]
    public void The_patient_names_only_when_the_beam_is_out()
    {
        Assert.True(HasElement("medic-builds-the-ubercharge", "healing"));
        Assert.True(HasElement("medic-releases-the-uber", "healing"));
        Assert.False(HasElement("medic-uber-ready", "healing"));
    }

    /// <summary>Whether the named part drew on the sample's screen.</summary>
    private static bool HasElement(string slug, string name)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), sample.Now);
        return UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == name);
    }
}

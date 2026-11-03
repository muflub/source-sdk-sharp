using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The CTF flag row (tf_hud_flagstatus.cpp): the row draws only when a
/// team fields a flag, each briefcase reads its team's status under its
/// compass, the captures and their target read beside them, and
/// carrying a flag swaps the row for the carried panel and zone arrow.
/// </summary>
public class TFHudFlagStatusTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_flag_row_draws_only_when_a_team_fields_a_flag()
    {
        Assert.False(HasElement("round-clock-in-setup", "flag bg"));      // before: no flags, no row
        Assert.True(HasElement("both-flags-home", "bg left"));            // after: the row is up, art and all
        Assert.False(HasElement("both-flags-home", "flag bg"));           // the art up: no plate under it
        Assert.True(HasFallback("both-flags-home", "flag bg"));           // artless: the plate backs the row
        Assert.False(HasFallback("round-clock-in-setup", "flag bg"));     // stimulus: no flags, no plate either
    }

    [Fact]
    public void Each_briefcase_reads_its_team()
    {
        Assert.Equal(2, Count("both-flags-home", "briefcase"));
        Assert.True(HasFallback("both-flags-home", "flag home"));   // the artless state square
    }

    [Fact]
    public void A_stolen_flag_shows_the_carrier()
    {
        Assert.True(HasFallback("red-flag-stolen", "flag stolen"));
        Assert.True(HasFallback("red-flag-stolen", "carrier"));
        Assert.True(HasFallback("red-flag-stolen", "flag home"));   // the blue one still home
        Assert.True(HasElement("red-flag-stolen", "status icon"));  // the art the game sees
    }

    [Fact]
    public void A_dropped_flag_shows_the_dropped_mark_alone()
    {
        Assert.True(HasFallback("blue-flag-dropped", "flag dropped"));
        Assert.True(HasFallback("blue-flag-dropped", "dropped"));
        Assert.False(HasElement("blue-flag-dropped", "return"));   // stock paints no countdown
        Assert.False(HasElement("red-flag-stolen", "return"));
    }

    [Fact]
    public void Each_flag_carries_a_compass_to_its_bearing()
    {
        Assert.Equal(2, Count("both-flags-home", "compass"));
        Assert.False(HasElement("round-clock-in-setup", "compass"));   // no flags, no arrow
    }

    [Fact]
    public void The_captures_read_beside_each_square()
    {
        Assert.True(HasElement("both-flags-home", "blue captures"));
        Assert.True(HasElement("both-flags-home", "red captures"));
        Assert.True(HasElement("both-flags-home", "playing to"));
        Assert.False(HasElement("red-flag-stolen", "playing to"));   // the fake's target is 0 there
    }

    [Fact]
    public void The_score_shadow_rides_the_art_arm()
    {
        // The drop shadow lifts the numeral off the busy art; artless, the
        // plate already parts value and world, and the shadow would be
        // black on the dark plate.
        Assert.True(HasElement("both-flags-home", "blue shadow"));
        Assert.True(HasElement("both-flags-home", "red shadow"));
        Assert.False(HasFallback("both-flags-home", "blue shadow"));
        Assert.False(HasFallback("both-flags-home", "red shadow"));
    }

    [Fact]
    public void The_score_plates_are_the_artless_fallback()
    {
        // Stock's res has no plate under the scores (hudobjectiveflagpanel.res
        // has no such panel): the bg art is the backdrop, and the Surface
        // plate only stands in when the art is missing.
        Assert.True(HasFallback("both-flags-home", "blue score plate"));
        Assert.True(HasFallback("both-flags-home", "red score plate"));
        Assert.False(HasElement("both-flags-home", "blue score plate"));
        Assert.False(HasElement("both-flags-home", "red score plate"));
    }

    [Fact]
    public void Carrying_a_flag_swaps_the_row_for_the_carried_panel()
    {
        Assert.True(HasElement("carrying-the-red-flag", "flag carried"));
        Assert.True(HasElement("carrying-the-red-flag", "capture zone"));
        Assert.False(HasElement("carrying-the-red-flag", "carrier"));     // the art up: no fallback figure
        Assert.True(HasFallback("carrying-the-red-flag", "carrier"));     // artless: the plate carries it
        Assert.False(HasElement("carrying-the-red-flag", "flag home"));
        Assert.False(HasElement("carrying-the-red-flag", "flag stolen"));
        Assert.False(HasElement("carrying-the-red-flag", "blue captures"));
        Assert.False(HasElement("carrying-the-red-flag", "red captures"));
    }

    /// <summary>Whether the named part drew on the sample's screen. The
    /// library answers every name, so the art layers draw (the state's
    /// image is one of them now).</summary>
    private static bool HasElement(string slug, string name)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(TFHudTests.AllArt.Library), UiSamples.Screen(1920, 1080), sample.Now);
        return UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == name);
    }

    /// <summary>Whether the named part drew with no art at all: the plates'
    /// arm, the fallback the game never sees over a live texture.</summary>
    private static bool HasFallback(string slug, string name)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(TFHudTests.NoArt.Library), UiSamples.Screen(1920, 1080), sample.Now);
        return UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == name);
    }

    /// <summary>How many elements carry the name.</summary>
    private static int Count(string slug, string name)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(TFHudTests.AllArt.Library), UiSamples.Screen(1920, 1080), sample.Now);
        return UiTestKit.Elements(ctx).Count(e => UiTestKit.NameOf(ctx, e) == name);
    }
}

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The TF2 cross's stock rules, each held by the pair of samples that
/// differs in just that rule's numbers (<c>CTFHudPlayerHealth::SetHealth</c>,
/// tf_hud_playerstatus.cpp:736-865): the bonus image exists only on
/// overheal, only below the 0.49 death warning when dying, the max number
/// only from 5 missing on, and death paints the dead material and neither
/// number nor band. The samples build against a library that answers every
/// name (the art layers draw); <see cref="TFHudArtTests"/> holds the
/// library's own arms.
/// </summary>
public class TFHudTests
{
    private static readonly UiFontLibrary Fonts = new();

    [Fact]
    public void The_overheal_band_shows_only_on_an_overhealed_local()
    {
        Assert.Equal(0f, OpacityOf("healthy-soldier", "bonus"));      // before: at max, no band
        Assert.True(OpacityOf("overhealed-heavy", "bonus") > 0f);     // after: the image is on screen
    }

    [Fact]
    public void The_dying_band_shows_only_under_the_death_warning()
    {
        Assert.Equal(0f, OpacityOf("healthy-soldier", "bonus"));
        // 30 of 200 is under HealthDeathWarning 0.49 (98 = 200×0.49); the
        // sample's Now sits mid-ramp of HudHealthDyingPulse (100.3 % 0.25 =
        // 0.05 of the 0.075 ramp up): on screen, at two-thirds strength.
        float a = OpacityOf("critical-soldier", "bonus");
        Assert.True(a > 0f && a < 1f, $"{a}: not the pulse's mid-ramp");
    }

    [Fact]
    public void The_max_number_shows_only_from_five_missing_on()
    {
        Assert.False(HasElement("healthy-soldier", "max number"));    // none missing
        Assert.False(HasElement("soldier-missing-four", "max number"));   // four missing
        Assert.True(HasElement("soldier-missing-five", "max number"));    // five missing (:851)
        Assert.True(HasElement("critical-soldier", "max number"));
    }

    [Fact]
    public void Death_paints_the_dead_cross_and_neither_number_nor_band()
    {
        Assert.True(HasElement("healthy-soldier", "cross"));          // before: the cross is drawn
        Assert.True(HasElement("dead-soldier", "dead cross"));        // after: hud/health_dead (:601-611)
        Assert.False(HasElement("dead-soldier", "health number"));
        Assert.False(HasElement("dead-soldier", "max number"));
        Assert.False(HasElement("dead-soldier", "bonus"));
    }

    [Fact]
    public void No_hero_yet_paints_nothing()
    {
        // No class and no health: stock's player status has nothing to draw
        // (the state the old "no hero yet" sample previewed; it drew the
        // corner's box, which the stock layout has no place for).
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, TFHudSamples.Build(new UiSampleServices(), new FakeLocalPlayer()),
                         UiSamples.Screen(1920, 1080), 100);
        Assert.Equal(0, ctx.List.OpCount);
    }

    /// <summary>Whether the named part drew on the sample's screen (shared
    /// with <see cref="TFHudAmmoTests"/>: same screen, same rig). The
    /// library answers every name, so the art layers draw.</summary>
    internal static bool HasElement(string slug, string key)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(AllArt.Library), UiSamples.Screen(1920, 1080), sample.Now);
        return UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == key);
    }

    /// <summary>Whether the named part drew with no art at all: the plates'
    /// arm, which the game never sees over a live texture.</summary>
    internal static bool HasFallback(string slug, string key)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(NoArt.Library), UiSamples.Screen(1920, 1080), sample.Now);
        return UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == key);
    }

    /// <summary>The named part's opacity on the sample's screen, 0 when it
    /// did not draw: the pulse arms read the alpha stock animates.</summary>
    internal static float OpacityOf(string slug, string key)
    {
        var sample = Assert.Single(PreviewRun.AllSamples(),
                                   s => !s.Gallery && s.Group == "tfhud" && s.Slug == slug);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(AllArt.Library), UiSamples.Screen(1920, 1080), sample.Now);
        var all = UiTestKit.Elements(ctx);
        for (int i = 0; i < all.Length; i++)
            if (UiTestKit.NameOf(ctx, all[i]) == key)
                return ctx.Report.FadeOf(i);
        return 0f;
    }

    /// <summary>Answers every name with a non-zero handle: the art layers
    /// all draw.</summary>
    internal sealed class AllArt : IUiTextureLibrary
    {
        public static readonly AllArt Library = new();
        public int Find(string name) => 1;
        public int Material(string name) => 0;
        public UiTextureInfo? About(string name) => null;
        public UiTextureIcon? Icon(string name) => null;
    }

    /// <summary>Answers no name: the plates draw, the art never.</summary>
    internal sealed class NoArt : IUiTextureLibrary
    {
        public static readonly NoArt Library = new();
        public int Find(string name) => 0;
        public int Material(string name) => 0;
        public UiTextureInfo? About(string name) => null;
        public UiTextureIcon? Icon(string name) => null;
    }
}

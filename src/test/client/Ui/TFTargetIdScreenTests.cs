using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Client.Ui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The name tag: <c>CTargetID::PerformLayout</c>'s size
/// (tf_hud_target_id.cpp:599-697) and targetid.res's desktop spots, at the
/// 720-line sample, 1.5 pixels to the stock unit.
/// </summary>
public sealed class TFTargetIdScreenTests
{
    private const float PixelsPerUnit = 1.5f;

    private static readonly UiFontLibrary Fonts = new();

    private static UiContext Painted(FakeTargetId target)
    {
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, TFTargetIdSamples.Build(new UiSampleServices { HudArt = new FakeTextureLibrary("hud/color_panel_brown", "hud/color_panel_red", "hud/color_panel_blu", "hud/health_bg", "hud/health_color", "hud/health_dead") }, target), UiSamples.Screen(1280, 720), 1.0);
        return ctx;
    }

    private static UiRect Host(FakeTargetId target)
    {
        var ctx = Painted(target);
        return UiTestKit.Named(ctx, "target id").Rect;
    }

    private static FakeTargetId Aimed(string name, bool health = true) => new()
    {
        Name = name, Team = 2, ShowsHealth = health, Health = 80, MaxHealth = 125,
    };

    [Fact]
    public void The_panel_is_as_tall_as_the_element_not_the_art()
        => Assert.Equal(35f * PixelsPerUnit, Host(Aimed("Alyx")).H, 1f);

    [Fact]
    public void The_panel_is_as_wide_as_its_longest_label()
        => Assert.True(Host(Aimed("Alyx the very long named")).W > Host(Aimed("Alyx")).W);

    [Fact]
    public void The_health_block_widens_the_panel_by_its_own_wide()
        => Assert.Equal(32f * PixelsPerUnit, Host(Aimed("Alyx")).W - Host(Aimed("Alyx", health: false)).W, 1f);

    [Fact]
    public void The_panel_centres_on_the_screen_whatever_its_width()
    {
        foreach (string name in new[] { "Al", "Alyx the very long named" })
        {
            var host = Host(Aimed(name));
            Assert.Equal(640f, host.X + host.W / 2f, 1f);
        }
    }

    [Fact]
    public void The_health_number_stands_on_the_cross_with_no_plate()
    {
        var ctx = Painted(Aimed("Alyx"));

        Assert.DoesNotContain(UiTestKit.Elements(ctx), e => UiTestKit.NameOf(ctx, e) == "target health chip");

        var host = UiTestKit.Named(ctx, "target id").Rect;
        var value = UiTestKit.Named(ctx, "target health").Rect;
        Assert.Equal(9f * PixelsPerUnit, value.X - host.X, 1f);
        Assert.Equal(13f * PixelsPerUnit, value.Y - host.Y, 1f);
        Assert.Equal(20f * PixelsPerUnit, value.W, 1f);
    }
}

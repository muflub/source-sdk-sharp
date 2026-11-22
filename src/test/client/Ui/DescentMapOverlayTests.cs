using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using TF2.Descent.Client.Ui;
using TF2.Descent.Client.Ui.Parts;
using TF2.Descent.Client.Ui.Screens;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The Tab map overlay (Diablo's Tab map): drawn over the view on the HUD layer
/// while the toggle is up and nothing while it is down, the hero kept as near the
/// screen's centre as the map square staying on screen allows.
/// </summary>
public class DescentMapOverlayTests
{
    private static readonly UiFontLibrary Fonts = new();

    private static UiElementInfo[] Draw(bool shown, FakeBigMap map)
    {
        var screen = new MapOverlay(new FakeMapOverlay { Shown = shown }, map, new AutomapCanvas(map));
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, screen, UiSamples.Screen(1920, 1080), 10.0);
        // What the screen drew: everything under the frame's own root.
        return [.. UiTestKit.Elements(ctx).Where(e => e.Parent >= 0)];
    }

    [Fact]
    public void It_is_a_hud_screen_so_play_and_the_hud_go_on_under_it()
        => Assert.Equal(UiLayerKind.Hud,
                        new MapOverlay(new FakeMapOverlay(), FakeBigMap.Revealed(), new AutomapCanvas(FakeBigMap.Revealed())).Layer);

    [Fact]
    public void Down_it_draws_nothing()
        => Assert.Empty(Draw(shown: false, FakeBigMap.Revealed()));

    [Fact]
    public void Up_it_draws_the_revealed_rooms()
    {
        var map = FakeBigMap.Revealed();
        var drawn = Draw(shown: true, map);

        // One rect per revealed room at least: the canvas drew the map.
        Assert.True(drawn.Length >= map.RoomCount, $"only {drawn.Length} elements drawn");
    }

    [Fact]
    public void Up_with_nothing_revealed_it_draws_nothing()
        => Assert.Empty(Draw(shown: true, new FakeBigMap()));

    /// <summary>Mid-map, the hero lands on the view's centre.</summary>
    [Fact]
    public void A_hero_mid_map_stands_on_the_screens_centre()
    {
        var (x, y) = MapOverlay.Place(1824, 984, 984, heroX: 492, heroY: 492);

        Assert.Equal(912f - 492f, x);
        Assert.Equal(0f, y);    // the square fills the view's height: it cannot slide
    }

    /// <summary>At the map's edge the square stops at the view's: the map stays on screen.</summary>
    [Fact]
    public void A_hero_at_the_maps_edge_keeps_the_square_inside_the_view()
    {
        var (x, _) = MapOverlay.Place(1824, 984, 984, heroX: 20, heroY: 492);
        Assert.Equal(1824f - 984f, x);

        var (x2, _) = MapOverlay.Place(1824, 984, 984, heroX: 980, heroY: 492);
        Assert.Equal(0f, x2);
    }
}

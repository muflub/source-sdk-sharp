using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The stock .res coordinate converter. Stock's vgui places an element by an
/// xpos/ypos whose "r" prefix measures from the parent's RIGHT or BOTTOM edge
/// to the element's LEFT or TOP edge (vgui Panel.cpp ComputePos:
/// <c>nNewPos = nParentSize - nPosDelta</c>), "c" from the parent's centre,
/// plain from the left/top — in the 640x480 space the .res files are written
/// in. The worked numbers here are the TF2 hud's own: hudplayerclass.res:17-36
/// (PlayerStatusClassImage 25 / r88 / 75x75), hudplayerhealth.res:1-36
/// (the r120 panel and its 75/35 51x51 health image),
/// hudlayout.res:16-27 (HudWeaponAmmo r95 / r55 / 94x45).
/// </summary>
public class StockResTests
{
    [Fact]
    public void An_r_ypos_reaches_the_element_top_so_the_bottom_is_n_minus_tall()
    {
        // PlayerStatusClassImage: ypos r88, tall 75 -> its BOTTOM sits
        // 88 - 75 = 13 units above the screen's bottom (29.25 du), not 88.
        var spot = new StockBox("25", "r88", 75, 75).In(StockRect.Screen);

        Assert.Equal(56.25f, spot.Left);   // 25 units from the left
        Assert.Equal(29.25f, spot.Bottom); // (88 - 75) units up
        Assert.Equal(168.75f, spot.Wide);
        Assert.Equal(168.75f, spot.Tall);
    }

    [Fact]
    public void An_r_xpos_measures_from_the_right_edge()
    {
        // HudWeaponAmmo: xpos r95, wide 94 -> the RIGHT edge is 95 - 94 = 1
        // unit from the screen's right (2.25 du); ypos r55, tall 45 -> the
        // bottom is 10 units up (22.5 du).
        var spot = new StockBox("r95", "r55", 94, 45).In(StockRect.Screen);

        Assert.Equal(2.25f, spot.Right);
        Assert.Equal(1226.25f, spot.Left); // 640 - 95 = 545 units from the left
        Assert.Equal(22.5f, spot.Bottom);
    }

    [Fact]
    public void A_nested_element_adds_its_parents_offset()
    {
        // HudPlayerHealth spans 0..360 units up (ypos r120, tall 120);
        // PlayerStatusHealthImage at 75 / 35 inside it lands its TOP 35 units
        // below the panel's top, so its bottom is 34 units above the screen's
        // bottom (76.5 du) and 75 units from the left (168.75 du).
        var panel = new StockBox("0", "r120", 250, 120).Rect(StockRect.Screen);
        var spot = new StockBox("75", "35", 51, 51).In(panel);

        Assert.Equal(168.75f, spot.Left);
        Assert.Equal(76.5f, spot.Bottom);
        Assert.Equal(114.75f, spot.Wide);
        Assert.Equal(114.75f, spot.Tall);
    }

    [Fact]
    public void A_c_xpos_places_the_element_about_the_parents_centre()
    {
        // hudobjectiveflagpanel.res places its halves at xpos c-140: the LEFT
        // edge 140 units left of the parent's centre (320 - 140 = 180), so a
        // 280-wide element spans 180..460 and centres on the screen.
        var rect = new StockBox("c-140", "r75", 280, 80).Rect(StockRect.Screen);

        Assert.Equal(180f, rect.Left);
        Assert.Equal(460f, rect.Left + rect.Wide);
        var spot = new StockBox("c-140", "r75", 280, 80).In(StockRect.Screen);
        Assert.Equal(0f, spot.CentreX);
        Assert.Equal(-11.25f, spot.Bottom); // (75 - 80) units: 5 below the bottom
    }

    [Fact]
    public void A_nested_c_xpos_measures_from_the_parents_own_centre()
    {
        // A 200-wide parent at plain 100: its centre is 200; a c-30 child of
        // width 20 lands its left edge at 200 - 30 = 170 (the parent's own
        // offset included), not at the screen's centre.
        var parent = new StockBox("100", "100", 200, 50).Rect(StockRect.Screen);
        Assert.Equal(170f, new StockBox("c-30", "10", 20, 20).Rect(parent).Left);
        var spot = new StockBox("c-30", "10", 20, 20).In(parent);

        Assert.Equal(70f * 2.25f, spot.Left); // 170 - 100: from the parent's left
    }

    [Fact]
    public void A_root_box_resolves_against_the_frames_own_screen()
    {
        // vgui's ComputePos measures every position against the actual
        // screen (Panel.cpp): at 1920x1080 the du space is 1920 wide, so
        // the stock screen is 853.33 units and a "c" canvas centres on the
        // screen's true centre — not the 640x480 authoring canvas's 720 du.
        var wide = UiScale.Screen(1920, 1080);

        // The build menu's canvas: c-225 wide 450.
        var at = StockRes.Spot(new StockBox("c-225", "0", 450, 100), wide);
        Assert.Equal(960f, at.CentreX + wide.W / 2f, 2f); // centred on the screen
        Assert.Equal(453.75f, at.Left, 2f);               // 960 - 225 * 2.25

        // The ammo panel: r95 wide 94 puts its right edge 95 - 94 = 1 unit
        // (2.25 du) in from the screen's right, not the 4:3 face's.
        var ammo = StockRes.Spot(new StockBox("r95", "r55", 94, 45), wide);
        Assert.Equal(2.25f, ammo.Right);

        // At 4:3 the stock screen is the authoring canvas itself.
        var plain = UiScale.Screen(1024, 768);
        var canvas = StockRes.Screen(plain);
        Assert.Equal(640f, canvas.Wide);
        Assert.Equal(480f, canvas.Tall);
    }
}

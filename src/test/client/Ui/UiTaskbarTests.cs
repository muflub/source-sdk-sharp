using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The taskbar: the menu layer's chrome listing the open windows, the snap
/// zones a window's edges and corners offer, the band the bar reserves, the
/// minimise, and the tray's settings and clock.
/// </summary>
public class UiTaskbarTests
{
    private static readonly UiWindowSpec Spec = new(600, 400, 300, 200, 4000, 4000, 500);

    private sealed class Win(UiWindows windows, string id, string title) : UiScreen, IUiWindowed
    {
        public int Described;

        public override string Name => id;

        public string WindowId => id;

        public float ScreenHeight;

        public override void Describe(ref UiFrame ui)
        {
            Described++;
            ScreenHeight = ui.DesignHeight;
            using (ui.Window(windows, id, title, Spec))
                ui.Rect(Tone.SurfaceRaised, Size.Fill, Size.Fill);
        }
    }

    private sealed class Menu : UiScreen
    {
        public override string Name => "mainmenu";

        public float DesignHeight;

        public override void Describe(ref UiFrame ui)
        {
            DesignHeight = ui.DesignHeight;
            ui.Rect(Tone.Surface, Size.Du(100), Size.Du(100));
            ui.Rect(Tone.Surface, Size.Du(100), Size.Du(10)).AtRes(UiAnchor.BottomLeft, 0f, 0f).Layered().Key("menu foot");
        }
    }

    private sealed class Rig
    {
        public readonly UiWindows Windows = new(new MemoryWindowStore());
        public readonly UiContext Ctx = Context();
        public readonly UiLayer Layer;
        public readonly TaskbarService Service;
        public readonly List<string> Opened = [];
        public readonly FixedTaskbar Launchers = new();
        public DateTime Now = new(2026, 11, 1, 21, 47, 0);
        private double _t = 1;

        public Rig()
        {
            Service = new TaskbarService(Windows, () => Now);
            Layer = new UiLayer([new Win(Windows, "console", "#SS_UI_Console_Title"), new Win(Windows, "serverbrowser", "#SS_UI_Browser_Title"),
                                 new Win(Windows, "options", "#SS_UI_Options_Title"), new Menu(), new Taskbar(Service, Service, Launchers, Windows)],
                                Ctx, new SvgBackend(new SvgBackendTests.FixedMetrics()), Windows);
            Service.Bind(Layer, name => { Opened.Add(name); Layer.Open(name); });
        }

        public void Frame() => Layer.Frame(UiLayerKind.Menu, Screen(1920, 1080), _t++);

        public void Click(UiRect r)
        {
            float x = r.X + r.W / 2, y = r.Y + r.H / 2;
            Layer.Input(new UiInputEvent(UiInputKind.MouseMove, x, y, 0, '\0', 0));
            Layer.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
            Frame();
            Frame();   // held a frame, as a press is: the frame after the down knows what it holds
            Layer.Input(new UiInputEvent(UiInputKind.MouseUp, x, y, (int)UiKey.MouseLeft, '\0', 0));
            Frame();
        }
    }

    // ---- snap zones ----

    [Theory]
    [InlineData(UiWindowHandle.TopLeft, 0, 0, 960, 500)]
    [InlineData(UiWindowHandle.TopRight, 960, 0, 960, 500)]
    [InlineData(UiWindowHandle.BottomLeft, 0, 500, 960, 500)]
    [InlineData(UiWindowHandle.BottomRight, 960, 500, 960, 500)]
    [InlineData(UiWindowHandle.Left, 0, 0, 960, 1000)]
    [InlineData(UiWindowHandle.Right, 960, 0, 960, 1000)]
    [InlineData(UiWindowHandle.Top, 0, 0, 1920, 500)]
    [InlineData(UiWindowHandle.Bottom, 0, 500, 1920, 500)]
    public void Each_edge_and_corner_snaps_to_its_segment_above_the_bar(UiWindowHandle handle, float x, float y, float w, float h)
    {
        Assert.True(UiWindows.TrySnapZone(handle, 1920, 1080, 80, out var zone));
        Assert.Equal(new UiRect(x, y, w, h), zone);
    }

    [Theory]
    [InlineData(UiWindowHandle.None)]
    [InlineData(UiWindowHandle.Move)]
    public void The_title_bar_and_no_handle_do_not_snap(UiWindowHandle handle)
        => Assert.False(UiWindows.TrySnapZone(handle, 1920, 1080, 0, out _));

    [Fact]
    public void A_clicked_edge_snaps_the_window_and_a_dragged_one_resizes_it()
    {
        var r = new Rig();
        r.Layer.Open("console");
        r.Frame();
        r.Frame();
        float reserved = r.Windows.DockReserved;
        Assert.True(reserved > 0, "the bar reserved no band");
        r.Click(Named(r.Ctx, "wnd-left").Rect);
        Assert.True(r.Windows.TryGet("console", out var p));
        Assert.Equal(new UiWindowPlacement(0, 0, 960, 1080 - reserved), p);

        // A drag of the right edge: travel well past a click, so a resize.
        r.Frame();
        var right = Named(r.Ctx, "wnd-right").Rect;
        float x = right.X + right.W / 2, y = right.Y + right.H / 2;
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseMove, x, y, 0, '\0', 0));
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
        r.Frame();
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseMove, x - 100, y, 0, '\0', 0));
        r.Frame();
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseUp, x - 100, y, (int)UiKey.MouseLeft, '\0', 0));
        r.Frame();
        Assert.True(r.Windows.TryGet("console", out var dragged));
        Assert.Equal(860, dragged.W, 1);
        Assert.Equal(0, dragged.X);
    }

    [Fact]
    public void A_snap_keeps_the_window_within_its_limits_against_the_segments_outer_edge()
    {
        var spec = new UiWindowSpec(600, 400, 300, 200, 700, 800, 500);
        var place = UiWindowWidget.SnapPlace(new UiRect(960, 0, 960, 1000), spec, 700, 800);
        Assert.Equal(new UiRect(1220, 0, 700, 800), place);
    }

    /// <summary>The ghost is a drag's: the pointer crossing a window's edge
    /// (hovering a handle) offers none, or it flashes as the pointer moves.</summary>
    [Fact]
    public void A_hovered_edge_offers_no_ghost()
    {
        var r = new Rig();
        r.Layer.Open("console");
        r.Frame();
        r.Frame();
        foreach (var handle in new[] { "wnd-left", "wnd-br", "wnd-top" })
        {
            var rect = Named(r.Ctx, handle).Rect;
            r.Layer.Input(new UiInputEvent(UiInputKind.MouseMove, rect.X + rect.W / 2, rect.Y + rect.H / 2, 0, '\0', 0));
            r.Frame();
            Assert.True(r.Windows.SnapGhost is null, $"hovering {handle} offered {r.Windows.SnapGhost}");
        }
    }

    /// <summary>Presses the title bar, drags it by (dx, dy) and releases there.</summary>
    private static void DragTitle(Rig r, float dx, float dy)
    {
        var title = Named(r.Ctx, "wnd-title").Rect;
        float x = title.X + title.W / 4, y = title.Y + 10;
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseMove, x, y, 0, '\0', 0));
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
        r.Frame();
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseMove, x + dx, y + dy, 0, '\0', 0));
        r.Frame();
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseUp, x + dx, y + dy, (int)UiKey.MouseLeft, '\0', 0));
        r.Frame();
    }

    /// <summary>Dragged out of a snap, a window takes back the size it had
    /// before the snap, with the point grabbed still under the pointer.</summary>
    [Fact]
    public void A_snapped_window_dragged_out_takes_back_its_size_from_before_the_snap()
    {
        var r = new Rig();
        r.Layer.Open("console");
        r.Frame();
        r.Windows.Snap("console", new UiRect(300, 200, 700, 500));
        r.Frame();
        r.Windows.TryUnsnap("console", out _);
        r.Windows.Snap("console", new UiRect(0, 0, 960, 1000));
        r.Frame();
        r.Frame();
        var title = Named(r.Ctx, "wnd-title").Rect;
        Assert.True(r.Windows.TryGet("console", out var snapped));
        float grab = title.X + title.W / 4, along = (grab - snapped.X) / snapped.W;
        DragTitle(r, 400, 300);
        Assert.True(r.Windows.TryGet("console", out var p));
        Assert.Equal((700f, 500f), (p.W, p.H));
        // As far along the window before as after: the grabbed point stays under the pointer.
        Assert.Equal(grab + 400 - along * 700f, p.X, 1);
    }

    /// <summary>The whole gesture: dragged to the left edge it fills the left
    /// half, dragged out again it is the size it started.</summary>
    [Fact]
    public void A_window_dragged_to_an_edge_and_back_out_is_the_size_it_started()
    {
        var r = new Rig();
        r.Layer.Open("console");
        r.Frame();
        r.Frame();
        Assert.True(r.Windows.TryGet("console", out var start));
        var title = Named(r.Ctx, "wnd-title").Rect;
        DragTitle(r, -(title.X + title.W / 4), 0);
        Assert.True(r.Windows.TryGet("console", out var snapped));
        Assert.Equal(960f, snapped.W, 1);
        Assert.NotEqual(start.W, snapped.W);
        r.Frame();
        DragTitle(r, 500, 200);
        Assert.True(r.Windows.TryGet("console", out var p));
        Assert.Equal((start.W, start.H), (p.W, p.H));
    }

    /// <summary>Snapped from one segment to another, a window keeps the size
    /// from before the first snap.</summary>
    [Fact]
    public void A_window_snapped_twice_takes_back_its_size_from_before_the_first_snap()
    {
        var r = new Rig();
        r.Layer.Open("console");
        r.Frame();
        r.Windows.Snap("console", new UiRect(300, 200, 700, 500));
        r.Windows.TryUnsnap("console", out _);
        r.Windows.Snap("console", new UiRect(0, 0, 960, 1000));
        r.Windows.Snap("console", new UiRect(960, 0, 960, 1000));
        Assert.True(r.Windows.TryUnsnap("console", out var before));
        Assert.Equal(new UiWindowPlacement(300, 200, 700, 500), before);
        Assert.False(r.Windows.TryUnsnap("console", out _));
    }

    /// <summary>Resized after a snap, the window keeps its new size when dragged.</summary>
    [Fact]
    public void A_snapped_window_resized_keeps_its_new_size_when_dragged()
    {
        var r = new Rig();
        r.Layer.Open("console");
        r.Frame();
        r.Windows.Snap("console", new UiRect(300, 200, 700, 500));
        r.Windows.TryUnsnap("console", out _);
        r.Windows.Snap("console", new UiRect(0, 0, 960, 1000));
        r.Frame();
        r.Frame();
        var grip = Named(r.Ctx, "wnd-right").Rect;
        float x = grip.X + grip.W / 2, y = grip.Y + grip.H / 2;
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseMove, x, y, 0, '\0', 0));
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
        r.Frame();
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseMove, x - 100, y, 0, '\0', 0));
        r.Frame();
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseUp, x - 100, y, (int)UiKey.MouseLeft, '\0', 0));
        r.Frame();
        Assert.True(r.Windows.TryGet("console", out var resized));
        DragTitle(r, 300, 200);
        Assert.True(r.Windows.TryGet("console", out var p));
        Assert.Equal((resized.W, resized.H), (p.W, p.H));
    }

    [Theory]
    [InlineData(2, 500, UiWindowHandle.Left)]
    [InlineData(1915, 500, UiWindowHandle.Right)]
    [InlineData(2, 40, UiWindowHandle.TopLeft)]
    [InlineData(1915, 950, UiWindowHandle.BottomRight)]
    [InlineData(960, 2, UiWindowHandle.Top)]
    [InlineData(960, 1075, UiWindowHandle.None)]
    [InlineData(500, 500, UiWindowHandle.None)]
    public void A_title_bar_at_a_screen_edge_offers_that_edge_s_segment(float x, float y, UiWindowHandle expected)
        => Assert.Equal(expected, UiWindowWidget.EdgeHandle(x, y, 1920, 1080, 80));

    /// <summary>Dragged by its title bar to the screen's left edge, a window
    /// shows the left half as the ghost while held and snaps to it on release.</summary>
    [Fact]
    public void A_title_bar_dragged_to_the_left_edge_snaps_the_window_to_the_left_half()
    {
        var r = new Rig();
        r.Layer.Open("console");
        r.Frame();
        r.Frame();
        float reserved = r.Windows.DockReserved;
        var title = Named(r.Ctx, "wnd-title").Rect;
        float x = title.X + 50, y = title.Y + 10;
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseMove, x, y, 0, '\0', 0));
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
        r.Frame();
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseMove, 0, 500, 0, '\0', 0));
        r.Frame();
        Assert.Equal(new UiRect(0, 0, 960, 1080 - reserved), r.Windows.SnapGhost);
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseUp, 0, 500, (int)UiKey.MouseLeft, '\0', 0));
        r.Frame();
        Assert.True(r.Windows.TryGet("console", out var p));
        Assert.Equal(new UiWindowPlacement(0, 0, 960, 1080 - reserved), p);
    }

    /// <summary>A title bar released away from the edges moves the window and
    /// snaps nothing.</summary>
    [Fact]
    public void A_title_bar_released_mid_screen_moves_and_does_not_snap()
    {
        var r = new Rig();
        r.Layer.Open("console");
        r.Frame();
        r.Frame();
        Assert.True(r.Windows.TryGet("console", out var before));
        var title = Named(r.Ctx, "wnd-title").Rect;
        float x = title.X + 50, y = title.Y + 10;
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseMove, x, y, 0, '\0', 0));
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
        r.Frame();
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseMove, x + 40, y + 30, 0, '\0', 0));
        r.Frame();
        Assert.Null(r.Windows.SnapGhost);
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseUp, x + 40, y + 30, (int)UiKey.MouseLeft, '\0', 0));
        r.Frame();
        Assert.True(r.Windows.TryGet("console", out var p));
        Assert.Equal(before.X + 40, p.X, 1);
        Assert.Equal(before.Y + 30, p.Y, 1);
        Assert.Equal(before.W, p.W, 1);
    }

    /// <summary>A click (no drag) on the title bar of a window at the top
    /// edge, the pointer within the edge's reach, snaps nothing.</summary>
    [Fact]
    public void A_click_on_a_title_bar_at_the_top_edge_does_not_snap()
    {
        var r = new Rig();
        r.Layer.Open("console");
        r.Frame();
        r.Windows.Snap("console", new UiRect(100, 0, 600, 400));
        r.Frame();
        r.Frame();
        var title = Named(r.Ctx, "wnd-title").Rect;
        Assert.True(title.Y <= UiWindowWidget.EdgeSnapDu - 1, $"the title bar starts at {title.Y}");
        r.Click(new UiRect(title.X + 40, title.Y, 20, 2));
        Assert.True(r.Windows.TryGet("console", out var p));
        Assert.Equal(new UiWindowPlacement(100, 0, 600, 400), p);
    }

    [Fact]
    public void A_window_drags_no_lower_than_the_bars_band()
    {
        var r = new Rig();
        r.Layer.Open("console");
        r.Frame();
        r.Frame();
        var title = Named(r.Ctx, "wnd-title").Rect;
        float x = title.X + 50, y = title.Y + 10;
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseMove, x, y, 0, '\0', 0));
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
        r.Frame();
        r.Layer.Input(new UiInputEvent(UiInputKind.MouseMove, x, 2000, 0, '\0', 0));
        r.Frame();
        Assert.True(r.Windows.TryGet("console", out var p));
        Assert.Equal(1080 - r.Windows.DockReserved - UiWindowWidget.TitleBar - 2 * UiWindowWidget.Ring, p.Y, 1);
    }

    /// <summary>The layer's Open raises a window before its first describe; the window is still on the z-order once.</summary>
    [Fact]
    public void A_window_opened_before_it_draws_is_on_the_z_order_once()
    {
        var r = new Rig();
        r.Layer.Open("console");
        r.Frame();
        r.Layer.Open("serverbrowser");
        r.Frame();
        Assert.Equal(1, r.Windows.TopRank);
    }

    // ---- the bar's windows ----

    [Fact]
    public void The_bar_lists_exactly_the_open_windows_in_the_order_they_were_first_shown()
    {
        var r = new Rig();
        r.Layer.Open("mainmenu");
        r.Layer.Open("serverbrowser");
        r.Frame();
        r.Layer.Open("console");
        r.Frame();
        Assert.Equal(["serverbrowser", "console"], Enumerable.Range(0, r.Service.WindowCount).Select(i => r.Service.Window(i).Screen));
        Assert.Equal(("#SS_UI_Browser_Title", false), (r.Service.Window(0).TitleToken, r.Service.Window(0).Top));
        Assert.True(r.Service.Window(1).Top);
        r.Layer.Open("serverbrowser");   // raised: its button keeps its place
        r.Frame();
        Assert.Equal(["serverbrowser", "console"], Enumerable.Range(0, r.Service.WindowCount).Select(i => r.Service.Window(i).Screen));
        Assert.True(r.Service.Window(0).Top);
        r.Layer.Close("serverbrowser");
        r.Frame();
        Assert.Equal(["console"], Enumerable.Range(0, r.Service.WindowCount).Select(i => r.Service.Window(i).Screen));
    }

    [Fact]
    public void A_button_raises_a_window_under_others_and_minimises_the_top_one()
    {
        var r = new Rig();
        r.Layer.Open("console");
        r.Frame();
        r.Layer.Open("serverbrowser");
        r.Frame();
        r.Service.Activate(0);                     // the console, under the browser
        Assert.Equal(["console"], r.Opened);
        r.Frame();
        Assert.Equal("console", r.Windows.Top);
        r.Service.Activate(0);                     // the console, now on top: minimised
        r.Frame();
        Assert.True(r.Layer.IsMinimised("console"));
        Assert.True(r.Layer.IsOpen("console"));
        Assert.Equal("serverbrowser", r.Windows.Top);
        Assert.True(r.Service.Window(0).Minimised);
        r.Service.Activate(0);                     // back from minimised, on top
        r.Frame();
        Assert.False(r.Layer.IsMinimised("console"));
        Assert.Equal("console", r.Windows.Top);
    }

    [Fact]
    public void A_minimised_window_draws_nothing_and_takes_no_mouse_but_stays_open()
    {
        var r = new Rig();
        var console = (Win)r.Layer.Screens.Single(s => s.Name == "console");
        r.Layer.Open("console");
        r.Frame();
        r.Layer.Minimise("console");
        int drawn = console.Described;
        r.Frame();
        Assert.Equal(drawn, console.Described);
        Assert.True(r.Layer.IsOpen("console"));
        Assert.Contains("console", r.Layer.OpenScreens());
    }

    [Fact]
    public void Clicking_a_window_button_on_the_bar_activates_it()
    {
        var r = new Rig();
        r.Layer.Open("console");
        r.Frame();
        r.Layer.Open("serverbrowser");
        r.Frame();
        r.Frame();
        // The bar draws last: its button is the last element named for the window.
        var button = Elements(r.Ctx).Last(e => NameOf(r.Ctx, e) == "console");
        r.Click(button.Rect);
        Assert.Equal("console", r.Windows.Top);
    }

    // ---- the bar's coming and going ----

    [Fact]
    public void The_bar_shows_only_while_a_window_is_open_and_then_reserves_nothing()
    {
        var r = new Rig();
        r.Layer.Open("mainmenu");
        r.Frame();
        Assert.False(r.Layer.IsOpen("taskbar"));
        r.Layer.Open("options");
        r.Frame();
        Assert.True(r.Layer.IsOpen("taskbar"));
        Assert.True(r.Windows.DockReserved > 0);
        r.Layer.Close("options");
        r.Frame();
        Assert.False(r.Layer.IsOpen("taskbar"));
        Assert.Equal(0f, r.Windows.DockReserved);
    }

    /// <summary>The bar is up whenever the main menu is, with no window open:
    /// the layer's ChromeWith screen shows it, and it goes with that screen.</summary>
    [Fact]
    public void The_bar_shows_with_the_main_menu_and_no_window()
    {
        var r = new Rig();
        r.Layer.ChromeWith = "mainmenu";
        r.Frame();
        Assert.False(r.Layer.IsOpen("taskbar"));
        r.Layer.Open("mainmenu");
        r.Frame();
        Assert.True(r.Layer.IsOpen("taskbar"));
        Assert.True(r.Windows.DockReserved > 0);
        r.Layer.Close("mainmenu");
        r.Frame();
        Assert.False(r.Layer.IsOpen("taskbar"));
        Assert.Equal(0f, r.Windows.DockReserved);
    }

    /// <summary>A mod's buttons stand on the bar: a left one first at the bar's
    /// left, a tray one just before Settings; a click launches it.</summary>
    [Fact]
    public void A_mods_buttons_stand_at_the_left_and_in_the_tray_and_launch()
    {
        var r = new Rig();
        r.Launchers.Launchers.Add(new UiTaskbarLauncher("#SS_UI_Main_Players", false));
        r.Launchers.Launchers.Add(new UiTaskbarLauncher("#SS_UI_Options_Title", true));
        r.Layer.ChromeWith = "mainmenu";
        r.Layer.Open("mainmenu");
        r.Frame();
        r.Frame();
        var left = Named(r.Ctx, "#SS_UI_Main_Players").Rect;
        var tray = Named(r.Ctx, "#SS_UI_Options_Title").Rect;
        var settings = Named(r.Ctx, "taskbar-settings").Rect;
        Assert.True(left.X < 100, $"the left button at {left.X}");
        Assert.True(tray.X > 960 && tray.X + tray.W <= settings.X, $"the tray button at {tray.X}, Settings at {settings.X}");
        r.Click(tray);
        Assert.Equal([1], r.Launchers.Launched);
    }

    /// <summary>In play (no main menu up) the bar stays down whatever windows
    /// are open, and windows then keep the whole screen.</summary>
    [Fact]
    public void In_play_the_bar_stays_down_with_windows_open()
    {
        var r = new Rig();
        r.Layer.ChromeWith = "mainmenu";
        r.Layer.Open("console");
        r.Layer.Open("options");
        r.Frame();
        Assert.False(r.Layer.IsOpen("taskbar"));
        Assert.Equal(0f, r.Windows.DockReserved);
        r.Layer.Open("mainmenu");
        r.Frame();
        Assert.True(r.Layer.IsOpen("taskbar"));
    }

    /// <summary>While the bar is up a plain screen (the menu) is laid out above
    /// its band, so a menu's own bottom row is not under it (TF2's items and
    /// gears were, live); a window, which keeps clear through DockReserved, is
    /// handed the whole screen.</summary>
    [Fact]
    public void A_plain_screen_is_laid_out_above_the_bars_band_and_a_window_is_not()
    {
        var r = new Rig();
        r.Layer.ChromeWith = "mainmenu";
        var menu = r.Layer.Screens.OfType<Menu>().Single();
        var console = r.Layer.Screens.OfType<Win>().First(w => w.Name == "console");
        r.Layer.Open("mainmenu");
        r.Layer.Open("console");
        r.Frame();
        r.Frame();                                   // the band is the bar's last frame's
        float band = r.Windows.DockReserved;
        Assert.True(band > 0);
        Assert.Equal(1080f - band, menu.DesignHeight, 3);
        var foot = Named(r.Ctx, "menu foot").Rect;
        Assert.Equal(1080f - band, foot.Y + foot.H, 3);
        Assert.Equal(1080f, console.ScreenHeight, 3);
        r.Layer.Close("mainmenu");
        r.Frame();
        Assert.Equal(1080f, console.ScreenHeight, 3);
    }

    /// <summary>A main menu held under the GameUI (not drawn) does not keep the bar up.</summary>
    [Fact]
    public void A_held_main_menu_shows_no_bar()
    {
        var r = new Rig();
        r.Layer.ChromeWith = "mainmenu";
        r.Layer.Open("mainmenu");
        r.Layer.Hold("mainmenu", true);
        r.Frame();
        Assert.False(r.Layer.IsOpen("taskbar"));
    }

    [Fact]
    public void The_bar_is_no_open_screen_and_a_reload_carries_only_the_windows()
    {
        var r = new Rig();
        r.Layer.Open("console");
        r.Frame();
        Assert.True(r.Layer.IsOpen("taskbar"));
        Assert.Equal(["console"], r.Layer.OpenScreens());
        var carried = r.Layer.Carry();

        var fresh = new Rig();
        Assert.Equal(1, fresh.Layer.Restore(carried, _ => null));
        Assert.Equal(["console"], fresh.Layer.OpenScreens());
    }

    // ---- the tray ----

    [Fact]
    public void The_settings_button_opens_the_options_window()
    {
        var r = new Rig();
        r.Layer.Open("console");
        r.Frame();
        r.Frame();
        Assert.False(r.Service.SettingsOpen);
        r.Click(Named(r.Ctx, "taskbar-settings").Rect);
        Assert.Equal(["options"], r.Opened);
        Assert.True(r.Layer.IsOpen("options"));
        Assert.True(r.Service.SettingsOpen);
    }

    [Theory]
    [InlineData(21, 47, "21:47")]
    [InlineData(9, 5, "9:05")]
    [InlineData(0, 0, "0:00")]
    public void The_clock_is_the_local_time(int hour, int minute, string text)
    {
        var r = new Rig { Now = new DateTime(2026, 11, 1, hour, minute, 30) };
        Assert.Equal(text, r.Service.Clock);
    }
}

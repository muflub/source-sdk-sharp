using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// Windows (plan_client_ui §5.7a, user ruling 2026-10-05): movable, resizable
/// frames, as stock's console and server browser are.
/// </summary>
public class UiWindowTests
{
    private static readonly UiWindowSpec Spec = new(600, 400, 300, 200, 1000, 800, 500);

    /// <summary>A windowed screen: one window, content that records whether it is narrow.</summary>
    private sealed class Win(UiWindows windows, string id, UiWindowSpec spec) : UiScreen, IUiWindowed
    {
        public bool NarrowInside, NarrowOutside;

        public override string Name => id;

        public string WindowId => id;

        public override void Describe(ref UiFrame ui)
        {
            NarrowOutside = ui.Narrow;
            using (ui.Window(windows, id, "#SS_UI_Console_Title", spec))
            {
                NarrowInside = ui.Narrow;
                var content = ui.Rect(Tone.SurfaceRaised, Size.Fill, Size.Fill);
                if (ui.Naming)
                    content.Name(id + " content");   // the name costs a string: only when names are recorded
            }
            NarrowOutside |= ui.Narrow;
        }
    }

    private sealed class Rig
    {
        public readonly MemoryWindowStore Store = new();
        public readonly UiWindows Windows;
        public readonly UiContext Ctx = Context();
        public readonly List<Win> Screens = new();
        public ScreenInfo ScreenInfo = Screen(1920, 1080);
        public double Now = 1;

        private readonly TestScreen.Describer _describe;

        public Rig(params string[] ids) : this(Spec, ids) { }

        public Rig(UiWindowSpec spec, params string[] ids)
        {
            Windows = new UiWindows(Store);
            foreach (var id in ids)
                Screens.Add(new Win(Windows, id, spec));
            // Made once: a lambda per frame would be the harness allocating, not the window.
            _describe = (ref UiFrame ui) =>
            {
                for (int i = 0; i < Screens.Count; i++)
                    Screens[i].Describe(ref ui);
            };
        }

        public void Frame() => UiTestKit.Frame(Ctx, ScreenInfo, _describe, Now += 1 / 60.0);

        public UiWindowPlacement Place(string id = "a")
        {
            Assert.True(Windows.TryGet(id, out var p));
            return p;
        }

        /// <summary>The rect of the <paramref name="index"/>th element named <paramref name="name"/> (one per window, in z order).</summary>
        public UiRect Rect(string name, int index = 0)
            => Elements(Ctx).Where(e => NameOf(Ctx, e) == name).ElementAt(index).Rect;

        public void Input(UiInputKind kind, float x, float y)
            => Ctx.Input(new UiInputEvent(kind, x, y, (int)UiKey.MouseLeft, '\0', 0));

        /// <summary>Presses on the middle of <paramref name="handle"/>, moves by (dx, dy) px over two frames, releases.</summary>
        public void Drag(string handle, float dx, float dy, int window = 0)
        {
            var r = Rect(handle, window);
            float x = r.X + r.W / 2, y = r.Y + r.H / 2;
            Input(UiInputKind.MouseDown, x, y);
            Frame();
            Input(UiInputKind.MouseMove, x + dx / 2, y + dy / 2);
            Frame();
            Input(UiInputKind.MouseMove, x + dx, y + dy);
            Frame();
            Input(UiInputKind.MouseUp, x + dx, y + dy);
            Frame();
        }
    }

    [Fact]
    public void A_new_window_opens_centred_at_its_default_size()
    {
        var rig = new Rig("a");
        rig.Frame();
        Assert.Equal(new UiWindowPlacement(660, 340, 600, 400), rig.Place());
        Assert.Equal(new UiRect(660, 340, 600, 400), rig.Rect("wnd-frame"));
    }

    [Fact]
    public void Dragging_the_title_bar_moves_the_window_by_the_delta_and_stores_it_when_released()
    {
        var rig = new Rig("a");
        rig.Frame();
        rig.Drag("wnd-title", 100, 50);
        Assert.Equal(new UiWindowPlacement(760, 390, 600, 400), rig.Place());
        Assert.Equal(1, rig.Store.Saves);
        Assert.True(rig.Store.TryLoad("a", out var stored));
        Assert.Equal(rig.Place(), stored);
    }

    [Theory]
    [InlineData("wnd-left", 40, 0, 40, 0, -40, 0)]
    [InlineData("wnd-right", 40, 0, 0, 0, 40, 0)]
    [InlineData("wnd-top", 0, 30, 0, 30, 0, -30)]
    [InlineData("wnd-bottom", 0, 30, 0, 0, 0, 30)]
    [InlineData("wnd-tl", 40, 30, 40, 30, -40, -30)]
    [InlineData("wnd-tr", 40, 30, 0, 30, 40, -30)]
    [InlineData("wnd-bl", 40, 30, 40, 0, -40, 30)]
    [InlineData("wnd-br", 40, 30, 0, 0, 40, 30)]
    public void Each_edge_and_corner_resizes_its_own_sides(string handle, float dx, float dy, float ex, float ey, float ew, float eh)
    {
        var rig = new Rig("a");
        rig.Frame();
        var before = rig.Place();
        rig.Drag(handle, dx, dy);
        var after = rig.Place();
        Assert.Equal(new UiWindowPlacement(before.X + ex, before.Y + ey, before.W + ew, before.H + eh), after);
    }

    [Fact]
    public void Resizing_holds_the_minimum_and_the_maximum()
    {
        var rig = new Rig("a");
        rig.Frame();
        rig.Drag("wnd-br", -2000, -2000);
        Assert.Equal((300f, 200f), (rig.Place().W, rig.Place().H));
        rig.Drag("wnd-br", 3000, 3000);
        Assert.Equal((1000f, 800f), (rig.Place().W, rig.Place().H));
        rig.Drag("wnd-tl", 3000, 3000);    // from the other corner: the min holds, the far edge stays put
        var p = rig.Place();
        Assert.Equal((300f, 200f), (p.W, p.H));
    }

    [Fact]
    public void A_click_raises_a_window_and_the_raised_window_draws_last_and_is_hit_first()
    {
        var rig = new Rig("a", "b");
        rig.Store.With("a", new UiWindowPlacement(100, 100, 600, 400)).With("b", new UiWindowPlacement(400, 300, 600, 400));
        rig.Frame();
        Assert.Equal("b", rig.Windows.Top);                       // the last opened is on top

        // A click on a's part that b does not cover raises a.
        rig.Input(UiInputKind.MouseDown, 150, 200);
        rig.Input(UiInputKind.MouseUp, 150, 200);
        rig.Frame();
        Assert.Equal("a", rig.Windows.Top);
        // Overlay roots are named by window id: a's comes after b's.
        int ea = Array.FindLastIndex(Elements(rig.Ctx), e => NameOf(rig.Ctx, e) == "a");
        int eb = Array.FindLastIndex(Elements(rig.Ctx), e => NameOf(rig.Ctx, e) == "b");
        Assert.True(ea > eb, "a is drawn after b");

        // Now the overlap belongs to a: a press there drags a, not b.
        var bBefore = rig.Place("b");
        rig.Input(UiInputKind.MouseDown, 500, 320);              // inside both
        rig.Frame();
        Assert.Equal("a", rig.Windows.Top);
        Assert.Equal(bBefore, rig.Place("b"));
    }

    [Fact]
    public void Esc_closes_the_window_on_top_and_only_that_one()
    {
        var store = new MemoryWindowStore();
        var windows = new UiWindows(store);
        var a = new Win(windows, "a", Spec);
        var b = new Win(windows, "b", Spec);
        var layer = new UiLayer([a, b], Context(), new SvgBackend(new SvgBackendTests.FixedMetrics()), windows);
        layer.Open("a");
        layer.Open("b");
        layer.Frame(UiLayerKind.Menu, Screen(1920, 1080), 1);
        Assert.Equal("b", windows.Top);
        layer.Input(new UiInputEvent(UiInputKind.Key, 0, 0, (int)UiKey.Escape, '\0', 0));
        layer.Frame(UiLayerKind.Menu, Screen(1920, 1080), 2);
        Assert.False(layer.IsOpen("b"));
        Assert.True(layer.IsOpen("a"));
    }

    [Fact]
    public void The_close_button_closes_its_window()
    {
        var windows = new UiWindows(new MemoryWindowStore());
        var a = new Win(windows, "a", Spec);
        var ctx = Context();
        var layer = new UiLayer([a], ctx, new SvgBackend(new SvgBackendTests.FixedMetrics()), windows);
        layer.Open("a");
        layer.Frame(UiLayerKind.Menu, Screen(1920, 1080), 1);
        var close = Named(ctx, "wnd-close").Rect;
        layer.Input(new UiInputEvent(UiInputKind.MouseDown, close.X + 5, close.Y + 5, (int)UiKey.MouseLeft, '\0', 0));
        layer.Input(new UiInputEvent(UiInputKind.MouseUp, close.X + 5, close.Y + 5, (int)UiKey.MouseLeft, '\0', 0));
        layer.Frame(UiLayerKind.Menu, Screen(1920, 1080), 2);
        Assert.False(layer.IsOpen("a"));
    }

    [Theory]
    [InlineData("wnd-title", UiCursor.Move)]
    [InlineData("wnd-left", UiCursor.ResizeEW)]
    [InlineData("wnd-bottom", UiCursor.ResizeNS)]
    [InlineData("wnd-br", UiCursor.ResizeNWSE)]
    [InlineData("wnd-tr", UiCursor.ResizeNESW)]
    public void Each_handle_asks_for_its_pointer_shape(string handle, UiCursor cursor)
    {
        var rig = new Rig("a");
        rig.Frame();
        Assert.Equal(UiCursor.Default, rig.Ctx.List.AsDrawList().Cursor);
        var r = rig.Rect(handle);
        rig.Input(UiInputKind.MouseMove, r.X + r.W / 2, r.Y + r.H / 2);
        rig.Frame();
        Assert.Equal(cursor, rig.Ctx.List.AsDrawList().Cursor);
    }

    [Fact]
    public void Placements_and_z_order_survive_a_ui_reload_and_a_new_session()
    {
        var rig = new Rig("a", "b");
        rig.Windows.Raise("a");
        rig.Frame();
        rig.Drag("wnd-br", -120, -60, window: 1);   // a, now on top, is the second described

        // A UI-only reload: the bytes carried to a fresh instance.
        var reloaded = new UiWindows(new MemoryWindowStore());
        reloaded.Restore(rig.Windows.Carry());
        Assert.Equal(rig.Windows.Top, reloaded.Top);
        Assert.Equal(rig.Windows.Rank("b"), reloaded.Rank("b"));
        Assert.True(reloaded.TryGet("a", out var a));
        Assert.Equal(rig.Place("a"), a);

        // A new session: placed from the store.
        var next = new Rig("a");
        foreach (var id in new[] { "a" })
            if (rig.Store.TryLoad(id, out var p)) next.Store.With(id, p);
        next.Frame();
        Assert.Equal(rig.Place("a"), next.Place("a"));
    }

    [Fact]
    public void A_window_left_off_screen_by_a_resolution_change_is_clamped_with_its_title_bar_reachable()
    {
        var rig = new Rig("a");
        rig.Store.With("a", new UiWindowPlacement(2500, 1050, 600, 400));
        rig.Frame();
        var p = rig.Place();
        Assert.Equal(1920 - UiWindowWidget.KeepVisible, p.X);
        Assert.Equal(1080 - UiWindowWidget.TitleBar - 2 * UiWindowWidget.Ring, p.Y);

        rig.ScreenInfo = Screen(1280, 1024);                    // 1350 x 1080 du
        rig.Frame();
        p = rig.Place();
        Assert.Equal(1350 - UiWindowWidget.KeepVisible, p.X, 1);
        var title = rig.Rect("wnd-title");
        // At least 64 du of the title bar on screen, all of its height.
        Assert.True(title.X + 64 * rig.ScreenInfo.Scale <= 1280 && title.Y >= 0 && title.Y + title.H <= 1024,
                    $"title bar {title} not reachable on 1280x1024");
    }

    [Fact]
    public void A_fresh_window_whose_minimum_exceeds_the_screen_opens_fully_on_it()
    {
        // The descent shops' spec shape at a raised ui_scale on a portrait
        // screen: the default width (94% of the screen) sits under a minimum
        // wider than the screen. The limits widen it to the screen's width;
        // centred by the narrower default, it ran 18 du off the right edge
        // with nothing stored and nothing dragged.
        var screen = UiScale.Screen(1080, 1920, 1.75f);
        float sw = UiScale.DesignWidth(screen), sh = UiScale.DesignHeight(screen);
        var spec = new UiWindowSpec(MathF.Round(sw * 0.94f), MathF.Round(sh * 0.5f), 900, 460, sw, sh, 760);
        var rig = new Rig(spec, "a") { ScreenInfo = screen };
        rig.Frame();
        var p = rig.Place();
        Assert.True(p.W > spec.DefaultW, $"the limits did not widen the window: {p.W}");   // the stimulus reached it
        Assert.True(p.X >= 0f && p.X + p.W <= sw + 0.01f, $"window {p.X}..{p.X + p.W} on a {sw} du screen");
    }

    [Fact]
    public void The_narrow_breakpoint_inside_a_window_is_the_windows()
    {
        var rig = new Rig("a");
        rig.Store.With("a", new UiWindowPlacement(100, 100, 450, 300));
        rig.Frame();
        Assert.True(rig.Screens[0].NarrowInside, "a 450 du window (breakpoint 500) is narrow");
        Assert.False(rig.Screens[0].NarrowOutside, "the 1920 screen around it is not");

        rig.Drag("wnd-br", 200, 0);
        Assert.False(rig.Screens[0].NarrowInside, "at 650 du it is not");
    }

    [Fact]
    public void A_small_console_window_drops_the_suggestions_help_on_a_wide_screen()
    {
        int Helps(float width)
        {
            var s = new UiSampleServices();
            var store = new MemoryWindowStore().With("console", new UiWindowPlacement(100, 100, width, 500));
            var screen = ConsoleSamples.Build(s, ConsoleSamples.Autocompleting(), store);
            var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), new UiFontLibrary(), UiSamplesTests.Language());
            UiSamples.Render(ctx, screen, UiSamples.Screen(1920, 1080), 100);
            return Elements(ctx).Count(e => NameOf(ctx, e).StartsWith("\"Sets the interpolation", StringComparison.Ordinal));
        }
        Assert.True(Helps(1200) > 0, "a wide console shows help");
        Assert.Equal(0, Helps(700));
    }

    [Fact]
    public void A_still_window_allocates_nothing()
    {
        var rig = new Rig("a");
        rig.Ctx.RecordNames = false;   // as in game: names are for tools
        for (int i = 0; i < 60; i++) rig.Frame();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++) rig.Frame();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Dragging_a_window_allocates_nothing()
    {
        var rig = new Rig("a");
        rig.Frame();
        var r = rig.Rect("wnd-br");
        float x = r.X + 4, y = r.Y + 4;
        rig.Ctx.RecordNames = false;   // as in game: names are for tools
        rig.Input(UiInputKind.MouseDown, x, y);
        for (int i = 0; i < 60; i++)
        {
            rig.Input(UiInputKind.MouseMove, x + (i % 20), y + (i % 10));
            rig.Frame();
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++)
        {
            rig.Input(UiInputKind.MouseMove, x + (i % 20), y + (i % 10));
            rig.Frame();
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(rig.Ctx.List.OpCount > 10);
        Assert.NotEqual(Spec.DefaultW, rig.Place().W);    // the stimulus reached it: it was resized
        Assert.Equal(0, bytes);
    }
}

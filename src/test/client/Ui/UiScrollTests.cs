using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>Scrolling: clamping, the bar, keys, both axes, virtual lists, eased steps.</summary>
public class UiScrollTests
{
    private static readonly TestScreen.Describer List150 = (ref UiFrame ui) =>
    {
        using (ui.Scroll("s").Size(Size.Du(200), Size.Du(100)).Name("s"))
            for (int i = 0; i < 3; i++)
                ui.Rect(Tone.Accent, Size.Fill, Size.Du(50)).Name($"row{i}");
    };

    private static void Wheel(UiContext ctx, float notches)
        => ctx.Input(new UiInputEvent(UiInputKind.Wheel, 10, 10, 0, '\0', notches));

    /// <summary>Frames until eased scrolling has arrived.</summary>
    private static void Settle(UiContext ctx, TestScreen.Describer d, ref double now)
    {
        for (int i = 0; i < 90; i++)
            Frame(ctx, Screen(1920, 1080), d, now += 1 / 60.0);
    }

    [Fact]
    public void Wheeling_past_the_end_stores_no_overscroll_so_the_first_notch_back_moves()
    {
        var ctx = Context();
        double now = 1;
        Frame(ctx, Screen(1920, 1080), List150, now);
        for (int i = 0; i < 10; i++)
        {
            Wheel(ctx, -1);
            Settle(ctx, List150, ref now);
        }
        Assert.Equal(-50, Named(ctx, "row0").Rect.Y);   // clamped at the end: 150 of content in 100

        // One notch back must move the list: no dead notches spent unwinding overscroll.
        Wheel(ctx, 1);
        Settle(ctx, List150, ref now);
        Assert.True(Named(ctx, "row0").Rect.Y > -50, $"row0 at {Named(ctx, "row0").Rect.Y} after one notch back");
    }

    [Fact]
    public void The_wheel_step_is_the_themes_and_the_scroll_eases_to_it()
    {
        var theme = PlainTheme();
        theme.ScrollStepDu = 20;
        var ctx = Context(theme);
        TestScreen.Describer tall = (ref UiFrame ui) =>
        {
            using (ui.Scroll("s").Size(Size.Du(200), Size.Du(100)))
                for (int i = 0; i < 10; i++)
                    ui.Rect(Tone.Accent, Size.Fill, Size.Du(50)).Name($"row{i}");
        };
        double now = 1;
        Frame(ctx, Screen(1920, 1080), tall, now);
        Wheel(ctx, -1);
        Frame(ctx, Screen(1920, 1080), tall, now += 1 / 60.0);
        float first = Named(ctx, "row0").Rect.Y;
        Assert.True(first < 0 && first > -20, $"eased: one frame in, row0 at {first}, between 0 and -20");

        Settle(ctx, tall, ref now);
        Assert.Equal(-20, Named(ctx, "row0").Rect.Y);   // one notch is the theme's 20 du, not 48
    }

    [Fact]
    public void A_horizontal_region_scrolls_sideways_with_the_wheel()
    {
        var ctx = Context();
        TestScreen.Describer wide = (ref UiFrame ui) =>
        {
            using (ui.Scroll("h", Space.None, ScrollAxes.Horizontal).Size(Size.Du(200), Size.Du(60)).Name("h"))
                for (int i = 0; i < 5; i++)
                    ui.Rect(Tone.Accent, Size.Du(100), Size.Fill).Key(i).Name($"col{i}");
        };
        double now = 1;
        Frame(ctx, Screen(1920, 1080), wide, now);
        Assert.Equal(0, Named(ctx, "col0").Rect.X);
        Assert.Equal(100, Named(ctx, "col1").Rect.X);    // laid out along x, not squeezed
        Wheel(ctx, -1);
        Settle(ctx, wide, ref now);
        Assert.Equal(-48, Named(ctx, "col0").Rect.X);
        Assert.Equal(0, Named(ctx, "col0").Rect.Y);
    }

    [Fact]
    public void A_both_axis_region_keeps_a_wide_tables_width_and_scrolls_down()
    {
        var ctx = Context();
        TestScreen.Describer table = (ref UiFrame ui) =>
        {
            using (ui.Scroll("t", Space.None, ScrollAxes.Both).Size(Size.Du(200), Size.Du(100)).Name("t"))
            using (ui.Grid([Size.Du(100), Size.Du(100), Size.Du(100), Size.Du(100), Size.Du(100)]).Width(Size.Fill).Name("grid"))
                for (int i = 0; i < 20; i++)
                    ui.Rect(Tone.Accent, Size.Fill, Size.Du(40)).Key(i).Name($"cell{i}");
        };
        double now = 1;
        Frame(ctx, Screen(1920, 1080), table, now);
        Assert.Equal(400, Named(ctx, "cell4").Rect.X);    // 500 du of columns, kept, in a 200 du view
        Assert.Equal(100, Named(ctx, "cell4").Rect.W);
        Assert.Equal(500, Named(ctx, "grid").Rect.W);      // Fill is the content's width, not the view's
        Wheel(ctx, -1);
        Settle(ctx, table, ref now);
        Assert.Equal(-48, Named(ctx, "cell0").Rect.Y);    // the wheel scrolls down
        Assert.Equal(0, Named(ctx, "cell0").Rect.X);
    }

    // ---- the bar, the keys, focus ----

    private static readonly TestScreen.Describer Rows = (ref UiFrame ui) =>
    {
        using (ui.Scroll("list").At(UiAnchor.TopLeft).Size(Size.Du(300), Size.Du(200)).Name("list"))
            for (int i = 0; i < 20; i++)
                ui.Interact(ui.Rect(Tone.SurfaceRaised, Size.Fill, Size.Du(50)).Key(i).Name($"row{i}"));
    };

    private static void Key(UiContext ctx, UiKey key) => ctx.Input(new UiInputEvent(UiInputKind.Key, 0, 0, (int)key, '\0', 0));

    private static void Mouse(UiContext ctx, UiInputKind kind, float x, float y) => ctx.Input(new UiInputEvent(kind, x, y, (int)UiKey.MouseLeft, '\0', 0));

    private static float Offset(UiContext ctx) => -Named(ctx, "row0").Rect.Y;

    [Fact]
    public void An_overflowing_region_draws_a_thumb_sized_to_its_content_and_a_fitting_one_none()
    {
        var ctx = Context();
        Frame(ctx, Screen(1920, 1080), Rows);
        var thumb = Named(ctx, "list thumb").Rect;
        // 200 of 1000 du in view: the thumb is a fifth of the 200 du track, at the top.
        Assert.Equal(40, thumb.H, 1f);
        Assert.Equal(0, thumb.Y, 1f);
        Assert.True(thumb.X >= 300 - 12 && thumb.X + thumb.W <= 300, $"thumb at {thumb}: inside the region's right edge");

        Frame(ctx, Screen(1920, 1080), List150);   // 150 in 100: overflows
        Assert.Single(Elements(ctx), e => NameOf(ctx, e) == "s thumb");
        var fits = Context();
        Frame(fits, Screen(1920, 1080), (ref UiFrame ui) =>
        {
            using (ui.Scroll("f").Size(Size.Du(200), Size.Du(300)))
                ui.Rect(Tone.Accent, Size.Fill, Size.Du(50));
        });
        Assert.DoesNotContain(Elements(fits), e => NameOf(fits, e) == "f thumb");
    }

    [Fact]
    public void Dragging_the_thumb_scrolls_in_proportion()
    {
        var ctx = Context();
        double now = 1;
        Frame(ctx, Screen(1920, 1080), Rows, now);
        var thumb = Named(ctx, "list thumb").Rect;
        float x = thumb.X + thumb.W / 2, y = thumb.Y + thumb.H / 2;
        Mouse(ctx, UiInputKind.MouseDown, x, y);
        Frame(ctx, Screen(1920, 1080), Rows, now += 1 / 60.0);
        Mouse(ctx, UiInputKind.MouseMove, x, y + 80);    // 80 of the 160 px the thumb can travel
        Frame(ctx, Screen(1920, 1080), Rows, now += 1 / 60.0);
        Mouse(ctx, UiInputKind.MouseUp, x, y + 80);
        Settle(ctx, Rows, ref now);
        Assert.Equal(400, Offset(ctx), 1f);              // half of the 800 du of travel
    }

    [Fact]
    public void A_focused_region_pages_and_jumps_with_the_keys()
    {
        var ctx = Context();
        double now = 1;
        Frame(ctx, Screen(1920, 1080), Rows, now);
        Mouse(ctx, UiInputKind.MouseDown, 100, 100);     // a click in the region focuses it
        Mouse(ctx, UiInputKind.MouseUp, 100, 100);
        Frame(ctx, Screen(1920, 1080), Rows, now += 1 / 60.0);

        Key(ctx, UiKey.PageDown);
        Settle(ctx, Rows, ref now);
        Assert.Equal(200, Offset(ctx), 1f);
        Key(ctx, UiKey.Down);
        Settle(ctx, Rows, ref now);
        Assert.Equal(248, Offset(ctx), 1f);
        Key(ctx, UiKey.End);
        Settle(ctx, Rows, ref now);
        Assert.Equal(800, Offset(ctx), 1f);
        Key(ctx, UiKey.PageUp);
        Settle(ctx, Rows, ref now);
        Assert.Equal(600, Offset(ctx), 1f);
        Key(ctx, UiKey.Home);
        Settle(ctx, Rows, ref now);
        Assert.Equal(0, Offset(ctx), 1f);
    }

    [Fact]
    public void Tabbing_to_a_row_below_the_view_scrolls_it_into_view()
    {
        var ctx = Context();
        double now = 1;
        Frame(ctx, Screen(1920, 1080), Rows, now);
        for (int i = 0; i < 6; i++)                      // row0 .. row5; row5 is at 250..300, below the 200 view
        {
            Key(ctx, UiKey.Tab);
            Frame(ctx, Screen(1920, 1080), Rows, now += 1 / 60.0);
        }
        Settle(ctx, Rows, ref now);
        var row = Named(ctx, "row5").Rect;
        Assert.True(row.Y >= 0 && row.Y + row.H <= 200, $"row5 at {row}, not inside the 200 du view");
        Assert.Equal(100, Offset(ctx), 1f);              // just enough: its bottom on the view's bottom
    }

    // ---- the virtual list ----

    /// <summary>One server-browser row: its index as text, on a stripe.</summary>
    internal sealed class ServerRow : UiPanel<int>
    {
        public override void Describe(ref UiFrame ui, in int i)
        {
            using (ui.Row(Space.S).Width(Size.Fill).Fill(Tone.SurfaceRaised))
                ui.Number(i, Text.Body);
        }
    }

    internal static TestScreen.Describer Browser(ServerRow row, int count) => (ref UiFrame ui) =>
    {
        ui.VirtualList("servers", count, 32, row).At(UiAnchor.TopLeft).Size(Size.Du(600), Size.Du(320)).Name("servers");
    };

    [Fact]
    public void A_virtual_list_of_ten_thousand_lays_out_only_the_rows_in_view()
    {
        var ctx = Context();
        var d = Browser(new ServerRow(), 10_000);
        double now = 1;
        Frame(ctx, Screen(1920, 1080), d, now);
        Frame(ctx, Screen(1920, 1080), d, now += 1 / 60.0);
        int rows = Elements(ctx).Count(e => NameOf(ctx, e).StartsWith("ServerRow", StringComparison.Ordinal));
        Assert.InRange(rows, 10, 13);                       // 320 / 32 = 10 in view, plus the partial ones
        Assert.Equal(0, Named(ctx, "ServerRow[row 0]").Rect.Y);

        // The thumb knows the whole list: 320 of 320,000 du.
        Assert.Equal(24, Named(ctx, "servers thumb").Rect.H, 1f);

        Mouse(ctx, UiInputKind.MouseDown, 100, 100);
        Mouse(ctx, UiInputKind.MouseUp, 100, 100);
        Frame(ctx, Screen(1920, 1080), d, now += 1 / 60.0);
        Key(ctx, UiKey.End);
        Settle(ctx, d, ref now);
        var last = Named(ctx, "ServerRow[row 9999]").Rect;
        Assert.Equal(320 - 32, last.Y, 1f);                 // the last row on the view's bottom
        Assert.InRange(Elements(ctx).Count(e => NameOf(ctx, e).StartsWith("ServerRow", StringComparison.Ordinal)), 10, 13);
    }

    [Fact]
    public void Scrolling_a_virtual_list_of_ten_thousand_allocates_nothing()
    {
        var ctx = new UiContext(SourceSharpTheme.Create(), new UiFontLibrary(), UiSamplesTests.Language(), []);
        var row = new ServerRow();
        var screen = new TestScreen("browser", Browser(row, 10_000));
        var info = UiSamples.Screen(1920, 1080);
        double now = 1;
        void F()
        {
            ctx.BeginFrame(info, now += 1 / 60.0);
            var ui = ctx.BeginScreen("browser");
            screen.Describe(ref ui);
            ctx.EndScreen();
            ctx.EndFrame();
        }

        for (int i = 0; i < 60; i++)
            F();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++)
        {
            ctx.Input(new UiInputEvent(UiInputKind.Wheel, 50, 50, 0, '\0', -1));   // scrolling every frame
            F();
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(ctx.List.OpCount > 20, $"{ctx.List.OpCount} ops");
        Assert.Equal(0, bytes);
    }
}

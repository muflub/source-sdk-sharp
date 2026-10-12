using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// Layout (plan_client_ui U1 step 2): anchors and design units, stacks, grids,
/// panels, scroll, nesting with clip intersection and id scopes, at the five
/// mock-up sizes. Positions are checked against the arithmetic the rule
/// promises (du × H/1080 from the anchored edge), not against a recording.
/// </summary>
public class UiLayoutTests
{
    public static TheoryData<int, int> FiveSizes()
    {
        var d = new TheoryData<int, int>();
        foreach (var (w, h) in Sizes) d.Add(w, h);
        return d;
    }

    private static void Near(float expected, float actual, string what)
        => Assert.True(MathF.Abs(expected - actual) <= 1f, $"{what}: expected {expected}, got {actual}");

    [Theory]
    [MemberData(nameof(FiveSizes))]
    public void Anchored_boxes_sit_at_their_edges_in_design_units(int w, int h)
    {
        var ctx = Context();
        Frame(ctx, Screen(w, h), (ref UiFrame ui) =>
        {
            foreach (var a in Enum.GetValues<UiAnchor>())
                ui.Box().At(a).Inset(Space.L).Size(Size.Du(100), Size.Du(50)).Name(a.ToString()).Dispose();
        });

        float s = UiScale.For(w, h), inset = 16 * s, bw = 100 * s, bh = 50 * s;
        var tl = Named(ctx, "TopLeft").Rect;
        var br = Named(ctx, "BottomRight").Rect;
        var c = Named(ctx, "Centre").Rect;
        var top = Named(ctx, "Top").Rect;

        Near(inset, tl.X, "TopLeft x");
        Near(inset, tl.Y, "TopLeft y");
        Near(bw, tl.W, "width scales with height");
        Near(w - inset - bw, br.X, "BottomRight x");
        Near(h - inset - bh, br.Y, "BottomRight y");
        Near((w - bw) / 2 + inset, c.X, "Centre x (inset moves right)");
        Near((w - bw) / 2 + inset, top.X, "Top x");

        foreach (var e in Elements(ctx))
            Assert.True(e.Rect.X >= 0 && e.Rect.Y >= 0 && e.Rect.X + e.Rect.W <= w && e.Rect.Y + e.Rect.H <= h,
                        $"{NameOf(ctx, e)} off screen at {w}x{h}: {e.Rect}");
    }

    [Theory]
    [MemberData(nameof(FiveSizes))]
    public void Font_heights_scale_with_the_screen(int w, int h)
    {
        var ctx = Context();
        Frame(ctx, Screen(w, h), (ref UiFrame ui) => { ui.Number(100); });
        var fonts = ctx.List.AsDrawList().Fonts;
        Assert.Equal((int)MathF.Round(40 * UiScale.For(w, h)), fonts[0].PxHeight);
    }

    [Fact]
    public void A_vertical_stack_places_children_in_order_and_fill_takes_the_rest()
    {
        var ctx = Context();
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) =>
        {
            using (ui.Column(Space.S).Size(Size.Du(200), Size.Du(400)).Name("col"))
            {
                ui.Rect(Tone.Accent, Size.Fill, Size.Du(50)).Name("a");
                ui.Rect(Tone.Accent, Size.Fill, Size.Fill).Name("fill");
                ui.Rect(Tone.Accent, Size.Du(80), Size.Du(30)).Name("b");
            }
        });

        var a = Named(ctx, "a").Rect;
        var fill = Named(ctx, "fill").Rect;
        var b = Named(ctx, "b").Rect;
        Assert.Equal(0, a.Y);
        Assert.Equal(200, a.W);              // Stretch is a column's default
        Assert.Equal(58, fill.Y);            // 50 + gap 8
        Assert.Equal(400 - 50 - 30 - 16, fill.H);
        Assert.Equal(400 - 30, b.Y);
        Assert.Equal(200, b.W);              // stretched across, as the column says
    }

    [Fact]
    public void A_row_centres_across_and_space_between_pushes_to_the_ends()
    {
        var ctx = Context();
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) =>
        {
            using (ui.Row(Space.S).Size(Size.Du(300), Size.Du(60)).Align(Align.SpaceBetween, Align.Centre).Name("row"))
            {
                ui.Rect(Tone.Accent, Size.Du(40), Size.Du(20)).Name("first");
                ui.Rect(Tone.Accent, Size.Du(40), Size.Du(40)).Name("last");
            }
        });

        var first = Named(ctx, "first").Rect;
        var last = Named(ctx, "last").Rect;
        Assert.Equal(0, first.X);
        Assert.Equal(20, first.Y);           // (60 - 20) / 2
        Assert.Equal(260, last.X);           // at the far end
        Assert.Equal(10, last.Y);
    }

    [Fact]
    public void A_grid_sizes_columns_fixed_auto_and_shared()
    {
        var ctx = Context();
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) =>
        {
            using (ui.Grid([Size.Du(100), Size.Auto, Size.Fr(1), Size.Fr(2)], Space.S, Space.XS).Width(Size.Du(800)).Name("grid"))
            {
                for (int r = 0; r < 2; r++)
                {
                    ui.Rect(Tone.Accent, Size.Auto, Size.Du(30)).Name($"r{r}c0");
                    ui.Rect(Tone.Accent, Size.Du(r == 0 ? 60 : 90), Size.Du(20)).Name($"r{r}c1");
                    ui.Rect(Tone.Accent, Size.Auto, Size.Du(10)).Name($"r{r}c2");
                    ui.Rect(Tone.Accent, Size.Auto, Size.Du(10)).Name($"r{r}c3");
                }
            }
        });

        // 800 - 100 - 90 (auto: widest) - 3 gaps of 8 = 586 shared 1:2.
        Assert.Equal(new UiRect(0, 0, 100, 30), Named(ctx, "r0c0").Rect);
        Assert.Equal(new UiRect(108, 0, 90, 30), Named(ctx, "r0c1").Rect);
        Assert.Equal(206, Named(ctx, "r0c2").Rect.X);
        Assert.Equal(MathF.Round(586 / 3f), Named(ctx, "r0c2").Rect.W);
        Assert.Equal(MathF.Round(800f) - MathF.Round(206 + 586 / 3f + 8), Named(ctx, "r0c3").Rect.W);
        Assert.Equal(34, Named(ctx, "r1c0").Rect.Y);      // row 30 + row gap 4
    }

    [Fact]
    public void Nested_clips_intersect_down_the_tree_in_the_list_and_the_svg()
    {
        var ctx = Context();
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) =>
        {
            using (ui.Box().At(UiAnchor.TopLeft).Inset(Raw.Du(100)).Size(Size.Du(300), Size.Du(200)).Clip().Name("outer"))
            using (ui.Box().At(UiAnchor.TopLeft).Inset(Raw.Du(150)).Size(Size.Du(300), Size.Du(300)).Clip().Name("inner"))
                ui.Rect(Tone.Accent, Size.Du(500), Size.Du(500)).Name("child");
        });

        var list = ctx.List.AsDrawList();
        Assert.Equal(2, list.Scissors.Length);
        Assert.Equal(new UiRect(100, 100, 300, 200), list.Scissors[0].Rect);
        // inner is at 250,250 (100 + 150): its clip is its rect ∩ outer's.
        Assert.Equal(new UiRect(250, 250, 150, 50), list.Scissors[1].Rect);

        // The child is drawn (in tree order) inside both pushes.
        string svg = Svg(ctx);
        int push0 = svg.IndexOf("clip-path=\"url(#c0)\"", StringComparison.Ordinal);
        int push1 = svg.IndexOf("clip-path=\"url(#c1)\"", StringComparison.Ordinal);
        int child = svg.IndexOf($"data-e=\"{Array.FindIndex(Elements(ctx), e => NameOf(ctx, e) == "child")}\"", StringComparison.Ordinal);
        int pop = svg.IndexOf("</g>", StringComparison.Ordinal);
        Assert.True(push0 >= 0 && push0 < push1 && push1 < child && child < pop, $"order: {push0} {push1} {child} {pop}");
        Assert.Contains("<clipPath id=\"c1\"><rect x=\"250\" y=\"250\" width=\"150\" height=\"50\"/></clipPath>", svg);
    }

    private sealed class Counter : UiPanel<int>
    {
        public override void Describe(ref UiFrame ui, in int props)
        {
            ref int clicks = ref ui.State<int>("clicks");
            clicks += props;
            ui.Number(clicks).Name("count");
            using (ui.Scroll("list").Size(Size.Du(100), Size.Du(50)))
                for (int i = 0; i < 5; i++)
                    ui.Rect(Tone.Accent, Size.Fill, Size.Du(40));
        }
    }

    [Fact]
    public void Two_instances_of_one_sub_panel_keep_separate_state_and_ids()
    {
        var ctx = Context();
        var panel = new Counter();
        TestScreen.Describer twice = (ref UiFrame ui) =>
        {
            using (ui.Row(Space.S))
            {
                ui.Sub("left", panel, 1);
                ui.Sub("right", panel, 10);
            }
        };

        Frame(ctx, Screen(1920, 1080), twice);
        Frame(ctx, Screen(1920, 1080), twice);
        Assert.Empty(ctx.Report.Collisions);

        // Scroll the left one only: its list moves, the right one's does not.
        var lists = Elements(ctx).Where(e => NameOf(ctx, e) == "list").ToArray();
        Assert.Equal(2, lists.Length);
        Assert.NotEqual(lists[0].Id, lists[1].Id);
        var l = lists[0].Rect;
        ctx.Input(new UiInputEvent(UiInputKind.Wheel, l.X + 5, l.Y + 5, 0, '\0', -1));
        for (int f = 1; f <= 60; f++)     // scrolling eases: let it arrive
            Frame(ctx, Screen(1920, 1080), twice, 1.0 + f / 60.0);

        var firstRows = Elements(ctx).Where(e => e.Kind == UiElementKind.Spacer).ToArray();
        Assert.Equal(10, firstRows.Length);
        Assert.True(firstRows[0].Rect.Y < firstRows[5].Rect.Y, $"left scrolled up: {firstRows[0].Rect.Y} vs right {firstRows[5].Rect.Y}");

        // Per-instance State<int>: 62 frames (2, then 60 to let the scroll arrive) of +1 and of +10.
        var counts = Elements(ctx).Select((e, i) => (e, i)).Where(p => NameOf(ctx, p.e) == "count")
                                  .Select(p => RunText(ctx, p.i)).ToArray();
        Assert.Equal(["62", "620"], counts);
    }

    [Fact]
    public void Two_keyed_elements_with_one_id_are_reported_as_a_collision()
    {
        var ctx = Context();
        Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) =>
        {
            ui.Box("same").Dispose();
            ui.Box("same").Dispose();
        });

        var c = Assert.Single(ctx.Report.Collisions);
        Assert.Equal("same", c.First);
    }

    [Fact]
    public void A_container_left_open_fails_the_frame_naming_it()
    {
        var ctx = Context();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Frame(ctx, Screen(1920, 1080), (ref UiFrame ui) => { ui.Column().Name("forgotten"); }));
        Assert.Contains("forgotten", ex.Message);
    }

    [Fact]
    public void A_scroll_region_clamps_to_its_content()
    {
        var ctx = Context();
        TestScreen.Describer d = (ref UiFrame ui) =>
        {
            using (ui.Scroll("s").Size(Size.Du(100), Size.Du(100)).Name("s"))
                for (int i = 0; i < 3; i++)
                    ui.Rect(Tone.Accent, Size.Fill, Size.Du(50)).Name($"row{i}");
        };
        Frame(ctx, Screen(1920, 1080), d);
        ctx.Input(new UiInputEvent(UiInputKind.Wheel, 10, 10, 0, '\0', -10));
        for (int f = 1; f <= 60; f++)     // scrolling eases: let it arrive
            Frame(ctx, Screen(1920, 1080), d, 1.0 + f / 60.0);

        // 150 of content in 100: at most 50 up.
        Assert.Equal(-50, Named(ctx, "row0").Rect.Y);
    }

    [Theory]
    [InlineData(1920, 1080, 1f)]
    [InlineData(2560, 1080, 1f)]            // wider: by height, as before
    [InlineData(3440, 1440, 1440 / 1080f)]
    [InlineData(1024, 768, 768 / 1080f)]    // 4:3: still by height (0.711)
    [InlineData(1280, 1024, 1024 / 1080f)]  // 5:4: by height (0.948), design width 1350: narrow
    [InlineData(1080, 1920, 1080 / 1280f)]  // portrait: by width against 1280 du (0.844)
    [InlineData(1440, 2560, 1440 / 1280f)]
    public void The_scale_is_height_driven_down_to_a_1280_du_design_width(int w, int h, float expected)
    {
        Assert.Equal(expected, UiScale.For(w, h), 4);
        Assert.Equal(expected * 1.5f, UiScale.For(w, h, 1.5f), 4);
        Assert.True(UiScale.DesignWidth(UiScale.Screen(w, h)) >= 1279.9f, "at ui_scale 1 the design width is never under 1280");
    }
}

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The toolkit's dropdown (<see cref="UiFormWidgets.Choice"/>): its open list is
/// a bounded box beside its field that scrolls, the keys work as stock vgui's
/// ComboBox and Menu do, and typing filters a long list. The user's report
/// (2026-11-15, the create-server map field): the list ran from the window's
/// top past its bottom over the field, with no scrolling, no arrow keys and no
/// filtering.
/// </summary>
public class UiChoiceTests
{
    private static readonly string[] Prefixes = ["arena", "cp", "ctf", "koth"];

    /// <summary>200 names, a long list: arena_000, cp_001, ctf_002, koth_003, arena_004, ...</summary>
    private static string[] Maps(int n = 200) => [.. Enumerable.Range(0, n).Select(i => $"{Prefixes[i % 4]}_{i:D3}")];

    private sealed class Rig
    {
        public readonly UiContext Ctx = UiTestKit.Context();
        public string[] Names = Maps();
        public int Chosen;
        public UiAnchor At = UiAnchor.Top;
        public float InsetY = 100;
        public bool InScroll;
        public bool EscapeSeenByParent;
        private double _now = 1.0;

        public void Frame()
        {
            UiTestKit.Frame(Ctx, UiTestKit.Screen(1920, 1080), Describe, _now);
            _now += 1 / 60.0;
        }

        public void Frames(int n)
        {
            for (int i = 0; i < n; i++)
                Frame();
        }

        private void Describe(ref UiFrame ui)
        {
            // What a window does before its body: it takes Escape for itself.
            EscapeSeenByParent |= ui.ConsumeKey((int)UiKey.Escape);
            if (InScroll)
            {
                using (ui.Scroll("outer").Size(Size.Du(400), Size.Du(300)))
                {
                    using (ui.Box().Size(Size.Fill, Size.Du(40)))
                        Chosen = ui.Choice("pick", Chosen, Names.Length, new UiChoiceList(Names));
                    ui.Spacer(Size.Fill, Size.Du(1000));
                }
                return;
            }
            using (ui.Box().At(At).Inset(Space.XL, Raw.Du(InsetY)).Size(Size.Du(400), Size.Du(40)))
                Chosen = ui.Choice("pick", Chosen, Names.Length, new UiChoiceList(Names));
        }

        public UiElementInfo Named(string name) => UiTestKit.Named(Ctx, name);

        public bool Has(string name) => UiTestKit.Elements(Ctx).Any(e => UiTestKit.NameOf(Ctx, e) == name);

        public UiRect Field => Named("pick").Rect;

        public UiRect List => Named("choice-box").Rect;

        public UiRect Rows => Named("choice-rows").Rect;

        public void Click(UiRect r) => Click(r.X + r.W / 2, r.Y + r.H / 2);

        public void Click(float x, float y)
        {
            Ctx.Input(new UiInputEvent(UiInputKind.MouseMove, x, y, 0, '\0', 0));
            Ctx.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
            Frame();
            Ctx.Input(new UiInputEvent(UiInputKind.MouseUp, x, y, (int)UiKey.MouseLeft, '\0', 0));
            Frame();
        }

        public void Key(UiKey key, int times = 1)
        {
            for (int i = 0; i < times; i++)
                Ctx.Input(new UiInputEvent(UiInputKind.Key, 0, 0, (int)key, '\0', 0));
            Frame();
        }

        public void Type(string text)
        {
            foreach (char c in text)
                Ctx.Input(new UiInputEvent(UiInputKind.Char, 0, 0, 0, c, 0));
            Frame();
        }

        /// <summary>Opens the list with a click on the field; two frames first so the field has a place.</summary>
        public Rig Open()
        {
            Frames(2);
            Click(Field);
            Frames(30);
            return this;
        }

        /// <summary>The rows drawn inside the list's view, by their choice's index, in order.</summary>
        public int[] RowsInView()
        {
            var view = Rows;
            return [.. UiTestKit.Elements(Ctx)
                        .Where(e => UiTestKit.NameOf(Ctx, e).StartsWith("row ", StringComparison.Ordinal)
                                    && e.Rect.Y >= view.Y - 0.5f && e.Rect.Y + e.Rect.H <= view.Y + view.H + 0.5f)
                        .Select(e => int.Parse(UiTestKit.NameOf(Ctx, e)[4..]))];
        }

        /// <summary>The text drawn inside the element named <paramref name="name"/>.</summary>
        public string TextIn(string name)
        {
            var all = UiTestKit.Elements(Ctx);
            int owner = Array.FindIndex(all, e => UiTestKit.NameOf(Ctx, e) == name);
            return string.Concat(Enumerable.Range(0, all.Length).Where(i => Within(all, i, owner)).Select(i => UiTestKit.RunText(Ctx, i)));
        }

        private static bool Within(UiElementInfo[] all, int i, int owner)
        {
            for (int p = i; p >= 0; p = all[p].Parent)
                if (p == owner)
                    return true;
            return false;
        }
    }

    private const float Row = 32f, Frame = 2 * 4f;

    [Fact]
    public void The_open_list_is_a_box_of_twelve_rows_under_its_field()
    {
        var r = new Rig().Open();
        Assert.Equal(UiFormWidgets.ChoiceRows, 12);
        Assert.InRange(r.List.Y, r.Field.Y + r.Field.H, r.Field.Y + r.Field.H + 8);       // under the field, not over it
        Assert.Equal(12 * Row, r.Rows.H, 0.5f);
        Assert.Equal(12 * Row + Frame, r.List.H, 1f);
        Assert.Equal(r.Field.W, r.List.W, 0.5f);                                          // as wide as the field
    }

    [Fact]
    public void With_no_room_below_the_list_opens_above_its_field()
    {
        var r = new Rig { At = UiAnchor.BottomLeft, InsetY = 100 }.Open();
        Assert.True(r.List.Y + r.List.H <= r.Field.Y && r.List.Y + r.List.H >= r.Field.Y - 8,
                    $"list {r.List}, field {r.Field}");                                  // its bottom on the field's top
        Assert.Equal(12 * Row, r.Rows.H, 0.5f);
    }

    [Fact]
    public void With_room_for_neither_the_list_shortens_on_the_roomier_side_and_keeps_off_the_field()
    {
        var r = new Rig { At = UiAnchor.TopLeft, InsetY = 280 };
        r.Ctx.PopupBandDu = 480;                                                          // a band at the bottom: 600 du left
        r.Open();
        Assert.True(r.List.Y >= r.Field.Y + r.Field.H, $"list at {r.List.Y}, field ends {r.Field.Y + r.Field.H}");
        Assert.True(r.List.Y + r.List.H <= 600f, $"list ends {r.List.Y + r.List.H}, past the band at 600");
        Assert.InRange(r.Rows.H / Row, 3f, 11f);
    }

    [Fact]
    public void The_chosen_row_is_in_view_when_the_list_opens()
    {
        var r = new Rig { Chosen = 150 }.Open();
        Assert.Contains(150, r.RowsInView());
        Assert.DoesNotContain(0, r.RowsInView());
    }

    [Fact]
    public void The_wheel_scrolls_the_open_list()
    {
        var r = new Rig().Open();
        Assert.Equal(0, r.RowsInView()[0]);
        var list = r.Rows;
        r.Ctx.Input(new UiInputEvent(UiInputKind.MouseMove, list.X + 20, list.Y + 20, 0, '\0', 0));
        r.Ctx.Input(new UiInputEvent(UiInputKind.Wheel, list.X + 20, list.Y + 20, 0, '\0', -3));
        r.Frames(60);
        Assert.True(r.RowsInView()[0] > 0, $"first row in view {r.RowsInView()[0]}");
    }

    [Fact]
    public void Long_lists_get_a_scroll_bar()
    {
        var r = new Rig().Open();
        Assert.True(r.Has("choice-rows thumb"));
    }

    [Fact]
    public void Down_moves_the_highlight_and_enter_chooses_it()
    {
        var r = new Rig().Open();
        r.Key(UiKey.Down, 3);
        r.Key(UiKey.Up);
        r.Key(UiKey.Enter);
        Assert.Equal(2, r.Chosen);
        Assert.False(r.Has("choice-box"));
    }

    [Fact]
    public void Page_down_moves_the_highlight_a_page()
    {
        var r = new Rig().Open();
        r.Key(UiKey.PageDown);
        r.Key(UiKey.Enter);
        Assert.Equal(UiFormWidgets.ChoiceRows - 1, r.Chosen);
    }

    [Fact]
    public void End_highlights_the_last_and_home_the_first()
    {
        var r = new Rig { Chosen = 5 }.Open();
        r.Key(UiKey.End);
        r.Key(UiKey.Enter);
        Assert.Equal(199, r.Chosen);
        r.Click(r.Field);
        r.Key(UiKey.Home);
        r.Key(UiKey.Enter);
        Assert.Equal(0, r.Chosen);
    }

    [Fact]
    public void The_highlight_moved_by_keys_is_scrolled_into_view()
    {
        var r = new Rig().Open();
        r.Key(UiKey.Down, 20);
        r.Frames(30);
        Assert.Contains(20, r.RowsInView());
    }

    [Fact]
    public void Escape_closes_the_list_without_a_change_and_its_window_never_sees_it()
    {
        var r = new Rig { Chosen = 7 }.Open();
        r.Key(UiKey.Down, 2);
        r.Key(UiKey.Escape);
        Assert.False(r.Has("choice-box"));
        Assert.Equal(7, r.Chosen);
        Assert.False(r.EscapeSeenByParent);
    }

    [Fact]
    public void A_click_on_a_row_chooses_it()
    {
        var r = new Rig().Open();
        r.Click(r.Named("row 4").Rect);
        r.Frame();
        Assert.Equal(4, r.Chosen);
        Assert.False(r.Has("choice-box"));
    }

    [Fact]
    public void A_press_outside_the_field_and_list_closes_the_list()
    {
        var r = new Rig().Open();
        r.Click(1800, 1000);
        Assert.False(r.Has("choice-box"));
        Assert.Equal(0, r.Chosen);
    }

    [Fact]
    public void A_closed_focused_field_steps_its_choice_with_the_arrows()
    {
        var r = new Rig { Chosen = 10 }.Open();
        r.Click(r.Field);                                   // closed again, and focused
        Assert.False(r.Has("choice-box"));
        r.Key(UiKey.Down, 2);
        Assert.Equal(12, r.Chosen);
        r.Key(UiKey.Up);
        Assert.Equal(11, r.Chosen);
    }

    [Fact]
    public void An_enclosing_scroll_region_leaves_a_focused_field_its_arrows()
    {
        var r = new Rig { InScroll = true }.Open();
        r.Click(r.Field);
        float y = r.Field.Y;
        r.Key(UiKey.Down);
        r.Frames(30);
        Assert.Equal(1, r.Chosen);
        Assert.Equal(y, r.Field.Y, 0.5f);                   // the region did not scroll as well
    }

    [Fact]
    public void Typing_filters_a_long_list_to_the_names_holding_the_text_in_any_case()
    {
        var r = new Rig().Open();
        r.Type("CP_");
        r.Frames(5);
        int[] rows = r.RowsInView();
        Assert.NotEmpty(rows);
        Assert.All(rows, i => Assert.StartsWith("cp_", r.Names[i], StringComparison.Ordinal));
        Assert.Equal(1, rows[0]);
    }

    [Fact]
    public void The_typed_filter_shows_in_the_field()
    {
        var r = new Rig().Open();
        r.Type("koth");
        Assert.Equal("koth", r.TextIn("pick"));
    }

    [Fact]
    public void The_first_match_is_highlighted_and_enter_takes_it()
    {
        var r = new Rig { Chosen = 100 }.Open();
        r.Type("ctf_1");
        r.Key(UiKey.Enter);
        Assert.Equal("ctf_102", r.Names[r.Chosen]);
    }

    [Fact]
    public void Backspace_takes_the_filter_back_and_an_empty_one_shows_every_name()
    {
        var r = new Rig().Open();
        r.Type("ko");
        Assert.All(r.RowsInView(), i => Assert.StartsWith("koth", r.Names[i], StringComparison.Ordinal));
        r.Key(UiKey.Backspace);
        r.Key(UiKey.Backspace);
        r.Frames(5);
        Assert.Equal([.. Enumerable.Range(0, 12)], r.RowsInView());
    }

    [Fact]
    public void A_filter_nothing_matches_says_so()
    {
        var r = new Rig().Open();
        r.Type("zzz");
        Assert.False(r.Has("choice-rows"));
        Assert.Contains("SS_UI_Choice_NoMatch", r.TextIn("choice-box"), StringComparison.Ordinal);
    }

    [Fact]
    public void Typing_into_a_closed_focused_long_list_opens_it_filtered()
    {
        var r = new Rig().Open();
        r.Click(r.Field);
        Assert.False(r.Has("choice-box"));
        r.Type("ctf");
        r.Frames(5);
        Assert.True(r.Has("choice-box"));
        Assert.All(r.RowsInView(), i => Assert.StartsWith("ctf", r.Names[i], StringComparison.Ordinal));
    }

    [Fact]
    public void A_short_list_does_not_filter_and_a_key_jumps_to_the_next_name_starting_with_it()
    {
        var r = new Rig { Names = ["Easy", "Normal", "Hard", "Nightmare"] }.Open();
        r.Type("n");
        r.Type("n");
        Assert.Equal(4, r.RowsInView().Length);            // every name still listed
        r.Key(UiKey.Enter);
        Assert.Equal(3, r.Chosen);                          // Normal, then Nightmare
    }
}

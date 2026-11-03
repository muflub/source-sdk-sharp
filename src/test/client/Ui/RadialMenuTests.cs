using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>The radial menu: which choice the pointer means, its input, and the way through menus.</summary>
public class RadialMenuTests
{
    // ---- the ring ----

    [Theory]
    [InlineData(0f, -100f, 4, 0)]    // up: the first
    [InlineData(100f, 0f, 4, 1)]     // right: clockwise
    [InlineData(0f, 100f, 4, 2)]
    [InlineData(-100f, 0f, 4, 3)]
    [InlineData(-1f, -100f, 4, 0)]   // just left of up is still the first, not the last
    [InlineData(70f, -70f, 8, 1)]
    public void A_direction_means_the_nearest_item(float dx, float dy, int count, int item)
        => Assert.Equal(item, UiRadialLayout.ItemAt(dx, dy, count, 50f));

    [Fact]
    public void The_hub_means_no_item()
    {
        Assert.Equal(-1, UiRadialLayout.ItemAt(10f, 10f, 4, 50f));
        Assert.Equal(-1, UiRadialLayout.ItemAt(100f, 0f, 0, 50f));
    }

    [Fact]
    public void An_item_is_drawn_where_the_pointer_means_it()
    {
        for (int n = 2; n <= UiRadialLayout.MaxItems; n++)
            for (int i = 0; i < n; i++)
            {
                var (x, y) = UiRadialLayout.Centre(i, n, 200f, 200f);
                Assert.Equal(i, UiRadialLayout.ItemAt(x, y, n, 50f));
            }
    }

    [Fact]
    public void Wedges_meet_round_the_ring_with_their_gaps_and_centre_on_their_items()
    {
        var (start, span) = UiRadialLayout.Wedge(0, 4, 0.1f);
        Assert.Equal(MathF.Tau / 4f - 0.1f, span, 4);
        Assert.Equal(0f, start + span / 2f, 4);      // the first is centred at the top
        Assert.Equal(MathF.Tau / 4f * 2f, UiRadialLayout.Wedge(2, 4, 0.1f).Start + span / 2f, 4);
    }

    /// <summary>A sector stays inside its annulus and within its angles, as convex quads.</summary>
    [Fact]
    public void A_sector_draws_only_inside_its_ring_slice()
    {
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), new UiFontLibrary(), UiSamplesTests.Language());
        UiTestKit.Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) =>
        {
            ui.Sector(Tone.Accent, 200f, 0f, MathF.PI / 2f, 0.5f).At(UiAnchor.TopLeft).Inset(new UiLength(100f, false), new UiLength(100f, false));
        }, 1.0);
        var list = ctx.List.AsDrawList();
        Assert.True(list.Polygons.Length > 2, $"{list.Polygons.Length} quads");
        foreach (var p in list.Polygons)
        {
            Assert.Equal(4, p.VertexCount);
            for (int i = 0; i < p.VertexCount; i++)
            {
                var v = list.Vertices[p.VertexStart + i];
                float dx = v.X - 200f, dy = v.Y - 200f, radius = MathF.Sqrt(dx * dx + dy * dy);
                Assert.InRange(radius, 49.9f, 100.1f);
                Assert.True(dx >= -0.01f && dy <= 0.01f, $"({dx},{dy}) outside the top right quarter");
            }
        }
    }

    // ---- the screen ----

    private static (UiContext, FakeRadialMenu, RadialMenu) Screen(params UiRadialItem[] items)
    {
        var menu = new FakeRadialMenu("Title", items);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), new UiFontLibrary(), UiSamplesTests.Language());
        return (ctx, menu, new RadialMenu(menu, menu));
    }

    private static void Draw(UiContext ctx, RadialMenu screen)
        => UiTestKit.Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => screen.Describe(ref ui), 1.0);

    private static readonly UiRadialItem A = new("One", null, true), B = new("Two", null, true);

    // A held key: the place under the pointer is what letting go takes.
    private static (RadialMenus Menus, List<string> Said) Held(params RadialEntry[] more)
    {
        var said = new List<string>();
        var menus = Menus(out _);
        menus.Show(new RadialPage("T",
        [
            new RadialEntry("One", () => said.Add("one")),
            new RadialEntry("Two", () => said.Add("two")),
            .. more,
        ]), "key");
        return (menus, said);
    }

    [Fact]
    public void Letting_go_on_a_choice_takes_it_and_closes()
    {
        var (menus, said) = Held();
        menus.Hover(1);
        menus.Release("key");
        Assert.Equal(["two"], said);
        Assert.False(menus.Showing);
    }

    [Fact]
    public void Letting_go_on_the_hub_takes_nothing_and_closes()
    {
        var (menus, said) = Held();
        menus.Hover(1);
        menus.Hover(-1);
        menus.Release("key");
        Assert.Empty(said);
        Assert.False(menus.Showing);
    }

    [Fact]
    public void Letting_go_with_the_pointer_never_moved_takes_nothing()
    {
        var (menus, said) = Held();
        menus.Release("key");
        Assert.Empty(said);
        Assert.False(menus.Showing);
    }

    [Fact]
    public void Opening_a_menu_forgets_the_last_one_s_hover()
    {
        var (menus, said) = Held();
        menus.Hover(1);
        menus.Show(new RadialPage("T", [new RadialEntry("A", () => said.Add("a")), new RadialEntry("B", () => said.Add("b"))]), "key");
        menus.Release("key");
        Assert.Empty(said);
    }

    [Fact]
    public void Letting_go_on_one_that_cannot_be_chosen_takes_nothing_and_closes()
    {
        bool taken = false;
        var (menus, said) = Held(new RadialEntry("No", () => taken = true, Enabled: false));
        menus.Hover(2);
        menus.Release("key");
        Assert.False(taken);
        Assert.Empty(said);
        Assert.False(menus.Showing);
    }

    [Fact]
    public void Clicking_first_takes_one_and_the_let_go_after_takes_nothing_more()
    {
        var (menus, said) = Held();
        menus.Hover(0);
        menus.Choose(0);
        menus.Release("key");
        Assert.Equal(["one"], said);
    }

    [Fact]
    public void Another_key_s_let_go_leaves_the_menu_up()
    {
        var (menus, said) = Held();
        menus.Hover(0);
        menus.Release("other");
        Assert.Empty(said);
        Assert.True(menus.Showing);
    }

    [Fact]
    public void Letting_go_on_a_folder_opens_it_and_the_next_page_has_no_hover_yet()
    {
        var said = new List<string>();
        var menus = Menus(out _);
        menus.Show(new RadialPage("T", [new RadialEntry("Folder", Open: () => new RadialPage("In", [new RadialEntry("Leaf", () => said.Add("leaf"))]))]), "key");
        menus.Hover(0);
        menus.Release("key");
        Assert.Equal("In", menus.Title);
        menus.Release("key");
        Assert.Empty(said);
    }

    [Fact]
    public void The_screen_reports_what_is_under_the_pointer()
    {
        var (ctx, menu, screen) = Screen(A, B);
        ctx.Input(new UiInputEvent(UiInputKind.MouseMove, 960, 1000, 0, '\0', 0));   // straight down: the second of two
        Draw(ctx, screen);
        Assert.Equal(1, menu.Hovered);

        ctx.Input(new UiInputEvent(UiInputKind.MouseMove, 960, 540, 0, '\0', 0));    // the hub
        Draw(ctx, screen);
        Assert.Equal(-1, menu.Hovered);
    }

    private sealed class Keys(int code, string command) : IRadialKeys
    {
        public bool IsBoundTo(int buttonCode, string bound) => buttonCode == code && bound == command;
    }

    private const int KeyO = 25;

    private static RadialMenus Up(string? command)
    {
        var menus = new RadialMenus(new Pointer(), new Keys(KeyO, "callvote"));
        menus.Show(new RadialPage("T", [new RadialEntry("One")]), "owner", command: command);
        return menus;
    }

    [Fact]
    public void The_key_that_opened_it_closes_it()
    {
        var menus = Up("callvote");
        Assert.True(menus.Pressed(KeyO));
        Assert.False(menus.Showing);
    }

    [Fact]
    public void Another_key_leaves_it_up_and_unclaimed()
    {
        var menus = Up("callvote");
        Assert.False(menus.Pressed(KeyO + 1));
        Assert.True(menus.Showing);
    }

    [Fact]
    public void A_menu_no_command_opened_has_no_such_key()
    {
        var menus = Up(null);
        Assert.False(menus.Pressed(KeyO));
        Assert.True(menus.Showing);
    }

    [Fact]
    public void With_nothing_up_the_key_is_not_claimed()
    {
        var menus = Up("callvote");
        menus.Close();
        Assert.False(menus.Pressed(KeyO));
    }

    [Fact]
    public void The_screen_hands_on_a_pressed_key()
    {
        var (ctx, menu, screen) = Screen(A, B);
        menu.Claims = KeyO;
        ctx.Input(new UiInputEvent(UiInputKind.Key, 0, 0, KeyO, '\0', 0));
        Draw(ctx, screen);
        Assert.Equal(["pressed"], menu.Done);
    }

    [Fact]
    public void A_choice_that_keeps_the_menu_up_runs_and_stays()
    {
        int ran = 0;
        var menus = Menus(out _);
        menus.Show(new RadialPage("T", [new RadialEntry("Add", () => ran++, KeepOpen: true)]));
        menus.Choose(0);
        menus.Choose(0);
        Assert.Equal(2, ran);
        Assert.True(menus.Showing);
    }

    [Fact]
    public void Hidden_it_draws_nothing_and_does_not_want_the_mouse()
    {
        var (ctx, menu, screen) = Screen(A, B);
        menu.Showing = false;
        Draw(ctx, screen);
        Assert.False(screen.WantsMouse);
        Assert.DoesNotContain(UiTestKit.Elements(ctx), e => UiTestKit.NameOf(ctx, e).Contains("One", StringComparison.Ordinal));
    }

    [Fact]
    public void Up_it_wants_the_mouse_and_draws_every_choice()
    {
        var (ctx, _, screen) = Screen(A, B);
        Draw(ctx, screen);
        Assert.True(screen.WantsMouse);
        var names = UiTestKit.Elements(ctx).Select(e => UiTestKit.NameOf(ctx, e)).ToList();
        Assert.Contains(names, n => n.Contains("One", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("Two", StringComparison.Ordinal));
    }

    [Fact]
    public void A_number_takes_its_choice()
    {
        var (ctx, menu, screen) = Screen(A, B);
        Draw(ctx, screen);
        ctx.Input(new UiInputEvent(UiInputKind.Key, 0, 0, (int)UiKey.Key0 + 2, '\0', 0));
        Draw(ctx, screen);
        Assert.Equal(["choose 1"], menu.Done);
    }

    [Fact]
    public void Escape_goes_back()
    {
        var (ctx, menu, screen) = Screen(A, B);
        Draw(ctx, screen);
        ctx.Input(new UiInputEvent(UiInputKind.Key, 0, 0, (int)UiKey.Escape, '\0', 0));
        Draw(ctx, screen);
        Assert.Equal(["back"], menu.Done);
    }

    private static void Click(UiContext ctx, RadialMenu screen, float x, float y)
    {
        ctx.Input(new UiInputEvent(UiInputKind.MouseMove, x, y, 0, '\0', 0));
        Draw(ctx, screen);
        ctx.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
        Draw(ctx, screen);
        ctx.Input(new UiInputEvent(UiInputKind.MouseUp, x, y, (int)UiKey.MouseLeft, '\0', 0));
        Draw(ctx, screen);
    }

    [Fact]
    public void A_click_outside_the_hub_takes_the_choice_in_that_direction()
    {
        var (ctx, menu, screen) = Screen(A, B);
        Draw(ctx, screen);
        Click(ctx, screen, 960, 1000);   // straight down: the second of two
        Assert.Equal(["choose 1"], menu.Done);
    }

    [Fact]
    public void A_click_on_the_hub_goes_back()
    {
        var (ctx, menu, screen) = Screen(A, B);
        Draw(ctx, screen);
        Click(ctx, screen, 960, 540);
        Assert.Equal(["back"], menu.Done);
    }

    // ---- the menus ----

    [Fact]
    public void The_menus_page_at_what_the_ring_holds()
        => Assert.Equal(UiRadialLayout.MaxItems, RadialMenus.MaxItems);

    private sealed class Pointer : IRadialPointer
    {
        public int Centred;

        public void Centre() => Centred++;
    }

    private static RadialMenus Menus(out Pointer pointer)
    {
        pointer = new Pointer();
        return new RadialMenus(pointer);
    }

    [Fact]
    public void Opening_a_menu_centres_the_pointer()
    {
        var menus = Menus(out var pointer);
        menus.Show(new RadialPage("T", []));
        Assert.True(menus.Showing);
        Assert.Equal(1, pointer.Centred);
    }

    [Fact]
    public void A_choice_closes_the_menu_and_then_acts()
    {
        var menus = Menus(out _);
        bool showingWhenActed = true;
        menus.Show(new RadialPage("T", [new RadialEntry("x", () => showingWhenActed = menus.Showing)]));
        menus.Choose(0);
        Assert.False(showingWhenActed);
        Assert.False(menus.Showing);
    }

    [Fact]
    public void A_disabled_choice_does_nothing_and_stays_up()
    {
        var menus = Menus(out _);
        bool acted = false;
        menus.Show(new RadialPage("T", [new RadialEntry("x", () => acted = true, Enabled: false)]));
        menus.Choose(0);
        Assert.False(acted);
        Assert.True(menus.Showing);
    }

    [Fact]
    public void A_submenu_opens_in_place_and_back_returns_then_closes()
    {
        var menus = Menus(out _);
        menus.Show(new RadialPage("Top", [new RadialEntry("sub", Open: () => new RadialPage("Sub", []))]));
        menus.Choose(0);
        Assert.Equal("Sub", menus.Title);
        menus.Back();
        Assert.Equal("Top", menus.Title);
        menus.Back();
        Assert.False(menus.Showing);
    }

    [Fact]
    public void A_list_longer_than_the_ring_is_paged_and_none_is_lost()
    {
        var menus = Menus(out _);
        var seen = new List<string>();
        menus.Show(new RadialPage("T", [.. Enumerable.Range(0, 20).Select(i => new RadialEntry($"e{i}", () => { }))]));
        while (true)
        {
            Assert.True(menus.ItemCount <= UiRadialLayout.MaxItems);
            int last = menus.ItemCount - 1;
            bool more = menus.Item(last).Label == "#SS_UI_Radial_More";
            for (int i = 0; i < (more ? last : menus.ItemCount); i++)
                seen.Add(menus.Item(i).Label);
            if (!more)
                break;
            menus.Choose(last);
        }
        Assert.Equal(Enumerable.Range(0, 20).Select(i => $"e{i}"), seen);
    }

    [Fact]
    public void A_menu_opened_for_a_state_closes_when_it_ends()
    {
        var menus = Menus(out _);
        bool alive = true;
        menus.Show(new RadialPage("T", []), this, () => alive);
        Assert.True(menus.Showing);
        alive = false;
        Assert.False(menus.Showing);
        alive = true;
        Assert.False(menus.Showing);
    }

    [Fact]
    public void The_same_owner_s_key_again_closes_and_another_s_replaces()
    {
        var menus = Menus(out _);
        object one = new(), two = new();
        menus.Toggle(one, () => new RadialPage("One", []));
        menus.Toggle(two, () => new RadialPage("Two", []));
        Assert.Equal("Two", menus.Title);
        menus.Toggle(two, () => new RadialPage("Two", []));
        Assert.False(menus.Showing);
    }
}

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// Sub-panels' other half (plan_client_ui §5.7a): overlays take input first and
/// a modal blocks what is under it; slots place contributions in their rect in
/// listing order; page stacks pop on Esc.
/// </summary>
public class UiSubPanelTests
{
    private static UiRect Rect(UiContext ctx, string name) => Named(ctx, name).Rect;

    private static void Click(UiContext ctx, UiRect r)
    {
        float x = r.X + r.W / 2, y = r.Y + r.H / 2;
        ctx.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
        ctx.Input(new UiInputEvent(UiInputKind.MouseUp, x, y, (int)UiKey.MouseLeft, '\0', 0));
    }

    [Fact]
    public void A_dropdown_over_a_scroll_region_gets_the_click_and_a_modal_blocks_the_screen()
    {
        var clicked = new List<string>();
        bool modal = false;

        TestScreen.Describer d = (ref UiFrame ui) =>
        {
            UiBox field;
            using (ui.Scroll("list").At(UiAnchor.TopLeft).Inset(Space.XL).Size(Size.Du(300), Size.Du(200)))
            {
                field = ui.Rect(Tone.SurfaceRaised, Size.Fill, Size.Du(40)).Key("row0").Name("row0");
                if (ui.Interact(field).Clicked) clicked.Add("row0");
                for (int i = 1; i < 6; i++)
                {
                    var row = ui.Rect(Tone.SurfaceRaised, Size.Fill, Size.Du(40)).Key(i).Name($"row{i}");
                    if (ui.Interact(row).Clicked) clicked.Add($"row{i}");
                }
            }

            // The dropdown: anchored under row0, so it lies over row1 and row2.
            using (ui.Overlay("dropdown").AnchorTo(field, UiAnchor.BottomLeft, UiAnchor.TopLeft))
            using (ui.Column().Width(Size.Du(300)).Fill(Tone.Surface))
            {
                var option = ui.Rect(Tone.SurfaceRaised, Size.Fill, Size.Du(60)).Key("option").Name("option");
                if (ui.Interact(option).Clicked) clicked.Add("option");
            }

            if (modal)
            {
                using (ui.Overlay("modal", modal: true))
                using (ui.Box().At(UiAnchor.Centre).Size(Size.Du(200), Size.Du(100)).Fill(Tone.Surface))
                {
                    var ok = ui.Rect(Tone.Accent, Size.Du(120), Size.Du(40)).Key("ok").At(UiAnchor.Centre).Name("ok");
                    if (ui.Interact(ok).Clicked) clicked.Add("ok");
                }
            }
        };

        var ctx = Context();
        var screen = Screen(1920, 1080);
        Frame(ctx, screen, d);

        // The option lies over row1: the click is the option's, and only its.
        var option = Rect(ctx, "option");
        var row1 = Rect(ctx, "row1");
        Assert.True(option.Y < row1.Y + row1.H && option.Y + option.H > row1.Y, $"option {option} not over row1 {row1}");
        Click(ctx, option);
        Frame(ctx, screen, d);
        Assert.Equal(["option"], clicked);

        // Off the dropdown, row 4 takes a click (the stimulus reaches the list).
        clicked.Clear();
        Click(ctx, Rect(ctx, "row4"));
        Frame(ctx, screen, d);
        Assert.Equal(["row4"], clicked);

        // With a modal up, a click on row 4 reaches nothing; the modal's button still works.
        modal = true;
        Frame(ctx, screen, d);
        clicked.Clear();
        Click(ctx, Rect(ctx, "row4"));
        Frame(ctx, screen, d);
        Assert.Empty(clicked);
        Click(ctx, Rect(ctx, "ok"));
        Frame(ctx, screen, d);
        Assert.Equal(["ok"], clicked);
    }

    private sealed class Contributor(string name, string slot, float heightDu) : IUiSlotContributor
    {
        public bool Contributes(string s) => s == slot;

        public void Describe(ref UiFrame ui, string s)
            => ui.Rect(Tone.Accent, Size.Du(120), Size.Du(heightDu)).Name(name);
    }

    [Fact]
    public void Slot_contributions_land_in_the_slots_rect_in_listing_order()
    {
        var ctx = Context(slots:
        [
            new Contributor("first", "hud.bottom-right", 30),
            new Contributor("elsewhere", "scoreboard.footer", 30),
            new Contributor("second", "hud.bottom-right", 50),
        ]);

        Frame(ctx, Screen(2560, 1080), (ref UiFrame ui) =>
        {
            ui.Slot("hud.bottom-right", Space.S).At(UiAnchor.BottomRight).Inset(Space.XL).Name("slot");
        });

        var slot = Rect(ctx, "slot");
        var first = Rect(ctx, "first");
        var second = Rect(ctx, "second");

        // In the slot, which sits 24 du in from the bottom-right corner.
        Assert.Equal(2560 - 24 - slot.W, slot.X);
        Assert.Equal(1080 - 24 - slot.H, slot.Y);
        foreach (var r in new[] { first, second })
            Assert.True(r.X >= slot.X && r.Y >= slot.Y && r.X + r.W <= slot.X + slot.W && r.Y + r.H <= slot.Y + slot.H,
                        $"{r} outside the slot {slot}");

        // Listing order, top to bottom, 8 du apart; a contributor to another slot is absent.
        Assert.Equal(slot.Y, first.Y);
        Assert.Equal(first.Y + 30 + 8, second.Y);
        Assert.DoesNotContain(Elements(ctx), e => NameOf(ctx, e) == "elsewhere");
    }

    private sealed class Page(string title, List<string> seen) : IUiPage
    {
        public string TitleToken => title;

        public void Describe(ref UiFrame ui)
        {
            seen.Add(title);
            ui.Text(title);
        }
    }

    [Fact]
    public void A_page_stack_pops_one_page_per_esc_and_asks_to_close_at_its_root()
    {
        var seen = new List<string>();
        var stack = new UiPageStack(new Page("#options", seen));
        stack.Push(new Page("#video", seen));
        bool close = false;
        var ctx = Context();
        TestScreen.Describer d = (ref UiFrame ui) => { close = stack.Describe(ref ui); };

        Frame(ctx, Screen(1920, 1080), d);
        Assert.Equal("#video", seen[^1]);

        ctx.Input(new UiInputEvent(UiInputKind.Key, 0, 0, (int)UiKey.Escape, '\0', 0));
        Frame(ctx, Screen(1920, 1080), d);
        Assert.Equal("#options", seen[^1]);
        Assert.False(close);

        ctx.Input(new UiInputEvent(UiInputKind.Key, 0, 0, (int)UiKey.Escape, '\0', 0));
        Frame(ctx, Screen(1920, 1080), d);
        Assert.True(close);
        Assert.Equal(1, stack.Depth);
    }
}

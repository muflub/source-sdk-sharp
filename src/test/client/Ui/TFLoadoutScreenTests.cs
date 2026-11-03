using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using TF2.Client.Ui;

using Xunit;

using static SourceSharp.Tests.Ui.UiTestKit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The TF2 loadout screen (stage 3.2): the panels sit at the .res's own
/// positions, an empty slot shows its slot's label, a panel click opens
/// the item selection whose owned click equips, cancel and close go their
/// own ways, the strip asks for a class, and the narrow canvas keeps the
/// columns on the screen (CClassLoadoutPanel, class_loadout_panel.cpp;
/// the selection, itemselectionpanel.res).
/// </summary>
public class TFLoadoutScreenTests
{
    private static TFLoadoutScreen Screen(FakeTFLoadoutSource source, FakeTFLoadoutActions? actions = null)
        => new(source, actions ?? new FakeTFLoadoutActions(), new NoArt(), new LoadoutLocalise());

    /// <summary>The library that misses everything: the layout facts read
    /// the fallback (names, no art), not the VPK images.</summary>
    private sealed class NoArt : IUiTextureLibrary
    {
        public int Find(string name) => 0;
        public int Material(string name) => 0;

        public UiTextureInfo? About(string name) => null;

        public UiTextureIcon? Icon(string name) => null;
    }

    /// <summary>A scout's reply: stock's seven panels in position order,
    /// the secondary and one cosmetic with nothing in them, and three
    /// owned primaries beside a pistol.</summary>
    private static readonly FakeTFLoadoutSource Scout = new()
    {
        Class = 1,
        Slots =
        [
            new(0, "#LoadoutSlot_Primary", "#TF_Weapon_Scattergun", "backpack/weapons/c_models/c_scattergun"),
            new(1, "#LoadoutSlot_Secondary", "", ""),
            new(2, "#LoadoutSlot_Melee", "#TF_Weapon_Bat", "backpack/weapons/c_models/c_bat"),
            new(7, "#LoadoutSlot_Misc", "#TF_Weapon_Cap", "backpack/player/items/scout/scout_cap"),
            new(8, "#LoadoutSlot_Misc", "#TF_Weapon_Dogfighter", ""),
            new(10, "#LoadoutSlot_Misc", "", ""),
            new(9, "#LoadoutSlot_Action", "#TF_Weapon_LunchBox", ""),
        ],
        Owned =
        [
            new(200, 0, "#TF_Weapon_Scattergun", "backpack/weapons/c_models/c_scattergun"),
            new(45, 0, "#TF_Weapon_ForceANature", "backpack/weapons/c_models/c_force_a_nature"),
            new(220, 0, "#TF_Weapon_Shortstop", "backpack/weapons/c_models/c_shortstop"),
            new(23, 1, "#TF_Weapon_Pistol", "backpack/weapons/c_models/c_pistol"),
        ],
    };

    /// <summary>Seven owned primaries: the sixth wraps only where the grid
    /// holds fewer than six columns.</summary>
    private static readonly FakeTFLoadoutSource SevenPrimaries = new()
    {
        Class = 1,
        Slots = [new(0, "#LoadoutSlot_Primary", "", "")],
        Owned =
        [
            new(200, 0, "#TF_Weapon_Scattergun", ""),
            new(45, 0, "#TF_Weapon_ForceANature", ""),
            new(220, 0, "#TF_Weapon_Shortstop", ""),
            new(221, 0, "#TF_Weapon_Shortstop", ""),
            new(222, 0, "#TF_Weapon_Shortstop", ""),
            new(223, 0, "#TF_Weapon_Shortstop", ""),
            new(224, 0, "#TF_Weapon_Shortstop", ""),
        ],
    };

    private static UiContext Drawn(UiScreen screen, int w = 1920, int h = 1080)
    {
        var ctx = Context();
        Frame(ctx, UiTestKit.Screen(w, h), (ref UiFrame ui) => screen.Describe(ref ui));
        return ctx;
    }

    private static void Click(UiContext ctx, UiRect r)
    {
        float x = r.X + r.W / 2, y = r.Y + r.H / 2;
        ctx.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
        ctx.Input(new UiInputEvent(UiInputKind.MouseUp, x, y, (int)UiKey.MouseLeft, '\0', 0));
    }

    private static int Count(UiContext ctx, string name) => Array.FindIndex(Elements(ctx), e => NameOf(ctx, e) == name);

    private static int TextIndex(UiContext ctx, string token)
    {
        var els = Elements(ctx);
        for (int i = 0; i < els.Length; i++)
            if (RunText(ctx, i) == token)
                return i;
        return -1;
    }

    /// <summary>Clicks and settles: the click resolves at the next frame's
    /// start and the state it sets shows the frame after (the toolkit's
    /// one-frame immediate-mode lag), so two describes follow the click.</summary>
    private static void ClickNamed(UiContext ctx, UiScreen screen, string name, int w = 1920, int h = 1080)
    {
        Click(ctx, Named(ctx, name).Rect);
        Frame(ctx, UiTestKit.Screen(w, h), (ref UiFrame ui) => screen.Describe(ref ui));
        Frame(ctx, UiTestKit.Screen(w, h), (ref UiFrame ui) => screen.Describe(ref ui));
    }

    /// <summary>Emitted rects are integral (edges rounded), so a du value
    /// and its pixel rect may differ by a pixel.</summary>
    private static void Near(float actual, float expected, string what)
        => Assert.True(MathF.Abs(actual - expected) < 1.6f, $"{what}: {actual}, expected {expected}");

    /// <summary>The panels stand where the .res puts them: column 0 at
    /// item_xpos_offcenter_a -310, column 1 at offcenter_b 165, rows
    /// item_ypos 60 plus item_ydelta 75 (classloadoutpanel.res:21-24,
    /// class_loadout_panel.cpp:601-609) — 140x70 panels at the 2.25
    /// scale, the 640x480 canvas centred in the design.</summary>
    [Fact]
    public void The_panels_sit_at_the_res_positions()
    {
        var ctx = Drawn(Screen(Scout));

        var p0 = Named(ctx, "row 100").Rect;   // column 0, row 0
        Near(p0.X, 262.5f, "panel 0 x");
        Near(p0.Y, 135f, "panel 0 y");
        Near(p0.W, 315f, "panel 0 w");
        Near(p0.H, 157.5f, "panel 0 h");

        var p4 = Named(ctx, "row 104").Rect;   // the slot at position 5: column 1
        Near(p4.X, 1331.25f, "panel 4 x");

        var p2 = Named(ctx, "row 102").Rect;   // two rows down: item_ydelta 75
        Near(p2.Y, 472.5f, "panel 2 y");
    }

    /// <summary>A slot with nothing in it shows the slot's own label
    /// (SetNoItemText, class_loadout_panel.cpp:599) and a filled one the
    /// schema's item name under the panel.</summary>
    [Fact]
    public void An_empty_slot_shows_its_slot_label_and_a_filled_one_its_item()
    {
        var ctx = Drawn(Screen(Scout));

        Assert.True(TextIndex(ctx, "Scattergun") >= 0);             // the equipped name
        Assert.True(TextIndex(ctx, "#LoadoutSlot_Secondary") >= 0); // the empty slot's label
        Assert.True(TextIndex(ctx, "#LoadoutSlot_Primary") < 0);    // a filled slot names its item, not its slot
    }

    /// <summary>Clicking a panel opens stock's item selection: the slot's
    /// label west and the selection's cancel under the second line
    /// (itemselectionpanel.res) — no equip sent for the opening click.</summary>
    [Fact]
    public void A_panel_click_opens_the_item_selection()
    {
        var actions = new FakeTFLoadoutActions();
        var screen = Screen(Scout, actions);
        var ctx = Drawn(screen);
        Assert.True(Count(ctx, "slot label") < 0);           // before: the loadout view

        ClickNamed(ctx, screen, "row 100");
        Assert.True(Count(ctx, "slot label") >= 0);          // after: the selection
        Assert.Equal("#LoadoutSlot_Primary", RunText(ctx, Count(ctx, "slot label")));
        Assert.True(Count(ctx, "cancel") >= 0);
        Assert.Empty(actions.Equips);                       // the opening click equips nothing
    }

    /// <summary>Clicking an owned item sends the named tf2_equip — the
    /// class, the slot and the item's defindex — and returns to the
    /// loadout for the server's fresh reply.</summary>
    [Fact]
    public void An_owned_item_click_equips_and_returns_to_the_loadout()
    {
        var actions = new FakeTFLoadoutActions();
        var screen = Screen(Scout, actions);
        var ctx = Drawn(screen);
        Assert.Empty(actions.Equips);                       // before: nothing equipped

        ClickNamed(ctx, screen, "row 100");                  // open the primary's selection
        ClickNamed(ctx, screen, "row 200");                  // the first owned weapon (its defindex)
        Assert.Equal([(1, 0, 200)], actions.Equips);
        Assert.True(Count(ctx, "slot label") < 0);           // back on the loadout
    }

    /// <summary>The selection's cancel is stock's return to the loadout,
    /// not an equip and not a close.</summary>
    [Fact]
    public void The_cancel_returns_to_the_loadout_without_equipping()
    {
        var actions = new FakeTFLoadoutActions();
        var screen = Screen(Scout, actions);
        var ctx = Drawn(screen);

        ClickNamed(ctx, screen, "row 100");
        ClickNamed(ctx, screen, "cancel");
        Assert.Empty(actions.Equips);
        Assert.Equal(0, actions.Closes);
        Assert.True(Count(ctx, "slot label") < 0);           // back on the loadout
    }

    /// <summary>The loadout view's own close: stock's full-screen panel
    /// closes from the game menu; ours sends the close once.</summary>
    [Fact]
    public void The_loadout_close_closes()
    {
        var actions = new FakeTFLoadoutActions();
        var screen = Screen(Scout, actions);
        var ctx = Drawn(screen);
        Assert.Equal(0, actions.Closes);                     // before: nothing closed

        ClickNamed(ctx, screen, "close");
        Assert.Equal(1, actions.Closes);
    }

    /// <summary>A strip tile asks for that class's loadout (the one-byte
    /// ask), by its raw TF_CLASS number.</summary>
    [Fact]
    public void The_strip_selects_a_class()
    {
        var actions = new FakeTFLoadoutActions();
        var screen = Screen(Scout, actions);
        var ctx = Drawn(screen);
        Assert.Empty(actions.Selects);                      // before: nothing asked

        ClickNamed(ctx, screen, "row 165");                  // the fifth tile: TF_CLASS_MEDIC
        Assert.Equal([5], actions.Selects);
    }

    /// <summary>A slot with nothing owned to equip says so (NoItemsLabel,
    /// itemselectionpanel.res:219-236) instead of an empty grid.</summary>
    [Fact]
    public void A_slot_with_nothing_owned_says_so()
    {
        var source = new FakeTFLoadoutSource
        {
            Class = 1,
            Slots = [new(0, "#LoadoutSlot_Primary", "", "")],
            Owned = [new(23, 1, "#TF_Weapon_Pistol", "")],
        };
        var screen = Screen(source);
        var ctx = Drawn(screen);

        ClickNamed(ctx, screen, "row 100");
        int i = Count(ctx, "no items");
        Assert.True(i >= 0);
        Assert.Equal("#NoItemsToEquip", RunText(ctx, i));
    }

    /// <summary>The grid wraps at the canvas's columns: six a row at
    /// stock's width (item_selection_panel.h:22-24), five on a narrow
    /// design — the sixth item is beside the first wide, below it narrow.</summary>
    [Fact]
    public void The_selection_grid_wraps_at_the_widths_columns()
    {
        var wideScreen = Screen(SevenPrimaries);
        var wide = Drawn(wideScreen);
        ClickNamed(wide, wideScreen, "row 100");
        var sixthWide = Named(wide, "row 223").Rect;
        var firstWide = Named(wide, "row 200").Rect;
        Near(sixthWide.Y, firstWide.Y, "wide: the sixth beside the first");

        // 1080x1920 scales by width (UiScale 0.84375): a 1280-du design is
        // 568.9 stock units, five columns — the sixth grid panel takes the
        // second row (item_ypos 60 + item_backpack_ydelta 73).
        var narrowScreen = Screen(SevenPrimaries);
        var narrow = Drawn(narrowScreen, 1080, 1920);
        ClickNamed(narrow, narrowScreen, "row 100", 1080, 1920);
        var sixthNarrow = Named(narrow, "row 223").Rect;
        var firstNarrow = Named(narrow, "row 200").Rect;
        var seventhNarrow = Named(narrow, "row 224").Rect;
        Assert.True(sixthNarrow.Y > firstNarrow.Y + 100f,
            $"narrow: the sixth beside the first ({sixthNarrow.Y} vs {firstNarrow.Y})");
        Near(sixthNarrow.Y, seventhNarrow.Y, "narrow: the sixth and seventh share the second row");
        Near(sixthNarrow.W, 94f * StockRes.Scale * 0.84375f, "narrow: the grid panel keeps stock's 94 units");
    }

    /// <summary>A design narrower than the layout's own 625 units draws
    /// the two columns symmetrically about the centre with the gap the
    /// width holds: equal margins, panels at stock's 140 units.</summary>
    [Fact]
    public void The_narrow_canvas_keeps_the_columns_symmetric()
    {
        var ctx = Drawn(Screen(Scout), 1080, 1920);

        var left = Named(ctx, "row 100").Rect;
        var right = Named(ctx, "row 104").Rect;
        Near(left.X, 10f * StockRes.Scale * 0.84375f, "left column x");
        Near(right.X + right.W, 1080f - 10f * StockRes.Scale * 0.84375f, "right column's right edge");
        Near(left.W, 140f * StockRes.Scale * 0.84375f, "the panel keeps stock's 140 units");
    }
}

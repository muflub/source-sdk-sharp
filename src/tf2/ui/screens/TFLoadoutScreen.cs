//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

/// <summary>
/// The TF2 loadout screen (stock's <c>CClassLoadoutPanel</c>,
/// game/client/tf/vgui/class_loadout_panel.cpp) at stock's own layout:
/// classloadoutpanel.res's canvas through <see cref="StockRes"/> — the
/// carat <c>&gt;&gt;</c> west of the class being equipped (CaratLabel
/// c-300, 20, 20x15, drawcolor 200 80 60, :93-110), the
/// <c>#ClassBeingEquipped</c> label beside it (c-280, 15, 240x25, :111-127
/// — the class name stock fills <c>%loadoutclass%</c> with,
/// tf_english.txt:9918, class_loadout_panel.cpp:900), the dotted
/// <c>loadout_dotted_line</c> under them (TopLine c-305, 40, 610x10,
/// tiled, :165-179) and the 140x70 item panels at the .res's own
/// positions (class_loadout_panel.cpp:601-609: column 0 at
/// <c>item_xpos_offcenter_a</c> -310, column 1 at
/// <c>item_xpos_offcenter_b</c> 165, rows at <c>item_ypos</c> 60 plus
/// <c>item_ydelta</c> 75, :21-24) — the panel order the
/// <c>TF2:loadout</c> reply's slots arrive in, which is stock's position
/// order. Each panel draws its item: the schema's
/// <c>image_inventory</c> in the model area (model_center_x 1,
/// model_ypos 5, model_wide 82, model_tall 55, :49-52) and the
/// <c>item_name</c> under it at <c>text_ypos</c> 54, centred (:53-55); a
/// library that misses the art leaves the name alone, and a slot with
/// nothing in it shows the slot's own label in the no-item colour 117 107
/// 94 (:45, <c>SetNoItemText</c>, class_loadout_panel.cpp:599).
///
/// <para>
/// Clicking a panel opens stock's item selection over the canvas
/// (itemselectionpanel.res): the slot's label west (ItemSlotLabel
/// c-55, 15, 375x25, :201-217), the owned items that fit the slot in the
/// 94x70 grid the backpack layout writes (backpack_panel.cpp:703-706
/// with <c>item_backpack_offcenter_x</c> -288, <c>item_backpack_xdelta</c>
/// 4, :22-24, three rows of six — 18 a page,
/// item_selection_panel.h:22-24; the reply's 13 never needs a second),
/// the second dotted line (BottomLine c-305, 330, :185-199) and the
/// <c>#Cancel</c> button under it (c-200, ypos 400, 200x25, :238-260).
/// Clicking an owned item equips it — the named <c>tf2_equip</c> through
/// <see cref="ITFLoadoutActions"/>; the server's fresh reply redraws the
/// loadout. A slot with nothing to equip says so (NoItemsLabel c-300,
/// 120, 600x30, drawcolor 200 80 60, :219-236).
/// </para>
/// <para>
/// Stock's 3D <c>classmodelpanel</c> (c-135, 20, 270x340, :181-217), the
/// attrib/preset/taunt/options panels and the passive-attribs label are
/// the deferred model and GC work (docs/plans/tf2/deferred.tsv); the
/// window surface is the backdrop under the layout until a model panel
/// lands, as the class menu's is. The class strip along the bottom is
/// ours: stock reaches another class's loadout through the tabs of the
/// full character panel, which is deferred, so the nine
/// <c>#TF_Class_Name_*</c> names (tf_english.txt:38-46) stand in at 64x25
/// a tile from ypos 450.
/// </para>
/// <para>
/// The canvas is stock's 640x480 space, centred in the design — the .res's
/// <c>c</c> offsets are about the screen's centre, so wider screens keep
/// the layout centred as stock's proportional canvas does. Two arms keep
/// it on the screen where stock's canvas has none of its own: a design
/// shallower than the canvas's 1080 du (a ui scale over 1) takes a
/// smaller du-per-unit so the whole canvas fits, and a design narrower
/// than the layout's own 625 units draws the two columns symmetrically
/// about the centre with the gap the width holds — panels, rows, grid
/// and fonts stay at stock's sizes, and the line, the labels, the strip
/// tiles and the grid's column count take the width that is there.
/// </para>
/// </summary>
public sealed class TFLoadoutScreen : UiScreen
{
    private readonly ITFLoadoutSource _source;
    private readonly ITFLoadoutActions _actions;
    private readonly IUiTextureLibrary _textures;
    private readonly IUiLocalisation _localise;

    /// <summary>The slot whose item selection is open; null is the loadout
    /// view. The raw TFLoadoutSlot number of the panel clicked.</summary>
    private int? _picking;

    /// <summary>The selection view, for the samples and the facts: the
    /// click arms set it in Describe, which a sample cannot do.</summary>
    internal int? Picking { get => _picking; set => _picking = value; }

    /// <summary>Stock's drawcolor 200 80 60 (CaratLabel's fgcolor_override,
    /// classloadoutpanel.res:109, NoItemsLabel's :219-236) lands on the dark
    /// window at 1.65:1, so the carat and the no-items label ride the theme's
    /// Accent — the same warm rust at a contrast the lint holds.</summary>
    private const Tone Carat = Tone.Accent;

    /// <param name="textures">The art library the dotted line and the
    /// inventory images come from; every miss falls back to the name.</param>
    /// <param name="localise">The item names' localiser: the schema's
    /// <c>item_name</c> tokens ride the game's own tf_english.txt, which
    /// the screen resolves per reply (the mod's file cannot hold them).</param>
    /// <param name="actions">What a click does (the ask, the equip and the
    /// close); the container's in game, a fake in a sample.</param>
    public TFLoadoutScreen(ITFLoadoutSource source, ITFLoadoutActions actions,
                           IUiTextureLibrary textures, IUiLocalisation localise)
    {
        _source = source;
        _actions = actions;
        _textures = textures;
        _localise = localise;
    }

    public override string Name => "tf2loadout";

    public override void Describe(ref UiFrame ui)
    {
        // The canvas bg: stock paints its own opaque 46 43 42 255
        // (bgcolor_override, classloadoutpanel.res:17-19,
        // itemselectionpanel.res:18) — the theme's Window, full alpha:
        // nothing of the world shows through the loadout.
        ui.Rect(Tone.Window, Size.Percent(100), Size.Percent(100))
            .AtRes(UiAnchor.BottomLeft, 0, 0).Layered().Key("backdrop");

        // Du per stock unit: stock's 2.25 (the 480-tall canvas into 1080),
        // less when the design is shallower than the canvas — a ui scale
        // over 1 — so the canvas scales whole, as stock's proportional
        // space does, instead of cropping.
        float s = MathF.Min(StockRes.Scale, ui.DesignHeight / Stock.Canvas.Tall);

        // The canvas: stock's 640x480 space centred in the design, so the
        // .res's "c" offsets hold about the screen's centre at any width.
        // Narrower than the layout's own 625 units, the canvas is the
        // design itself and the columns close (Narrow below).
        float units = ui.DesignWidth / s;
        bool narrow = units < WideUnits;
        StockRect canvas = narrow
            ? new StockRect(0f, 0f, units, Stock.Canvas.Tall)
            : new StockRect((units - Stock.Canvas.Wide) / 2f, 0f, Stock.Canvas.Wide, Stock.Canvas.Tall);

        if (_picking is { } slot)
            DescribeSelection(ref ui, slot, canvas, narrow, s);
        else
            DescribeLoadout(ref ui, canvas, narrow, s);
    }

    // ---- the stock canvas ----

    internal static class Stock
    {
        public static readonly StockRect Canvas = StockRect.Screen;

        // classloadoutpanel.res: CaratLabel :93-110, ClassLabel :111-127,
        // TopLine :165-179.
        public static readonly StockBox Carat = new("c-300", "20", 20, 15);
        public static readonly StockBox ClassLabel = new("c-280", "15", 240, 25);

        // itemselectionpanel.res: ItemSlotLabel :201-217, NoItemsLabel
        // :219-236, BottomLine :185-199, CancelButton :238-260.
        public static readonly StockBox ItemSlotLabel = new("c-55", "15", 375, 25);
        public static readonly StockBox NoItems = new("c-300", "120", 600, 30);
        public static readonly StockBox BottomLine = new("c-305", "330", 610, 10);
        public static readonly StockBox Cancel = new("c-200", "400", 200, 25);
    }

    /// <summary>The loadout's own width in units: the column-1 panel's
    /// right edge (485 + 140). A design narrower closes the columns.</summary>
    private const float WideUnits = 625f;

    /// <summary>The canvas centre, units.</summary>
    private static float CentreX(StockRect canvas) => canvas.Left + canvas.Wide / 2f;

    /// <summary>An element's AtRes(Bottom) x: its centre's du from the
    /// screen's centre (the .res "c" offset).</summary>
    private static float CentreFromScreenCentre(StockRect rect, float s, float designWidth)
        => (rect.Left + rect.Wide / 2f) * s - designWidth / 2f;

    /// <summary>An item panel's box, in units. The panel order is stock's
    /// position order (the reply's slots arrive sorted), so panel i is
    /// position i+1: <c>iButtonPos - 1</c> (class_loadout_panel.cpp:595),
    /// column <c>iButtonPos / 4</c>, row <c>iButtonPos % 4</c> (:602-604),
    /// x the centre plus offcenter A or B (:606), y <c>item_ypos</c> plus
    /// <c>item_ydelta</c> a row (:608). Narrow, the two columns draw
    /// symmetrically about the centre with the gap the width holds
    /// (stock's own 310/165 offsets at 625 units).</summary>
    internal static StockRect ItemPanel(int panel, StockRect canvas, bool narrow)
    {
        int col = panel / 4, row = panel % 4;
        float c = CentreX(canvas);
        float gap = Math.Clamp(canvas.Wide - 300f, 10f, 340f);
        float left = col == 0
            ? narrow ? c - gap / 2f - 140f : c - 310f
            : narrow ? c + gap / 2f : c + 165f + (col - 1) * 200f;
        float top = 60f + 75f * row;
        return new StockRect(left, 480f - top - 70f, 140f, 70f);
    }

    /// <summary>A selection grid panel's box, in units: the backpack
    /// layout (backpack_panel.cpp:703-706) over the selection .res's own
    /// numbers — columns of 94 + <c>item_backpack_xdelta</c> 4 from
    /// <c>item_backpack_offcenter_x</c> -288 (itemselectionpanel.res:22-23),
    /// rows of 70 + 3 from <c>item_ypos</c> 60 (:20). Six a row at stock's
    /// width (item_selection_panel.h:22-24); narrow, as many as the width
    /// holds, the grid centred.</summary>
    internal static StockRect SelectionPanel(int index, StockRect canvas, bool narrow)
    {
        int cols = Columns(canvas, narrow);
        int col = index % cols, row = index / cols;
        float c = CentreX(canvas);
        float left0 = cols == 6 ? c - 288f : c - (cols * 98f - 4f) / 2f;
        float top = 60f + row * 73f;
        return new StockRect(left0 + col * 98f, 480f - top - 70f, 94f, 70f);
    }

    /// <summary>The grid's columns: six at stock's width, else what the
    /// canvas holds. Three rows a page either way (the .res's own 60 +
    /// 2x73 + 70 ends at 276, under BottomLine's 330), so the page holds
    /// 18 at stock's width — item_selection_panel.h:22-24.</summary>
    internal static int Columns(StockRect canvas, bool narrow)
        => narrow ? Math.Clamp((int)((canvas.Wide - 16f) / 98f), 1, 6) : 6;

    private void DescribeLoadout(ref UiFrame ui, StockRect canvas, bool narrow, float s)
    {
        float c = CentreX(canvas);

        // CaratLabel ">>" (:93-110) and the class being equipped (:111-127);
        // narrow, the pair is centred as one group.
        var carat = narrow
            ? new StockRect(c - 130f, 445f, 20f, 15f)
            : Stock.Carat.Rect(canvas);
        ui.Data(">>", Text.Label).Color(Carat)
            .Width(Size.Du(carat.Wide * s)).TextAlign(Align.Start)
            .AtRes(UiAnchor.Bottom, CentreFromScreenCentre(carat, s, ui.DesignWidth), carat.Bottom * s)
            .Layered().Key("carat");
        if (_source.Class is >= 1 and <= 9)
        {
            var label = narrow
                ? new StockRect(c - 110f, 440f, MathF.Min(240f, canvas.Wide - 16f), 25f)
                : Stock.ClassLabel.Rect(canvas);
            ui.Text(ClassToken(_source.Class), Text.Heading, Tone.Text)
                .Width(Size.Du(label.Wide * s)).TextAlign(Align.Start)
                .AtRes(UiAnchor.Bottom, CentreFromScreenCentre(label, s, ui.DesignWidth), label.Bottom * s)
                .Layered().Key("class");
        }

        // TopLine, the tiled dotted line (:165-179); no art in the library
        // draws nothing, as the class menu's shaded bar does.
        DottedLine(ref ui, Line(narrow, canvas, 40f), canvas, s, 1000);

        // The item panels, in the reply's panel order.
        for (int i = 0; i < _source.Slots; i++)
        {
            var view = _source.Slot(i);
            var panel = ItemPanel(i, canvas, narrow);
            var box = ui.Box().Size(Size.Du(panel.Wide * s), Size.Du(panel.Tall * s))
                           .AtRes(UiAnchor.BottomLeft, panel.Left * s, panel.Bottom * s)
                           .Layered().Key(100 + i);
            var hit = ui.Interact(box);
            using (box)
            {
                // A clear bg with a border (bgcolor_override 0 0 0 0,
                // PaintBackgroundType 2, paintborder 1, :44-47): stock's
                // mouseover art raises the border, ours arms on hover.
                box.Border(hit.Hovered ? Tone.Text : Tone.SurfaceRaised);
                if (view.ItemName.Length > 0)
                {
                    int icon = _textures.Find(view.Icon);
                    if (icon != 0)
                        ui.Texture(icon, Size.Du(82f * s), Size.Du(55f * s))
                            .At(UiAnchor.Top).Inset(Space.None, Raw.Du(5f * s))
                            .Layered().Key(120 + i);
                    ui.Data(_localise.Find(view.ItemName) ?? view.ItemName, Text.Caption, Tone.Text)
                        .At(UiAnchor.Bottom).Inset(Space.None, Space.S)
                        .Width(Size.Fill).TextAlign(Align.Centre).Layered();
                }
                else
                {
                    // SetNoItemText (class_loadout_panel.cpp:599) in the
                    // no-item colour 117 107 94 (:45) — the theme's muted
                    // text, where stock's own value is under 3:1 here.
                    ui.Text(view.Label, Text.Body, Tone.TextMuted)
                        .At(UiAnchor.Centre).Width(Size.Fill).TextAlign(Align.Centre).Layered();
                }
            }
            if (hit.Clicked)
                _picking = view.Slot;
        }

        Strip(ref ui, canvas, s);
        CloseButton(ref ui, canvas, narrow, s, "close", loadout: true);
    }

    /// <summary>A dotted line's box: stock's own at the top (40) or above
    /// the cancel (330); narrow, trimmed to the canvas and centred.</summary>
    private static StockRect Line(bool narrow, StockRect canvas, float ypos)
    {
        if (!narrow)
            return ypos == 40f
                ? new StockBox("c-305", "40", 610, 10).Rect(canvas)
                : Stock.BottomLine.Rect(canvas);
        float wide = canvas.Wide - 32f;
        float bottom = ypos == 40f ? 480f - 50f : 480f - 340f;
        return new StockRect(CentreX(canvas) - wide / 2f, bottom, wide, 10f);
    }

    /// <summary>The tiled dotted line: stock's tileImage 1
    /// (classloadoutpanel.res:177, itemselectionpanel.res:195) repeats the
    /// texture at its own aspect across the line, so the draw is copies of
    /// it at the line's height, left to right. No art in the library draws
    /// nothing, as the class menu's shaded bar does.</summary>
    private void DottedLine(ref UiFrame ui, StockRect line, StockRect canvas, float s, int key)
    {
        var art = _textures.About("vgui/loadout_dotted_line");
        if (art is not { } tex || tex.Wide == 0 || tex.Tall == 0)
            return;
        float tall = line.Tall * s;
        float tile = tall * tex.Wide / tex.Tall;
        float left = line.Left * s;
        for (int n = 0; n * tile < line.Wide * s; n++)
        {
            float wide = MathF.Min(tile, line.Wide * s - n * tile);
            // A part tile clips (tileImage's own cut), never squeezes.
            var uv = wide < tile ? new UiRect(0f, 0f, wide / tile, 1f) : new UiRect(0f, 0f, 1f, 1f);
            ui.Texture(tex.Handle, Size.Du(wide), Size.Du(tall), uv)
                .AtRes(UiAnchor.BottomLeft, left + n * tile, line.Bottom * s)
                .Layered().Key(key + n);
        }
    }

    /// <summary>The class strip: the nine <c>#TF_Class_Name_*</c> names
    /// (tf_english.txt:38-46) at 64x25 from ypos 450 — the deferred
    /// character-panel tabs' stand-in, ours. Narrow, the tiles take the
    /// width the canvas holds.</summary>
    private void Strip(ref UiFrame ui, StockRect canvas, float s)
    {
        float wide = MathF.Min(64f, (canvas.Wide - 16f) / 9f - 2f);
        float left0 = canvas.Left + (canvas.Wide - 9f * wide) / 2f;
        for (int n = 1; n <= 9; n++)
        {
            var rect = new StockRect(left0 + (n - 1) * wide, 5f, wide, 25f);
            var box = ui.Box().Size(Size.Du(rect.Wide * s), Size.Du(rect.Tall * s))
                           .AtRes(UiAnchor.BottomLeft, rect.Left * s, rect.Bottom * s)
                           .Layered().Key(160 + n);
            var hit = ui.Interact(box);
            using (box)
            {
                ui.Text(ClassToken(n), Text.Label, n == _source.Class ? Tone.Text : Tone.TextMuted)
                    .At(UiAnchor.Centre).TextAlign(Align.Centre).Layered();
            }
            if (hit.Clicked)
            {
                _picking = null;
                _actions.Select(n);
            }
        }
    }

    /// <summary>The close: stock's full-screen panel closes from the game
    /// menu it lives in; ours is a screen, so the selection .res's
    /// CancelButton box (itemselectionpanel.res:238-260) carries
    /// <c>#SS_UI_Cancel</c>. Narrow past what the stock box holds, it
    /// centres.</summary>
    private void CloseButton(ref UiFrame ui, StockRect canvas, bool narrow, float s, string key, bool loadout)
    {
        var cancel = narrow && canvas.Wide < 420f
            ? new StockRect(CentreX(canvas) - 100f, 55f, 200f, 25f)
            : Stock.Cancel.Rect(canvas);
        var box = ui.Box().Size(Size.Du(cancel.Wide * s), Size.Du(cancel.Tall * s))
                       .Fill(Tone.SurfaceRaised).Round(Radius.S)
                       .AtRes(UiAnchor.Bottom, CentreFromScreenCentre(cancel, s, ui.DesignWidth),
                           cancel.Bottom * s)
                       .Layered().Key(key);
        var hit = ui.Interact(box);
        using (box)
            ui.Text("#SS_UI_Cancel", Text.Body, Tone.Text)
                .At(UiAnchor.Centre).TextAlign(Align.Centre).Layered();
        if (hit.Clicked)
        {
            if (loadout)
                _actions.Close();
            else
                _picking = null;
        }
    }

    // ---- the item selection (itemselectionpanel.res) ----

    private void DescribeSelection(ref UiFrame ui, int slot, StockRect canvas, bool narrow, float s)
    {
        float c = CentreX(canvas);

        var carat = narrow
            ? new StockRect(c - 130f, 445f, 20f, 15f)
            : Stock.Carat.Rect(canvas);
        ui.Data(">>", Text.Label).Color(Carat)
            .Width(Size.Du(carat.Wide * s)).TextAlign(Align.Start)
            .AtRes(UiAnchor.Bottom, CentreFromScreenCentre(carat, s, ui.DesignWidth), carat.Bottom * s)
            .Layered().Key("carat");

        // ItemSlotLabel: the slot's own label, west (itemselectionpanel.res:201-217);
        // narrow, centred and trimmed.
        var label = narrow
            ? new StockRect(c - MathF.Min(375f, canvas.Wide - 16f) / 2f, 440f,
                MathF.Min(375f, canvas.Wide - 16f), 25f)
            : Stock.ItemSlotLabel.Rect(canvas);
        ui.Text(LabelOf(slot), Text.Heading, Tone.Text)
            .Width(Size.Du(label.Wide * s)).TextAlign(Align.Start)
            .AtRes(UiAnchor.Bottom, CentreFromScreenCentre(label, s, ui.DesignWidth), label.Bottom * s)
            .Layered().Key("slot label");

        DottedLine(ref ui, Line(narrow, canvas, 40f), canvas, s, 1000);

        // The owned items that fit the slot, in the 94x70 grid (:26-43,
        // backpack_panel.cpp:703-706) — three rows a page.
        int cols = Columns(canvas, narrow);
        int shown = 0;
        for (int i = 0; i < _source.OwnedCount && shown < cols * 3; i++)
        {
            var item = _source.Owned(i);
            if (item.Slot != slot)
                continue;

            var grid = SelectionPanel(shown, canvas, narrow);
            var box = ui.Box().Size(Size.Du(grid.Wide * s), Size.Du(grid.Tall * s))
                           .AtRes(UiAnchor.BottomLeft, grid.Left * s, grid.Bottom * s)
                           .Layered().Key(item.Defindex);
            var hit = ui.Interact(box);
            using (box)
            {
                // Same panel chrome as the loadout's (itemselectionpanel.res:52-55).
                box.Border(hit.Hovered ? Tone.Text : Tone.SurfaceRaised);
                int icon = _textures.Find(item.Icon);
                if (icon != 0)
                    ui.Texture(icon, Size.Du(75f * s), Size.Du(50f * s))
                        .At(UiAnchor.Top).Inset(Raw.Du(2f * s), Raw.Du(5f * s))
                        .Layered().Key(140 + i);
                ui.Data(_localise.Find(item.Name) ?? item.Name, Text.Caption, Tone.Text)
                    .At(UiAnchor.Bottom).Inset(Space.None, Space.S)
                    .Width(Size.Fill).TextAlign(Align.Centre).Layered();
            }
            if (hit.Clicked)
            {
                _actions.Equip(_source.Class, slot, item.Defindex);
                _picking = null;
            }
            shown++;
        }

        if (shown == 0)
        {
            // NoItemsLabel (:219-236).
            var none = narrow
                ? new StockRect(c - MathF.Min(600f, canvas.Wide - 16f) / 2f, 330f,
                    MathF.Min(600f, canvas.Wide - 16f), 30f)
                : Stock.NoItems.Rect(canvas);
            ui.Text("#NoItemsToEquip", Text.Body).Color(Carat)
                .Width(Size.Du(none.Wide * s)).TextAlign(Align.Centre)
                .AtRes(UiAnchor.Bottom, CentreFromScreenCentre(none, s, ui.DesignWidth), none.Bottom * s)
                .Layered().Key("no items");
        }

        // BottomLine (:185-199) and CancelButton (:238-260): back to the
        // loadout, as stock's vguicancel returns to it.
        DottedLine(ref ui, Line(narrow, canvas, 330f), canvas, s, 2000);
        CloseButton(ref ui, canvas, narrow, s, "cancel", loadout: false);
    }

    /// <summary>The picking slot's <c>#LoadoutSlot_*</c> label token; empty
    /// for a slot the reply does not carry.</summary>
    private string LabelOf(int slot)
    {
        for (int i = 0; i < _source.Slots; i++)
        {
            var view = _source.Slot(i);
            if (view.Slot == slot)
                return view.Label;
        }
        return "";
    }

    /// <summary>The class's <c>#TF_Class_Name_*</c> token in TF_CLASS
    /// order (tf_english.txt:38-46); heavy's token is its own
    /// <c>HWGuy</c> key.</summary>
    private static string ClassToken(int classNumber) => classNumber switch
    {
        1 => "#TF_Class_Name_Scout",
        2 => "#TF_Class_Name_Sniper",
        3 => "#TF_Class_Name_Soldier",
        4 => "#TF_Class_Name_Demoman",
        5 => "#TF_Class_Name_Medic",
        6 => "#TF_Class_Name_HWGuy",
        7 => "#TF_Class_Name_Pyro",
        8 => "#TF_Class_Name_Spy",
        9 => "#TF_Class_Name_Engineer",
        _ => "#TF_Class_Name_Scout",
    };
}

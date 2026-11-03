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
/// The engineer's destroy menu (stock's <c>CHudMenuEngyDestroy</c>,
/// game/client/tf/tf_hud_menu_engy_destroy.cpp) at stock's own layout:
/// HudMenuEngyDestroy's canvas (hudlayout.res:1006-1016, c-225 c-59
/// 450x200) with hudmenuengydestroy.res's children on it — the
/// hud_menu_bg backdrop 0,14 450x170, the 255 222 208 divider 8,51
/// 436x2, the ico_demolish badge 0,-2 64x64, the Demolish title 31,4
/// 300x38, the Cancel sentence 218,39 200x13 east (stock names the
/// build menu's own token there), and the four 100x124 slots at x
/// 25/125/225/325, y 51. The menu shows only while the destroy PDA is
/// the active weapon.
///
/// <para>
/// A slot carries its building's name, art and key chip (base_active.res
/// / base_inactive.res) in one of stock's two states (OnTick's
/// :316-339): Built — the player's own object of the kind exists and is
/// not placing — draws the demolish cross over the building art at half
/// alpha; otherwise the slot draws base_inactive.res, the transparent
/// background and the <c>#TF_NotBuilt</c> reason. Stock's third arm
/// (unavailable_item_*, the <c>training_can_destroy_buildings</c>
/// convar) is training mode's and arms no slot a live client reaches,
/// as stock's own OnTick skips it.
/// </para>
/// <para>
/// A slot click is stock's send arm (:259-266): the OBJ_* pair goes out
/// as <c>destroy %d %d; lastinv</c> (:264) only from a built slot.
/// Stock's further gate — no sapper, not plasma-disabled — needs
/// client reads this port has not landed (docs/plans/tf2/deferred.tsv);
/// the adapter sends and the server's own gate has the last word.
/// </para>
/// </summary>
public sealed class TFDestroyMenu(ITFDestroyMenuSource source, TFDestroyTile tile,
                                  IUiTextureLibrary textures, ITFDestroyMenuActions? actions = null,
                                  ITFRenderGroups? groups = null)
    : UiScreen
{
    /// <summary>Stock's <c>g_kEngyBuildings</c>
    /// (tf_hud_menu_engy_destroy.cpp:35-84): the OBJ_*/mode pair each
    /// slot destroys — the build menu's own table.</summary>
    internal static (int Building, int Mode) Slot(int slot) => slot switch
    {
        0 => (2, 0),  // OBJ_SENTRYGUN, no mode — tf_shareddefs.h:1368-1370
        1 => (0, 0),  // OBJ_DISPENSER
        2 => (1, 0),  // OBJ_TELEPORTER, MODE_TELEPORTER_ENTRANCE — tf_shareddefs.h:1389-1390
        _ => (1, 1),  // OBJ_TELEPORTER, MODE_TELEPORTER_EXIT
    };

    /// <summary>How many slots the menu holds.</summary>
    internal const int SlotCount = 4;

    // The scheme colour the divider's fill names (clientscheme.res :19-29).
    private static readonly UiColor DividerFill = new(255, 222, 208, 255);

    private readonly ITFDestroyMenuSource _source = source;
    private readonly TFDestroyTile _tile = tile;
    private readonly IUiTextureLibrary _textures = textures;
    private readonly ITFDestroyMenuActions? _actions = actions;

    public override string Name => "tf2destroymenu";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    /// <summary>Stock's show arm: the menu is up while the destroy PDA is
    /// the active weapon (the build PDA's OnDataChanged pair,
    /// tf_weapon_pda.cpp:157-180).</summary>
    /// <summary>Stock's show arm, stood down while the scoreboard locks the
    /// hud's global group (ITFRenderGroups, hud.cpp:288-303).</summary>
    public override bool Visible => _source.DestroyerEquipped && groups?.GlobalLocked != true;

    /// <summary>The slot's state as stock's OnTick decides it
    /// (tf_hud_menu_engy_destroy.cpp:316-339): the building stands —
    /// <c>pObj != NULL &amp;&amp; !pObj-&gt;IsPlacing()</c> — or it does
    /// not. Stock's sapper and plasma-disabled gate is the send arm's,
    /// and its reads are deferred (see the screen summary).</summary>
    internal TFDestroySlotState StateOf(int slot)
    {
        var (building, mode) = Slot(slot);
        return _source.Built(building, mode)
                   ? TFDestroySlotState.Built                // :316-326
                   : TFDestroySlotState.NotBuilt;            // :327-339
    }

    public override void Describe(ref UiFrame ui)
    {
        // Stock keeps the hudlayout centred panel on the scaled-down
        // common window (the engine's ui_scale is stock's own scaling,
        // not a narrow screen — the live A/B at 1280x720 with ui_scale 2).
        if (ui.DesignWidth < UiScale.MinDesignW * 0.75f)
            DescribeStacked(ref ui);
        else
            DescribeStock(ref ui);
    }

    // ---- the stock canvas (wide screens) ----

    private void DescribeStock(ref UiFrame ui)
    {
        // The backdrop (MainBackground, hudmenuengydestroy.res). The
        // plate is what the text reads against when the art misses —
        // each art layer keeps a Tone.Surface plate at the same
        // coordinates (the TFHud.cs pattern).
        var bg = Stock.At(Stock.Background, ui.Screen);
        if (_textures.Icon("hud_menu_bg") is null)
            ui.Rect(Tone.Surface, Size.Du(bg.Wide), Size.Du(bg.Tall))
                .AtRes(UiAnchor.BottomLeft, bg.Left, bg.Bottom).Layered().Key("menu bg plate");
        if (_textures.Icon("hud_menu_bg") is { } backdrop)
            ui.Texture(backdrop.Handle, Size.Du(bg.Wide), Size.Du(bg.Tall), backdrop.Uv)
                .AtRes(UiAnchor.BottomLeft, bg.Left, bg.Bottom).Layered().Key("menu bg");

        // The divider (hudmenuengydestroy.res).
        var divider = Stock.At(Stock.Divider, ui.Screen);
        ui.Rect(UiPaint.FromGame(DividerFill),
                Size.Du(divider.Wide), Size.Du(divider.Tall))
            .AtRes(UiAnchor.BottomLeft, divider.Left, divider.Bottom).Layered().Key("divider");

        // The demolish badge (hudmenuengydestroy.res); it hangs 2 units
        // past the canvas's top, inside the screen.
        var badge = Stock.At(Stock.Badge, ui.Screen);
        _ = StockArt.DrawIcon(ref ui, _textures, "demolish badge", "ico_demolish",
                          badge.Left, badge.Bottom, badge.Wide, badge.Tall);

        // The title (hudmenuengydestroy.res).
        var title = Stock.At(Stock.Title, ui.Screen);
        ui.Text("#TF2_DestroyMenu_Title", Text.Display, Tone.Text)
            .Width(Size.Du(title.Wide)).TextAlign(Align.Start)
            .AtRes(UiAnchor.BottomLeft, title.Left, title.Bottom).Layered().Key("title");

        // The Cancel sentence with the lastinv binding already in it —
        // stock's CancelLabel names the build menu's token
        // (#Hud_Menu_Build_Cancel, hudmenuengydestroy.res): the source
        // resolves the token and the binding outside the frame, so it
        // reaches the localiser as game data (ui.Data), not as copy.
        var cancel = Stock.At(Stock.Cancel, ui.Screen);
        ui.Data(_source.CancelLabel, Text.Body, Tone.Text)
            .Width(Size.Du(cancel.Wide)).TextAlign(Align.End)
            .AtRes(UiAnchor.BottomLeft, cancel.Left, cancel.Bottom).Layered().Key("cancel");

        // The four slots in TF order: sentry, dispenser, tele entrance,
        // tele exit (g_kEngyBuildings, :35-84).
        for (int slot = 0; slot < SlotCount; slot++)
        {
            var at = Stock.At(Stock.Slot(slot), ui.Screen);
            var (building, mode) = Slot(slot);
            ui.Sub(SubKey(slot), _tile, new TFDestroyTileProps(slot + 1, NameToken(slot), Icon(slot),
                                                               slot + 1, StateOf(slot)))
                .Size(Size.Du(at.Wide), Size.Du(at.Tall))
                .AtRes(UiAnchor.BottomLeft, at.Left, at.Bottom).Layered();
            // Stock's send arm: the command goes out only from a built
            // slot (:259-266).
            if (_tile.Clicked && StateOf(slot) == TFDestroySlotState.Built)
                _actions?.Destroy(building, mode);
        }
    }

    // ---- the narrow arrangement (portrait and small windows) ----

    /// <summary>The 450-unit canvas is 1012 du wide and does not fit a
    /// narrow screen (portrait 1080x1920@1.75 is 731 du): the header keeps
    /// its row on a plate and the slots flow at the tile's own stock size
    /// — four across where the width allows, else two by two (the trade
    /// the build menu's stacked arm makes).</summary>
    private void DescribeStacked(ref UiFrame ui)
    {
        // Four tiles plus their gaps and the column's side pads.
        bool oneRow = ui.DesignWidth >= 4 * TFDestroyTile.Stock.WideDu
                      + 3 * (float)Space.S + 2 * (float)Space.L;

        using (ui.Column(Space.M, Align.Centre).Size(Size.Percent(100), Size.Percent(100))
                   .Pad(Space.L, Space.M))
        {
            // The badge over the title (hudmenuengydestroy.res), on the
            // plate the canvas's backdrop carries on wide screens.
            using (ui.Row(Space.M, Align.Centre).Fill(Tone.Surface).Round(Radius.M).Key("title bar"))
            {
                using (ui.Box().Size(Size.Du(TFDestroyTile.Stock.BadgeDu), Size.Du(TFDestroyTile.Stock.BadgeDu))
                           .Key("demolish badge"))
                    if (_textures.Icon("ico_demolish") is { } badge)
                        ui.Texture(badge.Handle, Size.Du(TFDestroyTile.Stock.BadgeDu), Size.Du(TFDestroyTile.Stock.BadgeDu), badge.Uv)
                            .At(UiAnchor.Centre);
                ui.Text("#TF2_DestroyMenu_Title", Text.Display, Tone.Text);
            }

            // The divider (hudmenuengydestroy.res), across the screen:
            // stock's 2 units on the 4 du grid token.
            ui.Rect(UiPaint.FromGame(DividerFill), Size.Fill, Size.Du((float)Space.XS))
                .Key("divider");

            // The slots in stock's order (g_kEngyBuildings, :35-84); the
            // send arm is stock's (:259-266).
            int perRow = oneRow ? 4 : 2;
            for (int row = 0; row < SlotCount / perRow; row++)
            {
                using (ui.Row(Space.S, Align.Centre))
                {
                    for (int col = 0; col < perRow; col++)
                    {
                        int slot = row * perRow + col;
                        var (building, mode) = Slot(slot);
                        ui.Sub(SubKey(slot), _tile, new TFDestroyTileProps(slot + 1, NameToken(slot), Icon(slot),
                                                                           slot + 1, StateOf(slot)))
                            .Size(Size.Du(TFDestroyTile.Stock.WideDu), Size.Du(TFDestroyTile.Stock.TallDu));
                        if (_tile.Clicked && StateOf(slot) == TFDestroySlotState.Built)
                            _actions?.Destroy(building, mode);
                    }
                }
            }

            ui.Flex();
            using (ui.Box().Width(Size.Fill).Height(Size.Du((float)Space.XXL))
                           .Fill(Tone.Surface).Round(Radius.M).Key("cancel bar"))
                ui.Data(_source.CancelLabel, Text.Body, Tone.Text)
                    .At(UiAnchor.Centre).TextAlign(Align.Centre);
        }
    }

    /// <summary>The slot's building name token (the per-slot .res's
    /// ItemNameLabel, e.g. sentry_active.res:17 names #TF_Object_Sentry;
    /// the string is this port's token, tf2_english.txt).</summary>
    private static string NameToken(int slot) => slot switch
    {
        0 => "#TF2_DestroyMenu_Sentry",
        1 => "#TF2_DestroyMenu_Dispenser",
        2 => "#TF2_DestroyMenu_TeleEntrance",
        _ => "#TF2_DestroyMenu_TeleExit",
    };

    /// <summary>The slot's building art (the per-slot .res's BuildingIcon
    /// icon key, e.g. sentry_active.res:68).</summary>
    private static string Icon(int slot) => slot switch
    {
        0 => "obj_status_sentrygun_1",     // sentry_active.res:68
        1 => "obj_status_dispenser",       // dispenser_active.res:67
        2 => "obj_status_tele_entrance",   // tele_entrance_active.res:67
        _ => "obj_status_tele_exit",       // tele_exit_active.res:67
    };

    /// <summary>The sub key of stock's own fieldName for the slot
    /// (hudmenuengydestroy.res's active_item_1..4).</summary>
    private static string SubKey(int slot) => slot switch
    {
        0 => "active_item_1",
        1 => "active_item_2",
        2 => "active_item_3",
        _ => "active_item_4",
    };

    /// <summary>The canvas and children of hudmenuengydestroy.res,
    /// resolved to du per Describe at the frame's screen.</summary>
    internal static class Stock
    {
        // HudMenuEngyDestroy (hudlayout.res:1006-1016) is the panel the
        // .res children sit in.
        private static readonly StockBox Canvas = new("c-225", "c-59", 450, 200);

        // MainBackground, Divider, DestroyIcon, TitleLabel, CancelLabel
        // (hudmenuengydestroy.res) and the four slots.
        public static readonly StockBox Background = new("0", "14", 450, 170);
        public static readonly StockBox Divider = new("8", "51", 436, 2);
        public static readonly StockBox Badge = new("0", "-2", 64, 64);
        public static readonly StockBox Title = new("31", "4", 300, 38);
        public static readonly StockBox Cancel = new("218", "39", 200, 13);

        public static readonly StockBox Slot1 = new("25", "51", 100, 124);
        public static readonly StockBox Slot2 = new("125", "51", 100, 124);
        public static readonly StockBox Slot3 = new("225", "51", 100, 124);
        public static readonly StockBox Slot4 = new("325", "51", 100, 124);

        public static StockBox Slot(int slot) => slot switch
        {
            0 => Slot1,
            1 => Slot2,
            2 => Slot3,
            _ => Slot4,
        };

        /// <summary>The box's spot on the frame's screen: its edges in du
        /// from the screen's bottom-left (the AtRes offsets from
        /// BottomLeft).</summary>
        public static StockSpot At(StockBox box, in ScreenInfo screen)
        {
            var canvas = Canvas.Rect(StockRes.Screen(screen));
            return StockRes.Spot(box.Rect(canvas), StockRes.Screen(screen));
        }
    }
}

/// <summary>What one of the destroy menu's two .res states shows
/// (base_active.res, base_inactive.res): the OnTick arms of
/// tf_hud_menu_engy_destroy.cpp:316-339.</summary>
public enum TFDestroySlotState : byte
{
    /// <summary>base_inactive.res: the transparent background and the
    /// <c>#TF_NotBuilt</c> reason.</summary>
    NotBuilt,
    /// <summary>base_active.res: the filled background, the demolish
    /// cross over the building art at half alpha.</summary>
    Built,
}

/// <summary>What one slot shows: its 1-based slot number and name token,
/// its building art, the key chip's number, and the state the OnTick
/// arms chose.</summary>
public readonly record struct TFDestroyTileProps(int Slot, string NameToken, string Icon,
                                                 int Number, TFDestroySlotState State);

/// <summary>
/// One slot of the destroy menu (base_active.res and its not-built
/// arm base_inactive.res): the building's name 6,0 84x15 over the 98x105
/// outlined background at 4,14; while built, the demolish cross 13,18
/// 70x70 over the building art (the per-slot .res's box, half alpha);
/// while not, the <c>#TF_NotBuilt</c> label 10,48 80x18; and the key
/// chip (ico_key_blank 41,99 18x18 with the number 0,98) at the bottom.
/// The whole tile is the click target; a miss in the library leaves the
/// fallback drawing.
/// </summary>
public sealed class TFDestroyTile(IUiTextureLibrary textures) : UiPanel<TFDestroyTileProps>
{
    /// <summary>Whether the last described tile was clicked.</summary>
    public bool Clicked { get; private set; }

    // The scheme colours the .res arms name (clientscheme.res).
    private static readonly UiColor MenuText = new(235, 226, 202, 255);      // TanLight :100
    private static readonly UiColor OffWhite = new(251, 235, 202, 255);      // ProgressOffWhite :108
    private static readonly UiColor OffWhiteTransparent = new(251, 235, 202, 128); // :112
    private static readonly UiColor HalfWhite = new(255, 255, 255, 128);     // the building art's iconColor
    private static readonly UiColor Black = new(0, 0, 0, 255);               // the chip number's

    public override void Describe(ref UiFrame ui, in TFDestroyTileProps p)
    {
        var tile = ui.Box().Size(Size.Fill, Size.Fill).Key("tile");
        var hit = ui.Interact(tile);
        Clicked = hit.Clicked;
        using (tile)
        {
            // The outlined background in its state's tint (base_active.res:23-37,
            // base_inactive.res:23-37 — same box and art, other tint). The
            // plate carries the tile's texts when the art misses — stock's
            // item background is translucent tan either way, and the name
            // hangs above the art box (6,0 over 4,14).
            if (textures.Icon("hud_menu_item_bg_outline") is null)
                ui.Rect(Tone.Surface, Size.Du(Stock.WideDu), Size.Du(Stock.TallDu))
                    .AtRes(UiAnchor.BottomLeft, 0f, 0f).Layered().Key("item bg plate");
            var bg = textures.Icon("hud_menu_item_bg_outline");
            if (bg is { } itemBg)
            {
                var at = Stock.At(Stock.Background);
                ui.Texture(itemBg.Handle, Size.Du(at.Wide), Size.Du(at.Tall), itemBg.Uv)
                    .AtRes(UiAnchor.BottomLeft, at.Left, at.Bottom).Layered()
                    .Tint(UiPaint.FromGame(p.State == TFDestroySlotState.Built
                                              ? OffWhite
                                              : OffWhiteTransparent))
                    .Key("item bg");
            }

            // The built arm (base_active.res): the demolish cross
            // (DestroyIcon, :39-53) rides the building art
            // (BuildingIcon, the per-slot .res, half alpha) — stock's
            // zpos 2 under the cross's 1.
            if (p.State == TFDestroySlotState.Built)
            {
                if (textures.Icon("ico_demolish") is { } cross)
                {
                    var d = Stock.At(Stock.Destroy);
                    ui.Texture(cross.Handle, Size.Du(d.Wide), Size.Du(d.Tall), cross.Uv)
                        .AtRes(UiAnchor.BottomLeft, d.Left, d.Bottom).Layered().Key("destroy icon");
                }
                if (textures.Icon(p.Icon) is { } building)
                {
                    var b = Stock.At(Stock.Building(p.Slot - 1));
                    ui.Texture(building.Handle, Size.Du(b.Wide), Size.Du(b.Tall), building.Uv)
                        .AtRes(UiAnchor.BottomLeft, b.Left, b.Bottom).Layered()
                        .Tint(UiPaint.FromGame(HalfWhite)).Key("building");
                }
            }
            else
            {
                // The not-built arm (NotBuiltLabel, base_inactive.res:39-57).
                var n = Stock.At(Stock.NotBuilt);
                var note = ui.Text("#TF2_DestroyMenu_NotBuilt", Text.Body, Tone.Text);
                if (bg is { })
                    note.Color(UiPaint.FromGame(MenuText));
                note.Width(Size.Du(n.Wide)).TextAlign(Align.Centre)
                    .AtRes(UiAnchor.BottomLeft, n.Left, n.Bottom).Layered().Key(NotBuiltKey(p.Slot));
            }

            // The name (ItemNameLabel, base_active.res:3-22).
            var name = Stock.At(Stock.Name);
            ui.Text(p.NameToken, Text.Body, Tone.Text).Color(UiPaint.FromGame(MenuText))
                .Width(Size.Du(name.Wide)).TextAlign(Align.Start)
                .AtRes(UiAnchor.BottomLeft, name.Left, name.Bottom).Layered().Key("name");

            // The key chip (NumberBg + NumberLabel, base_active.res:71-106).
            // Over the plate the fallback keeps the theme ink; stock's
            // Black rides its light key art, dimmed while not built.
            var chip = Stock.At(Stock.NumberBg);
            var chipArt = textures.Icon("ico_key_blank");
            if (chipArt is { } chipIcon)
                ui.Texture(chipIcon.Handle, Size.Du(chip.Wide), Size.Du(chip.Tall), chipIcon.Uv)
                    .AtRes(UiAnchor.BottomLeft, chip.Left, chip.Bottom).Layered()
                    .Tint(UiPaint.FromGame(p.State == TFDestroySlotState.Built
                                              ? OffWhite
                                              : OffWhiteTransparent)).Key("chip");
            var number = Stock.At(Stock.Number);
            var digit = ui.Number(p.Number, Text.Number, Tone.Text);
            if (chipArt is { })
                digit.Color(UiPaint.FromGame(Black));
            digit.Width(Size.Du(number.Wide)).TextAlign(Align.Centre)
                .AtRes(UiAnchor.BottomLeft, number.Left, number.Bottom).Layered().Key(NumberKey(p.Slot));
        }
    }

    /// <summary>The per-slot keys the facts read (the tile is Sub'd four
    /// times; a constant key would name the first one only).</summary>
    private static string NotBuiltKey(int slot) => slot switch
    {
        1 => "not-built-1", 2 => "not-built-2", 3 => "not-built-3", _ => "not-built-4",
    };

    private static string NumberKey(int slot) => slot switch
    {
        1 => "number-1", 2 => "number-2", 3 => "number-3", _ => "number-4",
    };

    /// <summary>The tile's own 100x124 stock space (base_active.res's
    /// boxes), resolved to du per Describe.</summary>
    internal static class Stock
    {
        private static readonly StockRect Tile = new(0f, 0f, 100f, 124f);

        /// <summary>The tile's du size: the stock 100x124 at the scale.</summary>
        public static float WideDu => Tile.Wide * StockRes.Scale;
        public static float TallDu => Tile.Tall * StockRes.Scale;

        /// <summary>The demolish badge's du size (64x64 stock,
        /// hudmenuengydestroy.res).</summary>
        public static float BadgeDu => 64f * StockRes.Scale;

        public static readonly StockBox Name = new("6", "0", 84, 15);
        public static readonly StockBox Background = new("4", "14", 98, 105);
        public static readonly StockBox Destroy = new("13", "18", 70, 70);
        public static readonly StockBox NotBuilt = new("10", "48", 80, 18);
        public static readonly StockBox NumberBg = new("41", "99", 18, 18);
        public static readonly StockBox Number = new("0", "98", 100, 18);

        /// <summary>The building art's box: sentry 10,8 80x80
        /// (sentry_active.res:61-64), dispenser 10,16 80x80
        /// (base_active.res:59-62), each teleporter end 20,30 60x60
        /// (tele_entrance_active.res:59-62).</summary>
        public static StockBox Building(int slot) => slot switch
        {
            0 => new StockBox("10", "8", 80, 80),
            1 => new StockBox("10", "16", 80, 80),
            _ => new StockBox("20", "30", 60, 60),
        };

        /// <summary>The box's spot in du from the tile's bottom-left.</summary>
        public static StockSpot At(StockBox box) => StockRes.Spot(box.Rect(Tile), Tile);
    }
}

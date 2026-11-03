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
/// The spy's disguise menu (stock's <c>CHudMenuSpyDisguise</c>,
/// game/client/tf/tf_hud_menu_spy_disguise.cpp) at stock's own layout:
/// HudMenuSpyDisguise's canvas (hudlayout.res:1030-1043, c-235 c-52
/// 470x200) with hudmenuspydisguise.res's children on it — the
/// hud_menu_bg backdrop 0,15 470x170, the 255 222 208 divider 8,65
/// 456x2, the hud_spy_disguise_menu_icon badge 10,-2 45x45, the
/// Disguise title 55,5 360x38, the toggle hint 20,49 200x13, the Cancel
/// sentence 250,49 200x13 east (stock names the build menu's token
/// there), and the nine 45x120 class tiles at x 20/60/100/172/212/252/
/// 324/364/404, y 50 — one row of stock's per-team sets
/// (class_item_red_1..9 / class_item_blue_1..9). The menu shows only
/// while the disguise kit is the active weapon and the local player is
/// not taunting (ShouldDraw, :169-179).
///
/// <para>
/// The menu arms showing the enemy team — stock's spy_pda_reset event
/// arm (:607-629) — and a toggle flips it (:569-582). A tile click is
/// stock's <c>SelectDisguise</c> (:543-564): the class and the shown
/// team go out in the interception's own naming, the enemy team -1 and
/// the player's own -2, because the menu's numbers do not match the
/// defines (c_tf_player.cpp:9637-9679; TFSpyDisguiseCommand's mapping).
/// The key arms (slot1-9, disguiseteam, next_disguise, :184-436) are
/// the HUD input layer's, deferred with the build menu's.
/// </para>
/// </summary>
public sealed class TFDisguiseMenu(ITFDisguiseMenuSource source, TFDisguiseTile tile,
                                   IUiTextureLibrary textures, ITFDisguiseMenuActions? actions = null,
                                   ITFRenderGroups? groups = null)
    : UiScreen
{
    /// <summary>How many class tiles the menu holds.</summary>
    internal const int ClassCount = 9;

    // Stock's m_iShowingTeam as a toggle: false shows the enemy team,
    // true the player's own (ToggleDisguiseTeam, :569-582).
    private bool _toggled;

    private readonly ITFDisguiseMenuSource _source = source;
    private readonly TFDisguiseTile _tile = tile;
    private readonly IUiTextureLibrary _textures = textures;
    private readonly ITFDisguiseMenuActions? _actions = actions;

    public override string Name => "tf2disguisemenu";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    /// <summary>Stock's show arm: the disguise kit is the active weapon
    /// (the PDA's OnDataChanged pair, tf_weapon_pda.cpp:157-180), stood
    /// down while the scoreboard locks the hud's global group
    /// (ITFRenderGroups, hud.cpp:288-303).</summary>
    public override bool Visible => _source.DisguiseKitEquipped && groups?.GlobalLocked != true;

    /// <summary>The team the tiles show, the raw TF number (2 RED, 3
    /// BLU): the enemy until toggled (:607-629, :569-582); no team yet
    /// and stock's constructor default RED stands.</summary>
    internal int ShowingTeam
    {
        get
        {
            int team = _source.Team is 2 or 3 ? _source.Team : 2;
            return _toggled ? team : (team == 2 ? 3 : 2);
        }
    }

    /// <summary>The toggle arm (stock's disguiseteam/+reload keys,
    /// :308-312, over ToggleDisguiseTeam :569-582). The HUD input layer
    /// calls it when a bound key lands while the menu is up.</summary>
    public void ToggleTeam() => _toggled = !_toggled;

    public override void Describe(ref UiFrame ui)
    {
        // A redeploy re-arms the enemy team (the spy_pda_reset event,
        // :613-624): the toggle lives for one kit deployment.
        if (!_source.DisguiseKitEquipped)
            _toggled = false;

        if (ui.Narrow)
            DescribeStacked(ref ui);
        else
            DescribeStock(ref ui);
    }

    // ---- the stock canvas (wide screens) ----

    private void DescribeStock(ref UiFrame ui)
    {
        bool blue = ShowingTeam == 3;

        // The backdrop (MainBackground, hudmenuspydisguise.res:3-17).
        // The plate is what the text reads against when the art misses —
        // each art layer keeps a Tone.Surface plate at the same
        // coordinates (the TFHud.cs pattern).
        var bg = Stock.At(Stock.Background, ui.Screen);
        if (_textures.Icon("hud_menu_bg") is null)
            ui.Rect(Tone.Surface, Size.Du(bg.Wide), Size.Du(bg.Tall))
                .AtRes(UiAnchor.BottomLeft, bg.Left, bg.Bottom).Layered().Key("menu bg plate");
        if (_textures.Icon("hud_menu_bg") is { } backdrop)
            ui.Texture(backdrop.Handle, Size.Du(bg.Wide), Size.Du(bg.Tall), backdrop.Uv)
                .AtRes(UiAnchor.BottomLeft, bg.Left, bg.Bottom).Layered().Key("menu bg");

        // The divider (hudmenuspydisguise.res:19-29).
        var divider = Stock.At(Stock.Divider, ui.Screen);
        ui.Rect(UiPaint.FromGame(DividerFill),
                Size.Du(divider.Wide), Size.Du(divider.Tall))
            .AtRes(UiAnchor.BottomLeft, divider.Left, divider.Bottom).Layered().Key("divider");

        // The badge (SpyIcon, hudmenuspydisguise.res:31-45); it hangs 2
        // units past the canvas's top, inside the screen.
        var badge = Stock.At(Stock.Badge, ui.Screen);
        _ = StockArt.DrawIcon(ref ui, _textures, "disguise badge", "hud_spy_disguise_menu_icon",
                          badge.Left, badge.Bottom, badge.Wide, badge.Tall);

        // The title (TitleLabel, hudmenuspydisguise.res:47-65). Stock's
        // one-unit black dropshadow is carried by the plate instead — a
        // second text run would be unreadable ink on the plate, the
        // trade every plate makes.
        var title = Stock.At(Stock.Title, ui.Screen);
        ui.Text("#TF2_DisguiseMenu_Title", Text.Display, Tone.Text)
            .Width(Size.Du(title.Wide)).TextAlign(Align.Start)
            .AtRes(UiAnchor.BottomLeft, title.Left, title.Bottom).Layered().Key("title");

        // The nine class tiles of the shown team (class_item_red_1..9 /
        // class_item_blue_1..9, hudmenuspydisguise.res:128-342), in
        // stock's iRemapKeyToClass order (:197-208).
        for (int slot = 0; slot < ClassCount; slot++)
        {
            var at = Stock.At(Stock.Slot(slot), ui.Screen);
            ui.Sub(SubKey(slot, blue), _tile,
                   new TFDisguiseTileProps(slot + 1, Icon(slot, blue), slot + 1))
                .Size(Size.Du(at.Wide), Size.Du(at.Tall))
                .AtRes(UiAnchor.BottomLeft, at.Left, at.Bottom).Layered();
            // Stock's SelectDisguise (:543-564): the class and the shown
            // team, in the interception's naming — the enemy -1, the
            // player's own -2 (c_tf_player.cpp:9637-9679).
            if (_tile.Clicked)
                _actions?.Disguise(slot + 1, blue ? (_source.Team == 3 ? -2 : -1)
                                                  : (_source.Team == 2 ? -2 : -1));
        }

        // The toggle and cancel sentences with their bindings already in
        // them (ToggleLabel and CancelLabel, hudmenuspydisguise.res:88-126;
        // FindToggleBinding's three dialog variables, :453-476): the
        // source resolves the tokens and the bindings outside the frame,
        // so they reach the localiser as game data (ui.Data), not as
        // copy. They hang one unit into the tiles' band (49 against the
        // tiles' 50) — stock's hint row rides the translucent art, so it
        // draws after the tiles here.
        var toggle = Stock.At(Stock.Toggle, ui.Screen);
        ui.Data(_source.ToggleLabel, Text.Body, Tone.Text)
            .Width(Size.Du(toggle.Wide)).TextAlign(Align.Start)
            .AtRes(UiAnchor.BottomLeft, toggle.Left, toggle.Bottom).Layered().Key("toggle");
        var cancel = Stock.At(Stock.Cancel, ui.Screen);
        ui.Data(_source.CancelLabel, Text.Body, Tone.Text)
            .Width(Size.Du(cancel.Wide)).TextAlign(Align.End)
            .AtRes(UiAnchor.BottomLeft, cancel.Left, cancel.Bottom).Layered().Key("cancel");
    }

    // ---- the narrow arrangement (portrait and small windows) ----

    /// <summary>The 470-unit canvas is 1057 du wide and does not fit a
    /// narrow screen (portrait 1080x1920@1.75 is 731 du): the header
    /// keeps its row on a plate and the tiles flow at their own stock
    /// size — nine across where the width allows (a 720-du-tall landscape
    /// screen cannot spend three rows of 270 on the tiles), else three to
    /// a row, stock's own three groups (the x runs 20/60/100,
    /// 172/212/252, 324/364/404).</summary>
    private void DescribeStacked(ref UiFrame ui)
    {
        bool blue = ShowingTeam == 3;

        // Nine tiles plus their gaps and the column's side pads.
        bool oneRow = ui.DesignWidth >= 9 * TFDisguiseTile.Stock.WideDu
                      + 8 * (float)Space.S + 2 * (float)Space.L;
        int perRow = oneRow ? 9 : 3;

        using (ui.Column(Space.M, Align.Centre).Size(Size.Percent(100), Size.Percent(100))
                   .Pad(Space.L, Space.M))
        {
            // The badge over the title (hudmenuspydisguise.res:31-65),
            // on the plate the canvas's backdrop carries on wide screens.
            using (ui.Row(Space.M, Align.Centre).Fill(Tone.Surface).Round(Radius.M).Key("title bar"))
            {
                using (ui.Box().Size(Size.Du(TFDisguiseTile.Stock.BadgeDu), Size.Du(TFDisguiseTile.Stock.BadgeDu))
                           .Key("disguise badge"))
                    if (_textures.Icon("hud_spy_disguise_menu_icon") is { } badge)
                        ui.Texture(badge.Handle, Size.Du(TFDisguiseTile.Stock.BadgeDu), Size.Du(TFDisguiseTile.Stock.BadgeDu), badge.Uv)
                            .At(UiAnchor.Centre);
                ui.Text("#TF2_DisguiseMenu_Title", Text.Display, Tone.Text);
            }

            // The divider (hudmenu spydisguise.res:19-29), across the
            // screen: stock's 2 units on the 4 du grid token.
            ui.Rect(UiPaint.FromGame(DividerFill), Size.Fill, Size.Du((float)Space.XS))
                .Key("divider");

            // The tiles of the shown team (stock's own groups where the
            // width asks for three rows); the send arm is stock's
            // SelectDisguise (:543-564).
            for (int row = 0; row < ClassCount / perRow; row++)
            {
                using (ui.Row(Space.S, Align.Centre))
                {
                    for (int col = 0; col < perRow; col++)
                    {
                        int slot = row * perRow + col;
                        ui.Sub(SubKey(slot, blue), _tile,
                               new TFDisguiseTileProps(slot + 1, Icon(slot, blue), slot + 1))
                            .Size(Size.Du(TFDisguiseTile.Stock.WideDu), Size.Du(TFDisguiseTile.Stock.TallDu));
                        if (_tile.Clicked)
                            _actions?.Disguise(slot + 1, blue ? (_source.Team == 3 ? -2 : -1)
                                                              : (_source.Team == 2 ? -2 : -1));
                    }
                }
            }

            ui.Flex();
            using (ui.Box().Width(Size.Fill).Height(Size.Du((float)Space.XL))
                           .Fill(Tone.Surface).Round(Radius.M).Key("toggle bar"))
                ui.Data(_source.ToggleLabel, Text.Body, Tone.Text)
                    .At(UiAnchor.Centre).TextAlign(Align.Centre);
            using (ui.Box().Width(Size.Fill).Height(Size.Du((float)Space.XL))
                           .Fill(Tone.Surface).Round(Radius.M).Key("cancel bar"))
                ui.Data(_source.CancelLabel, Text.Body, Tone.Text)
                    .At(UiAnchor.Centre).TextAlign(Align.Centre);
        }
    }

    // The scheme colour the divider's fill names (clientscheme.res :19-29).
    private static readonly UiColor DividerFill = new(255, 222, 208, 255);

    /// <summary>The tile's class art, per team (the per-class .res's
    /// ClassIcon icon key, e.g. scout_red.res:35 "hud_menu_scout_red";
    /// stock's blue file spells it _blu, scout_blue.res:35).</summary>
    private static string Icon(int slot, bool blue) => (slot, blue) switch
    {
        (0, false) => "hud_menu_scout_red",
        (1, false) => "hud_menu_soldier_red",
        (2, false) => "hud_menu_pyro_red",
        (3, false) => "hud_menu_demoman_red",
        (4, false) => "hud_menu_heavy_red",
        (5, false) => "hud_menu_engineer_red",
        (6, false) => "hud_menu_medic_red",
        (7, false) => "hud_menu_sniper_red",
        (8, false) => "hud_menu_spy_red",
        (0, true) => "hud_menu_scout_blu",
        (1, true) => "hud_menu_soldier_blu",
        (2, true) => "hud_menu_pyro_blu",
        (3, true) => "hud_menu_demoman_blu",
        (4, true) => "hud_menu_heavy_blu",
        (5, true) => "hud_menu_engineer_blu",
        (6, true) => "hud_menu_medic_blu",
        (7, true) => "hud_menu_sniper_blu",
        _ => "hud_menu_spy_blu",
    };

    /// <summary>The sub key of stock's own fieldName for the tile
    /// (hudmenuspydisguise.res:128-342).</summary>
    private static string SubKey(int slot, bool blue) => blue ? BlueKey(slot) : RedKey(slot);

    private static string RedKey(int slot) => slot switch
    {
        0 => "class_item_red_1",
        1 => "class_item_red_2",
        2 => "class_item_red_3",
        3 => "class_item_red_4",
        4 => "class_item_red_5",
        5 => "class_item_red_6",
        6 => "class_item_red_7",
        7 => "class_item_red_8",
        _ => "class_item_red_9",
    };

    private static string BlueKey(int slot) => slot switch
    {
        0 => "class_item_blue_1",
        1 => "class_item_blue_2",
        2 => "class_item_blue_3",
        3 => "class_item_blue_4",
        4 => "class_item_blue_5",
        5 => "class_item_blue_6",
        6 => "class_item_blue_7",
        7 => "class_item_blue_8",
        _ => "class_item_blue_9",
    };

    /// <summary>The canvas and children of hudmenuspydisguise.res,
    /// resolved to du per Describe at the frame's screen.</summary>
    internal static class Stock
    {
        // HudMenuSpyDisguise (hudlayout.res:1030-1043, the $WIN32 tall)
        // is the panel the .res children sit in.
        private static readonly StockBox Canvas = new("c-235", "c-52", 470, 200);

        // MainBackground (:3-17), Divider (:19-29), SpyIcon (:31-45),
        // TitleLabel (:47-65), ToggleLabel (:88-106), CancelLabel
        // (:108-126), and the nine tiles per team (:128-342).
        public static readonly StockBox Background = new("0", "15", 470, 170);
        public static readonly StockBox Divider = new("8", "65", 456, 2);
        public static readonly StockBox Badge = new("10", "-2", 45, 45);
        public static readonly StockBox Title = new("55", "5", 360, 38);
        public static readonly StockBox Toggle = new("20", "49", 200, 13);
        public static readonly StockBox Cancel = new("250", "49", 200, 13);

        public static readonly StockBox Slot1 = new("20", "50", 45, 120);
        public static readonly StockBox Slot2 = new("60", "50", 45, 120);
        public static readonly StockBox Slot3 = new("100", "50", 45, 120);
        public static readonly StockBox Slot4 = new("172", "50", 45, 120);
        public static readonly StockBox Slot5 = new("212", "50", 45, 120);
        public static readonly StockBox Slot6 = new("252", "50", 45, 120);
        public static readonly StockBox Slot7 = new("324", "50", 45, 120);
        public static readonly StockBox Slot8 = new("364", "50", 45, 120);
        public static readonly StockBox Slot9 = new("404", "50", 45, 120);

        public static StockBox Slot(int slot) => slot switch
        {
            0 => Slot1,
            1 => Slot2,
            2 => Slot3,
            3 => Slot4,
            4 => Slot5,
            5 => Slot6,
            6 => Slot7,
            7 => Slot8,
            _ => Slot9,
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

/// <summary>What one disguise tile shows: its 1-based class number, the
/// class art for the shown team, and the key chip's number.</summary>
public readonly record struct TFDisguiseTileProps(int ClassNumber, string Icon, int Number);

/// <summary>
/// One tile of the disguise menu (the per-class .res, e.g.
/// scout_red.res): the class art 0,0 45x90 (ClassIcon, :23-36) over the
/// tile and the key chip (ico_key_blank 15,90 15x15 with the number,
/// :38-94). The name label stock keeps invisible (ItemName, :3-21,
/// visible 0) stays invisible. The whole tile is the click target; a
/// miss in the library leaves the fallback drawing.
/// </summary>
public sealed class TFDisguiseTile(IUiTextureLibrary textures) : UiPanel<TFDisguiseTileProps>
{
    /// <summary>Whether the last described tile was clicked.</summary>
    public bool Clicked { get; private set; }

    // The scheme colours the .res names (clientscheme.res).
    private static readonly UiColor OffWhite = new(251, 235, 202, 255);      // ProgressOffWhite :108
    private static readonly UiColor Black = new(0, 0, 0, 255);               // the chip number's

    public override void Describe(ref UiFrame ui, in TFDisguiseTileProps p)
    {
        var tile = ui.Box().Size(Size.Fill, Size.Fill).Key("tile");
        var hit = ui.Interact(tile);
        Clicked = hit.Clicked;
        using (tile)
        {
            // The plate carries the tile when the art misses — stock's
            // icon art is translucent tan, and nothing else draws on the
            // tile's upper band.
            if (textures.Icon(p.Icon) is null)
                ui.Rect(Tone.Surface, Size.Du(Stock.WideDu), Size.Du(Stock.TallDu))
                    .AtRes(UiAnchor.BottomLeft, 0f, 0f).Layered().Key("item bg plate");

            // The class art (ClassIcon, scout_red.res:23-36): the icon
            // key through the tables, its UV rectangle of the file.
            if (textures.Icon(p.Icon) is { } icon)
            {
                var at = Stock.At(Stock.ClassIcon);
                ui.Texture(icon.Handle, Size.Du(at.Wide), Size.Du(at.Tall), icon.Uv)
                    .AtRes(UiAnchor.BottomLeft, at.Left, at.Bottom).Layered().Key("class icon");
            }

            // The key chip (NumberBg + NumberLabel, scout_red.res:38-94).
            // Over the plate the fallback keeps the theme ink; stock's
            // Black rides its light key art.
            var chip = Stock.At(Stock.NumberBg);
            var chipArt = textures.Icon("ico_key_blank");
            if (chipArt is { } chipIcon)
                ui.Texture(chipIcon.Handle, Size.Du(chip.Wide), Size.Du(chip.Tall), chipIcon.Uv)
                    .AtRes(UiAnchor.BottomLeft, chip.Left, chip.Bottom).Layered().Key("chip");
            var number = Stock.At(Stock.Number);
            var digit = ui.Number(p.Number, Text.Number, Tone.Text);
            if (chipArt is { })
                digit.Color(UiPaint.FromGame(Black));
            digit.Width(Size.Du(number.Wide)).TextAlign(Align.Centre)
                .AtRes(UiAnchor.BottomLeft, number.Left, number.Bottom)
                .Layered().Key(NumberKey(p.Number));
        }
    }

    /// <summary>The per-tile keys the facts read (the tile is Sub'd nine
    /// times; a constant key would name the first one only).</summary>
    private static string NumberKey(int number) => number switch
    {
        1 => "number-1", 2 => "number-2", 3 => "number-3",
        4 => "number-4", 5 => "number-5", 6 => "number-6",
        7 => "number-7", 8 => "number-8", _ => "number-9",
    };

    /// <summary>The tile's own 45x120 stock space (the per-class .res's
    /// boxes), resolved to du per Describe.</summary>
    internal static class Stock
    {
        private static readonly StockRect Tile = new(0f, 0f, 45f, 120f);

        /// <summary>The tile's du size: the stock 45x120 at the scale.</summary>
        public static float WideDu => Tile.Wide * StockRes.Scale;
        public static float TallDu => Tile.Tall * StockRes.Scale;

        /// <summary>The disguise badge's du size (45x45 stock,
        /// hudmenuspydisguise.res:31-45).</summary>
        public static float BadgeDu => 45f * StockRes.Scale;

        public static readonly StockBox ClassIcon = new("0", "0", 45, 90);
        public static readonly StockBox NumberBg = new("15", "90", 15, 15);
        public static readonly StockBox Number = new("15", "90", 15, 15);

        /// <summary>The box's spot in du from the tile's bottom-left.</summary>
        public static StockSpot At(StockBox box) => StockRes.Spot(box.Rect(Tile), Tile);
    }
}

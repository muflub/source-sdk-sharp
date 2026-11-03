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
/// The engineer's build menu (stock's <c>CHudMenuEngyBuild</c>,
/// game/client/tf/tf_hud_menu_engy_build.cpp) at stock's own layout:
/// HudMenuEngyBuild's canvas (hudlayout.res:994-1004, c-225 c-55 450x195)
/// with hudmenuengybuild.res's children on it — the hud_menu_bg backdrop
/// 0,10 450x170, the 255 222 208 divider 8,47 436x2, the ico_build badge
/// 15,-8 48x48 over its black shadow 16,-7, the Build title 68,0 300x38
/// over its one-unit dropshadow, the Cancel sentence 218,35 200x13 east,
/// and the four 100x124 slots at x 25/125/225/325, y 47. The menu shows
/// only while the build PDA is the active weapon (stock's show arm, the
/// PDA's OnDataChanged setting m_bBuilderEquipped, tf_weapon_pda.cpp:157-180).
///
/// <para>
/// A slot carries its building's name, art, metal price and key chip
/// (base_active.res), in one of stock's three states (OnTick's
/// :561-580): AlreadyBuilt — the player's own object of the kind exists
/// and is not placing while <c>CanBuild</c> says no (:562-565) — draws
/// base_already_built.res, the outlined background and the
/// <c>#Hud_Menu_Build_Already_Built</c> reason over the dimmed cost;
/// CantAfford — the metal account under the price (:572-575) — draws
/// base_cant_afford.res, the reason and the cost in RedSolid; otherwise
/// the slot is Available. The fourth .res state (base_unavailable.res)
/// arms only for m_bEnabled == false slots, which OnTick skips with
/// <c>continue</c> (:553-556) — no milestone mode draws it, so it is
/// not reachable here.
/// </para>
/// <para>
/// A slot click is stock's <c>SendBuildMessage</c> (:447-487): the
/// OBJ_* number and its mode go out as the slot's <c>build %d %d</c>
/// (:475-476) only from an available slot — stock's own arm refuses
/// when the building exists or the metal is short (:472) and plays the
/// deny sound instead, which this port's UI has no crossing for.
/// </para>
/// </summary>
public sealed class TFBuildMenu(ITFBuildMenuSource source, TFBuildTile tile,
                                IUiTextureLibrary textures, ITFBuildMenuActions? actions = null,
                                ITFRenderGroups? groups = null)
    : UiScreen
{
    /// <summary>Stock's <c>g_kEngyBuildings</c> (tf_hud_menu_engy_build.cpp:35-84):
    /// the OBJ_*/mode pair each slot sends, all four m_bEnabled.</summary>
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

    private readonly ITFBuildMenuSource _source = source;
    private readonly TFBuildTile _tile = tile;
    private readonly IUiTextureLibrary _textures = textures;
    private readonly ITFBuildMenuActions? _actions = actions;

    public override string Name => "tf2buildmenu";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    /// <summary>Stock's show arm: the menu is up while the build PDA is
    /// the active weapon (tf_weapon_pda.cpp:157-180), stood down while the
    /// scoreboard locks the hud's global group (ITFRenderGroups,
    /// hud.cpp:288-303).</summary>
    public override bool Visible => _source.BuilderEquipped && groups?.GlobalLocked != true;

    /// <summary>The slot's state as stock's OnTick decides it
    /// (tf_hud_menu_engy_build.cpp:561-580): already built first, then
    /// the metal account against the price, else available.</summary>
    internal TFBuildSlotState StateOf(int slot)
    {
        var (building, mode) = Slot(slot);
        if (_source.Built(building, mode) && !_source.CanBuild(building, mode))
            return TFBuildSlotState.AlreadyBuilt;           // :562-565
        if (_source.Metal < _source.Cost(building, mode))
            return TFBuildSlotState.CantAfford;             // :572-575
        return TFBuildSlotState.Available;
    }

    public override void Describe(ref UiFrame ui)
    {
        // Stock keeps the hudlayout centred panel on the scaled-down
        // common window: the engine's ui_scale is stock's own scaling,
        // not a narrow screen (the live A/B at 1280x720 with ui_scale 2).
        // The stacked column is the portrait fallback only.
        if (ui.DesignWidth < UiScale.MinDesignW * 0.75f)
            DescribeStacked(ref ui);
        else
            DescribeStock(ref ui);
    }

    // ---- the stock canvas (wide screens) ----

    private void DescribeStock(ref UiFrame ui)
    {
        // The backdrop (MainBackground, hudmenuengybuild.res:4-17). The
        // plate is what the text reads against when the art misses — each
        // art layer keeps a Tone.Surface plate at the same coordinates
        // (the TFHud.cs pattern).
        var bg = Stock.At(Stock.Background, ui.Screen);
        if (_textures.Icon("hud_menu_bg") is null)
            ui.Rect(Tone.Surface, Size.Du(bg.Wide), Size.Du(bg.Tall))
                .AtRes(UiAnchor.BottomLeft, bg.Left, bg.Bottom).Layered().Key("menu bg plate");
        if (_textures.Icon("hud_menu_bg") is { } backdrop)
            ui.Texture(backdrop.Handle, Size.Du(bg.Wide), Size.Du(bg.Tall), backdrop.Uv)
                .AtRes(UiAnchor.BottomLeft, bg.Left, bg.Bottom).Layered().Key("menu bg");

        // The divider (hudmenuengybuild.res:19-29).
        var divider = Stock.At(Stock.Divider, ui.Screen);
        ui.Rect(UiPaint.FromGame(DividerFill),
                Size.Du(divider.Wide), Size.Du(divider.Tall))
            .AtRes(UiAnchor.BottomLeft, divider.Left, divider.Bottom).Layered().Key("divider");

        // The badge over its shadow (hudmenuengybuild.res:31-61; the
        // shadow's zpos 0 draws under the badge's zpos 1, and both hang
        // 8 units past the canvas's top, inside the screen).
        var badgeShadow = Stock.At(Stock.BuildShadow, ui.Screen);
        _ = StockArt.DrawIcon(ref ui, _textures, "build shadow", "ico_build",
                          badgeShadow.Left, badgeShadow.Bottom, badgeShadow.Wide, badgeShadow.Tall);
        var badge = Stock.At(Stock.BuildBadge, ui.Screen);
        _ = StockArt.DrawIcon(ref ui, _textures, "build badge", "ico_build",
                          badge.Left, badge.Bottom, badge.Wide, badge.Tall);

        // The title (hudmenuengybuild.res:63-102). Stock's one-unit black
        // dropshadow is carried by the plate instead — a second text run
        // would be unreadable ink on the plate, the trade every plate
        // makes (TFHudFlagStatus's score labels).
        var title = Stock.At(Stock.Title, ui.Screen);
        ui.Text("#TF2_BuildMenu_Title", Text.Display, Tone.Text)
            .Width(Size.Du(title.Wide)).TextAlign(Align.Start)
            .AtRes(UiAnchor.BottomLeft, title.Left, title.Bottom).Layered().Key("title");

        // The Cancel sentence with the lastinv binding already in it
        // (hudmenuengybuild.res:104-122): the source resolves the token
        // and the binding outside the frame, so it reaches the localiser
        // as game data (ui.Data), not as copy. Stock's SpectatorKeyHints
        // face renders mixed case, so the sentence is a Body, not the
        // upper-case Label role.
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
            ui.Sub(SubKey(slot), _tile, new TFBuildTileProps(slot + 1, NameToken(slot), Icon(slot),
                                                             slot + 1, _source.Cost(building, mode),
                                                             StateOf(slot)))
                .Size(Size.Du(at.Wide), Size.Du(at.Tall))
                .AtRes(UiAnchor.BottomLeft, at.Left, at.Bottom).Layered();
            // Stock's send arm: the command goes out only from an
            // available slot (:472 refuses the built and the unaffordable
            // with a deny sound instead).
            if (_tile.Clicked && StateOf(slot) == TFBuildSlotState.Available)
                _actions?.Build(building, mode);
        }
    }

    // ---- the narrow arrangement (portrait and small windows) ----

    /// <summary>The 450-unit canvas is 1012 du wide and does not fit a
    /// narrow screen (portrait 1080x1920@1.75 is 731 du): the header keeps
    /// its row on a plate and the slots flow at the tile's own stock size
    /// — four across where the width allows, else two by two (the trade
    /// TFClassMenu's stacked arm makes).</summary>
    private void DescribeStacked(ref UiFrame ui)
    {
        // Four tiles plus their gaps and the column's side pads.
        bool oneRow = ui.DesignWidth >= 4 * TFBuildTile.Stock.WideDu
                      + 3 * (float)Space.S + 2 * (float)Space.L;

        using (ui.Column(Space.M, Align.Centre).Size(Size.Percent(100), Size.Percent(100))
                   .Pad(Space.L, Space.M))
        {
            // The badge over the title (hudmenuengybuild.res:31-102), on
            // the plate the canvas's backdrop carries on wide screens.
            using (ui.Row(Space.M, Align.Centre).Fill(Tone.Surface).Round(Radius.M).Key("title bar"))
            {
                using (ui.Box().Size(Size.Du(108f), Size.Du(108f)).Key("build badge"))
                    if (_textures.Icon("ico_build") is { } badge)
                        ui.Texture(badge.Handle, Size.Du(108f), Size.Du(108f), badge.Uv).At(UiAnchor.Centre);
                ui.Text("#TF2_BuildMenu_Title", Text.Display, Tone.Text);
            }

            // The divider (hudmenuengybuild.res:19-29), across the screen:
            // stock's 2 units on the 4 du grid token.
            ui.Rect(UiPaint.FromGame(DividerFill), Size.Fill, Size.Du((float)Space.XS))
                .Key("divider");

            // The slots in stock's order (g_kEngyBuildings, :35-84); the
            // send arm is stock's (:472).
            int perRow = oneRow ? 4 : 2;
            for (int row = 0; row < SlotCount / perRow; row++)
            {
                using (ui.Row(Space.S, Align.Centre))
                {
                    for (int col = 0; col < perRow; col++)
                    {
                        int slot = row * perRow + col;
                        var (building, mode) = Slot(slot);
                        ui.Sub(SubKey(slot), _tile, new TFBuildTileProps(slot + 1, NameToken(slot), Icon(slot),
                                                                         slot + 1, _source.Cost(building, mode),
                                                                         StateOf(slot)))
                            .Size(Size.Du(TFBuildTile.Stock.WideDu), Size.Du(TFBuildTile.Stock.TallDu));
                        if (_tile.Clicked && StateOf(slot) == TFBuildSlotState.Available)
                            _actions?.Build(building, mode);
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
    /// ItemNameLabel, e.g. sentry_active.res:7).</summary>
    private static string NameToken(int slot) => slot switch
    {
        0 => "#TF2_BuildMenu_Sentry",
        1 => "#TF2_BuildMenu_Dispenser",
        2 => "#TF2_BuildMenu_TeleEntrance",
        _ => "#TF2_BuildMenu_TeleExit",
    };

    /// <summary>The slot's building art (the per-slot .res's BuildingIcon
    /// icon key, e.g. sentry_active.res:13).</summary>
    private static string Icon(int slot) => slot switch
    {
        0 => "hud_menu_sentry_build",
        1 => "hud_menu_dispenser_build",
        2 => "hud_menu_tele_entrance_build",
        _ => "hud_menu_tele_exit_build",
    };

    /// <summary>The sub key of stock's own fieldName for the slot
    /// (hudmenuengybuild.res:124-170).</summary>
    private static string SubKey(int slot) => slot switch
    {
        0 => "active_item_1",
        1 => "active_item_2",
        2 => "active_item_3",
        _ => "active_item_4",
    };

    /// <summary>The canvas and children of hudmenuengybuild.res, resolved
    /// to du per Describe at the frame's screen.</summary>
    internal static class Stock
    {
        // HudMenuEngyBuild (hudlayout.res:994-1004) is the panel the .res
        // children sit in.
        private static readonly StockBox Canvas = new("c-225", "c-55", 450, 195);

        // MainBackground (:4-17), Divider (:19-29), the badge and shadow
        // (:31-61), the title and its dropshadow (:63-102), the Cancel
        // (:104-122), and the four slots (:124-170).
        public static readonly StockBox Background = new("0", "10", 450, 170);
        public static readonly StockBox Divider = new("8", "47", 436, 2);
        public static readonly StockBox BuildShadow = new("16", "-7", 48, 48);
        public static readonly StockBox BuildBadge = new("15", "-8", 48, 48);
        public static readonly StockBox TitleShadow = new("69", "1", 300, 38);
        public static readonly StockBox Title = new("68", "0", 300, 38);
        public static readonly StockBox Cancel = new("218", "35", 200, 13);

        public static readonly StockBox Slot1 = new("25", "47", 100, 124);
        public static readonly StockBox Slot2 = new("125", "47", 100, 124);
        public static readonly StockBox Slot3 = new("225", "47", 100, 124);
        public static readonly StockBox Slot4 = new("325", "47", 100, 124);

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

/// <summary>What one of stock's per-slot .res states shows
/// (base_active.res, base_already_built.res, base_cant_afford.res): the
/// OnTick arms of tf_hud_menu_engy_build.cpp:561-580.</summary>
public enum TFBuildSlotState : byte
{
    /// <summary>base_active.res: the filled background, the cost in
    /// TanDarker.</summary>
    Available,
    /// <summary>base_already_built.res: the outlined background, the
    /// <c>#Hud_Menu_Build_Already_Built</c> reason (tf_hud_menu_engy_build.cpp:562-565).</summary>
    AlreadyBuilt,
    /// <summary>base_cant_afford.res: the reason and the cost in RedSolid
    /// (:572-575).</summary>
    CantAfford,
}

/// <summary>What one slot shows: its 1-based slot number and name token,
/// its building art, the key chip's number, the metal price, and the
/// state the OnTick arms chose.</summary>
public readonly record struct TFBuildTileProps(int Slot, string NameToken, string Icon,
                                               int Number, int Cost, TFBuildSlotState State);

/// <summary>
/// One slot of the build menu (base_active.res and its already-built and
/// cant-afford arms): the building's name 6,0 84x15 over the 98x105
/// background at 4,14, the building art 22,33 56x56, the metal icon
/// 10,18 10x10 with the price 23,17, the reason label 10,48 80x18 when
/// stock's blocked arms name one, and the key chip (ico_key_blank 41,99
/// 18x18 with the number 0,98) at the bottom. The whole tile is the
/// click target; a miss in the library leaves the fallback drawing.
/// </summary>
public sealed class TFBuildTile(IUiTextureLibrary textures) : UiPanel<TFBuildTileProps>
{
    /// <summary>Whether the last described tile was clicked.</summary>
    public bool Clicked { get; private set; }

    // The scheme colours the .res arms name (clientscheme.res): the menu's
    // default label colour and the dimmed/red blocked tints.
    private static readonly UiColor MenuText = new(235, 226, 202, 255);      // TanLight :100
    private static readonly UiColor TanDarker = new(46, 43, 42, 255);        // :101
    private static readonly UiColor TanDark = new(117, 107, 94, 255);        // :99
    private static readonly UiColor RedSolid = new(192, 28, 0, 255);         // :49
    private static readonly UiColor OffWhite = new(251, 235, 202, 255);      // ProgressOffWhite :108
    private static readonly UiColor OffWhiteTransparent = new(251, 235, 202, 128); // :112
    private static readonly UiColor Black = new(0, 0, 0, 255);               // the chip number's

    public override void Describe(ref UiFrame ui, in TFBuildTileProps p)
    {
        var tile = ui.Box().Size(Size.Fill, Size.Fill).Key("tile");
        var hit = ui.Interact(tile);
        Clicked = hit.Clicked;
        using (tile)
        {
            // The background: filled while available, outlined when a
            // blocked arm is drawn (base_active.res:24-38,
            // base_already_built.res:24-38 — same box, other art and
            // tint). The plate carries the tile's texts when the art
            // misses — stock's item background is translucent tan either
            // way, and the name hangs above the art box (6,0 over 4,14).
            string bgName = p.State == TFBuildSlotState.Available ? "hud_menu_item_bg" : "hud_menu_item_bg_outline";
            if (textures.Icon(bgName) is null)
                ui.Rect(Tone.Surface, Size.Du(Stock.WideDu), Size.Du(Stock.TallDu))
                    .AtRes(UiAnchor.BottomLeft, 0f, 0f).Layered().Key("item bg plate");
            var bg = textures.Icon(bgName);
            if (bg is { } itemBg)
            {
                var at = Stock.At(Stock.Background);
                ui.Texture(itemBg.Handle, Size.Du(at.Wide), Size.Du(at.Tall), itemBg.Uv)
                    .AtRes(UiAnchor.BottomLeft, at.Left, at.Bottom).Layered()
                    .Tint(p.State == TFBuildSlotState.Available
                              ? UiPaint.FromGame(OffWhite)
                              : UiPaint.FromGame(OffWhiteTransparent))
                    .Key("item bg");
            }

            // The building art (BuildingIcon, base_active.res:40-54; the
            // per-slot .res names the icon key, e.g. sentry_active.res:13).
            if (textures.Icon(p.Icon) is { } building)
            {
                var at = Stock.At(Stock.Building);
                ui.Texture(building.Handle, Size.Du(at.Wide), Size.Du(at.Tall), building.Uv)
                    .AtRes(UiAnchor.BottomLeft, at.Left, at.Bottom).Layered().Key("building");
            }

            // The metal price: the icon (MetalIcon) and the number
            // (CostLabel), in the state's colour. Over the plate the
            // fallback keeps a readable ink — stock's TanDarker and red
            // are its light panel's inks, the trade the clock's fallback
            // makes (the reason carries the blocked state either way).
            var metal = Stock.At(Stock.Metal);
            var metalArt = textures.Icon("ico_metal");
            if (metalArt is { } metalIcon)
                ui.Texture(metalIcon.Handle, Size.Du(metal.Wide), Size.Du(metal.Tall), metalIcon.Uv)
                    .AtRes(UiAnchor.BottomLeft, metal.Left, metal.Bottom).Layered()
                    .Tint(UiPaint.FromGame(CostTint(p.State))).Key("metal");
            var cost = Stock.At(Stock.Cost);
            var price = ui.Number(p.Cost, Text.Label,
                                  p.State == TFBuildSlotState.CantAfford ? Tone.Warning : Tone.Text);
            if (bg is { })
                price.Color(UiPaint.FromGame(CostTint(p.State)));
            price.Width(Size.Du(cost.Wide)).TextAlign(Align.Start)
                .AtRes(UiAnchor.BottomLeft, cost.Left, cost.Bottom).Layered().Key(CostKey(p.Slot));

            // The reason a blocked arm names (CantBuildReason,
            // base_already_built.res:40-58 / base_cant_afford.res:40-57).
            if (p.State != TFBuildSlotState.Available)
            {
                var reason = Stock.At(Stock.Reason);
                var note = ui.Text(p.State == TFBuildSlotState.AlreadyBuilt
                                       ? "#TF2_BuildMenu_AlreadyBuilt"
                                       : "#TF2_BuildMenu_CantAfford",
                                   Text.Body, Tone.Text);
                if (bg is { })
                    note.Color(p.State == TFBuildSlotState.CantAfford
                                   ? UiPaint.FromGame(RedSolid)
                                   : UiPaint.FromGame(MenuText));
                note.Width(Size.Du(reason.Wide)).TextAlign(Align.Centre)
                    .AtRes(UiAnchor.BottomLeft, reason.Left, reason.Bottom).Layered().Key(ReasonKey(p.Slot));
            }

            // The name (ItemNameLabel, base_active.res:3-22).
            var name = Stock.At(Stock.Name);
            ui.Text(p.NameToken, Text.Body, Tone.Text).Color(UiPaint.FromGame(MenuText))
                .Width(Size.Du(name.Wide)).TextAlign(Align.Start)
                .AtRes(UiAnchor.BottomLeft, name.Left, name.Bottom).Layered().Key("name");

            // The key chip (NumberBg + NumberLabel,
            // base_active.res:110-145). Over the plate the fallback keeps
            // the theme ink; stock's Black rides its light key art.
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
                .AtRes(UiAnchor.BottomLeft, number.Left, number.Bottom).Layered().Key(NumberKey(p.Slot));
        }
    }

    /// <summary>The per-slot keys the facts read (the tile is Sub'd four
    /// times; a constant key would name the first one only).</summary>
    private static string CostKey(int slot) => slot switch
    {
        1 => "cost-1", 2 => "cost-2", 3 => "cost-3", _ => "cost-4",
    };

    private static string ReasonKey(int slot) => slot switch
    {
        1 => "reason-1", 2 => "reason-2", 3 => "reason-3", _ => "reason-4",
    };

    private static string NumberKey(int slot) => slot switch
    {
        1 => "number-1", 2 => "number-2", 3 => "number-3", _ => "number-4",
    };

    /// <summary>The state's ink for the cost row and the metal icon
    /// (base_active.res TanDarker; base_already_built.res TanDark;
    /// base_cant_afford.res RedSolid).</summary>
    private static UiColor CostTint(TFBuildSlotState state) => state switch
    {
        TFBuildSlotState.AlreadyBuilt => TanDark,
        TFBuildSlotState.CantAfford => RedSolid,
        _ => TanDarker,
    };

    /// <summary>The tile's own 100x124 stock space (base_active.res's
    /// boxes), resolved to du per Describe.</summary>
    internal static class Stock
    {
        private static readonly StockRect Tile = new(0f, 0f, 100f, 124f);

        /// <summary>The tile's du size: the stock 100x124 at the scale.</summary>
        public static float WideDu => Tile.Wide * StockRes.Scale;
        public static float TallDu => Tile.Tall * StockRes.Scale;

        public static readonly StockBox Name = new("6", "0", 84, 15);
        public static readonly StockBox Background = new("4", "14", 98, 105);
        public static readonly StockBox Building = new("22", "33", 56, 56);
        public static readonly StockBox Metal = new("10", "18", 10, 10);
        public static readonly StockBox Cost = new("23", "17", 84, 13);
        public static readonly StockBox Reason = new("10", "48", 80, 18);
        public static readonly StockBox NumberBg = new("41", "99", 18, 18);
        public static readonly StockBox Number = new("0", "98", 100, 18);

        /// <summary>The box's spot in du from the tile's bottom-left.</summary>
        public static StockSpot At(StockBox box) => StockRes.Spot(box.Rect(Tile), Tile);
    }
}

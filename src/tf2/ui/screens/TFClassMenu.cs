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
/// The TF2 class menu (stock's <c>CTFClassMenu</c>,
/// game/client/tf/vgui/tf_classmenu.cpp) at stock's own layout:
/// classselection.res's canvas through <see cref="StockRes"/> — the ten
/// portrait tiles 50x100 at ypos -5 hanging five units past the canvas top
/// (random c250, scout c-300, soldier c-250, pyro c-200, demoman c-110,
/// heavyweapons c-60, engineer c-10, medic c78, sniper c128, spy c178), the
/// offense/defense/support bucket labels over their rows (#TF_Offense
/// c-300, #TF_Defense c-110, #TF_Support c76, ypos 96), the
/// <c>#TF_SelectAClass</c> title at 30 r40, the Cancel at c-295 r38, and the
/// loadout gradient's shaded bar at the bottom (0 r50, f0x50). Each tile
/// asks the library for stock's art: <c>class_sel_sm_&lt;class&gt;_inactive</c>
/// at its drawcolor 180 (70.5%) at rest, the team's
/// <c>class_sel_sm_&lt;class&gt;_&lt;blu|red&gt;</c> at full alpha when the
/// tile is armed — hovered, or the class the player is on
/// (<c>SetImageSelected</c>, :567,576; the .res image_default/armed are the
/// _inactive pair). A library that misses a name leaves the tile's team
/// tone and its name in the fallback (the portraits are VPK UI images,
/// docs/plans/tf2/deferred.tsv, and the MenuBG/TFPlayerModel model panels
/// with them). A narrow screen cannot hold the 640-unit row, so the
/// buckets stack in stock's order.
///
/// <para>
/// Each tile counts how many of the team already play the class (stock's
/// <c>UpdateNumClassLabels</c> counts, :1407-1568, the local player not
/// counted, the num labels at ypos 10 over the tile's top), and marks it
/// FULL at the class limit. The arms are here: Cancel is only visible once
/// a class is chosen (:1044-1081 — a forced first pick cannot be
/// cancelled), a spectator's counts are blank (:1446-1449), and a tile
/// click is stock's <c>Go</c> (:1216-1275) — through
/// <see cref="ITFClassMenuActions"/>, which sends nothing for a full class
/// and the named <c>joinclass</c> otherwise (:1330-1339).
/// </para>
/// <para>
/// It also answers the names stock's <c>jointeam</c> raises the panel
/// under — <c>class_red</c> and <c>class_blue</c> (tf_player.cpp:6450-6455,
/// tf_shareddefs.h:162-163) — and wears the team the name names, as
/// stock's one menu took its team from the panel raised
/// (<c>UpdateNumClassLabels</c> colours and counts by that iTeam,
/// :1452, :1510) while its blank-count arm keys on the local player
/// (:1421, :1446-1449). The counts themselves come from the local
/// source, whose team is the panel's whenever the server raises a named
/// panel (the jointeam arm shows it only for the team just joined);
/// the arms that could separate them are arena, this port's deferred
/// mode.
/// </para>
/// </summary>
public sealed class TFClassMenu : UiScreen
{
    /// <summary>Stock's <c>TF_CLASS_RANDOM</c>: the number the random
    /// button records; the named command is <c>joinclass random</c>.</summary>
    internal const int RandomClass = 12;

    /// <summary>The names the panel is raised under, stock's own
    /// (tf_shareddefs.h:163,:162 beside the plain <see cref="Panel"/>).</summary>
    private static readonly IReadOnlyList<string> PanelNames = ["class", "class_red", "class_blue"];

    // The bottom band's colours (clientscheme.res): the CExButtons default
    // to the scheme's Button.BgColor TanDark face and Button.TextColor
    // TanLight label (:252,:251 — the constants :99,:100 name). The
    // Cancel's own fgcolor_override (:760) is darker than the lint's
    // large-text floor over that face, so the band wears the pair.
    private static readonly UiColor ButtonFace = new(117, 107, 94, 255);
    private static readonly UiColor ButtonInk = new(235, 226, 202, 255);

    private readonly ITFClassMenuSource _source;
    private readonly TFClassTile _tile;
    private readonly IUiTextureLibrary _textures;
    private readonly ITFClassMenuActions? _actions;
    private readonly IUiKeyBindingStore? _keys;

    /// <summary>The team the last named show named (null until one did or
    /// for the plain name): what the tiles' bands wear.</summary>
    private int? _menuTeam;

    /// <param name="textures">The art library the portraits and the shaded
    /// bar come from; every miss falls back to the tile's tone and name.</param>
    /// <param name="actions">What a choice does (the named
    /// <c>joinclass</c> and the closing clicks); none in a sample.</param>
    /// <param name="keys">The binds, for the changeclass close key; null
    /// leaves the keyless closes.</param>
    public TFClassMenu(ITFClassMenuSource source, TFClassTile tile, IUiTextureLibrary textures,
                       ITFClassMenuActions? actions = null, IUiKeyBindingStore? keys = null)
    {
        _source = source;
        _tile = tile;
        _textures = textures;
        _actions = actions;
        _keys = keys;
    }

    public override string Name => "tf2classmenu";

    public override string? Panel => "class";

    public override IReadOnlyList<string> Panels => PanelNames;

    /// <summary>A show of a claimed panel names the team the menu wears:
    /// the class panel stock's jointeam raises is class_red or class_blue
    /// (tf_shareddefs.h:163,:162) and the menu takes that team; a plain
    /// show follows the local player, as stock's did for the panel the
    /// <c>changeclass</c>-style opens raise. Every show is stock's
    /// <c>SetVisible(true)</c> telling the server <c>menuopen</c>
    /// (tf_classmenu.cpp:1180-1182), repeats and all.</summary>
    public override void PanelOpened(string panel)
    {
        _menuTeam = panel switch
        {
            "class_red" => 2,     // TF_TEAM_RED (tf_shareddefs.h:163 names the panel)
            "class_blue" => 3,    // TF_TEAM_BLUE (:162)
            _ => null,
        };
        _actions?.Opened();
    }

    /// <summary>The host closed the menu, whichever way — Escape, a named
    /// close, the window's own button: stock's <c>SetVisible(false)</c>
    /// telling the server <c>menuclosed</c> (tf_classmenu.cpp:1195-1199).</summary>
    public override void Closed() => _actions?.Closed();

    /// <summary>The raw TF_CLASS number of the class last clicked
    /// (Random's number for the random button), null until one is: what
    /// the command sender reads, as the core menu records
    /// <c>Chosen</c>.</summary>
    public int? Chosen { get; private set; }

    private void Choose(int number)
    {
        Chosen = number;
        if (number == RandomClass)
            _actions?.ChooseRandom();
        else
            _actions?.Choose(number);
    }

    /// <summary>Whether this frame pressed a close key: stock's arm at
    /// tf_classmenu.cpp:896-908 — KEY_0, KEY_PAD_0 or the
    /// <c>changeclass</c> bind — held for a player whose class stands
    /// (:903-908, <c>GetClassIndex() != TF_CLASS_UNDEFINED</c>). The bind
    /// is matched by name, the way the store speaks: the pressed codes
    /// against the key the command holds.</summary>
    private bool CloseKeyed(UiFrame ui)
    {
        if (!_source.HasClass)
            return false;
        if (ui.ConsumeKey((int)UiKey.Key0))
            return true;
        if (ui.ConsumeKey((int)UiKey.KeyPad0))
            return true;
        string? bound = _keys?.KeyFor("changeclass");
        if (bound is null)
            return false;
        foreach (int code in ui.ButtonsPressed)
            if (_keys!.KeyName(code) == bound)
                return true;
        return false;
    }

    public override void Describe(ref UiFrame ui)
    {
        // The close keys, before anything draws: stock's OnKeyCodePressed arm
        // (tf_classmenu.cpp:896-908) — the 0 key and the pad's, or the
        // changeclass bind, closing only for a player whose class stands
        // (the GetClassIndex gate, :903-908). The ask is the Cancel's: the
        // hide is the host's close, which tells the server after it.
        if (CloseKeyed(ui))
            _actions?.Cancel();

        // The team the menu wears: the named show's, or the local player's
        // (stock's one menu took its team from the panel raised).
        int menuTeam = _menuTeam ?? _source.Team;
        // Stock's spectator arm shows no counts (:1421, :1446-1449): keyed
        // on the local player, not on the panel's team.
        bool showCounts = _source.Team > 1;

        // MenuBG (CModelPanel, models/vgui/UI_class01.mdl, :827-855) is a
        // deferred 3D model; the window surface is the backdrop under the
        // layout until the model panels are ported (docs/plans/tf2/
        // deferred.tsv).
        ui.Rect(Tone.Window, Size.Percent(100), Size.Percent(100))
            .AtRes(UiAnchor.BottomLeft, 0, 0).Layered().Key("backdrop");

        // Stock's dark band across the screen top behind the tile row and
        // the bucket labels: the canvas's top ~110 units — its bottom lands
        // just under the labels (ypos 96 tall 12 = the classselection.res
        // rows, :103 and :96+12).
        ui.Rect(Tone.Surface, Size.Percent(100), Size.Du(110f * StockRes.Scale))
            .AtRes(UiAnchor.TopLeft, 0f, 0f).Layered().Key("top band");

        // The bar under the title and Cancel (stock's zpos: ShadedBar 4,
        // ClassMenuSelect 5, CancelButton 6).
        ShadedBar(ref ui);

        if (ui.Narrow)
            DescribeStacked(ref ui, menuTeam, showCounts);
        else
            DescribeStock(ref ui, menuTeam, showCounts);
    }

    // ---- the stock canvas (wide screens) ----

    /// <summary>The .res boxes on the 640x480 canvas classselection.res is
    /// written in (:3-16, 0,0 f0x480), resolved to du per Describe. A "c"
    /// xpos is the element's LEFT edge about the screen's centre, an "r"
    /// ypos its TOP edge above the bottom.</summary>
    internal static class Stock
    {
        public static readonly StockRect Canvas = StockRect.Screen;

        /// <summary>The gap stock keeps between the canvas top and the tile
        /// row (its frames read y 45-143 on a 720-high screen: about 30
        /// canvas units below the top).</summary>
        public const float TileTopInset = 30f;

        // The ten tiles: 50x100 at ypos -5 — each hangs 5 units past the
        // canvas top, and the visible band is what draws (random :36-45,
        // scout :122-145, soldier :186-209, pyro :250-273, demoman
        // :340-363, heavyweapons :402-425, engineer :464-487, medic
        // :552-575, sniper :615-638, spy :678-701).
        public static readonly StockBox Random = new("c250", "-5", 50, 100);
        public static readonly StockBox Scout = new("c-300", "-5", 50, 100);
        public static readonly StockBox Soldier = new("c-250", "-5", 50, 100);
        public static readonly StockBox Pyro = new("c-200", "-5", 50, 100);
        public static readonly StockBox Demoman = new("c-110", "-5", 50, 100);
        public static readonly StockBox Heavy = new("c-60", "-5", 50, 100);
        public static readonly StockBox Engineer = new("c-10", "-5", 50, 100);
        public static readonly StockBox Medic = new("c78", "-5", 50, 100);
        public static readonly StockBox Sniper = new("c128", "-5", 50, 100);
        public static readonly StockBox Spy = new("c178", "-5", 50, 100);

        // The bucket labels: 90x12 at ypos 96, over their rows' tops
        // (Offense :96-121, Defense :314-339, Support :526-551).
        public static readonly StockBox Offense = new("c-300", "96", 90, 12);
        public static readonly StockBox Defense = new("c-110", "96", 90, 12);
        public static readonly StockBox Support = new("c76", "96", 90, 12);

        // ClassMenuSelect: 30 r40 450x30 west (:803-825); CancelButton:
        // c-295 r38 100x25 (:742-761); EditLoadoutButton: r180 r38 150x25
        // (:763-781); ResetButton: r470 r38 110x25 (:783-801); ShadedBar:
        // 0 r50 f0x50 (:885-906).
        public static readonly StockBox Select = new("30", "r40", 450, 30);
        public static readonly StockBox Cancel = new("c-295", "r38", 100, 25);
        public static readonly StockBox EditLoadout = new("r180", "r38", 150, 25);
        public static readonly StockBox Reset = new("r470", "r38", 110, 25);
        public static readonly StockBox Shaded = new("0", "r50", 640, 50);

        /// <summary>The tile whose TF_CLASS number a box is.</summary>
        public static StockBox Tile(int number) => number switch
        {
            1 => Scout,
            2 => Sniper,
            3 => Soldier,
            4 => Demoman,
            5 => Medic,
            6 => Heavy,
            7 => Pyro,
            8 => Spy,
            9 => Engineer,
            _ => Random,
        };
    }

    private void DescribeStock(ref UiFrame ui, int menuTeam, bool showCounts)
    {
        // The canvas is the frame's own aspect (StockRes.Screen): vgui
        // computes every .res position against the real screen, so an "r"
        // xpos and an f0 wide stretch on a widescreen where the 640x480
        // authoring box would pinch them.
        StockRect screen = StockRes.Screen(ui.Screen);

        // The visible band of a stock box: the tiles hang 5 units past the
        // canvas top (ypos -5, 100 tall), and the screen clips what hangs
        // past it. Stock keeps the row clear of the screen edge: its frames
        // sit about 30 units below the canvas top (y 45-143 on a 720-high
        // screen), inside the dark band — the visible band hangs from
        // there, keeping its bottom where the labels start right under it.
        static StockSpot Visible(StockBox box, StockRect canvas)
        {
            var r = box.Rect(canvas);
            float over = r.Bottom + r.Tall - (canvas.Bottom + canvas.Tall);
            if (over > 0f)
                r = new StockRect(r.Left, r.Bottom, r.Wide, r.Tall - over);
            float inset = MathF.Min(Stock.TileTopInset, r.Tall);
            return StockRes.Spot(new StockRect(r.Left, r.Bottom, r.Wide, r.Tall - inset), canvas);
        }

        // The title (ClassMenuSelect, :803-825). Stock's
        // SetCancelButtonVisible shows it exactly while the Cancel is not
        // (:1035-1038): the first pick asks, a chosen class answers.
        if (!_source.HasClass)
        {
            var select = Stock.Select.In(screen);
            ui.Text("#TF2_ClassMenu_Select", Text.Title, Tone.Text)
                .Width(Size.Du(select.Wide)).TextAlign(Align.Start)
                .AtRes(UiAnchor.BottomLeft, select.Left, select.Bottom).Layered().Key("select");
        }

        // The ten tiles in TF_CLASS order (stock's button array, :484-493),
        // the art's hung 5% cropped by the same clip the band is.
        for (int n = 1; n <= RandomClass; n++)
        {
            if (n is 10 or 11)
                continue;
            var s = Visible(Stock.Tile(n), screen);
            ui.Sub(SubOf(n), _tile, Props(n, menuTeam, showCounts, ClassUv))
                .Size(Size.Du(s.Wide), Size.Du(s.Tall))
                .AtRes(UiAnchor.Bottom, s.CentreX, s.Bottom).Layered();
            if (_tile.Clicked)
                Choose(n);
        }

        // The groups' frames, under the tiles: offense, defense, support
        // (the screen order classselection.res's rows run in).
        GroupFrame(ref ui, Visible(Stock.Scout, screen), Visible(Stock.Soldier, screen),
            Visible(Stock.Pyro, screen), "offense frame");
        GroupFrame(ref ui, Visible(Stock.Demoman, screen), Visible(Stock.Heavy, screen),
            Visible(Stock.Engineer, screen), "defense frame");
        GroupFrame(ref ui, Visible(Stock.Medic, screen), Visible(Stock.Sniper, screen),
            Visible(Stock.Spy, screen), "support frame");

        Bucket(ref ui, "#TF2_ClassMenu_Offense", Stock.Offense, "offense");
        Bucket(ref ui, "#TF2_ClassMenu_Defense", Stock.Defense, "defense");
        Bucket(ref ui, "#TF2_ClassMenu_Support", Stock.Support, "support");

        // Stock shows Cancel only with a class already chosen (:1044-1081).
        if (_source.HasClass && AskButton(ref ui, Stock.Cancel, "#SS_UI_Cancel", "cancel"))
            _actions?.Cancel();

        // Edit Loadout is stock's openloadout (tf_classmenu.cpp:1307-1320)
        // and shows always; Reset (its resetclass arm, :1300-1306 — ours is
        // docs/divergences.md's) stock's OnTick hides until a class is
        // chosen (:1138-1142).
        if (AskButton(ref ui, Stock.EditLoadout, "#EditLoadout", "edit loadout"))
            _actions?.EditLoadout();
        if (_source.HasClass && AskButton(ref ui, Stock.Reset, "#TF_ClassMenu_Reset", "reset"))
            _actions?.ResetLoadout();
    }

    /// <summary>One of the bottom band's buttons: the .res box's spot on the
    /// frame's own canvas, stock's TanDark CExButton face and TanLight
    /// label; true when the click asked.</summary>
    private static bool AskButton(ref UiFrame ui, StockBox box, string token, string key)
    {
        var spot = box.In(StockRes.Screen(ui.Screen));
        var b = ui.Box().Size(Size.Du(spot.Wide), Size.Du(spot.Tall))
                       .Fill(UiPaint.FromGame(ButtonFace)).Round(Radius.S)
                       .AtRes(UiAnchor.Bottom, spot.CentreX, spot.Bottom).Layered().Key(key);
        var hit = ui.Interact(b);
        using (b)
            // HudFontSmallBold's em, the main menu's label (TFMainMenu.LabelEm):
            // 29.4 du at 1080, over the lint's large-text 24.
            ui.Text(token, Text.Heading).Color(UiPaint.FromGame(ButtonInk)).TextSize(TFMainMenu.LabelEm(StockRes.Scale))
                .At(UiAnchor.Centre).TextAlign(Align.Centre).Layered();
        return hit.Clicked;
    }

    /// <summary>Stock's thin rounded frame around one class group: the
    /// outline hugging the three portraits' union — the frame the stock
    /// screen shows; no .res element draws it (the nearest scheme border is
    /// TFThinLineBorder's scalable outline, clientscheme.res:3345), so it
    /// is drawn in the TanDark the tiles' own ink names.</summary>
    private static void GroupFrame(ref UiFrame ui, StockSpot a, StockSpot b, StockSpot c, string key)
    {
        // Absolute edges from the spots: x about the parent's centre
        // (CentreX ± Wide/2), y up from its bottom (Bottom .. Bottom+Tall).
        float left = MathF.Min(a.CentreX - a.Wide / 2f, MathF.Min(b.CentreX - b.Wide / 2f, c.CentreX - c.Wide / 2f));
        float right = MathF.Max(a.CentreX + a.Wide / 2f, MathF.Max(b.CentreX + b.Wide / 2f, c.CentreX + c.Wide / 2f));
        float bottom = MathF.Min(a.Bottom, MathF.Min(b.Bottom, c.Bottom));
        float top = MathF.Max(a.Bottom + a.Tall, MathF.Max(b.Bottom + b.Tall, c.Bottom + c.Tall));
        using (ui.Box().Size(Size.Du(right - left), Size.Du(top - bottom))
                   .Border(UiPaint.FromGame(ButtonFace)).Round(Radius.S)
                   .AtRes(UiAnchor.Bottom, (left + right) / 2f, bottom).Layered().Key(key))
        {
        }
    }

    private static void Bucket(ref UiFrame ui, string token, StockBox box, string key)
    {
        var s = box.In(StockRes.Screen(ui.Screen));
        ui.Text(token, Text.Label, Tone.TextMuted)
            .Width(Size.Du(s.Wide)).TextAlign(Align.Start)
            .AtRes(UiAnchor.Bottom, s.CentreX, s.Bottom).Layered().Key(key);
    }

    private void ShadedBar(ref UiFrame ui)
    {
        int shade = _textures.Find("vgui/loadout_bottom_gradient");
        if (shade != 0)
            ui.Texture(shade, Size.Percent(100), Size.Du(Stock.Shaded.Rect(Stock.Canvas).Tall * StockRes.Scale))
                .AtRes(UiAnchor.BottomLeft, 0, 0).Layered().Key("shaded bar");
    }

    // ---- the stacked canvas (narrow screens) ----

    private void DescribeStacked(ref UiFrame ui, int menuTeam, bool showCounts)
    {
        // The 640-unit row cannot fit: the buckets stack in stock's order,
        // the tiles scaled to what is left under the title bar and the
        // shaded bar (the tall stock keeps at the bottom).
        var stock = Stock.Random.Rect(Stock.Canvas);
        float tileH = MathF.Min(stock.Tall * StockRes.Scale, (ui.DesignHeight - 240f) / 4f);
        float tileW = MathF.Min(stock.Wide * StockRes.Scale,
                                MathF.Min((ui.DesignWidth - 64f) / 3f, tileH / 2f));
        tileH = tileW * 2f;

        using (ui.Column(Space.S, Align.Centre).Size(Size.Percent(100), Size.Percent(100))
                   .Pad(Space.L, Space.S))
        {
            StackBucket(ref ui, "#TF2_ClassMenu_Offense", menuTeam, showCounts, 1, 3, 7, tileW, tileH);
            StackBucket(ref ui, "#TF2_ClassMenu_Defense", menuTeam, showCounts, 4, 6, 9, tileW, tileH);
            StackBucket(ref ui, "#TF2_ClassMenu_Support", menuTeam, showCounts, 5, 2, 8, tileW, tileH);

            // Stock's random button sits right of the spy; stacked it takes
            // the row under the support bucket.
            ui.Sub(SubOf(RandomClass), _tile, Props(RandomClass, menuTeam, showCounts, default))
                .Width(Size.Du(tileW)).Height(Size.Du(tileH));
            if (_tile.Clicked)
                Choose(RandomClass);

            ui.Flex();

            using (ui.Row(Space.M, Align.Centre).Width(Size.Fill).Height(Size.Du(48f)))
            {
                // Stock shows Cancel only with a class already chosen; Edit
                // Loadout and Reset stand with it (their stock-canvas spots
                // cannot hold on the stacked row).
                if (_source.HasClass && ui.Button("cancel", "#SS_UI_Cancel").Clicked)
                    _actions?.Cancel();
                if (ui.Button("edit loadout", "#EditLoadout").Clicked)
                    _actions?.EditLoadout();
                if (_source.HasClass && ui.Button("reset", "#TF_ClassMenu_Reset").Clicked)
                    _actions?.ResetLoadout();
                ui.Flex();
                if (!_source.HasClass)      // stock's title yields to the Cancel (:1035-1038)
                    ui.Text("#TF2_ClassMenu_Select", Text.Title, Tone.Text);
            }
        }
    }

    private void StackBucket(ref UiFrame ui, string token, int menuTeam, bool showCounts,
                             int left, int mid, int right, float tileW, float tileH)
    {
        ui.Text(token, Text.Label, Tone.TextMuted);
        using (ui.Row(Space.S, Align.Centre).Width(Size.Fill))
        {
            ui.Flex();
            Tile(ref ui, left, menuTeam, showCounts, tileW, tileH);
            Tile(ref ui, mid, menuTeam, showCounts, tileW, tileH);
            Tile(ref ui, right, menuTeam, showCounts, tileW, tileH);
            ui.Flex();
        }
    }

    private void Tile(ref UiFrame ui, int number, int menuTeam, bool showCounts, float tileW, float tileH)
    {
        ui.Sub(SubOf(number), _tile, Props(number, menuTeam, showCounts, default))
            .Width(Size.Du(tileW)).Height(Size.Du(tileH));
        if (_tile.Clicked)
            Choose(number);
    }

    // ---- shared ----

    /// <summary>The art's hung and inset top: the tiles' visible band maps
    /// to the image below its top (the hung 5 units and the row's ~30-unit
    /// inset below the canvas top — the part the screen clips).</summary>
    private static readonly UiRect ClassUv = new(0f, 0.35f, 1f, 0.65f);

    private TFClassTileProps Props(int number, int menuTeam, bool showCounts, UiRect uv)
    {
        if (number == RandomClass)
            return new TFClassTileProps(number, "#TF2_ClassMenu_Random", menuTeam, 0, false, false, false, uv);
        var c = _source.Choice(number);
        return new TFClassTileProps(number, c.Name, menuTeam, c.Players, showCounts, c.Current, c.LimitHit, uv);
    }

    /// <summary>The Sub key stock's own fieldName for the tile
    /// (classselection.res: the ten CExImageButtons and the random).</summary>
    private static string SubOf(int number) => number switch
    {
        1 => "scout",
        2 => "sniper",
        3 => "soldier",
        4 => "demoman",
        5 => "medic",
        6 => "heavyweapons",
        7 => "pyro",
        8 => "spy",
        9 => "engineer",
        _ => "random",
    };
}

/// <summary>What one class tile shows: its raw TF_CLASS number and
/// display name, the local team whose colour the band wears, the count
/// (shown only off a spectator's blank arm), the two states stock
/// labels for — the class the player is on, and a class at its limit —
/// and the art window (stock's clipped band crops the hung 5%).</summary>
public readonly record struct TFClassTileProps(int Number, string Name, int Team, int Players,
                                               bool ShowCount, bool Current, bool LimitHit, UiRect Uv = default);

/// <summary>
/// One class on the TF2 class menu: stock's portrait tile
/// (classselection.res's CExImageButtons) — the _inactive art at its
/// drawcolor 180 (70.5%) at rest and the team's art at full alpha armed
/// (hovered, or the class the player is on; tf_classmenu.cpp:567,576),
/// the number of teammates already playing it over the tile's top (stock's
/// num labels, tf_classmenu.cpp:1407-1568), and FULL when it is at its
/// limit — stock's button stays clickable there; its <c>Go</c> is what
/// sends nothing (:1216-1275). The whole tile is the click target; with no
/// art in the library the team tone and the name stand in.
/// </summary>
public sealed class TFClassTile(IUiTextureLibrary textures) : UiPanel<TFClassTileProps>
{
    /// <summary>Whether the last described tile was clicked.</summary>
    public bool Clicked { get; private set; }

    public override void Describe(ref UiFrame ui, in TFClassTileProps p)
    {
        var tone = TFHudTeams.ToneOf(p.Team);
        var tile = ui.Box().Size(Size.Fill, Size.Fill).Key("tile");
        var hit = ui.Interact(tile);
        Clicked = hit.Clicked;
        // Armed: the hovered tile, and stock's selected color — the class
        // the player is on — at full alpha; a full class arms on nothing.
        float armed = ui.Animate(tile, (hit.Hovered && !p.LimitHit) || p.Current ? 1f : 0f, Motion.Quick);
        int rest = textures.Find(Rest(p.Number));
        int team = p.Number == TFClassMenu.RandomClass
            ? 0
            : textures.Find(Armed(p.Number, p.Team));
        // The portraits are transparent-backed: the team tone stands in for
        // them only when the library misses the art (the fallback tile).
        if (rest == 0)
            tile.Fill(tone);
        using (tile)
        {
            var uv = p.Uv == default ? new UiRect(0f, 0f, 1f, 1f) : p.Uv;
            if (rest != 0)
                ui.Texture(rest, Size.Fill, Size.Fill, uv).Layered().Opacity(0.705f).Key("art rest");
            if (team != 0)
                ui.Texture(team, Size.Fill, Size.Fill, uv).Layered().Opacity(armed).Key("art armed");
            if (rest == 0 && team == 0)
            {
                if (p.Number == TFClassMenu.RandomClass)
                    ui.Text("#TF2_ClassMenu_Random", Text.Body, Tone.OnAccent)
                        .At(UiAnchor.Centre).TextAlign(Align.Centre).Layered();
                else
                    ui.Data(p.Name, Text.Body, Tone.OnAccent)
                        .At(UiAnchor.Centre).TextAlign(Align.Centre).Layered();
            }
            if (p.ShowCount && (p.Players > 0 || p.LimitHit))
            {
                // The count is small text over the tile's top (the num
                // labels, classselection.res ypos 10) — stock blanks it at
                // zero (:1489) and relabels a full class with the
                // TF_ClassLimitHit string (:1499-1560); ours says FULL
                // below. The Surface chip hugs the count (stock's label
                // shadow) — the art under it is too busy for bare text.
                ui.Number(p.Players, Text.Body, Tone.Text)
                    .Fill(Tone.Surface).Name(CountName(p.Number)).At(UiAnchor.Top)
                    .Inset(Space.None, Space.M)
                    .Layered();
            }
            // The tile's hotkey (the buttons' labelText, south-west
            // aligned, classselection.res :140, :204, ...): bottom left in
            // the art, on its own chip. The label counts the screen
            // positions 1..9, not the class numbers — scout 1, soldier 2,
            // pyro 3, demoman 4, heavyweapons 5, engineer 6, medic 7,
            // sniper 8, spy 9 — and the random tile's is empty (:51).
            int hotkey = Hotkey(p.Number);
            if (hotkey > 0)
                ui.Number(hotkey, Text.Body, Tone.Text)
                    .Fill(Tone.Surface).Name("hotkey").At(UiAnchor.BottomLeft).Inset(Space.S, Space.S).Layered();
            if (p.LimitHit || p.Current)
            {
                using (ui.Column(Space.XS).At(UiAnchor.Bottom).Inset(Space.None, Space.S)
                           .Width(Size.Fill).Layered())
                {
                    if (p.LimitHit)
                        ui.Text("#TF2_ClassMenu_Full", Text.Label, Tone.Warning)
                            .Fill(Tone.Surface).TextAlign(Align.Centre);
                    if (p.Current)
                        ui.Text("#TF2_ClassMenu_Current", Text.Label, Tone.Text)
                            .Fill(Tone.Surface).TextAlign(Align.Centre);
                }
            }
        }
    }

    /// <summary>The tile's hotkey label: classselection.res's labelText per
    /// button — the screen position 1..9, not the class number (scout :140,
    /// soldier :204, pyro :268, demoman :357, heavyweapons :419,
    /// engineer :480, medic :570, sniper :633, spy :696); 0 is the random's
    /// empty label (:51).</summary>
    internal static int Hotkey(int number) => number switch
    {
        1 => 1, 3 => 2, 7 => 3, 4 => 4, 6 => 5, 9 => 6, 5 => 7, 2 => 8, 8 => 9,
        _ => 0,
    };

    /// <summary>The at-rest portrait (classselection.res's image_default,
    /// g_aRawPlayerClassNamesShort spelling the class).</summary>
    private static string Rest(int number) => number switch
    {
        1 => "vgui/class_sel_sm_scout_inactive",
        2 => "vgui/class_sel_sm_sniper_inactive",
        3 => "vgui/class_sel_sm_soldier_inactive",
        4 => "vgui/class_sel_sm_demo_inactive",
        5 => "vgui/class_sel_sm_medic_inactive",
        6 => "vgui/class_sel_sm_heavy_inactive",
        7 => "vgui/class_sel_sm_pyro_inactive",
        8 => "vgui/class_sel_sm_spy_inactive",
        9 => "vgui/class_sel_sm_engineer_inactive",
        _ => "vgui/class_sel_sm_random_inactive",
    };

    /// <summary>The armed portrait (stock's <c>SetImageSelected</c>,
    /// :567,576: the class's team art — the .res's image_armedcolor 255
    /// 255 255 255).</summary>
    private static string Armed(int number, int team) => number switch
    {
        1 => team == 3 ? "vgui/class_sel_sm_scout_blu" : "vgui/class_sel_sm_scout_red",
        2 => team == 3 ? "vgui/class_sel_sm_sniper_blu" : "vgui/class_sel_sm_sniper_red",
        3 => team == 3 ? "vgui/class_sel_sm_soldier_blu" : "vgui/class_sel_sm_soldier_red",
        4 => team == 3 ? "vgui/class_sel_sm_demo_blu" : "vgui/class_sel_sm_demo_red",
        5 => team == 3 ? "vgui/class_sel_sm_medic_blu" : "vgui/class_sel_sm_medic_red",
        6 => team == 3 ? "vgui/class_sel_sm_heavy_blu" : "vgui/class_sel_sm_heavy_red",
        7 => team == 3 ? "vgui/class_sel_sm_pyro_blu" : "vgui/class_sel_sm_pyro_red",
        8 => team == 3 ? "vgui/class_sel_sm_spy_blu" : "vgui/class_sel_sm_spy_red",
        _ => team == 3 ? "vgui/class_sel_sm_engineer_blu" : "vgui/class_sel_sm_engineer_red",
    };

    private static string CountName(int number) => number switch
    {
        1 => "count-1",
        2 => "count-2",
        3 => "count-3",
        4 => "count-4",
        5 => "count-5",
        6 => "count-6",
        7 => "count-7",
        8 => "count-8",
        9 => "count-9",
        _ => "count",
    };
}

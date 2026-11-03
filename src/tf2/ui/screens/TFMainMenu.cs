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
/// The TF2 main menu: stock's <c>CHudMainMenuOverride</c>
/// (game/client/tf/tf_hud_mainmenuoverride.cpp) at mainmenuoverride.res's
/// layout, with the matchmaking dashboard's top bar over it
/// (MatchMakingDashboard.res, the SDK's own copy, and
/// <c>CTFMatchmakingDashboard</c>). It claims stock's viewport name for the
/// panel the client hands the GameUI, <c>MainMenuOverride</c>
/// (tf_shareddefs.h:176), so the GameUI's activation, Escape and Resume
/// drive it in place of the core's menu.
///
/// <para>
/// At the menu: the holiday background and the character art (both hidden
/// in a level, <c>OnUpdateMenu</c> :992-1005 and :1087-1090), the TF2 logo
/// (TFLogoImage c-290 32 256x64). The top bar: HOST A GAME
/// (rs1-145, 115 wide), FIND A GAME left of Quit (rs1, 30 wide) as
/// <c>UpdateDisconnectAndResume</c> places it (:1280-1293); in a level
/// Disconnect (115 wide) replaces Quit and Resume (95) sits left of FIND A
/// GAME. Stock's footer row (ITEMS, the two gears and, in a level,
/// GameMenu.res's four in-game buttons) is on the taskbar instead
/// (<c>TFTaskbarLaunchers</c>), which stands where the footer stood.
/// Quit and Disconnect ask first, as the dashboard's <c>quit</c> arm does
/// (<c>ShowConfirmDialog</c> :389-399, <c>PromptOrFireCommand</c>).
/// </para>
///
/// <para>
/// Not drawn, as their sources are not here: the dashboard's party slots
/// and chat toggle (the Steam party), the friends list, the MOTD and
/// notification panels and the new-user forum note (Steam community pages),
/// the rank panel and the event promo (the GC) — docs/plans/tf2/deferred.tsv.
/// </para>
/// </summary>
public sealed class TFMainMenu(IMainMenuSource menu, ITFMainMenuArt art, ITFMainMenuActions actions,
                               IUiTextureLibrary textures) : UiScreen
{
    public override string Name => "tf2mainmenu";

    /// <summary>Stock TF2's viewport name for the panel (PANEL_MAINMENUOVERRIDE).</summary>
    public override string? Panel => "MainMenuOverride";

    /// <summary>Above the other menu screens, as the core's menu (the GameUI's
    /// root covers the viewport's panels while it is up).</summary>
    public override int Order => 50;

    private enum Confirm
    {
        None,
        Quit,
        Leave,
    }

    private Confirm _confirm;

    // The scheme colours the .res files name (TF2's clientscheme.res).
    private static readonly UiColor TanLight = new(235, 226, 202, 255);
    private static readonly UiColor GreenSolid = new(76, 107, 34, 255);
    private static readonly UiColor CreditsGreen = new(94, 150, 49, 255);
    private static readonly UiColor DarkBrown = new(41, 37, 38, 255);
    private static readonly UiColor LighterDarkBrown = new(59, 54, 48, 255);
    private static readonly UiColor QuitRed = new(192, 28, 0, 50);           // the Quit and Disconnect plates
    private static readonly UiColor QuitRedArmed = new(192, 28, 0, 150);
    private static readonly UiColor BarBrown = new(41, 37, 38, 245);         // ReplayDefaultBorder's fill

    // HudFontSmallBold, TF2 Build 14 tall.
    private const float TextTall = 14f;

    // TF2 Build's em over its win ascent + descent (2048 / (1790 + 368)).
    private const float WinEm = 2048f / 2158f;

    /// <summary>
    /// A label's em in du at <paramref name="unit"/> du a stock unit: vgui
    /// rounds the proportional tall down to whole pixels and fills it with
    /// the face's win ascent and descent, so TF2 Build's em is that share of
    /// it (29.4 du at 1080, where FIND A GAME measures 194 px in stock's
    /// screenshot).
    /// </summary>
    public static float LabelEm(float unit) => MathF.Floor(TextTall * unit) * WinEm;

    public override void Describe(ref UiFrame ui)
    {
        var screen = Canvas.Of(ref ui);
        bool inLevel = menu.InLevel;

        if (!inLevel)
        {
            Backdrop(ref ui, screen);
            Character(ref ui, screen);
        }

        Logo(ref ui, screen);
        TopBar(ref ui, screen, inLevel);

        if (_confirm != Confirm.None)
            ConfirmDialog(ref ui);
    }

    // ---- the backdrop (at the menu only) ----

    /// <summary>The Background panel (zpos -200): the holiday's image, wide
    /// screens full width at 0.628 its width tall from the top (if_wider),
    /// taller ones full height at 1.6 its height wide, centred (if_taller);
    /// the screen crops what overhangs.</summary>
    private void Backdrop(ref UiFrame ui, Canvas screen)
    {
        if (art.Background is not { } name || textures.Find(name) is not (> 0 and var handle))
            return;
        bool wider = screen.Wide / screen.Tall >= 1.6f;
        float w = wider ? screen.Wide : screen.Tall * 1.6f;
        float h = wider ? w * 0.628f : screen.Tall;
        Cropped(ref ui, screen, "background", handle, (screen.Wide - w) / 2f, 0f, w, h);
    }

    /// <summary>TFCharacterImage (zpos -99): c-250, ypos -80, 600x600.</summary>
    private void Character(ref UiFrame ui, Canvas screen)
    {
        if (art.Character is not { } name || textures.Find(name) is not (> 0 and var handle))
            return;
        Cropped(ref ui, screen, "character", handle, Centre(screen, -250f), -80f, 600f, 600f);
    }

    /// <summary>TFLogoImage: c-290 32 256x64, ../logo/new_tf2_logo.</summary>
    private void Logo(ref UiFrame ui, Canvas screen)
    {
        int logo = textures.Find("logo/new_tf2_logo");
        if (logo == 0)
            return;
        var at = At(screen, Centre(screen, -290f) + screen.LeftShift, 32f, 256f, 64f);
        ui.Texture(logo, Size.Du(at.Wide), Size.Du(at.Tall))
            .AtRes(UiAnchor.BottomLeft, at.Left, at.Bottom).Layered().Key("logo");
    }

    // ---- the top bar (MatchMakingDashboard.res) ----

    private void TopBar(ref UiFrame ui, Canvas screen, bool inLevel)
    {
        // The bar's plate: BGPanel, f5 tall from the dashboard's ypos -5, so
        // 25 units of it on screen; the buttons are f6, 24 on screen.
        var bar = At(screen, 0f, 0f, screen.Wide, 25f);
        ui.Rect(UiPaint.FromGame(BarBrown), Size.Du(bar.Wide), Size.Du(bar.Tall))
            .AtRes(UiAnchor.BottomLeft, bar.Left, bar.Bottom).Layered().Key("top bar");

        const float tall = 24f;
        // HostGameButton keeps its .res place (rs1-145), FIND A GAME draws over
        // its last units (the dashboard places FIND A GAME after).
        if (Button(ref ui, screen, "host", screen.Wide - 145f - 115f, 0f, 115f, tall, "#TF2_MainMenu_HostAGame",
                   null, 0f, 15f, DarkBrown, LighterDarkBrown).Clicked)
            actions.HostAGame();

        float edge;
        if (inLevel)
        {
            edge = screen.Wide - 115f;
            if (Button(ref ui, screen, "disconnect", edge, 0f, 115f, tall, "#TF2_MainMenu_Disconnect",
                       "vgui/glyph_disconnect", 6f, 26f, QuitRed, QuitRedArmed).Clicked)
                PromptLeave();
        }
        else
        {
            edge = screen.Wide - 30f;
            if (Button(ref ui, screen, "quit", edge, 0f, 30f, tall, null, "vgui/glyph_quit", 8f, 0f,
                       QuitRed, QuitRedArmed).Clicked)
                PromptQuit();
        }

        float find = edge - 120f - 1f;
        if (Button(ref ui, screen, "find", find, 0f, 120f, tall, "#TF2_MainMenu_FindAGame", "vgui/glyph_multiplayer",
                   6f, 26f, GreenSolid, CreditsGreen).Clicked)
            actions.FindAGame();

        if (inLevel && Button(ref ui, screen, "resume", find - 95f - 1f, 0f, 95f, tall, "#TF2_MainMenu_Resume",
                              "vgui/icon_resume", 6f, 26f, DarkBrown, LighterDarkBrown).Clicked)
            menu.Resume();
    }

    // ---- the confirmations ----

    /// <summary>The dashboard's quit prompt (#MMenu_PromptQuit_Title,
    /// #MMenu_PromptQuit_Body, Yes/No) and the in-level leave prompt
    /// (BuildDisconnectConfirmDialog's safe arm: #TF_MM_Disconnect_Title,
    /// #TF_MM_Disconnect, Leave/Stay).</summary>
    private void ConfirmDialog(ref UiFrame ui)
    {
        bool quit = _confirm == Confirm.Quit;
        using (ui.Dialog("confirm", quit ? "#TF2_MainMenu_QuitTitle" : "#TF2_MainMenu_LeaveTitle", 480))
        {
            ui.Text(quit ? "#TF2_MainMenu_QuitBody" : "#TF2_MainMenu_LeaveBody", Text.Body).Wrap(432);
            using (ui.DialogButtons())
            {
                if (ui.Button("no", quit ? "#TF2_MainMenu_No" : "#TF2_MainMenu_Stay").Clicked)
                    _confirm = Confirm.None;
                if (ui.Button("yes", quit ? "#TF2_MainMenu_Yes" : "#TF2_MainMenu_Leave", primary: true).Clicked)
                {
                    _confirm = Confirm.None;
                    if (quit)
                        menu.Quit();
                    else
                        actions.Disconnect();
                }
            }
        }
    }

    /// <summary>Quit's ask (the dashboard's quit arm at the menu).</summary>
    internal void PromptQuit() => _confirm = Confirm.Quit;

    /// <summary>Disconnect's ask (PromptOrFireCommand in a level).</summary>
    internal void PromptLeave() => _confirm = Confirm.Leave;

    public override void Closed() => _confirm = Confirm.None;

    // ---- the pieces ----

    /// <summary>One CExImageButton: its plate in the default colour or the
    /// armed one under the pointer, the subimage at its .res inset (14x14,
    /// centred on the button's height) and the label west at its text inset.</summary>
    private UiInteraction Button(ref UiFrame ui, Canvas screen, string key, float x, float y, float w, float h,
                                 string? token, string? glyph, float glyphX, float textX, UiColor plate, UiColor armed,
                                 string? tipKey = null, string? tip = null)
    {
        var at = At(screen, x, y, w, h);
        float u = screen.Unit;
        var box = ui.Box().Size(Size.Du(at.Wide), Size.Du(at.Tall))
            .AtRes(UiAnchor.BottomLeft, at.Left, at.Bottom).Layered().Key(key);
        var hit = ui.Interact(box);
        box.Fill(UiPaint.FromGame(hit.Hovered ? armed : plate));
        using (box)
        {
            if (glyph is not null && textures.Find(glyph) is > 0 and var handle)
            {
                float icon = 14f * u;
                ui.Texture(handle, Size.Du(icon), Size.Du(icon))
                    .AtRes(UiAnchor.BottomLeft, glyphX * u, (at.Tall - icon) / 2f)
                    .Tint(UiPaint.FromGame(TanLight)).Layered();
            }
            // West, centred on the button's height (textinsetx, textinsety 2.5).
            if (token is not null)
                ui.Text(token, Text.Heading).Color(UiPaint.FromGame(TanLight)).TextSize(LabelEm(u))
                    .AtRes(UiAnchor.BottomLeft, textX * u, (at.Tall - LabelEm(u)) / 2f).Layered();
        }
        if (tipKey is not null && tip is not null)
            ui.Tooltip(tipKey, box, hit.Hovered, tip);
        return hit;
    }

    /// <summary>An image at a stock box that may overhang the screen: the
    /// visible part, its UVs cut to match.</summary>
    private static void Cropped(ref UiFrame ui, Canvas screen, string key, int handle,
                                float x, float y, float w, float h)
    {
        float left = MathF.Max(0f, x), top = MathF.Max(0f, y);
        float right = MathF.Min(screen.Wide, x + w), bottom = MathF.Min(screen.Tall, y + h);
        if (right <= left || bottom <= top)
            return;
        var uv = new UiRect((left - x) / w, (top - y) / h, (right - left) / w, (bottom - top) / h);
        var at = At(screen, left, top, right - left, bottom - top);
        ui.Texture(handle, Size.Du(at.Wide), Size.Du(at.Tall), uv)
            .AtRes(UiAnchor.BottomLeft, at.Left, at.Bottom).Layered().Key(key);
    }

    /// <summary>A "c" xpos: the box's left edge about the screen's centre.</summary>
    private static float Centre(Canvas screen, float offset) => screen.Wide / 2f + offset;

    /// <summary>
    /// A box written top-down in stock units (x from the left, y from the
    /// top of stock's 480-tall canvas) as du from the screen's bottom-left,
    /// where AtRes places it. The canvas's lower half keeps
    /// its distance from the bottom and the rest from the top, so a screen
    /// taller than 480 units (portrait) opens the gap between them.
    /// </summary>
    private static Spot At(Canvas screen, float x, float y, float w, float h)
    {
        float bottom = y >= Canvas.StockTall / 2f ? Canvas.StockTall - y - h : screen.Tall - y - h;
        float u = screen.Unit;
        return new Spot(x * u, bottom * u, w * u, h * u);
    }

    /// <summary>A box in du: left and bottom from the screen's bottom-left.</summary>
    private readonly record struct Spot(float Left, float Bottom, float Wide, float Tall);

    /// <summary>
    /// The screen in stock units. Wide screens take vgui's proportional unit,
    /// 480 units to the screen's height (2.25 du at 1080), and stock's
    /// centred places. A narrow one (portrait, a raised ui_scale) takes the
    /// unit that fits <see cref="Span"/>, the width stock's rows needed
    /// (the logo at c-290 to the footer's second gear at c298), but never
    /// under <see cref="MinUnit"/>, where the buttons' labels reach the
    /// lint's large-text 24 du (<see cref="LabelEm"/>); what that leaves too
    /// narrow, the logo moves in to the screen's edge (<see cref="LeftShift"/>).
    /// </summary>
    private readonly record struct Canvas(float Unit, float Wide, float Tall)
    {
        public const float StockTall = 480f;

        public const float Span = 600f;

        public const float MinUnit = 1.875f;

        private const float Margin = 4f;

        public float LeftShift => MathF.Max(0f, Margin - (Wide / 2f - 290f));

        public static Canvas Of(ref UiFrame ui)
        {
            float u = ui.DesignHeight / StockTall;
            if (ui.Narrow)
                u = MathF.Max(MinUnit, MathF.Min(u, ui.DesignWidth / Span));
            return new Canvas(u, ui.DesignWidth / u, ui.DesignHeight / u);
        }
    }
}

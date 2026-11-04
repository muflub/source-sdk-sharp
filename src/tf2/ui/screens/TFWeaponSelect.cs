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
/// The weapon selection, stock's <c>CHudWeaponSelection</c>
/// (tf_hud_weaponselection.cpp) as the hud element the slots and the wheel
/// open: a right-edge column, one card per held slot, the selected slot's
/// card large. The layout is the hud's own
/// (<c>ComputeSlotLayout</c>, :416-452 over hudlayout.res's HudWeaponSelection
/// numbers): the column's boxes climb from <c>(480 - total) / 2</c>, each
/// box's right edge <c>Gap</c> in from <c>Wide - Gap - RightMargin</c>, the
/// small card 72x54 and the selected 110x77 with 4 between. Every card is
/// the border's own art nine-sliced (<c>TFFatLineBorder</c> brown, the
/// selection's <c>RedBG</c>/<c>BlueBG</c> by team — clientscheme.res's
/// scalable images, src corner 23 drawn 5); the label sits at the bottom, the
/// slot's number at the top right (<c>SelectionNumberXPos/YPos</c> against
/// the XRES margins, :758-793) in SelectionNumberFg, both at the hud's
/// Alpha 220; and the selected card's weapon that cannot fire names it in
/// red at <c>ErrorYPos</c> (:745-752, <c>#TF_OUT_OF_AMMO</c>). The card's
/// picture is the weapon item's own inventory image
/// (<c>image_inventory</c>); the script's art (<c>GetSpriteInactive</c>, or
/// <c>GetSpriteActive</c> on BLU) stands in where the item names none.
///
/// <para>
/// The hud element stands down for the scoreboard's locked group, as every
/// stock hud element registered "weapon_selection" does
/// (weapon_selection.cpp:101-105) — the <see cref="ITFRenderGroups"/> arm
/// the other TF screens read. The core hud's own selection panel stands
/// down with it: this screen takes <c>hud.weaponselection</c> (Hud.cs's
/// gate), the panel <c>ShouldDraw</c> would have drawn.
/// </para>
/// </summary>
public sealed class TFWeaponSelect(ITFWeaponSelect select, IUiTextureLibrary textures,
                                   IUiFontMetrics fonts, ITFRenderGroups? groups = null) : UiScreen
{
    private readonly string[] _takes = ["hud.weaponselection"];

    public override string Name => "tfweaponselect";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    // Stock's hud elements stand down for the scoreboard's locked "global"
    // group (ITFRenderGroups): the layer skips a screen whose Visible is
    // false, as CHudElement::ShouldDraw yields (hud.cpp:288-303).
    public override bool Visible => base.Visible && groups?.GlobalLocked != true;

    public override IReadOnlyList<string> Takes => _takes;

    // HudLayout.res's HudWeaponSelection numbers, in the 640x480 space the
    // .res files are written in; the frame's own width comes from the screen.
    private const float SmallWide = 72f, SmallTall = 54f, LargeWide = 110f, LargeTall = 77f;
    private const float Gap = 4f, RightMargin = 0f, Tall = 480f;
    private const int MaxSlots = 6;

    // The border art (clientscheme.res): the plain frame and the two teams'.
    private const string Border = "hud/color_panel_brown";
    private const string BorderRed = "hud/color_panel_red";
    private const string BorderBlue = "hud/color_panel_blu";

    // The nine-slice geometry the scheme borders share (src_corner 23, the
    // drawn corner 5 du — TFFatLineBorder's own numbers).
    private const float BorderCorner = 23f, BorderDraw = 5f;

    // The number's colour, SelectionNumberFg (clientscheme.res:429) at the
    // hud's Alpha 220 of 255.
    private static readonly UiPaint Cream = UiPaint.FromGame(new UiColor(251, 235, 202, 220));

    // The name's ink, the item panel label's fgcolor 235 226 202
    // (itemmodelpanel.res, standard_text_color keeps it over the quality
    // tint, item_model_panel.cpp:2676-2683) at the hud's Alpha 220.
    private static readonly UiPaint NameInk = UiPaint.FromGame(new UiColor(235, 226, 202, 220));

    // The out-of-ammo note, Color(255, 0, 0, 255) (tf_hud_weaponselection.cpp:749).
    private static readonly UiPaint EmptyRed = UiPaint.FromGame(new UiColor(255, 0, 0, 255));

    public override void Describe(ref UiFrame ui)
    {
        // The draw arms in stock's order: ShouldDrawInternal's visible arm
        // (the adapter's Showing), then the selection itself — the layout and
        // the draw both bail on a selected-weapon-less frame
        // (:533-534, :664-665).
        if (!select.Showing || select.SelectedSlot < 0)
            return;

        // The du per stock unit, the canvas pinned to the design's top-left
        // and never denser than its own 1080 (TFBuildingStatus's k).
        float k = MathF.Min(1f, ui.DesignHeight / (Tall * StockRes.Scale));
        float u = StockRes.Scale * k;

        float wide = StockRes.Screen(ui.Screen).Wide;
        int visible = 0;
        for (int slot = 0; slot < MaxSlots; slot++)
            if (select.SlotHasWeapon(slot))
                visible++;
        if (visible <= 0)
            return;

        // ComputeSlotLayout's buckets arm (:429-431): one large box among the
        // count, the column centred on 480, the right edge at the margin.
        float total = (visible - 1) * (SmallTall + Gap) + LargeTall;
        float xStart = wide - Gap - RightMargin;
        float top = (Tall - total) / 2f;

        float y = top;
        for (int slot = 0; slot < MaxSlots; slot++)
        {
            bool active = slot == select.SelectedSlot;
            float boxWide = active ? LargeWide : SmallWide;
            float boxTall = active ? LargeTall : SmallTall;
            float x = xStart - (boxWide + Gap);

            if (select.SlotHasWeapon(slot))
                Card(ref ui, slot, x, y, boxWide, boxTall, active, u);

            // Every slot advances the column, the empty ones too, as
            // ComputeSlotLayout's walk does (:434-451).
            y += boxTall + Gap;
        }
    }

    private void Card(ref UiFrame ui, int slot, float x, float y, float wide, float tall, bool active, float u)
    {
        // To du, top-down y to the bottom-up offset AtRes takes.
        float left = x * u;
        float bottom = (Tall - y - tall) * u;

        // The border's own art, nine-sliced: the selection's team colour on
        // the active card, the plain frame on the rest (:567-572, :591). The
        // artless preview keeps a plain plate, as the hud's own panels do.
        string border = !active ? Border
            : select.Team == 3 ? BorderBlue : BorderRed;
        if (StockArt.Missing(textures, border))
            _ = ui.Rect(Tone.Surface, Size.Du(wide * u), Size.Du(tall * u))
                .AtRes(UiAnchor.BottomLeft, left, bottom).Key(slot * 100 + 4);
        _ = StockArt.Scalable(ref ui, textures, "select border", border, left, bottom,
            wide * u, tall * u, BorderCorner * u, BorderDraw * u, slot * 100 + 10);

        // The card's picture, the item panel's: the weapon item's own
        // inventory image (SetItem; the panel's LoadInventoryImage,
        // item_model_panel.cpp:618-628) fitted whole — the ratio kept,
        // centred, as the panel's fit-to-height is (:792-799). The script's
        // sprite, its sub-rect the ratio keeps, stands in when the item
        // names no image or it binds nothing.
        var weapon = select.WeaponInSlot(slot);
        if (!CardArt(ref ui, textures, weapon.ItemImage, 0, 0, 0, 0, wide, tall, left, bottom, u, slot * 100 + 9))
            _ = CardArt(ref ui, textures, weapon.IconFile, weapon.IconX, weapon.IconY,
                weapon.IconWide, weapon.IconTall, wide, tall, left, bottom, u, slot * 100 + 9);

        if (weapon.Name.Length > 0)
        {
            // The card's name, stock's item panel label (itemmodelpanel.res
            // namelabel) through the hud panel's name_only arm: the theme's
            // heading face is the scheme's ItemFontName, TF2 Build, whose
            // lower case is cap-formed, so the localised name reads in
            // capitals as the panel's does. The size is the panel's fit
            // (item_model_panel.cpp:2127-2191) against this card's width,
            // the centre wrap only past the smallest face — never an
            // ellipsis.
            var face = ui.Theme.Type(Text.Heading);
            string shown = ui.Localised(weapon.Name);
            if (face.Upper)
                shown = shown.ToUpperInvariant();
            float tier = NameTier(shown, wide * u, ui.Screen.Scale, face, out bool wrap);
            var name = ui.Text(weapon.Name, Text.Heading, Tone.Text)
                .Color(NameInk).TextSize(tier).Layered()
                .Width(Size.Du(wide * u))
                .TextAlign(Align.Centre)
                .AtRes(UiAnchor.BottomLeft, left, bottom + 2f * u)
                .Key(slot * 100 + 1);
            if (wrap)
                _ = name.Wrap(wide * u);
        }

        // The number, top right: the active card's inset from the box's right
        // edge by XRES(5) and SelectionNumberXPos, a screen-height YRES(5)
        // and SelectionNumberYPos down (:764-766); the plain cards', from the
        // left, by XRES(10) more (:784-793). The glyph is the slot, '0'+i+1.
        float xres5 = 5f * wide / 640f, xres10 = 2f * xres5;
        float numberX = active
            ? x + wide - xres5 - 12f
            : x + xres5 + SmallWide - xres10 - 12f;
        var number = ui.Number(slot + 1, Text.Number, Tone.Text);
        _ = number.Color(Cream).TextSize(TFHudType.HudSelectionNumber).Layered()
            .Width(Size.Du(13f * u)).Height(Size.Du(16f * u))
            .AtRes(UiAnchor.BottomLeft, numberX * u, (Tall - y - 5f - 4f - 16f) * u)
            .Key(slot * 100 + 2);

        if (active && !weapon.CanSelect)
        {
            // The note, centred on the card at ErrorYPos down (:747-751).
            _ = ui.Text("#TF_OUT_OF_AMMO", Text.Caption, Tone.Text)
                .Color(EmptyRed).HudSize().Layered()
                .Width(Size.Du(wide * u)).Height(Size.Du(12f * u))
                .TextAlign(Align.Centre).Elide()
                .AtRes(UiAnchor.BottomLeft, left, (Tall - y - 48f - 12f) * u)
                .Key(slot * 100 + 3);
        }
    }

    // One card picture: the named texture's <paramref name="src"/> rect —
    // the whole texture where the rect is zero — fitted into the card's
    // inner box, the ratio kept and centred; false when the name is empty,
    // the rect empty or the texture binds nothing (the caller's next art
    // stands).
    private static bool CardArt(ref UiFrame ui, IUiTextureLibrary textures, string file,
                                int srcX, int srcY, int srcWide, int srcTall,
                                float wide, float tall, float left, float bottom, float u, int key)
    {
        if (file.Length == 0 || textures.About(file) is not { } info || info.Wide <= 0 || info.Tall <= 0)
            return false;
        int sx = srcWide > 0 ? srcX : 0, sy = srcTall > 0 ? srcY : 0;
        int sw = srcWide > 0 ? srcWide : info.Wide, sh = srcTall > 0 ? srcTall : info.Tall;

        float drawWide = (wide - 4f) * u, drawTall = (tall - 4f) * u;
        float leftEdge = left + 2f * u, bottomEdge = bottom + 2f * u;
        float u0 = sx / info.Wide, v0 = sy / info.Tall;
        float uw = (float)sw / info.Wide, vh = (float)sh / info.Tall;
        float fit = MathF.Min(drawWide / (sw * u), drawTall / (sh * u));
        float artWide = sw * fit * u, artTall = sh * fit * u;
        return StockArt.Draw(ref ui, textures, "", file,
            leftEdge + (drawWide - artWide) / 2f,
            bottomEdge + (drawTall - artTall) / 2f,
            artWide, artTall,
            uv: new UiRect(u0, v0, uw, vh), keyNumber: key);
    }

    // The label's tier, the panel's fit chain against the card's width in
    // the theme's own face: the name measured once at four times the large
    // tier (the metrics scale with the size), each face held against the px
    // the layout rounds that tier to — a half-pixel round is a wider face
    // than the tier's du. True wrap when even the smallest cannot hold it.
    private const float ItemNameProbe = 4f * TFHudType.ItemNameLarge;   // 108

    private float NameTier(string name, float budgetDu, float scale, in UiTypeStyle face, out bool wrap)
    {
        var probe = new UiFontSpec(face.Family, (int)ItemNameProbe, face.Weight, face.Flags);
        float wide = 0f;
        foreach (char c in name)
            wide += fonts.Advance(probe, c);

        bool fits(float tier)
        {
            float px = MathF.Round(tier * scale);
            return wide * px / ItemNameProbe <= budgetDu * scale;
        }

        wrap = false;
        if (fits(TFHudType.ItemNameLarge))
            return TFHudType.ItemNameLarge;
        if (fits(TFHudType.ItemNameSmall))
            return TFHudType.ItemNameSmall;
        if (!fits(TFHudType.ItemNameSmallest))
            wrap = true;
        return TFHudType.ItemNameSmallest;
    }
}
